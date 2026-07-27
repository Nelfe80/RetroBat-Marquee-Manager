using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Resolution;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Normalized dimensional statuses (spec §8.2).</summary>
public sealed class DimensionalAnalysisTests
{
    private readonly FitCalculator _fit = new();

    private FitDecision Fit(PixelSize s, PixelSize t, FitMode mode)
        => _fit.Calculate(s, t, new FitPolicy(mode), ProtectedRegions.None);

    [Fact]
    public void ExactPixels_IsExact()
    {
        var t = new PixelSize(1920, 360);
        var r = DimensionalAnalyzer.Analyze(t, t, Fit(t, t, FitMode.Contain));
        Assert.Equal(DimensionalStatus.ExactDimensions, r.Status);
    }

    [Fact]
    public void SameRatioDifferentSize_IsRatioCompatible()
    {
        var s = new PixelSize(1920, 1080);
        var t = new PixelSize(1280, 720);
        var r = DimensionalAnalyzer.Analyze(s, t, Fit(s, t, FitMode.Contain));
        Assert.Equal(DimensionalStatus.RatioCompatible, r.Status);
        Assert.True(r.RatioCompatible);
    }

    [Theory]
    [InlineData(1920, 1080, true)]   // identical ratio
    [InlineData(1000, 563, true)]    // within 0.5%
    [InlineData(1920, 360, false)]   // far off
    public void RatioCompatibility_UsesHalfPercentThreshold(int w, int h, bool expected)
        => Assert.Equal(expected, DimensionalAnalyzer.IsRatioCompatible(new PixelSize(w, h), new PixelSize(1920, 1080)));

    [Fact]
    public void IncompatibleRatioWithCrop_NeedsAdaptation()
    {
        var s = new PixelSize(1920, 1080);
        var t = new PixelSize(1920, 360);
        var r = DimensionalAnalyzer.Analyze(s, t, Fit(s, t, FitMode.Cover));
        Assert.Equal(DimensionalStatus.AdaptationNeeded, r.Status);
        Assert.True(r.CropY > 0);
    }

    [Fact]
    public void Upscale_IsMagnifiedAndFlagsHighAboveTwo()
    {
        var s = new PixelSize(200, 100);
        var t = new PixelSize(1920, 360);
        var r = DimensionalAnalyzer.Analyze(s, t, Fit(s, t, FitMode.Contain));
        Assert.Equal(DimensionalStatus.Magnified, r.Status);
        Assert.True(r.Magnification > 1);
        Assert.True(r.HighMagnification); // 3.6x
    }

    [Theory]
    [InlineData(GenerationState.Required, DimensionalStatus.AdaptationToGenerate)]
    [InlineData(GenerationState.Stale, DimensionalStatus.AdaptationStale)]
    [InlineData(GenerationState.Unsupported, DimensionalStatus.UnsupportedFormat)]
    public void GenerationState_DrivesStatus(GenerationState generation, DimensionalStatus expected)
    {
        var s = new PixelSize(1920, 1080);
        var t = new PixelSize(1920, 360);
        var r = DimensionalAnalyzer.Analyze(s, t, Fit(s, t, FitMode.Cover), generation);
        Assert.Equal(expected, r.Status);
    }

    [Fact]
    public void UnknownSourceSize_IsUnreadable()
    {
        var r = DimensionalAnalyzer.Analyze(null, new PixelSize(1920, 360), null);
        Assert.Equal(DimensionalStatus.UnreadableFormat, r.Status);
    }
}
