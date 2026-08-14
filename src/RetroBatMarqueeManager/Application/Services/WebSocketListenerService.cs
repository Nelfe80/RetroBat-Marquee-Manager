using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;
using OverrideSource = RetroBatMarqueeManager.Application.Media.PresentationOverrides.Source;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class WebSocketListenerService : BackgroundService
{
    private static readonly string[] Streams =
    {
        "arcade", "frontend", "marquee", "topper", "instruction-card", "panel", "hiscore",
        "retroachievements", "score", "timer", "ingame"
    };

    // Streams whose messages describe the CURRENT state (snapshots): during fast
    // ES navigation only the most recent one matters — replaying the backlog one
    // by one is what made the marquee lag tens of seconds behind the frontend.
    // Event-like streams (hiscore, retroachievements, ingame…) keep strict FIFO.
    private static readonly HashSet<string> StateStreams = new(StringComparer.OrdinalIgnoreCase)
    {
        "marquee", "topper", "instruction-card", "panel", "frontend"
    };

    private readonly Dictionary<string, Channel<JsonDocument>> _mailboxes = new(StringComparer.OrdinalIgnoreCase);

    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly IDmdService _dmd;
    private readonly LayManager _lay;
    private readonly SurfacePresentationService _presentation;
    private readonly InstructionCardService _instructionCards;
    private readonly ILogger<WebSocketListenerService> _logger;
    private string? _selectedSystem;
    private string? _selectedRom;
    private string? _runningRom;
    // The cabinet's panel description, kept so the stick colour published with a GAME
    // can be folded into it without waiting for the cabinet to be described again.
    private Core.Surfaces.PanelBoardConfig? _panelConfig;
    private string _panelStickColor = string.Empty;
    private bool _panelInputSeen;
    // Deferred ingame effects (sequenced actions) belong to the current play
    // session: a sprite scheduled 1.5 s out must NOT fire after the game ended or
    // another game started (§5 "Effets différés").
    private CancellationTokenSource _effectSessionCts = new();
    private bool _pinballDmdActive;
    private readonly Application.Lighting.IngameEffectLibrary _ingameEffects;
    private readonly Application.Lighting.GenreMap _genreMap;
    private readonly string _effectOverridesRoot;
    private readonly Application.Media.CompositionChainResolver _compositionChains;
    private readonly Application.Media.PresentationOverrides _overrides;
    private readonly Application.Media.CompositionTemplateRenderer _templateRenderer;

    public WebSocketListenerService(
        IConfigService config,
        MarqueeController surfaces,
        IDmdService dmd,
        LayManager lay,
        SurfacePresentationService presentation,
        InstructionCardService instructionCards,
        ILogger<WebSocketListenerService> logger)
    {
        _config = config;
        _surfaces = surfaces;
        _dmd = dmd;
        _lay = lay;
        _presentation = presentation;
        _instructionCards = instructionCards;
        _logger = logger;
        _ingameEffects = Application.Lighting.IngameEffectLibrary.Load(
            Path.Combine(config.BaseDirectory, "resources", "lighting"), logger);
        _genreMap = Application.Lighting.GenreMap.Load(
            Path.Combine(config.BaseDirectory, "resources", "lighting"), logger);
        _effectOverridesRoot = Path.Combine(config.BaseDirectory, "overrides", "effects");
        _compositionChains = new Application.Media.CompositionChainResolver(
            config.BaseDirectory, logger, config.LightingPreferGeneratedMarquee);
        _overrides = new Application.Media.PresentationOverrides(config.BaseDirectory, logger);
        _templateRenderer = new Application.Media.CompositionTemplateRenderer(config.BaseDirectory, logger);
        _dynamicRenderer = new Application.Media.DynamicSurfaceRenderer(config.BaseDirectory, logger);
        _gabaritRenderer = new Application.Media.GabaritSkiaRenderer(config.BaseDirectory, logger);
        _compositionChains.TemplateMissing = OnTemplateMissing;
        _compositionChains.GabaritMissing = OnGabaritMissing;
    }

    private readonly Application.Media.GabaritSkiaRenderer _gabaritRenderer;

    /// <summary>
    /// A gabarit is not baked yet (game scope when rom is set, system scope otherwise):
    /// render it here, in the background, then re-display if the selection has not moved
    /// on. The Setup no longer has to have opened that sheet for the template to apply.
    /// </summary>
    /// <summary>
    /// Changing entry drops EVERYTHING remembered from the previous one. A cached
    /// media path that outlives its game is how one fanart, or one instruction card,
    /// ends up following the whole library: a render fired by another stream picks up
    /// whatever was left behind. No stale value, no fallback — nothing rather than the
    /// neighbour's.
    /// </summary>
    private void ForgetPreviousEntry()
    {
        lock (_lastMarqueeKinds) _lastMarqueeKinds.Clear();
        lock (_lastKindsByCategory) _lastKindsByCategory.Clear();

        // The instruction-card stream stays SILENT for a game that has none, so waiting
        // for a message to clear meant never clearing: one card followed the user across
        // the whole library. The selection change is the only reliable signal.
        _ = _instructionCards.SetCardsAsync(Array.Empty<string>(), CancellationToken.None);
    }

    /// <summary>Last three path segments — enough to name the GAME, which the file name
    /// alone never does (every game has an artwork/fanart.jpg).</summary>
    private static string TailOf(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Join('/', parts.Skip(Math.Max(0, parts.Length - 3)));
    }

    private void OnGabaritMissing(string surfaceId, string category, string system, string? rom)
    {
        var (width, height) = _surfaces.SurfacePixelSize(surfaceId);
        if (width <= 0 || height <= 0) return;

        // ES calls a MAME set "mame" while the gabarit may have been saved under
        // "arcade" (or the reverse): both spellings name the same template.
        string? scope = null;
        if (string.IsNullOrEmpty(rom))
        {
            scope = Application.Media.GabaritSkiaRenderer.SystemScope;
            if (!_gabaritRenderer.HasGabarit(category, surfaceId, scope)) return;
        }
        else
        {
            foreach (var spelling in Application.Media.CompositionChainResolver.SystemNames(system))
            {
                var candidate = Application.Media.GabaritSkiaRenderer.GameScopeFor(spelling);
                if (!_gabaritRenderer.HasGabarit(category, surfaceId, candidate)) continue;
                scope = candidate;
                break;
            }
            if (scope == null) return;
        }

        var output = _compositionChains.GabaritCachePath(category, surfaceId, system, rom);
        var systemScope = string.IsNullOrEmpty(rom);

        // Capture the media of the CURRENT snapshot right now. The render runs in the
        // background, and reading _lastMarqueeKinds when it completes would use whatever
        // the user has browsed to since — that is how every system's template ended up
        // wearing the Mega Drive fanart. The snapshot is also the ONLY media source:
        // APIExpose serves it, MarqueeManager never goes looking in its folders.
        var kinds = KindsFor(category);

        // Names the file each layer actually took. Without it, "I see the same fanart on
        // every game" can only be argued about; with it, the log answers in one line.
        var trace = new List<string>();
        string? Resolve(MarqueeManager.Compositions.Core.Composition.MarqueeLayer layer)
        {
            var resolved = ResolveGabaritLayerMedia(layer, kinds, system);
            var key = string.IsNullOrWhiteSpace(layer.AssetKey) ? "(no key)" : layer.AssetKey;
            trace.Add($"{key}={(resolved == null ? "—" : TailOf(resolved))}");
            return resolved;
        }

        var meta = _lastMarqueeMeta;
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = meta?.GameName ?? rom ?? system,
            ["year"] = meta?.Year?.ToString() ?? "",
            ["developer"] = meta?.Developer ?? "",
            ["publisher"] = meta?.Publisher ?? "",
            ["system"] = system,
        };

        // Everything the entry's text block carries becomes a token: {desc}, {genre},
        // {players}, {rating}… The fields already known from the lighting meta stay
        // authoritative — they come from the selection itself, not from a scrape.
        foreach (var (field, value) in CurrentText())
            if (!tokens.TryGetValue(field, out var existing) || string.IsNullOrEmpty(existing))
                tokens[field] = value;

        _gabaritRenderer.RenderInBackground(category, surfaceId, scope, rom ?? system,
            width, height, Resolve, tokens, output, path =>
            {
                _logger.LogInformation("Gabarit layers for {Label}: {Trace}", rom ?? system, string.Join(" · ", trace));
                var current = _lastMarqueeMeta;
                if (!systemScope && !string.Equals(current?.Rom, rom, StringComparison.OrdinalIgnoreCase)) return;
                // a per-surface creation still outranks the template: never stomp it
                if (_compositionChains.SurfaceCreation(category, surfaceId, current, systemScope) != null) return;
                _ = _surfaces.DisplayMediaAsync(path, surfaceId, CancellationToken.None, current, resolved: true);
            });
    }

    /// <summary>
    /// Media of one gabarit layer. The APIExpose media folders are SLUGS, not rom names,
    /// so nothing is guessed from the file system: the layer's AssetKey resolves against
    /// the paths the snapshot already gave us. An absolute source (a downloaded image, a
    /// decoration) is used as-is.
    /// </summary>
    private static string? ResolveGabaritLayerMedia(MarqueeManager.Compositions.Core.Composition.MarqueeLayer layer,
        IReadOnlyDictionary<string, string?> kinds, string system)
    {
        // A background carries no AssetKey, only the path picked while composing:
        // infer the key from it, or the template stays bound to that one entry.
        var key = string.IsNullOrWhiteSpace(layer.AssetKey)
            ? MarqueeManager.Compositions.Core.Composition.GabaritAssets.KeyFromPath(layer.Source)
            : layer.AssetKey;

        // SYSTEM-scoped layers, and ONLY those, follow the system by swapping the
        // \systems\<sys>\ segment. Applying that to any path also matched a GAME asset —
        // whose game folder never changes — so a template composed on Sonic served
        // Sonic's art to every game of the system.
        if (key is "systemfanart" or "systemwheel" or "systemmarquee")
        {
            // the snapshot now carries the system's own table, kept apart from the
            // game's: ask it before deriving anything from a neighbouring path
            var declared = key switch
            {
                "systemfanart" => Lookup(kinds, "system:fanart"),
                "systemwheel" => Lookup(kinds, "system:wheel", "system:logo"),
                _ => Lookup(kinds, "system:marquee", "system:generated-marquee")
            };
            return declared
                   ?? SwapSystemSegment(layer.Source, system)
                   ?? SystemAssetBeside(kinds, system, key);
        }

        if (key == null)
        {
            // no key inferable: an ES theme background is stored per system as
            // …/art/background/<system>.jpg and must follow; otherwise it is a genuine
            // one-off (imported image, decoration) and keeps its own file.
            var themed = ResolveBySystemName(layer.Source, system);
            if (themed != null) return themed;
            return layer.Source is { Length: > 0 } source && Path.IsPathRooted(source) && File.Exists(source)
                ? source
                : null;
        }

        // Palette key -> the kinds the snapshot may carry it under. Canonical MediaKinds
        // first (what APIExpose publishes in its asset tables), then the legacy field
        // name for a snapshot that predates them. The old mapping guessed names the
        // stream never used — "box", "screenshot", "mix" — so those layers resolved to
        // nothing however well the file existed on disk.
        var candidates = key.ToLowerInvariant() switch
        {
            "fanart" => new[] { "fanart" },
            "wheel" => new[] { "wheel", "logo" },
            "marquee" => new[] { "marquee" },
            "screenmarquee" => new[] { "screen-marquee", "screenmarquee" },
            "generated" => new[] { "generated-marquee", "generated" },
            "generateddmd" => new[] { "generated-dmd", "dmd-generated" },
            "mix" => new[] { "mixrbv2", "mixrbv1" },
            "boxfront" => new[] { "box-front" },
            "box3d" => new[] { "box-3d" },
            "screenshot" => new[] { "thumbnail" },
            "screentitle" => new[] { "image" },
            "flyer" => new[] { "flyer" },
            "bezel" => new[] { "bezel" },
            "video" => new[] { "video" },
            _ => null
        };
        // A key that is not a GAME media kind — "gradient", or anything the composer
        // labelled itself — names a fixed decoration, identical for every entry: it
        // keeps its own file. Returning null here silently dropped the readability
        // gradient of every template that had one.
        if (candidates == null)
            return layer.Source is { Length: > 0 } fixedAsset && Path.IsPathRooted(fixedAsset) && File.Exists(fixedAsset)
                ? fixedAsset
                : null;

        return Lookup(kinds, candidates);
    }

    /// <summary>First of these kinds the snapshot actually carries.</summary>
    private static string? Lookup(IReadOnlyDictionary<string, string?> kinds, params string[] names)
    {
        foreach (var name in names)
            if (kinds.TryGetValue(name, out var path) && path is { Length: > 0 })
                return path;
        return null;
    }

    /// <summary>
    /// A SYSTEM asset lives under …\systems\&lt;sys&gt;\… : swapping that one segment makes it
    /// follow the system being rendered, so a Neo Geo logo placed in a template becomes
    /// the Mega Drive logo on a Mega Drive game. Existence-checked, and reserved to the
    /// system-scoped keys — a game asset's folder never changes, so swapping there
    /// served the sample game's art to the whole system.
    /// </summary>
    /// <summary>
    /// The system's own art, for a layer that carries no composed path — a template
    /// authored on TYPES has none, which is why a system logo placed that way drew
    /// nothing. It is derived from a path the SNAPSHOT already gave us (the entry's own
    /// media, which lives under …\systems\&lt;sys&gt;\…): the system segment is swapped and
    /// the known tail appended, then checked. Nothing is hunted for on disk — APIExpose
    /// does not publish system art inside a game payload, so this is the only thing the
    /// stream leaves to work with.
    /// </summary>
    private static string? SystemAssetBeside(IReadOnlyDictionary<string, string?> kinds, string system, string key)
    {
        var tails = key switch
        {
            "systemfanart" => new[]
            {
                Path.Combine("artwork", "fanart.jpg"),
                Path.Combine("artwork", "fanart.png")
            },
            "systemwheel" => new[] { Path.Combine("ui", "wheels", "wheel.png") },
            _ => new[]
            {
                Path.Combine("artwork", "marquee", "generated-system-marquee.png"),
                Path.Combine("artwork", "marquee", "marquee.png")
            }
        };

        foreach (var known in kinds.Values)
        {
            if (known is not { Length: > 0 } path || !Path.IsPathRooted(path)) continue;
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var systemsAt = Array.FindLastIndex(parts, p => p.Equals("systems", StringComparison.OrdinalIgnoreCase));
            if (systemsAt < 0 || systemsAt + 1 >= parts.Length) continue;
            var root = string.Join(Path.DirectorySeparatorChar, parts.Take(systemsAt + 1));

            foreach (var name in Application.Media.CompositionChainResolver.SystemNames(system))
                foreach (var tail in tails)
                {
                    var candidate = Path.Combine(root, name, tail);
                    if (File.Exists(candidate)) return candidate;
                }
        }
        return null;
    }

    private static string? SwapSystemSegment(string? source, string system)
    {
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathRooted(source)) return null;
        var parts = source!.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var systemsAt = Array.FindLastIndex(parts, p => p.Equals("systems", StringComparison.OrdinalIgnoreCase));
        if (systemsAt < 0 || systemsAt + 1 >= parts.Length) return File.Exists(source) ? source : null;

        foreach (var name in Application.Media.CompositionChainResolver.SystemNames(system))
        {
            var swapped = (string[])parts.Clone();
            swapped[systemsAt + 1] = name;
            var candidate = string.Join(Path.DirectorySeparatorChar, swapped);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// A stored path whose FILE NAME is a system name (the ES theme's
    /// art\background\arcade.jpg) re-points to the current system. Returns null unless
    /// the swapped file really exists — a template never borrows another system's image.
    /// </summary>
    private static string? ResolveBySystemName(string? source, string system)
    {
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathRooted(source)) return null;

        var directory = Path.GetDirectoryName(source);
        if (directory == null) return null;

        var extension = Path.GetExtension(source);
        foreach (var name in Application.Media.CompositionChainResolver.SystemNames(system))
        {
            foreach (var candidateExtension in new[] { extension, ".jpg", ".png" })
            {
                var candidate = Path.Combine(directory, name + candidateExtension);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private readonly Application.Media.DynamicSurfaceRenderer _dynamicRenderer;

    /// <summary>Current display state, mirrored from the scene broadcasts: it selects
    /// which dynamic render a surface shows, and ingame it is the ONLY source.</summary>
    private volatile string _displayScene = "navigation";

    /// <summary>
    /// The surface's own layer stack, flattened and cached (docs\RENDU-DYNAMIQUE.md).
    /// Null while it renders or when the surface has nothing to flatten — the caller
    /// then keeps the media it already had, so nothing ever blinks.
    /// </summary>
    private string? ResolveDynamicSurface(string category, string target,
        Application.Lighting.LightingSceneMeta? meta, bool systemScope)
    {
        var path = ResolveDynamicSurface(category, target, meta, systemScope, _displayScene, display: true);

        // Warm-up: while browsing, also render the INGAME variant of the selected game.
        // Ingame the dynamic render is the only law, so it must be ready the instant the
        // game starts — and the user always looks at a game's card before launching it.
        // One extra composite, off-thread, deduplicated, on exactly the right game (the
        // reason a "pre-generate everything" batch is the wrong tool here).
        if (_displayScene.Equals("navigation", StringComparison.OrdinalIgnoreCase))
            ResolveDynamicSurface(category, target, meta, systemScope, "ingame", display: false);

        return path;
    }

    private string? ResolveDynamicSurface(string category, string target,
        Application.Lighting.LightingSceneMeta? meta, bool systemScope, string scene, bool display)
    {
        var system = meta?.System;
        if (string.IsNullOrEmpty(system)) return null;

        var surface = _config.GetSurfaces().FirstOrDefault(s =>
            s.Id.Equals(target, StringComparison.OrdinalIgnoreCase));
        if (surface == null) return null;

        var run = Application.Media.DynamicSurfaceRenderer.FlattenableRun(surface, scene);
        if (run.Count == 0) return null; // no composed stack under a lighting engine

        var (width, height) = _surfaces.SurfacePixelSize(target);
        if (width <= 0 || height <= 0) return null;

        // Snapshot the media NOW. The render runs in the background and the cache key is
        // computed here: reading _lastMarqueeKinds later would mix the key of one entry
        // with the pixels of whatever has been browsed since — a wrong image, cached,
        // and served as if it were right.
        Dictionary<string, string?> kinds;
        lock (_lastMarqueeKinds) kinds = new Dictionary<string, string?>(_lastMarqueeKinds, StringComparer.OrdinalIgnoreCase);

        string? ResolveLayerMedia(Core.Surfaces.ComponentDefinition component)
        {
            var kind = component.Type.ToLowerInvariant() switch
            {
                "media.fanart" => "fanart",
                "media.logo" => "logo",
                "media.image" => component.Option("kind", "screenmarquee"),
                _ => null
            };
            return kind != null && kinds.TryGetValue(kind, out var path) ? path : null;
        }

        var output = _dynamicRenderer.CachePath(category, target, system!, meta?.Rom, scene, systemScope);
        var key = Application.Media.DynamicSurfaceRenderer.CacheKey(run, width, height, scene, ResolveLayerMedia);
        if (_dynamicRenderer.IsFresh(output, key)) return output;

        // stale or absent: render in the background, then re-display if the selection
        // has not moved on (same "pending → updated" pattern as the templates)
        var rom = meta?.Rom;
        _dynamicRenderer.RenderInBackground(run, width, height, scene, ResolveLayerMedia, output, path =>
        {
            if (!display) return; // warm-up job: render and cache, never take the screen
            var current = _lastMarqueeMeta;
            if (!string.Equals(current?.Rom, rom, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(_displayScene, scene, StringComparison.OrdinalIgnoreCase)) return;
            _surfaces.SetDynamicRenderActive(target, true);
            _ = _surfaces.DisplayMediaAsync(path, target, CancellationToken.None, current, resolved: true);
        });
        return null;
    }

    private readonly Dictionary<string, string?> _lastMarqueeKinds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Media of the last snapshot OF EACH CATEGORY. A gabarit must be rendered with the
    /// media of the stream that asked for it: the topper carries its own Topper/Fanart/
    /// Logo, and reading the marquee's instead rendered a topper with whatever game the
    /// marquee stream had last described — one game's fanart spreading over all the
    /// others, exactly as observed.
    /// </summary>
    /// <summary>Media of the last snapshot of each stream, STAMPED with the entry it
    /// describes. Each stream fills its own table at its own pace: merging them blind
    /// served the previous game's screenshot to the next one, because the topper
    /// snapshot for the new entry had simply not arrived yet.</summary>
    private readonly Dictionary<string, (string? Rom, Dictionary<string, string?> Kinds)> _lastKindsByCategory =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The entry's text fields, as published on the streams that print something about a
    /// game. Stamped with the entry, for the same reason the media tables are: a stream
    /// that has not caught up still describes the previous game.
    /// </summary>
    private (string? Rom, Dictionary<string, string> Fields) _lastText = (null, new(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// What the buttons DO, as published on the instruction-card stream: per button its
    /// function and its colour, the devices, and the game's note. Kept so a composed
    /// card can draw a panel without a second subscription.
    /// </summary>
    private (string? Rom, ControlsSnapshot? Controls) _lastControls = (null, null);

    public sealed record ControlsSnapshot(
        int? Players,
        bool Alternating,
        string Notes,
        IReadOnlyList<ControlsButton> Buttons,
        IReadOnlyList<ControlsDevice> Devices);

    public sealed record ControlsButton(string Player, string Id, string Function, string Color);
    public sealed record ControlsDevice(string Player, string Label, string Type, string Color);

    private void ReadControlsBlock(JsonElement payload, string? rom)
    {
        var block = Child(payload, "Controls", "controls");
        if (block.ValueKind != JsonValueKind.Object) return;

        var buttons = new List<ControlsButton>();
        var devices = new List<ControlsDevice>();
        var buttonList = Child(block, "Buttons", "buttons");
        if (buttonList.ValueKind == JsonValueKind.Array)
            foreach (var button in buttonList.EnumerateArray())
                buttons.Add(new ControlsButton(
                    Text(button, "Player", "player"), Text(button, "Id", "id"),
                    Text(button, "Function", "function"), Text(button, "Color", "color")));

        var deviceList = Child(block, "Devices", "devices");
        if (deviceList.ValueKind == JsonValueKind.Array)
            foreach (var device in deviceList.EnumerateArray())
                devices.Add(new ControlsDevice(
                    Text(device, "Player", "player"), Text(device, "Label", "label"),
                    Text(device, "Type", "type"), Text(device, "Color", "color")));

        if (buttons.Count == 0 && devices.Count == 0) return;

        var players = Child(block, "Players", "players");
        _lastControls = (rom, new ControlsSnapshot(
            players.ValueKind == JsonValueKind.Number ? players.GetInt32() : null,
            Child(block, "Alternating", "alternating").ValueKind == JsonValueKind.True,
            Text(block, "Notes", "notes"),
            buttons,
            devices));
    }

    /// <summary>Controls of the CURRENT entry — null when what we hold describes another.</summary>
    public ControlsSnapshot? CurrentControls()
    {
        var (rom, controls) = _lastControls;
        var current = _lastMarqueeMeta?.Rom;
        return current is null || rom is null || rom.Equals(current, StringComparison.OrdinalIgnoreCase)
            ? controls
            : null;
    }

    private void ReadTextBlock(JsonElement payload, string? rom)
    {
        var block = Child(payload, "Text", "text");
        if (block.ValueKind != JsonValueKind.Object) return;
        var fields = Child(block, "Fields", "fields");
        if (fields.ValueKind != JsonValueKind.Object) return;

        var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.EnumerateObject())
            if (field.Value.ValueKind == JsonValueKind.String && field.Value.GetString() is { Length: > 0 } value)
                read[field.Name] = value;

        if (read.Count > 0) _lastText = (rom, read);
    }

    /// <summary>Text of the CURRENT entry — empty when what we hold describes another.</summary>
    private IReadOnlyDictionary<string, string> CurrentText()
    {
        var (rom, fields) = _lastText;
        var current = _lastMarqueeMeta?.Rom;
        return current is null || rom is null || rom.Equals(current, StringComparison.OrdinalIgnoreCase)
            ? fields
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private void RememberKinds(string category, string? rom, Dictionary<string, string?> kinds)
    {
        lock (_lastKindsByCategory) _lastKindsByCategory[category] = (rom, kinds);
    }

    /// <summary>
    /// Every medium known about the CURRENT entry, the asking category winning where
    /// both describe the same kind. Partitioning strictly by category starved the topper
    /// gabarit: its stream carries no fanart, so a template built on one rendered
    /// nothing at all. The leak to avoid was never cross-stream — it was cross-ENTRY,
    /// and that is closed by clearing everything when the selection changes.
    /// </summary>
    private Dictionary<string, string?> KindsFor(string category)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        lock (_lastKindsByCategory)
        {
            // Only tables that describe the entry on screen. A stream that has not
            // caught up yet still holds the previous game, and merging it blind is how
            // 19xx ended up wearing the screenshot of 1945kiii.
            var current = _lastMarqueeMeta?.Rom;
            bool Describes(string? rom)
                => current is null || rom is null || rom.Equals(current, StringComparison.OrdinalIgnoreCase);

            foreach (var (name, entry) in _lastKindsByCategory)
            {
                if (name.Equals(category, StringComparison.OrdinalIgnoreCase) || !Describes(entry.Rom)) continue;
                foreach (var (kind, path) in entry.Kinds)
                    if (path is { Length: > 0 }) merged[kind] = path;
            }
            if (_lastKindsByCategory.TryGetValue(category, out var own) && Describes(own.Rom))
                foreach (var (kind, path) in own.Kinds)
                    if (path is { Length: > 0 }) merged[kind] = path;
        }
        lock (_lastMarqueeKinds)
        {
            foreach (var (kind, path) in _lastMarqueeKinds)
                if (path is { Length: > 0 } && !merged.ContainsKey(kind)) merged[kind] = path;
        }
        return merged;
    }
    private Application.Lighting.LightingSceneMeta? _lastMarqueeMeta;

    /// <summary>A chain asked for a template PNG not yet cached: render it in the
    /// background, then re-display if the selection did not move on (the
    /// "pending → updated" pattern of APIExpose's own generation).</summary>
    private void OnTemplateMissing(string templateId, string system, string rom, bool systemScope)
    {
        string? fanart, logo;
        lock (_lastMarqueeKinds)
        {
            _lastMarqueeKinds.TryGetValue("fanart", out fanart);
            _lastMarqueeKinds.TryGetValue("logo", out logo);
        }
        _templateRenderer.RenderInBackground("marquee", templateId, system, rom, fanart, logo, path =>
        {
            var meta = _lastMarqueeMeta;
            if (meta?.Rom == null || !meta.Rom.Equals(rom, StringComparison.OrdinalIgnoreCase)) return;
            foreach (var target in _config.GetTargetsForContent("marquee"))
            {
                // never stomp a surface that displays its own graphic creation
                if (_compositionChains.SurfaceCreation("marquee", target, meta, systemScope) != null) continue;
                _ = _surfaces.DisplayMediaAsync(path, target, CancellationToken.None, meta, resolved: true);
            }
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // rollback switch: [Settings] CoalesceStateStreams=false restores the
        // historical inline processing (strict FIFO on every stream)
        var coalesce = !_config.GetValue("Settings", "CoalesceStateStreams", "true")
            .Equals("false", StringComparison.OrdinalIgnoreCase);
        var tasks = new List<Task>();
        foreach (var stream in Streams)
        {
            if (coalesce && StateStreams.Contains(stream))
            {
                var mailbox = Channel.CreateUnbounded<JsonDocument>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
                _mailboxes[stream] = mailbox;
                tasks.Add(DrainLatestAsync(stream, mailbox.Reader, stoppingToken));
            }
            tasks.Add(ListenAsync(stream, stoppingToken));
        }
        return Task.WhenAll(tasks);
    }

    private async Task ListenAsync(string stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                var uri = new Uri($"{_config.ApiExposeWebSocketBaseUrl}/ws/{stream}");
                await socket.ConnectAsync(uri, cancellationToken);
                _logger.LogInformation("Connected to APIExpose {Stream} stream", stream);
                // A release that happened while we were disconnected will never arrive:
                // start from a dark panel rather than from a button we only THINK is held.
                if (stream.Equals("panel", StringComparison.OrdinalIgnoreCase)) _surfaces.ReleasePanelInputs();
                await ReceiveAsync(socket, stream, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("APIExpose {Stream} stream disconnected: {Message}; retrying in 5 seconds", stream, ex.Message);
                try { await Task.Delay(5000, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, string stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
                message.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            try
            {
                if (_mailboxes.TryGetValue(stream, out var mailbox))
                {
                    // never block the socket drain on processing: hand the
                    // snapshot to the stream worker (which owns its disposal)
                    mailbox.Writer.TryWrite(JsonDocument.Parse(message.ToArray()));
                    continue;
                }
                using var document = JsonDocument.Parse(message.ToArray());
                await ProcessAsync(stream, document.RootElement, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON received on {Stream}: {Message}", stream, ex.Message);
            }
        }
    }

    /// <summary>
    /// Worker for a state stream: drains everything already received and only
    /// processes the most recent snapshot — older ones describe selections the
    /// user has already scrolled past. On the frontend stream, lifecycle events
    /// (game started/ended) are never skipped; only `*.selected*` messages
    /// coalesce between themselves, in arrival order.
    /// </summary>
    private async Task DrainLatestAsync(string stream, ChannelReader<JsonDocument> reader, CancellationToken cancellationToken)
    {
        var batch = new List<JsonDocument>();
        try
        {
            while (await reader.WaitToReadAsync(cancellationToken))
            {
                batch.Clear();
                while (reader.TryRead(out var pending)) batch.Add(pending);

                // Coalescing is per SUBJECT, not per stream: two messages only supersede
                // each other when they describe the same thing. Dropping everything but
                // the last message of a stream lost the cabinet's panel description
                // whenever the game's panel state followed it in the same batch — two
                // different subjects, one of them silently gone.
                var newest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var keys = new string?[batch.Count];
                for (var i = 0; i < batch.Count; i++)
                {
                    keys[i] = CoalescingKey(stream, batch[i]);
                    if (keys[i] is { } key) newest[key] = i;
                }

                var skipped = 0;
                for (var i = 0; i < batch.Count; i++)
                {
                    var document = batch[i];
                    try
                    {
                        if (keys[i] is { } key && newest[key] != i)
                        {
                            skipped++;
                            continue;
                        }
                        await ProcessAsync(stream, document.RootElement, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Processing {Stream} message failed", stream);
                    }
                    finally
                    {
                        document.Dispose();
                    }
                }
                if (skipped > 0)
                    _logger.LogDebug("{Stream}: coalesced {Count} stale snapshot(s)", stream, skipped);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            while (reader.TryRead(out var leftover)) leftover.Dispose();
        }
    }

    /// <summary>
    /// What this message would supersede: two messages sharing a key describe the same
    /// subject, so only the newest is worth processing. A null key means "never skip" —
    /// the message is an EVENT, not a state, and skipping it loses something that
    /// happened.
    ///
    /// frontend: every `*.selected*` shares one key (they are one subject, the current
    /// selection); its lifecycle events (game started/ended) are never skipped.
    /// panel: the presses are events — a press and its release arrive milliseconds
    /// apart, and coalescing them dropped the press and kept the release, so the button
    /// never lit and nothing looked broken.
    /// </summary>
    private static string? CoalescingKey(string stream, JsonDocument document)
    {
        var type = Text(document.RootElement, "Type", "type");
        if (stream.Equals("frontend", StringComparison.OrdinalIgnoreCase))
            return type.Contains(".selected", StringComparison.OrdinalIgnoreCase) ? "selection" : null;
        if (stream.Equals("panel", StringComparison.OrdinalIgnoreCase))
            return type.StartsWith("panel.input.", StringComparison.OrdinalIgnoreCase) ? null : type;
        return type.Length > 0 ? type : stream;
    }

    private async Task ProcessAsync(string stream, JsonElement root, CancellationToken cancellationToken)
    {
        switch (stream)
        {
            case "retroachievements":
                await _presentation.HandleRetroAchievementsAsync(root, cancellationToken);
                return;
            case "score":
                await _presentation.HandleScoreAsync(root, cancellationToken);
                return;
            case "timer":
                await _presentation.HandleTimerAsync(root, cancellationToken);
                return;
            case "arcade":
                await HandleArcadeAsync(root, cancellationToken);
                return;
            case "ingame":
                HandleIngame(root);
                return;
            case "frontend":
                await HandleFrontendAsync(root, cancellationToken);
                return;
            case "marquee":
                await HandleMarqueeAsync(root, cancellationToken);
                return;
            case "topper":
                await HandleTopperAsync(root, cancellationToken);
                return;
            case "instruction-card":
                await HandleInstructionCardAsync(root, cancellationToken);
                return;
            case "panel":
                await HandlePanelAsync(root, cancellationToken);
                return;
            case "hiscore":
                HandleHiscore(root, cancellationToken);
                return;
        }
    }

    private async Task HandleMarqueeAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var media = Child(payload, "Media", "media");
        var snapshotMeta = ExtractLightingMeta(payload);
        var selection = Child(payload, "Selection", "selection");
        var systemScope = Text(selection, "Scope", "scope").Equals("system", StringComparison.OrdinalIgnoreCase);

        // remember the snapshot kinds: template renders and component feeds use them
        lock (_lastMarqueeKinds)
        {
            // rebuilt, never accumulated: the legacy fields below are always assigned —
            // null included — but the asset table only ADDS what the entry owns, so a
            // medium the next game lacks would have survived into it
            _lastMarqueeKinds.Clear();
            _lastMarqueeKinds["logo"] = MediaPath(media, "Logo");
            _lastMarqueeKinds["fanart"] = MediaPath(media, "Fanart");
            _lastMarqueeKinds["marquee"] = MediaPath(media, "Marquee");
            _lastMarqueeKinds["generated"] = MediaPath(media, "GeneratedMarquee");
            _lastMarqueeKinds["screenmarquee"] = MediaPath(media, "ScreenMarquee");
            _lastMarqueeKinds["screenmarquee-small"] = MediaPath(media, "ScreenMarqueeSmall");
            _lastMarqueeKinds["topper"] = MediaPath(media, "Topper");
            ReadAssetTables(payload, _lastMarqueeKinds);
        }
        // a marquee prints the game's name, its genre, its description: the text has to
        // be here BEFORE the gabarit renders, not on a stream that arrives afterwards
        ReadTextBlock(payload, snapshotMeta?.Rom);
        _lastMarqueeMeta = snapshotMeta;
        lock (_lastMarqueeKinds) RememberKinds("marquee", snapshotMeta?.Rom, new Dictionary<string, string?>(_lastMarqueeKinds, StringComparer.OrdinalIgnoreCase));

        // On-disk sources (creation / gabarit / drop) and the card overrides are keyed
        // by the FRONTEND system the user sees in ES and the Setup keys by (mame). The
        // marquee payload's own System is the CANONICAL media folder (arcade) or empty
        // on a system browse, so resolve those with _selectedSystem instead — otherwise
        // the runtime shows a different source than the Setup preview. SystemSpellings
        // still bridges mame ↔ arcade for the game files kept under the canonical folder.
        var resolveMeta = snapshotMeta;
        if (!string.IsNullOrEmpty(_selectedSystem)
            && (snapshotMeta == null || !string.Equals(snapshotMeta.System, _selectedSystem, StringComparison.OrdinalIgnoreCase)))
        {
            resolveMeta = snapshotMeta is null
                ? new Application.Lighting.LightingSceneMeta(null, null, null, null, _selectedSystem, _selectedRom)
                : snapshotMeta with { System = _selectedSystem };
        }

        // the per-system priority chain decides the marquee source; the stream's
        // own priority (marquee > generated > logo) stays the last resort
        var chained = _compositionChains.Resolve("marquee", resolveMeta, systemScope, SnapshotKind);
        var marquee = chained
                      ?? MediaPath(media, "Marquee") ?? MediaPath(media, "GeneratedMarquee") ?? MediaPath(media, "Logo");
        // may be null: a creation, a gabarit or the surface's own composition can still
        // carry this entry, so they are resolved BEFORE deciding to clear
        {
            if (snapshotMeta != null)
            {
                // the ingame effect layers (game > system > genre) follow the displayed game
                _ingameEffects.SetContext(snapshotMeta.System ?? _selectedSystem, snapshotMeta.Rom ?? _selectedRom,
                    _genreMap.Resolve(snapshotMeta.Genre, snapshotMeta.GenreIds), _effectOverridesRoot, _logger);
            }
            foreach (var target in _config.GetTargetsForContent("marquee"))
            {
                // the user's "use this source" card selection (Setup) wins when set: it
                // resolves the fixed chain for this exact target, skipping the sources
                // disabled by the click. No selection → the default precedence below.
                var policy = _overrides.For(target, systemScope, resolveMeta?.System, resolveMeta?.Rom);
                if (policy != null)
                {
                    var picked = ResolveMarqueeOverride(policy, target, resolveMeta, systemScope, media);
                    if (picked != null)
                    {
                        await _surfaces.DisplayMediaAsync(picked, target, cancellationToken, snapshotMeta, resolved: true);
                    }
                    else
                    {
                        // The chain picked in the game's card is the WHOLE answer: falling
                        // back to the stream served a source the user had explicitly
                        // switched off, and passed null down when there was none at all.
                        _surfaces.ClearMedia(target);
                    }
                    continue;
                }

                // per-surface precedence: a graphic creation wins, then the surface's
                // general template (gabarit) rendered for this game/system, then the
                // category-level chain resolution
                // ingame, the dynamic render is the ONLY law: the other sources are ES
                // browsing media (docs\RENDU-DYNAMIQUE.md)
                var dynamicRender = ResolveDynamicSurface("marquee", target, resolveMeta, systemScope);
                if (_displayScene.Equals("ingame", StringComparison.OrdinalIgnoreCase))
                {
                    if (dynamicRender != null)
                    {
                        _surfaces.SetDynamicRenderActive(target, true);
                        await _surfaces.DisplayMediaAsync(dynamicRender, target, cancellationToken, snapshotMeta, resolved: true);
                        continue;
                    }
                }

                // THE COMPOSITION RULES. A surface that stacks bakeable layers under a
                // lighting engine has said what it wants to show; nothing resolved
                // elsewhere may replace it. It only exists for such a surface, so every
                // other one keeps the historical precedence below.
                if (dynamicRender != null)
                {
                    _surfaces.SetDynamicRenderActive(target, true);
                    await _surfaces.DisplayMediaAsync(dynamicRender, target, cancellationToken, snapshotMeta, resolved: true);
                    continue;
                }

                var surfaceCreation = _compositionChains.SurfaceCreation("marquee", target, resolveMeta, systemScope);
                var surfaceGabarit = surfaceCreation == null
                    ? _compositionChains.SurfaceGabarit("marquee", target, resolveMeta, systemScope)
                    : null;
                _surfaces.SetDynamicRenderActive(target, false);
                var chosen = surfaceCreation ?? surfaceGabarit ?? marquee;
                if (chosen == null)
                {
                    _surfaces.ClearMedia(target);
                    continue;
                }
                await _surfaces.DisplayMediaAsync(chosen, target, cancellationToken, snapshotMeta,
                    resolved: surfaceCreation != null || surfaceGabarit != null || chained != null);
            }
        }

        FeedSurfaceComponents(media, snapshotMeta);

        var dmd = Child(media, "Dmd", "dmd");
        var generatedDmdPath = MediaPath(dmd, "Generated");
        var stillDmdPath = MediaPath(dmd, "Still");
        var chainedDmd = _compositionChains.Resolve("dmd", resolveMeta, systemScope, source => source.ToLowerInvariant() switch
        {
            "animations" => FirstAnimation(dmd),
            "still" => stillDmdPath,
            "generated" => generatedDmdPath,
            _ => null
        });
        // may be null: a creation or a gabarit can still carry this entry
        var dmdPath = chainedDmd ?? FirstAnimation(dmd) ?? stillDmdPath ?? generatedDmdPath;
        // Keep the generated game DMD behind text even when an animation is preferred while idle.
        if (dmdPath != null)
            await _dmd.SetBaseMediaAsync(dmdPath, cancellationToken, generatedDmdPath ?? stillDmdPath ?? dmdPath);
        foreach (var target in _config.GetTargetsForContent("dmd"))
        {
            var surfaceCreation = _compositionChains.SurfaceCreation("dmd", target, resolveMeta, systemScope);
            var surfaceGabarit = surfaceCreation == null
                ? _compositionChains.SurfaceGabarit("dmd", target, resolveMeta, systemScope)
                : null;
            var chosenDmd = surfaceCreation ?? surfaceGabarit ?? dmdPath;
            if (chosenDmd == null)
            {
                _surfaces.ClearMedia(target);
                continue;
            }
            await _surfaces.DisplayMediaAsync(chosenDmd, target, cancellationToken);
        }
    }

    // the fixed chain the Setup cards resolve in (spec §6); a disabled source is
    // skipped, so an override starts the chain at the highest source the user left on.
    private static readonly OverrideSource[] MarqueeSystemOrder =
        { OverrideSource.Personal, OverrideSource.UserDrop, OverrideSource.Generated, OverrideSource.Scraped, OverrideSource.Logo };
    private static readonly OverrideSource[] MarqueeGameOrder =
        { OverrideSource.Personal, OverrideSource.UserDrop, OverrideSource.Generated, OverrideSource.Scraped, OverrideSource.Logo, OverrideSource.SystemFallback };

    /// <summary>Resolves the marquee for a target that carries a card override: the
    /// first ENABLED source (fixed order) with a present file wins. Null → the caller
    /// keeps the stream's own offer.</summary>
    private string? ResolveMarqueeOverride(Application.Media.PresentationOverrides.TargetPolicy policy, string target,
        Application.Lighting.LightingSceneMeta? meta, bool systemScope, JsonElement media)
    {
        var order = systemScope || string.IsNullOrEmpty(meta?.Rom) ? MarqueeSystemOrder : MarqueeGameOrder;
        foreach (var source in order)
        {
            if (!policy.IsEnabled(source)) continue;
            var path = ResolveMarqueeSource(source, target, meta, systemScope, media);
            if (path != null && File.Exists(path)) return path;
        }
        return null;
    }

    private string? ResolveMarqueeSource(OverrideSource source, string target,
        Application.Lighting.LightingSceneMeta? meta, bool systemScope, JsonElement media)
        => source switch
        {
            OverrideSource.Personal => _compositionChains.SurfaceCreation("marquee", target, meta, systemScope)
                                       ?? _compositionChains.CategoryCreation("marquee", meta, systemScope),
            OverrideSource.UserDrop => _compositionChains.UserDropFile("marquee", meta, systemScope),
            // NO media fallback, ever: a source resolves to ITS media or to nothing.
            // Substituting a neighbour silently — the autogen for the template, the
            // screenmarquee for the marquee — is how a laid-out logo ends up stamped on
            // a screenmarquee that already carries one.
            OverrideSource.Generated => _compositionChains.SurfaceGabarit("marquee", target, meta, systemScope),
            OverrideSource.Scraped => MediaPath(media, "Marquee"),
            OverrideSource.Logo => MediaPath(media, "Logo"),
            // the game payload carries no system media, so the system fallback resolves
            // only the on-disk system sources (creation / drop / gabarit) for this surface
            OverrideSource.SystemFallback => _compositionChains.SurfaceCreation("marquee", target, meta, systemScope: true)
                                             ?? _compositionChains.CategoryCreation("marquee", meta, systemScope: true)
                                             ?? _compositionChains.UserDropFile("marquee", meta, systemScope: true)
                                             ?? _compositionChains.SurfaceGabarit("marquee", target, meta, systemScope: true),
            _ => null
        };

    /// <summary>
    /// The dynamic surface components eat the whole snapshot: every media kind
    /// (logo, fanart, screenmarquee…) plus the game video resolved on disk (the
    /// snapshot does not carry it), and the selection meta for text.meta.
    /// Cheap no-op when no surface declares dynamic components.
    /// </summary>
    private void FeedSurfaceComponents(JsonElement media, Application.Lighting.LightingSceneMeta? meta)
    {
        if (!_surfaces.HasComponent("media.logo") && !_surfaces.HasComponent("media.fanart")
            && !_surfaces.HasComponent("media.image") && !_surfaces.HasComponent("media.video")
            && !_surfaces.HasComponent("text.meta"))
            return;

        var kinds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["logo"] = MediaPath(media, "Logo"),
            ["fanart"] = MediaPath(media, "Fanart"),
            ["marquee"] = MediaPath(media, "Marquee"),
            ["generated"] = MediaPath(media, "GeneratedMarquee"),
            ["screenmarquee"] = MediaPath(media, "ScreenMarquee"),
            ["screenmarquee-small"] = MediaPath(media, "ScreenMarqueeSmall"),
            ["topper"] = MediaPath(media, "Topper"),
            // APIExpose 1.3.5+ carries Media.Video in the snapshot; the disk
            // walk stays as fallback for older APIs.
            ["video"] = MediaPath(media, "Video") ?? ResolveGameVideo(meta)
        };

        var metaValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = meta?.GameName ?? "",
            ["year"] = meta?.Year?.ToString() ?? "",
            ["developer"] = meta?.Developer ?? "",
            ["publisher"] = meta?.Publisher ?? "",
            ["system"] = meta?.System ?? ""
        };

        // The entry's whole text block becomes tokens too — {desc}, {genre}, {players},
        // {rating}. The gabarit already resolved them; a text LAYER could not, so a
        // layer written as {desc} showed the tag itself. Fields known from the selection
        // stay authoritative: they come from the entry, not from a scrape.
        foreach (var (field, value) in CurrentText())
        {
            if (!metaValues.TryGetValue(field, out var existing) || string.IsNullOrEmpty(existing))
            {
                metaValues[field] = value;
            }
        }

        _surfaces.UpdateComponentMedia(kinds, metaValues);
        _ = ResolveLiveVideoAsync(meta);
    }

    private static readonly HttpClient VideoHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private (string Token, DateTime Expires) _twitchToken;

    /// <summary>
    /// media.video source chain (user rule: live stream &gt; YouTube &gt; local video).
    /// The local file is already pushed with the snapshot; when a live Twitch
    /// stream (or a YouTube video) is found for the game, the component swaps to
    /// its embed. Every lookup failure silently keeps the previous source.
    /// </summary>
    private async Task ResolveLiveVideoAsync(Application.Lighting.LightingSceneMeta? meta)
    {
        if (meta?.GameName is not { Length: > 0 } gameName) return;
        var sources = _config.GetSurfaces()
            .SelectMany(surface => surface.Components)
            .FirstOrDefault(component => component.Type.Equals("media.video", StringComparison.OrdinalIgnoreCase))
            ?.Option("sources", "local")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (sources == null || !sources.Any(s => s is "twitch-live" or "youtube")) return;

        var rom = meta.Rom;
        foreach (var source in sources)
        {
            string? url = null;
            try
            {
                if (source.Equals("twitch-live", StringComparison.OrdinalIgnoreCase))
                    url = await TwitchLiveUrlAsync(gameName).ConfigureAwait(false);
                else if (source.Equals("youtube", StringComparison.OrdinalIgnoreCase))
                    url = await YouTubeEmbedUrlAsync(gameName).ConfigureAwait(false);
                else if (source.Equals("local", StringComparison.OrdinalIgnoreCase))
                    return; // the snapshot feed already pushed the local file
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Video source {Source} lookup failed: {Message}", source, ex.Message);
            }

            if (url != null)
            {
                // the selection may have moved on during the lookup
                if (_lastMarqueeMeta?.Rom != rom) return;
                _logger.LogInformation("media.video: {Source} found for {Game}", source, gameName);
                _surfaces.SetComponentSource("media.video", url);
                return;
            }
        }
    }

    /// <summary>Live Twitch stream on the game, via Helix (client credentials from
    /// config [Scraper] TwitchClientId/TwitchClientSecret).</summary>
    private async Task<string?> TwitchLiveUrlAsync(string gameName)
    {
        var clientId = _config.GetValue("Scraper", "TwitchClientId");
        var secret = _config.GetValue("Scraper", "TwitchClientSecret");
        if (clientId.Length == 0 || secret.Length == 0) return null;

        if (_twitchToken.Token is not { Length: > 0 } || DateTime.UtcNow >= _twitchToken.Expires)
        {
            using var tokenResponse = await VideoHttp.PostAsync(
                $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(secret)}&grant_type=client_credentials",
                null).ConfigureAwait(false);
            if (!tokenResponse.IsSuccessStatusCode) return null;
            using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
            var token = tokenDoc.RootElement.GetProperty("access_token").GetString() ?? "";
            var expires = tokenDoc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;
            _twitchToken = (token, DateTime.UtcNow.AddSeconds(Math.Max(60, expires - 120)));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.twitch.tv/helix/games?name={Uri.EscapeDataString(gameName)}");
        request.Headers.Add("Client-Id", clientId);
        request.Headers.Add("Authorization", "Bearer " + _twitchToken.Token);
        using var gameResponse = await VideoHttp.SendAsync(request).ConfigureAwait(false);
        if (!gameResponse.IsSuccessStatusCode) return null;
        using var gameDoc = JsonDocument.Parse(await gameResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        var gameId = gameDoc.RootElement.TryGetProperty("data", out var games) && games.GetArrayLength() > 0
            ? games[0].GetProperty("id").GetString()
            : null;
        if (gameId == null) return null;

        using var streamsRequest = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.twitch.tv/helix/streams?game_id={gameId}&first=1");
        streamsRequest.Headers.Add("Client-Id", clientId);
        streamsRequest.Headers.Add("Authorization", "Bearer " + _twitchToken.Token);
        using var streamsResponse = await VideoHttp.SendAsync(streamsRequest).ConfigureAwait(false);
        if (!streamsResponse.IsSuccessStatusCode) return null;
        using var streamsDoc = JsonDocument.Parse(await streamsResponse.Content.ReadAsStringAsync().ConfigureAwait(false));
        var login = streamsDoc.RootElement.TryGetProperty("data", out var streams) && streams.GetArrayLength() > 0
            ? streams[0].GetProperty("user_login").GetString()
            : null;
        return login == null ? null : $"https://www.twitch.tv/{login}";
    }

    /// <summary>First embeddable YouTube video on the game (Data API key in
    /// config [Scraper] YouTubeApiKey).</summary>
    private async Task<string?> YouTubeEmbedUrlAsync(string gameName)
    {
        var key = _config.GetValue("Scraper", "YouTubeApiKey");
        if (key.Length == 0) return null;
        var query = Uri.EscapeDataString(gameName + " arcade gameplay");
        var json = await VideoHttp.GetStringAsync(
            $"https://www.googleapis.com/youtube/v3/search?part=snippet&maxResults=1&type=video&videoEmbeddable=true&q={query}&key={Uri.EscapeDataString(key)}")
            .ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var id = doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0
            ? items[0].GetProperty("id").GetProperty("videoId").GetString()
            : null;
        return id == null ? null : $"https://www.youtube.com/embed/{id}?autoplay=1&mute=1&controls=0&loop=1&playlist={id}";
    }

    /// <summary>Fallback for APIExpose &lt; 1.3.5 (snapshot without Media.Video):
    /// games\&lt;rom&gt;\video.mp4 lives in the APIExpose media library (sibling
    /// plugin) and is walked on disk.</summary>
    private string? ResolveGameVideo(Application.Lighting.LightingSceneMeta? meta)
    {
        if (meta?.System is not { Length: > 0 } || meta.Rom is not { Length: > 0 }) return null;
        foreach (var system in meta.System.Equals("mame", StringComparison.OrdinalIgnoreCase)
                     ? new[] { meta.System, "arcade" } : new[] { meta.System })
        {
            var path = Path.Combine(_config.BaseDirectory, "..", "APIExpose", "media", "systems",
                system, "games", meta.Rom, "video.mp4");
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        return null;
    }

    /// <summary>
    /// Metadata carried by the enriched marquee stream (Selection.Releasedate /
    /// Developer / Publisher / System) — input of the §15 lighting profile resolver.
    /// </summary>
    private static Application.Lighting.LightingSceneMeta? ExtractLightingMeta(JsonElement payload)
    {
        var selection = Child(payload, "Selection", "selection");
        if (selection.ValueKind != JsonValueKind.Object) return null;

        int? year = null;
        var releasedate = Text(selection, "Releasedate", "releasedate", "ReleaseDate");
        if (releasedate.Length >= 4 && int.TryParse(releasedate[..4], out var parsed) && parsed is > 1950 and < 2100)
            year = parsed;

        var developer = Text(selection, "Developer", "developer");
        var publisher = Text(selection, "Publisher", "publisher");
        var gameName = Text(selection, "GameName", "gameName", "Name", "name");
        var system = Text(selection, "System", "system");
        var gamePath = Text(selection, "GamePath", "gamePath");
        var rom = gamePath.Length > 0 ? Path.GetFileNameWithoutExtension(gamePath) : Text(selection, "Game", "game");
        var genre = Text(selection, "Genre", "genre");
        var genreIds = Text(selection, "Genres", "genres");

        if (year == null && developer.Length == 0 && publisher.Length == 0 && gameName.Length == 0 && system.Length == 0)
            return null;
        return new Application.Lighting.LightingSceneMeta(year,
            developer.Length > 0 ? developer : null,
            publisher.Length > 0 ? publisher : null,
            gameName.Length > 0 ? gameName : null,
            system.Length > 0 ? system : null,
            rom.Length > 0 ? rom : null,
            genre.Length > 0 ? genre : null,
            genreIds.Length > 0 ? genreIds : null);
    }

    private async Task HandleTopperAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var media = Child(payload, "Media", "media");
        var meta = ExtractLightingMeta(payload) ?? _lastMarqueeMeta;
        var systemScope = Text(Child(payload, "Selection", "selection"), "Scope", "scope")
            .Equals("system", StringComparison.OrdinalIgnoreCase);
        var topperKinds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["topper"] = MediaPath(media, "Topper"),
            ["fanart"] = MediaPath(media, "Fanart"),
            ["logo"] = MediaPath(media, "Logo"),
            ["marquee"] = MediaPath(media, "Marquee"),
        };
        // the topper stream now carries its own table: flyer, box art, capture — a
        // composition on this surface no longer has to borrow the marquee's media
        ReadAssetTables(payload, topperKinds);
        ReadTextBlock(payload, meta?.Rom);
        RememberKinds("topper", meta?.Rom, topperKinds);
        var chained = _compositionChains.Resolve("topper", meta, systemScope, source =>
            source.Equals("topper", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Topper")
            : source.Equals("fanart", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Fanart")
            : source.Equals("logo", StringComparison.OrdinalIgnoreCase) ? MediaPath(media, "Logo")
            : null);
        // may be null: a game without a scraped topper can still have a creation or a
        // gabarit, so the per-surface sources are resolved BEFORE deciding to clear
        var path = chained ?? MediaPath(media, "Topper");
        {
            foreach (var target in _config.GetTargetsForContent("topper"))
            {
                var surfaceCreation = _compositionChains.SurfaceCreation("topper", target, meta, systemScope);
                var surfaceGabarit = surfaceCreation == null
                    ? _compositionChains.SurfaceGabarit("topper", target, meta, systemScope)
                    : null;
                var chosen = surfaceCreation ?? surfaceGabarit ?? path;
                if (chosen == null)
                {
                    // nothing at all for this entry: empty the surface rather than leave
                    // the previous game's topper standing
                    _surfaces.ClearMedia(target);
                    continue;
                }
                await _surfaces.DisplayMediaAsync(chosen, target, cancellationToken,
                    resolved: surfaceCreation != null || surfaceGabarit != null || chained != null);
            }
        }
    }

    /// <summary>Chain source name → the last marquee snapshot asset.</summary>
    private string? SnapshotKind(string source)
    {
        lock (_lastMarqueeKinds)
        {
            return _lastMarqueeKinds.TryGetValue(source, out var path) ? path : null;
        }
    }

    private async Task HandleInstructionCardAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);

        // this stream now carries the ingredients of a COMPOSED card — its media, the
        // entry's text and what the buttons do — and they are worth keeping even for a
        // game that ships no ready-made card
        var icKinds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        ReadAssetTables(payload, icKinds);
        if (icKinds.Count > 0) RememberKinds("iccard", _lastMarqueeMeta?.Rom, icKinds);
        ReadTextBlock(payload, _lastMarqueeMeta?.Rom);
        ReadControlsBlock(payload, _lastMarqueeMeta?.Rom);

        var cards = Child(payload, "Cards", "cards");
        if (cards.ValueKind != JsonValueKind.Array) return;
        // keep the whole catalog, with what each card is ABOUT: the role is the folder
        // the card sits in, and it is what lets a viewer follow one character instead of
        // walking the whole game
        var sources = new List<InstructionCardCatalog.CardSource>();
        foreach (var card in cards.EnumerateArray())
        {
            var path = ResolveLocal(Text(card, "Path", "path"));
            if (path == null) continue;
            sources.Add(new InstructionCardCatalog.CardSource(path, Text(card, "Role", "role"), ReadCardPanels(card)));
        }

        // A game without an instruction card CLEARS the previous one. Returning early
        // left the last game's card on screen — one card following you across the whole
        // library. Nothing of an entry may survive into the next.
        await _instructionCards.SetCardsAsync(sources, cancellationToken);
    }

    /// <summary>Where each entry sits inside a card, when the card's companion file says
    /// so. Rects come as fractions [x, y, w, h] of the drawing, so they survive any
    /// display size; a card without a companion simply has none.</summary>
    private static IReadOnlyList<InstructionCardCatalog.CardPanel> ReadCardPanels(JsonElement card)
    {
        var panels = Child(card, "Panels", "panels");
        if (panels.ValueKind != JsonValueKind.Array) return Array.Empty<InstructionCardCatalog.CardPanel>();

        var result = new List<InstructionCardCatalog.CardPanel>();
        foreach (var panel in panels.EnumerateArray())
        {
            var rect = Child(panel, "Rect", "rect");
            if (rect.ValueKind != JsonValueKind.Array) continue;
            var values = rect.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.Number)
                .Select(v => v.GetDouble())
                .ToArray();
            if (values.Length != 4) continue;

            var label = Text(panel, "Label", "label");
            result.Add(new InstructionCardCatalog.CardPanel(
                Text(panel, "Role", "role"),
                Text(panel, "Kind", "kind"),
                Boolean(panel, "Named", "named"),
                label.Length > 0 ? label : null,
                values[0], values[1], values[2], values[3]));
        }

        return result;
    }

    /// <summary>
    /// The panel stream carries three different things, and each has its own life:
    /// the CABINET's description (retained, changes only when the user reconfigures),
    /// what the SELECTED GAME makes of each button (changes with every selection), and
    /// the PRESSES themselves (events, never coalesced).
    ///
    /// Anything else on the stream keeps its historical route to the lcd surface.
    /// </summary>
    private async Task HandlePanelAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var type = Text(root, "Type", "type");

        if (type.StartsWith("panel.input.", StringComparison.OrdinalIgnoreCase))
        {
            HandlePanelInput(root, type.EndsWith(".pressed", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (type.Equals("panel.config.changed", StringComparison.OrdinalIgnoreCase))
        {
            HandlePanelConfig(root);
            return;
        }

        if (type.Equals("panel.state", StringComparison.OrdinalIgnoreCase))
        {
            HandlePanelState(root);
            return;
        }

        await HandleSimpleMediaAsync(root, "lcd", cancellationToken);
    }

    /// <summary>The cabinet's own description: how many panels, how many buttons each,
    /// where they sit. Read from the stream and NOT from APIExpose's settings file —
    /// the panel drawn has to be the panel the API is publishing.</summary>
    private void HandlePanelConfig(JsonElement root)
    {
        var payload = Payload(root);
        var cabinet = Child(payload, "Cabinet", "cabinet");
        var layout = Child(payload, "Layout", "layout");

        var rows = new List<IReadOnlyList<int>>();
        var rowsElement = Child(layout, "Rows", "rows");
        if (rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array) continue;
                var slots = row.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.Number)
                    .Select(x => x.GetInt32())
                    .ToArray();
                if (slots.Length > 0) rows.Add(slots);
            }
        }

        // no rows, no drawing: without the arrangement we know WHICH buttons the cabinet
        // has but not where they go, and inventing a layout would show a panel that is
        // not this cabinet's
        if (rows.Count == 0) return;

        var config = new Core.Surfaces.PanelBoardConfig(
            Math.Max(1, Integer(cabinet, "PlayerCount", "playerCount") ?? 1),
            Integer(cabinet, "ButtonsPerPlayer", "buttonsPerPlayer") ?? rows.Sum(r => r.Count),
            rows,
            Boolean(cabinet, "ArcadeJoystick", "arcadeJoystick") || Boolean(cabinet, "AnalogJoystick", "analogJoystick"),
            _panelStickColor);

        _panelConfig = config;
        _surfaces.UpdatePanelConfig(config);
        _logger.LogInformation("Panel config: {Players} panel(s), {Buttons} button(s), {Rows} row(s)",
            config.PlayerCount, config.ButtonsPerPlayer, rows.Count);
    }

    /// <summary>
    /// What the selected game does with each place. Being on the game's card in ES is
    /// enough — nothing has to be launched — which is exactly how a player uses it:
    /// browse the library, read what each button does, game after game.
    /// </summary>
    private void HandlePanelState(JsonElement root)
    {
        var payload = Payload(root);
        var panel = Child(payload, "ActivePanel", "activePanel");
        var inputs = Child(Child(panel, "ControlMap", "controlMap"), "Inputs", "inputs");
        if (inputs.ValueKind != JsonValueKind.Array) return;

        var byPlayer = new Dictionary<int, Dictionary<int, Core.Surfaces.PanelBoardButton>>();
        string? stickColor = null;

        foreach (var input in inputs.EnumerateArray())
        {
            var color = Text(input, "Color", "color");
            if (stickColor == null && color.Length > 0
                && Text(input, "DeviceType", "deviceType").Contains("joy", StringComparison.OrdinalIgnoreCase))
            {
                // the stick's colour is data too (1941 red, sf2 blue), not a decoration
                // to invent
                stickColor = color;
            }

            if (Integer(input, "Slot", "slot") is not { } slot) continue;
            var player = Math.Max(1, Integer(input, "Player", "player") ?? 1);
            var function = Text(input, "Function", "function");
            var used = function.Length > 0;

            var slots = byPlayer.TryGetValue(player, out var existing)
                ? existing
                : byPlayer[player] = new Dictionary<int, Core.Surfaces.PanelBoardButton>();

            // a place described twice keeps the description that says something: an entry
            // with no function must not erase the one that has one
            if (slots.TryGetValue(slot, out var already) && already.Used && !used) continue;

            var label = Text(input, "Label", "label");
            slots[slot] = new Core.Surfaces.PanelBoardButton(
                slot,
                label.Length > 0 ? label : slot.ToString(),
                function,
                color,
                used);
        }

        // the two drawn views of this same panel, written by APIExpose for the themes —
        // read from the path the stream gives, never from a folder we went looking for
        var svg = Child(payload, "Svg", "svg");
        _surfaces.UpdatePanelArt(ReadArt(Child(svg, "Top", "top")), ReadArt(Child(svg, "Front", "front")));

        if (stickColor != null && !string.Equals(stickColor, _panelStickColor, StringComparison.OrdinalIgnoreCase))
        {
            _panelStickColor = stickColor;
            if (_panelConfig != null) _surfaces.UpdatePanelConfig(_panelConfig with { StickColor = stickColor });
        }

        // Every panel the cabinet has, even those this game says nothing about: a player
        // 2 panel left describing the PREVIOUS game would be a lie, and the rule is the
        // same as for media — nothing of an entry survives into the next.
        var panels = Math.Max(_panelConfig?.PlayerCount ?? 1, byPlayer.Keys.DefaultIfEmpty(1).Max());
        for (var player = 1; player <= panels; player++)
        {
            _surfaces.UpdatePanelButtons(player,
                byPlayer.TryGetValue(player, out var slots)
                    ? slots
                    : new Dictionary<int, Core.Surfaces.PanelBoardButton>());
        }
    }

    /// <summary>One drawn view: where the file is, how big the drawing is, and where
    /// each button landed in it. Null when APIExpose drew nothing for this game — the
    /// panel then falls back to its own plain shapes.</summary>
    private Core.Surfaces.PanelBoardArt? ReadArt(JsonElement view)
    {
        if (view.ValueKind != JsonValueKind.Object) return null;
        var path = ResolveLocal(Text(view, "Path", "path"));
        if (path is null) return null;

        var width = Number(view, "Width", "width");
        var height = Number(view, "Height", "height");
        if (width is not > 0 || height is not > 0) return null;

        var buttons = new List<Core.Surfaces.PanelArtButton>();
        var list = Child(view, "Buttons", "buttons");
        if (list.ValueKind == JsonValueKind.Array)
        {
            foreach (var button in list.EnumerateArray())
            {
                if (Integer(button, "Slot", "slot") is not { } slot) continue;
                buttons.Add(new Core.Surfaces.PanelArtButton(
                    slot,
                    Number(button, "Cx", "cx") ?? 0,
                    Number(button, "Cy", "cy") ?? 0,
                    Number(button, "R", "r") ?? 0));
            }
        }

        // a drawing whose buttons we cannot place would light nothing: better the plain
        // panel, which at least answers when a button is pressed
        return buttons.Count > 0
            ? new Core.Surfaces.PanelBoardArt(path, width.Value, height.Value, buttons)
            : null;
    }

    /// <summary>A physical press, already resolved to a slot by APIExpose: the panel
    /// lights the place that was pressed. If another place lights, the wiring is not
    /// what the cabinet's map claims — which is the whole reason this exists.</summary>
    private void HandlePanelInput(JsonElement root, bool pressed)
    {
        var payload = Payload(root);
        var player = Math.Max(1, Integer(payload, "Player", "player") ?? 1);
        var slot = Integer(payload, "Slot", "slot");
        var system = Text(payload, "System", "system");
        _surfaces.SetPanelInput(player, slot, system.Length > 0 ? system : null, pressed);

        // The first press of a session is worth a line: it is the proof the whole chain
        // is alive — cabinet, API, stream, surface — and the one thing a support log
        // needs when someone reports "my panel does not light up". After that it would
        // be one line per press, so it says nothing more.
        if (pressed && !_panelInputSeen)
        {
            _panelInputSeen = true;
            _logger.LogInformation("First panel press received: player {Player}, slot {Slot}, system {System}",
                player, slot?.ToString() ?? "-", system.Length > 0 ? system : "-");
        }
        else if (pressed)
        {
            _logger.LogDebug("Panel press: player {Player}, slot {Slot}, system {System}",
                player, slot?.ToString() ?? "-", system.Length > 0 ? system : "-");
        }
    }

    private async Task HandleSimpleMediaAsync(JsonElement root, string defaultTarget, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var path = ResolveLocal(Text(payload, "Path", "path", "Value", "value"));
        var target = Text(payload, "Target", "target");
        var resolved = target.Length == 0 ? defaultTarget : target.ToLowerInvariant();
        if (path != null) await _surfaces.DisplayMediaAsync(path, resolved, cancellationToken);
        else _surfaces.ClearMedia(resolved); // nothing for this entry → empty, never the previous one's
    }

    private void HandleHiscore(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var rom = NormalizeRom(Text(payload, "RomName", "romName", "Rom", "rom"));
        if (rom.Length == 0) rom = NormalizeRom(Text(payload, "GamePath", "gamePath", "RomPath", "romPath"));
        if (rom.Length == 0 || !rom.Equals(_selectedRom, StringComparison.OrdinalIgnoreCase) && !rom.Equals(_runningRom, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Ignoring hiscore event outside selected/running game context: rom={Rom}, selected={Selected}, running={Running}", rom, _selectedRom, _runningRom);
            return;
        }

        // Prefer the full ranking (Top N) when APIExpose sends the Scores collection.
        var scores = Child(payload, "Scores", "scores");
        if (scores.ValueKind == JsonValueKind.Array)
        {
            var rows = ParseHiscoreRows(scores);
            if (rows.Count > 0)
            {
                _surfaces.SetHiscoreLeaderboard(HiscoreGameLabel(payload, rom), _selectedSystem ?? string.Empty, rows);
                return;
            }
        }

        // Fallback: a single value (legacy single-line overlay — zero regression).
        var score = Text(payload, "Score", "score", "Value", "value");
        var player = Text(payload, "Player", "player", "Name", "name");
        if (score.Length == 0) return;
        _surfaces.SetInformation("hiscore", "HIGH SCORE", $"{player} {score}".Trim(), null, true, 0);
    }

    /// <summary>Display label for the leaderboard title ("&lt;GAME&gt; — LOCAL LEADERBOARD").
    /// Prefers a real game name from the payload, else the rom id. Never translated.</summary>
    private string HiscoreGameLabel(JsonElement payload, string rom)
    {
        var name = Text(payload, "GameName", "gameName", "Title", "title", "LongName", "longName");
        return name.Length > 0 ? name : rom;
    }

    /// <summary>Builds the leaderboard rows from a Scores JSON array (shared by the WS
    /// hiscore event and the HTTP fetch). Ranks default to the row position.</summary>
    private List<Core.HiscoreRow> ParseHiscoreRows(JsonElement scores)
    {
        var rows = new List<Core.HiscoreRow>();
        if (scores.ValueKind != JsonValueKind.Array) return rows;
        var index = 0;
        foreach (var entry in scores.EnumerateArray())
        {
            index++;
            var name = Text(entry, "Name", "name", "Player", "player");
            var value = Text(entry, "Score", "score", "Value", "value");
            if (value.Length == 0 && name.Length == 0) continue;
            var rank = Text(entry, "Rank", "rank");
            if (rank.Length == 0) rank = index.ToString();
            rows.Add(new Core.HiscoreRow(rank, name, value));
        }
        return rows;
    }

    private CancellationTokenSource? _hiscoreFetchCts;

    private void CancelHiscoreFetch()
    {
        try { _hiscoreFetchCts?.Cancel(); } catch { /* already disposed */ }
        _hiscoreFetchCts = null;
    }

    /// <summary>Lot 3: debounced (latest-wins) fetch of GET /api/v1/hiscores for the
    /// just-selected game, so the leaderboard appears while browsing ES — not only on
    /// hiscore.updated. A fast scroll cancels pending fetches and only the last runs.</summary>
    private void ScheduleHiscoreFetch(string rom)
    {
        CancelHiscoreFetch();
        var cts = new CancellationTokenSource();
        _hiscoreFetchCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token).ConfigureAwait(false);
                await FetchHiscoreLeaderboardAsync(rom, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* superseded by a newer selection */ }
            catch (Exception ex) { _logger.LogDebug("Hiscore fetch failed: {Message}", ex.Message); }
        }, cts.Token);
    }

    private async Task FetchHiscoreLeaderboardAsync(string rom, CancellationToken ct)
    {
        var url = $"{HttpBaseUrl()}/api/v1/hiscores";
        using var response = await VideoHttp.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return; // no running/selected game, not found, error
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        // Selection may have moved on while the request was in flight.
        if (ct.IsCancellationRequested || !rom.Equals(_selectedRom, StringComparison.OrdinalIgnoreCase)) return;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (Text(root, "Status", "status").Equals("error", StringComparison.OrdinalIgnoreCase)) return;
        var rows = ParseHiscoreRows(Child(root, "Scores", "scores"));
        if (rows.Count == 0) return;
        var romName = Text(root, "RomName", "romName");
        var myRank = LocalMyRank(rows, Text(root, "Me", "me"));
        _surfaces.SetHiscoreLeaderboard(romName.Length > 0 ? romName : rom, _selectedSystem ?? string.Empty, rows, "local", myRank);
    }

    /// <summary>"Your best line" under the LOCAL board: the current player's best row
    /// (rows arrive rank-sorted, so the first name match is the best). Null when nobody is
    /// identified at the cabinet (anonymous), so no footer is drawn at all.</summary>
    private static Core.HiscoreMyRank? LocalMyRank(IReadOnlyList<Core.HiscoreRow> rows, string me)
    {
        if (string.IsNullOrWhiteSpace(me)) return null;
        var who = me.Trim();
        foreach (var r in rows)
        {
            if (!r.Name.Trim().Equals(who, StringComparison.OrdinalIgnoreCase)) continue;
            return new Core.HiscoreMyRank(false, true, false, r.Rank.Trim(), 0, r.Score.Trim(), who);
        }
        return new Core.HiscoreMyRank(false, false, false, string.Empty, 0, string.Empty, who);
    }

    private string HttpBaseUrl()
    {
        var ws = _config.ApiExposeWebSocketBaseUrl ?? "ws://127.0.0.1:12345";
        return ws.Replace("wss://", "https://").Replace("ws://", "http://").TrimEnd('/');
    }

    private CancellationTokenSource? _worldFetchCts;

    private void CancelWorldFetch()
    {
        try { _worldFetchCts?.Cancel(); } catch { /* already disposed */ }
        _worldFetchCts = null;
    }

    /// <summary>Debounced (latest-wins) fetch of the NelfePlay WORLD ranking (proxy of
    /// records/leaderboard) for the selected game, feeding a nelfeplay-source overlay.hiscore.</summary>
    private void ScheduleWorldFetch(string rom)
    {
        CancelWorldFetch();
        var cts = new CancellationTokenSource();
        _worldFetchCts = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, cts.Token).ConfigureAwait(false);
                await FetchWorldLeaderboardAsync(rom, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* superseded */ }
            catch (Exception ex) { _logger.LogDebug("World leaderboard fetch failed: {Message}", ex.Message); }
        }, cts.Token);
    }

    private async Task FetchWorldLeaderboardAsync(string rom, CancellationToken ct)
    {
        var url = $"{HttpBaseUrl()}/api/v1/nelfeplay/records/leaderboard";
        using var response = await VideoHttp.GetAsync(url, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return;
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (ct.IsCancellationRequested || !rom.Equals(_selectedRom, StringComparison.OrdinalIgnoreCase)) return;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!(root.TryGetProperty("present", out var present) && present.ValueKind == JsonValueKind.True)) return;
        if (!root.TryGetProperty("leaderboard", out var board) || board.ValueKind != JsonValueKind.Array) return;

        var rows = new List<Core.HiscoreRow>();
        foreach (var entry in board.EnumerateArray())
        {
            var score = entry.TryGetProperty("score", out var sc) ? sc.ToString() : string.Empty;
            if (score.Length == 0) continue;
            var rank = entry.TryGetProperty("rank", out var rk) ? rk.ToString() : string.Empty;
            var anon = entry.TryGetProperty("anonymous", out var an) && an.ValueKind == JsonValueKind.True;
            var player = (anon || !entry.TryGetProperty("player", out var pl)) ? "ANON" : (pl.GetString() ?? "ANON");
            rows.Add(new Core.HiscoreRow(rank, player, score));
        }
        if (rows.Count == 0) return;
        _surfaces.SetHiscoreLeaderboard(rom, _selectedSystem ?? string.Empty, rows, "nelfeplay", WorldMyRank(root));
    }

    /// <summary>"Your rank" under the WORLD board, from the me{} block records/leaderboard
    /// embeds: the certified rank if any, else a paired/unpaired state the window turns into
    /// a not-ranked-yet note or a call to identify.</summary>
    private static Core.HiscoreMyRank WorldMyRank(JsonElement root)
    {
        var paired = root.TryGetProperty("paired", out var pd) && pd.ValueKind == JsonValueKind.True;
        if (root.TryGetProperty("me", out var me) && me.ValueKind == JsonValueKind.Object)
        {
            var rank = me.TryGetProperty("rank", out var rk) && rk.ValueKind == JsonValueKind.Number ? rk.GetInt32() : 0;
            if (rank > 0)
            {
                var of = me.TryGetProperty("of", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt32() : 0;
                var score = me.TryGetProperty("score", out var sc) && sc.ValueKind == JsonValueKind.Number ? sc.GetInt32().ToString() : string.Empty;
                return new Core.HiscoreMyRank(true, true, paired, rank.ToString(), of, score, string.Empty);
            }
        }
        return new Core.HiscoreMyRank(true, false, paired, string.Empty, 0, string.Empty, string.Empty);
    }

    private async Task HandleFrontendAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var type = Text(root, "Type", "type");
        var payload = Payload(root);
        if (type.Equals("ui.game.selected", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.game.selected.raw", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.system.selected", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ui.system.selected.raw", StringComparison.OrdinalIgnoreCase))
        {
            if (_pinballDmdActive) ReleasePinballDmd("selection changed");
            _displayScene = "navigation";
            _surfaces.SetDisplayScene("navigation");
            var selectedSystem = ExtractSystem(payload);
            var selectedRom = ExtractRom(payload);
            if (selectedSystem.Length > 0)
            {
                _selectedSystem = selectedSystem;
                _logger.LogInformation("Frontend selected system: {System}", selectedSystem);
            }
            if (selectedRom.Length > 0)
            {
                var romChanged = !string.Equals(_selectedRom, selectedRom, StringComparison.OrdinalIgnoreCase);
                if (romChanged)
                {
                    _surfaces.ClearInformation("hiscore");
                    ForgetPreviousEntry();
                }
                _selectedRom = selectedRom;
                // Lot 3: load the local leaderboard for the newly-selected game so it
                // shows while browsing ES (debounced so a fast scroll fetches once).
                if (romChanged) ScheduleHiscoreFetch(selectedRom);
                // World ranking (NelfePlay) only when a surface asks for it, to avoid
                // hitting the online endpoint on every browse.
                if (romChanged && _surfaces.HasHiscoreSource("nelfeplay")) ScheduleWorldFetch(selectedRom);
            }
            else
            {
                _selectedRom = null;
                ForgetPreviousEntry();
                CancelHiscoreFetch();
                CancelWorldFetch();
                _surfaces.ClearInformation("hiscore");
            }
            return;
        }

        if (type.Equals("ui.game.ended", StringComparison.OrdinalIgnoreCase) || type.Equals("ui.game.ended.raw", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Frontend game ended event received: {Type}", type);
            // scene FIRST: ingame-only surfaces must leave the game screen even
            // if a later step throws — ES does not always re-select afterwards
            _displayScene = "navigation";
            _surfaces.SetDisplayScene("navigation");
            _lay.Clear();
            _presentation.MarkGameEnded();
            _runningRom = null;
            CancelDeferredEffects();
            if (_pinballDmdActive) ReleasePinballDmd("game ended");
            // back to the frontend: sounds return, audible re-ignition
            _surfaces.SetLightingIngame(false);
            _surfaces.PowerCycleLighting();
            // restore the navigation priority so the marquee keeps up while browsing ES
            ProcessPriorityHelper.Apply(_config.GetValue("Settings", "ProcessPriority", "belownormal"));
            return;
        }
        if (!type.Equals("ui.game.started", StringComparison.OrdinalIgnoreCase) && !type.Equals("ui.game.started.raw", StringComparison.OrdinalIgnoreCase)) return;
        // a new play session: drop any effect still pending from the previous one
        CancelDeferredEffects();
        _displayScene = "ingame";
        _surfaces.SetDisplayScene("ingame");
        _presentation.MarkGameStarted();
        // game launch drama: silent power cycle — the play session stays clean
        _surfaces.SetLightingIngame(true);
        _surfaces.PowerCycleLighting();
        // yield the CPU to the emulator during play (input latency); restored on game end
        ProcessPriorityHelper.Apply(_config.GetValue("Settings", "ProcessPriorityInGame", "belownormal"));
        var system = ExtractSystem(payload);
        if (system.Length == 0) system = _selectedSystem ?? string.Empty;
        if (system.Length > 0) _selectedSystem = system;
        var rom = ExtractRom(payload);
        if (rom.Length > 0) _runningRom = rom;
        _logger.LogInformation("Frontend game started event received: {Type}, system={System}, rom={Rom}", type, system, rom);
        if (system.Length > 0 && _config.ActiveSystemsDmd.Contains(system))
        {
            ActivatePinballDmd(system);
            return;
        }
        if (rom.Length > 0) await LoadLayoutAsync(rom, cancellationToken);
    }

    private void ActivatePinballDmd(string system)
    {
        if (_pinballDmdActive) return;
        _pinballDmdActive = true;
        _logger.LogInformation("System {System} is configured in ActiveSystemsDMD; private DMD is released for pinball.", system);
        _lay.Clear();
        _presentation.ClearGameState();
        _dmd.SetExternalControl(true);
    }

    private void ReleasePinballDmd(string reason)
    {
        if (!_pinballDmdActive) return;
        _pinballDmdActive = false;
        _logger.LogInformation("Pinball DMD external control released ({Reason}); private DMD will resume.", reason);
        _dmd.SetExternalControl(false);
    }

    private async Task HandleArcadeAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var type = Text(root, "Type", "type");
        var payload = Payload(root);
        var signals = Child(payload, "Signals", "signals");
        if (signals.ValueKind == JsonValueKind.Undefined) signals = Child(root, "Signals", "signals");
        if (signals.ValueKind == JsonValueKind.Array)
        {
            foreach (var signal in signals.EnumerateArray())
            {
                var key = Text(signal, "Key", "key");
                var value = Integer(signal, "Value", "value");
                if (key.Length > 0 && value != null)
                {
                    _lay.SetLampState(key, value.Value);
                    _surfaces.SetLightingOutput(key, value.Value);
                }
            }
        }
        if (type.Equals("mame.session.started", StringComparison.OrdinalIgnoreCase))
        {
            var rom = Text(payload, "MachineName", "machineName", "Rom", "rom");
            if (rom.Length > 0) await LoadLayoutAsync(rom, cancellationToken);
        }
    }

    /// <summary>
    /// ws/ingame: semantic .mem actions (CDC §9). The action is already semantic —
    /// resolve it through the ingame effects library and fire the light effect.
    /// </summary>
    private void HandleIngame(JsonElement root)
    {
        // APIExpose wrapper events are EventEnvelopes: the semantic action lives in
        // Payload.actionType or Payload.signal.Name (same extraction as LedManager)
        var payload = Payload(root);
        var signal = Child(payload, "Signal", "signal");

        var action = Text(root, "Action", "action");
        if (action.Length == 0) action = Text(payload, "ActionType", "actionType", "Action", "action");
        if (action.Length == 0 && signal.ValueKind == JsonValueKind.Object) action = Text(signal, "Name", "name");
        if (action.Length == 0) return;

        var family = Text(root, "Family", "family");
        if (family.Length == 0) family = Text(payload, "Family", "family");
        if (family.Length == 0 && signal.ValueKind == JsonValueKind.Object) family = Text(signal, "Family", "family");

        var color = Text(root, "Color", "color");
        if (color.Length == 0) color = Text(payload, "Color", "color");
        if (color.Length == 0 && signal.ValueKind == JsonValueKind.Object) color = Text(signal, "Color", "color");

        // flow lifecycle changes gate the speedrun leaderboard (no timer during demos)
        _presentation.OnGameplayFlow(action);

        // Two actions do not describe a change, they NAME what a player has in hand —
        // their description is "Cody", "Fire Water". That name is a card's role, so the
        // viewers following that player switch to it. Handled before the effect lookup:
        // a game announcing a character has something to show even when no light effect
        // is bound to it.
        if (action.Equals("CHARACTER_SELECTED", StringComparison.OrdinalIgnoreCase)
            || action.Equals("WEAPON_SELECTED", StringComparison.OrdinalIgnoreCase))
        {
            var name = Text(payload, "SourceCategory", "sourceCategory");
            if (name.Length == 0 && signal.ValueKind == JsonValueKind.Object)
                name = Text(signal, "SourceDescription", "sourceDescription");
            var player = Integer(payload, "Player", "player") ?? 1;
            // logged whatever happens next: an announcement that names nothing is the one
            // case where silence leaves no way to tell "the game said nothing" from
            // "nothing matched what it said"
            _logger.LogInformation("Player {Player} {Action}: \"{Name}\"", player, action, name);
            if (name.Length > 0)
            {
                _ = _instructionCards.OnNameAnnouncedAsync(player, name, CancellationToken.None);
            }
        }

        var sequence = _ingameEffects.Resolve(action, family.Length > 0 ? family : null);
        if (sequence.Count == 0) return;

        // la couleur portee par l'evenement (deltas score arcade) prime sur la
        // couleur de la regle : l'effet prend la teinte de la cible du jeu.
        var eventColor = Application.Lighting.IngameEffectLibrary.TryParseEventColor(color);

        _logger.LogInformation("Ingame action {Action} → {Count} effect action(s) ({Label})",
            action, sequence.Count, sequence[0].Label);
        foreach (var step in sequence)
        {
            var rule = eventColor is { } overrideColor ? step with { Color = overrideColor } : step;
            if (rule.DelayMs <= 0)
            {
                FireEffect(rule);
            }
            else
            {
                // sequenced action ("flash PUIS nuée de sprites"): fire after its
                // delay, but only if the play session is still the same one
                var token = _effectSessionCts.Token;
                _ = Task.Delay(rule.DelayMs, token).ContinueWith(t =>
                {
                    if (!t.IsCanceled && !token.IsCancellationRequested) FireEffect(rule);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    /// <summary>Cancels every effect still waiting on its delay and arms a fresh
    /// token for the next play session.</summary>
    private void CancelDeferredEffects()
    {
        var previous = _effectSessionCts;
        _effectSessionCts = new CancellationTokenSource();
        try { previous.Cancel(); } catch { /* already disposed */ }
        previous.Dispose();
    }

    private void FireEffect(Application.Lighting.IngameEffectRule rule)
    {
        if (rule.MediaPath is { Length: > 0 })
        {
            _surfaces.PlayMediaEffect(rule.MediaPath, rule.MediaFullscreen, rule.DurationMs);
            if (rule.Kind == Application.Lighting.IngameEffectKind.Sprite && rule.Sprite == null) return;
        }
        _surfaces.TriggerIngameEffect(rule);
    }

    private Task LoadLayoutAsync(string rom, CancellationToken cancellationToken)
    {
        if (!_config.LayEnabled || cancellationToken.IsCancellationRequested) return Task.CompletedTask;

        // CDC §26.3: the rbmarquee lighting scene owns the marquee, but the .lay
        // keeps driving the DMD with its purpose-built 128x32 view
        var lightingOwnsMarquee = _config.LightingEnabled &&
            File.Exists(Path.Combine(_config.BaseDirectory, "resources", "rbmarquee", rom + ".xml"));

        var path = Path.Combine(_config.LayDofPath, rom, "default.lay");
        if (!File.Exists(path)) path = ResolveAliasLayout(rom) ?? path;
        if (!File.Exists(path)) return Task.CompletedTask;
        var layout = MameLayParser.Parse(path);
        foreach (var warning in layout.Warnings) _logger.LogWarning("MAME layout {Path}: {Warning}", path, warning);
        if (layout.Views.Count == 0)
        {
            _logger.LogWarning("MAME layout contains no supported views: {Path}", path);
            return Task.CompletedTask;
        }
        if (lightingOwnsMarquee)
            _logger.LogInformation("Legacy .lay for {Rom}: DMD view only (rbmarquee scene owns the marquee)", rom);
        _lay.LoadMameLayout(layout, Path.GetDirectoryName(path)!, rom, dmdOnly: lightingOwnsMarquee);
        return Task.CompletedTask;
    }

    private string? ResolveAliasLayout(string rom)
    {
        var path = Path.Combine(_config.LayDofPath, "aliases.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var property in document.RootElement.EnumerateObject())
                    if (property.Name.Equals(rom, StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(_config.LayDofPath, property.Value.GetString() ?? string.Empty, "default.lay");
        }
        catch (Exception ex) { _logger.LogWarning("Invalid DOF aliases.json: {Message}", ex.Message); }
        return null;
    }

    private string ExtractRom(JsonElement payload)
    {
        var direct = Text(payload, "Rom", "rom", "MachineName", "machineName");
        if (direct.Length > 0) return Path.GetFileNameWithoutExtension(direct);
        foreach (var name in new[] { "Selection", "selection", "Running", "running" })
        {
            var child = Child(payload, name);
            var longName = Text(child, "LongName", "longName");
            if (longName.Length > 0) return longName;
            var path = Text(child, "GamePath", "gamePath");
            if (path.Length > 0) return Path.GetFileNameWithoutExtension(path);
        }
        var context = Child(payload, "Context", "context");
        var selected = Child(context, "Selected", "selected");
        if (selected.ValueKind == JsonValueKind.Undefined)
        {
            var ui = Child(context, "Ui", "ui");
            selected = Child(ui, "Selected", "selected");
        }
        var selectedPath = Text(selected, "GamePath", "gamePath");
        if (selectedPath.Length > 0) return Path.GetFileNameWithoutExtension(selectedPath);
        return string.Empty;
    }

    private string ExtractSystem(JsonElement payload)
    {
        var direct = Text(payload, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform", "Collection", "collection");
        if (direct.Length > 0) return NormalizeSystem(direct);
        foreach (var name in new[] { "Selection", "selection", "Selected", "selected", "Running", "running", "Game", "game" })
        {
            var child = Child(payload, name);
            var system = Text(child, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform");
            if (system.Length > 0) return NormalizeSystem(system);
            var path = Text(child, "GamePath", "gamePath", "Path", "path");
            var fromPath = SystemFromPath(path);
            if (fromPath.Length > 0) return fromPath;
        }
        var context = Child(payload, "Context", "context");
        foreach (var name in new[] { "Selected", "selected", "Running", "running", "Ui", "ui" })
        {
            var child = Child(context, name);
            var system = ExtractSystemFromObject(child);
            if (system.Length > 0) return system;
        }
        return SystemFromPath(Text(payload, "GamePath", "gamePath", "Path", "path"));
    }

    private static string ExtractSystemFromObject(JsonElement source)
    {
        var system = Text(source, "System", "system", "SystemName", "systemName", "SystemId", "systemId", "Platform", "platform");
        if (system.Length > 0) return NormalizeSystem(system);
        return SystemFromPath(Text(source, "GamePath", "gamePath", "Path", "path"));
    }

    private static string SystemFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var parts = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("roms", StringComparison.OrdinalIgnoreCase))
                return NormalizeSystem(parts[i + 1]);
        return string.Empty;
    }

    private static string NormalizeSystem(string value)
        => value.Trim().Trim('"').ToLowerInvariant();

    private static string NormalizeRom(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : Path.GetFileNameWithoutExtension(value.Trim().Trim('"')).ToLowerInvariant();

    /// <summary>
    /// Reads the canonical asset tables APIExpose now publishes alongside the legacy
    /// named fields. Keys are MediaKinds ("box-3d", "mixrbv2"…), and the system table is
    /// kept under a "system:" prefix: the legacy fields silently fall back from the game
    /// to its system, so a path alone never said whose art it was. Here the two scopes
    /// stay apart, and a key is present only when the entry really owns that medium.
    /// </summary>
    private void ReadAssetTables(JsonElement payload, Dictionary<string, string?> kinds)
    {
        Read(Child(payload, "Assets", "assets"), string.Empty);
        Read(Child(payload, "SystemAssets", "systemAssets"), "system:");

        void Read(JsonElement table, string prefix)
        {
            if (table.ValueKind != JsonValueKind.Object) return;
            foreach (var asset in table.EnumerateObject())
            {
                var path = ResolveLocal(Text(asset.Value, "Path", "path"));
                if (path != null) kinds[prefix + asset.Name] = path;
            }
        }
    }

    private string? MediaPath(JsonElement source, string name)
    {
        var node = Child(source, name, name.ToLowerInvariant());
        return ResolveLocal(node.ValueKind == JsonValueKind.String ? node.GetString() ?? string.Empty : Text(node, "Path", "path"));
    }

    private string? FirstAnimation(JsonElement dmd)
    {
        var animations = Child(dmd, "Animations", "animations");
        if (animations.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in animations.EnumerateArray())
        {
            var path = ResolveLocal(item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : Text(item, "Path", "path"));
            if (path != null) return path;
        }
        return null;
    }

    private string? ResolveLocal(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Uri.TryCreate(value, UriKind.Absolute, out var uri) && !uri.IsFile) return null;
        var path = Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(_config.BaseDirectory, "..", "APIExpose", value));
        return File.Exists(path) ? path : null;
    }

    private static JsonElement Payload(JsonElement root)
    {
        var payload = Child(root, "Payload", "payload");
        return payload.ValueKind == JsonValueKind.Undefined ? root : payload;
    }
    private static JsonElement Child(JsonElement source, params string[] names)
    {
        if (source.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in source.EnumerateObject())
            if (names.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return property.Value;
        return default;
    }
    private static string Text(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? string.Empty : value.ToString();
    }
    private static double? Number(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result)) return result;
        return value.ValueKind == JsonValueKind.String
               && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out result)
            ? result
            : null;
    }

    private static bool Boolean(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };
    }

    private static int? Integer(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result) ? result : null;
    }
}
