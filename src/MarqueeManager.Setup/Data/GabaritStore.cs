using System.IO;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>
/// The general template ("gabarit") of a surface for one scope (system or game):
/// the fanart+gradient+logo recipe applied to EVERY system/game of that surface.
/// It is exactly the runtime's CompositionTemplate recipe (background, gradient,
/// logo budget); the render dimensions come from the surface, so they are not
/// stored here. One file per surface + scope.
/// </summary>
public sealed record GabaritDefinition(
    string Background = "fanart",   // fanart | black
    bool Gradient = true,
    double LogoMaxWidth = 0.68,
    double LogoMaxHeight = 0.88);

public sealed class GabaritStore
{
    public const string SystemScope = "system";
    public const string GameScope = "game";

    private readonly string _pluginRoot;

    public GabaritStore(string pluginRoot) => _pluginRoot = pluginRoot;

    public string PathFor(string surfaceId, string scope)
        => Path.Combine(_pluginRoot, "media", "templates", "surfaces", Safe(surfaceId), scope + ".gabarit.json");

    public bool Exists(string surfaceId, string scope) => File.Exists(PathFor(surfaceId, scope));

    public GabaritDefinition Load(string surfaceId, string scope)
    {
        try
        {
            var path = PathFor(surfaceId, scope);
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var r = doc.RootElement;
                return new GabaritDefinition(
                    Str(r, "background", "fanart"),
                    !r.TryGetProperty("gradient", out var g) || g.ValueKind != JsonValueKind.False,
                    Dbl(r, "logoMaxWidth", 0.68),
                    Dbl(r, "logoMaxHeight", 0.88));
            }
        }
        catch
        {
            // unreadable: fall back to the default recipe
        }
        return new GabaritDefinition();
    }

    public void Save(string surfaceId, string scope, GabaritDefinition gabarit)
    {
        var path = PathFor(surfaceId, scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new Dictionary<string, object?>
        {
            ["generatedBy"] = "MarqueeManagerSetup",
            ["background"] = gabarit.Background,
            ["gradient"] = gabarit.Gradient,
            ["logoMaxWidth"] = gabarit.LogoMaxWidth,
            ["logoMaxHeight"] = gabarit.LogoMaxHeight
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }

    private static string Str(JsonElement e, string name, string fallback)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : fallback;

    private static double Dbl(JsonElement e, string name, double fallback)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
}
