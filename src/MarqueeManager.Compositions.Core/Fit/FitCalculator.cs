using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Fit;

/// <inheritdoc cref="IFitCalculator"/>
public sealed class FitCalculator : IFitCalculator
{
    private const double Epsilon = 1e-6;

    public FitDecision Calculate(PixelSize source, PixelSize target, FitPolicy policy, ProtectedRegions protectedRegions)
    {
        if (!source.IsValid) throw new ArgumentOutOfRangeException(nameof(source), "source dimensions must be positive");
        if (!target.IsValid) throw new ArgumentOutOfRangeException(nameof(target), "target dimensions must be positive");

        return policy.Mode switch
        {
            FitMode.Contain => Place(source, target, ContainScale(source, target), policy, FitMode.Contain, null),
            FitMode.Cover => Place(source, target, CoverScale(source, target), policy, FitMode.Cover, null),
            FitMode.FillHeight => Place(source, target, (double)target.Height / source.Height, policy, FitMode.FillHeight, null),
            FitMode.FillWidth => Place(source, target, (double)target.Width / source.Width, policy, FitMode.FillWidth, null),
            FitMode.Dynamic => Dynamic(source, target, policy, protectedRegions),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy.Mode, "unknown fit mode")
        };
    }

    private static double ContainScale(PixelSize s, PixelSize t)
        => Math.Min((double)t.Width / s.Width, (double)t.Height / s.Height);

    private static double CoverScale(PixelSize s, PixelSize t)
        => Math.Max((double)t.Width / s.Width, (double)t.Height / s.Height);

    // Cover if the crop stays within budget AND no protected region would be cut;
    // otherwise Contain. Equality with the threshold is accepted.
    private FitDecision Dynamic(PixelSize source, PixelSize target, FitPolicy policy, ProtectedRegions protectedRegions)
    {
        var k = CoverScale(source, target);
        double scaledW = source.Width * k, scaledH = source.Height * k;
        double cropX = scaledW > target.Width ? (scaledW - target.Width) / scaledW : 0;
        double cropY = scaledH > target.Height ? (scaledH - target.Height) / scaledH : 0;
        double crop = Math.Max(cropX, cropY);

        if (crop > policy.MaxCrop + Epsilon)
            return Place(source, target, ContainScale(source, target), policy, policy.Fallback, "crop_exceeds_threshold");

        var (dx, dy, reason) = ResolveProtectedShift(source, target, k, protectedRegions);
        if (reason is not null)
            return Place(source, target, ContainScale(source, target), policy, policy.Fallback, reason);

        return Place(source, target, k, policy, FitMode.Cover, null, dx, dy);
    }

    // Places the scaled source on the target and derives every geometry output.
    // dxOverride/dyOverride let Dynamic shift the framing to keep a protected
    // region visible on the cropped axis; a null override uses the aligned offset.
    private static FitDecision Place(
        PixelSize source, PixelSize target, double k, FitPolicy policy,
        FitMode effective, string? fallbackReason,
        double? dxOverride = null, double? dyOverride = null)
    {
        double scaledW = source.Width * k, scaledH = source.Height * k;
        double freeX = target.Width - scaledW;
        double freeY = target.Height - scaledH;

        var alignX = policy.AlignX == HAlign.Auto ? HAlign.Center : policy.AlignX;
        var alignY = policy.AlignY == VAlign.Auto ? VAlign.Center : policy.AlignY;

        double dx = dxOverride ?? OffsetX(freeX, alignX);
        double dy = dyOverride ?? OffsetY(freeY, alignY);

        // Visible target region = scaled image rect clipped to the surface.
        double visLeft = Math.Max(0, dx);
        double visTop = Math.Max(0, dy);
        double visRight = Math.Min(target.Width, dx + scaledW);
        double visBottom = Math.Min(target.Height, dy + scaledH);

        var sourceVisible = new RectD(
            (visLeft - dx) / k,
            (visTop - dy) / k,
            (visRight - visLeft) / k,
            (visBottom - visTop) / k);

        var padding = new Padding(
            Math.Max(0, dx),
            Math.Max(0, dy),
            Math.Max(0, target.Width - (dx + scaledW)),
            Math.Max(0, target.Height - (dy + scaledH)));

        double cropX = Clamp01(1 - sourceVisible.Width / source.Width);
        double cropY = Clamp01(1 - sourceVisible.Height / source.Height);

        return new FitDecision(
            policy.Mode, effective, k,
            new RectD(dx, dy, scaledW, scaledH),
            sourceVisible, padding, cropX, cropY, alignX, alignY, fallbackReason);
    }

    private static double OffsetX(double free, HAlign align) => align switch
    {
        HAlign.Left => 0,
        HAlign.Right => free,
        _ => free / 2 // Center / Auto
    };

    private static double OffsetY(double free, VAlign align) => align switch
    {
        VAlign.Top => 0,
        VAlign.Bottom => free,
        _ => free / 2 // Center / Auto
    };

    // On a cropped axis, choose a framing window that keeps every protected region
    // visible. Returns the offset override for that axis (null = aligned default),
    // or a reason when a region is wider than the window and cannot fit.
    private static (double? dx, double? dy, string? reason) ResolveProtectedShift(
        PixelSize source, PixelSize target, double k, ProtectedRegions protectedRegions)
    {
        if (!protectedRegions.Any) return (null, null, null);

        double scaledW = source.Width * k, scaledH = source.Height * k;
        double? dx = null, dy = null;

        if (scaledW > target.Width + Epsilon)
        {
            double window = target.Width / k; // visible source width, source px
            double pMin = protectedRegions.Regions.Min(r => r.X) * source.Width;
            double pMax = protectedRegions.Regions.Max(r => r.Right) * source.Width;
            if (pMax - pMin > window + Epsilon) return (null, null, "protected_region_horizontal");
            dx = -WindowStart(pMin, pMax, window, source.Width) * k;
        }

        if (scaledH > target.Height + Epsilon)
        {
            double window = target.Height / k;
            double pMin = protectedRegions.Regions.Min(r => r.Y) * source.Height;
            double pMax = protectedRegions.Regions.Max(r => r.Bottom) * source.Height;
            if (pMax - pMin > window + Epsilon) return (null, null, "protected_region_vertical");
            dy = -WindowStart(pMin, pMax, window, source.Height) * k;
        }

        return (dx, dy, null);
    }

    // Window start (source px). Begins at the centered default (matching the
    // Center/Auto alignment) and shifts MINIMALLY — only as far as needed — so the
    // protected span [pMin, pMax] falls fully inside [start, start+window].
    private static double WindowStart(double pMin, double pMax, double window, double sourceLen)
    {
        double maxStart = Math.Max(0, sourceLen - window);
        double start = (sourceLen - window) / 2;      // centered, like Auto/Center
        if (pMin < start) start = pMin;               // nudge left just enough
        if (pMax > start + window) start = pMax - window; // nudge right just enough
        return Math.Clamp(start, 0, maxStart);
    }

    private static double Clamp01(double value)
    {
        if (value < Epsilon) return 0;
        return value > 1 ? 1 : value;
    }
}
