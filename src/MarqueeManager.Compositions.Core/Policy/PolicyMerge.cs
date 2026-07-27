using MarqueeManager.Compositions.Core.Fit;

namespace MarqueeManager.Compositions.Core.Policy;

/// <summary>
/// Recursive, terminal-by-terminal merge of a delta onto a base policy (spec §20.1):
/// an absent (null) terminal inherits, a present one replaces only itself. A partial
/// sub-object (e.g. <c>logo.enabled</c>) never wipes its siblings (<c>logo.fit</c>,
/// <c>logo.logoLayout</c>). Deltas are pure data; this never generates or writes.
/// </summary>
public static class PolicyMerge
{
    // The fit a link starts from when the base leaves it unspecified but a delta
    // begins tuning it. Contain is the safe, no-crop default.
    private static readonly FitPolicy DefaultFit = new(FitMode.Contain);

    public static ScopePolicy Apply(ScopePolicy @base, ScopePolicyDelta? delta)
    {
        if (delta is null) return @base;

        return new ScopePolicy(
            delta.TemplateId ?? @base.TemplateId,
            delta.AutoGenerate ?? @base.AutoGenerate,
            MergeSources(@base.Sources, delta.Sources),
            delta.NeutralBackground is null ? @base.NeutralBackground : Apply(@base.NeutralBackground, delta.NeutralBackground));
    }

    private static IReadOnlyDictionary<SourceKind, SourceSettings> MergeSources(
        IReadOnlyDictionary<SourceKind, SourceSettings> @base,
        IReadOnlyDictionary<SourceKind, SourceSettingsDelta>? deltas)
    {
        if (deltas is null || deltas.Count == 0) return @base;

        var merged = new Dictionary<SourceKind, SourceSettings>(@base);
        foreach (var (kind, delta) in deltas)
        {
            var baseSettings = @base.TryGetValue(kind, out var existing)
                ? existing
                : new SourceSettings(Enabled: true); // a delta may introduce a link the base omitted
            merged[kind] = Apply(baseSettings, delta);
        }
        return merged;
    }

    public static SourceSettings Apply(SourceSettings @base, SourceSettingsDelta delta)
        => new(
            delta.Enabled ?? @base.Enabled,
            delta.Fit is null ? @base.Fit : Apply(@base.Fit ?? DefaultFit, delta.Fit),
            delta.LogoLayout is null ? @base.LogoLayout : Apply(@base.LogoLayout, delta.LogoLayout));

    public static FitPolicy Apply(FitPolicy @base, FitPolicyDelta delta)
        => new(
            delta.Mode ?? @base.Mode,
            delta.AlignX ?? @base.AlignX,
            delta.AlignY ?? @base.AlignY,
            delta.MaxCrop ?? @base.MaxCrop,
            delta.Fallback ?? @base.Fallback);

    // A logo layout delta with no base yet materializes onto neutral defaults so a
    // partial tweak still yields a complete layout.
    private static readonly LogoLayout DefaultLogoLayout =
        new(0.06, 0.08, 0.03, new BackgroundSpec(BackgroundKinds.ScopeNeutral));

    public static LogoLayout Apply(LogoLayout? @base, LogoLayoutDelta delta)
    {
        var b = @base ?? DefaultLogoLayout;
        return new LogoLayout(
            delta.PaddingX ?? b.PaddingX,
            delta.PaddingY ?? b.PaddingY,
            delta.MinimumPadding ?? b.MinimumPadding,
            delta.Background is null ? b.Background : Apply(b.Background, delta.Background));
    }

    public static BackgroundSpec Apply(BackgroundSpec @base, BackgroundSpecDelta delta)
        => new(delta.Kind ?? @base.Kind, delta.Color ?? @base.Color);
}
