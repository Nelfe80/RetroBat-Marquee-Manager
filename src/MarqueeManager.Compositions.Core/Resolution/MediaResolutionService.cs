using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Policy;

namespace MarqueeManager.Compositions.Core.Resolution;

/// <summary>
/// Walks the FIXED chains of spec §6. The order is hard-coded here and is never
/// configurable: no screen may reorder links. A link that is disabled, missing,
/// invalid or not yet displayable (a required/stale derivative) is skipped; the
/// neutral background is the non-disableable terminal.
/// </summary>
public sealed class MediaResolutionService : IMediaResolutionService
{
    // System chain (§6.1): personal → generated → scraped → logo → [neutral].
    private static readonly SourceKind[] SystemChain =
        { SourceKind.Personal, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo };

    // Game chain (§6.2): personal → generated → scraped → logo → SYSTEM chain → [neutral].
    private static readonly SourceKind[] GameChainBeforeFallback =
        { SourceKind.Personal, SourceKind.Generated, SourceKind.Scraped, SourceKind.Logo };

    private readonly IPresentationPolicyProvider _policies;
    private readonly IMediaAssetResolver _assets;
    private readonly IGenerationPlanner _planner;
    private readonly IFitCalculator _fit;

    public MediaResolutionService(
        IPresentationPolicyProvider policies,
        IMediaAssetResolver assets,
        IGenerationPlanner planner,
        IFitCalculator fit)
    {
        _policies = policies;
        _assets = assets;
        _planner = planner;
        _fit = fit;
    }

    public ResolvedMedia Resolve(ResolutionContext context)
    {
        var trace = new List<ResolutionTraceEntry>();
        if (!context.HasFrontendSystem)
            trace.Add(new ResolutionTraceEntry(ResolutionSource.None, TraceCodes.IdentityFrontendMissing));

        return context.Scope == MediaScope.System
            ? ResolveSystem(context, trace)
            : ResolveGame(context, trace);
    }

    private ResolvedMedia ResolveSystem(ResolutionContext context, List<ResolutionTraceEntry> trace)
    {
        var policy = _policies.PolicyFor(context);
        foreach (var kind in SystemChain)
            if (TryLink(kind, policy, context, trace, out var resolved))
                return resolved;

        trace.Add(new ResolutionTraceEntry(ResolutionSource.Neutral, TraceCodes.FallbackNeutral));
        return ResolvedMedia.Neutral(context.Target, trace);
    }

    private ResolvedMedia ResolveGame(ResolutionContext context, List<ResolutionTraceEntry> trace)
    {
        var policy = _policies.PolicyFor(context);
        foreach (var kind in GameChainBeforeFallback)
            if (TryLink(kind, policy, context, trace, out var resolved))
                return resolved;

        // System fallback: run the FULL system chain, already framed for this same
        // surface — the game never applies a second adaptation on top (§6.3).
        if (policy.IsEnabled(SourceKind.SystemFallback))
        {
            var systemContext = context with { Scope = MediaScope.System, StableGameId = null, Rom = null };
            trace.Add(new ResolutionTraceEntry(ResolutionSource.SystemFallback, TraceCodes.FallbackSystem));
            var systemResult = ResolveSystem(systemContext, trace);
            if (systemResult.Source != ResolutionSource.Neutral)
                return systemResult;
            // system chain yielded nothing either → fall through to neutral (already traced)
            return systemResult;
        }

        trace.Add(new ResolutionTraceEntry(ResolutionSource.SystemFallback, TraceCodes.SourceDisabled));
        trace.Add(new ResolutionTraceEntry(ResolutionSource.Neutral, TraceCodes.FallbackNeutral));
        return ResolvedMedia.Neutral(context.Target, trace);
    }

    private bool TryLink(
        SourceKind kind, ScopePolicy policy, ResolutionContext context,
        List<ResolutionTraceEntry> trace, out ResolvedMedia resolved)
    {
        resolved = null!;
        var link = ToSource(kind);

        if (!policy.IsEnabled(kind))
        {
            trace.Add(new ResolutionTraceEntry(link, TraceCodes.SourceDisabled));
            return false;
        }

        var lookup = _assets.Resolve(kind, context);
        if (lookup.Asset is not { } asset)
        {
            trace.Add(new ResolutionTraceEntry(link, lookup.UnavailableCode ?? TraceCodes.SourceMissing));
            return false;
        }
        if (asset.Size is { IsValid: false })
        {
            trace.Add(new ResolutionTraceEntry(link, TraceCodes.SourceInvalid));
            return false;
        }

        var settings = policy.Source(kind)!;
        var fit = ComputeFit(asset, settings, context);
        var plan = _planner.Plan(kind, asset, fit, context);

        switch (plan.State)
        {
            case GenerationState.Required:
                trace.Add(new ResolutionTraceEntry(link, TraceCodes.AdaptationRequired));
                return false;
            case GenerationState.Stale:
                trace.Add(new ResolutionTraceEntry(link, TraceCodes.AdaptationStale));
                return false;
            case GenerationState.Failed:
            case GenerationState.Unsupported:
                // not displayable now → skip to the next link (§6.3)
                trace.Add(new ResolutionTraceEntry(link, TraceCodes.SourceInvalid));
                return false;
        }

        // NotRequired (live transform) or Ready (valid derivative): this link wins.
        trace.Add(new ResolutionTraceEntry(link, TraceCodes.SourceSelected, asset.Provenance));
        resolved = new ResolvedMedia(
            link,
            asset.Path,
            plan.DerivativePath ?? asset.Path,
            asset.Size,
            context.Target,
            fit,
            plan.State,
            IsLowResolution(fit),
            asset.IsAnimated,
            trace);
        return true;
    }

    // A media is framed with the link's fit policy — unless the target is
    // lighting-pinned, in which case EVERY link uses the pinned policy so the lamp
    // coordinates never move (user decision on lighting games).
    private FitDecision? ComputeFit(MediaAsset asset, SourceSettings settings, ResolutionContext context)
    {
        if (asset.Size is not { IsValid: true } size) return null;
        var policy = context.PinnedFit ?? settings.Fit;
        return policy is null ? null : _fit.Calculate(size, context.Target, policy, asset.Protected ?? ProtectedRegions.None);
    }

    private static bool IsLowResolution(FitDecision? fit) => fit is { Scale: > 1.0 + 1e-6 };

    private static ResolutionSource ToSource(SourceKind kind) => kind switch
    {
        SourceKind.Personal => ResolutionSource.Personal,
        SourceKind.Generated => ResolutionSource.Generated,
        SourceKind.Scraped => ResolutionSource.Scraped,
        SourceKind.Logo => ResolutionSource.Logo,
        SourceKind.SystemFallback => ResolutionSource.SystemFallback,
        _ => ResolutionSource.None
    };
}
