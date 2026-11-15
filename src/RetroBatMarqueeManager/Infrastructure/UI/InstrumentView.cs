using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using RetroBatMarqueeManager.Application.Services;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace RetroBatMarqueeManager.Infrastructure.UI;

/// <summary>
/// One instrument of the running game: a signal the game's semantics name (ammunition,
/// bombs, lives, shield, a shot level, the weapon in hand) drawn as a horizontal bar.
///
/// - a quantity that can be counted is a row of places, one per unit, lit while held;
/// - a quantity too large to count is one continuous bar with its number;
/// - a state with a name ("Vulcan", "Boosted") is that name;
/// - a spendable quantity on its floor replaces the bar with a blinking word - "RELOAD"
///   for ammunition, "EMPTY" otherwise, or the operator's own text.
///
/// It draws nothing at all for a game whose semantics do not carry the signal: an empty
/// frame would claim an instrument the game does not have.
///
/// Laid out at a fixed design size inside a Viewbox, like the control panel: the bar
/// keeps its proportions whatever rectangle the composition gives it.
/// </summary>
public sealed class InstrumentView : Viewbox
{
    private const double DesignWidth = 480;
    private const double DesignHeight = 72;
    private const double CaptionWidth = 120;
    private const double BarHeight = 34;

    private readonly Grid _root = new() { Width = DesignWidth, Height = DesignHeight };
    private readonly TextBlock _caption;
    private readonly StackPanel _places = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _bar = new() { VerticalAlignment = VerticalAlignment.Center, Height = BarHeight };
    private readonly System.Windows.Shapes.Rectangle _barFill;
    private readonly TextBlock _number;
    private readonly TextBlock _word;
    private readonly TextBlock _empty;

    private readonly string _wanted;
    private readonly string _emptyText;
    private readonly bool _showCaption;
    private readonly Color _color;
    private readonly bool _french;

    private SemanticSignal? _signal;
    private long _highest;
    private int _placesDrawn = -1;
    private bool _blinking;

    /// <summary>Which player this instrument belongs to. Two players, two components.</summary>
    public int Player { get; }

    /// <summary>False while the running game has nothing for this instrument: the host
    /// then hides the element instead of showing an empty frame.</summary>
    public bool HasContent => _signal != null;

    public InstrumentView(int player, string wanted, string emptyText, bool showCaption, string color)
    {
        Player = Math.Max(1, player);
        _wanted = wanted ?? string.Empty;
        _emptyText = emptyText ?? string.Empty;
        _showCaption = showCaption;
        _color = ParseColor(color, Color.FromRgb(0xFF, 0xC8, 0x3D));
        _french = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            .Equals("fr", StringComparison.OrdinalIgnoreCase);

        Stretch = Stretch.Uniform;
        Child = _root;
        IsHitTestVisible = false;

        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(showCaption ? CaptionWidth : 0) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _caption = Text(22, FontWeights.Bold, Brushes.White);
        _caption.HorizontalAlignment = HorizontalAlignment.Left;
        _caption.Visibility = showCaption ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(_caption, 0);
        _root.Children.Add(_caption);

        var zone = new Grid();
        Grid.SetColumn(zone, 1);
        _root.Children.Add(zone);

        zone.Children.Add(_places);

        _bar.Children.Add(new System.Windows.Shapes.Rectangle
        {
            RadiusX = 6, RadiusY = 6,
            Fill = new SolidColorBrush(Color.FromArgb(0x55, 0, 0, 0)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x88, _color.R, _color.G, _color.B)),
            StrokeThickness = 2
        });
        _barFill = new System.Windows.Shapes.Rectangle
        {
            RadiusX = 5, RadiusY = 5, Margin = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Fill = new SolidColorBrush(_color)
        };
        _bar.Children.Add(_barFill);
        _number = Text(20, FontWeights.Bold, Brushes.White);
        _number.HorizontalAlignment = HorizontalAlignment.Center;
        _bar.Children.Add(_number);
        zone.Children.Add(_bar);

        _word = Text(30, FontWeights.Bold, new SolidColorBrush(_color));
        _word.HorizontalAlignment = HorizontalAlignment.Left;
        zone.Children.Add(_word);

        _empty = Text(34, FontWeights.Black, new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4D)));
        _empty.HorizontalAlignment = HorizontalAlignment.Left;
        zone.Children.Add(_empty);

        ShowNothing();
    }

    /// <summary>
    /// The signals of the game that just started (or none: the game ended, or has no
    /// semantics). Chooses the one this component shows and draws what is known of it.
    /// </summary>
    public void SetGame(IReadOnlyList<SemanticSignal> signals, IReadOnlyList<SemanticReading> readings)
    {
        _signal = InstrumentPlan.Pick(signals, Player, _wanted);
        _highest = 0;
        _placesDrawn = -1;
        if (_signal is null)
        {
            ShowNothing();
            return;
        }

        _caption.Text = InstrumentPlan.Caption(_signal);
        var known = readings.FirstOrDefault(r => r.Signal.Id.Equals(_signal.Id, StringComparison.OrdinalIgnoreCase));
        if (known != null) Draw(known.Value, known.Text);
        // nothing read yet and no starting value declared: the caption alone says the
        // instrument exists, the bar comes with the first value
        else ShowOnly(null);
    }

    /// <summary>One reading moved. Ignored unless it is this component's signal.</summary>
    public void Apply(SemanticReading reading)
    {
        if (_signal is null || !reading.Signal.Id.Equals(_signal.Id, StringComparison.OrdinalIgnoreCase)) return;
        Draw(reading.Value, reading.Text);
    }

    private void Draw(long value, string text)
    {
        var signal = _signal!;
        _highest = Math.Max(_highest, value);

        if (InstrumentPlan.IsEmpty(signal, value))
        {
            _empty.Text = InstrumentPlan.EmptyText(signal, _emptyText, _french);
            ShowOnly(_empty);
            Blink(true);
            return;
        }
        Blink(false);

        // a named state is its name; a level with names shows the name, not a number
        if (!InstrumentPlan.Depletes(signal) && text.Length > 0)
        {
            _word.Text = text.ToUpperInvariant();
            ShowOnly(_word);
            return;
        }

        var places = InstrumentPlan.Places(signal, value, _highest);
        if (places == 0)
        {
            var width = Math.Max(0, (DesignWidth - (_showCaption ? CaptionWidth : 0) - 6) * InstrumentPlan.Fill(signal, value, _highest));
            _barFill.Width = width;
            _number.Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            ShowOnly(_bar);
            return;
        }

        if (places != _placesDrawn) BuildPlaces(places);
        var held = (int)Math.Clamp(value - (signal.Min ?? 0), 0, places);
        for (var i = 0; i < _places.Children.Count; i++)
            ((Shape)_places.Children[i]).Opacity = i < held ? 1.0 : 0.18;
        ShowOnly(_places);
    }

    /// <summary>One block per unit, sized so the row fills the zone whatever the count.</summary>
    private void BuildPlaces(int places)
    {
        _places.Children.Clear();
        _placesDrawn = places;
        var available = DesignWidth - (_showCaption ? CaptionWidth : 0);
        var gap = places > 12 ? 3.0 : 6.0;
        var width = Math.Clamp((available - gap * (places - 1)) / places, 4, 44);
        for (var i = 0; i < places; i++)
        {
            _places.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = width, Height = BarHeight, RadiusX = 4, RadiusY = 4,
                Margin = new Thickness(0, 0, i == places - 1 ? 0 : gap, 0),
                Fill = new SolidColorBrush(_color),
                Stroke = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)),
                StrokeThickness = 1
            });
        }
    }

    private void ShowNothing()
    {
        Blink(false);
        _caption.Text = string.Empty;
        ShowOnly(null);
    }

    private void ShowOnly(UIElement? shown)
    {
        foreach (var element in new UIElement[] { _places, _bar, _word, _empty })
            element.Visibility = ReferenceEquals(element, shown) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A slow blink, not a strobe: it has to be read, and a marquee flashing
    /// at full rate beside the screen is the last thing a player needs.</summary>
    private void Blink(bool on)
    {
        if (on == _blinking) return;
        _blinking = on;
        if (!on)
        {
            _empty.BeginAnimation(OpacityProperty, null);
            _empty.Opacity = 1;
            return;
        }
        _empty.BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromMilliseconds(450)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    private static TextBlock Text(double size, FontWeight weight, System.Windows.Media.Brush brush) => new()
    {
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = size,
        FontWeight = weight,
        Foreground = brush,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.9
        }
    };

    private static Color ParseColor(string? text, Color fallback)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) return fallback;
        var named = value.ToLowerInvariant() switch
        {
            "gold" => "#FFC83D", "red" => "#E24B4B", "blue" => "#4B8BE2", "green" => "#4BC96B",
            "white" => "#FFFFFF", "orange" => "#F08A30", "cyan" => "#3FC8D8", "pink" => "#E265A8",
            _ => value
        };
        try
        {
            return (Color)ColorConverter.ConvertFromString(named);
        }
        catch
        {
            return fallback;
        }
    }
}
