using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RetroBatMarqueeManager.Core.Surfaces;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;

namespace RetroBatMarqueeManager.Infrastructure.UI;

/// <summary>
/// Canvas hosting the DYNAMIC components of a surface (media.logo, media.fanart,
/// media.image, media.video, text.meta, text.custom, iccard.static, iccard.cycle,
/// external.web). Built-in component types (media.flux, lighting.engine,
/// lamps.scene, overlay.*) are rendered by the historical MarqueeWindow layers
/// and skipped here. Component rects are fractions of the surface; the host
/// re-lays out on every size change.
/// </summary>
public sealed class ComponentHost : Canvas
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "media.flux", "lighting.engine", "lamps.scene",
        "overlay.hiscore", "overlay.live.score", "overlay.live.timer",
        "overlay.ra.info", "overlay.ra.badges", "overlay.ra.speedrun"
    };

    private readonly ILogger _logger;
    private readonly List<(ComponentDefinition Definition, FrameworkElement Element)> _visuals = new();

    /// <summary>True when the surface declares at least one non-built-in component.</summary>
    public static bool IsNeeded(SurfaceDefinition surface)
        => surface.Components.Any(c => !BuiltIn.Contains(c.Type));

    public ComponentHost(SurfaceDefinition surface, ILogger logger)
    {
        _logger = logger;
        ClipToBounds = true;
        IsHitTestVisible = false; // taps stay on the window (touch iccard)

        foreach (var component in surface.Components)
        {
            if (BuiltIn.Contains(component.Type)) continue;
            var element = Build(component);
            if (element == null) continue;
            _visuals.Add((component, element));
            Children.Add(element);
        }

        SizeChanged += (_, _) => Relayout();
    }

    /// <summary>Feeds every media-driven component from the snapshot kinds
    /// (logo, fanart, screenmarquee, marquee, video…). Null path = hide.</summary>
    public void ApplyMedia(IReadOnlyDictionary<string, string?> kinds)
    {
        foreach (var (definition, element) in _visuals)
        {
            switch (definition.Type.ToLowerInvariant())
            {
                case "media.logo":
                    SetImage(element, Lookup(kinds, "logo"));
                    break;
                case "media.fanart":
                    SetImage(element, Lookup(kinds, "fanart"));
                    break;
                case "media.image":
                    SetImage(element, Lookup(kinds, definition.Option("kind", "screenmarquee")));
                    break;
                case "media.video":
                    SetVideo(element, Lookup(kinds, "video"));
                    break;
                case "iccard.static":
                case "iccard.cycle":
                    // fed by InstructionCardService through SetSource
                    break;
            }
        }
    }

    /// <summary>Direct source assignment for one component type (instruction cards…).</summary>
    public void SetSource(string type, string? path)
    {
        foreach (var (definition, element) in _visuals)
        {
            if (!definition.Type.Equals(type, StringComparison.OrdinalIgnoreCase)) continue;
            if (definition.Type.StartsWith("media.video", StringComparison.OrdinalIgnoreCase))
                SetVideo(element, path);
            else
                SetImage(element, path);
        }
    }

    /// <summary>Renders the text.meta template ({name} {year} {developer} {publisher}
    /// {system}) with the current selection values.</summary>
    public void ApplyMeta(IReadOnlyDictionary<string, string> meta)
    {
        foreach (var (definition, element) in _visuals)
        {
            if (!definition.Type.Equals("text.meta", StringComparison.OrdinalIgnoreCase)
                || element is not TextBlock text) continue;
            var template = definition.Option("template", "{name}");
            foreach (var (key, value) in meta)
                template = template.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
            text.Text = template.Trim();
            text.Visibility = text.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ================= construction =================

    private FrameworkElement? Build(ComponentDefinition component)
    {
        switch (component.Type.ToLowerInvariant())
        {
            case "media.logo":
            case "media.fanart":
            case "media.image":
            case "iccard.static":
            case "iccard.cycle":
                return new Image
                {
                    Stretch = component.Option("stretch") == "fill" ? Stretch.Fill : Stretch.Uniform,
                    Visibility = Visibility.Collapsed
                };

            case "media.video":
                // host swaps between a MediaElement (local file) and a WebView2
                // (live stream / embed URL) depending on what the chain resolved
                return new ContentControl { Visibility = Visibility.Collapsed };

            case "text.custom":
            case "text.meta":
            {
                var text = new TextBlock
                {
                    Text = component.Option("text"),
                    Foreground = ParseBrush(component.Option("color", "#FFFFFF")),
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0 }
                };
                return text;
            }

            case "shape.gradient":
            {
                // readability veil under a logo (surface templates): linear gradient
                // from transparent to `color`, direction up|down|left|right
                var color = ParseBrush(component.Option("color", "#000000")) is SolidColorBrush brush
                    ? brush.Color
                    : Colors.Black;
                var direction = component.Option("direction", "down").ToLowerInvariant();
                var (start, end) = direction switch
                {
                    "up" => (new Point(0, 1), new Point(0, 0)),
                    "left" => (new Point(1, 0), new Point(0, 0)),
                    "right" => (new Point(0, 0), new Point(1, 0)),
                    _ => (new Point(0, 0), new Point(0, 1))
                };
                var opacity = double.TryParse(component.Option("opacity", "0.7"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedOpacity)
                    ? Math.Clamp(parsedOpacity, 0, 1)
                    : 0.7;
                return new System.Windows.Shapes.Rectangle
                {
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(0, color.R, color.G, color.B),
                        Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B),
                        start, end)
                };
            }

            case "external.web":
                return BuildWebView(component);

            default:
                _logger.LogWarning("Unknown surface component type {Type} ignored", component.Type);
                return null;
        }
    }

    /// <summary>Embedded web player (Twitch/YouTube embeds, any page). WebView2's
    /// Edge runtime ships with Windows 11; when missing the component degrades to
    /// a muted notice — never fatal.</summary>
    private FrameworkElement BuildWebView(ComponentDefinition component)
    {
        var url = component.Option("url");
        try
        {
            var view = new Microsoft.Web.WebView2.Wpf.WebView2();
            Loaded += async (_, _) =>
            {
                try
                {
                    await view.EnsureCoreWebView2Async();
                    view.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    view.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    if (component.Option("mute", "true") != "false")
                        view.CoreWebView2.IsMuted = true;
                    if (url.Length > 0) view.Source = new Uri(url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("WebView2 runtime unavailable, external.web hidden: {Message}", ex.Message);
                    view.Visibility = Visibility.Collapsed;
                }
            };
            return view;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("external.web component unavailable: {Message}", ex.Message);
            return new TextBlock
            {
                Text = "WebView2 runtime missing",
                Foreground = Brushes.Gray,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
        }
    }

    // ================= layout & media =================

    private void Relayout()
    {
        foreach (var (definition, element) in _visuals)
        {
            var width = Math.Max(1, definition.W * ActualWidth);
            var height = Math.Max(1, definition.H * ActualHeight);
            element.Width = width;
            element.Height = height;
            SetLeft(element, definition.X * ActualWidth);
            SetTop(element, definition.Y * ActualHeight);
            if (element is TextBlock text)
                text.FontSize = Math.Max(8, height * 0.5);
        }
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> kinds, string kind)
        => kinds.TryGetValue(kind, out var path) ? path : null;

    private static void SetImage(FrameworkElement element, string? path)
    {
        if (element is not Image image) return;
        if (path == null || !File.Exists(path))
        {
            image.Visibility = Visibility.Collapsed;
            image.Source = null;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            image.Source = bitmap;
            image.Visibility = Visibility.Visible;
        }
        catch
        {
            image.Visibility = Visibility.Collapsed;
        }
    }

    private void SetVideo(FrameworkElement element, string? path)
    {
        if (element is not ContentControl host) return;
        if (path == null)
        {
            (host.Content as MediaElement)?.Stop();
            host.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // live stream / embed: same WebView2 path as external.web
                if (host.Content is not Microsoft.Web.WebView2.Wpf.WebView2)
                {
                    (host.Content as MediaElement)?.Stop();
                    host.Content = BuildWebView(new ComponentDefinition("external.web",
                        Options: new Dictionary<string, string> { ["url"] = path, ["mute"] = "true" }));
                }
                else if (host.Content is Microsoft.Web.WebView2.Wpf.WebView2 { CoreWebView2: not null } view)
                {
                    view.Source = new Uri(path);
                }
                host.Visibility = Visibility.Visible;
                return;
            }

            if (!File.Exists(path))
            {
                host.Visibility = Visibility.Collapsed;
                return;
            }

            if (host.Content is not MediaElement media)
            {
                media = new MediaElement
                {
                    LoadedBehavior = MediaState.Manual,
                    UnloadedBehavior = MediaState.Manual,
                    Stretch = Stretch.Uniform,
                    IsMuted = true
                };
                media.MediaEnded += (_, _) =>
                {
                    media.Position = TimeSpan.FromMilliseconds(1);
                    media.Play();
                };
                host.Content = media;
            }
            media.Source = new Uri(path);
            host.Visibility = Visibility.Visible;
            media.Play();
        }
        catch
        {
            host.Visibility = Visibility.Collapsed;
        }
    }

    private static System.Windows.Media.Brush ParseBrush(string hex)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return Brushes.White;
        }
    }
}
