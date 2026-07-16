using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Visual compositing editor for one screen: every surface hosted by the screen
/// is a draggable/resizable rectangle at scale. This is where x,y live — the
/// surfaces view only knows width × height. Same gestures as the marquee
/// composer: drag to move, bottom-right handle to resize, wheel to zoom;
/// magnetic guides snap to the screen edges and to the neighbors' edges, and
/// the position/size is displayed while dragging.
/// </summary>
public sealed class ScreenCompositor : Window
{
    private const double SnapPixels = 12;

    private readonly ScreenInfo _screen;
    private readonly List<SurfaceModel> _surfaces;
    private readonly SurfaceModel _focused;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TextBlock _readout;
    private double _zoom;

    private SurfaceModel? _dragging;
    private bool _resizing;
    private Point _dragStart;
    private (int X, int Y, int W, int H) _dragOrigin;

    public ScreenCompositor(int screenIndex, ScreenInfo screen, List<SurfaceModel> surfaces, SurfaceModel focused)
    {
        _screen = screen;
        _surfaces = surfaces;
        _focused = focused;

        Title = L.T($"Composer l'écran {screenIndex} — {screen.Bounds.Width}×{screen.Bounds.Height}",
            $"Compose screen {screenIndex} — {screen.Bounds.Width}×{screen.Bounds.Height}");
        Width = 980;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var root = new DockPanel { Margin = new Thickness(14) };

        // actions bar
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _readout = Ui.Label(L.T("Glissez une surface ; poignée en bas à droite = taille ; molette = zoom.",
            "Drag a surface; bottom-right handle = size; wheel = zoom."), 12);
        bar.Children.Add(_readout);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button(L.T("Annuler", "Cancel"), (_, _) => DialogResult = false));
        buttons.Children.Add(Ui.Button(L.T("Valider les positions", "Apply positions"), (_, _) => DialogResult = true, primary: true));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // scaled viewport
        var plate = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = _canvas
        };
        root.Children.Add(plate);
        Content = root;

        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => EndDrag();
        _canvas.MouseLeave += (_, _) => EndDrag();
        _canvas.MouseWheel += Canvas_MouseWheel;
        _canvas.Background = Brushes.Transparent;

        SizeChanged += (_, _) => FitAndRender();
        Loaded += (_, _) => FitAndRender();
    }

    private void FitAndRender()
    {
        var availableW = Math.Max(200, ActualWidth - 60);
        var availableH = Math.Max(200, ActualHeight - 120);
        _zoom = Math.Min(availableW / _screen.Bounds.Width, availableH / _screen.Bounds.Height);
        Render();
    }

    // ================= rendering =================

    private (int X, int Y, int W, int H) RectOf(SurfaceModel surface) => surface.IsFullscreen
        ? (0, 0, _screen.Bounds.Width, _screen.Bounds.Height)
        : (surface.X ?? 0, surface.Y ?? 0, surface.Width ?? _screen.Bounds.Width, surface.Height ?? _screen.Bounds.Height);

    private void Render()
    {
        _canvas.Children.Clear();
        _canvas.Width = _screen.Bounds.Width * _zoom;
        _canvas.Height = _screen.Bounds.Height * _zoom;

        // screen frame
        _canvas.Children.Add(new Rectangle
        {
            Width = _canvas.Width,
            Height = _canvas.Height,
            Stroke = Ui.Muted,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 3 },
            IsHitTestVisible = false
        });

        foreach (var surface in _surfaces)
        {
            var (x, y, w, h) = RectOf(surface);
            var isFocused = ReferenceEquals(surface, _focused);
            var accent = isFocused ? Ui.Accent : Ui.Brush(Color.FromRgb(0x30, 0x60, 0xE8));

            var rect = new Border
            {
                Width = Math.Max(8, w * _zoom),
                Height = Math.Max(8, h * _zoom),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0xC0)),
                BorderBrush = accent,
                BorderThickness = new Thickness(isFocused ? 2 : 1),
                Tag = surface
            };
            Canvas.SetLeft(rect, x * _zoom);
            Canvas.SetTop(rect, y * _zoom);
            _canvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = $"{surface.Id}\n{w}×{h} @ {x},{y}" + (surface.IsFullscreen ? L.T(" (plein écran)", " (fullscreen)") : ""),
                Foreground = Ui.Foreground,
                FontSize = 11,
                FontWeight = isFocused ? FontWeights.Bold : FontWeights.Normal,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0 }
            };
            Canvas.SetLeft(label, x * _zoom + 6);
            Canvas.SetTop(label, y * _zoom + 4);
            _canvas.Children.Add(label);

            // resize handle (bottom-right)
            var handle = new Rectangle
            {
                Width = 12,
                Height = 12,
                Fill = accent,
                Tag = surface,
                Cursor = Cursors.SizeNWSE
            };
            Canvas.SetLeft(handle, (x + w) * _zoom - 6);
            Canvas.SetTop(handle, (y + h) * _zoom - 6);
            _canvas.Children.Add(handle);
        }
    }

    // ================= interactions =================

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        // handles first (they sit on top)
        var hit = _canvas.Children.OfType<FrameworkElement>().Reverse()
            .FirstOrDefault(element => element.Tag is SurfaceModel
                                       && position.X >= Canvas.GetLeft(element)
                                       && position.X <= Canvas.GetLeft(element) + element.Width
                                       && position.Y >= Canvas.GetTop(element)
                                       && position.Y <= Canvas.GetTop(element) + element.Height);
        if (hit?.Tag is not SurfaceModel surface) return;

        _dragging = surface;
        _resizing = hit is Rectangle { Cursor: not null };
        _dragStart = position;
        _dragOrigin = RectOf(surface);
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(_canvas);
        var dx = (int)Math.Round((position.X - _dragStart.X) / _zoom);
        var dy = (int)Math.Round((position.Y - _dragStart.Y) / _zoom);

        if (_resizing)
        {
            var w = Math.Clamp(_dragOrigin.W + dx, 16, _screen.Bounds.Width);
            var h = Math.Clamp(_dragOrigin.H + dy, 16, _screen.Bounds.Height);
            (w, h) = SnapSize(_dragOrigin.X, _dragOrigin.Y, w, h);
            _dragging.X = _dragOrigin.X;
            _dragging.Y = _dragOrigin.Y;
            _dragging.Width = w;
            _dragging.Height = h;
        }
        else
        {
            var x = Math.Clamp(_dragOrigin.X + dx, 0, Math.Max(0, _screen.Bounds.Width - _dragOrigin.W));
            var y = Math.Clamp(_dragOrigin.Y + dy, 0, Math.Max(0, _screen.Bounds.Height - _dragOrigin.H));
            (x, y) = SnapPosition(x, y, _dragOrigin.W, _dragOrigin.H);
            _dragging.X = x;
            _dragging.Y = y;
            _dragging.Width = _dragOrigin.W;
            _dragging.Height = _dragOrigin.H;
        }

        var (rx, ry, rw, rh) = RectOf(_dragging);
        _readout.Text = $"{_dragging.Id} : x={rx}, y={ry}, {rw}×{rh}";
        Render();
    }

    private void EndDrag()
    {
        if (_dragging != null)
        {
            _dragging = null;
            _canvas.ReleaseMouseCapture();
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), 0.05, 4);
        Render();
        e.Handled = true;
    }

    // ================= magnetic guides =================

    /// <summary>Snaps x/y to the screen edges and every neighbor's edges.</summary>
    private (int X, int Y) SnapPosition(int x, int y, int w, int h)
    {
        var xTargets = EdgeTargets(horizontal: true, w);
        var yTargets = EdgeTargets(horizontal: false, h);
        return (SnapValue(x, xTargets, w), SnapValue(y, yTargets, h));
    }

    private (int W, int H) SnapSize(int x, int y, int w, int h)
    {
        // snap the moving right/bottom edge to the same guides
        var xTargets = EdgeTargets(horizontal: true, 0);
        var yTargets = EdgeTargets(horizontal: false, 0);
        var right = SnapEdge(x + w, xTargets);
        var bottom = SnapEdge(y + h, yTargets);
        return (Math.Max(16, right - x), Math.Max(16, bottom - y));
    }

    private List<int> EdgeTargets(bool horizontal, int _)
    {
        var targets = new List<int> { 0, horizontal ? _screen.Bounds.Width : _screen.Bounds.Height };
        foreach (var neighbor in _surfaces)
        {
            if (ReferenceEquals(neighbor, _dragging)) continue;
            var (x, y, w, h) = RectOf(neighbor);
            if (horizontal)
            {
                targets.Add(x);
                targets.Add(x + w);
            }
            else
            {
                targets.Add(y);
                targets.Add(y + h);
            }
        }
        return targets;
    }

    /// <summary>Snaps the leading edge OR the trailing edge to a guide.</summary>
    private int SnapValue(int value, List<int> targets, int size)
    {
        var threshold = SnapPixels / _zoom;
        foreach (var target in targets)
        {
            if (Math.Abs(value - target) <= threshold) return target;
            if (Math.Abs(value + size - target) <= threshold) return target - size;
        }
        return value;
    }

    private int SnapEdge(int edge, List<int> targets)
    {
        var threshold = SnapPixels / _zoom;
        foreach (var target in targets)
        {
            if (Math.Abs(edge - target) <= threshold) return target;
        }
        return edge;
    }
}
