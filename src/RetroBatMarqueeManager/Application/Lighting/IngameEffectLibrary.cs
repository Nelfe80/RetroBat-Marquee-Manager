using System.Text.Json;
using System.Xml.Linq;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

public enum IngameEffectKind { Flash, Pulse, PowerCycle, Blackout, Sprite, Shake, Strobe, Tint }

/// <summary>One rule of the ingame effects library (resources/lighting/ingame.effects.xml).
/// A rule can combine a glass flash AND sprites (sprite attr on any kind).</summary>
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
    SKColor? TrailColor = null);

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
    private sealed record Rule(string[]? Actions, string? FamilyPrefix, IngameEffectRule? Effect, string[]? Genres = null, bool ExactActions = false);

    private readonly List<Rule> _rules = new();
    private readonly Dictionary<string, long> _lastFired = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    private readonly object _contextSync = new();
    private List<Rule> _gameRules = new();
    private List<Rule> _systemRules = new();
    private List<Rule> _genreRules = new();
    private string[] _genreSlugs = Array.Empty<string>();
    private string? _contextKey;

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

            try
            {
                foreach (var candidateSystem in SystemSpellings(system))
                {
                    var safeSystem = SafeName(candidateSystem);
                    if (rom is { Length: > 0 } && _gameRules.Count == 0)
                        _gameRules = LoadOverrideFile(Path.Combine(overridesRoot, safeSystem, SafeName(rom) + ".json"), logger);
                    if (_systemRules.Count == 0)
                        _systemRules = LoadOverrideFile(Path.Combine(overridesRoot, safeSystem + ".json"), logger);
                }

                foreach (var slug in genreSlugs)
                {
                    _genreRules.AddRange(LoadOverrideFile(Path.Combine(overridesRoot, "genres", SafeName(slug) + ".json"), logger));
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

    /// <summary>Resolve an action/family to a rule, applying per-rule throttling.
    /// Null = no effect (no match, throttled, or explicitly turned off).</summary>
    public IngameEffectRule? Resolve(string action, string? family)
    {
        List<Rule> game, system, genre;
        string[] slugs;
        lock (_contextSync)
        {
            game = _gameRules;
            system = _systemRules;
            genre = _genreRules;
            slugs = _genreSlugs;
        }

        // override layers first, then the library: genre-scoped rules beat generic ones
        foreach (var layer in new[] { game, system, genre })
        {
            if (FindMatch(layer, action, family, requireGenre: null) is { } rule)
                return Throttled(rule);
        }
        if (FindMatch(_rules, action, family, requireGenre: slugs) is { } scoped)
            return Throttled(scoped);
        if (FindMatch(_rules, action, family, requireGenre: Array.Empty<string>()) is { } generic)
            return Throttled(generic);
        return null;
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

    private IngameEffectRule? Throttled(Rule rule)
    {
        if (rule.Effect == null) return null; // "off": the signal is silenced on purpose

        var now = _clock.ElapsedMilliseconds;
        lock (_lastFired)
        {
            if (_lastFired.TryGetValue(rule.Effect.Label, out var last) && now - last < rule.Effect.ThrottleMs)
                return null;
            _lastFired[rule.Effect.Label] = now;
        }
        return rule.Effect;
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
            _rules.Add(new Rule(actions, family, ParseEffect(element, label), genres is { Length: > 0 } ? genres : null));
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
    /// { "rules": { "ACTION" | "family:prefix": { kind, color, durationMs, dip,
    /// sprite, count, motion, throttleMs } | { "off": true } } }.</summary>
    private static List<Rule> LoadOverrideFile(string path, ILogger logger)
    {
        var rules = new List<Rule>();
        try
        {
            if (!File.Exists(path)) return rules;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("rules", out var ruleSet) || ruleSet.ValueKind != JsonValueKind.Object)
                return rules;

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

                var effect = new IngameEffectRule(
                    ReadString(entry.Value, "kind")?.ToLowerInvariant() switch
                    {
                        "pulse" => IngameEffectKind.Pulse,
                        "powercycle" => IngameEffectKind.PowerCycle,
                        "blackout" => IngameEffectKind.Blackout,
                        "sprite" => IngameEffectKind.Sprite,
                        "shake" => IngameEffectKind.Shake,
                        "strobe" => IngameEffectKind.Strobe,
                        "tint" => IngameEffectKind.Tint,
                        _ => IngameEffectKind.Flash
                    },
                    ParseColor(ReadString(entry.Value, "color")),
                    ReadInt(entry.Value, "durationMs") ?? 300,
                    (float)(ReadDouble(entry.Value, "dip") ?? 0.0),
                    ReadInt(entry.Value, "throttleMs") ?? 400,
                    $"override:{Path.GetFileNameWithoutExtension(path)}:{key}",
                    ReadString(entry.Value, "sprite"),
                    Math.Clamp(ReadInt(entry.Value, "count") ?? 1, 1, 8),
                    ReadString(entry.Value, "motion")?.ToLowerInvariant() ?? "pop");
                rules.Add(new Rule(actions, familyPrefix, effect, ExactActions: true));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("Invalid effect override {Path} skipped: {Message}", path, ex.Message);
        }
        return rules;
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
