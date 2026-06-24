using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// Connects a LayScene to a LayWpfCompositor and an ILayOutputAdapter.
    /// Implements dirty-debounce: batches rapid state changes into one render per 16ms.
    /// </summary>
    public class LayPipeline : IDisposable
    {
        public string            Target     { get; }
        public LayScene          Scene      { get; }
        public LayWpfCompositor  Compositor { get; }
        public ILayOutputAdapter Adapter    { get; }

        private readonly ILogger          _logger;
        private readonly Dispatcher       _dispatcher;   // STA thread for WPF rendering
        private volatile int              _dirty;        // 1 = render pending
        private readonly SemaphoreSlim    _renderLock = new(1, 1);

        public LayPipeline(string target, double refW, double refH,
                            ILayOutputAdapter adapter,
                            Dispatcher dispatcher,
                            ILogger logger)
        {
            Target      = target;
            Adapter     = adapter;
            _logger     = logger;
            _dispatcher = dispatcher;

            Scene = new LayScene((float)refW, (float)refH);

            // LayWpfCompositor creates WPF UIElements → must be instantiated on STA thread
            Compositor = dispatcher.Invoke(() => new LayWpfCompositor(refW, refH));

            Scene.RenderRequested += OnRenderRequested;
        }

        // ── Lamp state (MAME .lay shortcut) ──────────────────────────────────

        public void SetLampState(string lampName, int state)
            => Scene.SetProperties(lampName, new LayProperties { Show = state != 0 });

        // ── Render pipeline ───────────────────────────────────────────────────

        private void OnRenderRequested()
        {
            if (System.Threading.Interlocked.Exchange(ref _dirty, 1) == 0)
            {
                // Schedule one render after 16ms debounce
                Task.Delay(16).ContinueWith(_ => ScheduleRender());
            }
        }

        private void ScheduleRender()
        {
            System.Threading.Interlocked.Exchange(ref _dirty, 0);
            // RenderTargetBitmap requires STA thread
            _dispatcher.BeginInvoke(() => RenderAndPush(), DispatcherPriority.Render);
        }

        private void RenderAndPush()
        {
            if (!_renderLock.Wait(0)) return; // skip if a render is already in progress
            try
            {
                // Apply all updated objects to the compositor canvas
                foreach (var obj in Scene.GetOrderedByZ())
                {
                    if (obj.Updated)
                        Compositor.Apply(obj);
                }

                // For DMD and other non-LCD targets, capture a bitmap and push
                if (Adapter is not LcdOutputAdapter)
                {
                    var frame = Compositor.Render();
                    Adapter.Push(frame, Compositor.RefW, Compositor.RefH);
                }
                // LCD: UIElements are already live in the canvas — no push needed
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[LayPipeline:{Target}] Render error: {ex.Message}");
            }
            finally
            {
                _renderLock.Release();
            }
        }

        public void ForceRefresh() => Scene.ForceRefresh();

        public void Dispose()
        {
            Scene.RenderRequested -= OnRenderRequested;
            // WPF Compositor must be cleaned up on its own STA thread
            _dispatcher.BeginInvoke(() => Compositor.Dispose());
            _renderLock.Dispose();
        }
    }
}
