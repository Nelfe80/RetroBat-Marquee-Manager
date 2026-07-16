using System.IO;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// Reader/writer of media\assignments.json (schema marqueemanager.compositions.v1,
/// consumed by the runtime's CompositionChainResolver): per CATEGORY (marquee,
/// topper, dmd) then per system, an ordered chain of sources. Also owns the
/// user drop folders (media\&lt;cat&gt;s\user\&lt;sys&gt;\) and their alias sidecars.
/// </summary>
public sealed class CompositionAssignments
{
    public const string Schema = "marqueemanager.compositions.v1";

    public static readonly string[] Categories = { "marquee", "topper", "dmd" };

    /// <summary>Source vocabulary per category (template:&lt;id&gt; entries are appended
    /// dynamically from the template library).</summary>
    public static string[] SourcesFor(string category) => category switch
    {
        "topper" => new[] { "composition", "user", "topper", "fanart", "logo" },
        "dmd" => new[] { "composition", "user", "animations", "still", "generated" },
        _ => new[] { "composition", "user", "marquee", "screenmarquee", "generated", "logo", "fanart" }
    };

    /// <summary>Default chains — MUST stay aligned with the runtime resolver.</summary>
    public static string[] DefaultChain(string category) => category switch
    {
        "topper" => new[] { "composition", "user", "topper" },
        "dmd" => new[] { "user", "animations", "still", "generated" },
        _ => new[] { "composition", "user", "marquee", "generated", "logo" }
    };

    private readonly string _pluginRoot;
    private readonly Dictionary<string, (List<string> Global, Dictionary<string, List<string>> Systems)> _categories = new(StringComparer.OrdinalIgnoreCase);

    public CompositionAssignments(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        Load();
    }

    public string DocumentPath => Path.Combine(_pluginRoot, "media", "assignments.json");

    public string UserFolder(string category, string system)
        => Path.Combine(_pluginRoot, "media", category + "s", "user", SafeName(system));

    public string CompositionPath(string category, string system, string? rom)
        => rom == null
            ? Path.Combine(_pluginRoot, "media", category + "s", "systems", SafeName(system) + ".png")
            : Path.Combine(_pluginRoot, "media", category + "s", SafeName(system), SafeName(rom) + ".png");

    /// <summary>The chain shown/edited for a system (falls back global → default).</summary>
    public List<string> ChainFor(string category, string? system)
    {
        if (_categories.TryGetValue(category, out var entry))
        {
            if (system != null && entry.Systems.TryGetValue(system, out var chain)) return new List<string>(chain);
            if (system == null && entry.Global.Count > 0) return new List<string>(entry.Global);
            if (system != null && entry.Global.Count > 0) return new List<string>(entry.Global);
        }
        return DefaultChain(category).ToList();
    }

    public bool HasExplicitChain(string category, string? system)
        => _categories.TryGetValue(category, out var entry)
           && (system == null ? entry.Global.Count > 0 : entry.Systems.ContainsKey(system));

    public void SetChain(string category, string? system, IReadOnlyList<string>? chain)
    {
        if (!_categories.TryGetValue(category, out var entry))
        {
            entry = (new List<string>(), new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));
            _categories[category] = entry;
        }
        if (system == null)
        {
            entry.Global.Clear();
            if (chain != null) entry.Global.AddRange(chain);
        }
        else if (chain == null)
        {
            entry.Systems.Remove(system);
        }
        else
        {
            entry.Systems[system] = chain.ToList();
        }
    }

    public void Save()
    {
        var document = new Dictionary<string, object?> { ["schema"] = Schema, ["generatedBy"] = "MarqueeManagerSetup" };
        var categories = new Dictionary<string, object?>();
        foreach (var (name, entry) in _categories)
        {
            if (entry.Global.Count == 0 && entry.Systems.Count == 0) continue;
            categories[name] = new Dictionary<string, object?>
            {
                ["global"] = entry.Global,
                ["systems"] = entry.Systems
            };
        }
        document["categories"] = categories;
        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        if (File.Exists(DocumentPath))
        {
            try
            {
                File.Copy(DocumentPath, DocumentPath + ".bak", overwrite: true);
            }
            catch
            {
                // best effort
            }
        }
        File.WriteAllText(DocumentPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(DocumentPath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(DocumentPath));
            if (!doc.RootElement.TryGetProperty("categories", out var categories)
                || categories.ValueKind != JsonValueKind.Object) return;
            foreach (var category in categories.EnumerateObject())
            {
                var global = ReadChain(category.Value, "global");
                var systems = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                if (category.Value.TryGetProperty("systems", out var systemsElement)
                    && systemsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var system in systemsElement.EnumerateObject())
                    {
                        var chain = system.Value.ValueKind == JsonValueKind.Array
                            ? system.Value.EnumerateArray()
                                .Where(v => v.ValueKind == JsonValueKind.String)
                                .Select(v => v.GetString() ?? "")
                                .Where(v => v.Length > 0)
                                .ToList()
                            : new List<string>();
                        if (chain.Count > 0) systems[system.Name] = chain;
                    }
                }
                _categories[category.Name] = (global, systems);
            }
        }
        catch
        {
            // unreadable: defaults everywhere
        }
    }

    private static List<string> ReadChain(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(v => v.ValueKind == JsonValueKind.String)
                .Select(v => v.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToList()
            : new List<string>();

    // ================= user drop folder =================

    /// <summary>Copies a dropped media file into the category's user folder under
    /// its canonical rom name (alias-resolved when possible). Returns the rom.</summary>
    public string? ImportUserFile(string category, string system, string sourcePath,
        GameIdentityIndex identity, string? forcedRom = null)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var rom = forcedRom ?? identity.ResolveRom(system, stem) ?? stem;

        var folder = UserFolder(category, system);
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder, SafeName(rom) + extension);
        File.Copy(sourcePath, destination, overwrite: true);
        ReindexUserFolder(category, system, identity);
        return rom;
    }

    /// <summary>Rebuilds the sidecar .index.json (file → canonical rom) of a user
    /// folder — the runtime consults it for alias-named files.</summary>
    public int ReindexUserFolder(string category, string system, GameIdentityIndex identity)
    {
        var folder = UserFolder(category, system);
        if (!Directory.Exists(folder)) return 0;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('.')) continue;
            var rom = identity.ResolveRom(system, Path.GetFileNameWithoutExtension(name));
            if (rom != null) map[name] = rom;
        }
        File.WriteAllText(Path.Combine(folder, ".index.json"),
            JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        return map.Count;
    }

    /// <summary>Coverage counters for the priorities table.</summary>
    public (int UserFiles, int Compositions) Coverage(string category, string system)
    {
        var user = 0;
        var folder = UserFolder(category, system);
        if (Directory.Exists(folder))
            user = Directory.EnumerateFiles(folder).Count(f => !Path.GetFileName(f).StartsWith('.'));
        var compositions = 0;
        var compositionFolder = Path.Combine(_pluginRoot, "media", category + "s", SafeName(system));
        if (Directory.Exists(compositionFolder))
            compositions = Directory.EnumerateFiles(compositionFolder, "*.png").Count();
        return (user, compositions);
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
