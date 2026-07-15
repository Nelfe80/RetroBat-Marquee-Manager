using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarqueeManager.Setup.Data;

/// <summary>One effect binding, mirror of the runtime's rule grammar
/// (ingame.effects.xml attributes). Null effect = signal explicitly silenced.</summary>
public sealed class EffectRule
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "flash";
    [JsonPropertyName("color")] public string Color { get; set; } = "#ff2a18";
    [JsonPropertyName("durationMs")] public int DurationMs { get; set; } = 300;
    [JsonPropertyName("dip")] public double Dip { get; set; }
    [JsonPropertyName("sprite")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sprite { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
    [JsonPropertyName("motion")] public string Motion { get; set; } = "pop";
    [JsonPropertyName("throttleMs")] public int ThrottleMs { get; set; } = 400;
    [JsonPropertyName("off")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Off { get; set; }
}

/// <summary>
/// Sparse effect overrides consumed by the runtime's IngameEffectLibrary
/// (schema marqueemanager.effects-override.v1). Layers, strongest first:
/// overrides\effects\&lt;system&gt;\&lt;rom&gt;.json, overrides\effects\&lt;system&gt;.json,
/// overrides\effects\genres\&lt;slug&gt;.json. Only the rules the user touched are
/// stored; a file whose last rule is removed is deleted (LedManager pattern).
/// Keys are exact .MEM action names, or "family:prefix".
/// </summary>
public sealed class EffectsOverrideStore
{
    private const string Schema = "marqueemanager.effects-override.v1";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root;

    public EffectsOverrideStore(string pluginRoot)
    {
        _root = Path.Combine(pluginRoot, "overrides", "effects");
    }

    public string GamePath(string system, string rom) => Path.Combine(_root, SafeName(system), SafeName(rom) + ".json");
    public string SystemPath(string system) => Path.Combine(_root, SafeName(system) + ".json");
    public string GenrePath(string slug) => Path.Combine(_root, "genres", SafeName(slug) + ".json");

    public Dictionary<string, EffectRule> LoadGame(string system, string rom) => LoadRules(GamePath(system, rom));
    public Dictionary<string, EffectRule> LoadSystem(string system) => LoadRules(SystemPath(system));
    public Dictionary<string, EffectRule> LoadGenre(string slug) => LoadRules(GenrePath(slug));

    public void SaveGame(string system, string rom, Dictionary<string, EffectRule> rules)
        => SaveRules(GamePath(system, rom), rules, keepOtherSections: true);

    public void SaveSystem(string system, Dictionary<string, EffectRule> rules)
        => SaveRules(SystemPath(system), rules, keepOtherSections: true);

    public void SaveGenre(string slug, Dictionary<string, EffectRule> rules)
        => SaveRules(GenrePath(slug), rules, keepOtherSections: true);

    // ---- the per-game file also hosts the "lighting" section (bulb/cabinet profile) ----

    public (string? Bulb, string? Cabinet) LoadLightingProfile(string system, string rom)
    {
        try
        {
            var path = GamePath(system, rom);
            if (!File.Exists(path)) return (null, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("lighting", out var lighting) || lighting.ValueKind != JsonValueKind.Object)
                return (null, null);
            return (
                lighting.TryGetProperty("bulb", out var bulb) && bulb.ValueKind == JsonValueKind.String ? bulb.GetString() : null,
                lighting.TryGetProperty("cabinet", out var cab) && cab.ValueKind == JsonValueKind.String ? cab.GetString() : null);
        }
        catch
        {
            return (null, null);
        }
    }

    public void SaveLightingProfile(string system, string rom, string? bulb, string? cabinet)
    {
        var path = GamePath(system, rom);
        var document = ReadDocument(path);
        if (bulb == null && cabinet == null)
        {
            document.Remove("lighting");
        }
        else
        {
            var lighting = new Dictionary<string, object>();
            if (bulb != null) lighting["bulb"] = bulb;
            if (cabinet != null) lighting["cabinet"] = cabinet;
            document["lighting"] = lighting;
        }
        WriteOrDelete(path, document);
    }

    // ================= internals =================

    private static Dictionary<string, EffectRule> LoadRules(string path)
    {
        var rules = new Dictionary<string, EffectRule>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return rules;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("rules", out var set) || set.ValueKind != JsonValueKind.Object)
                return rules;
            foreach (var entry in set.EnumerateObject())
            {
                var rule = entry.Value.Deserialize<EffectRule>();
                if (rule != null) rules[entry.Name] = rule;
            }
        }
        catch
        {
            // unreadable layer: empty
        }
        return rules;
    }

    /// <summary>Rewrites only the "rules" section, preserving anything else in the
    /// file (e.g. "lighting"); deletes the file when nothing remains.</summary>
    private void SaveRules(string path, Dictionary<string, EffectRule> rules, bool keepOtherSections)
    {
        var document = keepOtherSections ? ReadDocument(path) : new Dictionary<string, object?>();
        if (rules.Count == 0)
        {
            document.Remove("rules");
        }
        else
        {
            document["rules"] = rules;
        }
        WriteOrDelete(path, document);
    }

    private static Dictionary<string, object?> ReadDocument(string path)
    {
        var document = new Dictionary<string, object?>();
        try
        {
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Name is "schema" or "generatedBy") continue;
                    document[property.Name] = property.Value.Clone();
                }
            }
        }
        catch
        {
            // corrupt file: start fresh (the .bak keeps the old content)
        }
        return document;
    }

    private static void WriteOrDelete(string path, Dictionary<string, object?> document)
    {
        var hasContent = document.Keys.Any(k => k is "rules" or "lighting") &&
                         document.Any(kv => kv.Value is not null);
        if (!hasContent)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // locked: leave it
            }
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            try
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        var ordered = new Dictionary<string, object?>
        {
            ["schema"] = Schema,
            ["generatedBy"] = "MarqueeManagerSetup"
        };
        foreach (var (key, value) in document)
        {
            ordered[key] = value;
        }
        File.WriteAllText(path, JsonSerializer.Serialize(ordered, JsonOptions));
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
