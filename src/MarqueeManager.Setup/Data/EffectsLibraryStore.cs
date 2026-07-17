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

    /// <summary>Effects shipped by MarqueeManager (seeded, flagged "official" in
    /// the file): they can be duplicated and tweaked but never deleted.</summary>
    public HashSet<string> OfficialNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOfficial(string name) => OfficialNames.Contains(name);

    /// <summary>Name → ordered action stack. Insertion order preserved.</summary>
    public Dictionary<string, List<EffectRule>> Load()
    {
        var effects = new Dictionary<string, List<EffectRule>>(StringComparer.OrdinalIgnoreCase);
        OfficialNames.Clear();
        try
        {
            if (!File.Exists(LibraryPath)) return effects;
            using var doc = JsonDocument.Parse(File.ReadAllText(LibraryPath));
            if (!doc.RootElement.TryGetProperty("effects", out var set) || set.ValueKind != JsonValueKind.Object)
                return effects;
            foreach (var entry in set.EnumerateObject())
            {
                if (entry.Value.TryGetProperty("official", out var official) && official.ValueKind == JsonValueKind.True)
                {
                    OfficialNames.Add(entry.Name);
                }
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
                kv => IsOfficial(kv.Key)
                    ? new Dictionary<string, object> { ["official"] = true, ["actions"] = kv.Value }
                    : new Dictionary<string, object> { ["actions"] = kv.Value })
        };
        File.WriteAllText(LibraryPath, JsonSerializer.Serialize(document, JsonOptions));
    }

    /// <summary>Loads the library and (re)injects the official presets when absent
    /// — a fresh install gets a full shelf, an upgrade gains the new ones, and
    /// the user's own effects are never touched.</summary>
    public Dictionary<string, List<EffectRule>> LoadOrSeed()
    {
        var effects = Load();
        var added = false;
        foreach (var (name, actions) in OfficialSeeds())
        {
            OfficialNames.Add(name);
            if (!effects.ContainsKey(name))
            {
                effects[name] = actions;
                added = true;
            }
        }
        if (added) Save(effects);
        return effects;
    }

    /// <summary>The shipped presets (sprites from resources\sprites). The 1943
    /// scenario ("Touché") is among them.</summary>
    private static IEnumerable<(string Name, List<EffectRule> Actions)> OfficialSeeds()
    {
        yield return ("Touché (voile + secousse + explosions)", new List<EffectRule>
        {
            new() { Kind = "tint", Color = "#ff2a18", DurationMs = 3000, Dip = 0.3 },
            new() { Kind = "shake", DurationMs = 500 },
            new() { Kind = "sprite", Sprite = "bombexplode.gif", Count = 3, Motion = "pop", DurationMs = 900 }
        });
        yield return ("Flash puis nuée d'étoiles", new List<EffectRule>
        {
            new() { Kind = "flash", Color = "#ff2a18", DurationMs = 250 },
            new() { Kind = "sprite", Sprite = "star.gif", Count = 6, Motion = "rise", DurationMs = 1200, DelayMs = 300 }
        });
        yield return ("Victoire (strobe doré + coupe)", new List<EffectRule>
        {
            new() { Kind = "strobe", Color = "#ffb300", DurationMs = 1200 },
            new() { Kind = "sprite", Sprite = "championcup.gif", Count = 1, Motion = "pop", DurationMs = 2000, DelayMs = 400, Scale = 2.0, Grow = true, Placement = "center" }
        });
        yield return ("KO sanglant", new List<EffectRule>
        {
            new() { Kind = "tint", Color = "#8a0000", DurationMs = 2000, Dip = 0.4 },
            new() { Kind = "sprite", Sprite = "blood.gif", Count = 4, Motion = "fall", DurationMs = 1500, DelayMs = 150 }
        });
        yield return ("Pluie de pièces", new List<EffectRule>
        {
            new() { Kind = "flash", Color = "#ffd944", DurationMs = 220 },
            new() { Kind = "sprite", Sprite = "coins.gif", Count = 6, Motion = "fall", DurationMs = 1600, DelayMs = 120, Placement = "spread" }
        });
        yield return ("Combo enflammé", new List<EffectRule>
        {
            new() { Kind = "pulse", Color = "#ff7a18", DurationMs = 600 },
            new() { Kind = "sprite", Sprite = "fireball.gif", Count = 3, Motion = "cross", DurationMs = 1400, DelayMs = 100 }
        });
        yield return ("1UP", new List<EffectRule>
        {
            new() { Kind = "pulse", Color = "#39d353", DurationMs = 500 },
            new() { Kind = "sprite", Sprite = "oneup.gif", Count = 1, Motion = "pop", DurationMs = 1500, Scale = 2.0, Grow = true, Placement = "center" }
        });
        yield return ("Cœur perdu", new List<EffectRule>
        {
            new() { Kind = "tint", Color = "#ff2a18", DurationMs = 1500, Dip = 0.3 },
            new() { Kind = "sprite", Sprite = "heart.gif", Count = 1, Motion = "rise", DurationMs = 1400, Scale = 2.0, Placement = "center" }
        });
        yield return ("Game over (extinction + fumée)", new List<EffectRule>
        {
            new() { Kind = "blackout", DurationMs = 1800, Dip = 0.9 },
            new() { Kind = "sprite", Sprite = "smoke.gif", Count = 3, Motion = "rise", DurationMs = 2500, DelayMs = 500 }
        });
        yield return ("Impact (secousse + éclats)", new List<EffectRule>
        {
            new() { Kind = "shake", DurationMs = 450 },
            new() { Kind = "sprite", Sprite = "impact.gif", Count = 2, Motion = "pop", DurationMs = 700, Scale = 1.5 }
        });
    }
}
