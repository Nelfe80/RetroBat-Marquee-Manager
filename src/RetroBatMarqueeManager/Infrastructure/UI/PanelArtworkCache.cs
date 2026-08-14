using System.Windows.Media.Imaging;
using SkiaSharp;

namespace RetroBatMarqueeManager.Infrastructure.UI;

/// <summary>
/// Rasterises the panel SVG that APIExpose writes for EmulationStation themes, so the
/// marquee shows the SAME drawing the themes do.
///
/// Rendered well above its natural size: the panel is drawn at around 420 units wide
/// and stretched across a marquee, so rasterising at 1:1 would show every edge. It is
/// vector artwork — enlarging it costs nothing but pixels.
///
/// Cached by file AND timestamp, because the file name does not identify the picture:
/// every system that is not arcade shares one "default.svg" that is rewritten with each
/// game's colours and functions.
/// </summary>
public static class PanelArtworkCache
{
    /// <summary>Enough to stay sharp on a 4K marquee without making a 20 MB bitmap.</summary>
    private const double Supersample = 4.0;

    private const double MaxDimension = 4096;

    private static readonly Dictionary<string, BitmapSource> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    /// <summary>The drawing as a frozen bitmap, or null when it cannot be read — the
    /// caller then draws the plain panel rather than an empty rectangle.</summary>
    public static BitmapSource? Render(string path, double width, double height)
    {
        if (string.IsNullOrWhiteSpace(path) || width <= 0 || height <= 0) return null;

        string key;
        try
        {
            if (!File.Exists(path)) return null;
            key = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{width}x{height}";
        }
        catch
        {
            return null;
        }

        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
        }

        var rendered = Rasterise(path, width, height);
        if (rendered is null) return null;

        lock (Lock)
        {
            // one panel per game, a handful of games in a session: the cache is small by
            // nature, but a long browse must not grow it without end
            if (Cache.Count > 24) Cache.Clear();
            Cache[key] = rendered;
        }

        return rendered;
    }

    private static BitmapSource? Rasterise(string path, double width, double height)
    {
        try
        {
            using var svg = new Svg.Skia.SKSvg();
            if (svg.Load(path) is null || svg.Picture is null) return null;

            var scale = Math.Min(Supersample, MaxDimension / Math.Max(width, height));
            var pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(height * scale));

            using var bitmap = new SKBitmap(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                // transparent: the panel sits over whatever the surface already shows
                canvas.Clear(SKColors.Transparent);
                canvas.Scale((float)(pixelWidth / width), (float)(pixelHeight / height));
                canvas.DrawPicture(svg.Picture);
            }

            // through PNG rather than a raw buffer copy: it keeps the whole path in safe
            // code, and this runs once per game, not per frame
            using var image = SKImage.FromBitmap(bitmap);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encoded is null) return null;

            using var stream = new MemoryStream();
            encoded.SaveTo(stream);
            stream.Position = 0;

            var target = new BitmapImage();
            target.BeginInit();
            target.StreamSource = stream;
            target.CacheOption = BitmapCacheOption.OnLoad; // decode now: the stream dies here
            target.EndInit();
            target.Freeze(); // handed to the UI thread from wherever this ran
            return target;
        }
        catch
        {
            return null;
        }
    }
}
