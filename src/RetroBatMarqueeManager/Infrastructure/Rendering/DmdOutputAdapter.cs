using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Infrastructure.Native;

namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// Adapts a WPF RenderTargetBitmap frame to the physical DMD device.
    /// Handles BGRA→RGB24 conversion and optional scaling when the .lay
    /// reference resolution differs from the device resolution.
    /// </summary>
    public class DmdOutputAdapter : ILayOutputAdapter
    {
        private readonly DmdDeviceWrapper _dmd;
        private readonly int              _deviceW;
        private readonly int              _deviceH;
        private readonly ILogger          _logger;

        public LayScaleMode DefaultScaleMode { get; }

        public DmdOutputAdapter(DmdDeviceWrapper dmd, int deviceW, int deviceH,
                                 ILogger logger, LayScaleMode scaleMode = LayScaleMode.Fit)
        {
            _dmd          = dmd;
            _deviceW      = deviceW;
            _deviceH      = deviceH;
            _logger       = logger;
            DefaultScaleMode = scaleMode;
        }

        public void Push(RenderTargetBitmap frame, double refW, double refH)
        {
            if (!_dmd.IsLoaded) return;

            try
            {
                var srcW   = (int)refW;
                var srcH   = (int)refH;
                var stride = srcW * 4;
                var bgra   = new byte[srcH * stride];
                frame.CopyPixels(bgra, stride, 0);

                byte[] rgb24;
                if (srcW == _deviceW && srcH == _deviceH)
                {
                    rgb24 = ConvertBgraToRgb24(bgra, srcW, srcH);
                }
                else
                {
                    rgb24 = ScaleAndConvert(bgra, srcW, srcH, _deviceW, _deviceH, DefaultScaleMode);
                }

                _dmd.Render((ushort)_deviceW, (ushort)_deviceH, rgb24);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[DmdOutputAdapter] Push failed: {ex.Message}");
            }
        }

        // ── Conversion ────────────────────────────────────────────────────────

        private static byte[] ConvertBgraToRgb24(byte[] bgra, int w, int h)
        {
            var rgb24 = new byte[w * h * 3];
            for (int i = 0, j = 0; i < bgra.Length; i += 4, j += 3)
            {
                rgb24[j]     = bgra[i + 2]; // R
                rgb24[j + 1] = bgra[i + 1]; // G
                rgb24[j + 2] = bgra[i];     // B
                // alpha ignored — DMD is opaque
            }
            return rgb24;
        }

        private static byte[] ScaleAndConvert(byte[] bgra, int srcW, int srcH,
                                               int dstW, int dstH, LayScaleMode mode)
        {
            // Determine actual source rect to use (for crop modes)
            int drawX = 0, drawY = 0, drawW = dstW, drawH = dstH;
            int srcRectX = 0, srcRectY = 0, srcRectW = srcW, srcRectH = srcH;

            if (mode == LayScaleMode.Fit)
            {
                // Letterbox: fit inside dstW×dstH, preserve ratio
                float scale = Math.Min((float)dstW / srcW, (float)dstH / srcH);
                drawW = (int)(srcW * scale);
                drawH = (int)(srcH * scale);
                drawX = (dstW - drawW) / 2;
                drawY = (dstH - drawH) / 2;
            }
            else if (mode == LayScaleMode.Fill)
            {
                // Crop: fill dstW×dstH, preserve ratio, crop center
                float scale = Math.Max((float)dstW / srcW, (float)dstH / srcH);
                var scaledW  = (int)(srcW * scale);
                var scaledH  = (int)(srcH * scale);
                srcRectX     = (scaledW - dstW) / 2;
                srcRectY     = (scaledH - dstH) / 2;
                srcRectW     = dstW;
                srcRectH     = dstH;
                // We'll sample from the scaled source
            }
            // Stretch: drawW=dstW, drawH=dstH (already set)

            var result = new byte[dstW * dstH * 3];

            // Bilinear sample from source BGRA into dest RGB24
            for (int dy = 0; dy < dstH; dy++)
            {
                for (int dx = 0; dx < dstW; dx++)
                {
                    // Map dest pixel → source pixel
                    float sx, sy;
                    if (mode == LayScaleMode.Fit)
                    {
                        int px = dx - drawX;
                        int py = dy - drawY;
                        if (px < 0 || py < 0 || px >= drawW || py >= drawH)
                        {
                            // Letterbox area — black
                            var ri = (dy * dstW + dx) * 3;
                            result[ri] = result[ri + 1] = result[ri + 2] = 0;
                            continue;
                        }
                        sx = (float)px / drawW * srcW;
                        sy = (float)py / drawH * srcH;
                    }
                    else
                    {
                        sx = (float)dx / dstW * srcW;
                        sy = (float)dy / dstH * srcH;
                    }

                    // Bilinear interpolation
                    int x0 = Math.Clamp((int)sx, 0, srcW - 1);
                    int y0 = Math.Clamp((int)sy, 0, srcH - 1);
                    int x1 = Math.Clamp(x0 + 1, 0, srcW - 1);
                    int y1 = Math.Clamp(y0 + 1, 0, srcH - 1);
                    float tx = sx - x0, ty = sy - y0;

                    var ri2 = (dy * dstW + dx) * 3;
                    for (int c = 0; c < 3; c++) // R, G, B
                    {
                        int ci = 2 - c; // BGRA → R=+2, G=+1, B=+0
                        float v = Lerp(
                            Lerp(bgra[y0 * srcW * 4 + x0 * 4 + ci],
                                 bgra[y0 * srcW * 4 + x1 * 4 + ci], tx),
                            Lerp(bgra[y1 * srcW * 4 + x0 * 4 + ci],
                                 bgra[y1 * srcW * 4 + x1 * 4 + ci], tx),
                            ty);
                        result[ri2 + c] = (byte)Math.Clamp(v, 0, 255);
                    }
                }
            }
            return result;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
