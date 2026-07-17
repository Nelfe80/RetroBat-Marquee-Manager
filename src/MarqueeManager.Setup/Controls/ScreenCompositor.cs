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
/// "Edit this screen's surfaces": every surface hosted by the screen is a
/// draggable/resizable rectangle at scale — this is where x,y live. Surfaces can
/// be ADDED (typed, with the category's default components) and REMOVED here;
/// marquee surfaces offer the standard APIExpose dimensions as presets
/// (1920×360, 1280×400, 920×360). Drag to move, bottom-right handle to resize,
/// wheel to zoom; magnetic guides snap to the screen and neighbor edges. On the
/// screen RetroBat lives on, adding a surface shows a masking warning (ES draws
/// fullscreen — unless the ES theme is designed for it, the surface covers it).
/// </summary>
public sealed class ScreenCompositor : Window
{
    private const double SnapPixels = 12;

    private static readonly (string Key, string Fr, string En)[] SurfaceTypes =
    {
        ("marquee", "Marquee", "Marquee"),
        ("topper", "Topper", "Topper"),
        ("iccard", "Instruction card", "Instruction card"),
        ("dmd-virtual", "DMD virtuel", "Virtual DMD"),
        ("lcd", "LCD (scores, défis)", "LCD (scores, challenges)"),
        ("custom", "Libre", "Custom")
    };

    private static readonly (string Label, int W, int H)[] MarqueePresets =
    {
        ("1920 × 360", 1920, 360),
        ("1280 × 400", 1280, 400),
        ("920 × 360", 920, 360)
    };

    private readonly int _screenIndex;
    private readonly ScreenInfo _screen;
    private readonly List<SurfaceModel> _all;
    private readonly bool _hostsGame;
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly TextBlock _readout;
    private readonly WrapPanel _selectionTools = new() { Margin = new Thickness(0, 4, 0, 4) };
    private readonly TextBlock _status = Ui.MutedLabel("", 11);
    private double _zoom;

    private SurfaceModel? _selected;
    private SurfaceModel? _dragging;
    private bool _resizing;
    private Point _dragStart;
    private (int X, int Y, int W, int H) _dragOrigin;

    public ScreenCompositor(int screenIndex, ScreenInfo screen, List<SurfaceModel> allSurfaces,
        SurfaceModel? focused = null, bool hostsGame = false)
    {
        _screenIndex = screenIndex;
        _screen = screen;
        _all = allSurfaces;
        _hostsGame = hostsGame;
        _selected = focused ?? Hosted().FirstOrDefault();

        Title = L.T($"Éditer les surfaces de l'écran {screenIndex} — {screen.Bounds.Width}×{screen.Bounds.Height}",
            $"Edit the surfaces of screen {screenIndex} — {screen.Bounds.Width}×{screen.Bounds.Height}");
        Width = 1020;
        Height = 740;
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        var root = new DockPanel { Margin = new Thickness(14) };

        // actions bar
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        _readout = Ui.Label(L.T("Glissez une surface ; poignée en bas à droite = taille ; molette = zoom.",
            "Drag a surface; bottom-right handle = size; wheel = zoom."), 12);
        bar.Children.Add(_readout);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(Ui.Button(L.T("Annuler", "Cancel"), (_, _) => DialogResult = false));
        buttons.Children.Add(Ui.Button(L.T("Valider les surfaces", "Apply surfaces"), (_, _) => DialogResult = true, primary: true));
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // add-surface row
        var addRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
        var typePicker = Ui.ComboBox(190);
        foreach (var (key, fr, en) in SurfaceTypes)
        {
            typePicker.Items.Add(new ComboBoxItem { Content = L.T(fr, en), Tag = key });
        }
        typePicker.SelectedIndex = 0;
        addRow.Children.Add(typePicker);
        addRow.Children.Add(Ui.Button(L.T("+ Ajouter une surface", "+ Add a surface"), (_, _) =>
        {
            if ((typePicker.SelectedItem as ComboBoxItem)?.Tag is string type) AddSurface(type);
        }));
        root.Children.Add(addRow);
        DockPanel.SetDock(addRow, Dock.Top);

        if (_hostsGame)
        {
            var warning = Ui.MutedLabel(L.T(
                "⚠ RetroBat/EmulationStation dessine en plein écran sur cet écran : une surface ajoutée MASQUERA cette zone d'ES, "
                + "sauf si votre thème ES est conçu pour lui laisser la place.",
                "⚠ RetroBat/EmulationStation draws fullscreen on this screen: an added surface WILL COVER that part of ES, "
                + "unless your ES theme is designed to leave room for it."));
            warning.Foreground = Ui.Accent;
            warning.TextWrapping = TextWrapping.Wrap;
            DockPanel.SetDock(warning, Dock.Top);
            root.Children.Add(warning);
        }

        DockPanel.SetDock(_selectionTools, Dock.Top);
        root.Children.Add(_selectionTools);
        DockPanel.SetDock(_status, Dock.Bottom);
        _status.TextWrapping = TextWrapping.Wrap;
        root.Children.Add(_status);

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
        Loaded += (_, _) =>
        {
            FitAndRender();
            RefreshSelectionTools();
        };
    }

    private IEnumerable<SurfaceModel> Hosted() => _all.Where(s => s.Screens.Contains(_screenIndex));

    // ================= add / remove =================

    private void AddSurface(string type)
    {
        var stem = type == "dmd-virtual" ? "dmd" : type;
        var id = stem;
        var n = 2;
        while (_all.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) id = $"{stem}-{n++}";

        var surface = new SurfaceModel { Id = id, Category = type };
        surface.Screens.Add(_screenIndex);
        surface.Streams.Add(type switch
        {
            "topper" => "topper",
            "iccard" => "iccard",
            "dmd-virtual" => "dmd",
            "lcd" => "lcd",
            _ => "marquee"
        });
        surface.Components.AddRange(SurfacesStore.DefaultComponents(type, type == "marquee"));

        // sensible starting rect: the marquee presets when they fit, else a half-screen zone
        if (type == "marquee")
        {
            var preset = MarqueePresets.FirstOrDefault(p => p.W <= _screen.Bounds.Width && p.H <= _screen.Bounds.Height);
            var (w, h) = preset.W > 0 ? (preset.W, preset.H) : (_screen.Bounds.Width, Math.Max(60, _screen.Bounds.Height / 5));
            surface.X = (_screen.Bounds.Width - w) / 2;
            surface.Y = 0;
            surface.Width = w;
            surface.Height = h;
        }
        else
        {
            surface.X = _screen.Bounds.Width / 4;
            surface.Y = _screen.Bounds.Height / 4;
            surface.Width = _screen.Bounds.Width / 2;
            surface.Height = _screen.Bounds.Height / 2;
        }

        _all.Add(surface);
        _selected = surface;
        _status.Text = L.T($"Surface « {id} » ajoutée avec ses composants par défaut.",
            $"Surface “{id}” added with its default components.");
        _status.Foreground = Ui.Ok;
        Render();
        RefreshSelectionTools();
    }

    private void RefreshSelectionTools()
    {
        _selectionTools.Children.Clear();
        if (_selected == null) return;
        var surface = _selected;

        var label = Ui.Label($"{surface.Id} ({surface.Category})", 12);
        label.FontWeight = FontWeights.Bold;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        _selectionTools.Children.Add(label);

        if (surface.Category.Equals("marquee", StringComparison.OrdinalIgnoreCase))
        {
            var presetLabel = Ui.MutedLabel(L.T("Dimensions standard :", "Standard dimensions:"));
            presetLabel.Margin = new Thickness(0, 0, 6, 0);
            presetLabel.VerticalAlignment = VerticalAlignment.Center;
            _selectionTools.Children.Add(presetLabel);
            foreach (var (text, w, h) in MarqueePresets)
            {
                _selectionTools.Children.Add(Ui.Button(text, (_, _) =>
                {
                    surface.Width = Math.Min(w, _screen.Bounds.Width);
                    surface.Height = Math.Min(h, _screen.Bounds.Height);
                    surface.X = Math.Max(0, (_screen.Bounds.Width - surface.Width.Value) / 2);
                    surface.Y ??= 0;
                    Render();
                }));
            }
        }

        // display state of the WHOLE surface (ES browsing / ingame / both):
        // e.g. no surface over ES while browsing, marquee band only ingame
        var whenLabel = Ui.MutedLabel(L.T("Visible en :", "Visible in:"));
        whenLabel.Margin = new Thickness(6, 0, 6, 0);
        whenLabel.VerticalAlignment = VerticalAlignment.Center;
        _selectionTools.Children.Add(whenLabel);
        var whenPicker = Ui.ComboBox(200);
        foreach (var (key, fr, en) in new[]
                 {
                     ("both", "Navigation ES + En jeu", "ES browsing + Ingame"),
                     ("navigation", "Navigation ES seulement", "ES browsing only"),
                     ("ingame", "En jeu seulement", "Ingame only")
                 })
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            whenPicker.Items.Add(item);
            if (key.Equals(surface.When, StringComparison.OrdinalIgnoreCase)) whenPicker.SelectedItem = item;
        }
        if (whenPicker.SelectedItem == null) whenPicker.SelectedIndex = 0;
        whenPicker.SelectionChanged += (_, _) =>
        {
            if ((whenPicker.SelectedItem as ComboBoxItem)?.Tag is string when)
            {
                surface.When = when;
                Render();
            }
        };
        _selectionTools.Children.Add(whenPicker);

        _selectionTools.Children.Add(Ui.Button(L.T("Plein écran", "Fullscreen"), (_, _) =>
        {
            surface.X = null;
            surface.Y = null;
            surface.Width = null;
            surface.Height = null;
            Render();
        }));
        _selectionTools.Children.Add(Ui.Button(L.T("Supprimer cette surface", "Delete this surface"), (_, _) =>
        {
            if (MessageBox.Show(
                    L.T($"Supprimer la surface « {surface.Id} » et ses composants ?", $"Delete surface “{surface.Id}” and its components?"),
                    Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _all.Remove(surface);
            _selected = Hosted().FirstOrDefault();
            Render();
            RefreshSelectionTools();
        }));
    }

    private void FitAndRender()
    {
        var availableW = Math.Max(200, ActualWidth - 60);
        var availableH = Math.Max(200, ActualHeight - 200);
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

        foreach (var surface in Hosted())
        {
            var (x, y, w, h) = RectOf(surface);
            var isSelected = ReferenceEquals(surface, _selected);
            var accent = isSelected ? Ui.Accent : Ui.Brush(Color.FromRgb(0x30, 0x60, 0xE8));

            var rect = new Border
            {
                Width = Math.Max(8, w * _zoom),
                Height = Math.Max(8, h * _zoom),
                Background = new SolidColorBrush(Color.FromArgb(0x30, 0x88, 0x88, 0xC0)),
                BorderBrush = accent,
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Tag = surface
            };
            Canvas.SetLeft(rect, x * _zoom);
            Canvas.SetTop(rect, y * _zoom);
            _canvas.Children.Add(rect);

            var whenBadge = surface.When.ToLowerInvariant() switch
            {
                "navigation" => L.T("\n[Navigation ES seulement]", "\n[ES browsing only]"),
                "ingame" => L.T("\n[En jeu seulement]", "\n[Ingame only]"),
                _ => ""
            };
            var label = new TextBlock
            {
                Text = $"{surface.Id}\n{w}×{h} @ {x},{y}"
                       + (surface.IsFullscreen ? L.T(" (plein écran)", " (fullscreen)") : "") + whenBadge,
                Foreground = Ui.Foreground,
                FontSize = 11,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
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

        _selected = surface;
        _dragging = surface;
        _resizing = hit is Rectangle { Cursor: not null };
        _dragStart = position;
        _dragOrigin = RectOf(surface);
        _canvas.CaptureMouse();
        RefreshSelectionTools();
        Render();
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
        var xTargets = EdgeTargets(horizontal: true);
        var yTargets = EdgeTargets(horizontal: false);
        return (SnapValue(x, xTargets, w), SnapValue(y, yTargets, h));
    }

    private (int W, int H) SnapSize(int x, int y, int w, int h)
    {
        // snap the moving right/bottom edge to the same guides
        var xTargets = EdgeTargets(horizontal: true);
        var yTargets = EdgeTargets(horizontal: false);
        var right = SnapEdge(x + w, xTargets);
        var bottom = SnapEdge(y + h, yTargets);
        return (Math.Max(16, right - x), Math.Max(16, bottom - y));
    }

    private List<int> EdgeTargets(bool horizontal)
    {
        var targets = new List<int> { 0, horizontal ? _screen.Bounds.Width : _screen.Bounds.Height };
        foreach (var neighbor in Hosted())
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
