using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class WebSocketListenerService : BackgroundService
{
    private static readonly string[] Streams =
    {
        "arcade", "frontend", "marquee", "topper", "instruction-card", "panel", "hiscore",
        "retroachievements", "score", "timer", "ingame"
    };

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
    private bool _pinballDmdActive;
    private readonly Application.Lighting.IngameEffectLibrary _ingameEffects;

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
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.WhenAll(Streams.Select(stream => ListenAsync(stream, stoppingToken)));

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
                using var document = JsonDocument.Parse(message.ToArray());
                await ProcessAsync(stream, document.RootElement, cancellationToken);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning("Invalid JSON received on {Stream}: {Message}", stream, ex.Message);
            }
        }
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
                await HandleSimpleMediaAsync(root, "lcd", cancellationToken);
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
        var marquee = MediaPath(media, "Marquee") ?? MediaPath(media, "GeneratedMarquee") ?? MediaPath(media, "Logo");
        if (marquee != null)
        {
            var meta = ExtractLightingMeta(payload);
            foreach (var target in _config.GetTargetsForContent("marquee"))
                await _surfaces.DisplayMediaAsync(marquee, target, cancellationToken, meta);
        }

        var dmd = Child(media, "Dmd", "dmd");
        var generatedDmdPath = MediaPath(dmd, "Generated");
        var stillDmdPath = MediaPath(dmd, "Still");
        var dmdPath = FirstAnimation(dmd) ?? stillDmdPath ?? generatedDmdPath;
        if (dmdPath == null) return;
        // Keep the generated game DMD behind text even when an animation is preferred while idle.
        await _dmd.SetBaseMediaAsync(dmdPath, cancellationToken, generatedDmdPath ?? stillDmdPath ?? dmdPath);
        foreach (var target in _config.GetTargetsForContent("dmd"))
            await _surfaces.DisplayMediaAsync(dmdPath, target, cancellationToken);
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

        if (year == null && developer.Length == 0 && publisher.Length == 0 && gameName.Length == 0 && system.Length == 0)
            return null;
        return new Application.Lighting.LightingSceneMeta(year,
            developer.Length > 0 ? developer : null,
            publisher.Length > 0 ? publisher : null,
            gameName.Length > 0 ? gameName : null,
            system.Length > 0 ? system : null,
            rom.Length > 0 ? rom : null);
    }

    private async Task HandleTopperAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var media = Child(Payload(root), "Media", "media");
        var path = MediaPath(media, "Topper");
        if (path != null) foreach (var target in _config.GetTargetsForContent("topper")) await _surfaces.DisplayMediaAsync(path, target, cancellationToken);
    }

    private async Task HandleInstructionCardAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var cards = Child(payload, "Cards", "cards");
        if (cards.ValueKind != JsonValueKind.Array) return;
        // keep the whole catalog: the touch profile can cycle/show any of them
        var paths = new List<string>();
        foreach (var card in cards.EnumerateArray())
        {
            var path = ResolveLocal(Text(card, "Path", "path"));
            if (path != null) paths.Add(path);
        }

        if (paths.Count == 0) return;
        await _instructionCards.SetCardsAsync(paths, cancellationToken);
    }

    private async Task HandleSimpleMediaAsync(JsonElement root, string defaultTarget, CancellationToken cancellationToken)
    {
        var payload = Payload(root);
        var path = ResolveLocal(Text(payload, "Path", "path", "Value", "value"));
        var target = Text(payload, "Target", "target");
        if (path != null) await _surfaces.DisplayMediaAsync(path, target.Length == 0 ? defaultTarget : target.ToLowerInvariant(), cancellationToken);
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

        var score = Text(payload, "Score", "score", "Value", "value");
        var player = Text(payload, "Player", "player", "Name", "name");
        if (score.Length == 0)
        {
            var scores = Child(payload, "Scores", "scores");
            if (scores.ValueKind == JsonValueKind.Array)
            {
                var first = scores.EnumerateArray().FirstOrDefault();
                score = Text(first, "Score", "score", "Value", "value");
                player = Text(first, "Name", "name", "Player", "player");
            }
        }
        if (score.Length == 0) return;
        _surfaces.SetInformation("hiscore", "HIGH SCORE", $"{player} {score}".Trim(), null, true, 0);
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
            var selectedSystem = ExtractSystem(payload);
            var selectedRom = ExtractRom(payload);
            if (selectedSystem.Length > 0)
            {
                _selectedSystem = selectedSystem;
                _logger.LogInformation("Frontend selected system: {System}", selectedSystem);
            }
            if (selectedRom.Length > 0)
            {
                if (!string.Equals(_selectedRom, selectedRom, StringComparison.OrdinalIgnoreCase))
                    _surfaces.ClearInformation("hiscore");
                _selectedRom = selectedRom;
            }
            else
            {
                _selectedRom = null;
                _surfaces.ClearInformation("hiscore");
            }
            return;
        }

        if (type.Equals("ui.game.ended", StringComparison.OrdinalIgnoreCase) || type.Equals("ui.game.ended.raw", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Frontend game ended event received: {Type}", type);
            _lay.Clear();
            _presentation.MarkGameEnded();
            _runningRom = null;
            if (_pinballDmdActive) ReleasePinballDmd("game ended");
            // back to the frontend: sounds return, audible re-ignition
            _surfaces.SetLightingIngame(false);
            _surfaces.PowerCycleLighting();
            return;
        }
        if (!type.Equals("ui.game.started", StringComparison.OrdinalIgnoreCase) && !type.Equals("ui.game.started.raw", StringComparison.OrdinalIgnoreCase)) return;
        _presentation.MarkGameStarted();
        // game launch drama: silent power cycle — the play session stays clean
        _surfaces.SetLightingIngame(true);
        _surfaces.PowerCycleLighting();
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

        // flow lifecycle changes gate the speedrun leaderboard (no timer during demos)
        _presentation.OnGameplayFlow(action);

        var rule = _ingameEffects.Resolve(action, family.Length > 0 ? family : null);
        if (rule == null) return;
        _logger.LogInformation("Ingame action {Action} → lighting effect {Kind} ({Label})", action, rule.Kind, rule.Label);
        _surfaces.TriggerLightingEffect(rule);
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
    private static int? Integer(JsonElement source, params string[] names)
    {
        var value = Child(source, names);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out result) ? result : null;
    }
}
