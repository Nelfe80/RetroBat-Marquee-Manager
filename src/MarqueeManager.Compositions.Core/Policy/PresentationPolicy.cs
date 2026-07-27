using MarqueeManager.Compositions.Core.Fit;

namespace MarqueeManager.Compositions.Core.Policy;

/// <summary>The links a surface policy can enable/disable and tune. Distinct from
/// <see cref="Resolution.ResolutionSource"/> (a result): these are the CONFIGURABLE
/// chain links. The neutral background is never one of them (non-disableable).</summary>
public enum SourceKind { Personal, Generated, Scraped, Logo, SystemFallback }

public static class BackgroundKinds
{
    public const string Solid = "solid";
    public const string ScopeNeutral = "scope-neutral";
    public const string Gradient = "gradient";
}

/// <summary>Fill of a canvas area. <c>scope-neutral</c> means the current scope's
/// neutralBackground (Systems policy for a system logo, Jeux for a game logo).</summary>
public sealed record BackgroundSpec(string Kind, string? Color = null);

/// <summary>Safe-zone layout of a laid-out logo (spec §11). Percentages are per
/// edge of the corresponding surface dimension.</summary>
public sealed record LogoLayout(
    double PaddingX,
    double PaddingY,
    double MinimumPadding,
    BackgroundSpec Background);

/// <summary>Effective settings of one chain link on one target.</summary>
public sealed record SourceSettings(
    bool Enabled,
    FitPolicy? Fit = null,
    LogoLayout? LogoLayout = null);

/// <summary>
/// The fully-resolved presentation policy for one scope (system or game) on one
/// surface: which links are on, how each frames, the template and the neutral
/// background. Produced by merging a surface base with any target delta (§20.1).
/// </summary>
public sealed record ScopePolicy(
    string? TemplateId,
    bool AutoGenerate,
    IReadOnlyDictionary<SourceKind, SourceSettings> Sources,
    BackgroundSpec NeutralBackground)
{
    public SourceSettings? Source(SourceKind kind)
        => Sources.TryGetValue(kind, out var s) ? s : null;

    public bool IsEnabled(SourceKind kind) => Source(kind)?.Enabled == true;
}
