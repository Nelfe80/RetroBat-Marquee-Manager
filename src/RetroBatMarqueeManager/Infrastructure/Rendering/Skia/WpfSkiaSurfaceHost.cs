using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;

namespace RetroBatMarqueeManager.Infrastructure.Rendering.Skia;

/// <summary>
/// WPF host for the Skia lighting surface. Renders on a dedicated thread into a
/// double-buffered pair of SKBitmaps, then presents the front buffer to a
/// WriteableBitmap on the UI thread (latest-wins: at most one present in flight).
/// The UI thread and WebSocket threads are never blocked by rendering.
/// </summary>
public sealed class WpfSkiaSurfaceHost : System.Windows.Controls.Image, IDisposable
{
    private readonly ILogger _logger;
    private readonly int _fpsLimit;
    private readonly bool _showFps;
    private readonly double _renderScale;

    private ISkiaFrameRenderer? _renderer;
    private Thread? _renderThread;
    private CancellationTokenSource? _cts;

    private readonly object _swapLock = new();
    private SKBitmap? _front;
    private SKBitmap? _back;
    private WriteableBitmap? _writeable;
    private int _presentQueued;

    private volatile int _targetWidth;
    private volatile int _targetHeight;

    private SKPaint? _fpsPaint;
    private SKFont? _fpsFont;

    // Lot 0 instrumentation (docs\Update.txt §4): measures the pipeline, changes nothing.
    private readonly RenderMetrics _metrics = new();
    public RenderMetrics Metrics => _metrics;
    private double _presentedFps;
    private bool _idle;

    /// <summary>Adaptive resolution: when the CPU raster cannot hold the frame
    /// budget (sprite bursts force full-frame renders), the surface renders at a
    /// reduced internal scale (down to 0.5) and WPF stretches it back — smooth
    /// effects beat crisp-but-stuttering ones. Recovers when the load drops.</summary>
    private double _adaptiveScale = 1.0;

    public double CurrentFps { get; private set; }

    /// <summary>
    /// Called on the render thread with the freshly rendered frame (front buffer,
    /// under the swap lock — copy what you need, do not keep the reference).
    /// Used by the DMD mirror.
    /// </summary>
    public Action<SKBitmap>? FrameRendered;

    public WpfSkiaSurfaceHost(ILogger logger, int fpsLimit, bool showFps, double renderScale = 1.0)
    {
        _logger = logger;
        _fpsLimit = Math.Clamp(fpsLimit, 15, 240);
        _showFps = showFps;
        _renderScale = Math.Clamp(renderScale, 0.25, 1.0);
        Stretch = Stretch.Fill;
        SizeChanged += (_, _) => UpdateTargetSize();
        Loaded += (_, _) =>
        {
            // An Image without Source measures 0x0: track the parent container's size instead.
            if (Parent is FrameworkElement parent)
                parent.SizeChanged += (_, _) => UpdateTargetSize();
            UpdateTargetSize();
        };
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);

    public void Start(ISkiaFrameRenderer renderer)
    {
        if (_renderThread != null) return;
        _renderer = renderer;
        _cts = new CancellationTokenSource();
        UpdateTargetSize();
        _renderThread = new Thread(() => RenderLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "MarqueeManager.SkiaRender",
            // AboveNormal WITHIN the BelowNormal process: this keeps the render thread
            // fed among the process's own threads (dropping it to Normal starved the
            // render to ~3 FPS on a loaded machine) while the whole process still
            // yields to ES — its priority stays below ES's Normal-class threads.
            Priority = ThreadPriority.AboveNormal
        };
        _renderThread.Start();
        _logger.LogInformation("Skia lighting surface started (fps limit {FpsLimit})", _fpsLimit);
    }

    private void UpdateTargetSize()
    {
        var reference = Parent as FrameworkElement ?? this;
        var width = ActualWidth > 0 ? ActualWidth : reference.ActualWidth;
        var height = ActualHeight > 0 ? ActualHeight : reference.ActualHeight;
        var dpi = VisualTreeHelper.GetDpi(this);
        _targetWidth = Math.Max(0, (int)Math.Round(width * dpi.DpiScaleX * _renderScale));
        _targetHeight = Math.Max(0, (int)Math.Round(height * dpi.DpiScaleY * _renderScale));
    }

    private void RenderLoop(CancellationToken ct)
    {
        timeBeginPeriod(1);
        try
        {
            RenderLoopCore(ct);
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    private int _lastLogicalWidth;
    private int _lastLogicalHeight;
    private int _lastPhysicalWidth;
    private int _lastPhysicalHeight;

    private void RenderLoopCore(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var fpsFrames = 0;
        var fpsWindowStart = 0L;
        var lastFpsLog = 0L;

        while (!ct.IsCancellationRequested)
        {
            var frameStart = clock.ElapsedTicks;
            // §6: pace to what the content actually needs, never above the config cap
            var targetFps = Math.Clamp(Math.Min(_fpsLimit, _renderer?.DesiredFps ?? _fpsLimit), 1, 240);
            var frameTicks = Stopwatch.Frequency / targetFps;

            // §6 "Dimensions logiques stables": the renderer always sees the FULL
            // logical size; adaptive scale only shrinks the physical buffer, applied
            // through canvas.Scale. A scale change no longer regenerates the maps.
            var logicalWidth = _targetWidth;
            var logicalHeight = _targetHeight;
            var physicalWidth = Math.Max(1, (int)Math.Round(logicalWidth * _adaptiveScale));
            var physicalHeight = Math.Max(1, (int)Math.Round(logicalHeight * _adaptiveScale));

            if (logicalWidth > 0 && logicalHeight > 0 && _renderer != null)
            {
                var sizeChanged = logicalWidth != _lastLogicalWidth || logicalHeight != _lastLogicalHeight
                                  || physicalWidth != _lastPhysicalWidth || physicalHeight != _lastPhysicalHeight;
                if (sizeChanged || _renderer.WantsFrame(clock.Elapsed))
                {
                    try
                    {
                        var renderStart = Stopwatch.GetTimestamp();
                        RenderFrame(logicalWidth, logicalHeight, physicalWidth, physicalHeight, clock.Elapsed);
                        _metrics.RecordRender((Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency);
                        _metrics.RecordSpriteCount(_renderer.ActiveSpriteCount);
                        _metrics.RecordMapGeneration(_renderer.LastMapGenerationMs);
                        SchedulePresent();
                        _lastLogicalWidth = logicalWidth;
                        _lastLogicalHeight = logicalHeight;
                        _lastPhysicalWidth = physicalWidth;
                        _lastPhysicalHeight = physicalHeight;
                        fpsFrames++;
                        _idle = false;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Skia lighting frame failed; surface loop continues");
                        Thread.Sleep(500);
                    }
                }
                else
                {
                    // renderer skipped a visually identical frame (§17.5)
                    _metrics.RecordSkip();
                    _idle = true;
                }
            }
            var now = clock.ElapsedTicks;
            if (now - fpsWindowStart >= Stopwatch.Frequency)
            {
                CurrentFps = fpsFrames * (double)Stopwatch.Frequency / (now - fpsWindowStart);
                // §6: adaptive resolution judged against the CONTENT cadence, so a
                // 24 Hz scene can recover; only windows with continuous rendering count
                if (fpsFrames >= targetFps / 3)
                {
                    if (CurrentFps < targetFps * 0.70 && _adaptiveScale > 0.5)
                    {
                        _adaptiveScale = Math.Max(0.5, _adaptiveScale * 0.8);
                        _logger.LogInformation("Lighting surface overloaded ({Fps:F1}/{Target} FPS): render scale lowered to {Scale:P0}", CurrentFps, targetFps, _adaptiveScale);
                    }
                    else if (CurrentFps > targetFps * 0.92 && _adaptiveScale < 1.0)
                    {
                        _adaptiveScale = Math.Min(1.0, _adaptiveScale * 1.12);
                    }
                }
                _presentedFps = Interlocked.Exchange(ref _presentsThisWindow, 0) * (double)Stopwatch.Frequency / (now - fpsWindowStart);
                fpsFrames = 0;
                fpsWindowStart = now;
                if (now - lastFpsLog >= 5 * Stopwatch.Frequency)
                {
                    lastFpsLog = now;
                    _logger.LogInformation("{Line}", _metrics.SnapshotLine(_adaptiveScale, physicalWidth, physicalHeight));
                }
            }

            var remaining = frameTicks - (clock.ElapsedTicks - frameStart);
            var remainingMs = (int)(remaining * 1000 / Stopwatch.Frequency);
            if (remainingMs > 2) Thread.Sleep(remainingMs - 1);
            while (clock.ElapsedTicks - frameStart < frameTicks && !ct.IsCancellationRequested)
                Thread.SpinWait(120);
        }
    }

    /// <summary>Renders at the STABLE logical size into a physical buffer that may
    /// be smaller (adaptive scale). The canvas is pre-scaled so the renderer draws
    /// in logical coordinates; WPF then stretches the physical buffer to the window.
    /// Maps therefore never regenerate just because the scale changed (§6).</summary>
    private void RenderFrame(int logicalWidth, int logicalHeight, int physicalWidth, int physicalHeight, TimeSpan elapsed)
    {
        if (_back == null || _back.Width != physicalWidth || _back.Height != physicalHeight)
        {
            _back?.Dispose();
            _back = new SKBitmap(new SKImageInfo(physicalWidth, physicalHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        }

        var info = new SKImageInfo(physicalWidth, physicalHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var surface = SKSurface.Create(info, _back.GetPixels(), _back.RowBytes))
        {
            var canvas = surface.Canvas;
            canvas.Save();
            canvas.Scale(physicalWidth / (float)logicalWidth, physicalHeight / (float)logicalHeight);
            _renderer!.Render(canvas, logicalWidth, logicalHeight, elapsed);
            canvas.Restore();
            if (_showFps) DrawFps(canvas);
            canvas.Flush();
        }

        lock (_swapLock)
        {
            (_front, _back) = (_back, _front);
            if (FrameRendered != null && _front != null)
            {
                try { FrameRendered(_front); }
                catch (Exception ex) { _logger.LogDebug(ex, "Frame sink failed"); }
            }
        }
    }

    private void DrawFps(SKCanvas canvas)
    {
        _fpsFont ??= new SKFont(SKTypeface.Default, 18);
        _fpsPaint ??= new SKPaint { Color = SKColors.Lime, IsAntialias = true };
        // §4: show frames actually PRESENTED by WPF, or IDLE when the renderer is
        // deliberately skipping visually identical frames — not the computed count
        var text = _idle ? "IDLE" : $"{_presentedFps:F0} FPS";
        canvas.DrawText(text, 10, 24, _fpsFont, _fpsPaint);
    }

    private int _presentsThisWindow;

    private void SchedulePresent()
    {
        // a present was already queued but not yet run: the frame it would have
        // shown is now superseded — count it as a dropped present (§4)
        if (Interlocked.Exchange(ref _presentQueued, 1) == 1)
        {
            _metrics.RecordDroppedPresent();
            return;
        }
        Dispatcher.BeginInvoke(Present, DispatcherPriority.Render);
    }

    private void Present()
    {
        Interlocked.Exchange(ref _presentQueued, 0);
        var waitStart = Stopwatch.GetTimestamp();
        lock (_swapLock)
        {
            var lockAcquired = Stopwatch.GetTimestamp();
            if (_front == null) return;
            var w = _front.Width;
            var h = _front.Height;
            if (_writeable == null || _writeable.PixelWidth != w || _writeable.PixelHeight != h)
            {
                _writeable = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                Source = _writeable;
            }
            _writeable.WritePixels(new Int32Rect(0, 0, w, h), _front.GetPixels(), _front.RowBytes * h, _front.RowBytes);
            var done = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref _presentsThisWindow);
            _metrics.RecordPresent(
                (lockAcquired - waitStart) * 1000.0 / Stopwatch.Frequency,
                (done - lockAcquired) * 1000.0 / Stopwatch.Frequency);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _renderThread?.Join(2000);
        _renderThread = null;
        _cts?.Dispose();
        _cts = null;
        lock (_swapLock)
        {
            _front?.Dispose();
            _back?.Dispose();
            _front = _back = null;
        }
        _renderer?.Dispose();
        _renderer = null;
        _fpsPaint?.Dispose();
        _fpsFont?.Dispose();
    }
}
