using SkiaSharp;

namespace RetroBatMarqueeManager.Infrastructure.Rendering.Skia;

/// <summary>
/// Produces one frame of the lighting surface. Called from the dedicated render
/// thread, never from the UI thread — implementations must not touch WPF objects.
/// </summary>
public interface ISkiaFrameRenderer : IDisposable
{
    void Render(SKCanvas canvas, int width, int height, TimeSpan elapsed);

    /// <summary>
    /// Frame-skip hook (§17.5): return false when the next frame would be visually
    /// identical to the last one; the host then skips rendering and presenting.
    /// </summary>
    bool WantsFrame(TimeSpan elapsed) => true;
}

/// <summary>Options passed to marquee windows when the lighting engine layer is enabled.</summary>
/// <param name="RenderScale">Internal render resolution factor (0.25–1.0); the surface is
/// upscaled by WPF. Compensates the CPU raster backend until the GPU backend lands (§17.4).</param>
/// <param name="FillHeightMaxCrop">Max acceptable horizontal material loss (0–0.6) when
/// filling the window height instead of letterboxing; 0 disables fill-height framing.</param>
public sealed record LightingSurfaceOptions(bool TestPattern, int FpsLimit, bool ShowFps, double RenderScale, double FillHeightMaxCrop, bool SoundEnabled, double SoundVolume, double GlassReflection, double TubeVisualOpacity, double TubeThickness = 1.0, double TubeBlur = 1.0, double TubeEndFade = 0.10, string TubeColor = "#FFE0B2");
