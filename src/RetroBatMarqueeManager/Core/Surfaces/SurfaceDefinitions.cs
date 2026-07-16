using System.Text.Json;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Core.Surfaces;

/// <summary>One component placed on a surface. Coordinates are FRACTIONS of the
/// surface (0..1) so a layout survives a resolution change.</summary>
public sealed record ComponentDefinition(
    string Type,
    double X = 0, double Y = 0, double W = 1, double H = 1,
    IReadOnlyDictionary<string, string>? Options = null)
{
    public string Option(string name, string fallback = "")
        => Options != null && Options.TryGetValue(name, out var value) ? value : fallback;
}

/// <summary>
/// A dynamic surface: a categorized rectangle on a screen, fed by streams and
/// rendering a stack of components. Replaces the five fixed [Screens] targets;
/// the legacy config converts losslessly into these definitions.
/// </summary>
public sealed record SurfaceDefinition(
    string Id,
    string Category,
    IReadOnlyList<int> Screens,
    TargetBounds? Bounds,
    IReadOnlyList<string> Streams,
    IReadOnlyDictionary<string, string> Params,
    IReadOnlyList<ComponentDefinition> Components)
{
    public bool HasComponent(string type)
        => Components.Any(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public ComponentDefinition? Component(string type)
        => Components.FirstOrDefault(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase));

    public bool AcceptsStream(string stream)
        => Streams.Any(s => s.Equals(stream, StringComparison.OrdinalIgnoreCase));

    public string Param(string name, string fallback = "")
        => Params.TryGetValue(name, out var value) ? value : fallback;

    public bool ParamBool(string name, bool fallback)
        => bool.TryParse(Param(name), out var value) ? value : fallback;
}

/// <summary>
/// Loader of state\surfaces.json (schema marqueemanager.surfaces.v1, written by
/// MarqueeManagerSetup) and legacy converter. The runtime NEVER writes the file:
/// absent or invalid → <see cref="FromLegacy"/> rebuilds the exact historical
/// behavior from [Screens], so un-migrated installs keep working untouched.
/// </summary>
public static class SurfacesDocument
{
    public const string Schema = "marqueemanager.surfaces.v1";

    public static string PathFor(string baseDirectory)
        => System.IO.Path.Combine(baseDirectory, "state", "surfaces.json");

    /// <summary>Null = no valid dynamic document (caller falls back to legacy).</summary>
    public static IReadOnlyList<SurfaceDefinition>? TryLoad(string path, ILogger logger)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("schema", out var schema) || schema.GetString() != Schema)
            {
                logger.LogWarning("surfaces.json schema mismatch; legacy [Screens] used");
                return null;
            }
            if (!root.TryGetProperty("surfaces", out var surfaces) || surfaces.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<SurfaceDefinition>();
            foreach (var element in surfaces.EnumerateArray())
            {
                var surface = ParseSurface(element);
                if (surface != null) result.Add(surface);
            }

            if (result.Count == 0) return null;
            logger.LogInformation("Dynamic surfaces loaded: {Count} surface(s) from {Path}",
                result.Count, path);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unreadable surfaces.json; legacy [Screens] used");
            return null;
        }
    }

    private static SurfaceDefinition? ParseSurface(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        var id = ReadString(element, "id");
        var category = ReadString(element, "category").ToLowerInvariant();
        if (id.Length == 0 || category.Length == 0) return null;

        // "screen": 2 or "screens": [1,2]; absent/-1 with a category dmd-physical = window-less sink
        var screens = new List<int>();
        if (element.TryGetProperty("screen", out var single) && single.TryGetInt32(out var index) && index >= 0)
            screens.Add(index);
        if (element.TryGetProperty("screens", out var many) && many.ValueKind == JsonValueKind.Array)
            foreach (var s in many.EnumerateArray())
                if (s.TryGetInt32(out var i) && i >= 0 && !screens.Contains(i))
                    screens.Add(i);

        TargetBounds? bounds = null;
        if (ReadInt(element, "width") is { } w && ReadInt(element, "height") is { } h && w > 0 && h > 0)
            bounds = new TargetBounds(ReadInt(element, "x") ?? 0, ReadInt(element, "y") ?? 0, w, h);

        var streams = new List<string>();
        if (element.TryGetProperty("streams", out var streamArray) && streamArray.ValueKind == JsonValueKind.Array)
            foreach (var s in streamArray.EnumerateArray())
                if (s.ValueKind == JsonValueKind.String && s.GetString() is { Length: > 0 } stream)
                    streams.Add(stream);

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("params", out var paramObject) && paramObject.ValueKind == JsonValueKind.Object)
            foreach (var property in paramObject.EnumerateObject())
                parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? ""
                    : property.Value.GetRawText();

        var components = new List<ComponentDefinition>();
        if (element.TryGetProperty("components", out var componentArray) && componentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in componentArray.EnumerateArray())
            {
                var type = ReadString(c, "type");
                if (type.Length == 0) continue;
                var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in c.EnumerateObject())
                {
                    if (property.Name is "type" or "x" or "y" or "w" or "h") continue;
                    options[property.Name] = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : property.Value.GetRawText();
                }
                components.Add(new ComponentDefinition(type,
                    ReadDouble(c, "x") ?? 0, ReadDouble(c, "y") ?? 0,
                    ReadDouble(c, "w") ?? 1, ReadDouble(c, "h") ?? 1,
                    options));
            }
        }

        return new SurfaceDefinition(id, category, screens, bounds, streams, parameters, components);
    }

    /// <summary>
    /// Historical behavior rebuilt from [Screens]: one surface per legacy target,
    /// id = target name so every existing "marquee"/"topper" reference still
    /// resolves, default components mirror what each target has always shown.
    /// </summary>
    public static IReadOnlyList<SurfaceDefinition> FromLegacy(IConfigService config)
    {
        var surfaces = new List<SurfaceDefinition>();
        foreach (var target in new[] { "marquee", "topper", "iccard", "dmd", "lcd" })
        {
            var screens = config.GetScreenIndices(target);
            if (screens.Count == 0) continue;
            var category = target == "dmd" ? "dmd-virtual" : target;
            var contentKey = target == "iccard" ? "IcCardContent" : Capitalize(target) + "Content";
            var stream = config.GetValue("Screens", contentKey, target);
            surfaces.Add(new SurfaceDefinition(
                target,
                category,
                screens,
                config.GetTargetBounds(target),
                new[] { stream.Length > 0 ? stream : target },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                DefaultComponents(category, target == "marquee" && config.LightingEnabled)));
        }
        return surfaces;
    }

    private static string Capitalize(string value)
        => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    /// <summary>Component stack a category has always rendered (iso-behavior for
    /// migrated/legacy configs). Full-rect defaults keep the historical layout.</summary>
    public static IReadOnlyList<ComponentDefinition> DefaultComponents(string category, bool lighting)
    {
        var components = new List<ComponentDefinition> { new("media.flux") };
        if (category.Equals("marquee", StringComparison.OrdinalIgnoreCase))
        {
            if (lighting) components.Add(new ComponentDefinition("lighting.engine"));
            components.Add(new ComponentDefinition("lamps.scene"));
            components.Add(new ComponentDefinition("overlay.hiscore"));
            components.Add(new ComponentDefinition("overlay.live.score"));
            components.Add(new ComponentDefinition("overlay.live.timer"));
            components.Add(new ComponentDefinition("overlay.ra.info"));
            components.Add(new ComponentDefinition("overlay.ra.badges"));
            components.Add(new ComponentDefinition("overlay.ra.speedrun"));
        }
        return components;
    }

    private static string ReadString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static int? ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : null;

    private static double? ReadDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;
}
