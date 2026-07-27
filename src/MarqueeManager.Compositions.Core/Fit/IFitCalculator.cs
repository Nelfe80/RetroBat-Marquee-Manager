using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Fit;

/// <summary>
/// Pure, deterministic homothety calculator. Given a source, a target and a
/// policy it decides a SINGLE scale factor (scaleX == scaleY) and the resulting
/// framing — never a stretch. Shared verbatim by the Setup preview and the
/// runtime renderer so what the user previews equals what plays, to the pixel.
/// </summary>
public interface IFitCalculator
{
    FitDecision Calculate(PixelSize source, PixelSize target, FitPolicy policy, ProtectedRegions protectedRegions);
}
