using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Marquee composition editor: layers of game media (fanart, logo, boxes…)
/// arranged on a canvas locked to the real marquee resolution. Fractions-based
/// model (MarqueeProject), WYSIWYG at display scale, exported at full resolution
/// by <see cref="RenderPng"/>. Interactions: click = select, drag = move,
/// wheel = scale, Shift+wheel = rotate; fine tuning through the inspector row.
/// The plate stays dark in both themes, like every viewport.
/// </summary>
public sealed class MarqueeComposer : UserControl
{
    /// <summary>Canvas display width — the host passes the available width so the
    /// center column fills the window (default keeps the historical inline size).</summary>
    private readonly double DisplayWidth;

    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly string _mediaRoot;
    private readonly double _displayHeight;

    private readonly Canvas _canvas;
    private readonly List<LayerVisual> _layers = new();
    private MarqueeBackground _background = new();
    private LayerVisual? _selected;
    private Point _dragStart;
    private (double X, double Y) _dragOrigin;
    private bool _dragging;

    // inspector
    private readonly StackPanel _inspector;
    private readonly Slider _scale;
    private readonly Slider _rotation;
    private readonly Slider _opacity;
    private readonly TextBlock _selectionLabel = Ui.MutedLabel("");
    private bool _syncingInspector;

    public event Action? Changed;

    /// <summary>Raised when the selection or the layer stack changed — lets a
    /// host window drive an external layers panel + inspector (RetroCreator
    /// Designer layout) instead of the inline inspector row.</summary>
    public event Action? StackChanged;

    private sealed class LayerVisual
    {
        public required MarqueeLayer Model { get; init; }
        public required FrameworkElement Element { get; init; }
        public double AspectRatio { get; init; } = 1.0; // width / height
    }

    // ================= external panel API =================

    /// <summary>Layer models, BACK to FRONT (the canvas z-order).</summary>
    public IReadOnlyList<MarqueeLayer> LayerModels => _layers.Select(l => l.Model).ToList();

    public MarqueeLayer? SelectedLayer => _selected?.Model;

    /// <summary>Hides the inline inspector row when a host window provides its own.</summary>
    public bool InlineInspector
    {
        set => _externalPanel = !value;
    }

    private bool _externalPanel;

    public void SelectLayer(MarqueeLayer? layer)
    {
        Select(layer == null ? null : _layers.FirstOrDefault(l => ReferenceEquals(l.Model, layer)));
        Render();
    }

    public void ReorderLayer(MarqueeLayer layer, int direction)
    {
        var visual = _layers.FirstOrDefault(l => ReferenceEquals(l.Model, layer));
        if (visual == null) return;
        var index = _layers.IndexOf(visual);
        var target = index + direction;
        if (target < 0 || target >= _layers.Count) return;
        (_layers[index], _layers[target]) = (_layers[target], _layers[index]);
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke();
    }

    public void DeleteLayer(MarqueeLayer layer)
    {
        var visual = _layers.FirstOrDefault(l => ReferenceEquals(l.Model, layer));
        if (visual == null) return;
        if (_selected == visual) Select(null);
        _layers.Remove(visual);
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke();
    }

    /// <summary>Applies a change to a specific layer (external inspector).</summary>
    public void ApplyToLayer(MarqueeLayer layer, Action<MarqueeLayer> change)
    {
        change(layer);
        Render();
        Changed?.Invoke();
    }

    /// <summary>Drag & drop reorder: moves a layer to an index of the back→front list.</summary>
    public void MoveLayerTo(MarqueeLayer layer, int newIndex)
    {
        var visual = _layers.FirstOrDefault(l => ReferenceEquals(l.Model, layer));
        if (visual == null) return;
        _layers.Remove(visual);
        _layers.Insert(Math.Clamp(newIndex, 0, _layers.Count), visual);
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke();
    }

    public MarqueeComposer(int targetWidth, int targetHeight, string mediaRoot, double displayWidth = 640)
    {
        _targetWidth = Math.Max(64, targetWidth);
        _targetHeight = Math.Max(32, targetHeight);
        _mediaRoot = mediaRoot;
        DisplayWidth = Math.Clamp(displayWidth, 320, 2400);
        _displayHeight = DisplayWidth * _targetHeight / _targetWidth;

        _canvas = new Canvas
        {
            Width = DisplayWidth,
            Height = _displayHeight,
            Background = MakeCheckerboard(),
            ClipToBounds = true
        };
        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => EndDrag();
        _canvas.MouseLeave += (_, _) => EndDrag();
        _canvas.MouseWheel += Canvas_MouseWheel;

        var plate = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _canvas
        };

        // ---- inspector row ----
        _inspector = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var row1 = new WrapPanel();
        row1.Children.Add(_selectionLabel);
        _inspector.Children.Add(row1);

        var row2 = new WrapPanel();
        _scale = InspectorSlider(row2, L.T("Taille", "Size"), 0.05, 3.0, v => ApplySelected(l => l.Scale = v));
        _rotation = InspectorSlider(row2, L.T("Rotation", "Rotation"), -180, 180, v => ApplySelected(l => l.Rotation = v));
        _opacity = InspectorSlider(row2, L.T("Opacité", "Opacity"), 0.05, 1.0, v => ApplySelected(l => l.Opacity = v));
        _inspector.Children.Add(row2);

        var row3 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row3.Children.Add(Ui.Button(L.T("Miroir", "Mirror"), (_, _) => ApplySelected(l => l.FlipH = !l.FlipH)));
        row3.Children.Add(Ui.Button(L.T("Monter", "Raise"), (_, _) => Reorder(+1)));
        row3.Children.Add(Ui.Button(L.T("Descendre", "Lower"), (_, _) => Reorder(-1)));
        row3.Children.Add(Ui.Button(L.T("Supprimer le calque", "Delete layer"), (_, _) => DeleteSelected()));
        _inspector.Children.Add(row3);
        _inspector.Visibility = Visibility.Collapsed;

        var host = new StackPanel();
        host.Children.Add(Ui.MutedLabel(L.T(
            $"Surface réelle : {_targetWidth}×{_targetHeight} px — cliquer pour sélectionner, glisser pour déplacer, molette = taille, Maj+molette = rotation.",
            $"Real surface: {_targetWidth}×{_targetHeight} px — click to select, drag to move, wheel = size, Shift+wheel = rotate.")));
        host.Children.Add(plate);
        host.Children.Add(_inspector);
        Content = host;
    }

    // ================= model in/out =================

    public void LoadProject(MarqueeProject project)
    {
        _layers.Clear();
        _background = project.Background;
        foreach (var layer in project.Layers)
        {
            AddLayerVisual(layer);
        }
        Select(null);
        Render();
    }

    public MarqueeProject BuildProject(string system, string rom) => new()
    {
        System = system,
        Rom = rom,
        Width = _targetWidth,
        Height = _targetHeight,
        Background = _background,
        Layers = _layers.Select(l => l.Model).ToList()
    };

    public bool HasLayers => _layers.Count > 0;

    public void SetBackground(MarqueeBackground background)
    {
        _background = background;
        Render();
        Changed?.Invoke();
    }

    public MarqueeBackground BackgroundModel => _background;

    /// <summary>Adds a media layer centered. Logos land at 50 % of the surface
    /// WIDTH by default (user rule); other media fit the height.</summary>
    public void AddMediaLayer(string absolutePath, string assetKey)
    {
        var layer = new MarqueeLayer
        {
            Source = ToRelative(absolutePath),
            AssetKey = assetKey,
            Scale = 0.9
        };
        var visual = AddLayerVisual(layer);
        if (assetKey is "wheel" or "logo" && visual.AspectRatio > 0)
        {
            // Scale is height-relative: width = Scale × displayH × aspect = 0.5 × displayW
            layer.Scale = Math.Clamp(0.5 * DisplayWidth / (_displayHeight * visual.AspectRatio), 0.05, 3.0);
        }
        Select(visual);
        Render();
        Changed?.Invoke();
    }

    /// <summary>One-click APIExpose-style recipe: fanart as a real LOCKED cover
    /// layer (visible in the layers panel, selectable, not movable) + the logo at
    /// half the surface width. Starting point of every template.</summary>
    public void ApplyTemplatePreset(string? fanartPath, string? logoPath)
    {
        _layers.Clear();
        Select(null);
        if (fanartPath != null)
        {
            var fanart = new MarqueeLayer { Source = ToRelative(fanartPath), AssetKey = "fanart", Locked = true };
            var visual = AddLayerVisual(fanart);
            // cover: fills both dimensions (Scale is height-relative)
            fanart.Scale = visual.AspectRatio > 0
                ? Math.Clamp(Math.Max(1.0, DisplayWidth / (_displayHeight * visual.AspectRatio)), 0.05, 3.0)
                : 1.0;
        }
        if (logoPath != null)
        {
            AddMediaLayer(logoPath, "wheel");
        }
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke();
    }

    public void AddTextLayer(string text)
    {
        var layer = new MarqueeLayer { Source = "text", AssetKey = "text", Text = text, Scale = 1.0 };
        var visual = AddLayerVisual(layer);
        Select(visual);
        Render();
        Changed?.Invoke();
    }

    // ================= interactions =================

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        var hit = _layers.LastOrDefault(l => !l.Model.Hidden && Bounds(l).Contains(position));
        Select(hit);
        // locked layers stay selectable (inspector opens) but never drag
        if (hit is { Model.Locked: false })
        {
            _dragging = true;
            _dragStart = position;
            _dragOrigin = (hit.Model.X, hit.Model.Y);
            _canvas.CaptureMouse();
        }
        Render();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _selected == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(_canvas);
        _selected.Model.X = Math.Clamp(_dragOrigin.X + (position.X - _dragStart.X) / DisplayWidth, -0.5, 1.5);
        _selected.Model.Y = Math.Clamp(_dragOrigin.Y + (position.Y - _dragStart.Y) / _displayHeight, -0.5, 1.5);
        Render();
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            _canvas.ReleaseMouseCapture();
            Changed?.Invoke();
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_selected == null || _selected.Model.Locked)
        {
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _selected.Model.Rotation = Math.Clamp(_selected.Model.Rotation + (e.Delta > 0 ? 2 : -2), -180, 180);
        }
        else
        {
            _selected.Model.Scale = Math.Clamp(_selected.Model.Scale * (e.Delta > 0 ? 1.06 : 1 / 1.06), 0.05, 3.0);
        }
        SyncInspector();
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke(); // external inspector sliders follow the wheel
        e.Handled = true;
    }

    private void ApplySelected(Action<MarqueeLayer> change)
    {
        if (_selected == null || _syncingInspector)
        {
            return;
        }

        change(_selected.Model);
        Render();
        Changed?.Invoke();
    }

    private void Reorder(int direction)
    {
        if (_selected == null)
        {
            return;
        }

        var index = _layers.IndexOf(_selected);
        var target = index + direction;
        if (target < 0 || target >= _layers.Count)
        {
            return;
        }

        (_layers[index], _layers[target]) = (_layers[target], _layers[index]);
        Render();
        Changed?.Invoke();
        StackChanged?.Invoke();
    }

    private void DeleteSelected()
    {
        if (_selected == null)
        {
            return;
        }

        _layers.Remove(_selected);
        Select(null);
        Render();
        Changed?.Invoke();
    }

    private void Select(LayerVisual? layer)
    {
        _selected = layer;
        _inspector.Visibility = layer == null || _externalPanel ? Visibility.Collapsed : Visibility.Visible;
        _selectionLabel.Text = layer == null
            ? ""
            : L.T($"Calque sélectionné : {layer.Model.AssetKey}", $"Selected layer: {layer.Model.AssetKey}");
        SyncInspector();
        StackChanged?.Invoke();
    }

    private void SyncInspector()
    {
        if (_selected == null)
        {
            return;
        }

        _syncingInspector = true;
        _scale.Value = _selected.Model.Scale;
        _rotation.Value = _selected.Model.Rotation;
        _opacity.Value = _selected.Model.Opacity;
        _syncingInspector = false;
    }

    private Slider InspectorSlider(Panel host, string label, double min, double max, Action<double> onChange)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(text);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Width = 110,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, args) => onChange(args.NewValue);
        line.Children.Add(slider);
        host.Children.Add(line);
        return slider;
    }

    // ================= display rendering =================

    private LayerVisual AddLayerVisual(MarqueeLayer layer)
    {
        FrameworkElement element;
        var aspect = 1.0;
        if (layer.Source == "text")
        {
            element = new TextBlock
            {
                Text = layer.Text ?? "",
                Foreground = new SolidColorBrush(ParseColor(layer.TextColor)),
                FontWeight = layer.Bold ? FontWeights.Bold : FontWeights.Normal,
                FontFamily = new FontFamily("Segoe UI")
            };
        }
        else
        {
            var bitmap = TryLoadBitmap(ToAbsolute(layer.Source));
            if (bitmap != null)
            {
                aspect = bitmap.PixelHeight == 0 ? 1.0 : (double)bitmap.PixelWidth / bitmap.PixelHeight;
                element = new Image { Source = bitmap, Stretch = Stretch.Uniform };
            }
            else
            {
                element = new Border
                {
                    Background = Ui.Brush(Color.FromRgb(0x2E, 0x2E, 0x44)),
                    Child = Ui.MutedLabel(L.T("média introuvable", "missing media"))
                };
            }
        }

        var visual = new LayerVisual { Model = layer, Element = element, AspectRatio = aspect };
        _layers.Add(visual);
        return visual;
    }

    private Rect Bounds(LayerVisual layer)
    {
        var height = layer.Model.Scale * _displayHeight;
        var width = layer.Model.Source == "text"
            ? MeasureText(layer).Width
            : height * layer.AspectRatio;
        if (layer.Model.Source == "text")
        {
            height = MeasureText(layer).Height;
        }

        var centerX = layer.Model.X * DisplayWidth;
        var centerY = layer.Model.Y * _displayHeight;
        return new Rect(centerX - width / 2, centerY - height / 2, width, height);
    }

    private Size MeasureText(LayerVisual layer)
    {
        var text = (TextBlock)layer.Element;
        text.FontSize = Math.Max(4, layer.Model.FontSize * layer.Model.Scale * _displayHeight);
        text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return text.DesiredSize;
    }

    private void Render()
    {
        _canvas.Children.Clear();
        DrawBackground();

        foreach (var layer in _layers)
        {
            if (layer.Model.Hidden) continue; // kept in the project, not rendered
            var bounds = Bounds(layer);
            var element = layer.Element;
            element.Opacity = layer.Model.Opacity;
            element.Width = double.NaN;
            element.Height = double.NaN;
            if (element is Image)
            {
                element.Height = bounds.Height;
            }

            var transform = new TransformGroup();
            if (layer.Model.FlipH)
            {
                transform.Children.Add(new ScaleTransform(-1, 1));
            }
            if (Math.Abs(layer.Model.Rotation) > 0.01)
            {
                transform.Children.Add(new RotateTransform(layer.Model.Rotation));
            }
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);

            Canvas.SetLeft(element, bounds.X);
            Canvas.SetTop(element, bounds.Y);
            _canvas.Children.Add(element);

            if (layer == _selected)
            {
                var ring = new Rectangle
                {
                    Width = bounds.Width + 6,
                    Height = bounds.Height + 6,
                    Stroke = Ui.Accent,
                    StrokeThickness = 1.5,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    RenderTransform = element.RenderTransform,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(ring, bounds.X - 3);
                Canvas.SetTop(ring, bounds.Y - 3);
                _canvas.Children.Add(ring);
            }
        }
    }

    private void DrawBackground()
    {
        FrameworkElement? element = _background.Kind switch
        {
            "gradient" => new Rectangle
            {
                Fill = new LinearGradientBrush(ParseColor(_background.Color), ParseColor(_background.Color2), 20)
            },
            "media" when _background.Source is { Length: > 0 } => BuildBlurredBackground(),
            _ when _background.Color != "#000000" => new Rectangle
            {
                Fill = new SolidColorBrush(ParseColor(_background.Color))
            },
            _ => new Rectangle { Fill = Brushes.Black }
        };

        if (element != null)
        {
            // media backgrounds overflow the frame so the blur never samples
            // past the edges (dark border otherwise); the canvas clips the rest
            var pad = _background.Kind == "media" ? _background.Blur * 2 : 0;
            element.Width = DisplayWidth + pad * 2;
            element.Height = _displayHeight + pad * 2;
            element.IsHitTestVisible = false;
            Canvas.SetLeft(element, -pad);
            Canvas.SetTop(element, -pad);
            _canvas.Children.Add(element);
        }
    }

    private FrameworkElement? BuildBlurredBackground()
    {
        var bitmap = TryLoadBitmap(ToAbsolute(_background.Source!));
        if (bitmap == null)
        {
            return new Rectangle { Fill = Brushes.Black };
        }

        var image = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
        if (_background.Blur > 0)
        {
            image.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = _background.Blur };
        }
        return image;
    }

    // ================= full-resolution export =================

    /// <summary>Renders the composition at the real marquee resolution to a PNG.</summary>
    public void RenderPng(string path)
    {
        var scale = _targetWidth / DisplayWidth;
        var selected = _selected;
        Select(null);
        Render();

        // render the display canvas scaled up to the real surface: WYSIWYG by construction
        _canvas.LayoutTransform = new ScaleTransform(scale, scale);
        _canvas.Measure(new Size(_targetWidth, _targetHeight));
        _canvas.Arrange(new Rect(0, 0, _targetWidth, _targetHeight));
        _canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(_targetWidth, _targetHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_canvas);

        _canvas.LayoutTransform = Transform.Identity;
        _canvas.Measure(new Size(DisplayWidth, _displayHeight));
        _canvas.Arrange(new Rect(0, 0, DisplayWidth, _displayHeight));
        _canvas.UpdateLayout();
        Select(selected);
        Render();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    // ================= helpers =================

    private string ToRelative(string absolute)
    {
        try
        {
            var full = Path.GetFullPath(absolute);
            var root = Path.GetFullPath(_mediaRoot);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? full[root.Length..].TrimStart('\\', '/')
                : full;
        }
        catch
        {
            return absolute;
        }
    }

    private string ToAbsolute(string source)
        => Path.IsPathRooted(source) ? source : Path.Combine(_mediaRoot, source);

    private static BitmapImage? TryLoadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Colors.White;
        }
    }

    private static DrawingBrush MakeCheckerboard()
    {
        var dark = new SolidColorBrush(Color.FromRgb(0x12, 0x12, 0x1B));
        var darker = new SolidColorBrush(Color.FromRgb(0x0D, 0x0D, 0x14));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(darker, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }
}
