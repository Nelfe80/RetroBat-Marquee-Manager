using System.IO;
using System.Text.Json;
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
/// Composition editor, Photoshop logic (patterns of RetroCreator's Designer):
/// preset palette on the left (business presets, not bare primitives — click to
/// place intelligently), canvas at scale in the middle (drag/resize/snap), layers
/// (eye/lock/reorder) and a 3-group shared-state inspector on the right, display
/// STATE tabs at the top (Navigation ES | Ingame | Les deux), snapshot undo/redo.
/// Coordinates stay FRACTIONS — the composition survives any surface size.
/// </summary>
public sealed class CompositionEditor : Window
{
    private sealed record Preset(string Group, string Label, Func<List<ComponentModel>> Make);

    private readonly string _pluginRoot;
    private readonly SurfaceModel _surface;
    private readonly double _aspect;

    private readonly Canvas _canvas = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly StackPanel _layersPanel = new();
    private readonly StackPanel _inspector = new();
    private readonly TextBlock _readout;
    private string _state; // navigation | ingame | both (edit filter + default When)
    private ComponentModel? _selected;
    private ComponentModel? _dragging;
    private bool _resizing;
    private Point _dragStart;
    private (double X, double Y, double W, double H) _origin;

    // undo/redo by full snapshot (RetroCreator pattern — simple and reliable)
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    // example-game media so the canvas previews REAL content
    private readonly Dictionary<string, string> _exampleMedia = new(StringComparer.OrdinalIgnoreCase);
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
        ["iccard.cycle"] = Color.FromRgb(0x20, 0xE8, 0xE8),
        ["effects.engine"] = Color.FromRgb(0x39, 0xD3, 0x53)
    };


    public CompositionEditor(string pluginRoot, SurfaceModel surface, double aspect, string initialState = "navigation")
    {
        _pluginRoot = pluginRoot;
        _surface = surface;
        _aspect = aspect;
        _state = initialState is "ingame" or "navigation" ? initialState : "navigation";

        Title = L.T($"Création graphique — surface {surface.Id}", $"Graphic creation — surface {surface.Id}");
        Width = 1240;
        Height = 760;
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;
        LoadExampleMedia();
        SnapshotHistory();

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(215) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(285) });

        // ===== header: breadcrumb + state tabs + undo/redo + save =====
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _readout = Ui.MutedLabel(L.T("Cliquez un préréglage pour le poser ; glissez, poignée = taille.",
            "Click a preset to place it; drag, handle = size."), 11);
        var crumbs = Ui.Label(L.T($"Mon setup › {surface.Id} › ", $"My setup › {surface.Id} › "), 13);
        crumbs.FontWeight = FontWeights.Bold;
        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal };
        headerLeft.Children.Add(crumbs);
        foreach (var (key, fr, en) in new[]
                 {
                     ("navigation", "Navigation ES", "ES browsing"),
                     ("ingame", "En jeu", "Ingame")
                 })
        {
            var tab = Ui.Button(L.T(fr, en), (_, _) =>
            {
                _state = key;
                RenderAll();
            }, primary: key == _state);
            tab.Margin = new Thickness(4, 0, 0, 0);
            _stateTabs.Add((key, tab));
            headerLeft.Children.Add(tab);
        }
        header.Children.Add(headerLeft);

        var headerRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        headerRight.Children.Add(Ui.Button("↶", (_, _) => Undo()));
        headerRight.Children.Add(Ui.Button("↷", (_, _) => Redo()));
        headerRight.Children.Add(Ui.Button(L.T("Annuler", "Cancel"), (_, _) => DialogResult = false));
        headerRight.Children.Add(Ui.Button(L.T("Valider la création graphique", "Apply the graphic creation"), (_, _) => DialogResult = true, primary: true));
        DockPanel.SetDock(headerRight, Dock.Right);
        header.Children.Add(headerRight);
        Grid.SetColumnSpan(header, 3);
        root.Children.Add(header);

        // ===== left: preset palette =====
        var palette = BuildPalette();
        Grid.SetRow(palette, 1);
        root.Children.Add(palette);

        // ===== center: canvas =====
        var stage = new DockPanel { Margin = new Thickness(10, 0, 10, 0) };
        DockPanel.SetDock(_readout, Dock.Bottom);
        stage.Children.Add(_readout);
        stage.Children.Add(new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = _canvas
        });
        Grid.SetRow(stage, 1);
        Grid.SetColumn(stage, 1);
        root.Children.Add(stage);

        _canvas.MouseLeftButtonDown += Canvas_MouseDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => EndDrag();
        _canvas.MouseLeave += (_, _) => EndDrag();

        // ===== right: layers + inspector =====
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62, GridUnitType.Star) });
        var layersScroll = new ScrollViewer { Content = _layersPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var layersBox = new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
            Child = layersScroll
        };
        right.Children.Add(layersBox);
        var inspectorScroll = new ScrollViewer { Content = _inspector, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var inspectorBox = new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Child = inspectorScroll
        };
        Grid.SetRow(inspectorBox, 1);
        right.Children.Add(inspectorBox);
        Grid.SetRow(right, 1);
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        Content = root;
        PreviewKeyDown += OnKeyDown;
        SizeChanged += (_, _) => RenderCanvas();
        Loaded += (_, _) => RenderAll();
    }

    // ================= palette =================

    /// <summary>
    /// My setup places LIVE elements — things fed by the streams. Image composing
    /// belongs to My systems / My games, where a layout is built for a system or a game
    /// and resolved with that entry's media. Offering both here meant the same picture
    /// could be built in two places, from two screens, with no way to tell which won.
    /// </summary>
    private List<Preset> Presets()
    {
        ComponentModel C(string type, double x = 0, double y = 0, double w = 1, double h = 1,
            params (string K, string V)[] options)
        {
            var component = new ComponentModel { Type = type, X = x, Y = y, W = w, H = h, When = _state };
            foreach (var (k, v) in options) component.Options[k] = v;
            return component;
        }

        return new List<Preset>
        {
            new("📊 Live", "Hiscores", () => new() { C("overlay.hiscore", 0.7, 0.05, 0.28, 0.6) }),
            new("📊 Live", L.T("Score live", "Live score"), () => new() { C("overlay.live.score", 0.02, 0.7, 0.3, 0.28) }),
            new("📊 Live", L.T("Timer live", "Live timer"), () => new() { C("overlay.live.timer", 0.68, 0.7, 0.3, 0.28) }),
            // the game video is a LIVE element — it is fed by the stream and cannot be
            // baked into a composition, which is a still image by construction
            new("📊 Live", L.T("Vidéo du jeu", "Game video"),
                () => new() { C("media.video", 0, 0, 1, 1, ("sources", "local")) }),
            new("📊 Live", L.T("Web (Twitch/YouTube)", "Web (Twitch/YouTube)"),
                () => new() { C("external.web", 0, 0, 1, 1, ("url", "")) }),
            new("📊 Live", L.T("Chat Twitch", "Twitch chat"),
                () => new() { C("external.web", 0.7, 0, 0.3, 1, ("url", "https://www.twitch.tv/embed/MA_CHAINE/chat?parent=twitch.tv&darkpopout")) }),

            // The cabinet's own panel: what its buttons do in the selected game, and
            // which one is being pressed right now.
            new("📊 Live", L.T("Panneau de contrôle", "Control panel"),
                () => new() { C("panel.controls", 0.25, 0.5, 0.5, 0.45, ("player", "1")) }),

            new("🏆 RetroAchievements", L.T("Badges", "Badges"), () => new() { C("overlay.ra.badges", 0, 0.85, 1, 0.15) }),
            new("🏆 RetroAchievements", L.T("Infos RA", "RA info"), () => new() { C("overlay.ra.info", 0, 0.7, 1, 0.3) }),
            new("🏆 RetroAchievements", "Speedrun", () => new() { C("overlay.ra.speedrun") }),
        };
    }

    private FrameworkElement BuildPalette()
    {
        var host = new StackPanel();
        host.Children.Add(Ui.SectionHeader(L.T("Éléments", "Elements")));
        var first = true;
        foreach (var group in Presets().GroupBy(p => p.Group))
        {
            var body = new StackPanel();
            foreach (var preset in group)
            {
                var button = Ui.Button(preset.Label, (_, _) => AddPreset(preset));
                button.Margin = new Thickness(0, 2, 0, 2);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                body.Children.Add(button);
            }
            host.Children.Add(new Expander
            {
                Header = new TextBlock { Text = group.Key, Foreground = Ui.Foreground, FontWeight = FontWeights.SemiBold },
                Content = body,
                IsExpanded = first,
                Margin = new Thickness(0, 2, 0, 2)
            });
            first = false;
        }
        return new ScrollViewer { Content = host, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private void AddPreset(Preset preset)
    {
        SnapshotHistory();
        var made = preset.Make();
        // orientation-aware fanart: fill width when landscape, height when portrait
        foreach (var component in made.Where(c => c.Type == "media.fanart"))
        {
            component.X = 0;
            component.Y = 0;
            component.W = 1;
            component.H = 1;
            component.Options["stretch"] = "fill";
        }
        foreach (var component in made)
        {
            component.When = _state;
            _surface.Components.Add(component);
        }
        _selected = made.LastOrDefault();
        RenderAll();
    }

    // ================= canvas =================

    private IEnumerable<ComponentModel> EditableComponents()
        => _surface.Components.Where(c => c.When.Equals("both", StringComparison.OrdinalIgnoreCase)
            || c.When.Equals(_state, StringComparison.OrdinalIgnoreCase));

    private (double W, double H) CanvasSize()
    {
        var availableW = Math.Max(300, ActualWidth - 560);
        var availableH = Math.Max(200, ActualHeight - 160);
        var w = Math.Min(availableW, availableH * _aspect);
        return (w, w / _aspect);
    }

    private void RenderAll()
    {
        RenderCanvas();
        RenderLayers();
        RenderInspector();
        RefreshStateTabs();
    }

    private readonly List<(string Key, Button Tab)> _stateTabs = new();

    private void RefreshStateTabs()
    {
        // the active tab wears the accent style, exactly like a primary button
        var accent = System.Windows.Application.Current?.TryFindResource("AccentButton") as Style;
        foreach (var (key, tab) in _stateTabs)
        {
            var isActive = key == _state;
            if (isActive && accent != null)
            {
                tab.Style = accent;
            }
            else
            {
                tab.ClearValue(StyleProperty); // back to the implicit button style
                tab.FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal;
            }
        }
    }

    private void RenderCanvas()
    {
        var (width, height) = CanvasSize();
        _canvas.Width = width;
        _canvas.Height = height;
        _canvas.Children.Clear();

        _canvas.Children.Add(new Rectangle
        {
            Width = width, Height = height,
            Stroke = Ui.Muted, StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 2, 3 },
            IsHitTestVisible = false
        });

        foreach (var component in EditableComponents())
        {
            var color = TypeColors.TryGetValue(component.Type, out var c) ? c : Color.FromRgb(0x4C, 0xC9, 0x6E);
            var isSelected = ReferenceEquals(component, _selected);
            var rect = new Border
            {
                Width = Math.Max(8, component.W * width),
                Height = Math.Max(8, component.H * height),
                Background = new SolidColorBrush(Color.FromArgb(0x26, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                Opacity = component.Visible ? 1.0 : 0.35,
                Tag = component
            };

            // live-ish preview: real example-game media inside media components
            var kind = component.Type switch
            {
                "media.fanart" => "fanart",
                "media.logo" => "logo",
                "media.image" => component.Options.TryGetValue("kind", out var k) ? k : "screenmarquee",
                // the background shows the media of THIS surface's stream, not the
                // marquee's: a topper previewed with a marquee is a lie, and "why does
                // this show up when I asked for nothing?" starts exactly there
                "media.flux" => _surface.Category.ToLowerInvariant() switch
                {
                    "topper" => "topper",
                    "iccard" => "iccard",
                    "dmd-virtual" or "dmd" => "dmd",
                    _ => "marquee"
                },
                _ => null
            };
            if (kind != null && _exampleMedia.TryGetValue(kind, out var mediaPath))
            {
                rect.Child = new Image
                {
                    Source = LoadThumb(mediaPath),
                    // Never distort a media: "fill" = UniformToFill (keeps aspect, crops).
                    Stretch = component.Options.TryGetValue("stretch", out var s) && s == "fill" ? Stretch.UniformToFill : Stretch.Uniform,
                    Opacity = 0.9,
                    IsHitTestVisible = false
                };
            }

            Canvas.SetLeft(rect, component.X * width);
            Canvas.SetTop(rect, component.Y * height);
            _canvas.Children.Add(rect);

            var label = new TextBlock
            {
                Text = LayerName(component) + (component.When == "both" ? "" : $" · {StateBadge(component.When)}"),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x00, 0x00)),
                Padding = new Thickness(3, 1, 3, 1),
                FontSize = 12,
                IsHitTestVisible = false
            };
            TextOptions.SetTextFormattingMode(label, TextFormattingMode.Display);
            Canvas.SetLeft(label, component.X * width + 5);
            Canvas.SetTop(label, component.Y * height + 3);
            _canvas.Children.Add(label);

            if (!component.Locked)
            {
                var handle = new Rectangle
                {
                    Width = 11, Height = 11,
                    Fill = new SolidColorBrush(color),
                    Tag = component,
                    Cursor = Cursors.SizeNWSE
                };
                Canvas.SetLeft(handle, (component.X + component.W) * width - 5.5);
                Canvas.SetTop(handle, (component.Y + component.H) * height - 5.5);
                _canvas.Children.Add(handle);
            }
        }
    }

    private string StateBadge(string when)
        => when == "ingame" ? L.T("jeu", "game") : when == "navigation" ? "ES" : when;

    private static readonly Dictionary<string, BitmapImage> ThumbCache = new(StringComparer.OrdinalIgnoreCase);

    private static BitmapImage? LoadThumb(string path)
    {
        if (ThumbCache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 480;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            ThumbCache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void LoadExampleMedia()
    {
        try
        {
            var media = new GameMediaCatalog(_pluginRoot);
            if (!media.IsAvailable) return;
            foreach (var (system, rom) in new[] { ("arcade", "mslug"), ("arcade", "1943"), ("gamegear", "sonic_the_hedgehog") })
            {
                var assets = media.ListAssets(system, rom);
                if (assets.Count == 0) continue;
                foreach (var asset in assets)
                {
                    var key = asset.Key == "wheel" ? "logo" : asset.Key;
                    _exampleMedia.TryAdd(key, asset.Path);
                }
                if (_exampleMedia.Count >= 4) break;
            }
        }
        catch
        {
            // no example media: colored rects only
        }
    }

    // ================= interactions =================

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        var hit = _canvas.Children.OfType<FrameworkElement>().Reverse()
            .FirstOrDefault(el => el.Tag is ComponentModel
                                  && position.X >= Canvas.GetLeft(el) && position.X <= Canvas.GetLeft(el) + el.Width
                                  && position.Y >= Canvas.GetTop(el) && position.Y <= Canvas.GetTop(el) + el.Height);
        _selected = hit?.Tag as ComponentModel;
        // a rail covers the surface by definition: it is selectable, so its properties
        // stay reachable, but dragging or resizing it would only produce a frame that
        // means nothing
        if (_selected is { Locked: false } && !IsPinned(_selected.Type))
        {
            SnapshotHistory();
            _dragging = _selected;
            _resizing = hit is Rectangle { Cursor: not null };
            _dragStart = position;
            _origin = (_selected.X, _selected.Y, _selected.W, _selected.H);
            _canvas.CaptureMouse();
        }
        RenderAll();
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
            _dragging.W = Snap(Math.Clamp(_origin.W + dx, 0.03, 1.5), true, _dragging.X);
            _dragging.H = Snap(Math.Clamp(_origin.H + dy, 0.03, 1.5), false, _dragging.Y);
        }
        else
        {
            _dragging.X = SnapPos(Math.Clamp(_origin.X + dx, -0.25, 1), _dragging.W, true);
            _dragging.Y = SnapPos(Math.Clamp(_origin.Y + dy, -0.25, 1), _dragging.H, false);
        }
        _readout.Text = $"{LayerName(_dragging)} : x={_dragging.X:0.###} y={_dragging.Y:0.###} · {_dragging.W:0.###}×{_dragging.H:0.###}";
        RenderCanvas();
    }

    private void EndDrag()
    {
        if (_dragging == null) return;
        _dragging = null;
        _canvas.ReleaseMouseCapture();
        RenderInspector();
    }

    private List<double> Guides(bool horizontal, ComponentModel exclude)
    {
        var guides = new List<double> { 0, 0.5, 1 };
        foreach (var other in EditableComponents())
        {
            if (ReferenceEquals(other, exclude)) continue;
            guides.Add(horizontal ? other.X : other.Y);
            guides.Add(horizontal ? other.X + other.W : other.Y + other.H);
        }
        return guides;
    }

    private double SnapPos(double value, double size, bool horizontal)
    {
        const double threshold = 0.015;
        foreach (var guide in Guides(horizontal, _dragging!))
        {
            if (Math.Abs(value - guide) <= threshold) return guide;
            if (Math.Abs(value + size - guide) <= threshold) return guide - size;
            if (Math.Abs(value + size / 2 - guide) <= threshold) return guide - size / 2;
        }
        return value;
    }

    private double Snap(double size, bool horizontal, double origin)
    {
        const double threshold = 0.015;
        foreach (var guide in Guides(horizontal, _dragging!))
        {
            if (Math.Abs(origin + size - guide) <= threshold) return guide - origin;
        }
        return size;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // While typing in a field (e.g. the leaderboard title), the editor shortcuts
        // (Delete = remove layer, Ctrl+D/Z/Y…) must NOT fire — let the field handle the key.
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        if (e.Key == Key.Delete && _selected != null)
        {
            SnapshotHistory();
            if (IsPinned(_selected.Type)) return; // a rail is hidden, never removed
            _surface.Components.Remove(_selected);
            _selected = null;
            RenderAll();
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _selected != null)
        {
            SnapshotHistory();
            var copy = CloneComponent(_selected);
            copy.X = Math.Min(1, copy.X + 0.03);
            copy.Y = Math.Min(1, copy.Y + 0.05);
            _surface.Components.Add(copy);
            _selected = copy;
            RenderAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Undo();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Redo();
            e.Handled = true;
        }
    }

    // ================= layers (Photoshop-style) =================

    /// <summary>
    /// A slider for one numeric option, with its value written next to it. A slider
    /// rather than a text field: these are proportions you judge by eye on the marquee,
    /// not numbers you know in advance — and it makes an out-of-range value impossible
    /// to type.
    /// </summary>
    private static FrameworkElement OptionSlider(ComponentModel component, string key, double fallback,
        double min, double max, string label, Func<double, string> format)
    {
        var current = component.Options.TryGetValue(key, out var raw)
                      && double.TryParse(raw, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : fallback;

        var readout = Ui.MutedLabel(format(current));
        readout.Margin = new Thickness(8, 0, 0, 0);
        readout.MinWidth = 46;

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = current,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center
        };
        slider.ValueChanged += (_, args) =>
        {
            readout.Text = format(args.NewValue);
            component.Options[key] = args.NewValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        };

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(slider);
        line.Children.Add(readout);
        return Ui.Row(label, line, labelWidth: 90);
    }

    private string LayerName(ComponentModel component)
        => component.Name.Length > 0 ? component.Name : component.Type;

    /// <summary>
    /// The four RAILS of a surface, in render order. They are not layers you compose:
    /// they are the boundaries of the sandwich, and the rendering pipeline fixes their
    /// order. Pinned means: eye only — no delete, no move. Losing one by recomposing is
    /// what silently disconnected a whole surface from the resolution chain.
    ///
    /// "lamps.scene" is WELDED to "lighting.engine": the rbmarquee lamps are painted
    /// inside the lighting pass, over the lit artwork — nothing can be inserted between
    /// the two.
    /// </summary>
    private static readonly string[] PinnedFront = { "effects.engine", "lamps.scene", "lighting.engine" };
    private const string PinnedBack = "media.flux";

    private static bool IsPinned(string type)
        => type.Equals(PinnedBack, StringComparison.OrdinalIgnoreCase)
           || PinnedFront.Contains(type, StringComparer.OrdinalIgnoreCase);

    /// <summary>Live layers are fed by the streams; baked under the light they would be
    /// covered by the opaque lit artwork.</summary>
    /// <summary>
    /// Overlays that only mean something WHILE PLAYING: a live score, a live timer, the
    /// RetroAchievements panels. There is no score, no run and no session to report
    /// while browsing the library — shown there they were simply the last game's
    /// figures, left on screen. The state is not a preference for these, so it is
    /// forced rather than offered.
    /// </summary>
    private static readonly HashSet<string> IngameOnlyTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "overlay.live.score", "overlay.live.timer",
        "overlay.ra.info", "overlay.ra.badges", "overlay.ra.speedrun"
    };

    private static readonly HashSet<string> LiveTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "media.video", "iccard.static", "iccard.cycle", "external.web",
        "overlay.hiscore", "overlay.live.score", "overlay.live.timer",
        "overlay.ra.info", "overlay.ra.badges", "overlay.ra.speedrun",
        // the panel lights up under the player's fingers: a baked copy would be a
        // photograph of a panel nobody is pressing
        "panel.controls"
    };

    private static string PinnedLabel(string type) => type.ToLowerInvariant() switch
    {
        "effects.engine" => L.T("Événements animés", "Animated events"),
        "lamps.scene" => L.T("Lampes", "Lamps"),
        "lighting.engine" => L.T("Lumière (tubes néon)", "Lighting (neon tubes)"),
        _ => L.T("Image du jeu (fond)", "Game image (background)")
    };

    /// <summary>Makes sure the four rails exist and sit at their fixed positions, keeping
    /// whatever eye the surface already carried. Silent migration: an old surface simply
    /// finds its rails in place.</summary>
    private void NormalizeRails()
    {
        var components = _surface.Components;

        // 1. every rail exists
        foreach (var type in PinnedFront)
            if (!components.Any(c => c.Type.Equals(type, StringComparison.OrdinalIgnoreCase)))
                components.Add(new ComponentModel { Type = type });
        if (!components.Any(c => c.Type.Equals(PinnedBack, StringComparison.OrdinalIgnoreCase)))
            components.Insert(0, new ComponentModel { Type = PinnedBack });

        // 2. the background is the floor — nothing renders under the game image
        var back = components.First(c => c.Type.Equals(PinnedBack, StringComparison.OrdinalIgnoreCase));
        if (components.IndexOf(back) != 0)
        {
            components.Remove(back);
            components.Insert(0, back);
        }

        // 3. the three engines keep their RELATIVE order (lighting < lamps < effects in
        //    back→front order) but stay where the user put them, so the composable zones
        //    between them survive. Only the rails are re-sorted, in their own slots.
        var slots = components
            .Select((c, i) => (Component: c, Index: i))
            .Where(x => PinnedFront.Contains(x.Component.Type, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var desired = slots
            .Select(x => x.Component)
            .OrderBy(c => Array.FindIndex(PinnedFront, t => t.Equals(c.Type, StringComparison.OrdinalIgnoreCase)))
            .Reverse() // list order is back → front, PinnedFront is front → back
            .ToList();
        for (var i = 0; i < slots.Count; i++) components[slots[i].Index] = desired[i];

        // 4. a rail covers the WHOLE surface, always. The lighting lights what is under
        //    it, the lamps sit on the cabinet, the game image is the floor: none of the
        //    three means anything as a rectangle you can drag into a corner. Forcing the
        //    frame here also keeps a rail that was moved before this rule from staying
        //    stuck off-centre.
        foreach (var rail in components.Where(c => IsPinned(c.Type)))
        {
            rail.X = 0;
            rail.Y = 0;
            rail.W = 1;
            rail.H = 1;
        }

        // 5. the ingame-only overlays are pinned to their state, whatever a composition
        //    saved before this rule says. This is also the migration: an RA panel left
        //    on "both" kept showing the previous session's badges over the library.
        foreach (var overlay in components.Where(c => IngameOnlyTypes.Contains(c.Type)))
            overlay.When = "ingame";
    }

    private static string StateName(string state)
        => state == "ingame" ? L.T("En jeu", "Ingame") : L.T("Navigation ES", "ES browsing");

    /// <summary>👁 shown here · ◌ absent from this state · — off everywhere.</summary>
    private static string EyeGlyph(ComponentModel component, bool inState)
        => !component.Visible ? "—" : inState ? "👁" : "◌";

    /// <summary>
    /// The eye means "shown in THIS state". The two states are independent, but the
    /// state a layer belongs to lives in When while Visible has no state dimension at
    /// all: hiding a both-states layer while browsing used to switch off the single
    /// flag the two tabs share, so it went dark ingame too. Hiding now scopes the layer
    /// out of the state you are looking at, and only a layer that belongs to this state
    /// alone is switched off outright.
    /// </summary>
    private void ToggleInState(ComponentModel component)
    {
        // an ingame-only overlay has no state to be scoped into: the eye switches it on
        // or off, nothing more
        if (IngameOnlyTypes.Contains(component.Type))
        {
            component.Visible = !component.Visible;
            component.When = "ingame";
            return;
        }

        var other = _state == "ingame" ? "navigation" : "ingame";
        if (!component.Visible)
        {
            component.Visible = true;
            if (!component.When.Equals(_state, StringComparison.OrdinalIgnoreCase)) component.When = "both";
        }
        else if (component.When.Equals("both", StringComparison.OrdinalIgnoreCase))
        {
            component.When = other; // kept where it still belongs
        }
        else if (component.When.Equals(_state, StringComparison.OrdinalIgnoreCase))
        {
            component.Visible = false; // this was its only state: nothing left to scope
        }
        else
        {
            component.When = "both"; // bring it back into the state you are looking at
        }
    }

    private void RenderLayers()
    {
        _layersPanel.Children.Clear();
        var title = Ui.MutedLabel(L.T("CALQUES (avant → arrière)", "LAYERS (front → back)"), 10);
        title.FontWeight = FontWeights.Bold;
        _layersPanel.Children.Add(title);

        NormalizeRails();

        // front-most first, RetroCreator/Photoshop convention
        var ordered = Enumerable.Reverse(_surface.Components).ToList();
        var lightingIndex = ordered.FindIndex(c => c.Type.Equals("lighting.engine", StringComparison.OrdinalIgnoreCase));
        for (var position = 0; position < ordered.Count; position++)
        {
            var component = ordered[position];
            var pinned = IsPinned(component.Type);

            // a composable gap, so the three zones read at a glance
            if (!pinned && (position == 0 || IsPinned(ordered[position - 1].Type)))
                _layersPanel.Children.Add(ZoneSeparator());

            var inState = component.When is "both"
                          || component.When.Equals(_state, StringComparison.OrdinalIgnoreCase);
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Opacity = inState ? 1 : 0.4 };

            var eye = Ui.Button(EyeGlyph(component, inState), (_, _) =>
            {
                SnapshotHistory();
                ToggleInState(component);
                RenderAll();
            });
            eye.Padding = new Thickness(4, 2, 4, 2);
            eye.ToolTip = IngameOnlyTypes.Contains(component.Type)
                ? L.T("Uniquement en jeu : il n'y a ni score ni session a montrer pendant la navigation.",
                      "Ingame only: there is no score and no session to report while browsing.")
                : !component.Visible
                ? L.T("Éteint partout — cliquez pour rallumer ici.", "Off everywhere — click to switch it back on here.")
                : inState
                    ? L.T($"Affiché en {StateName(_state)} — cliquez pour le retirer de cet état.",
                          $"Shown in {StateName(_state)} — click to drop it from this state.")
                    : L.T($"Absent en {StateName(_state)} — cliquez pour l'y afficher.",
                          $"Absent in {StateName(_state)} — click to show it here.");
            row.Children.Add(eye);
            if (!pinned)
            {
                var padlock = Ui.Button(component.Locked ? "🔒" : "🔓", (_, _) =>
                {
                    SnapshotHistory();
                    component.Locked = !component.Locked;
                    RenderAll();
                });
                padlock.Padding = new Thickness(4, 2, 4, 2);
                row.Children.Add(padlock);
            }

            if (!pinned)
            {
                var buttons = new StackPanel { Orientation = Orientation.Horizontal };
                buttons.Children.Add(Ui.Button("↑", (_, _) => MoveLayer(component, +1)));
                buttons.Children.Add(Ui.Button("↓", (_, _) => MoveLayer(component, -1)));
                DockPanel.SetDock(buttons, Dock.Right);
                row.Children.Add(buttons);
            }

            // a live layer under the light is covered by the opaque lit artwork —
            // say it here instead of letting it be discovered on the cabinet
            var covered = !pinned && lightingIndex >= 0 && position > lightingIndex
                          && LiveTypes.Contains(component.Type);
            if (covered) row.Opacity = 0.45;

            var label = pinned
                ? PinnedLabel(component.Type)
                : LayerName(component) + (component.When == "both" ? "" : $"  [{StateBadge(component.When)}]");
            if (covered) label += L.T("  · sous la lumière : sera recouvert", "  · under the light: will be covered");
            var name = Ui.Label(label, 11);
            name.Margin = new Thickness(6, 0, 0, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            if (ReferenceEquals(component, _selected)) name.Foreground = Ui.Accent;
            name.Cursor = Cursors.Hand;
            name.MouseLeftButtonDown += (_, _) =>
            {
                _selected = component;
                RenderAll();
            };
            row.Children.Add(name);
            _layersPanel.Children.Add(row);
        }
    }

    private FrameworkElement ZoneSeparator()
        => new TextBlock
        {
            Text = L.T("┈┈ vos calques ┈┈", "┈┈ your layers ┈┈"),
            Foreground = Ui.Muted,
            FontSize = 9,
            Margin = new Thickness(0, 4, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center
        };

    private void MoveLayer(ComponentModel component, int towardFront)
    {
        if (IsPinned(component.Type)) return; // rails never move
        var index = _surface.Components.IndexOf(component);
        var target = index + towardFront; // list order = back → front
        if (index < 0 || target < 0 || target >= _surface.Components.Count) return;
        // crossing a rail is how a layer changes zone (lit ↔ unlit); the background is
        // the one that can never be crossed — nothing renders under the game image
        if (_surface.Components[target].Type.Equals(PinnedBack, StringComparison.OrdinalIgnoreCase)) return;
        SnapshotHistory();
        (_surface.Components[index], _surface.Components[target]) = (_surface.Components[target], _surface.Components[index]);
        RenderAll();
    }

    // ================= inspector (3 shared-state groups) =================

    private static readonly bool[] GroupOpen = { true, false, false };

    private void RenderInspector()
    {
        _inspector.Children.Clear();
        if (_selected == null)
        {
            _inspector.Children.Add(Ui.MutedLabel(L.T("Sélectionnez un calque.", "Select a layer.")));
            return;
        }
        var component = _selected;

        var header = Ui.Label(LayerName(component), 13);
        header.FontWeight = FontWeights.Bold;
        _inspector.Children.Add(header);
        var nameBox = Ui.TextBox(component.Name, 180);
        nameBox.TextChanged += (_, _) => component.Name = nameBox.Text.Trim();
        _inspector.Children.Add(Ui.Row(L.T("Nom du calque", "Layer name"), nameBox, labelWidth: 110));

        Expander Group(int index, string title, StackPanel body)
        {
            var expander = new Expander
            {
                Header = new TextBlock { Text = title, Foreground = Ui.Accent, FontWeight = FontWeights.Bold },
                Content = body,
                IsExpanded = GroupOpen[index],
                Margin = new Thickness(0, 6, 0, 0)
            };
            expander.Expanded += (_, _) => GroupOpen[index] = true;
            expander.Collapsed += (_, _) => GroupOpen[index] = false;
            return expander;
        }

        // --- Disposition ---
        var layout = new StackPanel();
        foreach (var (label, get, set) in new (string, Func<double>, Action<double>)[]
                 {
                     ("x", () => component.X, v => component.X = v),
                     ("y", () => component.Y, v => component.Y = v),
                     ("w", () => component.W, v => component.W = v),
                     ("h", () => component.H, v => component.H = v)
                 })
        {
            var box = Ui.TextBox(get().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), 70);
            box.TextChanged += (_, _) =>
            {
                if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    set(Math.Clamp(parsed, -0.5, 1.5));
                    RenderCanvas();
                }
            };
            layout.Children.Add(Ui.Row(label, box, labelWidth: 40));
        }
        _inspector.Children.Add(Group(0, L.T("Disposition", "Layout"), layout));

        // --- Contenu (état + option + provenance) ---
        var content = new StackPanel();
        // No "Visible in" picker: it wrote the same When field the eye now drives,
        // one control contradicting the other on the next click.

        var optionKey = component.Type switch
        {
            "external.web" => "url",
            "media.image" => "kind",
            "text.custom" => "text",
            "text.meta" => "template",
            "iccard.static" => "card",
            "media.video" => "sources",
            _ => null
        };
        if (optionKey != null)
        {
            var opt = Ui.TextBox(component.Options.TryGetValue(optionKey, out var value) ? value : "", 170);
            opt.TextChanged += (_, _) => component.Options[optionKey] = opt.Text;
            content.Children.Add(Ui.Row(optionKey, opt, labelWidth: 90));
        }
        if (component.Type.StartsWith("media.", StringComparison.OrdinalIgnoreCase))
        {
            content.Children.Add(Ui.MutedLabel(L.T(
                "D'où vient ce média ? Flux APIExpose du jeu courant, selon les priorités du système (Mes systèmes).",
                "Where does this media come from? The current game's APIExpose stream, per the system priorities (My systems).")));
        }
        if (component.Type == "panel.controls")
        {
            // One component draws ONE player's panel. A two-player cabinet places two of
            // them, each set to its side — that a press on panel 2 lights panel 2 is part
            // of what the wiring check verifies.
            var players = Ui.ComboBox(180);
            var currentPlayer = component.Options.TryGetValue("player", out var pv) && pv.Length > 0 ? pv : "1";
            for (var player = 1; player <= 4; player++)
            {
                var item = new ComboBoxItem { Content = L.T($"Joueur {player}", $"Player {player}"), Tag = player.ToString() };
                players.Items.Add(item);
                if (item.Tag as string == currentPlayer) players.SelectedItem = item;
            }
            if (players.SelectedItem == null) players.SelectedIndex = 0;
            players.SelectionChanged += (_, _) =>
            {
                if ((players.SelectedItem as ComboBoxItem)?.Tag is string tag) component.Options["player"] = tag;
            };
            content.Children.Add(Ui.Row(L.T("Panneau", "Panel"), players, labelWidth: 90));

            CheckBox PanelToggle(string key, string fr, string en)
            {
                var cb = Ui.CheckBox(L.T(fr, en),
                    !component.Options.TryGetValue(key, out var v) || !v.Equals("false", StringComparison.OrdinalIgnoreCase));
                cb.Checked += (_, _) => component.Options[key] = "true";
                cb.Unchecked += (_, _) => component.Options[key] = "false";
                return cb;
            }
            // both only apply to the plain drawing: the artwork carries its own labels,
            // and has no SELECT/START to show
            content.Children.Add(PanelToggle("labels", "Afficher la fonction des boutons (aspect simple)", "Show what each button does (plain look)"));
            content.Children.Add(PanelToggle("system", "Afficher SELECT et START (aspect simple)", "Show SELECT and START (plain look)"));
            content.Children.Add(Ui.MutedLabel(L.T(
                "Les boutons que le jeu n'utilise pas restent visibles, en transparence : le panneau montre la borne telle qu'elle est. Un appui physique allume le bouton correspondant — c'est ainsi qu'on vérifie son câblage.",
                "Buttons the game does not use stay visible, faded: the panel shows the cabinet as it is. Pressing a physical button lights the matching one — that is how you check your wiring.")));
        }
        if (component.Type == "overlay.hiscore")
        {
            ComboBox Combo(string key, string defaultValue, params (string tag, string fr, string en)[] items)
            {
                var box = Ui.ComboBox(180);
                var current = component.Options.TryGetValue(key, out var cv) && cv.Length > 0 ? cv : defaultValue;
                foreach (var (tag, fr, en) in items)
                {
                    var item = new ComboBoxItem { Content = L.T(fr, en), Tag = tag };
                    box.Items.Add(item);
                    if (tag.Equals(current, StringComparison.OrdinalIgnoreCase)) box.SelectedItem = item;
                }
                if (box.SelectedItem == null) box.SelectedIndex = 0;
                box.SelectionChanged += (_, _) => { if ((box.SelectedItem as ComboBoxItem)?.Tag is string t) component.Options[key] = t; };
                return box;
            }
            CheckBox Toggle(string key, string fr, string en, bool def)
            {
                var cb = Ui.CheckBox(L.T(fr, en),
                    component.Options.TryGetValue(key, out var v) ? !v.Equals("false", StringComparison.OrdinalIgnoreCase) : def);
                cb.Checked += (_, _) => component.Options[key] = "true";
                cb.Unchecked += (_, _) => component.Options[key] = "false";
                return cb;
            }

            // Defaults follow the chosen source AND the UI language; they're plain text the
            // operator can override (title + "my rank" footer). suppress guards programmatic
            // refreshes so they don't get recorded as manual customizations.
            var suppress = false;
            string SourceNow() => component.Options.TryGetValue("source", out var s) && s.Length > 0 ? s : "local";
            string TitleDefault(string src) => src.Equals("nelfeplay", StringComparison.OrdinalIgnoreCase)
                ? L.T("{name} — CLASSEMENT MONDIAL", "{name} — WORLD RANKING")
                : L.T("{name} — CLASSEMENT LOCAL", "{name} — LOCAL LEADERBOARD");
            string MyRankDefault(string src) => src.Equals("nelfeplay", StringComparison.OrdinalIgnoreCase)
                ? L.T("★ TON RANG MONDIAL  {rank} / {of}", "★ YOUR WORLD RANK  {rank} / {of}")
                : L.T("★ TON MEILLEUR ICI  {rank}   {score}", "★ YOUR BEST HERE  {rank}   {score}");

            var titleBox = Ui.TextBox(component.Options.TryGetValue("title", out var tv) ? tv : TitleDefault(SourceNow()), 200);
            titleBox.TextChanged += (_, _) => { if (!suppress) component.Options["title"] = titleBox.Text; };
            var myRankBox = Ui.TextBox(component.Options.TryGetValue("myRankTemplate", out var mrv) ? mrv : MyRankDefault(SourceNow()), 200);
            myRankBox.TextChanged += (_, _) => { if (!suppress) component.Options["myRankTemplate"] = myRankBox.Text; };

            content.Children.Add(Ui.Row(L.T("Titre", "Title"), titleBox, labelWidth: 90));
            content.Children.Add(Ui.MutedLabel(L.T(
                "{name} (ou simplement « gamename ») = nom du jeu ; {system} = système.",
                "{name} (or just \"gamename\") = game name; {system} = system.")));
            var sourceCombo = Combo("source", "local",
                ("local", "Classement local", "Local hiscores"),
                ("nelfeplay", "NelfePlay (en ligne)", "NelfePlay (online)"),
                ("dual", "Les deux (monde puis local)", "Both (world then local)"));
            sourceCombo.SelectionChanged += (_, _) =>
            {
                suppress = true;
                if (!component.Options.ContainsKey("title")) titleBox.Text = TitleDefault(SourceNow());
                if (!component.Options.ContainsKey("myRankTemplate")) myRankBox.Text = MyRankDefault(SourceNow());
                suppress = false;
            };
            content.Children.Add(Ui.Row(L.T("Source", "Source"), sourceCombo, labelWidth: 90));
            // Rows: a free number, or "Dynamique" which fits the count to the available zone.
            var isDynRows = !component.Options.TryGetValue("rows", out var rowsVal)
                || string.IsNullOrWhiteSpace(rowsVal)
                || (rowsVal.Trim().ToLowerInvariant() is "0" or "auto" or "dynamic" or "dynamique");
            var rowsBox = Ui.TextBox(isDynRows ? "10" : rowsVal!.Trim(), 60);
            rowsBox.IsEnabled = !isDynRows;
            var dynRows = Ui.CheckBox(L.T("Dynamique (selon la place)", "Dynamic (fit to space)"), isDynRows);
            dynRows.Margin = new Thickness(10, 0, 0, 0);
            rowsBox.TextChanged += (_, _) => { if (dynRows.IsChecked != true) component.Options["rows"] = rowsBox.Text.Trim(); };
            dynRows.Checked += (_, _) => { component.Options["rows"] = "auto"; rowsBox.IsEnabled = false; };
            dynRows.Unchecked += (_, _) => { rowsBox.IsEnabled = true; component.Options["rows"] = string.IsNullOrWhiteSpace(rowsBox.Text) ? "10" : rowsBox.Text.Trim(); };
            var rowsPanel = new StackPanel { Orientation = Orientation.Horizontal };
            rowsPanel.Children.Add(rowsBox);
            rowsPanel.Children.Add(dynRows);
            content.Children.Add(Ui.Row(L.T("Lignes par page", "Rows per page"), rowsPanel, labelWidth: 90));
            content.Children.Add(Ui.Row(L.T("Durée par page (s)", "Page duration (s)"), Combo("pageSeconds", "6",
                ("4", "4", "4"), ("6", "6", "6"), ("8", "8", "8"), ("10", "10", "10")), labelWidth: 90));
            content.Children.Add(Ui.MutedLabel(L.T(
                "Au-delà de N lignes, le classement défile page par page (max 100 par jeu). « Dynamique » ajuste N à la surface.",
                "Beyond N rows, the leaderboard cycles page by page (max 100 per game). \"Dynamic\" fits N to the surface.")));
            content.Children.Add(Ui.Row(L.T("Mode", "Mode"), Combo("mode", "full",
                ("full", "Classement complet", "Full ranking"), ("best", "Meilleur score", "Best score only")), labelWidth: 90));
            content.Children.Add(Ui.Row(L.T("Fond", "Background"), Combo("background", "dark",
                ("dark", "Sombre", "Dark"), ("transparent", "Transparent", "Transparent"), ("gradient", "Dégradé", "Gradient")), labelWidth: 90));
            content.Children.Add(Ui.Row(L.T("Position", "Alignment"), Combo("align", "middle",
                ("top", "Haut", "Top"), ("middle", "Milieu", "Middle"), ("bottom", "Bas", "Bottom")), labelWidth: 90));
            content.Children.Add(Ui.Row(L.T("Couleur rang/score", "Rank/score colour"), Combo("color", "gold",
                ("gold", "Or (défaut)", "Gold (default)"), ("auto", "Auto (couleur du jeu)", "Auto (game colour)"),
                ("white", "Blanc", "White"), ("cyan", "Cyan", "Cyan"), ("green", "Vert", "Green"),
                ("orange", "Orange", "Orange"), ("pink", "Rose", "Pink"), ("red", "Rouge", "Red")), labelWidth: 90));
            content.Children.Add(Ui.MutedLabel(L.T(
                "« Auto » extrait une couleur vive du logo/marquee du jeu (repli or si absent).",
                "\"Auto\" pulls a vivid colour from the game logo/marquee (falls back to gold).")));
            content.Children.Add(Toggle("showTitle", "Afficher le titre", "Show title", true));
            content.Children.Add(Toggle("showRank", "Colonne rang", "Rank column", true));
            content.Children.Add(Toggle("highlight", "Mettre en valeur un nouveau score", "Highlight a new score", true));
            content.Children.Add(Toggle("showSource", "Filigrane « local / mondial » en bas", "\"local / world\" watermark at the bottom", true));
            content.Children.Add(Toggle("showMyRank", "Afficher mon meilleur rang (sous le classement)", "Show my best rank (below the board)", true));
            content.Children.Add(Ui.Row(L.T("Libellé du rang", "Rank label"), myRankBox, labelWidth: 90));
            content.Children.Add(Ui.MutedLabel(L.T(
                "Modèle libre — {rank} {of} {score} {pseudo}. Local : ta meilleure ligne du jeu. NelfePlay : ton rang mondial certifié (ou une invitation à t'identifier).",
                "Free template — {rank} {of} {score} {pseudo}. Local: your best line for the game. NelfePlay: your certified world rank (or a prompt to identify).")));
        }

        _inspector.Children.Add(Group(1, L.T("Contenu", "Content"), content));

        // --- Style ---
        var style = new StackPanel();
        if (component.Type == "shape.gradient")
        {
            foreach (var key in new[] { "color", "direction", "opacity" })
            {
                var box = Ui.TextBox(component.Options.TryGetValue(key, out var v) ? v : "", 100);
                box.TextChanged += (_, _) => component.Options[key] = box.Text.Trim();
                if (key == "color")
                {
                    var line = new WrapPanel();
                    line.Children.Add(box);
                    line.Children.Add(Ui.ColorPalette(box));
                    style.Children.Add(Ui.Row(key, line, labelWidth: 90));
                }
                else
                {
                    style.Children.Add(Ui.Row(key, box, labelWidth: 90));
                }
            }
        }
        else if (component.Type is "text.meta" or "text.custom")
        {
            var box = Ui.TextBox(component.Options.TryGetValue("color", out var v) ? v : "#FFFFFF", 100);
            box.TextChanged += (_, _) => component.Options["color"] = box.Text.Trim();
            var line = new WrapPanel();
            line.Children.Add(box);
            line.Children.Add(Ui.ColorPalette(box));
            style.Children.Add(Ui.Row(L.T("Couleur", "Color"), line, labelWidth: 90));
        }
        else if (component.Type.StartsWith("media."))
        {
            var stretch = Ui.CheckBox(L.T("Remplir la zone (recadrer, sans déformer)", "Fill the zone (crop, no distortion)"),
                component.Options.TryGetValue("stretch", out var s) && s == "fill");
            stretch.Checked += (_, _) => component.Options["stretch"] = "fill";
            stretch.Unchecked += (_, _) => component.Options.Remove("stretch");
            style.Children.Add(stretch);
        }
        else if (component.Type == "panel.controls")
        {
            // The drawn views are APIExpose's own artwork — the very SVG it writes for
            // EmulationStation themes — so the panel on the marquee and the panel in a
            // theme are the same picture. "Plain" needs no artwork at all, which is also
            // what a cabinet whose theme files were never generated falls back to.
            var looks = Ui.ComboBox(200);
            var currentLook = component.Options.TryGetValue("style", out var sv) && sv.Length > 0 ? sv : "top";
            foreach (var (tag, fr, en) in new[]
                     {
                         ("top", "Vue de dessus (dessinée)", "Top view (artwork)"),
                         ("3d", "Vue de face 3D (dessinée)", "3D front view (artwork)"),
                         ("default", "Simple (formes)", "Plain (shapes)")
                     })
            {
                var item = new ComboBoxItem { Content = L.T(fr, en), Tag = tag };
                looks.Items.Add(item);
                if (tag.Equals(currentLook, StringComparison.OrdinalIgnoreCase)) looks.SelectedItem = item;
            }
            if (looks.SelectedItem == null) looks.SelectedIndex = 0;
            looks.SelectionChanged += (_, _) =>
            {
                if ((looks.SelectedItem as ComboBoxItem)?.Tag is string tag) component.Options["style"] = tag;
            };
            style.Children.Add(Ui.Row(L.T("Aspect", "Look"), looks, labelWidth: 90));

            // A backdrop behind the panel: the artwork is drawn on transparency, and over
            // a busy fanart the buttons lose their edges.
            //
            // A short list rather than a free colour: this veil exists to make the panel
            // readable, and a hand-typed colour is how you end up with a tinted rectangle
            // fighting the game's own artwork. Five neutrals cover it, and "none" is the
            // default, so it costs nothing to anyone who does not want one.
            var backgrounds = Ui.ComboBox(200);
            var currentBackground = component.Options.TryGetValue("bg", out var bg) && bg.Length > 0 ? bg : "";
            foreach (var (tag, fr, en) in new[]
                     {
                         ("", "Aucun", "None"),
                         ("#000000", "Noir", "Black"),
                         ("#FFFFFF", "Blanc", "White"),
                         ("#D64545", "Rouge", "Red"),
                         ("#E0B038", "Jaune", "Yellow"),
                         ("#3D6FD6", "Bleu", "Blue")
                     })
            {
                var item = new ComboBoxItem { Content = L.T(fr, en), Tag = tag };
                backgrounds.Items.Add(item);
                if (tag.Equals(currentBackground, StringComparison.OrdinalIgnoreCase)) backgrounds.SelectedItem = item;
            }
            if (backgrounds.SelectedItem == null) backgrounds.SelectedIndex = 0;
            backgrounds.SelectionChanged += (_, _) =>
            {
                if ((backgrounds.SelectedItem as ComboBoxItem)?.Tag is string tag) component.Options["bg"] = tag;
            };
            style.Children.Add(Ui.Row(L.T("Fond", "Background"), backgrounds, labelWidth: 90));

            style.Children.Add(OptionSlider(component, "bgOpacity", 0.5, 0, 1,
                L.T("Opacité du fond", "Background opacity"), value => $"{value * 100:0} %"));
            style.Children.Add(OptionSlider(component, "bgPadding", 0.03, 0, 0.12,
                L.T("Marge du fond", "Background padding"), value => $"{value * 100:0.0} %"));
        }
        else
        {
            style.Children.Add(Ui.MutedLabel(L.T("Aucun réglage de style pour ce type.", "No style setting for this type.")));
        }
        _inspector.Children.Add(Group(2, "Style", style));

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Supprimer le calque", "Delete layer"), (_, _) =>
        {
            SnapshotHistory();
            if (IsPinned(component.Type)) return; // a rail is hidden, never removed
            _surface.Components.Remove(component);
            _selected = null;
            RenderAll();
        }));
        _inspector.Children.Add(actions);
    }

    // ================= undo/redo =================

    private string Serialize() => JsonSerializer.Serialize(_surface.Components.Select(c => new
    {
        c.Type, c.X, c.Y, c.W, c.H, c.When, c.Visible, c.Locked, c.Name,
        Options = c.Options.ToDictionary(kv => kv.Key, kv => kv.Value)
    }));

    private void Restore(string snapshot)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            _surface.Components.Clear();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                var component = new ComponentModel
                {
                    Type = element.GetProperty("Type").GetString() ?? "media.flux",
                    X = element.GetProperty("X").GetDouble(),
                    Y = element.GetProperty("Y").GetDouble(),
                    W = element.GetProperty("W").GetDouble(),
                    H = element.GetProperty("H").GetDouble(),
                    When = element.GetProperty("When").GetString() ?? "both",
                    Visible = element.GetProperty("Visible").GetBoolean(),
                    Locked = element.GetProperty("Locked").GetBoolean(),
                    Name = element.GetProperty("Name").GetString() ?? ""
                };
                foreach (var option in element.GetProperty("Options").EnumerateObject())
                    component.Options[option.Name] = option.Value.GetString() ?? "";
                _surface.Components.Add(component);
            }
        }
        catch
        {
            // corrupt snapshot: keep current state
        }
        _selected = null;
        RenderAll();
    }

    private void SnapshotHistory()
    {
        if (_historyIndex < _history.Count - 1)
            _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        _history.Add(Serialize());
        if (_history.Count > 100) _history.RemoveAt(0);
        _historyIndex = _history.Count - 1;
    }

    private void Undo()
    {
        if (_historyIndex <= 0) return;
        if (_historyIndex == _history.Count - 1)
        {
            _history.Add(Serialize());
        }
        _historyIndex--;
        Restore(_history[_historyIndex]);
    }

    private void Redo()
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        Restore(_history[_historyIndex]);
    }

    private static ComponentModel CloneComponent(ComponentModel source)
    {
        var copy = new ComponentModel
        {
            Type = source.Type, X = source.X, Y = source.Y, W = source.W, H = source.H,
            When = source.When, Visible = source.Visible, Locked = source.Locked, Name = source.Name
        };
        foreach (var (key, value) in source.Options) copy.Options[key] = value;
        return copy;
    }
}
