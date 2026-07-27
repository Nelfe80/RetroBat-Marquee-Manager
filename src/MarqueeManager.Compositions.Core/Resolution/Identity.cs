using System.Security.Cryptography;
using System.Text;
using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;

namespace MarqueeManager.Compositions.Core.Resolution;

/// <summary>Whether a resolution targets a system tile or a game.</summary>
public enum MediaScope { System, Game }

/// <summary>
/// Everything the shared resolver needs, and nothing renderer-specific. Built
/// identically by the Setup and by the runtime's surface loop. Identity carries
/// BOTH the frontend system (mame, fbneo…) and the canonical system (arcade):
/// system settings/compositions key on the frontend, game catalog on the
/// canonical + <see cref="StableGameId"/>. See spec §18.1 and §19.
/// </summary>
public sealed record ResolutionContext(
    string SurfaceId,
    string Category,
    int TargetWidth,
    int TargetHeight,
    MediaScope Scope,
    string? FrontendSystem,
    string? CanonicalSystem,
    string? StableGameId,
    string? Rom,
    string DisplayState)
{
    public PixelSize Target => new(TargetWidth, TargetHeight);

    /// <summary>When set, the target is lighting-pinned: the resolver frames EVERY
    /// link with this policy instead of the per-source fit, so lamp/tube/map
    /// coordinates stay aligned to the original framing (user decision — keep the
    /// framing of lighting-enabled games). Null on ordinary targets.</summary>
    public FitPolicy? PinnedFit { get; init; }

    /// <summary>The system key used for system settings and compositions: the
    /// frontend system, never silently the canonical one — MAME and FBNeo must
    /// not share a system policy. Falls back to canonical only when the payload
    /// omits the frontend (a trace <c>identity.frontend_missing</c> is expected).</summary>
    public string? SystemKey => FrontendSystem ?? CanonicalSystem;

    public bool HasFrontendSystem => !string.IsNullOrWhiteSpace(FrontendSystem);
}

/// <summary>
/// Builds the fallback <c>StableGameId</c> when APIExpose publishes none.
/// It is a fingerprint of the normalized relative ROM path, EXTENSION INCLUDED —
/// never the displayed title, never the bare file stem — so <c>sonic.zip</c> and
/// <c>sonic.7z</c> never collide (spec §19).
/// </summary>
public static class StableGameIds
{
    public const string PathPrefix = "path:";

    /// <summary>Fingerprint id from a relative ROM path, e.g. <c>path:9bf7e0c4</c>.</summary>
    public static string FromRomPath(string relativeRomPath)
    {
        if (string.IsNullOrWhiteSpace(relativeRomPath))
            throw new ArgumentException("relative ROM path is required", nameof(relativeRomPath));

        var normalized = Normalize(relativeRomPath);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return PathPrefix + Convert.ToHexString(hash, 0, 4).ToLowerInvariant();
    }

    /// <summary>Prefers the APIExpose-published id; otherwise the path fingerprint.</summary>
    public static string Resolve(string? apiExposeStableId, string relativeRomPath)
        => string.IsNullOrWhiteSpace(apiExposeStableId)
            ? FromRomPath(relativeRomPath)
            : apiExposeStableId!.Trim();

    private static string Normalize(string path)
        => path.Trim().Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}
