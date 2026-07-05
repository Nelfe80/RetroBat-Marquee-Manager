using System.Xml.Linq;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>A lamp of the scene (CDC §12.1): circular (x/y/radius) or region rect, normalized 0..1.</summary>
public sealed record LampDefinition(string Id, float X, float Y, float Radius, SKRect? Region,
    float ColorR, float ColorG, float ColorB);

/// <summary>
/// Per-game scene definition, new format `rbmarquee.xml` (CDC §20) loaded from
/// resources/rbmarquee/&lt;rom&gt;.xml: lamps + arcade output bindings + attract hint.
/// Converted from the legacy .lay files or generated from the MAME outputs data.
/// Invalid files are skipped, never fatal (§20.6).
/// </summary>
public sealed class RbMarqueeScene
{
    public required string Rom { get; init; }
    public required IReadOnlyList<LampDefinition> Lamps { get; init; }
    /// <summary>MAME output name → lamp id.</summary>
    public required IReadOnlyDictionary<string, string> OutputMap { get; init; }
    public string AttractMode { get; init; } = "chase";
    /// <summary>
    /// Calibrated artwork (resources/images) the lamp regions were measured on —
    /// preferred over the scraped marquee so lamps stay glued to their letters.
    /// </summary>
    public string? CalibratedImagePath { get; init; }

    public static RbMarqueeScene? Load(string directory, string rom, ILogger logger)
    {
        var path = Path.Combine(directory, rom + ".xml");
        if (!File.Exists(path)) return null;
        try
        {
            var document = XDocument.Load(path);
            var lamps = new List<LampDefinition>();
            foreach (var element in document.Descendants("lamp"))
            {
                var id = (string?)element.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) continue;
                var (r, g, b) = ParseColor((string?)element.Attribute("color"));
                SKRect? region = null;
                float x = 0.5f, y = 0.5f, radius = 0.10f;
                var regionRaw = (string?)element.Attribute("region");
                if (regionRaw != null)
                {
                    var parts = regionRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4 &&
                        TryF(parts[0], out var rx) && TryF(parts[1], out var ry) &&
                        TryF(parts[2], out var rw) && TryF(parts[3], out var rh))
                    {
                        region = SKRect.Create(rx, ry, rw, rh);
                        x = rx + rw / 2f;
                        y = ry + rh / 2f;
                    }
                    else continue;
                }
                else
                {
                    if (!TryF((string?)element.Attribute("x"), out x) ||
                        !TryF((string?)element.Attribute("y"), out y)) continue;
                    if (TryF((string?)element.Attribute("radius"), out var rr)) radius = rr;
                }
                lamps.Add(new LampDefinition(id, x, y, radius, region, r, g, b));
            }

            var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var map in document.Descendants("map"))
            {
                var output = (string?)map.Attribute("output");
                var to = ((string?)map.Attribute("to"))?.Replace("lamp:", "", StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(to))
                    outputs[output] = to;
            }

            if (lamps.Count == 0) return null;

            string? calibrated = null;
            var imageName = (string?)document.Descendants("scene").FirstOrDefault()?.Attribute("image");
            if (imageName != null)
            {
                var imagePath = Path.GetFullPath(Path.Combine(directory, "..", "images", imageName));
                if (File.Exists(imagePath)) calibrated = imagePath;
            }

            return new RbMarqueeScene
            {
                Rom = rom,
                Lamps = lamps,
                OutputMap = outputs,
                AttractMode = (string?)document.Descendants("attract").FirstOrDefault()?.Attribute("mode") ?? "chase",
                CalibratedImagePath = calibrated
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Invalid rbmarquee scene {Path}; ignored", path);
            return null;
        }
    }

    private static bool TryF(string? raw, out float value)
        => float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value);

    private static (float R, float G, float B) ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return (1f, 0.88f, 0.70f); // warm default
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            return (1f, 0.88f, 0.70f);
        return ((value >> 16 & 0xFF) / 255f, (value >> 8 & 0xFF) / 255f, (value & 0xFF) / 255f);
    }
}

/// <summary>Runtime state of a lamp: eased intensity toward its target (CDC §12.2).</summary>
public sealed class LampState
{
    public required LampDefinition Definition { get; init; }
    public float Current;
    public float Target;

    public void Step(float easing = 0.30f) => Current += (Target - Current) * easing;
}
