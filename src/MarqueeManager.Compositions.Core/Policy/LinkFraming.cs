using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Policy;

/// <summary>
/// Frames one chain link's media for a target — the SINGLE place the runtime and
/// the Setup preview both use. A lighting-pinned target frames every link with the
/// pinned policy; the LOGO link always uses its safe-zone layout (§11, padding +
/// centered, never edge-to-edge); everything else uses the link's fit policy.
/// </summary>
public static class LinkFraming
{
    private static readonly LogoLayout DefaultLogo =
        new(0.06, 0.08, 0.03, new BackgroundSpec(BackgroundKinds.ScopeNeutral));

    public static FitDecision? Compute(
        IFitCalculator fit, SourceKind kind, PixelSize source, PixelSize target,
        SourceSettings settings, ProtectedRegions? protectedRegions = null, FitPolicy? pinned = null)
    {
        if (pinned is not null)
            return fit.Calculate(source, target, pinned, protectedRegions ?? ProtectedRegions.None);

        if (kind == SourceKind.Logo)
            return LogoLayoutCalculator.Place(source, target, settings.LogoLayout ?? DefaultLogo).Fit;

        return settings.Fit is { } policy
            ? fit.Calculate(source, target, policy, protectedRegions ?? ProtectedRegions.None)
            : null;
    }
}
