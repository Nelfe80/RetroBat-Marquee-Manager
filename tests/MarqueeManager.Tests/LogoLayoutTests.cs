using MarqueeManager.Compositions.Core.Geometry;
using MarqueeManager.Compositions.Core.Policy;
using Xunit;

namespace MarqueeManager.Tests;

/// <summary>Logo safe-zone layout (spec §11, matrix §28.4).</summary>
public sealed class LogoLayoutTests
{
    private static LogoLayout Layout(double px = 0.06, double py = 0.08, double min = 0.03)
        => new(px, py, min, new BackgroundSpec(BackgroundKinds.ScopeNeutral));

    // LOGO-01 — horizontal logo, padding respected, never cropped.
    [Fact]
    public void HorizontalLogo_FitsInsideSafeZone_NoCrop()
    {
        var p = LogoLayoutCalculator.Place(new PixelSize(800, 200), new PixelSize(1920, 360), Layout());

        Assert.True(p.Ok);
        Assert.Equal(0, p.Fit!.CropX, 6);
        Assert.Equal(0, p.Fit.CropY, 6);
        Assert.True(p.Fit.Padding.Left >= 0.06 * 1920 - 0.01);  // at least the 6% padding
        Assert.True(p.Fit.Padding.Top >= 0.08 * 360 - 0.01);
        Assert.True(p.Fit.TargetRect.Right <= 1920 - (0.06 * 1920) + 0.01); // never touches the edge
    }

    // LOGO-04 — a padding below the 3% floor is raised to the floor.
    [Fact]
    public void PaddingBelowFloor_IsRaisedToThreePercent()
    {
        var p = LogoLayoutCalculator.Place(new PixelSize(1000, 10), new PixelSize(1920, 360), Layout(px: 0.01, py: 0.01));

        Assert.True(p.Ok);
        Assert.Equal(0.03 * 1920, p.Fit!.Padding.Left, 2); // 57.6, the 3% floor, not 1%
    }

    // LOGO-09 — a tiny DMD-like surface still yields a positive usable box.
    [Fact]
    public void SmallSurface_StillPlacesLogo()
    {
        var p = LogoLayoutCalculator.Place(new PixelSize(64, 16), new PixelSize(128, 32), Layout());

        Assert.True(p.Ok);
        Assert.True(p.Fit!.Scale > 0);
    }

    // LOGO-10 — a surface too small for any safe zone returns the diagnostic.
    [Fact]
    public void SurfaceTooSmall_ReturnsDiagnostic()
    {
        var p = LogoLayoutCalculator.Place(new PixelSize(2, 2), new PixelSize(4, 4), Layout());

        Assert.False(p.Ok);
        Assert.Equal(LogoPlacement.TooSmall, p.Diagnostic);
    }
}
