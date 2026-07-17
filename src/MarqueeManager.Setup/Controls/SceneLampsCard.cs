using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Xml.Linq;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// Editor for the game's rbmarquee scene lamps (resources\rbmarquee\&lt;rom&gt;.xml):
/// drag a lamp on the marquee image, resize it with the wheel, recolor it, and
/// rewire it to a MAME output. Handles both lamp shapes of the format — circles
/// (x/y/radius) and rectangles (region="x y w h"), fractional coordinates.
/// Saving stamps generated="false": the scene is curated, the generator will
/// never overwrite it again (.bak kept).
/// </summary>
public sealed class SceneLampsCard : UserControl
{
    private const double ViewWidth = 620;

    private sealed class Lamp
    {
        public string Id = "";
        public string Color = "#ffd9a0";
        public bool IsRegion;
        public double X = 0.5, Y = 0.5, Radius = 0.12;          // circle
        public double RX, RY, RW = 0.2, RH = 1.0;               // region
        public string Output = "";
    }

    private readonly string _pluginRoot;
    private readonly string _rom;
    private readonly string _scenePath;
    private readonly List<Lamp> _lamps = new();
    private readonly List<string> _knownOutputs = new();
    private readonly IReadOnlyList<(string Label, string Path)> _backgrounds;
    private string _attractMode = "none";
    private string? _imageAttr;
    private string? _backgroundPath;
    private double _viewHeight = ViewWidth / 4;

    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly StackPanel _inspector = new() { Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _lampList = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private Lamp? _selected;
    private bool _dragging;
    private Point _dragStart;
    private (double X, double Y) _dragOrigin;

    // attract-mode test: animates the placed lamps like the runtime would
    private System.Windows.Threading.DispatcherTimer? _attractTimer;
    private int _attractStep;
    private Button? _attractButton;

    /// <summary>backgrounds: candidate marquee images to place lamps on (the
    /// generated marquee first when it exists); a selector shows when several.</summary>
    public SceneLampsCard(string pluginRoot, string system, string rom,
        IReadOnlyList<(string Label, string Path)>? backgrounds = null)
    {
        _pluginRoot = pluginRoot;
        _rom = rom;
        _backgrounds = backgrounds ?? Array.Empty<(string, string)>();
        _scenePath = Path.Combine(pluginRoot, "resources", "rbmarquee", rom + ".xml");

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Mon marquee dynamique Arcade", "My dynamic Arcade marquee")));

        LoadKnownOutputs(system, rom);
        var exists = File.Exists(_scenePath);
        if (exists)
        {
            try
            {
                LoadScene();
            }
            catch (Exception ex)
            {
                card.Children.Add(Ui.Label(L.T($"Scène illisible : {ex.Message}", $"Unreadable scene: {ex.Message}")));
                Content = card;
                return;
            }
        }

        _backgroundPath = ResolveBackground(_backgrounds.FirstOrDefault().Path);
        if (!exists)
        {
            card.Children.Add(Ui.MutedLabel(L.T(
                "Ce jeu n'a pas de scène lumineuse. Créez-en une pour poser des lampes pilotées par les outputs MAME.",
                "This game has no light scene yet. Create one to place lamps driven by the MAME outputs.")));
            var create = Ui.Button(L.T("Créer une scène", "Create a scene"), (_, _) =>
            {
                _lamps.Add(NewLamp());
                RebuildAsEditor();
            }, primary: true);
            var host = new WrapPanel();
            host.Children.Add(create);
            card.Children.Add(host);
            card.Children.Add(_status);
            Content = card;
            return;
        }

        BuildEditor(card);
        Content = card;
    }

    private void RebuildAsEditor()
    {
        // _status/_canvas/_inspector are reused fields: detach them from the old
        // visual tree first, or WPF throws "already the logical child".
        (Content as Panel)?.Children.Clear();
        if (_canvas.Parent is Border oldHost) oldHost.Child = null;

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Mon marquee dynamique Arcade", "My dynamic Arcade marquee")));
        BuildEditor(card);
        Content = card;
    }

    private bool _canvasWired;

    private void BuildEditor(StackPanel card)
    {
        card.Children.Add(Ui.MutedLabel(L.T(
            "Cliquer = sélectionner, glisser = déplacer, molette = taille. Les couleurs et le câblage output se règlent sous l'aperçu.",
            "Click = select, drag = move, wheel = resize. Colors and output wiring live under the preview.")));

        // background picker when several marquee images exist (generated first)
        if (_backgrounds.Count > 1)
        {
            var backgroundRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            var backgroundLabel = Ui.MutedLabel(L.T("Image de fond :", "Background image:"));
            backgroundLabel.Margin = new Thickness(0, 0, 6, 0);
            backgroundLabel.VerticalAlignment = VerticalAlignment.Center;
            backgroundRow.Children.Add(backgroundLabel);
            var backgroundPicker = Ui.ComboBox(240);
            foreach (var (label, path) in _backgrounds)
            {
                var item = new ComboBoxItem { Content = label, Tag = path };
                backgroundPicker.Items.Add(item);
                if (path.Equals(_backgroundPath, StringComparison.OrdinalIgnoreCase)) backgroundPicker.SelectedItem = item;
            }
            if (backgroundPicker.SelectedItem == null) backgroundPicker.SelectedIndex = 0;
            backgroundPicker.SelectionChanged += (_, _) =>
            {
                if ((backgroundPicker.SelectedItem as ComboBoxItem)?.Tag is not string path) return;
                _backgroundPath = path;
                SizeCanvasToBackground();
                _canvas.Height = _viewHeight;
                Render();
            };
            backgroundRow.Children.Add(backgroundPicker);
            card.Children.Add(backgroundRow);
        }

        SizeCanvasToBackground();
        _canvas.Width = ViewWidth;
        _canvas.Height = _viewHeight;
        _canvas.Background = Ui.Viewport;
        if (!_canvasWired)
        {
            _canvasWired = true;
            _canvas.MouseLeftButtonDown += Canvas_MouseDown;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseLeftButtonUp += (_, _) => EndDrag();
            _canvas.MouseLeave += (_, _) => EndDrag();
            _canvas.MouseWheel += Canvas_MouseWheel;
        }

        card.Children.Add(new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _canvas
        });

        card.Children.Add(_inspector);

        // every placed lamp with its parameters at a glance, click = select
        card.Children.Add(_lampList);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Ajouter une lampe", "Add a lamp"), (_, _) =>
        {
            var lamp = NewLamp();
            _lamps.Add(lamp);
            Select(lamp);
            Render();
        }));

        var attractLabel = Ui.MutedLabel(L.T("Mode attract :", "Attract mode:"));
        attractLabel.Margin = new Thickness(8, 0, 6, 0);
        actions.Children.Add(attractLabel);
        var attract = Ui.ComboBox(130);
        foreach (var (key, fr, en) in new[]
                 {
                     ("none", "Aucun", "None"),
                     ("chase", "Chenillard", "Chase"),
                     ("alternate", "Alterné", "Alternate")
                 })
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            attract.Items.Add(item);
            if (key.Equals(_attractMode, StringComparison.OrdinalIgnoreCase)) attract.SelectedItem = item;
        }
        if (attract.SelectedItem == null) attract.SelectedIndex = 0;
        attract.SelectionChanged += (_, _) =>
        {
            if ((attract.SelectedItem as ComboBoxItem)?.Tag is string mode) _attractMode = mode;
        };
        actions.Children.Add(attract);
        _attractButton = Ui.Button(L.T("▶ Tester l'attract mode", "▶ Test attract mode"), (_, _) => ToggleAttractTest());
        actions.Children.Add(_attractButton);
        actions.Children.Add(Ui.Button(L.T("Enregistrer la scène (curée)", "Save the scene (curated)"), (_, _) => SaveScene(), primary: true));
        card.Children.Add(actions);
        card.Children.Add(Ui.MutedLabel(L.T(
            "Enregistrer marque la scène generated=\"false\" : le générateur rbmarquee ne l'écrasera plus.",
            "Saving stamps the scene generated=\"false\": the rbmarquee generator will never overwrite it again.")));
        _status.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(_status);

        Render();
    }

    private Lamp NewLamp() => new()
    {
        Id = "L" + _lamps.Count,
        Output = _knownOutputs.FirstOrDefault() ?? "",
        X = 0.2 + 0.15 * _lamps.Count % 0.8
    };

    // ================= scene I/O =================

    private void LoadScene()
    {
        var doc = XDocument.Load(_scenePath);
        var scene = doc.Root?.Element("scene");
        if (scene == null) return;
        _imageAttr = (string?)scene.Attribute("image");
        _attractMode = (string?)scene.Element("attract")?.Attribute("mode") ?? "none";

        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in scene.Element("bindings")?.Element("arcadeOutputs")?.Elements("map") ?? Enumerable.Empty<XElement>())
        {
            var to = (string?)map.Attribute("to") ?? "";
            if (to.StartsWith("lamp:", StringComparison.OrdinalIgnoreCase))
            {
                bindings[to["lamp:".Length..]] = (string?)map.Attribute("output") ?? "";
            }
        }

        foreach (var element in scene.Element("lamps")?.Elements("lamp") ?? Enumerable.Empty<XElement>())
        {
            var lamp = new Lamp
            {
                Id = (string?)element.Attribute("id") ?? "L" + _lamps.Count,
                Color = (string?)element.Attribute("color") ?? "#ffd9a0"
            };
            var region = (string?)element.Attribute("region");
            if (region != null)
            {
                var parts = region.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    lamp.IsRegion = true;
                    lamp.RX = Fraction(parts[0]);
                    lamp.RY = Fraction(parts[1]);
                    lamp.RW = Fraction(parts[2]);
                    lamp.RH = Fraction(parts[3]);
                }
            }
            else
            {
                lamp.X = Fraction((string?)element.Attribute("x") ?? "0.5");
                lamp.Y = Fraction((string?)element.Attribute("y") ?? "0.5");
                lamp.Radius = Fraction((string?)element.Attribute("radius") ?? "0.1");
            }
            lamp.Output = bindings.TryGetValue(lamp.Id, out var output) ? output : "";
            _lamps.Add(lamp);
        }
    }

    public void SaveScene()
    {
        try
        {
            var system = "mame";
            if (File.Exists(_scenePath))
            {
                try
                {
                    File.Copy(_scenePath, _scenePath + ".bak", overwrite: true);
                    system = (string?)XDocument.Load(_scenePath).Root?.Attribute("system") ?? "mame";
                }
                catch
                {
                    // backup/system best effort
                }
            }

            var lamps = new XElement("lamps");
            var maps = new XElement("arcadeOutputs");
            foreach (var lamp in _lamps)
            {
                var element = new XElement("lamp", new XAttribute("id", lamp.Id));
                if (lamp.IsRegion)
                {
                    element.Add(new XAttribute("region",
                        string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"{lamp.RX:0.0000} {lamp.RY:0.0000} {lamp.RW:0.0000} {lamp.RH:0.0000}")));
                }
                else
                {
                    element.Add(new XAttribute("x", Frac(lamp.X)), new XAttribute("y", Frac(lamp.Y)),
                        new XAttribute("radius", Frac(lamp.Radius)));
                }
                element.Add(new XAttribute("color", lamp.Color));
                lamps.Add(element);
                if (lamp.Output.Length > 0)
                {
                    maps.Add(new XElement("map", new XAttribute("output", lamp.Output),
                        new XAttribute("to", "lamp:" + lamp.Id)));
                }
            }

            var scene = new XElement("scene", new XAttribute("target", "marquee"));
            if (_imageAttr is { Length: > 0 }) scene.Add(new XAttribute("image", _imageAttr));
            scene.Add(lamps, new XElement("bindings", maps), new XElement("attract", new XAttribute("mode", _attractMode)));

            var doc = new XDocument(
                new XComment(" Curé avec MarqueeManagerSetup : ce fichier prime et le générateur ne le touche plus. "),
                new XElement("rbmarquee",
                    new XAttribute("version", "1.2"),
                    new XAttribute("game", _rom),
                    new XAttribute("system", system),
                    new XAttribute("generated", "false"),
                    scene));
            Directory.CreateDirectory(Path.GetDirectoryName(_scenePath)!);
            doc.Save(_scenePath);
            _status.Text = L.T($"Scène enregistrée : {_scenePath}", $"Scene saved: {_scenePath}");
            _status.Foreground = Ui.Ok;
        }
        catch (Exception ex)
        {
            _status.Text = L.T($"Échec de l'enregistrement : {ex.Message}", $"Save failed: {ex.Message}");
            _status.Foreground = Ui.Error;
        }
    }

    // ================= attract-mode test =================

    /// <summary>Animates the lamps like the runtime's attract mode: chase lights
    /// one lamp after the other, alternate blinks odd/even, none blinks together.</summary>
    private void ToggleAttractTest()
    {
        if (_attractTimer != null)
        {
            StopAttractTest();
            return;
        }
        if (_lamps.Count == 0)
        {
            _status.Text = L.T("Ajoutez d'abord une lampe.", "Add a lamp first.");
            _status.Foreground = Ui.Error;
            return;
        }
        _attractStep = 0;
        _attractTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(280)
        };
        _attractTimer.Tick += (_, _) =>
        {
            _attractStep++;
            if (_attractStep > 40) StopAttractTest(); // ~11 s then back to normal
            else Render();
        };
        _attractTimer.Start();
        _attractButton!.Content = L.T("■ Arrêter le test", "■ Stop the test");
        _status.Text = L.T($"Attract mode « {_attractMode} » en cours sur {_lamps.Count} lampe(s).",
            $"Attract mode “{_attractMode}” running on {_lamps.Count} lamp(s).");
        _status.Foreground = Ui.Muted;
        Render();
    }

    private void StopAttractTest()
    {
        _attractTimer?.Stop();
        _attractTimer = null;
        if (_attractButton != null) _attractButton.Content = L.T("▶ Tester l'attract mode", "▶ Test attract mode");
        Render();
    }

    /// <summary>Lit state of a lamp during the attract test (full brightness vs dimmed).</summary>
    private bool AttractLit(int index) => _attractMode switch
    {
        "chase" => index == _attractStep % Math.Max(1, _lamps.Count),
        "alternate" => (index + _attractStep) % 2 == 0,
        _ => _attractStep % 2 == 0
    };

    // ================= canvas =================

    private void SizeCanvasToBackground()
    {
        if (_backgroundPath != null && TryLoad(_backgroundPath) is { } bitmap && bitmap.PixelWidth > 0)
        {
            _viewHeight = Math.Clamp(ViewWidth * bitmap.PixelHeight / bitmap.PixelWidth, 60, 320);
        }
    }

    private string? ResolveBackground(string? fallback)
    {
        if (_imageAttr is { Length: > 0 })
        {
            var path = Path.Combine(_pluginRoot, "resources", "images", _imageAttr);
            if (File.Exists(path)) return path;
        }
        return fallback != null && File.Exists(fallback) ? fallback : null;
    }

    private void Render()
    {
        _canvas.Children.Clear();
        if (_backgroundPath != null && TryLoad(_backgroundPath) is { } bitmap)
        {
            _canvas.Children.Add(new Image
            {
                Source = bitmap,
                Width = ViewWidth,
                Height = _viewHeight,
                Stretch = Stretch.Fill,
                Opacity = 0.85
            });
        }

        var testing = _attractTimer != null;
        for (var lampIndex = 0; lampIndex < _lamps.Count; lampIndex++)
        {
            var lamp = _lamps[lampIndex];
            var lit = !testing || AttractLit(lampIndex);
            var fill = new SolidColorBrush(ParseColor(lamp.Color)) { Opacity = testing ? (lit ? 0.9 : 0.10) : 0.45 };
            var stroke = new SolidColorBrush(ParseColor(lamp.Color));
            Shape shape;
            if (lamp.IsRegion)
            {
                shape = new Rectangle
                {
                    Width = Math.Max(8, lamp.RW * ViewWidth),
                    Height = Math.Max(8, lamp.RH * _viewHeight),
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(shape, lamp.RX * ViewWidth);
                Canvas.SetTop(shape, lamp.RY * _viewHeight);
            }
            else
            {
                var diameter = Math.Max(10, lamp.Radius * 2 * _viewHeight);
                shape = new Ellipse { Width = diameter, Height = diameter };
                Canvas.SetLeft(shape, lamp.X * ViewWidth - diameter / 2);
                Canvas.SetTop(shape, lamp.Y * _viewHeight - diameter / 2);
            }
            shape.Fill = fill;
            shape.Stroke = stroke;
            shape.StrokeThickness = lamp == _selected ? 2.5 : 1.2;
            if (lamp == _selected)
            {
                shape.StrokeDashArray = new DoubleCollection { 3, 2 };
            }
            shape.Tag = lamp;
            _canvas.Children.Add(shape);

            var label = new TextBlock
            {
                Text = lamp.Id + (lamp.Output.Length > 0 ? $" ← {lamp.Output}" : ""),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(0x88, 0x00, 0x00, 0x00)),
                Padding = new Thickness(3, 1, 3, 1),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            };
            TextOptions.SetTextFormattingMode(label, TextFormattingMode.Display);
            Canvas.SetLeft(label, (lamp.IsRegion ? lamp.RX + lamp.RW / 2 : lamp.X) * ViewWidth - 20);
            Canvas.SetTop(label, (lamp.IsRegion ? lamp.RY + lamp.RH / 2 : lamp.Y) * _viewHeight - 8);
            _canvas.Children.Add(label);
        }
        RefreshLampList();
    }

    /// <summary>Detailed table of every lamp: shape, geometry, color, output, actions.</summary>
    private void RefreshLampList()
    {
        _lampList.Children.Clear();
        if (_lamps.Count == 0) return;
        var header = Ui.MutedLabel(L.T($"LAMPES ({_lamps.Count})", $"LAMPS ({_lamps.Count})"), 10);
        header.FontWeight = FontWeights.Bold;
        _lampList.Children.Add(header);

        foreach (var lamp in _lamps)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(ParseColor(lamp.Color)),
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(dot);

            var id = Ui.Label(lamp.Id, 11);
            id.FontWeight = lamp == _selected ? FontWeights.Bold : FontWeights.Normal;
            if (lamp == _selected) id.Foreground = Ui.Accent;
            id.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(id, 1);
            row.Children.Add(id);

            var geometry = lamp.IsRegion
                ? L.T($"rectangle · x={lamp.RX:0.###} y={lamp.RY:0.###} · {lamp.RW:0.###}×{lamp.RH:0.###}",
                    $"rectangle · x={lamp.RX:0.###} y={lamp.RY:0.###} · {lamp.RW:0.###}×{lamp.RH:0.###}")
                : L.T($"cercle · x={lamp.X:0.###} y={lamp.Y:0.###} · rayon {lamp.Radius:0.###}",
                    $"circle · x={lamp.X:0.###} y={lamp.Y:0.###} · radius {lamp.Radius:0.###}");
            var details = Ui.MutedLabel(geometry, 11);
            details.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(details, 2);
            row.Children.Add(details);

            var wiring = Ui.MutedLabel(lamp.Output.Length > 0 ? $"← {lamp.Output}" : L.T("(non câblée)", "(not wired)"), 11);
            wiring.VerticalAlignment = VerticalAlignment.Center;
            wiring.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(wiring, 3);
            row.Children.Add(wiring);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var currentLamp = lamp;
            var remove = Ui.Button("✕", (_, _) =>
            {
                _lamps.Remove(currentLamp);
                if (_selected == currentLamp) Select(null);
                Render();
            });
            remove.Padding = new Thickness(7, 2, 7, 2);
            actions.Children.Add(remove);
            Grid.SetColumn(actions, 4);
            row.Children.Add(actions);

            row.MouseLeftButtonDown += (_, e) =>
            {
                Select(currentLamp);
                Render();
                e.Handled = true;
            };
            _lampList.Children.Add(row);
        }
    }

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_canvas);
        Lamp? hit = null;
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is Shape { Tag: Lamp lamp } shape)
            {
                var left = Canvas.GetLeft(shape);
                var top = Canvas.GetTop(shape);
                if (position.X >= left && position.X <= left + shape.Width
                    && position.Y >= top && position.Y <= top + shape.Height)
                {
                    hit = lamp;
                    break;
                }
            }
        }

        Select(hit);
        if (hit != null)
        {
            _dragging = true;
            _dragStart = position;
            _dragOrigin = hit.IsRegion ? (hit.RX, hit.RY) : (hit.X, hit.Y);
            _canvas.CaptureMouse();
        }
        Render();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _selected == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(_canvas);
        var dx = (position.X - _dragStart.X) / ViewWidth;
        var dy = (position.Y - _dragStart.Y) / _viewHeight;
        if (_selected.IsRegion)
        {
            _selected.RX = Math.Clamp(_dragOrigin.X + dx, -0.2, 1.0);
            _selected.RY = Math.Clamp(_dragOrigin.Y + dy, -0.2, 1.0);
        }
        else
        {
            _selected.X = Math.Clamp(_dragOrigin.X + dx, 0, 1);
            _selected.Y = Math.Clamp(_dragOrigin.Y + dy, 0, 1);
        }
        Render();
    }

    private void EndDrag()
    {
        if (_dragging)
        {
            _dragging = false;
            _canvas.ReleaseMouseCapture();
        }
    }

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_selected == null) return;
        var factor = e.Delta > 0 ? 1.07 : 1 / 1.07;
        if (_selected.IsRegion)
        {
            _selected.RW = Math.Clamp(_selected.RW * factor, 0.02, 1.4);
            _selected.RH = Math.Clamp(_selected.RH * factor, 0.05, 1.4);
        }
        else
        {
            _selected.Radius = Math.Clamp(_selected.Radius * factor, 0.02, 0.6);
        }
        Render();
        e.Handled = true;
    }

    // ================= inspector =================

    private void Select(Lamp? lamp)
    {
        _selected = lamp;
        _inspector.Children.Clear();
        _inspector.Visibility = lamp == null ? Visibility.Collapsed : Visibility.Visible;
        if (lamp == null) return;

        var line = new WrapPanel();
        var idLabel = Ui.MutedLabel(L.T("Lampe", "Lamp"));
        idLabel.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(idLabel);
        var idBox = Ui.TextBox(lamp.Id, 90);
        idBox.TextChanged += (_, _) =>
        {
            lamp.Id = idBox.Text.Trim();
            Render();
        };
        line.Children.Add(idBox);

        var colorLabel = Ui.MutedLabel(L.T("Couleur", "Color"));
        colorLabel.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(colorLabel);
        var colorBox = Ui.TextBox(lamp.Color, 80);
        var swatch = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ParseColor(lamp.Color)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0)
        };
        colorBox.TextChanged += (_, _) =>
        {
            lamp.Color = colorBox.Text.Trim();
            swatch.Background = new SolidColorBrush(ParseColor(lamp.Color));
            Render();
        };
        line.Children.Add(colorBox);
        line.Children.Add(swatch);

        var outputLabel = Ui.MutedLabel("Output");
        outputLabel.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(outputLabel);
        var output = new ComboBox
        {
            Width = 160,
            FontSize = 12,
            IsEditable = true,
            Margin = new Thickness(0, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        foreach (var known in _knownOutputs)
        {
            output.Items.Add(known);
        }
        // the lamp's CURRENT output pre-selects in the list (free text otherwise)
        output.SelectedItem = _knownOutputs.FirstOrDefault(k => k.Equals(lamp.Output, StringComparison.OrdinalIgnoreCase));
        if (output.SelectedItem == null) output.Text = lamp.Output;
        output.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            lamp.Output = output.Text.Trim();
            Render();
        }));
        output.SelectionChanged += (_, _) =>
        {
            if (output.SelectedItem is string chosen)
            {
                lamp.Output = chosen;
                Render();
            }
        };
        line.Children.Add(output);

        line.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) =>
        {
            _lamps.Remove(lamp);
            Select(null);
            Render();
        }));
        _inspector.Children.Add(line);

        // geometry: shape, position and dimensions in fractions of the marquee
        var line2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var shapeLabel = Ui.MutedLabel(L.T("Forme", "Shape"));
        shapeLabel.Margin = new Thickness(0, 0, 6, 0);
        line2.Children.Add(shapeLabel);
        var shape = Ui.ComboBox(120);
        shape.Items.Add(new ComboBoxItem { Content = L.T("Cercle", "Circle"), Tag = "circle" });
        shape.Items.Add(new ComboBoxItem { Content = "Rectangle", Tag = "region" });
        shape.SelectedIndex = lamp.IsRegion ? 1 : 0;
        shape.SelectionChanged += (_, _) =>
        {
            var wantRegion = (shape.SelectedItem as ComboBoxItem)?.Tag as string == "region";
            if (wantRegion == lamp.IsRegion) return;
            if (wantRegion)
            {
                // circle → rectangle, same center
                lamp.RH = Math.Clamp(lamp.Radius * 2, 0.05, 1.4);
                lamp.RW = Math.Clamp(lamp.Radius * 2 * _viewHeight / ViewWidth, 0.02, 1.4);
                lamp.RX = lamp.X - lamp.RW / 2;
                lamp.RY = lamp.Y - lamp.RH / 2;
            }
            else
            {
                // rectangle → circle, same center
                lamp.X = lamp.RX + lamp.RW / 2;
                lamp.Y = lamp.RY + lamp.RH / 2;
                lamp.Radius = Math.Clamp(lamp.RH / 2, 0.02, 0.6);
            }
            lamp.IsRegion = wantRegion;
            Select(lamp); // rebuild the fields for the new shape
            Render();
        };
        line2.Children.Add(shape);

        void Fraction(string label, Func<double> get, Action<double> set, double min = -0.5, double max = 1.5)
        {
            var text = Ui.MutedLabel(label);
            text.Margin = new Thickness(6, 0, 4, 0);
            line2.Children.Add(text);
            var box = Ui.TextBox(get().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), 56);
            box.TextChanged += (_, _) =>
            {
                if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    set(Math.Clamp(parsed, min, max));
                    Render();
                }
            };
            line2.Children.Add(box);
        }

        if (lamp.IsRegion)
        {
            Fraction("x", () => lamp.RX, v => lamp.RX = v);
            Fraction("y", () => lamp.RY, v => lamp.RY = v);
            Fraction(L.T("largeur", "width"), () => lamp.RW, v => lamp.RW = v, 0.02, 1.4);
            Fraction(L.T("hauteur", "height"), () => lamp.RH, v => lamp.RH = v, 0.05, 1.4);
        }
        else
        {
            Fraction("x", () => lamp.X, v => lamp.X = v, 0, 1);
            Fraction("y", () => lamp.Y, v => lamp.Y = v, 0, 1);
            Fraction(L.T("rayon", "radius"), () => lamp.Radius, v => lamp.Radius = v, 0.02, 0.6);
        }
        line2.Children.Add(Ui.MutedLabel(L.T("(fractions du marquee, 0–1)", "(marquee fractions, 0–1)")));
        _inspector.Children.Add(line2);

        // composer-style size slider (wheel on the canvas does the same)
        var line3 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var sizeLabel = Ui.MutedLabel(L.T("Taille", "Size"));
        sizeLabel.Margin = new Thickness(0, 0, 6, 0);
        line3.Children.Add(sizeLabel);
        var size = new Slider
        {
            Minimum = 0.02,
            Maximum = lamp.IsRegion ? 1.4 : 0.6,
            Value = lamp.IsRegion ? lamp.RH : lamp.Radius,
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center
        };
        size.ValueChanged += (_, args) =>
        {
            if (lamp.IsRegion)
            {
                var ratio = lamp.RH > 0.001 ? lamp.RW / lamp.RH : 0.2;
                lamp.RH = args.NewValue;
                lamp.RW = Math.Clamp(args.NewValue * ratio, 0.02, 1.4);
            }
            else
            {
                lamp.Radius = args.NewValue;
            }
            Render();
        };
        line3.Children.Add(size);
        _inspector.Children.Add(line3);
    }

    // ================= data =================

    /// <summary>MAME output names of the game — APIExpose ships them in
    /// resources\outputs\mame\&lt;rom&gt;.json as an "outputs" ARRAY of
    /// { name, label, physical_type… } objects (the dynpanel files carry none).</summary>
    private void LoadKnownOutputs(string system, string rom)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine(_pluginRoot, "..", "APIExpose", "resources", "outputs"));
            if (!Directory.Exists(root)) return;
            var file = Path.Combine(root, "mame", rom + ".json");
            if (!File.Exists(file))
            {
                file = Directory.EnumerateFiles(root, rom + ".json", SearchOption.AllDirectories).FirstOrDefault() ?? "";
            }
            if (file.Length == 0 || !File.Exists(file)) return;

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("outputs", out var outputs)
                && outputs.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var output in outputs.EnumerateArray())
                {
                    if (output.TryGetProperty("name", out var name)
                        && name.ValueKind == System.Text.Json.JsonValueKind.String
                        && name.GetString() is { Length: > 0 } value)
                    {
                        _knownOutputs.Add(value);
                    }
                }
            }
        }
        catch
        {
            // no outputs file: free-text output only
        }
    }

    private static double Fraction(string value)
        => double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static string Frac(double value)
        => Math.Clamp(value, -2, 2).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    private static BitmapImage? TryLoad(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.DecodePixelWidth = 900;
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
            return Color.FromRgb(0xFF, 0xD9, 0xA0);
        }
    }
}
