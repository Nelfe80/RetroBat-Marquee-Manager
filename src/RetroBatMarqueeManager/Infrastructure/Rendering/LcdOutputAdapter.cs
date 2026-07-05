using System.Windows.Media.Imaging;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// LCD output adapter — pushes composite frames into the MarqueeController WPF window.
    /// For LCD the compositor works directly on the _layCanvas of MarqueeWindow,
    /// so Push() is a no-op here; the compositor already updated the live canvas.
    /// </summary>
    public class LcdOutputAdapter : ILayOutputAdapter
    {
        private readonly MarqueeController _marquee;
        private readonly string            _target;

        public LayScaleMode DefaultScaleMode => LayScaleMode.Direct;

        public LcdOutputAdapter(MarqueeController marquee, string target = "marquee")
        {
            _marquee = marquee;
            _target  = target;
        }

        /// <summary>
        /// No-op for LCD: the LayWpfCompositor already pushed UIElements
        /// directly into the MarqueeWindow _layCanvas.
        /// WPF Viewbox handles the scaling to the physical screen.
        /// </summary>
        public void Push(RenderTargetBitmap frame, double refW, double refH)
        {
            // LCD path: no bitmap capture needed.
            // UIElements are live in the WPF canvas — WPF renders them natively.
        }

        public string Target => _target;
    }
}
