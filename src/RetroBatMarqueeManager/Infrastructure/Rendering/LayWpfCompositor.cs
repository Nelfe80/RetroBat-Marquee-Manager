using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBrushes   = System.Windows.Media.Brushes;
using WpfColor     = System.Windows.Media.Color;
using WpfColors    = System.Windows.Media.Colors;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSize      = System.Windows.Size;
using WpfRect      = System.Windows.Rect;

namespace RetroBatMarqueeManager.Infrastructure.Rendering
{
    /// <summary>
    /// Offscreen WPF compositor — ISO gfx_refresh() from ra.lua.
    /// Maintains a Canvas matching the .lay view reference dimensions,
    /// renders it to a RenderTargetBitmap on demand (no window required).
    /// Must be used on an STA thread.
    /// </summary>
    public class LayWpfCompositor : IDisposable
    {
        public double RefW { get; }
        public double RefH { get; }

        private readonly Canvas _canvas;
        private readonly Dictionary<string, UIElement> _elements  = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BitmapImage> _imgCache = new(StringComparer.OrdinalIgnoreCase);

        // Last snapshot (cache_screen equivalent)
        private byte[]? _snapshot;

        public LayWpfCompositor(double refW, double refH)
        {
            RefW = refW;
            RefH = refH;
            _canvas = new Canvas
            {
                Width        = refW,
                Height       = refH,
                Background   = WpfBrushes.Black,
                ClipToBounds = true
            };
        }

        // ── Apply a LayObject → UIElement ────────────────────────────────────
        // ISO gfx_refresh() for a single object

        public void Apply(LayObject obj)
        {
            if (!_elements.TryGetValue(obj.Name, out var el))
            {
                el = CreateElement(obj);
                if (el == null) return;
                _elements[obj.Name] = el;
                Canvas.SetZIndex(el, obj.Z);
                _canvas.Children.Add(el);
            }

            UpdateElement(el, obj);
            obj.Updated = false;
        }

        public void Remove(string name)
        {
            if (!_elements.TryGetValue(name, out var el)) return;
            _canvas.Children.Remove(el);
            _elements.Remove(name);
        }

        public void Clear()
        {
            _canvas.Children.Clear();
            _elements.Clear();
        }

        // ── Render offscreen → RenderTargetBitmap ────────────────────────────

        public RenderTargetBitmap Render()
        {
            _canvas.Measure(new WpfSize(RefW, RefH));
            _canvas.Arrange(new WpfRect(0, 0, RefW, RefH));
            _canvas.UpdateLayout();

            var rtb = new RenderTargetBitmap(
                (int)RefW, (int)RefH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_canvas);
            rtb.Freeze();
            return rtb;
        }

        // ── cache_screen() ────────────────────────────────────────────────────

        public byte[] Snapshot()
        {
            var rtb    = Render();
            var stride = (int)RefW * 4;
            var pixels = new byte[(int)RefH * stride];
            rtb.CopyPixels(pixels, stride, 0);
            _snapshot = pixels;
            return pixels;
        }

        public byte[]? GetLastSnapshot() => _snapshot;

        // ── Element factory ───────────────────────────────────────────────────

        private UIElement? CreateElement(LayObject obj) => obj.Type switch
        {
            LayObjectType.Image or LayObjectType.Gif => new System.Windows.Controls.Image
            {
                Stretch = Stretch.Fill
            },
            LayObjectType.Shape => new WpfRectangle(),
            LayObjectType.Text  => new TextBlock { TextWrapping = TextWrapping.Wrap },
            _                   => null
        };

        private void UpdateElement(UIElement el, LayObject obj)
        {
            var p = obj.Properties;

            // Visibility + opacity
            el.Visibility = p.Show ? Visibility.Visible : Visibility.Collapsed;
            el.Opacity    = Math.Clamp(p.Opacity, 0f, 1f);
            Canvas.SetZIndex(el, obj.Z);

            // Resolve anchor → actual X/Y
            var (rx, ry) = ResolvePosition(p);
            var (rw, rh) = ResolveDimensions(p, el);

            switch (el)
            {
                case System.Windows.Controls.Image img:
                    img.Width  = rw > 0 ? rw : double.NaN;
                    img.Height = rh > 0 ? rh : double.NaN;
                    if (!string.IsNullOrEmpty(p.ImagePath))
                        img.Source = LoadImage(p.ImagePath);
                    Canvas.SetLeft(img, rx);
                    Canvas.SetTop(img, ry);
                    img.Stretch = p.FitAndCrop ? Stretch.UniformToFill :
                                  p.FitInside  ? Stretch.Uniform :
                                  (rw < 0 || rh < 0) ? Stretch.Uniform : Stretch.Fill;
                    break;

                case WpfRectangle rect:
                    rect.Width   = rw > 0 ? rw : 0;
                    rect.Height  = rh > 0 ? rh : 0;
                    rect.Fill    = ParseBrush(p.ColorHex, p.Opacity);
                    rect.Opacity = 1.0;
                    Canvas.SetLeft(rect, rx);
                    Canvas.SetTop(rect, ry);
                    break;

                case TextBlock tb:
                    tb.Width     = rw > 0 ? rw : double.NaN;
                    tb.Height    = rh > 0 ? rh : double.NaN;
                    tb.Text      = p.Text ?? "";
                    tb.FontFamily = new WpfFontFamily(p.Font);
                    tb.FontSize  = p.Size;
                    tb.Foreground = new SolidColorBrush(ParseColor(p.Color));
                    tb.TextAlignment = AssAlignToTextAlignment(p.Align);
                    if (p.BorderSize > 0)
                    {
                        // Simulate text border via effect or manual rendering (simplified here)
                        tb.Effect = new System.Windows.Media.Effects.DropShadowEffect
                        {
                            Color      = ParseColor(p.BorderColor),
                            BlurRadius = p.BorderSize * 2,
                            ShadowDepth = 0,
                            Opacity    = 1.0
                        };
                    }
                    if (p.RotationZ.HasValue)
                        tb.RenderTransform = new RotateTransform(p.RotationZ.Value);
                    Canvas.SetLeft(tb, rx);
                    Canvas.SetTop(tb, ry);
                    break;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private (float x, float y) ResolvePosition(LayProperties p)
        {
            if (p.Anchor == null) return (p.X, p.Y);

            // Resolve anchor relative to canvas dimensions
            var (aw, ah) = ResolveDimensionsFromProps(p);
            return p.Anchor.ToLowerInvariant() switch
            {
                "top-left"      => (p.X, p.Y),
                "top-center"    => ((float)(RefW / 2 - aw / 2) + p.X, p.Y),
                "top-right"     => ((float)(RefW - aw) - p.X, p.Y),
                "center-left"   => (p.X, (float)(RefH / 2 - ah / 2) + p.Y),
                "center"        => ((float)(RefW / 2 - aw / 2) + p.X, (float)(RefH / 2 - ah / 2) + p.Y),
                "center-right"  => ((float)(RefW - aw) - p.X, (float)(RefH / 2 - ah / 2) + p.Y),
                "bottom-left"   => (p.X, (float)(RefH - ah) - p.Y),
                "bottom-center" => ((float)(RefW / 2 - aw / 2) + p.X, (float)(RefH - ah) - p.Y),
                "bottom-right"  => ((float)(RefW - aw) - p.X, (float)(RefH - ah) - p.Y),
                _               => (p.X, p.Y)
            };
        }

        private (float w, float h) ResolveDimensions(LayProperties p, UIElement el)
            => ResolveDimensionsFromProps(p);

        private (float w, float h) ResolveDimensionsFromProps(LayProperties p)
        {
            // -1 means "auto / preserve ratio" — WPF handles this via Stretch.Uniform + NaN
            return (p.W, p.H);
        }

        private BitmapImage LoadImage(string path)
        {
            if (_imgCache.TryGetValue(path, out var cached)) return cached;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource    = new Uri(path, UriKind.Absolute);
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();
                _imgCache[path] = bmp;
                return bmp;
            }
            catch { return new BitmapImage(); }
        }

        private static SolidColorBrush ParseBrush(string? hex, float opacity)
        {
            var color = ParseColor(hex);
            color.A   = (byte)Math.Clamp(opacity * 255, 0, 255);
            return new SolidColorBrush(color);
        }

        private static WpfColor ParseColor(string? hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length < 6) return WpfColors.White;
            try
            {
                hex = hex.TrimStart('#');
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return WpfColor.FromRgb(r, g, b);
            }
            catch { return WpfColors.White; }
        }

        private static TextAlignment AssAlignToTextAlignment(int assAlign) => (assAlign % 3) switch
        {
            1 => TextAlignment.Left,
            2 => TextAlignment.Center,
            0 => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        public void Dispose()
        {
            _imgCache.Clear();
            _elements.Clear();
            _canvas.Children.Clear();
        }
    }
}
