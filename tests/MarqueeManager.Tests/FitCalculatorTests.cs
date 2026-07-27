using MarqueeManager.Compositions.Core.Fit;
using MarqueeManager.Compositions.Core.Geometry;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>
/// Golden framing cases mirroring the recette matrix (§28.3 FIT-01..FIT-12).
/// The calculator is pure, so these numbers are the contract the Setup preview
/// and the runtime must both honor.
/// </summary>
public sealed class FitCalculatorTests
{
    private readonly FitCalculator _fit = new();

    private FitDecision Calc(PixelSize source, PixelSize target, FitPolicy policy, ProtectedRegions? protectedRegions = null)
        => _fit.Calculate(source, target, policy, protectedRegions ?? ProtectedRegions.None);

    // FIT-01 — contain, different ratios: whole image, no crop, letterbox.
    [Fact]
    public void Contain_DifferentRatios_ShowsWholeImageWithLetterbox()
    {
        var d = Calc(new PixelSize(1920, 1080), new PixelSize(1920, 360), new FitPolicy(FitMode.Contain));

        Assert.Equal(1.0 / 3, d.Scale, 4);
        Assert.Equal(0, d.CropX, 4);
        Assert.Equal(0, d.CropY, 4);
        Assert.Equal(640, d.Padding.Left, 3);   // pillarbox on both sides
        Assert.Equal(640, d.Padding.Right, 3);
        Assert.Equal(0, d.Padding.Top, 3);
        Assert.Equal(1920, d.SourceVisible.Width, 3); // nothing cropped
        Assert.False(d.FellBack);
    }

    // FIT-02 — cover, different ratios: surface covered, crop announced.
    [Fact]
    public void Cover_DifferentRatios_CoversSurfaceAndAnnouncesCrop()
    {
        var d = Calc(new PixelSize(1920, 1080), new PixelSize(1920, 360), new FitPolicy(FitMode.Cover));

        Assert.Equal(1.0, d.Scale, 4);
        Assert.Equal(0, d.CropX, 4);
        Assert.Equal(2.0 / 3, d.CropY, 4);       // 66.7% vertical crop (spec §8.1 example)
        Assert.True(d.Padding.IsEmpty);
        Assert.Equal(360, d.SourceVisible.Y, 3); // centered vertical window
        Assert.Equal(360, d.SourceVisible.Height, 3);
    }

    // FIT-03 — fill-height: exact target height, single factor.
    [Fact]
    public void FillHeight_MakesHeightExact()
    {
        var d = Calc(new PixelSize(1920, 1080), new PixelSize(1920, 360), new FitPolicy(FitMode.FillHeight));

        Assert.Equal(360.0, d.TargetRect.Height, 3);
        Assert.Equal(1.0 / 3, d.Scale, 4);
    }

    // FIT-04 — fill-width: exact target width, single factor.
    [Fact]
    public void FillWidth_MakesWidthExact()
    {
        var d = Calc(new PixelSize(800, 600), new PixelSize(1920, 360), new FitPolicy(FitMode.FillWidth));

        Assert.Equal(1920.0, d.TargetRect.Width, 3);
        Assert.Equal(1920.0 / 800, d.Scale, 4);
    }

    // FIT-05 — dynamic under threshold: covering framing accepted.
    [Fact]
    public void Dynamic_UnderThreshold_AcceptsCover()
    {
        var d = Calc(new PixelSize(1920, 400), new PixelSize(1920, 360),
            new FitPolicy(FitMode.Dynamic, MaxCrop: 0.30));

        Assert.Equal(FitMode.Cover, d.EffectiveMode);
        Assert.False(d.FellBack);
        Assert.Equal(0.10, d.CropY, 4);
    }

    // FIT-06 — dynamic above threshold: falls back to contain.
    [Fact]
    public void Dynamic_AboveThreshold_FallsBackToContain()
    {
        var d = Calc(new PixelSize(1920, 1080), new PixelSize(1920, 360),
            new FitPolicy(FitMode.Dynamic, MaxCrop: 0.30));

        Assert.Equal(FitMode.Contain, d.EffectiveMode);
        Assert.True(d.FellBack);
        Assert.Equal("crop_exceeds_threshold", d.FallbackReason);
        Assert.Equal(0, d.CropY, 4);             // contain: nothing cropped
    }

    // FIT-07 — crop exactly at the threshold is accepted.
    [Fact]
    public void Dynamic_CropEqualToThreshold_IsAccepted()
    {
        var d = Calc(new PixelSize(1000, 1000), new PixelSize(1000, 700),
            new FitPolicy(FitMode.Dynamic, MaxCrop: 0.30));

        Assert.Equal(FitMode.Cover, d.EffectiveMode);
        Assert.False(d.FellBack);
        Assert.Equal(0.30, d.CropY, 4);
    }

    // FIT-08 — same ratio: no crop in any covering mode.
    [Fact]
    public void SameRatio_NoCrop()
    {
        var d = Calc(new PixelSize(800, 450), new PixelSize(1600, 900), new FitPolicy(FitMode.Cover));

        Assert.Equal(2.0, d.Scale, 4);
        Assert.Equal(0, d.CropPercent, 4);
    }

    // FIT-09 — low definition: upscaling is reflected in the factor.
    [Fact]
    public void LowResolution_ReportsUpscaleFactor()
    {
        var d = Calc(new PixelSize(200, 100), new PixelSize(1920, 360), new FitPolicy(FitMode.Contain));

        Assert.Equal(3.6, d.Scale, 4);           // > 1 => magnification
        Assert.True(d.Scale > 1);
    }

    // FIT-10 — a square (circle/grid) stays square in every mode.
    [Theory]
    [InlineData(FitMode.Contain)]
    [InlineData(FitMode.Cover)]
    [InlineData(FitMode.FillHeight)]
    [InlineData(FitMode.FillWidth)]
    public void Square_StaysSquare(FitMode mode)
    {
        var d = Calc(new PixelSize(100, 100), new PixelSize(1920, 360), new FitPolicy(mode));

        Assert.Equal(1.0, d.TargetRect.Width / d.TargetRect.Height, 4);
    }

    // FIT-11 — vertical surface: symmetric behavior (mirror of FIT-02).
    [Fact]
    public void VerticalSurface_SymmetricCrop()
    {
        var d = Calc(new PixelSize(1080, 1920), new PixelSize(360, 1920), new FitPolicy(FitMode.Cover));

        Assert.Equal(1.0, d.Scale, 4);
        Assert.Equal(2.0 / 3, d.CropX, 4);       // horizontal crop, symmetric to the vertical case
        Assert.Equal(0, d.CropY, 4);
    }

    // FIT-12 — no stretch: the target rect always keeps the source aspect ratio.
    [Theory]
    [InlineData(FitMode.Contain)]
    [InlineData(FitMode.Cover)]
    [InlineData(FitMode.FillHeight)]
    [InlineData(FitMode.FillWidth)]
    [InlineData(FitMode.Dynamic)]
    public void NeverStretches_TargetRectKeepsSourceAspect(FitMode mode)
    {
        var source = new PixelSize(1280, 720);
        var d = Calc(source, new PixelSize(1920, 360), new FitPolicy(mode, MaxCrop: 0.90));

        Assert.Equal(source.Ratio, d.TargetRect.Width / d.TargetRect.Height, 4);
    }

    // Protected region on the cropped axis shifts the window instead of cropping it.
    [Fact]
    public void Dynamic_ProtectedRegionNearEdge_ShiftsWindowToKeepItVisible()
    {
        var regions = new ProtectedRegions(new[] { new RelativeRect(0.90, 0, 0.05, 1) });
        var d = Calc(new PixelSize(2000, 1000), new PixelSize(1000, 1000),
            new FitPolicy(FitMode.Dynamic, MaxCrop: 0.60), regions);

        Assert.Equal(FitMode.Cover, d.EffectiveMode);
        Assert.False(d.FellBack);
        // centered window would start at x=500; it is pushed right to include the region
        Assert.Equal(900, d.SourceVisible.X, 2);
        Assert.True(d.SourceVisible.Right >= 1900 - 0.01);
    }

    // A protected region wider than the visible window forces a contain fallback.
    [Fact]
    public void Dynamic_ProtectedRegionTooWide_FallsBackToContain()
    {
        var regions = new ProtectedRegions(new[] { new RelativeRect(0.10, 0, 0.80, 1) });
        var d = Calc(new PixelSize(2000, 1000), new PixelSize(1000, 1000),
            new FitPolicy(FitMode.Dynamic, MaxCrop: 0.60), regions);

        Assert.Equal(FitMode.Contain, d.EffectiveMode);
        Assert.True(d.FellBack);
        Assert.Equal("protected_region_horizontal", d.FallbackReason);
    }

    // Invalid dimensions never produce a framing (spec §26).
    [Fact]
    public void InvalidDimensions_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Calc(new PixelSize(0, 100), new PixelSize(1920, 360), new FitPolicy(FitMode.Contain)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Calc(new PixelSize(100, 100), new PixelSize(1920, 0), new FitPolicy(FitMode.Contain)));
    }
}
