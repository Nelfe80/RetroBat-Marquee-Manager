using MarqueeManager.Compositions.Core.Fit;

namespace MarqueeManager.Compositions.Core.Policy;

// Deltas mirror the effective model but make EVERY field nullable: null = "absent"
// = inherit; a present value replaces only that terminal (spec §20.1). null is never
// a delete instruction (the schema refuses it); reverting to inherit means dropping
// the field from the document, not writing null.

public sealed record BackgroundSpecDelta(string? Kind = null, string? Color = null);

public sealed record FitPolicyDelta(
    FitMode? Mode = null,
    HAlign? AlignX = null,
    VAlign? AlignY = null,
    double? MaxCrop = null,
    FitMode? Fallback = null);

public sealed record LogoLayoutDelta(
    double? PaddingX = null,
    double? PaddingY = null,
    double? MinimumPadding = null,
    BackgroundSpecDelta? Background = null);

public sealed record SourceSettingsDelta(
    bool? Enabled = null,
    FitPolicyDelta? Fit = null,
    LogoLayoutDelta? LogoLayout = null);

public sealed record ScopePolicyDelta(
    string? TemplateId = null,
    bool? AutoGenerate = null,
    IReadOnlyDictionary<SourceKind, SourceSettingsDelta>? Sources = null,
    BackgroundSpecDelta? NeutralBackground = null);
