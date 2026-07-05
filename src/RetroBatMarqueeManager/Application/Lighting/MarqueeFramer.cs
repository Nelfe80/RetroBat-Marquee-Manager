using SkiaSharp;

namespace RetroBatMarqueeManager.Application.Lighting;

/// <summary>
/// Content-aware framing: fill the window height by cropping the sides, but never
/// cut through the title. Column edge energy locates the artwork/text; the crop
/// window slides to the position that cuts the least content (naturally aligning
/// left or right when the title sits on one side). If significant content lives at
/// both extremities, framing falls back to contain (letterbox).
/// </summary>
public static class MarqueeFramer
{
    private const int AnalysisWidth = 320;
    /// <summary>Max fraction of total content energy allowed in the cropped-away margins.</summary>
    private const double MaxCutEnergyFraction = 0.10;

    public sealed record Framing(int Width, int Height, SKRectI? SourceCrop, string Label);

    public static Framing Choose(SKBitmap source, int surfaceWidth, int surfaceHeight, double fillHeightMaxCrop,
        bool logoMode = false, (float Min, float Max)? mustInclude = null)
    {
        // system logos: centered, comfortably sized, never cropped
        if (logoMode)
        {
            var logoScale = Math.Min((double)surfaceWidth / source.Width, (double)surfaceHeight / source.Height) * 0.80;
            return new Framing(
                Math.Max(1, (int)Math.Round(source.Width * logoScale)),
                Math.Max(1, (int)Math.Round(source.Height * logoScale)),
                null, "logo centered");
        }

        var widthAtHeightFill = (double)source.Width * surfaceHeight / source.Height;
        if (widthAtHeightFill <= surfaceWidth || fillHeightMaxCrop <= 0)
            return Contain(source, surfaceWidth, surfaceHeight, "contain");

        var cropFraction = 1.0 - surfaceWidth / widthAtHeightFill;
        if (cropFraction > fillHeightMaxCrop)
            return Contain(source, surfaceWidth, surfaceHeight, "contain (crop too large)");

        // DOF lamps present: the crop window must contain ALL lamps — centered on
        // their span, never sliding one beacon out of frame
        if (mustInclude is { } span)
        {
            var keptFraction = surfaceWidth / widthAtHeightFill;
            if (span.Max - span.Min > keptFraction + 0.001)
                return Contain(source, surfaceWidth, surfaceHeight, "contain (lamps wider than crop)");
            var x0Fraction = Math.Clamp((span.Min + span.Max) / 2 - keptFraction / 2, 0, 1 - keptFraction);
            x0Fraction = Math.Min(x0Fraction, span.Min);
            x0Fraction = Math.Max(x0Fraction, span.Max - keptFraction);
            x0Fraction = Math.Clamp(x0Fraction, 0, 1 - keptFraction);
            var lampCropWidth = (int)Math.Round(source.Width * keptFraction);
            var lampX0 = Math.Clamp((int)Math.Round(x0Fraction * source.Width), 0, source.Width - lampCropWidth);
            return new Framing(surfaceWidth, surfaceHeight,
                new SKRectI(lampX0, 0, lampX0 + lampCropWidth, source.Height),
                $"fill-height lamps-centered (cut {cropFraction:P0})");
        }

        // where is the content? column edge energy on a downscaled copy
        var energy = ComputeColumnEnergy(source);
        var total = energy.Sum();
        var cropWidthSource = (int)Math.Round(source.Width * (surfaceWidth / widthAtHeightFill));
        var cropColumns = (int)Math.Round((double)energy.Length * cropWidthSource / source.Width);
        cropColumns = Math.Clamp(cropColumns, 1, energy.Length);

        // slide the window: cut energy = everything outside [offset, offset+cropColumns)
        var prefix = new double[energy.Length + 1];
        for (var i = 0; i < energy.Length; i++) prefix[i + 1] = prefix[i] + energy[i];
        var bestOffset = 0;
        var bestCut = double.MaxValue;
        var maxOffset = energy.Length - cropColumns;
        var step = Math.Max(1, maxOffset / 48);
        for (var offset = 0; offset <= maxOffset; offset += step)
        {
            var cut = prefix[offset] + (total - prefix[offset + cropColumns]);
            if (cut < bestCut) { bestCut = cut; bestOffset = offset; }
        }

        if (total > 0 && bestCut / total > MaxCutEnergyFraction)
            return Contain(source, surfaceWidth, surfaceHeight, "contain (content at both ends)");

        var x0 = (int)Math.Round((double)bestOffset / energy.Length * source.Width);
        x0 = Math.Clamp(x0, 0, source.Width - cropWidthSource);
        var side = maxOffset == 0 ? "center"
            : bestOffset < maxOffset * 0.25 ? "left"
            : bestOffset > maxOffset * 0.75 ? "right"
            : "center";
        return new Framing(surfaceWidth, surfaceHeight,
            new SKRectI(x0, 0, x0 + cropWidthSource, source.Height),
            $"fill-height {side} (cut {cropFraction:P0} cols, {(total > 0 ? bestCut / total : 0):P0} energy)");
    }

    private static Framing Contain(SKBitmap source, int surfaceWidth, int surfaceHeight, string label)
    {
        var scale = Math.Min((double)surfaceWidth / source.Width, (double)surfaceHeight / source.Height);
        return new Framing(
            Math.Max(1, (int)Math.Round(source.Width * scale)),
            Math.Max(1, (int)Math.Round(source.Height * scale)),
            null, label);
    }

    /// <summary>
    /// Horizontal edge energy per column on a small grayscale copy — text and art
    /// score high, flat background scores ~0. Outlier columns (thin decorative
    /// borders) are clamped so they cannot veto the crop on their own.
    /// </summary>
    private static double[] ComputeColumnEnergy(SKBitmap source)
    {
        var width = Math.Min(AnalysisWidth, source.Width);
        var height = Math.Max(8, (int)Math.Round((double)source.Height * width / source.Width));
        var info = new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque);
        using var small = source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (small == null) return new double[width];

        var pixels = new byte[width * height];
        System.Runtime.InteropServices.Marshal.Copy(small.GetPixels(), pixels, 0, pixels.Length);

        var energy = new double[width];
        for (var y = 0; y < height; y++)
        for (var x = 1; x < width; x++)
            energy[x] += Math.Abs(pixels[y * width + x] - pixels[y * width + x - 1]);

        // clamp spikes (thin frame borders) to 3x the mean column energy
        var mean = energy.Average();
        if (mean > 0)
        {
            var cap = mean * 3;
            for (var x = 0; x < width; x++) energy[x] = Math.Min(energy[x], cap);
        }

        // light smoothing so single-column gaps don't fragment the content zone
        var smoothed = new double[width];
        for (var x = 0; x < width; x++)
        {
            var sum = energy[x];
            var n = 1;
            if (x > 0) { sum += energy[x - 1]; n++; }
            if (x < width - 1) { sum += energy[x + 1]; n++; }
            smoothed[x] = sum / n;
        }
        return smoothed;
    }
}
