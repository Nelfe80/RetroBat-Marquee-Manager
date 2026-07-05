using System.Runtime.InteropServices;
using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// The generated per-marquee lighting maps (CDC §16), at render resolution.
/// Composition is a drive-lerp: final = unlit×(1−drive) + lit×drive, so at full
/// drive the render is exactly the (light-tinted) source — no saturation loss.
/// Transmission/tint stay implicit in (lit − unlit); explicit maps return with
/// the local colored lamps of Phase 5, where colored light must cross the ink.
/// </summary>
public sealed class LightingMaps : IDisposable
{
    public required SKBitmap Unlit { get; init; }
    public required SKBitmap Lit { get; init; }
    public int Width => Unlit.Width;
    public int Height => Unlit.Height;

    public void Dispose()
    {
        Unlit.Dispose();
        Lit.Dispose();
    }
}

/// <summary>
/// Backlight geometry behind the glass: tube positions and light falloff. Chosen
/// from the marquee aspect ratio (wide → single center tube, tall → dual tubes)
/// and the lighting technology (LED → nearly uniform panel).
/// </summary>
public sealed record BacklightProfile(float TubeY1, float TubeY2, bool TwoTubes, float Sigma, float Ambient)
{
    public static BacklightProfile Resolve(bool ledUniform, double aspectRatio)
    {
        if (ledUniform)
            return new BacklightProfile(0.5f, 0.5f, false, 0.55f, 0.85f);
        return aspectRatio >= 2.2
            ? new BacklightProfile(0.50f, 0.50f, false, 0.26f, 0.30f)   // fluorescent_uniform
            : new BacklightProfile(0.30f, 0.70f, true, 0.17f, 0.26f);  // fluorescent_dual
    }
}

/// <summary>
/// Generates the unlit / lit map pair from the received marquee image (CDC §16).
/// Everything static per scene is baked here — lamp light color, aging, horizontal
/// end falloff — so the per-frame shader stays minimal on the CPU raster backend.
/// Maps are generated at the target render size (§27.3), never at source size.
/// </summary>
public static class AutoMapGenerator
{
    public static LightingMaps Generate(SKBitmap source, int targetWidth, int targetHeight,
        ResolvedLightProfile profile, SKRectI? sourceCrop)
    {
        var info = new SKImageInfo(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var scaled = ScaleTo(source, info, sourceCrop);

        var count = targetWidth * targetHeight * 4;
        var src = new byte[count];
        Marshal.Copy(scaled.GetPixels(), src, 0, count);

        var unlit = new byte[count];
        var lit = new byte[count];
        var endFalloff = ComputeEndFalloff(targetWidth);
        var (cornerX, cornerY) = ComputeCornerFalloff(targetWidth, targetHeight);
        var aging = (float)Math.Clamp(profile.Aging, 0, 1);

        // lamp light color × aged-material transmission loss (slight yellowing)
        var litR = profile.Bulb.ColorR * (1f - 0.10f * aging);
        var litG = profile.Bulb.ColorG * (1f - 0.16f * aging);
        var litB = profile.Bulb.ColorB * (1f - 0.30f * aging);

        // unlit print: darkened, detail preserved, aging warms and dims it (§16.5)
        var unlitR = 1f - 0.05f * aging;
        var unlitG = 0.98f - 0.12f * aging;
        var unlitB = 0.95f - 0.28f * aging;

        for (var i = 0; i < count; i += 4)
        {
            var pixel = i / 4;
            var x = pixel % targetWidth;
            var y = pixel / targetWidth;
            // dark corners: light never fully reaches the box angles — the corner
            // term only bites where BOTH edge proximities are high
            var corner = 1f - CornerDarkness * (1f - cornerX[x]) * (1f - cornerY[y]);
            var falloff = endFalloff[x] * corner;
            var b = src[i] / 255f;
            var g = src[i + 1] / 255f;
            var r = src[i + 2] / 255f;

            lit[i] = (byte)(Math.Clamp(b * litB * falloff, 0f, 1f) * 255f);
            lit[i + 1] = (byte)(Math.Clamp(g * litG * falloff, 0f, 1f) * 255f);
            lit[i + 2] = (byte)(Math.Clamp(r * litR * falloff, 0f, 1f) * 255f);
            lit[i + 3] = 255;

            unlit[i] = (byte)(Math.Clamp((b * 0.22f + 0.02f) * unlitB, 0f, 1f) * 255f);
            unlit[i + 1] = (byte)(Math.Clamp((g * 0.22f + 0.02f) * unlitG, 0f, 1f) * 255f);
            unlit[i + 2] = (byte)(Math.Clamp((r * 0.22f + 0.02f) * unlitR, 0f, 1f) * 255f);
            unlit[i + 3] = 255;
        }

        return new LightingMaps
        {
            Unlit = ToBitmap(unlit, info),
            Lit = ToBitmap(lit, info)
        };
    }

    /// <summary>
    /// Per-tube vertical glow profiles (gaussian falloff), used by the renderer to
    /// rebuild the dynamic 1D shape lookup each frame from live tube intensities.
    /// </summary>
    public static (float[] Tube1, float[]? Tube2) ComputeTubeRows(int height, BacklightProfile profile)
    {
        var tube1 = new float[height];
        var tube2 = profile.TwoTubes ? new float[height] : null;
        for (var y = 0; y < height; y++)
        {
            var v = (y + 0.5f) / height;
            var d1 = v - profile.TubeY1;
            tube1[y] = MathF.Exp(-(d1 * d1) / (2f * profile.Sigma * profile.Sigma));
            if (tube2 != null)
            {
                var d2 = v - profile.TubeY2;
                tube2[y] = MathF.Exp(-(d2 * d2) / (2f * profile.Sigma * profile.Sigma));
            }
        }
        return (tube1, tube2);
    }

    /// <summary>How dark the box corners get (0..1) relative to the lit surface.</summary>
    private const float CornerDarkness = 0.55f;

    /// <summary>
    /// Edge proximity ramps for the corner vignette: 0 at the border, 1 once away
    /// from it (25% of width, 40% of height), smoothstepped.
    /// </summary>
    private static (float[] X, float[] Y) ComputeCornerFalloff(int width, int height)
    {
        var fx = new float[width];
        for (var x = 0; x < width; x++)
        {
            var edge = Math.Min(x, width - 1 - x) / (width * 0.25f);
            fx[x] = SmoothStep(0f, 1f, edge);
        }
        var fy = new float[height];
        for (var y = 0; y < height; y++)
        {
            var edge = Math.Min(y, height - 1 - y) / (height * 0.40f);
            fy[y] = SmoothStep(0f, 1f, edge);
        }
        return (fx, fy);
    }

    /// <summary>End falloff: tube ends and cabinet walls absorb light. Static, baked into the lit map.</summary>
    private static float[] ComputeEndFalloff(int width)
    {
        var col = new float[width];
        for (var x = 0; x < width; x++)
        {
            var u = (x + 0.5f) / width;
            var ends = SmoothStep(0f, 0.06f, u) * SmoothStep(1f, 0.94f, u);
            col[x] = 0.80f + 0.20f * ends;
        }
        return col;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static SKBitmap ToBitmap(byte[] pixels, SKImageInfo info)
    {
        var bitmap = new SKBitmap(info);
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        return bitmap;
    }

    private static SKBitmap ScaleTo(SKBitmap source, SKImageInfo info, SKRectI? sourceCrop)
    {
        var bitmap = new SKBitmap(info);
        using var surface = SKSurface.Create(info, bitmap.GetPixels(), bitmap.RowBytes)
                            ?? throw new InvalidOperationException("Marquee scale surface failed");
        using var image = SKImage.FromBitmap(source);
        var src = sourceCrop.HasValue
            ? SKRect.Create(sourceCrop.Value.Left, sourceCrop.Value.Top, sourceCrop.Value.Width, sourceCrop.Value.Height)
            : SKRect.Create(source.Width, source.Height);
        surface.Canvas.DrawImage(image, src, SKRect.Create(info.Width, info.Height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        surface.Canvas.Flush();
        return bitmap;
    }
}
