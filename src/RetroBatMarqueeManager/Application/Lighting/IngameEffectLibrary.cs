using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

public enum IngameEffectKind { Flash, Pulse, PowerCycle, Blackout, Sprite, Shake, Strobe, Tint }

/// <summary>One rule of the ingame effects library (resources/lighting/ingame.effects.xml).
/// A rule can combine a glass flash AND sprites (sprite attr on any kind).
/// `DelayMs` sequences stacked actions (0 = fires with the signal); `MediaPath`
/// carries a user-dropped effect media (webm/gif) with `MediaFullscreen`.</summary>
public sealed record IngameEffectRule(
    IngameEffectKind Kind,
    SKColor Color,
    int DurationMs,
    float Dip,
    int ThrottleMs,
    string Label,
    string? Sprite = null,
    int Count = 1,
    string Motion = "pop",
    SKColor? TrailColor = null,
    int DelayMs = 0,
    string? MediaPath = null,
    bool MediaFullscreen = false);

/// <summary>
/// Maps semantic .mem actions from the ws/ingame stream to light effects (CDC §18),
/// resolved through layers: game override &gt; system override &gt; genre override
/// (user) &gt; genre-scoped library rule &gt; generic library rule. The library XML is
/// editable; overrides are sparse JSON written by MarqueeManagerSetup
/// (overrides\effects\&lt;system&gt;.json, &lt;system&gt;\&lt;rom&gt;.json, genres\&lt;slug&gt;.json,
/// schema marqueemanager.effects-override.v1, "off": true silences a signal).
/// The context follows the enriched marquee stream (system/rom/genre slugs).
/// Action match is a case-insensitive contains on alternatives (exact key for
/// JSON rules), family match is a prefix; the first matching rule in layer then
/// file order wins. Missing/invalid file → layer skipped, never fatal (§20.6).
/// </summary>
public sealed class IngameEffectLibrary
{
    private sealed record Rule(string[]? Actions, string? FamilyPrefix, IReadOnlyList<IngameEffectRule>? Effects, string[]? Genres = null, bool ExactActions = false);

    private readonly List<Rule> _rules = new();
    private readonly Dictionary<string, long> _lastFired = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private readonly object _contextSync = new();
    private List<Rule> _gameRules = new();
    private List<Rule> _systemRules = new();
    private List<Rule> _genreRules = new();
    private string[] _genreSlugs = Array.Empty<string>();
    private string? _contextKey;
    /// <summary>Per-game effect policy: inherit (default) | custom-only | off.</summary>
    private string _policy = "inherit";
    private string _libraryRoot = "";

    public static IngameEffectLibrary Load(string directory, ILogger logger)
    {
        var library = new IngameEffectLibrary();
        var path = Path.Combine(directory, "ingame.effects.xml");
        try
        {
            if (File.Exists(path))
            {
                library.LoadRules(XDocument.Load(path));
                logger.LogInformation("Ingame effects library loaded: {Count} rule(s) from {Path}", library._rules.Count, path);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid ingame effects library {Path}; built-in defaults kept", path);
        }
        if (library._rules.Count == 0) library.LoadRules(XDocument.Parse(BuiltInXml));
        return library;
    }

    /// <summary>
    /// Follows the displayed game: loads its sparse override layers and remembers
    /// its genre slugs for the genre-scoped library rules. Cheap when the game
    /// did not change. MAME sets are exposed as "arcade" by ES — both spellings
    /// locate the same override files.
    /// </summary>
    public void SetContext(string? system, string? rom, IReadOnlyList<string> genreSlugs, string overridesRoot, ILogger logger)
    {
        var key = $"{system}|{rom}|{string.Join(',', genreSlugs)}";
        lock (_contextSync)
        {
            if (key == _contextKey) return;
            _contextKey = key;
            _genreSlugs = genreSlugs.ToArray();
            _gameRules = new List<Rule>();
            _systemRules = new List<Rule>();
            _genreRules = new List<Rule>();
            _policy = "inherit";
            // named effect library lives beside the user media: media\effects\
            _libraryRoot = Path.GetFullPath(Path.Combine(overridesRoot, "..", "..", "media", "effects"));

            try
            {
                foreach (var candidateSystem in SystemSpellings(system))
                {
                    var safeSystem = SafeName(candidateSystem);
                    if (rom is { Length: > 0 } && _gameRules.Count == 0)
                        (_gameRules, _policy) = LoadOverrideFile(Path.Combine(overridesRoot, safeSystem, SafeName(rom) + ".json"), logger);
                    if (_systemRules.Count == 0)
                        (_systemRules, _) = LoadOverrideFile(Path.Combine(overridesRoot, safeSystem + ".json"), logger);
                }

                foreach (var slug in genreSlugs)
                {
                    _genreRules.AddRange(LoadOverrideFile(Path.Combine(overridesRoot, "genres", SafeName(slug) + ".json"), logger).Rules);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Effect override layers unavailable for {Key}", key);
            }

            if (_gameRules.Count + _systemRules.Count + _genreRules.Count > 0 || genreSlugs.Count > 0)
            {
                logger.LogInformation(
                    "Effect context {System}/{Rom}: {Game} game, {Sys} system, {Genre} genre rule(s), genres [{Slugs}]",
                    system, rom, _gameRules.Count, _systemRules.Count, _genreRules.Count, string.Join(',', genreSlugs));
            }
        }
    }

    /// <summary>Resolve an action/family to a SEQUENCE of effect actions (delays
    /// included), applying per-rule throttling. Empty = nothing fires (no match,
    /// throttled, explicitly off, or game policy). Policies: `off` silences the
    /// game entirely; `custom-only` only fires the game's own allocations.</summary>
    public IReadOnlyList<IngameEffectRule> Resolve(string action, string? family)
    {
        List<Rule> game, system, genre;
        string[] slugs;
        string policy;
        lock (_contextSync)
        {
            game = _gameRules;
            system = _systemRules;
            genre = _genreRules;
            slugs = _genreSlugs;
            policy = _policy;
        }

        if (policy.Equals("off", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<IngameEffectRule>();

        if (FindMatch(game, action, family, requireGenre: null) is { } gameRule)
            return Throttled(gameRule);
        if (policy.Equals("custom-only", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<IngameEffectRule>();

        // override layers first, then the library: genre-scoped rules beat generic ones
        foreach (var layer in new[] { system, genre })
        {
            if (FindMatch(layer, action, family, requireGenre: null) is { } rule)
                return Throttled(rule);
        }
        if (FindMatch(_rules, action, family, requireGenre: slugs) is { } scoped)
            return Throttled(scoped);
        if (FindMatch(_rules, action, family, requireGenre: Array.Empty<string>()) is { } generic)
            return Throttled(generic);
        return Array.Empty<IngameEffectRule>();
    }

    /// <summary>requireGenre: null = layer already scoped (overrides); empty = only
    /// genre-less rules; otherwise only rules scoped to one of these slugs.</summary>
    private static Rule? FindMatch(List<Rule> rules, string action, string? family, string[]? requireGenre)
    {
        foreach (var rule in rules)
        {
            if (requireGenre != null)
            {
                if (requireGenre.Length == 0 && rule.Genres != null) continue;
                if (requireGenre.Length > 0 && (rule.Genres == null
                    || !rule.Genres.Any(g => requireGenre.Contains(g, StringComparer.OrdinalIgnoreCase)))) continue;
            }

            var matches = rule.Actions != null
                ? rule.Actions.Any(a => rule.ExactActions
                    ? action.Equals(a, StringComparison.OrdinalIgnoreCase)
                    : action.Contains(a, StringComparison.OrdinalIgnoreCase))
                : family != null && rule.FamilyPrefix != null &&
                  family.StartsWith(rule.FamilyPrefix, StringComparison.OrdinalIgnoreCase);
            if (matches) return rule;
        }
        return null;
    }

    private IReadOnlyList<IngameEffectRule> Throttled(Rule rule)
    {
        if (rule.Effects is not { Count: > 0 }) return Array.Empty<IngameEffectRule>(); // "off"

        // one throttle for the whole sequence (its first action carries the label)
        var head = rule.Effects[0];
        var now = _clock.ElapsedMilliseconds;
        lock (_lastFired)
        {
            if (_lastFired.TryGetValue(head.Label, out var last) && now - last < head.ThrottleMs)
                return Array.Empty<IngameEffectRule>();
            _lastFired[head.Label] = now;
        }
        return rule.Effects;
    }

    private void LoadRules(XDocument document)
    {
        foreach (var element in document.Descendants("effect"))
        {
            var actionsRaw = (string?)element.Attribute("action");
            var family = (string?)element.Attribute("family");
            if (actionsRaw == null && family == null) continue;
            var actions = actionsRaw?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var genres = ((string?)element.Attribute("genre"))
                ?.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var label = (genres is { Length: > 0 } ? $"[{genres[0]}]" : "") + (actionsRaw ?? $"family:{family}");
            _rules.Add(new Rule(actions, family, new[] { ParseEffect(element, label) }, genres is { Length: > 0 } ? genres : null));
        }
    }

    private static IngameEffectRule ParseEffect(XElement element, string label)
    {
        var kind = ((string?)element.Attribute("kind"))?.ToLowerInvariant() switch
        {
            "pulse" => IngameEffectKind.Pulse,
            "powercycle" => IngameEffectKind.PowerCycle,
            "blackout" => IngameEffectKind.Blackout,
            "sprite" => IngameEffectKind.Sprite,
            "shake" => IngameEffectKind.Shake,
            "strobe" => IngameEffectKind.Strobe,
            "tint" => IngameEffectKind.Tint,
            _ => IngameEffectKind.Flash
        };
        var trailRaw = (string?)element.Attribute("trail");
        return new IngameEffectRule(kind,
            ParseColor((string?)element.Attribute("color")),
            (int?)element.Attribute("durationMs") ?? 300,
            (float)((double?)element.Attribute("dip") ?? 0.0),
            (int?)element.Attribute("throttleMs") ?? 400,
            label,
            (string?)element.Attribute("sprite"),
            Math.Clamp((int?)element.Attribute("count") ?? 1, 1, 8),
            ((string?)element.Attribute("motion") ?? "pop").ToLowerInvariant(),
            trailRaw != null ? ParseColor(trailRaw) : null);
    }

    /// <summary>Sparse JSON override layer (schema marqueemanager.effects-override.v1):
    /// { "policy": "inherit"|"custom-only"|"off",
    ///   "rules": { "ACTION" | "family:prefix":
    ///       { kind, color, durationMs, dip, sprite, count, motion, throttleMs, delayMs, media, fullscreen }
    ///     | { "actions": [ {…}, {…} ] }          // stacked/sequenced actions
    ///     | { "effect": "Touché nucléaire" }      // named effect from media\effects\library.json
    ///     | { "off": true } } }.</summary>
    private (List<Rule> Rules, string Policy) LoadOverrideFile(string path, ILogger logger)
    {
        var rules = new List<Rule>();
        var policy = "inherit";
        try
        {
            if (!File.Exists(path)) return (rules, policy);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("policy", out var policyValue)
                && policyValue.ValueKind == JsonValueKind.String)
                policy = policyValue.GetString() ?? "inherit";

            if (!doc.RootElement.TryGetProperty("rules", out var ruleSet) || ruleSet.ValueKind != JsonValueKind.Object)
                return (rules, policy);

            foreach (var entry in ruleSet.EnumerateObject())
            {
                var key = entry.Name.Trim();
                if (key.Length == 0 || entry.Value.ValueKind != JsonValueKind.Object) continue;

                string[]? actions = null;
                string? familyPrefix = null;
                if (key.StartsWith("family:", StringComparison.OrdinalIgnoreCase))
                    familyPrefix = key["family:".Length..];
                else
                    actions = new[] { key };

                if (entry.Value.TryGetProperty("off", out var off) && off.ValueKind == JsonValueKind.True)
                {
                    rules.Add(new Rule(actions, familyPrefix, null, ExactActions: true));
                    continue;
                }

                var label = $"override:{Path.GetFileNameWithoutExtension(path)}:{key}";
                var effects = ParseRuleEffects(entry.Value, label, logger);
                if (effects.Count > 0)
                    rules.Add(new Rule(actions, familyPrefix, effects, ExactActions: true));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Invalid effect override {Path} skipped: {Message}", path, ex.Message);
        }
        return (rules, policy);
    }

    /// <summary>Single object, "actions" array, or "effect" library reference —
    /// all normalize to a sequence of actions sharing the rule's label/throttle.</summary>
    private List<IngameEffectRule> ParseRuleEffects(JsonElement value, string label, ILogger logger)
    {
        // named effect from the user library
        if (value.TryGetProperty("effect", out var reference) && reference.ValueKind == JsonValueKind.String)
        {
            var named = LookupLibraryEffect(reference.GetString() ?? "", logger);
            if (named.Count > 0)
            {
                // the whole sequence throttles on the head; keep the rule label
                return named.Select((action, index) => action with { Label = label + (index == 0 ? "" : $"#{index}") }).ToList();
            }
            return new List<IngameEffectRule>();
        }

        if (value.TryGetProperty("actions", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            var throttle = ReadInt(value, "throttleMs");
            var list = new List<IngameEffectRule>();
            var index = 0;
            foreach (var action in array.EnumerateArray())
            {
                if (action.ValueKind != JsonValueKind.Object) continue;
                var parsed = ParseJsonAction(action, label + (index == 0 ? "" : $"#{index}"));
                if (throttle != null && index == 0) parsed = parsed with { ThrottleMs = throttle.Value };
                list.Add(parsed);
                index++;
            }
            return list;
        }

        return new List<IngameEffectRule> { ParseJsonAction(value, label) };
    }

    private IngameEffectRule ParseJsonAction(JsonElement value, string label)
    {
        var media = ReadString(value, "media");
        string? mediaPath = null;
        if (media is { Length: > 0 })
        {
            mediaPath = Path.IsPathRooted(media) ? media : Path.Combine(_libraryRoot, "user", media);
        }

        return new IngameEffectRule(
            ReadString(value, "kind")?.ToLowerInvariant() switch
            {
                "pulse" => IngameEffectKind.Pulse,
                "powercycle" => IngameEffectKind.PowerCycle,
                "blackout" => IngameEffectKind.Blackout,
                "sprite" => IngameEffectKind.Sprite,
                "shake" => IngameEffectKind.Shake,
                "strobe" => IngameEffectKind.Strobe,
                "tint" => IngameEffectKind.Tint,
                _ when mediaPath != null => IngameEffectKind.Sprite, // media rides the overlay pipeline
                _ => IngameEffectKind.Flash
            },
            ParseColor(ReadString(value, "color")),
            ReadInt(value, "durationMs") ?? 300,
            (float)(ReadDouble(value, "dip") ?? 0.0),
            ReadInt(value, "throttleMs") ?? 400,
            label,
            ReadString(value, "sprite"),
            Math.Clamp(ReadInt(value, "count") ?? 1, 1, 8),
            ReadString(value, "motion")?.ToLowerInvariant() ?? "pop",
            null,
            ReadInt(value, "delayMs") ?? 0,
            mediaPath,
            value.TryGetProperty("fullscreen", out var fullscreen) && fullscreen.ValueKind == JsonValueKind.True);
    }

    private (string Path, DateTime Stamp, Dictionary<string, List<IngameEffectRule>> Effects) _libraryCache;

    /// <summary>media\effects\library.json — the user's named, reusable effect
    /// compositions: { "effects": { "<name>": { "actions": [ {…}, {…} ] } } }.</summary>
    private List<IngameEffectRule> LookupLibraryEffect(string name, ILogger logger)
    {
        var path = Path.Combine(_libraryRoot, "library.json");
        try
        {
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_libraryCache.Effects == null || _libraryCache.Path != path || _libraryCache.Stamp != stamp)
            {
                var effects = new Dictionary<string, List<IngameEffectRule>>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(path))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(path));
                    if (doc.RootElement.TryGetProperty("effects", out var set) && set.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var entry in set.EnumerateObject())
                        {
                            effects[entry.Name] = ParseRuleEffects(entry.Value, "fx:" + entry.Name, logger);
                        }
                    }
                }
                _libraryCache = (path, stamp, effects);
            }
            return _libraryCache.Effects!.TryGetValue(name, out var found) ? found : new List<IngameEffectRule>();
        }
        catch (Exception ex)
        {
            logger.LogWarning("Effect library unreadable ({Message}); named effect {Name} skipped", ex.Message, name);
            return new List<IngameEffectRule>();
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static double? ReadDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static IEnumerable<string> SystemSpellings(string? system)
    {
        if (string.IsNullOrEmpty(system)) yield break;
        yield return system;
        if (system.Equals("mame", StringComparison.OrdinalIgnoreCase)) yield return "arcade";
        else if (system.Equals("arcade", StringComparison.OrdinalIgnoreCase)) yield return "mame";
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }

    private static SKColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new SKColor(255, 40, 24);
        hex = hex.TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? new SKColor((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF))
            : new SKColor(255, 40, 24);
    }

    /// <summary>
    /// Couleur d'evenement portee par les .MEM arcade (deltas score : l'avion
    /// orange de 1944...). Noms simples normalises par le generateur ; null si
    /// inconnu, l'effet garde alors sa couleur de regle.
    /// </summary>
    public static SKColor? TryParseEventColor(string? name)
    {
        return (name ?? "").Trim().ToLowerInvariant() switch
        {
            "red" => new SKColor(255, 32, 21),
            "green" => new SKColor(53, 208, 115),
            "blue" => new SKColor(64, 128, 255),
            "orange" => new SKColor(255, 150, 30),
            "yellow" => new SKColor(255, 214, 60),
            "pink" => new SKColor(255, 63, 164),
            "cyan" => new SKColor(55, 224, 232),
            "white" => new SKColor(240, 240, 245),
            "silver" => new SKColor(190, 195, 205),
            _ => null,
        };
    }

    private const string BuiltInXml = @"
<ingameEffects version='1.0'>
  <effect action='LOSE_LIFE|KO|CRASH|HIT|DAMAGE' kind='flash' color='#ff2015' durationMs='300' dip='0.4' throttleMs='400' />
  <effect action='GAME_OVER' kind='blackout' durationMs='1600' throttleMs='4000' />
  <effect action='GAIN_LIFE|HEAL|COIN_GAIN|KEY_GET' kind='pulse' color='#ffffff' durationMs='200' throttleMs='500' />
</ingameEffects>";
}
