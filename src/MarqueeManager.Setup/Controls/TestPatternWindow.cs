using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Test pattern ("mire") window: grid, border, center cross and a label, either
/// fullscreen on a display or restricted to a surface zone — this is how the user
/// verifies a *Bounds rectangle really lands where he expects, before saving it.
/// Click or Escape closes it.
/// </summary>
public sealed class TestPatternWindow : Window
{
    private readonly string _label;

    public TestPatternWindow(string label, int x, int y, int width, int height)
    {
        _label = label;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Content = new PatternElement(label, width, height);

        SourceInitialized += (_, _) => NativePlacement.Place(this, x, y, width, height);
        MouseDown += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private sealed class PatternElement : FrameworkElement
    {
        private readonly string _text;
        private readonly int _pixelWidth;
        private readonly int _pixelHeight;

        public PatternElement(string text, int pixelWidth, int pixelHeight)
        {
            _text = text;
            _pixelWidth = pixelWidth;
            _pixelHeight = pixelHeight;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0)
            {
                return;
            }

            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));

            var grid = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x44)), 1);
            const double step = 80;
            for (var gx = step; gx < w; gx += step)
            {
                dc.DrawLine(grid, new Point(gx, 0), new Point(gx, h));
            }

            for (var gy = step; gy < h; gy += step)
            {
                dc.DrawLine(grid, new Point(0, gy), new Point(w, gy));
            }

            var accent = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x00));
            var border = new Pen(accent, 6);
            dc.DrawRectangle(null, border, new Rect(3, 3, w - 6, h - 6));

            var cross = new Pen(accent, 2);
            dc.DrawLine(cross, new Point(w / 2 - 40, h / 2), new Point(w / 2 + 40, h / 2));
            dc.DrawLine(cross, new Point(w / 2, h / 2 - 40), new Point(w / 2, h / 2 + 40));

            var corner = new Pen(Brushes.White, 3);
            const double c = 34;
            dc.DrawLine(corner, new Point(0, 0), new Point(c, c));
            dc.DrawLine(corner, new Point(w, 0), new Point(w - c, c));
            dc.DrawLine(corner, new Point(0, h), new Point(c, h - c));
            dc.DrawLine(corner, new Point(w, h), new Point(w - c, h - c));

            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var title = new FormattedText(
                $"{_text} — {_pixelWidth}x{_pixelHeight}",
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                Math.Clamp(h / 8, 16, 42),
                Brushes.White,
                dpi);
            dc.DrawText(title, new Point((w - title.Width) / 2, h / 2 + 48));

            var hint = new FormattedText(
                Localization.L.T("Cliquez pour fermer", "Click to close"),
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                Math.Clamp(h / 16, 11, 18),
                new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x9A)),
                dpi);
            dc.DrawText(hint, new Point((w - hint.Width) / 2, h - hint.Height - 14));
        }
    }
}
