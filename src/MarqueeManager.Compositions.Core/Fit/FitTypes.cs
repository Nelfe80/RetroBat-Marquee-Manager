using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Fit;

/// <summary>How a media is framed into a surface. Every mode is homothetic
/// (scaleX == scaleY); there is deliberately NO stretch mode.</summary>
public enum FitMode
{
    /// <summary>Whole image visible, no crop, letterbox/pillarbox may remain.</summary>
    Contain,
    /// <summary>Whole surface covered, one axis cropped when ratios differ.</summary>
    Cover,
    /// <summary>Height matches the target exactly; width follows by homothety.</summary>
    FillHeight,
    /// <summary>Width matches the target exactly; height follows by homothety.</summary>
    FillWidth,
    /// <summary>Cover if the crop stays within budget and no protected region is
    /// cut; otherwise falls back to <see cref="Contain"/>.</summary>
    Dynamic
}

public enum HAlign { Left, Center, Right, Auto }
public enum VAlign { Top, Center, Bottom, Auto }

/// <summary>The framing request for one source on one target.</summary>
public sealed record FitPolicy(
    FitMode Mode,
    HAlign AlignX = HAlign.Auto,
    VAlign AlignY = VAlign.Auto,
    // Dynamic only: max fraction (0..1) of the source that may be cropped on the
    // cropped axis before falling back to Contain. Equality is accepted.
    double MaxCrop = 0.30,
    FitMode Fallback = FitMode.Contain);

/// <summary>Regions of the SOURCE (in fractions) that must not be cropped away —
/// logo, declared ROI, lamp/output extent, detected salient content. Consulted
/// only by <see cref="FitMode.Dynamic"/>.</summary>
public sealed record ProtectedRegions(IReadOnlyList<RelativeRect> Regions)
{
    public static readonly ProtectedRegions None = new(Array.Empty<RelativeRect>());
    public bool Any => Regions.Count > 0;
}

/// <summary>The homothetic framing result. A single <see cref="Scale"/> is applied
/// to both axes; the caller (renderer or preview) draws <see cref="TargetRect"/>
/// and everything geometry-dependent (lamps, tubes, maps) reuses the same values.</summary>
public sealed record FitDecision(
    FitMode RequestedMode,
    // Contain when Dynamic fell back, Cover when Dynamic accepted, else == requested.
    FitMode EffectiveMode,
    double Scale,
    // The scaled source rect placed on the target canvas (may exceed the target
    // on a cropped axis; the renderer clips to the surface).
    RectD TargetRect,
    // The portion of the source actually visible, in source pixels.
    RectD SourceVisible,
    Padding Padding,
    // Fraction (0..1) of the source cropped on each axis.
    double CropX,
    double CropY,
    // Auto resolved to a concrete alignment (deterministic across Setup/runtime).
    HAlign AlignX,
    VAlign AlignY,
    // Non-null only when Dynamic fell back to Contain; a stable, non-localized code.
    string? FallbackReason)
{
    public bool FellBack => FallbackReason is not null;
    public double CropPercent => Math.Max(CropX, CropY);
}
