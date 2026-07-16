using System.Text.Json;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>One composition template (media\templates.json or built-in preset).</summary>
public sealed record CompositionTemplate(
    string Id, int Width, int Height,
    string Background = "fanart",   // fanart | black
    bool Gradient = true,
    double LogoMaxWidth = 0.68,
    double LogoMaxHeight = 0.88);

/// <summary>
/// Lazy Skia renderer of the template cache
/// (media\&lt;cat&gt;s\.cache\&lt;sys&gt;\&lt;rom&gt;-&lt;template&gt;.png). Recipe mirrors APIExpose's
/// marquee autogen: fanart cover-cropped as background, black/white gradient
/// under the logo picked by luminance (threshold 145), logo aspect-fit within
/// its budget. Static part only — dynamic elements stay live overlay components.
/// Jobs are deduplicated; completion invokes the callback so the surface swaps
/// from the stream media to the cached PNG (pending → updated pattern).
/// </summary>
public sealed class CompositionTemplateRenderer
{
    public static readonly IReadOnlyList<CompositionTemplate> BuiltIn = new[]
    {
        new CompositionTemplate("h-1920x360", 1920, 360),
        new CompositionTemplate("h-1280x400", 1280, 400),
        new CompositionTemplate("h-920x360", 920, 360),
        new CompositionTemplate("v-1080x1920", 1080, 1920, LogoMaxWidth: 0.86, LogoMaxHeight: 0.30)
    };

    private readonly string _baseDirectory;
    private readonly ILogger _logger;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public CompositionTemplateRenderer(string baseDirectory, ILogger logger)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;
    }

    public CompositionTemplate? Find(string templateId)
    {
        try
        {
            var path = Path.Combine(_baseDirectory, "media", "templates.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("templates", out var templates)
                    && templates.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in templates.EnumerateArray())
                    {
                        if (!Str(element, "id").Equals(templateId, StringComparison.OrdinalIgnoreCase)) continue;
                        return new CompositionTemplate(
                            templateId,
                            Int(element, "width", 1920), Int(element, "height", 360),
                            Str(element, "background", "fanart"),
                            !element.TryGetProperty("gradient", out var g) || g.ValueKind != JsonValueKind.False,
                            Dbl(element, "logoMaxWidth", 0.68), Dbl(element, "logoMaxHeight", 0.88));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Invalid templates.json: {Message}", ex.Message);
        }
        return BuiltIn.FirstOrDefault(t => t.Id.Equals(templateId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Background render job; onDone(outputPath) fires on success only.</summary>
    public void RenderInBackground(string category, string templateId, string system, string rom,
        string? fanartPath, string? logoPath, Action<string> onDone)
    {
        var output = Path.Combine(_baseDirectory, "media", category.ToLowerInvariant() + "s", ".cache",
            SafeName(system), SafeName(rom) + "-" + SafeName(templateId) + ".png");
        lock (_sync)
        {
            if (!_inFlight.Add(output)) return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var template = Find(templateId);
                if (template == null)
                {
                    _logger.LogWarning("Unknown composition template {Template}", templateId);
                    return;
                }
                if (Render(template, fanartPath, logoPath, output))
                {
                    _logger.LogInformation("Template {Template} rendered for {System}/{Rom}", templateId, system, rom);
                    onDone(output);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Template render failed for {System}/{Rom}: {Message}", system, rom, ex.Message);
            }
            finally
            {
                lock (_sync)
                {
                    _inFlight.Remove(output);
                }
            }
        });
    }

    /// <summary>Synchronous render (also used by the Setup's pre-generation).</summary>
    public bool Render(CompositionTemplate template, string? fanartPath, string? logoPath, string outputPath)
    {
        if (logoPath == null && fanartPath == null) return false;

        using var surface = SKSurface.Create(new SKImageInfo(template.Width, template.Height,
            SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Black);

        var brightBackground = false;
        if (template.Background == "fanart" && fanartPath != null && File.Exists(fanartPath))
        {
            using var fanart = SKBitmap.Decode(fanartPath);
            if (fanart != null)
            {
                DrawCover(canvas, fanart, template.Width, template.Height);
                brightBackground = AverageLuminance(fanart) >= 145;
            }
        }

        if (template.Gradient)
        {
            // gradient sits under the logo zone so the title stays readable —
            // black over bright art, white sheen over dark art (APIExpose rule)
            var color = brightBackground ? SKColors.Black : SKColors.White;
            using var paint = new SKPaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, template.Height * 0.25f), new SKPoint(0, template.Height),
                new[] { color.WithAlpha(0), color.WithAlpha(brightBackground ? (byte)190 : (byte)120) },
                null, SKShaderTileMode.Clamp);
            canvas.DrawRect(new SKRect(0, template.Height * 0.25f, template.Width, template.Height), paint);
        }

        if (logoPath != null && File.Exists(logoPath))
        {
            using var logo = SKBitmap.Decode(logoPath);
            if (logo != null && logo.Width > 0 && logo.Height > 0)
            {
                var maxW = (float)(template.Width * template.LogoMaxWidth);
                var maxH = (float)(template.Height * template.LogoMaxHeight);
                var scale = Math.Min(maxW / logo.Width, maxH / logo.Height);
                var w = logo.Width * scale;
                var h = logo.Height * scale;
                var rect = new SKRect(
                    (template.Width - w) / 2f,
                    (template.Height - h) / 2f,
                    (template.Width + w) / 2f,
                    (template.Height + h) / 2f);
                using var paint = new SKPaint();
                paint.IsAntialias = true;
                canvas.DrawBitmap(logo, rect, paint);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return true;
    }

    private static void DrawCover(SKCanvas canvas, SKBitmap bitmap, int width, int height)
    {
        var scale = Math.Max((float)width / bitmap.Width, (float)height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var rect = new SKRect((width - w) / 2f, (height - h) / 2f, (width + w) / 2f, (height + h) / 2f);
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        canvas.DrawBitmap(bitmap, rect, paint);
    }

    private static byte AverageLuminance(SKBitmap bitmap)
    {
        long sum = 0;
        var samples = 0;
        var stepX = Math.Max(1, bitmap.Width / 32);
        var stepY = Math.Max(1, bitmap.Height / 16);
        for (var y = bitmap.Height / 3; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                sum += (pixel.Red * 299 + pixel.Green * 587 + pixel.Blue * 114) / 1000;
                samples++;
            }
        }
        return samples == 0 ? (byte)0 : (byte)(sum / samples);
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }

    private static string Str(JsonElement element, string name, string fallback = "")
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;

    private static int Int(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static double Dbl(JsonElement element, string name, double fallback)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble() : fallback;
}
