using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using RetroBatMarqueeManager.Core.Surfaces;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace RetroBatMarqueeManager.Infrastructure.UI;

/// <summary>
/// The cabinet's control panel, drawn live: its real buttons, what the selected game
/// makes of them, and WHICH ONE the player is pressing right now.
///
/// That last part is the whole point. Press the bottom-left button, see the
/// bottom-left button light up: the wiring is right. See another one light up, or
/// none at all, and the wiring is wrong — in one second, with no config file to read
/// and no LedManager to install.
///
/// It is laid out at a fixed design size inside a Viewbox rather than against the
/// component rectangle: the panel then keeps the proportions of the SVG APIExpose
/// writes for themes (same radii, same gaps, same convention), so the panel on the
/// marquee and the panel in the theme are the same drawing at two sizes.
/// </summary>
public sealed class PanelControlsView : Viewbox
{
    // the SVG renderer's geometry, kept identical on purpose
    private const double ButtonRadius = 26;
    private const double ButtonGap = 18;
    private const double RowGap = 22;
    private const double Margin = 28;
    private const double StickRadius = 34;

    /// <summary>A button the game does not use is drawn faded, not hidden: the panel
    /// has to show the holes the cabinet really has.</summary>
    private const double UnusedOpacity = 0.2;

    /// <summary>A tap can last 30 ms. Lighting for exactly as long as the press would
    /// flicker past unseen, so a press always shows for at least this long.</summary>
    private static readonly TimeSpan MinimumLit = TimeSpan.FromMilliseconds(150);

    /// <summary>Fade rather than snap: a rattle of buttons snapping on and off reads
    /// as flicker instead of as playing.</summary>
    private static readonly Duration FadeOut = new(TimeSpan.FromMilliseconds(250));

    /// <summary>A press whose release never arrives would leave the panel lit for good.
    /// A reconnection already darkens everything, so this only catches what survives a
    /// live connection — and it has to stay well clear of real play: holding fire or a
    /// charge shot for several seconds is ordinary, and cutting the light there would
    /// tell the player their button had stopped answering.</summary>
    private static readonly TimeSpan StuckPressTimeout = TimeSpan.FromSeconds(30);

    private readonly Canvas _canvas = new();
    private readonly Dictionary<int, Lamp> _slots = new();
    private readonly Dictionary<string, Lamp> _systemButtons = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _showLabels;
    private readonly bool _showSystemButtons;
    private readonly string _style;
    private readonly string _background;
    private readonly double _backgroundOpacity;
    private readonly double _backgroundPadding;

    private PanelBoardConfig _config = PanelBoardConfig.Unknown;
    private IReadOnlyDictionary<int, PanelBoardButton> _buttons = new Dictionary<int, PanelBoardButton>();
    private PanelBoardArt? _art;
    private string _artStamp = string.Empty;
    private int _artRetries;

    /// <summary>Which panel this component draws. A two-player cabinet carries two
    /// components, one per player, each positioned where its side of the panel is.</summary>
    public int Player { get; }

    public PanelControlsView(int player, bool showLabels, bool showSystemButtons, string style,
        string background, double backgroundOpacity, double backgroundPadding)
    {
        Player = Math.Max(1, player);
        _showLabels = showLabels;
        _showSystemButtons = showSystemButtons;
        _style = string.IsNullOrWhiteSpace(style) ? "default" : style.Trim().ToLowerInvariant();
        _background = background;
        _backgroundOpacity = Math.Clamp(backgroundOpacity, 0, 1);
        _backgroundPadding = Math.Clamp(backgroundPadding, 0, 0.2);
        Child = _canvas;
        Stretch = Stretch.Uniform;
        Build();
    }

    /// <summary>
    /// A backdrop behind the panel. The artwork is drawn on transparency, so over a busy
    /// fanart the buttons lose their edges; a veil under them gives the drawing something
    /// to sit on. Drawn slightly larger than the panel, so the outer buttons are not
    /// touching its edge.
    /// </summary>
    private void DrawBackground(double width, double height)
    {
        if (string.IsNullOrWhiteSpace(_background) || _backgroundOpacity <= 0) return;

        var bleed = Math.Max(width, height) * _backgroundPadding;
        var veil = new System.Windows.Shapes.Rectangle
        {
            Width = width + bleed * 2,
            Height = height + bleed * 2,
            RadiusX = bleed * 2,
            RadiusY = bleed * 2,
            Fill = new SolidColorBrush(ParseColor(_background, Colors.Black)),
            Opacity = _backgroundOpacity
        };
        Canvas.SetLeft(veil, -bleed);
        Canvas.SetTop(veil, -bleed);
        _canvas.Children.Add(veil);
    }

    /// <summary>True when this component wants the drawn artwork rather than the plain
    /// shapes — "top" (seen from above) or "3d" (seen from the front).</summary>
    public bool WantsArtwork => _style is "top" or "3d";

    /// <summary>Which of the two drawn views this component asked for.</summary>
    public bool WantsFrontView => _style == "3d";

    /// <summary>
    /// The panel as APIExpose drew it, for the view this component asked for. Null —
    /// no file yet for this game — falls back to the plain shapes rather than to an
    /// empty rectangle: the wiring check has to work on a cabinet whose theme artwork
    /// was never generated.
    /// </summary>
    public void ApplyArt(PanelBoardArt? art)
    {
        // The path alone does not identify the drawing: every system that is not arcade
        // shares a single "default.svg", rewritten with each game's own colours and
        // functions. Same name, different panel — so the file's own timestamp is what
        // says whether this is still the same picture.
        var stamp = Stamp(art);
        if (stamp == _artStamp) return;
        _artStamp = stamp;
        _art = art;
        _artRetries = 0; // a new drawing deserves its own attempts
        Build();
    }

    private static string Stamp(PanelBoardArt? art)
    {
        if (art is null) return string.Empty;
        try
        {
            return $"{art.Path}|{File.GetLastWriteTimeUtc(art.Path).Ticks}";
        }
        catch
        {
            return art.Path;
        }
    }

    /// <summary>The cabinet's own description. Rebuilds the drawing: a panel that grew
    /// from six to eight buttons is a different panel.</summary>
    public void ApplyConfig(PanelBoardConfig config)
    {
        _config = config;
        Build();
    }

    /// <summary>What the selected game does with each place. Nothing is rebuilt — only
    /// the colours and the labels change, so a press held across a selection keeps its
    /// light.</summary>
    public void ApplyButtons(IReadOnlyDictionary<int, PanelBoardButton> buttons)
    {
        _buttons = buttons;
        foreach (var (slot, lamp) in _slots)
        {
            _buttons.TryGetValue(slot, out var button);
            lamp.Describe(button);
        }
    }

    /// <summary>A physical press, already resolved to a slot by APIExpose.</summary>
    public void SetSlotPressed(int slot, bool pressed)
    {
        if (_slots.TryGetValue(slot, out var lamp)) lamp.SetPressed(pressed);
    }

    /// <summary>START, SELECT and the stick clicks: wired on their own pins, outside
    /// the numbered slots, so they light their own places.</summary>
    public void SetSystemPressed(string name, bool pressed)
    {
        if (_systemButtons.TryGetValue(name, out var lamp)) lamp.SetPressed(pressed);
    }

    /// <summary>Everything goes dark. Called when the panel state no longer describes
    /// what is on screen — a press is about the here and now.</summary>
    public void ReleaseAll()
    {
        foreach (var lamp in _slots.Values) lamp.SetPressed(false);
        foreach (var lamp in _systemButtons.Values) lamp.SetPressed(false);
    }

    // ================= drawing =================

    private void Build()
    {
        _canvas.Children.Clear();
        _slots.Clear();
        _systemButtons.Clear();

        if (WantsArtwork && BuildFromArtwork()) return;

        var rows = _config.Rows.Count > 0 ? _config.Rows : PanelBoardConfig.Unknown.Rows;
        var columns = rows.Max(row => row.Count);
        var stickWidth = _config.HasStick ? StickRadius * 2 + ButtonGap * 2 : 0;
        var width = Margin * 2 + stickWidth + columns * (ButtonRadius * 2) + (columns - 1) * ButtonGap;
        var top = Margin + 30; // headroom for the system buttons
        var height = Margin * 2 + rows.Count * (ButtonRadius * 2) + (rows.Count - 1) * RowGap + 46;

        _canvas.Width = width;
        _canvas.Height = height;
        DrawBackground(width, height);

        if (_config.HasStick)
        {
            var cx = Margin + StickRadius;
            var cy = top + rows.Count * ButtonRadius + (rows.Count - 1) * RowGap / 2.0;
            DrawStick(cx, cy);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            // a shorter top row is centred over the longer one, as the convention draws it
            var rowWidth = row.Count * (ButtonRadius * 2) + (row.Count - 1) * ButtonGap;
            var startX = Margin + stickWidth
                + (columns * (ButtonRadius * 2) + (columns - 1) * ButtonGap - rowWidth) / 2.0;
            var cy = top + ButtonRadius + r * (ButtonRadius * 2 + RowGap);

            for (var c = 0; c < row.Count; c++)
            {
                var slot = row[c];
                var cx = startX + ButtonRadius + c * (ButtonRadius * 2 + ButtonGap);
                var lamp = AddButton(cx, cy, ButtonRadius);
                _slots[slot] = lamp;
                _buttons.TryGetValue(slot, out var described);
                lamp.Describe(described);
            }
        }

        if (_showSystemButtons)
        {
            // SELECT then START, top-left: the convention drives them on their own pins,
            // so they belong outside the rows — showing them among the game buttons would
            // read as a mis-wired panel when nothing is wrong.
            var x = Margin + 22;
            foreach (var name in new[] { "SELECT", "START" })
            {
                _systemButtons[name] = AddSystemButton(x, Margin + 4, name);
                x += 74;
            }
        }
    }

    /// <summary>
    /// The drawn panel: APIExpose's own artwork, rasterised, with a light placed over
    /// each button at the coordinates that artwork reported.
    ///
    /// Nothing is drawn on top of it besides the lights — the picture already carries
    /// the buttons, their colours and what they do in this game, and drawing our own
    /// over it would show two panels at once.
    ///
    /// Returns false when the file cannot be read: the caller then falls back to the
    /// plain shapes. A cabinet whose theme artwork was never generated still deserves
    /// its wiring check.
    /// </summary>
    private bool BuildFromArtwork()
    {
        if (_art is null || _art.Width <= 0 || _art.Height <= 0) return false;

        var bitmap = PanelArtworkCache.Render(_art.Path, _art.Width, _art.Height);
        if (bitmap is null)
        {
            // The file can be unreadable for a moment — it is rewritten the instant a
            // game starts, and an antivirus can hold it just as briefly. Falling back to
            // the plain shapes for good on a moment like that is how the panel "lost its
            // artwork the second you launched a game". Try again shortly; the plain
            // drawing stands in meanwhile, so the wiring check never stops working.
            ScheduleArtworkRetry();
            return false;
        }

        _artRetries = 0;

        _canvas.Width = _art.Width;
        _canvas.Height = _art.Height;
        DrawBackground(_art.Width, _art.Height);

        var picture = new System.Windows.Controls.Image
        {
            Source = bitmap,
            Width = _art.Width,
            Height = _art.Height,
            Stretch = Stretch.Fill // the bitmap IS this drawing, rendered at its own ratio
        };
        Canvas.SetLeft(picture, 0);
        Canvas.SetTop(picture, 0);
        _canvas.Children.Add(picture);

        foreach (var button in _art.Buttons)
        {
            var lamp = AddArtworkLamp(button.Cx, button.Cy, button.R);
            _slots[button.Slot] = lamp;
            _buttons.TryGetValue(button.Slot, out var described);
            lamp.Describe(described);
        }

        return true;
    }

    /// <summary>Comes back for the drawing in a moment, a few times, then gives up and
    /// leaves the plain panel in place.</summary>
    private void ScheduleArtworkRetry()
    {
        if (_artRetries >= 4) return;
        _artRetries++;

        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_art != null) Build();
        };
        timer.Start();
    }

    /// <summary>A light with NO body of its own: the artwork underneath is the button,
    /// and this only adds what a press adds — a ring and a glow.</summary>
    private Lamp AddArtworkLamp(double cx, double cy, double radius)
    {
        var lit = Glow(cx, cy, radius);
        _canvas.Children.Add(lit);

        // the invisible body carries the resting opacity so the lamp logic is the same
        // in both modes; over artwork it must never paint anything
        var ghost = new Ellipse { Width = 0, Height = 0, Opacity = 0 };
        _canvas.Children.Add(ghost);
        return new Lamp(ghost, lit, null, Dispatcher) { PaintsBody = false };
    }

    /// <summary>
    /// A lamp, built like the lighting engine builds its own: a wide soft halo with a
    /// hotter core, in the button's colour, and nothing else. A ring drawn around the
    /// button was an ANNOTATION — a marker pointing at a button — where a cabinet's
    /// answer to a press is light. The engine's lamps read as light because they have
    /// no edge; this follows them, so both belong to the same picture.
    ///
    /// It spreads well past the button: that is what makes it read as a glow rather
    /// than as a coloured disc pasted over the artwork.
    /// </summary>
    private static Ellipse Glow(double cx, double cy, double radius)
    {
        var reach = radius * 2.6;
        var lamp = new Ellipse
        {
            Width = reach * 2,
            Height = reach * 2,
            Opacity = 0,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(lamp, cx - reach);
        Canvas.SetTop(lamp, cy - reach);
        return lamp;
    }

    /// <summary>The lamp's own light, in one colour. Stops follow the engine's two
    /// passes — a bright core inside a much wider, much fainter halo — and the core
    /// is pulled towards white the way a filament is hotter than the glass.</summary>
    private static System.Windows.Media.Brush GlowBrush(Color color)
    {
        Color Hot(double amount) => Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));

        return new RadialGradientBrush
        {
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            Center = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
            // Alphas stay in the engine's range — its own lamps top out around 200 — so
            // the light SITS ON the button instead of erasing it. At full opacity the cap
            // disappeared under a white disc, which reads as a hole in the panel rather
            // than as a button answering.
            GradientStops = new GradientStopCollection
            {
                new(Color.FromArgb(170, Hot(0.55).R, Hot(0.55).G, Hot(0.55).B), 0.0),
                new(Color.FromArgb(140, Hot(0.20).R, Hot(0.20).G, Hot(0.20).B), 0.20),
                new(Color.FromArgb(96, color.R, color.G, color.B), 0.36),
                new(Color.FromArgb(32, color.R, color.G, color.B), 0.64),
                new(Color.FromArgb(0, color.R, color.G, color.B), 1.0)
            }
        };
    }

    private void DrawStick(double cx, double cy)
    {
        var shaft = new Ellipse
        {
            Width = StickRadius * 2,
            Height = StickRadius * 2,
            Fill = new SolidColorBrush(Color.FromRgb(0x20, 0x23, 0x2b)),
            Stroke = new SolidColorBrush(ParseColor(_config.StickColor, Color.FromRgb(0x5b, 0x62, 0x70))),
            StrokeThickness = 3
        };
        Canvas.SetLeft(shaft, cx - StickRadius);
        Canvas.SetTop(shaft, cy - StickRadius);
        _canvas.Children.Add(shaft);

        var ball = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = new SolidColorBrush(ParseColor(_config.StickColor, Color.FromRgb(0x5b, 0x62, 0x70)))
        };
        Canvas.SetLeft(ball, cx - 9);
        Canvas.SetTop(ball, cy - 9);
        _canvas.Children.Add(ball);
    }

    private Lamp AddButton(double cx, double cy, double radius)
    {
        var body = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Stroke = new SolidColorBrush(Color.FromRgb(0x0d, 0x0f, 0x13)),
            StrokeThickness = 2
        };
        Canvas.SetLeft(body, cx - radius);
        Canvas.SetTop(body, cy - radius);

        // the light sits on top at zero opacity and is animated: the resting button keeps
        // its own colour, so a press ADDS light rather than replacing the drawing
        var lit = Glow(cx, cy, radius);

        TextBlock? label = null;
        if (_showLabels)
        {
            label = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xcf, 0xd3, 0xdc)),
                TextAlignment = TextAlignment.Center,
                Width = radius * 4,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Canvas.SetLeft(label, cx - radius * 2);
            Canvas.SetTop(label, cy + radius + 4);
            _canvas.Children.Add(label);
        }

        _canvas.Children.Add(body);
        _canvas.Children.Add(lit);
        return new Lamp(body, lit, label, Dispatcher);
    }

    private Lamp AddSystemButton(double x, double y, string name)
    {
        var body = new System.Windows.Shapes.Rectangle
        {
            Width = 62,
            Height = 20,
            RadiusX = 10,
            RadiusY = 10,
            Fill = new SolidColorBrush(Color.FromRgb(0x3a, 0x3f, 0x4b)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x0d, 0x0f, 0x13)),
            StrokeThickness = 2,
            Opacity = UnusedOpacity
        };
        Canvas.SetLeft(body, x);
        Canvas.SetTop(body, y);

        // same lamp as the game buttons, flattened to the pill's shape: a press must
        // read the same way wherever it lands on the panel
        var lit = new Ellipse
        {
            Width = 62 * 2.2,
            Height = 20 * 4.4,
            Opacity = 0,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(lit, x + 31 - lit.Width / 2);
        Canvas.SetTop(lit, y + 10 - lit.Height / 2);

        var caption = new TextBlock
        {
            Text = name,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xcf, 0xd3, 0xdc)),
            TextAlignment = TextAlignment.Center,
            Width = 62,
            Opacity = UnusedOpacity
        };
        Canvas.SetLeft(caption, x);
        Canvas.SetTop(caption, y + 3);

        _canvas.Children.Add(body);
        _canvas.Children.Add(lit);
        _canvas.Children.Add(caption);
        var lamp = new Lamp(body, lit, caption, Dispatcher);
        lamp.SetRestingOpacity(UnusedOpacity);
        return lamp;
    }

    /// <summary>Named dynpanel colours ("Red", "Blue") and hex alike; anything unknown
    /// stays the neutral plastic — a wrong colour would claim a function the game never
    /// declared.</summary>
    private static Color ParseColor(string color, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(color)) return fallback;
        var value = color.Trim();
        var named = value.ToLowerInvariant() switch
        {
            "red" => "#d64545", "blue" => "#3d6fd6", "green" => "#3fa650",
            "yellow" => "#e0b038", "white" => "#e9e9e9", "black" => "#1a1c22",
            "orange" => "#e08a38", "purple" => "#8a5cd6", "pink" => "#d65c9e",
            "cyan" => "#3fb6c4", "magenta" => "#c43fb6",
            _ => value.StartsWith('#') ? value : null
        };
        if (named == null) return fallback;
        try
        {
            return (Color)ColorConverter.ConvertFromString(named);
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>
    /// One place on the panel and its light. It owns the timing rather than the caller:
    /// the stream reports raw presses and releases, and the eye needs a minimum on-time
    /// and a fade to read them.
    /// </summary>
    private sealed class Lamp
    {
        private static readonly Color Neutral = Color.FromRgb(0x3a, 0x3f, 0x4b);

        private readonly Shape _body;
        private readonly Shape _lit;
        private readonly TextBlock? _label;
        private readonly DispatcherTimer _timer;
        private double _resting = UnusedOpacity;
        private bool _pressed;
        private DateTime _pressedAt;
        private bool _releasePending;

        /// <summary>False over the drawn artwork: the picture already IS the button, so
        /// this lamp contributes the light and nothing else.</summary>
        public bool PaintsBody { get; init; } = true;

        public Lamp(Shape body, Shape lit, TextBlock? label, Dispatcher dispatcher)
        {
            _body = body;
            _lit = lit;
            _label = label;
            _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timer.Tick += (_, _) => OnTick();
        }

        public void SetRestingOpacity(double opacity)
        {
            _resting = opacity;
            if (!_pressed) ApplyResting();
        }

        /// <summary>What the selected game makes of this place. A place the game ignores
        /// keeps the neutral plastic and fades — it exists, it just does nothing here.</summary>
        public void Describe(PanelBoardButton? button)
        {
            var used = button?.Used == true;
            var color = used ? ParseColor(button!.Color, Neutral) : Neutral;
            if (PaintsBody) _body.Fill = new SolidColorBrush(color);

            // A button the game does not use still answers when pressed — it just has no
            // colour of its own to answer WITH, so it lights white. Lighting it in the
            // neutral plastic grey would have shown almost nothing, and "I pressed and
            // nothing happened" is the one answer this panel must never give wrongly.
            _lit.Fill = GlowBrush(used ? color : Color.FromRgb(0xff, 0xf3, 0xd6));

            if (_label != null) _label.Text = used ? button!.Function : string.Empty;
            SetRestingOpacity(used ? 1.0 : UnusedOpacity);
        }

        public void SetPressed(bool pressed)
        {
            if (pressed)
            {
                _pressed = true;
                _releasePending = false;
                _pressedAt = DateTime.UtcNow;
                _lit.BeginAnimation(UIElement.OpacityProperty, null);
                _lit.Opacity = 1;
                _body.BeginAnimation(UIElement.OpacityProperty, null);
                _body.Opacity = 1;
                if (_label != null)
                {
                    _label.BeginAnimation(UIElement.OpacityProperty, null);
                    _label.Opacity = 1;
                }
                _timer.Start(); // also arms the stuck-press guard
                return;
            }

            if (!_pressed) return;

            // held long enough to have been seen: let go now. Otherwise the timer
            // finishes the minimum on-time and releases then.
            var elapsed = DateTime.UtcNow - _pressedAt;
            if (elapsed >= MinimumLit) Extinguish();
            else _releasePending = true;
        }

        private void OnTick()
        {
            if (!_pressed)
            {
                _timer.Stop();
                return;
            }

            var elapsed = DateTime.UtcNow - _pressedAt;
            if (_releasePending && elapsed >= MinimumLit)
            {
                Extinguish();
                return;
            }

            // a release that never came: the stream dropped while the button was held,
            // and without this the panel stays lit forever on a button nobody is touching
            if (elapsed >= StuckPressTimeout) Extinguish();
        }

        private void Extinguish()
        {
            _pressed = false;
            _releasePending = false;
            _timer.Stop();
            _lit.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, FadeOut) { FillBehavior = FillBehavior.HoldEnd });
            AnimateTo(_body, _resting);
            if (_label != null) AnimateTo(_label, _resting);
        }

        private void ApplyResting()
        {
            _body.BeginAnimation(UIElement.OpacityProperty, null);
            _body.Opacity = _resting;
            if (_label == null) return;
            _label.BeginAnimation(UIElement.OpacityProperty, null);
            _label.Opacity = _resting;
        }

        private static void AnimateTo(UIElement element, double opacity)
            => element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(opacity, FadeOut) { FillBehavior = FillBehavior.HoldEnd });
    }
}
