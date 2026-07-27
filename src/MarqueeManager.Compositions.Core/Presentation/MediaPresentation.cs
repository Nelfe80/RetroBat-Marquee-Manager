using MarqueeManager.Compositions.Core.Policy;
using MarqueeManager.Compositions.Core.Resolution;

namespace MarqueeManager.Compositions.Core.Presentation;

/// <summary>The per-surface base deltas (over <see cref="PolicyDefaults"/>) for the
/// system and game scopes.</summary>
public sealed record SurfaceScopeDeltas(ScopePolicyDelta? System = null, ScopePolicyDelta? Game = null);

/// <summary>
/// One <c>targetPolicies</c> entry (spec §20.1): a delta that applies to a specific
/// target. A system entry keys on frontend system; a game entry keys on the
/// canonical system + stable game id (rom kept for search/back-compat).
/// </summary>
public sealed record TargetPolicy(
    MediaScope Scope,
    string SurfaceId,
    string? FrontendSystem,
    string? CanonicalSystem,
    string? GameId,
    string? Rom,
    ScopePolicyDelta Delta)
{
    /// <summary>True when this entry applies to the given resolution context.</summary>
    public bool Matches(ResolutionContext context)
    {
        if (Scope != context.Scope) return false;
        if (!Eq(SurfaceId, context.SurfaceId)) return false;
        if (!Eq(FrontendSystem, context.FrontendSystem)) return false;
        if (Scope == MediaScope.Game)
        {
            if (CanonicalSystem is not null && !Eq(CanonicalSystem, context.CanonicalSystem)) return false;
            // a game entry must pin the game: by stable id (preferred) or by rom
            if (GameId is not null) return Eq(GameId, context.StableGameId);
            if (Rom is not null) return Eq(Rom, context.Rom);
            return false;
        }
        return true;
    }

    private static bool Eq(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The whole <c>state/media-presentation.json</c> (schema
/// marqueemanager.media-presentation.v1): per-surface base deltas plus a flat list
/// of target deltas. Pure data — reading/writing the file lives in the Setup and
/// runtime; parsing/merging is shared here.
/// </summary>
public sealed record MediaPresentationDocument(
    IReadOnlyDictionary<string, SurfaceScopeDeltas> Surfaces,
    IReadOnlyList<TargetPolicy> TargetPolicies)
{
    public const string SchemaId = "marqueemanager.media-presentation.v1";
    public const string Generator = "MarqueeManagerSetup";

    public static MediaPresentationDocument Empty { get; } =
        new(new Dictionary<string, SurfaceScopeDeltas>(StringComparer.OrdinalIgnoreCase), Array.Empty<TargetPolicy>());

    public SurfaceScopeDeltas? Surface(string surfaceId)
        => Surfaces.TryGetValue(surfaceId, out var s) ? s : null;
}
