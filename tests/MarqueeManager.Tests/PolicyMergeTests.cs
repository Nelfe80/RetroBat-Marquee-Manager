using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Policy;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Recursive terminal-by-terminal delta merge and maxCrop validation
/// exactly as spec §20.1 requires.</summary>
public sealed class PolicyMergeTests
{
    // Mirrors the §20.1 example system policy.
    private static ScopePolicy BasePolicy() => new(
        TemplateId: "system-default",
        AutoGenerate: false,
        Sources: new Dictionary<SourceKind, SourceSettings>
        {
            [SourceKind.Personal] = new(true, new FitPolicy(FitMode.Contain, HAlign.Center, VAlign.Center)),
            [SourceKind.Generated] = new(true),
            [SourceKind.Scraped] = new(true, new FitPolicy(FitMode.Dynamic, HAlign.Auto, VAlign.Auto, 0.30, FitMode.Contain)),
            [SourceKind.Logo] = new(true,
                new FitPolicy(FitMode.Contain, HAlign.Center, VAlign.Center),
                new LogoLayout(0.06, 0.08, 0.03, new BackgroundSpec(BackgroundKinds.ScopeNeutral))),
        },
        NeutralBackground: new BackgroundSpec(BackgroundKinds.Solid, "#000000"));

    [Fact]
    public void NullDelta_ReturnsBaseUnchanged()
    {
        var b = BasePolicy();
        Assert.Equal(b, PolicyMerge.Apply(b, null));
    }

    [Fact]
    public void DisablingOneLink_LeavesEveryOtherTerminalInherited()
    {
        var delta = new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
        {
            [SourceKind.Generated] = new(Enabled: false)
        });

        var merged = PolicyMerge.Apply(BasePolicy(), delta);

        Assert.False(merged.IsEnabled(SourceKind.Generated));
        // scraped untouched, including its nested fit
        Assert.Equal(BasePolicy().Source(SourceKind.Scraped), merged.Source(SourceKind.Scraped));
        Assert.Equal("system-default", merged.TemplateId);
    }

    [Fact]
    public void PartialFitDelta_ReplacesOnlyGivenTerminals()
    {
        var delta = new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
        {
            [SourceKind.Scraped] = new(Fit: new FitPolicyDelta(Mode: FitMode.FillHeight, AlignX: HAlign.Center))
        });

        var fit = PolicyMerge.Apply(BasePolicy(), delta).Source(SourceKind.Scraped)!.Fit!;

        Assert.Equal(FitMode.FillHeight, fit.Mode); // changed
        Assert.Equal(HAlign.Center, fit.AlignX);    // changed
        Assert.Equal(VAlign.Auto, fit.AlignY);      // inherited
        Assert.Equal(0.30, fit.MaxCrop, 4);         // inherited
        Assert.Equal(FitMode.Contain, fit.Fallback);// inherited
    }

    // The explicit §20.1 example: a partial logo object must not wipe its siblings.
    [Fact]
    public void LogoEnabledDelta_DoesNotWipeFitOrLayout()
    {
        var delta = new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
        {
            [SourceKind.Logo] = new(Enabled: false)
        });

        var logo = PolicyMerge.Apply(BasePolicy(), delta).Source(SourceKind.Logo)!;

        Assert.False(logo.Enabled);
        Assert.NotNull(logo.Fit);                       // logo.fit preserved
        Assert.NotNull(logo.LogoLayout);                // logo.logoLayout preserved
        Assert.Equal(0.06, logo.LogoLayout!.PaddingX, 4);
        Assert.Equal(BackgroundKinds.ScopeNeutral, logo.LogoLayout.Background.Kind);
    }

    [Fact]
    public void TemplateIdDelta_OverridesWhenPresentInheritsWhenAbsent()
    {
        Assert.Equal("game-x", PolicyMerge.Apply(BasePolicy(), new ScopePolicyDelta(TemplateId: "game-x")).TemplateId);
        Assert.Equal("system-default", PolicyMerge.Apply(BasePolicy(), new ScopePolicyDelta(AutoGenerate: true)).TemplateId);
    }

    // Nested background is merged recursively: a color-only delta keeps the base kind.
    [Fact]
    public void NeutralBackgroundColorDelta_KeepsBaseKind()
    {
        var delta = new ScopePolicyDelta(NeutralBackground: new BackgroundSpecDelta(Color: "#101010"));

        var bg = PolicyMerge.Apply(BasePolicy(), delta).NeutralBackground;

        Assert.Equal(BackgroundKinds.Solid, bg.Kind); // inherited
        Assert.Equal("#101010", bg.Color);            // replaced
    }

    [Fact]
    public void DeltaCanIntroduceALinkTheBaseOmitted()
    {
        var noFallback = BasePolicy(); // has no SystemFallback
        var delta = new ScopePolicyDelta(Sources: new Dictionary<SourceKind, SourceSettingsDelta>
        {
            [SourceKind.SystemFallback] = new(Enabled: true)
        });

        var merged = PolicyMerge.Apply(noFallback, delta);

        Assert.True(merged.IsEnabled(SourceKind.SystemFallback));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.30, true)]
    [InlineData(0.60, true)]
    [InlineData(0.601, false)]
    [InlineData(-0.01, false)]
    public void MaxCrop_RangeIsZeroToSixtyPercent(double value, bool valid)
        => Assert.Equal(valid, PolicyValidation.IsValidMaxCrop(value));

    [Fact]
    public void Validate_FlagsOutOfRangeMaxCrop()
    {
        var bad = BasePolicy() with
        {
            Sources = new Dictionary<SourceKind, SourceSettings>(BasePolicy().Sources)
            {
                [SourceKind.Scraped] = new(true, new FitPolicy(FitMode.Dynamic, MaxCrop: 0.75))
            }
        };

        var errors = PolicyValidation.Validate(bad);

        Assert.Single(errors);
        Assert.Equal(PolicyValidationError.MaxCropOutOfRange, errors[0].Code);
        Assert.Equal("sources.scraped.fit.maxCrop", errors[0].Path);
        Assert.Empty(PolicyValidation.Validate(BasePolicy()));
    }
}
