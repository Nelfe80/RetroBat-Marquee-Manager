using System.IO;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// "Mes effets" — the user's named, reusable effect compositions, stored in
/// media\effects\library.json (the exact file the runtime's IngameEffectLibrary
/// reads): { "effects": { "&lt;name&gt;": { "actions": [ {…}, {…} ] } } }.
/// An action is an <see cref="EffectRule"/> with delayMs (0 = simultaneous with
/// the previous one) and optionally media (file under media\effects\user\).
/// </summary>
public sealed class EffectsLibraryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _root;

    public EffectsLibraryStore(string pluginRoot)
    {
        _root = Path.Combine(pluginRoot, "media", "effects");
    }

    public string LibraryPath => Path.Combine(_root, "library.json");
    public string UserMediaRoot => Path.Combine(_root, "user");

    /// <summary>webm / gif / apng files the user dropped in media\effects\user\.</summary>
    public IReadOnlyList<string> ListUserMedia()
    {
        try
        {
            if (!Directory.Exists(UserMediaRoot)) return Array.Empty<string>();
            return Directory.EnumerateFiles(UserMediaRoot)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".webm" or ".gif" or ".png" or ".apng")
                .Select(Path.GetFileName)
                .Where(name => name != null)
                .OrderBy(name => name)
                .ToList()!;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Name → ordered action stack. Insertion order preserved.</summary>
    public Dictionary<string, List<EffectRule>> Load()
    {
        var effects = new Dictionary<string, List<EffectRule>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(LibraryPath)) return effects;
            using var doc = JsonDocument.Parse(File.ReadAllText(LibraryPath));
            if (!doc.RootElement.TryGetProperty("effects", out var set) || set.ValueKind != JsonValueKind.Object)
                return effects;
            foreach (var entry in set.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("actions", out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    var actions = array.EnumerateArray()
                        .Select(a => a.Deserialize<EffectRule>())
                        .Where(a => a != null)
                        .Select(a => a!)
                        .ToList();
                    if (actions.Count > 0) effects[entry.Name] = actions;
                }
                else if (entry.Value.Deserialize<EffectRule>() is { } single)
                {
                    effects[entry.Name] = new List<EffectRule> { single };
                }
            }
        }
        catch
        {
            // unreadable library: empty (the .bak keeps the old content)
        }
        return effects;
    }

    public void Save(Dictionary<string, List<EffectRule>> effects)
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(UserMediaRoot);
        if (File.Exists(LibraryPath))
        {
            try
            {
                File.Copy(LibraryPath, LibraryPath + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }

        var document = new Dictionary<string, object>
        {
            ["schema"] = "marqueemanager.effects-library.v1",
            ["generatedBy"] = "MarqueeManagerSetup",
            ["effects"] = effects.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, object> { ["actions"] = kv.Value })
        };
        File.WriteAllText(LibraryPath, JsonSerializer.Serialize(document, JsonOptions));
    }

    /// <summary>Ships a few example compositions on first run so the library never
    /// opens empty (the 1943 scenario among them).</summary>
    public Dictionary<string, List<EffectRule>> LoadOrSeed()
    {
        var effects = Load();
        if (effects.Count > 0 || File.Exists(LibraryPath)) return effects;

        effects["Touché (voile + secousse + explosions)"] = new List<EffectRule>
        {
            new() { Kind = "tint", Color = "#ff2a18", DurationMs = 3000, Dip = 0.3 },
            new() { Kind = "shake", DurationMs = 500 },
            new() { Kind = "sprite", Sprite = "bombexplode.gif", Count = 3, Motion = "pop", DurationMs = 900 }
        };
        effects["Flash puis nuée de sprites"] = new List<EffectRule>
        {
            new() { Kind = "flash", Color = "#ff2a18", DurationMs = 250 },
            new() { Kind = "sprite", Sprite = "star.gif", Count = 6, Motion = "rise", DurationMs = 1200, DelayMs = 300 }
        };
        effects["Victoire (strobe doré)"] = new List<EffectRule>
        {
            new() { Kind = "strobe", Color = "#ffb300", DurationMs = 1200 },
            new() { Kind = "sprite", Sprite = "championcup.gif", Count = 4, Motion = "fall", DurationMs = 2000, DelayMs = 400 }
        };
        Save(effects);
        return effects;
    }
}
