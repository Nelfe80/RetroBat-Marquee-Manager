using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Image = System.Windows.Controls.Image;
using Color = System.Windows.Media.Color;

namespace RetroBatMarqueeManager.Infrastructure.UI;

/// <summary>
/// An instruction card, and the ability to point AT something inside it.
///
/// A card is one drawing holding several entries — the weapons of Ghouls'n Ghosts, the
/// moves of a character — and APIExpose publishes where each entry sits, as fractions of
/// the drawing. When the game announces what the player just picked up, the entry that
/// names it gets framed: the card stops being a poster and starts answering the question
/// the player has right now.
///
/// The frame is computed against the DISPLAYED image, not the layer: a card is fitted
/// inside its zone and letterboxed, so the fractions must be applied to the picture as it
/// is actually drawn — otherwise the frame drifts off the entry as soon as the zone's
/// aspect differs from the card's.
/// </summary>
public sealed class InstructionCardView : Grid
{
    private static readonly Color Accent = Color.FromRgb(0xFF, 0xD2, 0x4A);

    private readonly Image _image;
    private readonly Canvas _overlay = new() { IsHitTestVisible = false };
    private readonly string _style;
    private (double X, double Y, double W, double H)? _panel;

    public InstructionCardView(Stretch stretch, string style)
    {
        _style = style.ToLowerInvariant();
        _image = new Image { Stretch = stretch };
        Children.Add(_image);
        Children.Add(_overlay);
        SizeChanged += (_, _) => Redraw();

        // the source is assigned asynchronously once the file is decoded: the frame can
        // only be placed when the picture's natural size is known
        System.ComponentModel.DependencyPropertyDescriptor
            .FromProperty(Image.SourceProperty, typeof(Image))
            .AddValueChanged(_image, (_, _) => Redraw());
    }

    /// <summary>The inner picture, which the host feeds like any other image layer.</summary>
    public Image Picture => _image;

    /// <summary>Frames one entry of the card, in fractions of the drawing. Null clears it —
    /// which is what browsing must do: a frame left from an earlier announcement would
    /// point at something the player no longer holds.</summary>
    public void SetPanel(double[]? rect)
    {
        _panel = rect is { Length: 4 } ? (rect[0], rect[1], rect[2], rect[3]) : null;
        Redraw();
    }

    private void Redraw()
    {
        _overlay.Children.Clear();
        if (_panel is not { } panel || _style == "none") return;
        if (_image.Source is not BitmapSource source) return;
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        double natural = source.PixelWidth, height = source.PixelHeight;
        if (natural <= 0 || height <= 0) return;

        // exactly what Stretch does with the picture, so the fractions land where the
        // player sees the entry
        var scale = _image.Stretch == Stretch.UniformToFill
            ? Math.Max(ActualWidth / natural, ActualHeight / height)
            : Math.Min(ActualWidth / natural, ActualHeight / height);
        double drawnWidth = natural * scale, drawnHeight = height * scale;
        double left = (ActualWidth - drawnWidth) / 2, top = (ActualHeight - drawnHeight) / 2;

        var x = left + panel.X * drawnWidth;
        var y = top + panel.Y * drawnHeight;
        var w = Math.Max(2, panel.W * drawnWidth);
        var h = Math.Max(2, panel.H * drawnHeight);

        if (_style == "spotlight")
        {
            // everything else steps back: the entry is the only lit part of the card
            var veil = new System.Windows.Shapes.Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(left, top, drawnWidth, drawnHeight)),
                    new RectangleGeometry(new Rect(x, y, w, h), 6, 6))
            };
            _overlay.Children.Add(veil);
        }

        var thickness = Math.Max(2, drawnHeight * 0.006);
        var frame = new System.Windows.Shapes.Rectangle
        {
            Width = w,
            Height = h,
            RadiusX = 6,
            RadiusY = 6,
            Stroke = new SolidColorBrush(Accent),
            StrokeThickness = thickness,
            // the same soft halo the lamps use: a hard outline reads as a UI element
            // pasted on the card, a glow reads as something being pointed at
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Accent,
                BlurRadius = thickness * 6,
                ShadowDepth = 0,
                Opacity = 0.85
            }
        };
        Canvas.SetLeft(frame, x);
        Canvas.SetTop(frame, y);
        _overlay.Children.Add(frame);
    }
}
