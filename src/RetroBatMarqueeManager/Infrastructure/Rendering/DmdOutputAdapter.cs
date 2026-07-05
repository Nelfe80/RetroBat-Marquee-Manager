using System.Windows.Media.Imaging;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Rendering;

public sealed class DmdOutputAdapter : ILayOutputAdapter
{
    private readonly IDmdService _dmd;
    private readonly int _deviceWidth;
    private readonly int _deviceHeight;
    private readonly ILogger _logger;

    public DmdOutputAdapter(IDmdService dmd, int deviceWidth, int deviceHeight, ILogger logger, LayScaleMode scaleMode = LayScaleMode.Fit)
    {
        _dmd = dmd;
        _deviceWidth = deviceWidth;
        _deviceHeight = deviceHeight;
        _logger = logger;
        DefaultScaleMode = scaleMode;
    }

    public LayScaleMode DefaultScaleMode { get; }

    public void Push(RenderTargetBitmap frame, double refWidth, double refHeight)
    {
        try
        {
            var sourceWidth = Math.Max(1, (int)refWidth);
            var sourceHeight = Math.Max(1, (int)refHeight);
            var stride = sourceWidth * 4;
            var source = new byte[sourceHeight * stride];
            frame.CopyPixels(source, stride, 0);
            var output = new byte[_deviceWidth * _deviceHeight * 3];
            var scale = Math.Min((double)_deviceWidth / sourceWidth, (double)_deviceHeight / sourceHeight);
            var drawWidth = Math.Max(1, (int)(sourceWidth * scale));
            var drawHeight = Math.Max(1, (int)(sourceHeight * scale));
            var offsetX = (_deviceWidth - drawWidth) / 2;
            var offsetY = (_deviceHeight - drawHeight) / 2;
            for (var y = 0; y < drawHeight; y++)
            for (var x = 0; x < drawWidth; x++)
            {
                var sourceX = Math.Clamp(x * sourceWidth / drawWidth, 0, sourceWidth - 1);
                var sourceY = Math.Clamp(y * sourceHeight / drawHeight, 0, sourceHeight - 1);
                var input = sourceY * stride + sourceX * 4;
                var target = ((y + offsetY) * _deviceWidth + x + offsetX) * 3;
                output[target] = source[input + 2];
                output[target + 1] = source[input + 1];
                output[target + 2] = source[input];
            }
            _dmd.SetLayoutFrame(output);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to push .lay frame to DMD coordinator");
        }
    }
}
