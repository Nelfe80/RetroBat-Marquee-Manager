using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;

namespace MarqueeManager.Compositions.Core.Presentation;

/// <summary>
/// The real <see cref="IPresentationPolicyProvider"/>: starts from the domain
/// defaults, layers the surface's base delta, then every matching target delta
/// (system- or game-scoped). An absent/empty document yields the pure defaults, so
/// nothing regresses before the user saves anything.
/// </summary>
public sealed class MediaPresentationPolicyProvider : IPresentationPolicyProvider
{
    private readonly MediaPresentationDocument _document;

    public MediaPresentationPolicyProvider(MediaPresentationDocument? document = null)
        => _document = document ?? MediaPresentationDocument.Empty;

    public ScopePolicy PolicyFor(ResolutionContext context)
    {
        var effective = context.Scope == MediaScope.System ? PolicyDefaults.System : PolicyDefaults.Game;

        var surface = _document.Surface(context.SurfaceId);
        var surfaceDelta = context.Scope == MediaScope.System ? surface?.System : surface?.Game;
        effective = PolicyMerge.Apply(effective, surfaceDelta);

        foreach (var target in _document.TargetPolicies)
            if (target.Matches(context))
                effective = PolicyMerge.Apply(effective, target.Delta);

        return effective;
    }
}
