using System.Windows.Media.Imaging;

namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    public enum LayScaleMode
    {
        /// <summary>refW==deviceW && refH==deviceH — no scaling.</summary>
        Direct,
        /// <summary>Proportional scale with letterbox (black bars).</summary>
        Fit,
        /// <summary>Proportional scale with center crop.</summary>
        Fill,
        /// <summary>Stretch to fill, ignoring aspect ratio.</summary>
        Stretch
    }

    public interface ILayOutputAdapter
    {
        LayScaleMode DefaultScaleMode { get; }

        /// <summary>
        /// Receives the composited frame at .lay reference resolution (refW × refH)
        /// and pushes it to the physical target at its native resolution.
        /// </summary>
        void Push(RenderTargetBitmap frame, double refW, double refH);
    }
}
