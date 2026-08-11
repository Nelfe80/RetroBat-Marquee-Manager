using System.Security.Cryptography;
using System.Text;
using RetroBatMarqueeManager.Core.Surfaces;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Media;

/// <summary>
/// Flattens the surface's own layer stack into ONE cached image, so the compositing
/// that sits under the lighting engine becomes a media source like any other
/// (docs\RENDU-DYNAMIQUE.md). The lighting engine keeps its one-line contract — a
/// path in, a lit image out — and never learns to read a layer stack.
///
/// Generalizes <see cref="CompositionTemplateRenderer"/>: same Skia-off-thread,
/// deduplicated, "pending → updated" mechanics, but the frozen recipe
/// (fanart + gradient + logo) is replaced by the surface's DECLARED layers.
/// </summary>
public sealed class DynamicSurfaceRenderer
{
    /// <summary>Layer kinds a still image can faithfully stand in for. Anything else
    /// (video, cycling card, text, web embed) stays live and BREAKS the run — see
    /// <see cref="FlattenableRun"/>.</summary>
    private static readonly HashSet<string> Flattenable = new(StringComparer.OrdinalIgnoreCase)
    {
        "media.fanart", "media.logo", "media.image", "shape.gradient"
    };

    private readonly string _baseDirectory;
    private readonly ILogger _logger;
    private readonly HashSet<string> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public DynamicSurfaceRenderer(string baseDirectory, ILogger logger)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;
    }

    /// <summary>
    /// The contiguous run of flattenable layers immediately BELOW the lighting
    /// engine, for one display state. Declaration order is back-to-front, so "below"
    /// means "declared earlier"; we walk backwards from the engine and stop at the
    /// first layer a still image cannot stand in for.
    ///
    /// A layer that is not active in this state is invisible, so it neither joins the
    /// run nor interrupts it — it is simply skipped.
    ///
    /// Returned back-to-front, ready to draw.
    /// </summary>
    public static IReadOnlyList<ComponentDefinition> FlattenableRun(SurfaceDefinition surface, string scene)
    {
        var components = surface.Components;

        // several engines can coexist (one per state); bind to the front-most one
        // that participates in this state
        var engine = -1;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i].Type.Equals("lighting.engine", StringComparison.OrdinalIgnoreCase)
                && components[i].ActiveIn(scene))
                engine = i;
        }
        if (engine < 0) return Array.Empty<ComponentDefinition>();

        var run = new List<ComponentDefinition>();
        for (var i = engine - 1; i >= 0; i--)
        {
            var component = components[i];
            if (!component.ActiveIn(scene)) continue;              // invisible here: no effect
            if (!Flattenable.Contains(component.Type)) break;      // animated/live: run stops
            run.Add(component);
        }
        run.Reverse(); // back-to-front
        return run;
    }

    /// <summary>media\&lt;cat&gt;\.cache\surfaces\&lt;id&gt;\{systems\&lt;sys&gt; | games\&lt;sys&gt;\&lt;rom&gt;}\&lt;scene&gt;.png</summary>
    public string CachePath(string category, string surfaceId, string system, string? rom, string scene, bool systemScope)
    {
        var root = Path.Combine(_baseDirectory, "media", CategoryRoot(category), ".cache", "surfaces", Safe(surfaceId));
        var scoped = systemScope || string.IsNullOrEmpty(rom)
            ? Path.Combine(root, "systems", Safe(system))
            : Path.Combine(root, "games", Safe(system), Safe(rom!));
        return Path.Combine(scoped, Safe(scene) + ".png");
    }

    /// <summary>
    /// Everything the render depends on, hashed. Miss one input and we light a stale
    /// composition — the failure mode is silent and unpleasant to spot, so the key
    /// deliberately covers the WHOLE recipe: which layers were included, their
    /// geometry and options, and the identity (path + mtime + length) of every media
    /// they resolved to, plus the surface size and the display state.
    /// </summary>
    public static string CacheKey(IReadOnlyList<ComponentDefinition> run, int width, int height,
        string scene, Func<ComponentDefinition, string?> resolveMedia)
    {
        var builder = new StringBuilder();
        builder.Append(scene).Append('|').Append(width).Append('x').Append(height);
        foreach (var component in run)
        {
            builder.Append('|').Append(component.Type)
                .Append(';').Append(component.X.ToString("F4")).Append(',').Append(component.Y.ToString("F4"))
                .Append(',').Append(component.W.ToString("F4")).Append(',').Append(component.H.ToString("F4"))
                .Append(';').Append(component.Option("stretch"))
                .Append(';').Append(component.Option("kind"))
                .Append(';').Append(component.Option("color")).Append(',').Append(component.Option("direction"))
                .Append(',').Append(component.Option("opacity"));

            var path = resolveMedia(component);
            builder.Append(';');
            if (path is { Length: > 0 } && File.Exists(path))
            {
                var info = new FileInfo(path);
                builder.Append(path).Append(',').Append(info.LastWriteTimeUtc.Ticks).Append(',').Append(info.Length);
            }
            else builder.Append("none");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..32];
    }

    /// <summary>Path of the sidecar holding the key the cached PNG was rendered from.</summary>
    private static string KeyPath(string outputPath) => outputPath + ".key";

    /// <summary>True when the cached render is present AND still matches its recipe.</summary>
    public bool IsFresh(string outputPath, string key)
    {
        try
        {
            return File.Exists(outputPath)
                   && File.Exists(KeyPath(outputPath))
                   && File.ReadAllText(KeyPath(outputPath)).Trim() == key;
        }
        catch { return false; }
    }

    /// <summary>Background render; onDone(outputPath) fires on success only. Jobs are
    /// deduplicated on the output path, so a burst of selections renders once.</summary>
    public void RenderInBackground(IReadOnlyList<ComponentDefinition> run, int width, int height,
        string scene, Func<ComponentDefinition, string?> resolveMedia, string outputPath, Action<string> onDone)
    {
        lock (_sync)
        {
            if (!_inFlight.Add(outputPath)) return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var key = CacheKey(run, width, height, scene, resolveMedia);
                if (Render(run, width, height, resolveMedia, outputPath))
                {
                    try { File.WriteAllText(KeyPath(outputPath), key); } catch { /* cache only */ }
                    _logger.LogInformation("Dynamic surface render: {Count} layer(s) → {Path}", run.Count, outputPath);
                    onDone(outputPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Dynamic surface render failed ({Path}): {Message}", outputPath, ex.Message);
            }
            finally
            {
                lock (_sync) { _inFlight.Remove(outputPath); }
            }
        });
    }

    /// <summary>
    /// Draws the run back-to-front, mirroring ComponentHost's rules exactly: rects are
    /// FRACTIONS of the surface, a media is never distorted ("fill" covers and crops,
    /// anything else fits and letterboxes), a gradient runs from transparent to its
    /// colour at `opacity` along `direction`.
    /// </summary>
    public bool Render(IReadOnlyList<ComponentDefinition> run, int width, int height,
        Func<ComponentDefinition, string?> resolveMedia, string outputPath)
    {
        if (run.Count == 0 || width <= 0 || height <= 0) return false;

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (surface == null) return false;
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        var drewSomething = false;
        foreach (var component in run)
        {
            var rect = SKRect.Create(
                (float)(component.X * width), (float)(component.Y * height),
                (float)Math.Max(1, component.W * width), (float)Math.Max(1, component.H * height));

            if (component.Type.Equals("shape.gradient", StringComparison.OrdinalIgnoreCase))
            {
                DrawGradient(canvas, component, rect);
                drewSomething = true;
                continue;
            }

            var path = resolveMedia(component);
            if (path is not { Length: > 0 } || !File.Exists(path)) continue;
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap == null) continue;
            DrawMedia(canvas, bitmap, rect, component.Option("stretch") == "fill");
            drewSomething = true;
        }

        if (!drewSomething) return false;

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Create(outputPath);
        data.SaveTo(stream);
        return true;
    }

    /// <summary>"fill" = cover the zone keeping aspect (overflow cropped); otherwise
    /// fit inside it. Never Stretch.Fill — a media is never distorted.</summary>
    private static void DrawMedia(SKCanvas canvas, SKBitmap bitmap, SKRect zone, bool fill)
    {
        var scale = fill
            ? Math.Max(zone.Width / bitmap.Width, zone.Height / bitmap.Height)
            : Math.Min(zone.Width / bitmap.Width, zone.Height / bitmap.Height);
        var w = bitmap.Width * scale;
        var h = bitmap.Height * scale;
        var dest = SKRect.Create(zone.MidX - w / 2f, zone.MidY - h / 2f, w, h);

        canvas.Save();
        if (fill) canvas.ClipRect(zone); // cover crops to its zone
        using var image = SKImage.FromBitmap(bitmap);
        canvas.DrawImage(image, dest, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        canvas.Restore();
    }

    private static void DrawGradient(SKCanvas canvas, ComponentDefinition component, SKRect zone)
    {
        var color = ParseColor(component.Option("color", "#000000"));
        var opacity = double.TryParse(component.Option("opacity", "0.7"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, 0, 1)
            : 0.7;

        var (start, end) = component.Option("direction", "down").ToLowerInvariant() switch
        {
            "up" => (new SKPoint(zone.Left, zone.Bottom), new SKPoint(zone.Left, zone.Top)),
            "left" => (new SKPoint(zone.Right, zone.Top), new SKPoint(zone.Left, zone.Top)),
            "right" => (new SKPoint(zone.Left, zone.Top), new SKPoint(zone.Right, zone.Top)),
            _ => (new SKPoint(zone.Left, zone.Top), new SKPoint(zone.Left, zone.Bottom))
        };

        using var shader = SKShader.CreateLinearGradient(start, end,
            new[] { color.WithAlpha(0), color.WithAlpha((byte)(opacity * 255)) }, null, SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(zone, paint);
    }

    private static SKColor ParseColor(string hex)
    {
        hex = (hex ?? "").TrimStart('#');
        return hex.Length == 6 && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? new SKColor((byte)(v >> 16), (byte)(v >> 8 & 0xFF), (byte)(v & 0xFF))
            : SKColors.Black;
    }

    /// <summary>Setup writes the surface caches under "marquees"/"toppers"/"dmd".</summary>
    private static string CategoryRoot(string category)
        => category.Equals("dmd", StringComparison.OrdinalIgnoreCase)
            ? "dmd"
            : category.ToLowerInvariant() + "s";

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((name ?? "").ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
