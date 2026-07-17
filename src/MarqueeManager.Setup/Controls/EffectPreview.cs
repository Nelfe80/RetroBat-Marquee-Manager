using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MarqueeManager.Setup.Data;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Small dark marquee band that REPLAYS an effect rule so the user can judge
/// color, rhythm and sprite without launching a game. WPF approximation of the
/// runtime's SkiaSharp render: flash/pulse/tint = colored veil, shake = band
/// jolt, strobe/blackout = tube dimming, sprite = animated GIF frames with the
/// motion roughly honored. Deliberately theme-invariant (dark plate).
/// </summary>
public sealed class EffectPreview : UserControl
{
    /// <summary>Preview band size — the effect composer asks for a bigger one.</summary>
    private readonly double BandWidth;
    private readonly double BandHeight;

    private readonly Grid _band;
    private readonly Rectangle _tube;
    private readonly Rectangle _veil;
    private readonly Canvas _spriteLayer;
    private readonly TranslateTransform _jolt = new();
    private readonly List<System.Windows.Threading.DispatcherTimer> _timers = new();

    public EffectPreview(double width = 320)
    {
        BandWidth = Math.Clamp(width, 200, 1600);
        BandHeight = BandWidth * 72 / 320;
        _tube = new Rectangle
        {
            Fill = new LinearGradientBrush(
                Color.FromRgb(0x3A, 0x38, 0x2E), Color.FromRgb(0x14, 0x13, 0x0E), 90),
            RadiusX = 4,
            RadiusY = 4
        };
        _veil = new Rectangle { Opacity = 0, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
        _spriteLayer = new Canvas { ClipToBounds = true, IsHitTestVisible = false };

        _band = new Grid { Width = BandWidth, Height = BandHeight, RenderTransform = _jolt };
        _band.Children.Add(_tube);
        var label = new TextBlock
        {
            Text = "MARQUEE",
            Foreground = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xE8, 0xB0)),
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _band.Children.Add(label);
        _band.Children.Add(_spriteLayer);
        _band.Children.Add(_veil);

        Content = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _band
        };
    }

    /// <summary>Replays the rule once (stops whatever was still running).</summary>
    public void Play(EffectRule rule, string spritesDirectory)
    {
        Stop();
        if (rule.Off)
        {
            return;
        }

        var color = ParseColor(rule.Color);
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(rule.DurationMs, 80, 4000));

        switch (rule.Kind)
        {
            case "pulse":
                Veil(color, 0.55, duration, autoReverse: true);
                break;
            case "tint":
                Veil(color, 0.35, duration);
                break;
            case "shake":
                Shake(rule.Dip <= 0 ? 0.5 : rule.Dip, duration);
                break;
            case "strobe":
                Strobe(rule.Dip <= 0 ? 0.5 : rule.Dip, duration);
                break;
            case "blackout":
            case "powercycle":
                Blackout(duration);
                break;
            case "sprite":
                break; // sprites only
            default: // flash
                Veil(color, 0.7, duration);
                if (rule.Dip > 0) Dim(rule.Dip, duration);
                break;
        }

        if (rule.Sprite is { Length: > 0 })
        {
            SpawnSprites(Path.Combine(spritesDirectory, rule.Sprite), rule.Count, rule.Motion, duration,
                rule.Scale <= 0 ? 1.0 : rule.Scale);
        }
    }

    public void Stop()
    {
        foreach (var timer in _timers) timer.Stop();
        _timers.Clear();
        _spriteLayer.Children.Clear();
        _veil.BeginAnimation(OpacityProperty, null);
        _veil.Opacity = 0;
        _tube.BeginAnimation(OpacityProperty, null);
        _tube.Opacity = 1;
        _jolt.BeginAnimation(TranslateTransform.XProperty, null);
        _jolt.BeginAnimation(TranslateTransform.YProperty, null);
        _jolt.X = _jolt.Y = 0;
    }

    private void Veil(Color color, double peak, TimeSpan duration, bool autoReverse = false)
    {
        _veil.Fill = new SolidColorBrush(color);
        var animation = new DoubleAnimation(peak, 0, duration) { EasingFunction = new QuadraticEase() };
        if (autoReverse)
        {
            animation = new DoubleAnimation(0, peak, TimeSpan.FromMilliseconds(duration.TotalMilliseconds / 2))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase()
            };
        }
        _veil.BeginAnimation(OpacityProperty, animation);
    }

    private void Dim(double dip, TimeSpan duration)
    {
        var animation = new DoubleAnimation(1 - Math.Clamp(dip, 0, 1), 1,
            TimeSpan.FromMilliseconds(duration.TotalMilliseconds))
        { EasingFunction = new QuadraticEase() };
        _tube.BeginAnimation(OpacityProperty, animation);
    }

    private void Shake(double amplitude, TimeSpan duration)
    {
        var pixels = 4 + amplitude * 10;
        var x = new DoubleAnimationUsingKeyFrames { Duration = duration };
        var random = new Random(7);
        var steps = Math.Max(4, (int)(duration.TotalMilliseconds / 40));
        for (var i = 0; i <= steps; i++)
        {
            var progress = (double)i / steps;
            var value = i == steps ? 0 : (random.NextDouble() * 2 - 1) * pixels * (1 - progress);
            x.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromPercent(progress)));
        }
        _jolt.BeginAnimation(TranslateTransform.XProperty, x);
    }

    private void Strobe(double depth, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
        var cycles = Math.Max(2, (int)(duration.TotalMilliseconds / 55));
        for (var i = 0; i <= cycles; i++)
        {
            animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(
                i % 2 == 0 ? 1 - Math.Clamp(depth, 0, 1) : 1,
                KeyTime.FromPercent((double)i / cycles)));
        }
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromPercent(1)));
        _tube.BeginAnimation(OpacityProperty, animation);
    }

    private void Blackout(TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = duration + TimeSpan.FromMilliseconds(500) };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration + TimeSpan.FromMilliseconds(500))));
        _tube.BeginAnimation(OpacityProperty, animation);
    }

    private void SpawnSprites(string gifPath, int count, string motion, TimeSpan duration, double scale)
    {
        var frames = DecodeGif(gifPath);
        if (frames.Count == 0)
        {
            return;
        }

        var random = new Random();
        var life = duration < TimeSpan.FromMilliseconds(500) ? TimeSpan.FromMilliseconds(700) : duration;
        var height = 34 * scale;
        for (var i = 0; i < Math.Clamp(count, 1, 6); i++)
        {
            var image = new Image { Source = frames[0], Height = height, Stretch = Stretch.Uniform };
            if (scale >= 1.5)
            {
                // same rule as the runtime: deliberate upscales keep crisp pixels
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            }
            var startX = random.NextDouble() * (BandWidth - 40);
            var startY = motion switch
            {
                "fall" => -(height + 2),
                "rise" => BandHeight,
                _ => random.NextDouble() * Math.Max(2, BandHeight - height)
            };
            Canvas.SetLeft(image, startX);
            Canvas.SetTop(image, startY);
            _spriteLayer.Children.Add(image);

            // GIF frames on a plain timer — cheap and disposable
            var frameIndex = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
            timer.Tick += (_, _) =>
            {
                frameIndex = (frameIndex + 1) % frames.Count;
                image.Source = frames[frameIndex];
            };
            timer.Start();
            _timers.Add(timer);

            var move = new TranslateTransform();
            image.RenderTransform = move;
            var delay = TimeSpan.FromMilliseconds(i * 120);
            switch (motion)
            {
                case "fall":
                    move.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, BandHeight + 60, life) { BeginTime = delay });
                    break;
                case "rise":
                    move.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, -(BandHeight + 60), life) { BeginTime = delay });
                    break;
                case "cross":
                    move.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(-startX - 60, BandWidth + 60, life) { BeginTime = delay });
                    break;
                default: // pop
                    image.Opacity = 0;
                    image.BeginAnimation(OpacityProperty, BuildPopOpacity(life, delay));
                    break;
            }

            var cleanup = new System.Windows.Threading.DispatcherTimer { Interval = life + delay + TimeSpan.FromMilliseconds(200) };
            cleanup.Tick += (_, _) =>
            {
                cleanup.Stop();
                timer.Stop();
                _spriteLayer.Children.Remove(image);
            };
            cleanup.Start();
            _timers.Add(cleanup);
        }
    }

    private static DoubleAnimationUsingKeyFrames BuildPopOpacity(TimeSpan life, TimeSpan delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = delay, Duration = life };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.12)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.75)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
        return animation;
    }

    private static List<BitmapSource> DecodeGif(string path)
    {
        var frames = new List<BitmapSource>();
        try
        {
            if (!File.Exists(path))
            {
                return frames;
            }

            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            foreach (var frame in decoder.Frames)
            {
                frame.Freeze();
                frames.Add(frame);
            }
        }
        catch
        {
            // undecodable gif: no sprite in the preview
        }
        return frames;
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Color.FromRgb(0xFF, 0x2A, 0x18);
        }
    }
}
