using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Visual placement of a surface's COMPONENTS: the surface at scale (dark
/// viewport, real aspect ratio), each component a draggable/resizable rectangle
/// in fractions. Same gestures as the screen compositor: drag = move,
/// bottom-right handle = resize, wheel = zoom, magnetic guides on the surface
/// edges, centers and the neighbors' edges; position/size read out live.
/// </summary>
public sealed class SurfaceComponentEditor : Window
{
    private const double SnapFraction = 0.015;

    private static readonly Dictionary<string, Color> TypeColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["media.flux"] = Color.FromRgb(0x30, 0x60, 0xE8),
        ["media.logo"] = Color.FromRgb(0xFF, 0xB3, 0x00),
        ["media.fanart"] = Color.FromRgb(0x8A, 0x2B, 0xE2),
        ["media.image"] = Color.FromRgb(0x8A, 0x2B, 0xE2),
        ["media.video"] = Color.FromRgb(0xE8, 0x5C, 0x5C),
        ["shape.gradient"] = Color.FromRgb(0x66, 0x66, 0x78),
        ["external.web"] = Color.FromRgb(0xE8, 0x5C, 0x5C),
        ["iccard.static"] = Color.FromRgb(0x20, 0xE8, 0xE8),
        ["iccard.cycle"] = Color.FromRgb(0x20, 0xE8, 0xE8)
    };
    private static readonly Color OverlayColor = Color.FromRgb(0x4C, 0xC9, 0x6E);

    private readonly SurfaceModel _surface;
    private readonly double _aspect;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TextBlock _readout;
    private double _zoom = 1;

    private ComponentModel? _dragging;
    private bool _resizing;
    private Point _dragStart;
    private (double X, double Y, double W, double H) _origin;

    public SurfaceComponentEditor(SurfaceModel surface, double fallbackAspect = 4.0)
    {
        _surface = surface;
        _aspect = surface is { Width: > 0, Height: > 0 }
            ? (double)surface.Width.Value / surface.Height.Value
            : fallbackAspect;

        Title = L.T($"Composants de « {surface.Id} »", $"Components of “{surface.Id}”")
                + (surface is { Width: > 0, Height: > 0 } ? $" — {surface.Width}×{surface.Height}" : "");
        Width = 980;
        Height = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var root = new DockPanel { Margin = new Thickness(14) };
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _readout = Ui.Label(L.T(
            "Glissez un composant ; poignée en bas à droite = taille ; molette = zoom. L'ordre de la pile reste celui de la liste.",
            "Drag a component; bottom-right handle = size; wheel = zoom. Stack order stays the list order."), 12);
        bar.Children.Add(_readout);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button(L.T("Annuler", "Cancel"), (_, _) => DialogResult = false));
        buttons.Children.Add(Ui.Button(L.T("Valider le placement", "Apply placement"), (_, _) => DialogResult = true, primary: true));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        root.Children.Add(new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = _canvas
        });
        Content = root;

        _canvas.Background = Brushes.Transparent;
        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => EndDrag();
        _canvas.MouseLeave += (_, _) => EndDrag();
        _canvas.MouseWheel += (_, e) =>
        {
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.3, 3);
            Render();
            e.Handled = true;
        };

        SizeChanged += (_, _) => Render();
        Loaded += (_, _) => Render();
    }

    private (double W, double H) CanvasSize()
    {
        var availableW = Math.Max(200, ActualWidth - 60) * _zoom;
        var availableH = Math.Max(160, ActualHeight - 110) * _zoom;
        var w = Math.Min(availableW, availableH * _aspect);
        return (w, w / _aspect);
    }

    private void Render()
    {
        var (width, height) = CanvasSize();
        _canvas.Width = width;
        _canvas.Height = height;
        _canvas.Children.Clear();

        // surface frame
        _canvas.Children.Add(new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = Ui.Muted,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 3 },
            IsHitTestVisible = false
        });

        foreach (var component in _surface.Components)
        {
            var color = TypeColors.TryGetValue(component.Type, out var c) ? c : OverlayColor;
            var isSelected = ReferenceEquals(component, _dragging);
            var rect = new Border
            {
                Width = Math.Max(8, component.W * width),
                Height = Math.Max(8, component.H * height),
                Background = new SolidColorBrush(Color.FromArgb(0x28, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Tag = component
            };
            Canvas.SetLeft(rect, component.X * width);
            Canvas.SetTop(rect, component.Y * height);
            _canvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = component.Type,
                Foreground = Ui.Foreground,
                FontSize = 10,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0 }
            };
            Canvas.SetLeft(label, component.X * width + 5);
            Canvas.SetTop(label, component.Y * height + 3);
            _canvas.Children.Add(label);

            var handle = new Rectangle
            {
                Width = 11,
                Height = 11,
                Fill = new SolidColorBrush(color),
                Tag = component,
                Cursor = Cursors.SizeNWSE
            };
            Canvas.SetLeft(handle, (component.X + component.W) * width - 5.5);
            Canvas.SetTop(handle, (component.Y + component.H) * height - 5.5);
            _canvas.Children.Add(handle);
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        var hit = _canvas.Children.OfType<FrameworkElement>().Reverse()
            .FirstOrDefault(element => element.Tag is ComponentModel
                                       && position.X >= Canvas.GetLeft(element)
                                       && position.X <= Canvas.GetLeft(element) + element.Width
                                       && position.Y >= Canvas.GetTop(element)
                                       && position.Y <= Canvas.GetTop(element) + element.Height);
        if (hit?.Tag is not ComponentModel component) return;

        _dragging = component;
        _resizing = hit is Rectangle { Cursor: not null };
        _dragStart = position;
        _origin = (component.X, component.Y, component.W, component.H);
        _canvas.CaptureMouse();
        Render();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
        var (width, height) = CanvasSize();
        var position = e.GetPosition(_canvas);
        var dx = (position.X - _dragStart.X) / width;
        var dy = (position.Y - _dragStart.Y) / height;

        if (_resizing)
        {
            _dragging.W = Snap(Math.Clamp(_origin.W + dx, 0.03, 1.5), Edges(horizontal: true, exclude: _dragging), _origin.X);
            _dragging.H = Snap(Math.Clamp(_origin.H + dy, 0.03, 1.5), Edges(horizontal: false, exclude: _dragging), _origin.Y);
        }
        else
        {
            _dragging.X = SnapPosition(Math.Clamp(_origin.X + dx, -0.25, 1), _dragging.W, Edges(horizontal: true, exclude: _dragging));
            _dragging.Y = SnapPosition(Math.Clamp(_origin.Y + dy, -0.25, 1), _dragging.H, Edges(horizontal: false, exclude: _dragging));
        }

        _readout.Text = $"{_dragging.Type} : x={_dragging.X:0.###}  y={_dragging.Y:0.###}  w={_dragging.W:0.###}  h={_dragging.H:0.###}";
        Render();
    }

    private void EndDrag()
    {
        if (_dragging == null) return;
        _canvas.ReleaseMouseCapture();
        Render();
    }

    // ---- magnetic guides: surface edges, center, neighbors' edges ----

    private List<double> Edges(bool horizontal, ComponentModel exclude)
    {
        var edges = new List<double> { 0, 0.5, 1 };
        foreach (var neighbor in _surface.Components)
        {
            if (ReferenceEquals(neighbor, exclude)) continue;
            if (horizontal)
            {
                edges.Add(neighbor.X);
                edges.Add(neighbor.X + neighbor.W);
            }
            else
            {
                edges.Add(neighbor.Y);
                edges.Add(neighbor.Y + neighbor.H);
            }
        }
        return edges;
    }

    private static double SnapPosition(double value, double size, List<double> edges)
    {
        foreach (var edge in edges)
        {
            if (Math.Abs(value - edge) <= SnapFraction) return edge;
            if (Math.Abs(value + size - edge) <= SnapFraction) return edge - size;
            if (Math.Abs(value + size / 2 - edge) <= SnapFraction) return edge - size / 2;
        }
        return value;
    }

    private static double Snap(double size, List<double> edges, double origin)
    {
        foreach (var edge in edges)
        {
            if (Math.Abs(origin + size - edge) <= SnapFraction) return edge - origin;
        }
        return size;
    }
}
