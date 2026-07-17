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
        ["iccard.cycle"] = Color.FromRgb(0x20, 0xE8, 0xE8)
    };

    public CompositionEditor(string pluginRoot, SurfaceModel surface, double aspect, string initialState = "navigation")
    {
        _pluginRoot = pluginRoot;
        _surface = surface;
        _aspect = aspect;
        _state = initialState is "ingame" or "navigation" ? initialState : "navigation";

        Title = L.T($"Composition — {surface.Id}", $"Composition — {surface.Id}");
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
                     ("ingame", "En jeu", "Ingame"),
                     ("both", "Navigation ES + En jeu", "ES browsing + Ingame")
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
        headerRight.Children.Add(Ui.Button(L.T("Valider la composition", "Apply composition"), (_, _) => DialogResult = true, primary: true));
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

    private List<Preset> Presets()
    {
        ComponentModel C(string type, double x = 0, double y = 0, double w = 1, double h = 1,
            params (string K, string V)[] options)
        {
            var component = new ComponentModel { Type = type, X = x, Y = y, W = w, H = h, When = _state };
            foreach (var (k, v) in options) component.Options[k] = v;
            return component;
        }

        // orientation-aware fanart (user rule): full width when landscape, full height when portrait
        List<ComponentModel> Fanart() => new() { C("media.fanart") };
        List<ComponentModel> Logo()
        {
            var w = 0.5;
            var h = Math.Min(1, 0.5 * _aspect * 0.5); // roughly half width, aspect-kept by Uniform stretch
            return new() { C("media.logo", (1 - w) / 2, (1 - h) / 2, w, h) };
        }

        return new List<Preset>
        {
            new("🖼 " + L.T("Médias", "Media"), "Fanart", Fanart),
            new("🖼 " + L.T("Médias", "Media"), L.T("Logo (50 %)", "Logo (50 %)"), Logo),
            new("🖼 " + L.T("Médias", "Media"), L.T("Marquee du flux", "Stream marquee"), () => new() { C("media.flux") }),
            new("🖼 " + L.T("Médias", "Media"), L.T("Vidéo du jeu", "Game video"), () => new() { C("media.video", 0, 0, 1, 1, ("sources", "local")) }),
            new("🖼 " + L.T("Médias", "Media"), L.T("Image (kind)", "Image (kind)"), () => new() { C("media.image", 0.25, 0.25, 0.5, 0.5, ("kind", "screenmarquee")) }),

            new("🃏 " + L.T("Infos du jeu", "Game info"), L.T("Titre du jeu", "Game title"), () => new() { C("text.meta", 0.05, 0.75, 0.9, 0.2, ("template", "{name}")) }),
            new("🃏 " + L.T("Infos du jeu", "Game info"), L.T("Année · éditeur", "Year · publisher"), () => new() { C("text.meta", 0.05, 0.85, 0.9, 0.12, ("template", "{year} — {publisher}")) }),
            new("🃏 " + L.T("Infos du jeu", "Game info"), "Instruction card", () => new() { C("iccard.cycle") }),

            new("📊 Live", "Hiscores", () => new() { C("overlay.hiscore", 0.7, 0.05, 0.28, 0.6) }),
            new("📊 Live", L.T("Score live", "Live score"), () => new() { C("overlay.live.score", 0.02, 0.7, 0.3, 0.28) }),
            new("📊 Live", L.T("Timer live", "Live timer"), () => new() { C("overlay.live.timer", 0.68, 0.7, 0.3, 0.28) }),

            new("🏆 RetroAchievements", L.T("Badges", "Badges"), () => new() { C("overlay.ra.badges", 0, 0.85, 1, 0.15) }),
            new("🏆 RetroAchievements", L.T("Infos RA", "RA info"), () => new() { C("overlay.ra.info", 0, 0.7, 1, 0.3) }),
            new("🏆 RetroAchievements", "Speedrun", () => new() { C("overlay.ra.speedrun") }),

            new("🔷 " + L.T("Décoration", "Decoration"), L.T("Gradient (lisibilité)", "Gradient (readability)"),
                () => new() { C("shape.gradient", 0, 0.35, 1, 0.65, ("color", "#000000"), ("direction", "down"), ("opacity", "0.75")) }),
            new("🔷 " + L.T("Décoration", "Decoration"), L.T("Texte libre", "Custom text"), () => new() { C("text.custom", 0.1, 0.4, 0.8, 0.2, ("text", "Mon texte")) }),
            new("🔷 " + L.T("Décoration", "Decoration"), L.T("Web (Twitch/YouTube)", "Web (Twitch/YouTube)"), () => new() { C("external.web", 0, 0, 1, 1, ("url", "")) }),
            new("🔷 " + L.T("Décoration", "Decoration"), L.T("Lumière (tubes néon)", "Lighting (neon tubes)"), () => new() { C("lighting.engine") }),

            new("🧩 " + L.T("Composites", "Composites"), L.T("Marquee (fanart+gradient+logo)", "Marquee (fanart+gradient+logo)"),
                () => Fanart()
                    .Append(C("shape.gradient", 0, 0.35, 1, 0.65, ("color", "#000000"), ("direction", "down"), ("opacity", "0.75")))
                    .Concat(Logo()).ToList()),
            new("🧩 " + L.T("Composites", "Composites"), L.T("Score complet (fond+titres+liste)", "Full score (bg+titles+list)"),
                () => new()
                {
                    C("shape.gradient", 0, 0, 1, 1, ("color", "#000010"), ("direction", "down"), ("opacity", "0.9")),
                    C("text.meta", 0.05, 0.02, 0.9, 0.18, ("template", "{name}")),
                    C("text.meta", 0.05, 0.2, 0.9, 0.1, ("template", "HIGH SCORES — {system}")),
                    C("overlay.hiscore", 0.1, 0.32, 0.8, 0.64)
                }),
            new("🧩 " + L.T("Composites", "Composites"), L.T("Live media (fanart+logo+vidéo)", "Live media (fanart+logo+video)"),
                () => Fanart()
                    .Append(C("media.video", 0.55, 0.1, 0.4, 0.8, ("sources", "twitch-live|youtube|local")))
                    .Concat(Logo().Select(l => { l.X = 0.05; l.W = 0.4; return l; })).ToList()),
            new("🧩 " + L.T("Composites", "Composites"), L.T("Chat Twitch", "Twitch chat"),
                () => new() { C("external.web", 0.7, 0, 0.3, 1, ("url", "https://www.twitch.tv/embed/MA_CHAINE/chat?parent=twitch.tv&darkpopout")) })
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
            if (_state != "both") component.When = _state;
            _surface.Components.Add(component);
        }
        _selected = made.LastOrDefault();
        RenderAll();
    }

    // ================= canvas =================

    private IEnumerable<ComponentModel> EditableComponents()
        => _surface.Components.Where(c => _state == "both"
            || c.When.Equals("both", StringComparison.OrdinalIgnoreCase)
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
                "media.flux" => "marquee",
                _ => null
            };
            if (kind != null && _exampleMedia.TryGetValue(kind, out var mediaPath))
            {
                rect.Child = new Image
                {
                    Source = LoadThumb(mediaPath),
                    Stretch = component.Options.TryGetValue("stretch", out var s) && s == "fill" ? Stretch.Fill : Stretch.Uniform,
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
        if (_selected is { Locked: false })
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
        if (e.Key == Key.Delete && _selected != null)
        {
            SnapshotHistory();
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

    private string LayerName(ComponentModel component)
        => component.Name.Length > 0 ? component.Name : component.Type;

    private void RenderLayers()
    {
        _layersPanel.Children.Clear();
        var title = Ui.MutedLabel(L.T("CALQUES (avant → arrière)", "LAYERS (front → back)"), 10);
        title.FontWeight = FontWeights.Bold;
        _layersPanel.Children.Add(title);

        // front-most first, RetroCreator/Photoshop convention
        foreach (var component in Enumerable.Reverse(_surface.Components).ToList())
        {
            var inState = _state == "both" || component.When is "both"
                          || component.When.Equals(_state, StringComparison.OrdinalIgnoreCase);
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Opacity = inState ? 1 : 0.4 };

            var eye = Ui.Button(component.Visible ? "👁" : "—", (_, _) =>
            {
                SnapshotHistory();
                component.Visible = !component.Visible;
                RenderAll();
            });
            eye.Padding = new Thickness(4, 2, 4, 2);
            row.Children.Add(eye);
            var padlock = Ui.Button(component.Locked ? "🔒" : "🔓", (_, _) =>
            {
                SnapshotHistory();
                component.Locked = !component.Locked;
                RenderAll();
            });
            padlock.Padding = new Thickness(4, 2, 4, 2);
            row.Children.Add(padlock);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Ui.Button("↑", (_, _) => MoveLayer(component, +1)));
            buttons.Children.Add(Ui.Button("↓", (_, _) => MoveLayer(component, -1)));
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);

            var name = Ui.Label(LayerName(component) + (component.When == "both" ? "" : $"  [{StateBadge(component.When)}]"), 11);
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

    private void MoveLayer(ComponentModel component, int towardFront)
    {
        var index = _surface.Components.IndexOf(component);
        var target = index + towardFront; // list order = back → front
        if (index < 0 || target < 0 || target >= _surface.Components.Count) return;
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
        var when = Ui.ComboBox(150);
        foreach (var (key, fr, en) in new[]
                 {
                     ("both", "Les deux états", "Both states"),
                     ("navigation", "Navigation ES", "ES browsing"),
                     ("ingame", "En jeu", "Ingame")
                 })
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            when.Items.Add(item);
            if (key.Equals(component.When, StringComparison.OrdinalIgnoreCase)) when.SelectedItem = item;
        }
        if (when.SelectedItem == null) when.SelectedIndex = 0;
        when.SelectionChanged += (_, _) =>
        {
            if ((when.SelectedItem as ComboBoxItem)?.Tag is string key)
            {
                component.When = key;
                RenderAll();
            }
        };
        content.Children.Add(Ui.Row(L.T("Visible en", "Visible in"), when, labelWidth: 90));

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
        _inspector.Children.Add(Group(1, L.T("Contenu", "Content"), content));

        // --- Style ---
        var style = new StackPanel();
        if (component.Type == "shape.gradient")
        {
            foreach (var key in new[] { "color", "direction", "opacity" })
            {
                var box = Ui.TextBox(component.Options.TryGetValue(key, out var v) ? v : "", 100);
                box.TextChanged += (_, _) => component.Options[key] = box.Text.Trim();
                style.Children.Add(Ui.Row(key, box, labelWidth: 90));
            }
        }
        else if (component.Type is "text.meta" or "text.custom")
        {
            var box = Ui.TextBox(component.Options.TryGetValue("color", out var v) ? v : "#FFFFFF", 100);
            box.TextChanged += (_, _) => component.Options["color"] = box.Text.Trim();
            style.Children.Add(Ui.Row(L.T("Couleur", "Color"), box, labelWidth: 90));
        }
        else if (component.Type.StartsWith("media."))
        {
            var stretch = Ui.CheckBox(L.T("Étirer (fill)", "Stretch (fill)"),
                component.Options.TryGetValue("stretch", out var s) && s == "fill");
            stretch.Checked += (_, _) => component.Options["stretch"] = "fill";
            stretch.Unchecked += (_, _) => component.Options.Remove("stretch");
            style.Children.Add(stretch);
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
