using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Policy;

namespace MarqueeManager.Compositions.Core.Resolution;

/// <summary>A discovered media candidate for one link. Provenance travels for the
/// "where does this come from?" UI. Dimensions come from a cached index, never a
/// full decode (spec §27). Protected regions (logo/ROI/lamps) ride along for
/// dynamic framing.</summary>
public sealed record MediaAsset(
    string Path,
    PixelSize? Size,
    bool IsAnimated = false,
    string? Provenance = null,
    ProtectedRegions? Protected = null);

/// <summary>Outcome of asking the asset resolver for a link's media: either a
/// candidate, or a stable reason it is unavailable (mapped straight to a trace).</summary>
public sealed record AssetLookup(MediaAsset? Asset, string? UnavailableCode)
{
    public static readonly AssetLookup Missing = new(null, TraceCodes.SourceMissing);
    public static readonly AssetLookup IngredientsMissing = new(null, TraceCodes.TemplateIngredientsMissing);
    public static readonly AssetLookup Invalid = new(null, TraceCodes.SourceInvalid);
    public static AssetLookup Found(MediaAsset asset) => new(asset, null);
}

/// <summary>Discovers the best media for a link on a target. Impl in the Setup and
/// the runtime; both share the SAME cascade (spec §18.5). Applies the internal
/// Marquee → ScreenMarquee preference inside the Scraped link (not a config link).</summary>
public interface IMediaAssetResolver
{
    AssetLookup Resolve(SourceKind kind, ResolutionContext context);
}

/// <summary>Whether a link needs a baked derivative before it can win, and where a
/// valid one lives. Honors "recadrer en direct" (user decision): a plain homothetic
/// transform the runtime can do live is <see cref="GenerationState.NotRequired"/>;
/// a blur fill / gradient / layer composition / logo layout the runtime cannot do
/// live is Required (or Ready/Stale when a cache exists).</summary>
public sealed record GenerationPlan(GenerationState State, string? DerivativePath = null)
{
    public static readonly GenerationPlan Live = new(GenerationState.NotRequired);
    public static GenerationPlan Ready(string path) => new(GenerationState.Ready, path);
    public static readonly GenerationPlan Required = new(GenerationState.Required);
    public static readonly GenerationPlan Stale = new(GenerationState.Stale);
}

public interface IGenerationPlanner
{
    GenerationPlan Plan(SourceKind kind, MediaAsset asset, FitDecision? fit, ResolutionContext context);
}

/// <summary>Supplies the fully-merged effective policy for a context's surface and
/// scope (surface base + any target delta already applied, §20.1). Impls cache.</summary>
public interface IPresentationPolicyProvider
{
    ScopePolicy PolicyFor(ResolutionContext context);
}

/// <summary>The single shared resolver. Deterministic, generates nothing, writes
/// nothing, does no heavy decode — it decides which link wins and returns a full
/// trace. Called INSIDE the surface/screen loop, never once globally (§18.1).</summary>
public interface IMediaResolutionService
{
    ResolvedMedia Resolve(ResolutionContext context);
}
