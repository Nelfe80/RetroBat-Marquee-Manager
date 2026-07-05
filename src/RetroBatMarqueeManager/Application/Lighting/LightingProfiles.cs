using System.Xml.Linq;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>Metadata received with the marquee (WS Selection), input of the profile resolver.</summary>
public sealed record LightingSceneMeta(int? Year, string? Developer, string? Publisher, string? GameName, string? System, string? Rom = null);

public enum BulbTechnology { Incandescent, Fluorescent, Neon, Led, El, Lcd }

/// <summary>A bulb type from the bulbs library (CDC §13): its light and its temperament.</summary>
public sealed record BulbProfile(
    string Id,
    BulbTechnology Technology,
    float ColorR, float ColorG, float ColorB,
    double IgnitionScale,
    double EventRateScale,
    double FlickerAmount,
    double HumAmount,
    double WarmupSeconds)
{
    /// <summary>Instant-on solid-state light: no strikes, no warmup, no life events.</summary>
    public bool SolidState => Technology is BulbTechnology.Led or BulbTechnology.Lcd or BulbTechnology.El;
}

/// <summary>The §15 resolution output: which bulb, how aged, and why (provenance).</summary>
public sealed record ResolvedLightProfile(BulbProfile Bulb, double Aging, string Source);

/// <summary>
/// Community-editable lighting knowledge (CDC §15.4 / §18.2): bulb types and
/// cabinet-era profiles loaded from resources/lighting/*.xml, with safe built-in
/// defaults when files are missing or invalid — a bad library never crashes (§20.6).
/// Cabinet match grammar (v1.1): year:1980-1992, developer:capcom|capcom co ltd,
/// publisher:taito, name:vewlix|big blue (substring in the game name), plus legacy
/// system:/source:/manufacturer:. Most specific wins: name > developer > publisher
/// > year; ties resolved by file order (first profile wins).
/// </summary>
public sealed class LightingLibraries
{
    private readonly Dictionary<string, BulbProfile> _bulbs = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CabinetRule> _rules = new();
    private readonly BulbProfile _defaultBulb;

    private sealed record CabinetRule(
        int? YearFrom, int? YearTo,
        string[]? Developers,
        string[]? Publishers,
        string[]? Names,
        HashSet<string>? Systems,
        string? Source,
        string BulbId,
        double Aging,
        int Score,
        string Label);

    private LightingLibraries()
    {
        foreach (var bulb in BuiltInBulbs()) _bulbs[bulb.Id] = bulb;
        _defaultBulb = _bulbs["fluorescent_tube_warm_18w"];
    }

    public static LightingLibraries Load(string directory, ILogger logger)
    {
        var libraries = new LightingLibraries();
        libraries.LoadBulbs(Path.Combine(directory, "bulbs.xml"), logger);
        libraries.LoadCabinets(Path.Combine(directory, "cabinets.xml"), logger);
        if (libraries._rules.Count == 0)
        {
            logger.LogWarning("No cabinet profiles loaded from {Dir}; using built-in defaults", directory);
            libraries.LoadCabinetRulesFromXml(XDocument.Parse(BuiltInCabinetsXml), "builtin", logger);
        }
        return libraries;
    }

    public ResolvedLightProfile Resolve(LightingSceneMeta? meta, bool composited)
    {
        var developer = Normalize(meta?.Developer);
        var publisher = Normalize(meta?.Publisher);
        var gameName = Normalize(meta?.GameName);
        var system = meta?.System?.Trim().ToLowerInvariant();
        var source = composited ? "composited" : "scanned";

        CabinetRule? best = null;
        foreach (var rule in _rules)
        {
            if (rule.YearFrom != null && (meta?.Year == null || meta.Year < rule.YearFrom || meta.Year > rule.YearTo)) continue;
            if (rule.Developers != null && !MatchesAny(developer, rule.Developers)) continue;
            if (rule.Publishers != null && !MatchesAny(publisher, rule.Publishers)) continue;
            if (rule.Names != null && !MatchesAny(gameName, rule.Names)) continue;
            if (rule.Systems != null && (system == null || !rule.Systems.Contains(system))) continue;
            if (rule.Source != null && !rule.Source.Equals(source, StringComparison.OrdinalIgnoreCase)) continue;
            // strictly greater: on equal score the first profile in file order wins
            if (best == null || rule.Score > best.Score) best = rule;
        }

        if (best == null)
            return new ResolvedLightProfile(_defaultBulb, 0.15, "default");
        var bulb = _bulbs.TryGetValue(best.BulbId, out var found) ? found : _defaultBulb;
        return new ResolvedLightProfile(bulb, best.Aging, best.Label);
    }

    /// <summary>Normalized substring match: any alternative contained in the field.</summary>
    private static bool MatchesAny(string? field, string[] alternatives)
    {
        if (string.IsNullOrEmpty(field)) return false;
        foreach (var alternative in alternatives)
            if (field.Contains(alternative, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>lowercase, punctuation → space, compressed spaces (v1.1 fieldMapping).</summary>
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var chars = new char[raw.Length];
        var length = 0;
        var lastWasSpace = true;
        foreach (var c in raw.Trim())
        {
            var mapped = char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ';
            if (mapped == ' ' && lastWasSpace) continue;
            chars[length++] = mapped;
            lastWasSpace = mapped == ' ';
        }
        while (length > 0 && chars[length - 1] == ' ') length--;
        return length > 0 ? new string(chars, 0, length) : null;
    }

    private void LoadBulbs(string path, ILogger logger)
    {
        try
        {
            if (!File.Exists(path)) return;
            var count = 0;
            foreach (var element in XDocument.Load(path).Descendants("bulb"))
            {
                var id = (string?)element.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var technology = ParseTechnology((string?)element.Attribute("technology"));
                var (r, g, b) = ParseColor((string?)element.Attribute("color"), 1f, 0.96f, 0.88f);
                _bulbs[id] = new BulbProfile(id, technology, r, g, b,
                    (double?)element.Attribute("ignitionScale") ?? 1.0,
                    (double?)element.Attribute("eventRate") ?? DefaultEventRate(technology),
                    (double?)element.Attribute("flickerAmount") ?? DefaultFlicker(technology),
                    (double?)element.Attribute("humAmount") ?? DefaultHum(technology),
                    ParseDouble(element.Attribute("warmupSeconds")) ?? 0);
                count++;
            }
            logger.LogInformation("Bulb library loaded: {Count} bulb(s) from {Path}", count, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid bulbs library {Path}; built-in defaults kept", path);
        }
    }

    private static double? ParseDouble(XAttribute? attribute)
        => double.TryParse((string?)attribute, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;

    private static BulbTechnology ParseTechnology(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "incandescent" => BulbTechnology.Incandescent,
        "neon" => BulbTechnology.Neon,
        "led" => BulbTechnology.Led,
        "el" => BulbTechnology.El,
        "lcd" => BulbTechnology.Lcd,
        _ => BulbTechnology.Fluorescent
    };

    private static double DefaultEventRate(BulbTechnology tech) => tech switch
    {
        BulbTechnology.Led or BulbTechnology.Lcd => 0,
        BulbTechnology.El => 0.1,
        BulbTechnology.Incandescent => 0.4,
        BulbTechnology.Neon => 1.2,
        _ => 1.0
    };

    private static double DefaultFlicker(BulbTechnology tech) => tech switch
    {
        BulbTechnology.Led or BulbTechnology.Lcd => 0,
        BulbTechnology.El => 0.004,
        BulbTechnology.Incandescent => 0.008,
        _ => 0.012
    };

    private static double DefaultHum(BulbTechnology tech) => tech switch
    {
        BulbTechnology.Led or BulbTechnology.Lcd => 0,
        BulbTechnology.El => 0.06,
        BulbTechnology.Incandescent => 0.05,
        BulbTechnology.Neon => 0.30,
        _ => 0.20
    };

    private void LoadCabinets(string path, ILogger logger)
    {
        try
        {
            if (!File.Exists(path)) return;
            LoadCabinetRulesFromXml(XDocument.Load(path), Path.GetFileName(path), logger);
            logger.LogInformation("Cabinet profiles loaded: {Count} rule(s) from {Path}", _rules.Count, path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid cabinets library {Path}; built-in defaults kept", path);
        }
    }

    private void LoadCabinetRulesFromXml(XDocument document, string origin, ILogger logger)
    {
        foreach (var element in document.Descendants("profile"))
        {
            var bulbId = (string?)element.Attribute("bulb");
            if (string.IsNullOrWhiteSpace(bulbId)) continue;
            var match = ((string?)element.Attribute("match") ?? string.Empty).Trim();
            int? yearFrom = null, yearTo = null;
            string[]? developers = null, publishers = null, names = null;
            HashSet<string>? systems = null;
            string? source = null;
            var score = 0;

            foreach (var clause in match.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var split = clause.IndexOf(':');
                if (split <= 0) continue;
                var key = clause[..split].ToLowerInvariant();
                var value = clause[(split + 1)..].Trim();
                switch (key)
                {
                    case "year":
                        var range = value.Split('-');
                        if (int.TryParse(range[0], out var from)) yearFrom = from;
                        yearTo = range.Length > 1 && int.TryParse(range[1], out var to) ? to : yearFrom;
                        score += 10;
                        break;
                    case "name":
                        names = SplitAlternatives(value);
                        score += 40;
                        break;
                    case "developer":
                        developers = SplitAlternatives(value);
                        score += 30;
                        break;
                    case "publisher":
                        publishers = SplitAlternatives(value);
                        score += 20;
                        break;
                    case "manufacturer": // legacy alias: matches developer OR publisher fields
                        developers = SplitAlternatives(value);
                        score += 30;
                        break;
                    case "system":
                        systems = value.ToLowerInvariant().Split('|', ';').Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        score += 25;
                        break;
                    case "source":
                        source = value.ToLowerInvariant();
                        score += 5;
                        break;
                }
            }
            var id = (string?)element.Attribute("id");
            _rules.Add(new CabinetRule(yearFrom, yearTo, developers, publishers, names, systems, source, bulbId,
                (double?)element.Attribute("aging") ?? 0.15, score,
                id ?? (match.Length == 0 ? $"{origin}:default" : $"{origin}:{match}")));
        }
    }

    private static string[] SplitAlternatives(string value)
        => value.Split('|', ';')
            .Select(v => Normalize(v))
            .Where(v => v != null)
            .Select(v => v!)
            .ToArray();

    private static (float R, float G, float B) ParseColor(string? hex, float r, float g, float b)
    {
        if (string.IsNullOrWhiteSpace(hex)) return (r, g, b);
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return (r, g, b);
        return ((value >> 16 & 0xFF) / 255f, (value >> 8 & 0xFF) / 255f, (value & 0xFF) / 255f);
    }

    private static IEnumerable<BulbProfile> BuiltInBulbs()
    {
        yield return new BulbProfile("incandescent_warm_12v_5w", BulbTechnology.Incandescent, 1f, 0.84f, 0.62f, 0.35, 0.4, 0.008, 0.05, 0.4);
        yield return new BulbProfile("fluorescent_tube_warm_18w", BulbTechnology.Fluorescent, 1f, 0.96f, 0.86f, 1.0, 1.0, 0.012, 0.20, 2.0);
        yield return new BulbProfile("fluorescent_tube_cool_18w", BulbTechnology.Fluorescent, 0.93f, 0.96f, 1f, 0.9, 0.8, 0.010, 0.18, 1.8);
        yield return new BulbProfile("neon_sign", BulbTechnology.Neon, 1f, 0.90f, 0.95f, 1.6, 1.2, 0.015, 0.30, 2.5);
        yield return new BulbProfile("led_white_fast", BulbTechnology.Led, 0.96f, 0.98f, 1f, 1.0, 0, 0, 0, 0);
    }

    private const string BuiltInCabinetsXml = @"
<cabinetProfiles version='1.0'>
  <profile match='source:composited' bulb='led_white_fast' aging='0' />
  <profile match='year:1970-1979' bulb='incandescent_warm_12v_5w' aging='0.35' />
  <profile match='year:1980-1992' bulb='fluorescent_tube_warm_18w' aging='0.20' />
  <profile match='year:1993-2010' bulb='fluorescent_tube_cool_18w' aging='0.08' />
  <profile match='' bulb='fluorescent_tube_warm_18w' aging='0.15' />
</cabinetProfiles>";
}
