using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Resolution;

/// <summary>Normalized dimensional states shown next to each media (spec §8.2).</summary>
public enum DimensionalStatus
{
    ExactDimensions,
    RatioCompatible,
    Resizable,
    AdaptationNeeded,
    Magnified,
    AdaptationToGenerate,
    AdaptationStale,
    UnreadableFormat,
    UnsupportedFormat
}

public sealed record DimensionalReport(
    DimensionalStatus Status,
    PixelSize? Source,
    PixelSize Target,
    double Magnification,  // homothety factor k (1 = no scale)
    bool RatioCompatible,
    double CropX,
    double CropY,
    bool HighMagnification); // k > 2 (spec §8.2: severity high)

/// <summary>
/// Turns raw sizes + a framing + a generation state into the one normalized status
/// the UI shows. Ratio is compatible within 0.5% relative; exact requires pixel
/// equality; any magnification is surfaced, ×2+ flagged high (spec §8.2).
/// </summary>
public static class DimensionalAnalyzer
{
    public const double RatioTolerance = 0.005; // 0.5%

    public static bool IsRatioCompatible(PixelSize source, PixelSize target)
    {
        if (!source.IsValid || !target.IsValid) return false;
        double t = target.Ratio;
        return t > 0 && Math.Abs(source.Ratio - t) / t <= RatioTolerance;
    }

    public static DimensionalReport Analyze(
        PixelSize? source, PixelSize target, FitDecision? fit,
        GenerationState generation = GenerationState.NotRequired)
    {
        if (source is not { IsValid: true } s)
            return new DimensionalReport(DimensionalStatus.UnreadableFormat, source, target, 1, false, 0, 0, false);

        double k = fit?.Scale ?? Math.Min((double)target.Width / s.Width, (double)target.Height / s.Height);
        double cropX = fit?.CropX ?? 0;
        double cropY = fit?.CropY ?? 0;
        bool ratioOk = IsRatioCompatible(s, target);
        bool exact = s == target;
        bool high = k > 2 + 1e-9;

        var status = generation switch
        {
            GenerationState.Unsupported => DimensionalStatus.UnsupportedFormat,
            GenerationState.Required => DimensionalStatus.AdaptationToGenerate,
            GenerationState.Stale => DimensionalStatus.AdaptationStale,
            _ when exact => DimensionalStatus.ExactDimensions,
            _ when ratioOk => DimensionalStatus.RatioCompatible,
            _ when k > 1 + 1e-9 => DimensionalStatus.Magnified,
            _ when cropX > 0 || cropY > 0 => DimensionalStatus.AdaptationNeeded,
            _ => DimensionalStatus.Resizable
        };

        return new DimensionalReport(status, s, target, k, ratioOk, cropX, cropY, high);
    }
}
