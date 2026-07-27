using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Policy;

/// <summary>Result of laying out a logo: the framing inside its safe zone, or a
/// diagnostic when the surface is too small.</summary>
public sealed record LogoPlacement(FitDecision? Fit, string? Diagnostic)
{
    public const string TooSmall = "surface.too_small_for_logo";
    public bool Ok => Fit is not null;
}

/// <summary>
/// Lays a logo out on a canvas exactly the size of the surface, inside a safe zone
/// (spec §11). The only allowed mode is Contain within the safe box — a logo is
/// never cropped or stretched, and never touches an edge. Percentages apply PER
/// EDGE; each edge's effective padding is at least the configured value, the
/// configured minimum, 3% of the dimension and 2 px.
/// </summary>
public static class LogoLayoutCalculator
{
    private const double MinFraction = 0.03; // 3% floor per edge
    private const double MinPixels = 2;      // 2 px floor per edge

    public static LogoPlacement Place(PixelSize logo, PixelSize target, LogoLayout layout)
    {
        if (!logo.IsValid || !target.IsValid)
            return new LogoPlacement(null, LogoPlacement.TooSmall);

        double padX = EffectivePadding(layout.PaddingX, layout.MinimumPadding, target.Width);
        double padY = EffectivePadding(layout.PaddingY, layout.MinimumPadding, target.Height);

        double usableW = target.Width - 2 * padX;
        double usableH = target.Height - 2 * padY;
        if (usableW < 1 || usableH < 1)
            return new LogoPlacement(null, LogoPlacement.TooSmall);

        // Contain inside the safe box, centered — the logo's apparent size may
        // shrink but never exceeds the safe zone.
        double k = Math.Min(usableW / logo.Width, usableH / logo.Height);
        double scaledW = logo.Width * k, scaledH = logo.Height * k;
        double offX = padX + (usableW - scaledW) / 2;
        double offY = padY + (usableH - scaledH) / 2;

        var fit = new FitDecision(
            FitMode.Contain, FitMode.Contain, k,
            new RectD(offX, offY, scaledW, scaledH),
            new RectD(0, 0, logo.Width, logo.Height), // whole logo visible
            new Padding(offX, offY, target.Width - (offX + scaledW), target.Height - (offY + scaledH)),
            0, 0, HAlign.Center, VAlign.Center, null);

        return new LogoPlacement(fit, null);
    }

    private static double EffectivePadding(double axisFraction, double minimumFraction, int dimension)
    {
        double byAxis = axisFraction * dimension;
        double byMinimum = minimumFraction * dimension;
        double byFloor = MinFraction * dimension;
        return Math.Max(Math.Max(byAxis, byMinimum), Math.Max(byFloor, MinPixels));
    }
}
