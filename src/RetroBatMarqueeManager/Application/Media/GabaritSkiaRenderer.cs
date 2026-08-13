using System.Text.Json;
using MarqueeManager.Compositions.Core.Composition;
using System.Text;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>
/// Renders a surface gabarit (the user's general template), IN THE RUNTIME, in Skia,
/// off the UI thread — for a game or for a system.
///
/// Why this exists: the gabarit used to be baked only by the Setup, lazily, when the
/// user opened that game's (or that system's) sheet. On a real library the template
/// therefore applied to the handful of entries whose sheet had been visited, and
/// looked like it simply never worked. The runtime now bakes it on demand, exactly
/// like the composition templates and the dynamic surface render.
///
/// It NEVER resolves media by guessing folder names: the APIExpose media folders are
/// slugs ("3-ninjas-kick-back"), not rom names ("Streets of Rage"), so the caller —
/// which already holds the resolved snapshot paths — supplies a resolver. Guessing is
/// what once produced a black render for every megadrive game.
///
/// Fidelity: geometry is expressed in FRACTIONS of the surface (centre x/y, scale as
/// a share of the height), so rendering straight at the target size reproduces the
/// composer's WYSIWYG output without a scaling step. Two known approximations, both on
/// effects rather than layout: WPF's BlurEffect.Radius maps to a Gaussian sigma of
/// radius/2, and text uses the platform default face.
/// </summary>
public sealed class GabaritSkiaRenderer
{
    public const string GabaritSystemId = "__gabarit__";
    public const string SystemScope = "system";

    /// <summary>The GAME gabarit is PER SYSTEM: every game of one system shares its
    /// generic layout, stored under the scope key "game-&lt;system&gt;".</summary>
    public static string GameScopeFor(string system) => "game-" + Safe(system);

    private readonly string _baseDirectory;
    private readonly ILogger _logger;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public GabaritSkiaRenderer(string baseDirectory, ILogger logger)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;
    }

    /// <summary>Where the Setup stores the editable project of a surface's gabarit:
    /// media\&lt;cat&gt;\surfaces\&lt;surfaceId&gt;\__gabarit__\{system|game-&lt;sys&gt;}.project.json
    /// (mirror of the Setup's GabaritIdentity + MarqueeProjectStore).</summary>
    private string ProjectPath(string category, string surfaceId, string scope)
        => Path.Combine(_baseDirectory, "media", CategoryRoot(category), "surfaces", Safe(surfaceId),
            Safe(GabaritSystemId), Safe(scope) + ".project.json");

    /// <summary>True when the surface actually has a template for this scope — lets the
    /// caller skip the whole round trip.</summary>
    public bool HasGabarit(string category, string surfaceId, string scope)
        => File.Exists(ProjectPath(category, surfaceId, scope));

    /// <summary>
    /// Bakes a gabarit in the background; onDone(outputPath) fires on success only.
    /// Jobs are deduplicated on the output path, so a burst of selections renders once.
    /// <paramref name="resolveMedia"/> maps a layer to a real file: the caller owns
    /// media resolution (see the class remarks).
    /// </summary>
    public void RenderInBackground(string category, string surfaceId, string scope, string label,
        int width, int height, Func<MarqueeLayer, string?> resolveMedia,
        IReadOnlyDictionary<string, string> tokens, string outputPath, Action<string> onDone)
    {
        if (width <= 0 || height <= 0) return;
        lock (_sync)
        {
            if (!_inFlight.Add(outputPath)) return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var project = LoadProject(ProjectPath(category, surfaceId, scope));
                // A system without its own game template falls back to the one composed
                // for ALL games — the level of last resort, never another system's.
                if (project == null && scope.StartsWith("game-", StringComparison.OrdinalIgnoreCase))
                {
                    project = LoadProject(ProjectPath(category, surfaceId, "game"));
                }
                if (project == null || !project.Layers.Any(l => !l.Hidden)) return;
                if (Render(project, resolveMedia, tokens, width, height, outputPath))
                {
                    _logger.LogInformation("Gabarit rendered ({Scope}) for {Label} on {Surface} → {Path}",
                        scope, label, surfaceId, outputPath);
                    onDone(outputPath);
                }
                else
                {
                    _logger.LogInformation(
                        "Gabarit ({Scope}) for {Label} resolved no media — nothing written, the chain continues",
                        scope, label);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Gabarit render failed ({Scope}) for {Label}: {Message}", scope, label, ex.Message);
            }
            finally
            {
                lock (_sync) { _inFlight.Remove(outputPath); }
            }
        });
    }

    private MarqueeProject? LoadProject(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<MarqueeProject>(File.ReadAllText(path))
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unreadable gabarit project {Path}: {Message}", path, ex.Message);
            return null;
        }
    }

    private static bool Render(MarqueeProject project, Func<MarqueeLayer, string?> resolveMedia,
        IReadOnlyDictionary<string, string> tokens, int width, int height, string outputPath)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface == null) return false;
        var canvas = surface.Canvas;

        var drew = DrawBackground(canvas, project.Background, width, height, resolveMedia);
        foreach (var layer in project.Layers)
        {
            if (layer.Hidden) continue;
            drew |= layer.Source == "text"
                ? DrawTextLayer(canvas, layer, tokens, width, height)
                : DrawMediaLayer(canvas, layer, width, height, resolveMedia);
        }

        // NEVER write a blank. An empty render becomes the source the chain serves, and
        // a black marquee on every game of a system is exactly what that cost. No
        // pixels, no file — the chain then falls through to the next source, as before.
        if (!drew) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return true;
    }

    /// <summary>solid | gradient | media (cover, blurred). A media background overflows
    /// the frame by blur×2 so the blur never samples past the edges — the same trick the
    /// composer uses to avoid a dark border. A plain black background counts as "nothing
    /// drawn": it must not, on its own, make a blank render look legitimate.</summary>
    private static bool DrawBackground(SKCanvas canvas, MarqueeBackground background, int width, int height,
        Func<MarqueeLayer, string?> resolveMedia)
    {
        canvas.Clear(SKColors.Black);
        switch (background.Kind?.ToLowerInvariant())
        {
            case "gradient":
            {
                // composer: LinearGradientBrush(color, color2, 20°)
                var radians = 20 * Math.PI / 180;
                var end = new SKPoint((float)(width * Math.Cos(radians)), (float)(height * Math.Sin(radians)));
                using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), end,
                    new[] { ParseColor(background.Color, SKColors.Black), ParseColor(background.Color2, SKColors.Black) },
                    null, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(SKRect.Create(0, 0, width, height), paint);
                return true;
            }
            case "media" when !string.IsNullOrWhiteSpace(background.Source):
            {
                var path = resolveMedia(new MarqueeLayer { Source = background.Source! });
                using var bitmap = path != null ? SKBitmap.Decode(path) : null;
                if (bitmap == null) return false; // stays black, like the composer's fallback
                var pad = (float)(background.Blur * 2);
                var zone = SKRect.Create(-pad, -pad, width + pad * 2, height + pad * 2);
                using var paint = new SKPaint();
                if (background.Blur > 0)
                    paint.ImageFilter = SKImageFilter.CreateBlur((float)(background.Blur / 2), (float)(background.Blur / 2));
                DrawCover(canvas, bitmap, zone, paint);
                return true;
            }
            default:
                if (string.IsNullOrWhiteSpace(background.Color) || background.Color == "#000000") return false;
                canvas.Clear(ParseColor(background.Color, SKColors.Black));
                return true;
        }
    }

    /// <summary>Composer geometry: height = scale × surface height, width follows the
    /// bitmap's aspect, centred on (x, y). Then flipH, then rotation, both about the
    /// layer's own centre.</summary>
    private static bool DrawMediaLayer(SKCanvas canvas, MarqueeLayer layer, int width, int height,
        Func<MarqueeLayer, string?> resolveMedia)
    {
        var path = resolveMedia(layer);
        if (path == null) return false;
        using var bitmap = SKBitmap.Decode(path);
        if (bitmap == null || bitmap.Height == 0) return false;

        var h = (float)(layer.Scale * height);
        var w = h * bitmap.Width / bitmap.Height;
        // scale is a share of the HEIGHT, so a very wide logo overflowed the frame with
        // nothing to cap it. Keep it inside, aspect preserved — same rule as the editor.
        if (w > width)
        {
            h *= width / w;
            w = width;
        }
        var cx = (float)(layer.X * width);
        var cy = (float)(layer.Y * height);

        canvas.Save();
        canvas.Translate(cx, cy);
        if (layer.FlipH) canvas.Scale(-1, 1);
        if (Math.Abs(layer.Rotation) > 0.01) canvas.RotateDegrees((float)layer.Rotation);
        using var paint = new SKPaint { Color = SKColors.White.WithAlpha(Alpha(layer.Opacity)) };
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, SKRect.Create(-w / 2f, -h / 2f, w, h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        canvas.Restore();
        return true;
    }

    /// <summary>
    /// A template's text is a TEMPLATE: {name} {year} {developer} {publisher} {system},
    /// resolved for the entry being rendered. Storing the literal string baked the
    /// preview entry's name into every game of the system — the window title, even.
    /// </summary>
    private static bool DrawTextLayer(SKCanvas canvas, MarqueeLayer layer,
        IReadOnlyDictionary<string, string> tokens, int width, int height)
    {
        var text = layer.Text ?? "";
        foreach (var (token, value) in tokens)
            text = text.Replace("{" + token + "}", value, StringComparison.OrdinalIgnoreCase);
        text = text.Trim();
        if (text.Length == 0) return false;

        // in a BOX the type size is its own property: the rectangle is sized by the
        // handles, the reading size by the inspector, and neither drags the other
        var size = (float)Math.Max(4, layer.IsTextBox
            ? layer.FontSize * height
            : layer.FontSize * layer.Scale * height);
        using var typeface = SKTypeface.FromFamilyName(null,
            layer.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint
        {
            Color = ParseColor(layer.TextColor, SKColors.White).WithAlpha(Alpha(layer.Opacity)),
            IsAntialias = true
        };

        var metrics = font.Metrics;
        var lines = layer.WrapWidth > 0
            ? WrapText(text, font, (float)(layer.WrapWidth * width), layer.MaxLines)
            : new[] { text };

        canvas.Save();
        canvas.Translate((float)(layer.X * width), (float)(layer.Y * height));
        if (layer.FlipH) canvas.Scale(-1, 1);
        if (Math.Abs(layer.Rotation) > 0.01) canvas.RotateDegrees((float)layer.Rotation);

        var lineHeight = metrics.Descent - metrics.Ascent + metrics.Leading;
        var blockHeight = lines.Length * lineHeight;
        var boxWidth = (float)(layer.WrapWidth * width);
        var boxHeight = layer.BoxHeight > 0 ? (float)(layer.BoxHeight * height) : blockHeight;

        // the block sits where the layer says INSIDE its box, not always in the middle
        var top = layer.IsTextBox
            ? layer.VAlign?.ToLowerInvariant() switch
            {
                "top" => -boxHeight / 2f,
                "bottom" => boxHeight / 2f - blockHeight,
                _ => -blockHeight / 2f
            }
            : -blockHeight / 2f;

        for (var i = 0; i < lines.Length; i++)
        {
            var advance = font.MeasureText(lines[i]);
            var x = layer.IsTextBox
                ? layer.HAlign?.ToLowerInvariant() switch
                {
                    "left" => -boxWidth / 2f,
                    "right" => boxWidth / 2f - advance,
                    _ => -advance / 2f
                }
                : -advance / 2f;
            canvas.DrawText(lines[i], x, top + i * lineHeight - metrics.Ascent, font, paint);
        }

        canvas.Restore();
        return true;
    }

    /// <summary>
    /// Breaks text on word boundaries to fit a box. A description is 500 to 1500
    /// characters: drawn as one line it ran off both edges of the surface and was, in
    /// practice, unusable. When it still does not fit in <paramref name="maxLines"/>,
    /// the last line is ellipsised rather than silently cut mid-word.
    /// </summary>
    private static string[] WrapText(string text, SKFont font, float maxWidth, int maxLines)
    {
        if (maxWidth <= 0) return new[] { text };
        var lines = new List<string>();
        var current = new StringBuilder();

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth || current.Length == 0)
            {
                current.Clear().Append(candidate);
                continue;
            }

            lines.Add(current.ToString());
            current.Clear().Append(word);
            if (maxLines > 0 && lines.Count == maxLines) break;
        }

        if (current.Length > 0 && (maxLines <= 0 || lines.Count < maxLines)) lines.Add(current.ToString());
        if (lines.Count == 0) return new[] { text };

        if (maxLines > 0 && lines.Count == maxLines)
        {
            var last = lines[^1];
            var consumed = lines.Sum(l => l.Length + 1);
            if (consumed < text.Length)
            {
                while (last.Length > 1 && font.MeasureText(last + "…") > maxWidth)
                    last = last[..^1];
                lines[^1] = last.TrimEnd() + "…";
            }
        }

        return lines.ToArray();
    }

    /// <summary>Cover: fills the zone keeping the aspect, overflow cropped.</summary>
    private static void DrawCover(SKCanvas canvas, SKBitmap bitmap, SKRect zone, SKPaint paint)
    {
        var scale = Math.Max(zone.Width / bitmap.Width, zone.Height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var dest = SKRect.Create(zone.MidX - w / 2f, zone.MidY - h / 2f, w, h);
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
    }

    /// <summary>Mirror of the resolver's mapping: gabarit stores live under
    /// "marquees" / "toppers" / "dmd".</summary>
    private static string CategoryRoot(string category) => category.ToLowerInvariant() switch
    {
        "topper" => "toppers",
        "dmd" or "dmd-virtual" => "dmd",
        _ => "marquees"
    };

    private static byte Alpha(double opacity) => (byte)(Math.Clamp(opacity, 0, 1) * 255);

    private static SKColor ParseColor(string? hex, SKColor fallback)
    {
        hex = (hex ?? "").TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? new SKColor((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF))
            : fallback;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((name ?? "").ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
