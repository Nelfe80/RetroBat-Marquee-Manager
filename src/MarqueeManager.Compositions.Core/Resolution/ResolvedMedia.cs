using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Resolution;

/// <summary>Which fixed-chain link produced the result (spec §6). Ordered as the
/// chains are; <see cref="Neutral"/> is the non-disableable terminal safety net.</summary>
public enum ResolutionSource
{
    Personal,       // "Création graphique" (user project or imported image)
    UserDrop,       // "Mon dossier médias" — a raw file dropped in media\<cat>s\user
    Generated,      // composed from the surface template
    Scraped,        // ready-to-show marquee from APIExpose (Marquee/ScreenMarquee)
    Logo,           // logo/wheel laid out on a safe canvas
    SystemFallback, // (game chain only) the fully resolved system chain
    Neutral,        // the surface's neutral background
    None            // nothing resolved yet (initial/empty)
}

/// <summary>State of the derivative a link may need before it can win (spec §16).</summary>
public enum GenerationState
{
    NotRequired, // directly displayable, or a live homothetic transform suffices
    Ready,       // a valid derivative exists for this exact target
    Required,    // a derivative must be generated
    Stale,       // a derivative exists but its fingerprint no longer matches
    Failed,      // last generation attempt failed
    Unsupported  // cannot be generated (declared, e.g. some video adaptations)
}

/// <summary>One decision the resolver made, as a STABLE, non-localized code. The
/// Setup translates these; the runtime logs them.</summary>
public sealed record ResolutionTraceEntry(ResolutionSource Link, string Code, string? Detail = null)
{
    public override string ToString() => Detail is null ? $"{Link}:{Code}" : $"{Link}:{Code}({Detail})";
}

/// <summary>Stable trace codes (spec §18.2). Never localize or reuse for another meaning.</summary>
public static class TraceCodes
{
    public const string SourceDisabled = "source.disabled";
    public const string SourceMissing = "source.missing";
    public const string SourceInvalid = "source.invalid";
    public const string TemplateIngredientsMissing = "template.ingredients_missing";
    public const string AdaptationRequired = "adaptation.required";
    public const string AdaptationStale = "adaptation.stale";
    public const string SourceSelected = "source.selected";
    public const string FallbackSystem = "fallback.system";
    public const string FallbackNeutral = "fallback.neutral";
    public const string IdentityFrontendMissing = "identity.frontend_missing";
}

/// <summary>
/// The resolver's outcome for one target: which link won, the media, its framing
/// and whether a derivative is needed, plus the full decision trace. The resolver
/// never touches the filesystem or generates anything — it only decides.
/// </summary>
public sealed record ResolvedMedia(
    ResolutionSource Source,
    string? OriginalPath,
    string? EffectivePath,
    PixelSize? OriginalSize,
    PixelSize TargetSize,
    FitDecision? Fit,
    GenerationState Generation,
    bool IsLowResolution,
    bool IsAnimated,
    IReadOnlyList<ResolutionTraceEntry> Trace)
{
    /// <summary>The terminal neutral background: nothing to show, old media cleared.</summary>
    public static ResolvedMedia Neutral(PixelSize target, IReadOnlyList<ResolutionTraceEntry> trace)
        => new(ResolutionSource.Neutral, null, null, null, target, null,
               GenerationState.NotRequired, false, false, trace);
}
