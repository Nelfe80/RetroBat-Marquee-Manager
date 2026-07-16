using System.IO;
using System.Text.Json;
using MarqueeManager.Setup.Config;

namespace MarqueeManager.Setup.Data;

/// <summary>One component placed on a surface (fractions of the surface).</summary>
public sealed class ComponentModel
{
    public string Type { get; set; } = "media.flux";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 1;
    public double H { get; set; } = 1;
    /// <summary>Free options (url, kind, card, template…) — serialized FLAT into
    /// the component object, the shape the runtime parser expects.</summary>
    public Dictionary<string, string> Options { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A dynamic surface as edited by the Setup.</summary>
public sealed class SurfaceModel
{
    public string Id { get; set; } = "";
    public string Category { get; set; } = "marquee";
    public List<int> Screens { get; } = new();
    public int? X { get; set; }
    public int? Y { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public List<string> Streams { get; } = new();
    public Dictionary<string, string> Params { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ComponentModel> Components { get; } = new();

    public bool IsFullscreen => Width is null or <= 0 || Height is null or <= 0;
}

/// <summary>
/// Reader/writer of state\surfaces.json (schema marqueemanager.surfaces.v1,
/// consumed by the runtime) + the one-shot automatic migration from the legacy
/// [Screens] five-target model. Ownership-guarded like every Setup JSON.
/// </summary>
public sealed class SurfacesStore
{
    public const string Schema = "marqueemanager.surfaces.v1";
    public const string Generator = "MarqueeManagerSetup";

    /// <summary>Set when this session created surfaces.json from [Screens] — the
    /// surfaces view shows a one-time conversion banner.</summary>
    public static bool MigratedThisSession { get; private set; }

    private readonly string _pluginRoot;

    public SurfacesStore(string pluginRoot) => _pluginRoot = pluginRoot;

    public string DocumentPath => Path.Combine(_pluginRoot, "state", "surfaces.json");

    public bool Exists => File.Exists(DocumentPath);

    public List<SurfaceModel> Load()
    {
        var surfaces = new List<SurfaceModel>();
        try
        {
            if (!Exists) return surfaces;
            using var doc = JsonDocument.Parse(File.ReadAllText(DocumentPath));
            if (!doc.RootElement.TryGetProperty("surfaces", out var array) || array.ValueKind != JsonValueKind.Array)
                return surfaces;

            foreach (var element in array.EnumerateArray())
            {
                var surface = new SurfaceModel
                {
                    Id = Str(element, "id"),
                    Category = Str(element, "category", "marquee"),
                    X = Int(element, "x"),
                    Y = Int(element, "y"),
                    Width = Int(element, "width"),
                    Height = Int(element, "height")
                };
                if (surface.Id.Length == 0) continue;
                if (Int(element, "screen") is { } single && single >= 0) surface.Screens.Add(single);
                if (element.TryGetProperty("screens", out var screens) && screens.ValueKind == JsonValueKind.Array)
                    foreach (var s in screens.EnumerateArray())
                        if (s.TryGetInt32(out var index) && index >= 0 && !surface.Screens.Contains(index))
                            surface.Screens.Add(index);
                if (element.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
                    foreach (var s in streams.EnumerateArray())
                        if (s.GetString() is { Length: > 0 } stream)
                            surface.Streams.Add(stream);
                if (element.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object)
                    foreach (var p in parameters.EnumerateObject())
                        surface.Params[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                if (element.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in components.EnumerateArray())
                    {
                        var component = new ComponentModel
                        {
                            Type = Str(c, "type"),
                            X = Dbl(c, "x") ?? 0,
                            Y = Dbl(c, "y") ?? 0,
                            W = Dbl(c, "w") ?? 1,
                            H = Dbl(c, "h") ?? 1
                        };
                        if (component.Type.Length == 0) continue;
                        foreach (var property in c.EnumerateObject())
                        {
                            if (property.Name is "type" or "x" or "y" or "w" or "h") continue;
                            component.Options[property.Name] = property.Value.ValueKind == JsonValueKind.String
                                ? property.Value.GetString() ?? ""
                                : property.Value.GetRawText();
                        }
                        surface.Components.Add(component);
                    }
                }
                surfaces.Add(surface);
            }
        }
        catch
        {
            // unreadable document: empty list, the caller offers a reset
        }
        return surfaces;
    }

    public bool IsOwnedBySetup()
    {
        if (!Exists) return true;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(DocumentPath));
            return doc.RootElement.TryGetProperty("generatedBy", out var by) && by.GetString() == Generator;
        }
        catch
        {
            return false;
        }
    }

    public void Save(IReadOnlyList<SurfaceModel> surfaces)
    {
        var document = new Dictionary<string, object?>
        {
            ["schema"] = Schema,
            ["generatedBy"] = Generator,
            ["surfaces"] = surfaces.Select(Serialize).ToList()
        };

        Directory.CreateDirectory(Path.GetDirectoryName(DocumentPath)!);
        if (Exists)
        {
            try
            {
                File.Copy(DocumentPath, DocumentPath + ".bak", overwrite: true);
            }
            catch
            {
                // best effort backup
            }
        }
        File.WriteAllText(DocumentPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static Dictionary<string, object?> Serialize(SurfaceModel surface)
    {
        var element = new Dictionary<string, object?>
        {
            ["id"] = surface.Id,
            ["category"] = surface.Category
        };
        if (surface.Screens.Count == 1) element["screen"] = surface.Screens[0];
        else if (surface.Screens.Count > 1) element["screens"] = surface.Screens;
        if (!surface.IsFullscreen)
        {
            element["x"] = surface.X ?? 0;
            element["y"] = surface.Y ?? 0;
            element["width"] = surface.Width;
            element["height"] = surface.Height;
        }
        element["streams"] = surface.Streams;
        if (surface.Params.Count > 0) element["params"] = surface.Params;
        element["components"] = surface.Components.Select(component =>
        {
            // options are flattened into the component object (runtime contract)
            var c = new Dictionary<string, object?> { ["type"] = component.Type };
            if (component.X != 0) c["x"] = component.X;
            if (component.Y != 0) c["y"] = component.Y;
            if (Math.Abs(component.W - 1) > 0.0001) c["w"] = component.W;
            if (Math.Abs(component.H - 1) > 0.0001) c["h"] = component.H;
            foreach (var (key, value) in component.Options) c[key] = value;
            return c;
        }).ToList();
        return element;
    }

    // ================= migration =================

    /// <summary>
    /// One-shot conversion of the legacy [Screens] model. Iso-behavior: each of
    /// the five targets becomes a surface with the component stack its category
    /// has always rendered (mirror of the runtime's SurfacesDocument.FromLegacy).
    /// </summary>
    public bool MigrateFromLegacyIfNeeded()
    {
        if (Exists) return false;
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var lighting = ini.GetBool("Lighting", "Enabled", false);
        var surfaces = new List<SurfaceModel>();

        foreach (var (target, category) in new[]
                 {
                     ("Marquee", "marquee"), ("Topper", "topper"), ("IcCard", "iccard"),
                     ("Dmd", "dmd-virtual"), ("Lcd", "lcd")
                 })
        {
            var raw = ini.Get("Screens", target + "Screen", target == "Marquee" ? "1" : "-1");
            var screens = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var index) ? index : -1)
                .Where(index => index >= 0)
                .Distinct()
                .ToList();
            if (screens.Count == 0) continue;

            var surface = new SurfaceModel { Id = target.ToLowerInvariant(), Category = category };
            surface.Screens.AddRange(screens);
            surface.Streams.Add(ini.Get("Screens", target + "Content", target.ToLowerInvariant()));

            var bounds = ini.Get("Screens", target + "Bounds", "")
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (bounds.Length == 4
                && int.TryParse(bounds[0], out var x) && int.TryParse(bounds[1], out var y)
                && int.TryParse(bounds[2], out var w) && int.TryParse(bounds[3], out var h)
                && w > 0 && h > 0)
            {
                surface.X = x;
                surface.Y = y;
                surface.Width = w;
                surface.Height = h;
            }

            surface.Components.AddRange(DefaultComponents(category, category == "marquee" && lighting));
            surfaces.Add(surface);
        }

        if (surfaces.Count == 0) return false;
        Save(surfaces);
        MigratedThisSession = true;
        return true;
    }

    /// <summary>Component stack a category has always rendered (must stay aligned
    /// with the runtime's SurfacesDocument.DefaultComponents).</summary>
    public static List<ComponentModel> DefaultComponents(string category, bool lighting)
    {
        var components = new List<ComponentModel> { new() { Type = "media.flux" } };
        if (category.Equals("marquee", StringComparison.OrdinalIgnoreCase))
        {
            if (lighting) components.Add(new ComponentModel { Type = "lighting.engine" });
            components.Add(new ComponentModel { Type = "lamps.scene" });
            components.Add(new ComponentModel { Type = "overlay.hiscore" });
            components.Add(new ComponentModel { Type = "overlay.live.score" });
            components.Add(new ComponentModel { Type = "overlay.live.timer" });
            components.Add(new ComponentModel { Type = "overlay.ra.info" });
            components.Add(new ComponentModel { Type = "overlay.ra.badges" });
            components.Add(new ComponentModel { Type = "overlay.ra.speedrun" });
        }
        return components;
    }

    private static string Str(JsonElement element, string name, string fallback = "")
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static double? Dbl(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
