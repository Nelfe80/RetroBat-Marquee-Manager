using System.Runtime.CompilerServices;
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
        "media.flux", "lighting.engine", "effects.engine", "lamps.scene",
        "overlay.hiscore", "overlay.live.score", "overlay.live.timer",
        "overlay.ra.info", "overlay.ra.badges", "overlay.ra.speedrun"
    };

    private readonly ILogger _logger;
    private readonly List<(ComponentDefinition Definition, FrameworkElement Element)> _visuals = new();
    private string _scene = "navigation";

    /// <summary>Display state switch (navigation ↔ ingame). Components scoped by
    /// `when` show/hide; "both" components never blink.</summary>
    public void ApplyScene(string scene)
    {
        _scene = scene;
        foreach (var (definition, element) in _visuals)
        {
            RefreshVisibility(definition, element);
        }
    }

    /// <summary>Layers already baked into the dynamic render: drawing them live too
    /// would show an UNLIT copy on top of the lit one. Reference identity is enough —
    /// the suppressed set comes from the very component list this host was built
    /// from.</summary>
    private readonly HashSet<ComponentDefinition> _suppressed =
        new(ReferenceEqualityComparer.Instance as IEqualityComparer<ComponentDefinition>);

    public void SetSuppressed(IReadOnlyCollection<ComponentDefinition> suppressed)
    {
        _suppressed.Clear();
        foreach (var component in suppressed) _suppressed.Add(component);
        foreach (var (definition, element) in _visuals) RefreshVisibility(definition, element);
    }

    /// <summary>Final visibility = active in the current state, not baked into the
    /// dynamic render, AND has content.</summary>
    private void RefreshVisibility(ComponentDefinition definition, FrameworkElement element)
    {
        if (!definition.ActiveIn(_scene) || _suppressed.Contains(definition))
        {
            element.Visibility = Visibility.Collapsed;
            return;
        }

        var hasContent = element switch
        {
            Image image => image.Source != null,
            ContentControl host => host.Content != null,
            TextBlock text => text.Text.Length > 0,
            _ => true // shapes, gradients, web views
        };
        element.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

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
                case "iccard.viewer":
                    // fed by InstructionCardService through SetSource / SetCardSource
                    break;
            }
            RefreshVisibility(definition, element);
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
            RefreshVisibility(definition, element);
        }
    }

    /// <summary>
    /// Feeds the instruction card viewers tuned to ONE channel. A viewer says which
    /// channel it reads through its options — explicitly, or through its player or its
    /// role, so the usual setups need no naming at all.
    ///
    /// The historical `iccard.cycle` has no options to say it: it answers on the main
    /// channel, which is also what an unconfigured zone drives. A composition made
    /// before channels existed therefore behaves exactly as it did.
    /// </summary>
    public void SetCardSource(string channel, string? path)
    {
        foreach (var (definition, element) in _visuals)
        {
            var type = definition.Type.ToLowerInvariant();
            if (type is not ("iccard.viewer" or "iccard.cycle")) continue;
            var own = type == "iccard.cycle"
                ? "main"
                : Application.Services.InstructionCardCatalog.ChannelOf(
                    definition.Option("channel"), definition.Option("role"), definition.Option("player"));
            if (!own.Equals(channel, StringComparison.OrdinalIgnoreCase)) continue;
            SetImage(element, path);
            RefreshVisibility(definition, element);
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
            RefreshVisibility(definition, element);
        }

        // the fit depends on the TEXT, not only on the zone: a one-line name and a
        // 1500-character description in the same layer do not take the same size
        Relayout();
    }

    /// <summary>The cabinet's own panel description (retained on /ws/panel, so it
    /// arrives on connection). Every panel component takes it: the cabinet is the same
    /// on both sides, only the player differs.</summary>
    public void ApplyPanelConfig(Core.Surfaces.PanelBoardConfig config)
    {
        foreach (var (_, element) in _visuals)
        {
            if (element is PanelControlsView panel) panel.ApplyConfig(config);
        }
    }

    /// <summary>What the SELECTED game does with each place, per player. Being on a
    /// game's card in ES is enough — nothing has to be launched for the panel to tell
    /// what its buttons do.</summary>
    public void ApplyPanelButtons(int player, IReadOnlyDictionary<int, Core.Surfaces.PanelBoardButton> buttons)
    {
        foreach (var (_, element) in _visuals)
        {
            if (element is PanelControlsView panel && panel.Player == player) panel.ApplyButtons(buttons);
        }
    }

    /// <summary>The two drawn views of the panel. Each component takes the one its
    /// style asked for — seen from above, or from the front.</summary>
    public void ApplyPanelArt(Core.Surfaces.PanelBoardArt? top, Core.Surfaces.PanelBoardArt? front)
    {
        foreach (var (_, element) in _visuals)
        {
            if (element is not PanelControlsView panel || !panel.WantsArtwork) continue;
            panel.ApplyArt(panel.WantsFrontView ? front : top);
        }
    }

    /// <summary>A physical press, already resolved to a slot by APIExpose. Only the
    /// panel of the player who pressed lights up — that a press on panel 2 lights
    /// panel 2 is itself part of what the check verifies.</summary>
    public void SetPanelInput(int player, int? slot, string? system, bool pressed)
    {
        foreach (var (_, element) in _visuals)
        {
            if (element is not PanelControlsView panel || panel.Player != player) continue;
            if (slot.HasValue) panel.SetSlotPressed(slot.Value, pressed);
            else if (!string.IsNullOrEmpty(system)) panel.SetSystemPressed(system, pressed);
        }
    }

    /// <summary>Everything goes dark — the panel state on screen no longer describes
    /// what is being pressed.</summary>
    public void ReleasePanelInputs()
    {
        foreach (var (_, element) in _visuals)
        {
            if (element is PanelControlsView panel) panel.ReleaseAll();
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
            case "iccard.viewer":
                return new Image
                {
                    // A media is NEVER distorted: "fill" fills the zone keeping aspect
                    // (crops overflow), otherwise it fits (letterboxes). Never Stretch.Fill.
                    Stretch = component.Option("stretch") == "fill" ? Stretch.UniformToFill : Stretch.Uniform,
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
                    // A description is a paragraph, not a title: bold at 1500 characters
                    // is a wall. The weight follows the intent instead of being fixed.
                    FontWeight = component.Option("weight", "bold") == "normal" ? FontWeights.Normal : FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = component.Option("align", "center").ToLowerInvariant() switch
                    {
                        "left" => TextAlignment.Left,
                        "right" => TextAlignment.Right,
                        "justify" => TextAlignment.Justify,
                        _ => TextAlignment.Center
                    },
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

            case "panel.controls":
                // one component per player panel: a two-player cabinet places two of
                // them, each drawing its own side
                return new PanelControlsView(
                    int.TryParse(component.Option("player", "1"), out var player) ? player : 1,
                    component.Option("labels", "true") != "false",
                    component.Option("system", "true") != "false",
                    component.Option("style", "top"),
                    component.Option("bg"),
                    Fraction(component.Option("bgOpacity"), 0.5),
                    Fraction(component.Option("bgPadding"), 0.03));

            case "iccard.touch":
                return BuildTouchZone(component);

            case "external.web":
                return BuildWebView(component);

            default:
                _logger.LogWarning("Unknown surface component type {Type} ignored", component.Type);
                return null;
        }
    }

    /// <summary>
    /// A zone that answers to the finger. The rectangle IS the zone — what the user drew
    /// in the editor is what he can press — and the tap itself is handled by the window,
    /// which owns the touch events; this only draws what the player must SEE of it.
    ///
    /// Nothing by default: a working touchscreen needs no marking, and a card covered in
    /// boxes is a worse card. `label` writes a word in the zone, `hint` outlines it —
    /// both for a cabinet whose players do not know the zone is there.
    /// </summary>
    private static FrameworkElement BuildTouchZone(ComponentDefinition component)
    {
        var label = component.Option("label");
        var hint = component.Option("hint", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        var opacity = double.TryParse(component.Option("opacity", "0.35"),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? Math.Clamp(parsed, 0, 1)
            : 0.35;

        var border = new Border
        {
            // transparent, always: the zone must never hide the card it drives
            Background = Brushes.Transparent,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(hint ? 2 : 0),
            CornerRadius = new CornerRadius(8),
            Opacity = label.Length > 0 || hint ? opacity : 0
        };

        if (label.Length > 0)
        {
            border.Child = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 0 }
            };
        }

        return border;
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
            if (element is TextBlock text) LayoutText(definition, text, width, height);
        }
    }

    /// <summary>
    /// Sizes and places a text inside its zone.
    ///
    /// The size used to be half the zone's height, which only ever worked for a title:
    /// a game description is 500 to 1500 characters, and in a zone 300 px tall that rule
    /// asked for a 150 px font — one word on screen. The text is FITTED instead: it
    /// starts from what a single line would take and shrinks until the whole paragraph
    /// fits, so the same layer holds a name or a paragraph without being told which.
    ///
    /// Vertical placement is done by moving the block rather than by wrapping it in a
    /// container: a wrapper would change the element type, and the meta feed, the
    /// visibility rule and this very layout all recognise a text by being a TextBlock.
    /// </summary>
    private void LayoutText(ComponentDefinition definition, TextBlock text, double width, double height)
    {
        var explicitSize = definition.Option("size");
        if (double.TryParse(explicitSize, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fraction) && fraction > 0)
        {
            // a fraction of the SURFACE height, not of the zone: the text then keeps its
            // size when the zone is resized, which is what "I want it this big" means
            text.FontSize = Math.Max(8, fraction * ActualHeight);
        }
        else
        {
            text.FontSize = FitFontSize(text, width, height);
        }

        text.Height = double.NaN; // measured, not stretched
        text.Measure(new System.Windows.Size(width, double.PositiveInfinity));
        var used = Math.Min(text.DesiredSize.Height, height);
        var offset = definition.Option("valign", "top").ToLowerInvariant() switch
        {
            "middle" or "center" => (height - used) / 2,
            "bottom" => height - used,
            _ => 0
        };
        SetTop(text, definition.Y * ActualHeight + Math.Max(0, offset));
    }

    /// <summary>The largest size at which the whole text still fits, found by shrinking.
    /// Bounded: a dozen steps, and never below what stays readable on a marquee.</summary>
    private static double FitFontSize(TextBlock text, double width, double height)
    {
        var size = Math.Max(8, height * 0.5);
        for (var attempt = 0; attempt < 14; attempt++)
        {
            text.FontSize = size;
            text.Measure(new System.Windows.Size(width, double.PositiveInfinity));
            if (text.DesiredSize.Height <= height || size <= 9) break;
            size *= 0.85;
        }

        return Math.Max(8, size);
    }

    private static string? Lookup(IReadOnlyDictionary<string, string?> kinds, string kind)
        => kinds.TryGetValue(kind, out var path) ? path : null;

    // Per-Image load state: the path currently shown/loading and a monotonic
    // sequence so a slower decode can never overwrite a newer request. Kept in a
    // weak table so it disappears with the Image (no leak). Only touched on the
    // UI thread, so no locking is needed on its fields.
    private sealed class ImageLoadState
    {
        public string? CurrentPath;
        public int Seq;
    }

    private static readonly ConditionalWeakTable<Image, ImageLoadState> _imageState = new();

    /// <summary>Assigns an image source WITHOUT blocking the UI thread. The decode
    /// (BitmapImage EndInit reads and decodes the whole file — 100-234 ms for a
    /// large fanart/logo) runs on a thread-pool thread; the frozen bitmap is
    /// handed back on the Dispatcher. Two guards keep it correct and cheap:
    /// a DEDUP short-circuit (same path already shown/loading = no work) and a
    /// SEQUENCE guard (an older decode finishing late never clobbers a newer one).</summary>
    private static void SetImage(FrameworkElement element, string? path)
    {
        if (element is not Image image) return;
        var state = _imageState.GetOrCreateValue(image);

        // Same target as the one already shown or in flight: nothing to do. This
        // absorbs the repeated ApplyMedia passes (raw then enriched snapshot) that
        // otherwise re-decoded the identical file back to back.
        if (string.Equals(state.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
            return;

        state.CurrentPath = path;
        var seq = ++state.Seq;

        if (path == null || !File.Exists(path))
        {
            image.Source = null;
            return;
        }

        var dispatcher = image.Dispatcher;
        var target = path;
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            BitmapImage? bitmap = null;
            try
            {
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(target);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
            }
            catch
            {
                bitmap = null;
            }

            dispatcher.BeginInvoke(new Action(() =>
            {
                // A newer SetImage superseded this one while we decoded: drop it.
                if (state.Seq != seq) return;
                image.Source = bitmap;
            }));
        });
    }

    private void SetVideo(FrameworkElement element, string? path)
    {
        if (element is not ContentControl host) return;
        if (path == null)
        {
            (host.Content as MediaElement)?.Stop();
            host.Content = null;
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
                return;
            }

            if (!File.Exists(path))
            {
                (host.Content as MediaElement)?.Stop();
                host.Content = null;
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
            media.Play();
        }
        catch
        {
            host.Content = null;
        }
    }

    /// <summary>A 0..1 option written by a slider, invariant-culture. An unreadable value
    /// keeps the default rather than collapsing to zero — a background that vanished
    /// because a comma slipped into a decimal point would be a puzzle, not a setting.</summary>
    private static double Fraction(string value, double fallback)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

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
