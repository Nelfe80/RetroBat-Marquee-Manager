using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// "Mon setup" — the drill-down entry point: the physical plan of the
/// installation (screens laid out where they really are, draggable, absent ones
/// grayed), a display-state selector previewing what each screen shows, and a
/// screen panel opening on click: type-based ZERO-CONFIG ("this is a Marquee" →
/// everything functional), surfaces with their compositions, division templates,
/// test pattern, identification, DMD/touch access.
/// </summary>
public sealed class MonSetupView : UserControl
{
    private static readonly (string Key, string Fr, string En)[] ScreenTypes =
    {
        ("game", "Écran de jeu (RetroBat)", "Game screen (RetroBat)"),
        ("marquee", "Marquee", "Marquee"),
        ("topper", "Topper", "Topper"),
        ("iccard", "Instruction card", "Instruction card"),
        ("dmd", "DMD virtuel", "Virtual DMD"),
        ("mixed-vertical", "Vertical mixte (marquee + jeu + IC)", "Mixed vertical (marquee + game + IC)"),
        ("custom", "Libre", "Custom")
    };

    private static readonly Dictionary<string, Color> CategoryColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["marquee"] = Color.FromRgb(0xFF, 0xB3, 0x00),
        ["topper"] = Color.FromRgb(0x8A, 0x2B, 0xE2),
        ["iccard"] = Color.FromRgb(0x20, 0xE8, 0xE8),
        ["dmd-virtual"] = Color.FromRgb(0xE8, 0x5C, 0x5C),
        ["lcd"] = Color.FromRgb(0x30, 0x60, 0xE8),
        ["custom"] = Color.FromRgb(0x4C, 0xC9, 0x6E)
    };

    private readonly string _pluginRoot;
    private readonly SurfacesStore _store;
    private readonly List<SurfaceModel> _surfaces;
    private readonly List<ScreenModel> _plan;
    private readonly IReadOnlyList<ScreenInfo> _detected;

    private readonly Canvas _map = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly StackPanel _screenPanel = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private string _previewState = "navigation";
    private ScreenModel? _selected;
    private ScreenModel? _dragging;
    private bool _dragMoved;
    private Point _dragStart;
    private (double X, double Y) _dragOrigin;
    private double _scale = 0.06;
    private readonly (bool Present, string Model, int W, int H) _physicalDmd;

    public MonSetupView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _store = new SurfacesStore(pluginRoot);
        _surfaces = _store.Load();
        _detected = ScreenProbe.Detect();
        _plan = ReconcilePlan(_store.LoadScreens());

        // a configured physical DMD joins the plan as a screen-like node
        try
        {
            var ini = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));
            var enabled = ini.Get("DMD", "Enabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
            var model = ini.Get("DMD", "Model", "");
            _physicalDmd = (enabled && model.Length > 0, model,
                int.TryParse(ini.Get("DMD", "Width", "128"), out var dmdW) ? dmdW : 128,
                int.TryParse(ini.Get("DMD", "Height", "32"), out var dmdH) ? dmdH : 32);
        }
        catch
        {
            _physicalDmd = (false, "", 128, 32);
        }

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mon setup", "My setup")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Votre installation vue d'en haut : disposez les écrans comme ils le sont physiquement, "
            + "puis cliquez sur un écran pour le configurer. Choisir son type suffit — tout devient fonctionnel.",
            "Your installation from above: lay the screens out as they physically are, "
            + "then click one to configure it. Picking its type is enough — everything turns functional.")));

        var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Ui.Button(L.T("Identifier les écrans", "Identify screens"), (_, _) => IdentifyWindow.ShowAll(_detected)));
        var stateLabel = Ui.MutedLabel(L.T("Aperçu de l'état :", "Preview state:"));
        stateLabel.Margin = new Thickness(10, 0, 6, 0);
        bar.Children.Add(stateLabel);
        var statePicker = Ui.ComboBox(160);
        statePicker.Items.Add(new ComboBoxItem { Content = L.T("Navigation ES", "ES browsing"), Tag = "navigation" });
        statePicker.Items.Add(new ComboBoxItem { Content = L.T("En jeu", "Ingame"), Tag = "ingame" });
        statePicker.SelectedIndex = 0;
        statePicker.SelectionChanged += (_, _) =>
        {
            if ((statePicker.SelectedItem as ComboBoxItem)?.Tag is string state)
            {
                _previewState = state;
                RenderMap();
                RenderScreenPanel();
            }
        };
        bar.Children.Add(statePicker);
        bar.Children.Add(Ui.Button(L.T("Enregistrer le plan", "Save the plan"), (_, _) => SaveAll(), primary: true));
        page.Children.Add(bar);

        _map.Height = 360;
        _map.MouseLeftButtonDown += Map_MouseDown;
        _map.MouseMove += Map_MouseMove;
        _map.MouseLeftButtonUp += (_, _) => EndDrag();
        _map.MouseLeave += (_, _) => EndDrag();
        page.Children.Add(new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Child = _map
        });
        page.Children.Add(Ui.MutedLabel(L.T(
            "Glissez les écrans pour refléter leur position physique (base des futures animations traversantes). Écrans absents en gris.",
            "Drag the screens to mirror their physical layout (basis of future cross-screen animations). Absent screens in gray.")));

        page.Children.Add(_screenPanel);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);
        Content = Ui.Page(page);

        SizeChanged += (_, _) => RenderMap();
        RenderMap();
    }

    // ================= plan model =================

    /// <summary>Merges the saved plan with the live Windows screens: known screens
    /// keep their physical position, new ones are appended around the primary,
    /// missing ones stay (grayed). Windows indexes are recomputed.</summary>
    private List<ScreenModel> ReconcilePlan(List<ScreenModel> saved)
    {
        var plan = new List<ScreenModel>(saved);
        foreach (var screen in plan) screen.Connected = false;

        for (var i = 0; i < _detected.Count; i++)
        {
            var info = _detected[i];
            var known = plan.FirstOrDefault(s => s.Id.Equals(info.DeviceName, StringComparison.OrdinalIgnoreCase));
            if (known == null)
            {
                known = new ScreenModel
                {
                    Id = info.DeviceName,
                    Name = info.Primary ? L.T("Écran RetroBat", "RetroBat screen") : $"{L.T("Écran", "Screen")} {i}",
                    // physical default = the Windows layout (a sensible seed)
                    PhysicalX = info.Bounds.X,
                    PhysicalY = info.Bounds.Y,
                    Usage = info.Primary ? "game" : ""
                };
                plan.Add(known);
            }
            known.WindowsIndex = i;
            known.Connected = true;
        }
        return plan;
    }

    private (int W, int H) SizeOf(ScreenModel screen)
    {
        if (screen.Connected && screen.WindowsIndex >= 0 && screen.WindowsIndex < _detected.Count)
        {
            var bounds = _detected[screen.WindowsIndex].Bounds;
            return (bounds.Width, bounds.Height);
        }
        return (1920, 1080);
    }

    private IEnumerable<SurfaceModel> SurfacesOf(ScreenModel screen)
        => screen.WindowsIndex < 0
            ? Enumerable.Empty<SurfaceModel>()
            : _surfaces.Where(s => s.Screens.Contains(screen.WindowsIndex));

    // ================= map rendering =================

    private void RenderMap()
    {
        _map.Children.Clear();
        if (_plan.Count == 0) return;

        // fit: bounding box of physical positions
        var minX = _plan.Min(s => s.PhysicalX);
        var minY = _plan.Min(s => s.PhysicalY);
        var maxX = _plan.Max(s => s.PhysicalX + SizeOf(s).W);
        var maxY = _plan.Max(s => s.PhysicalY + SizeOf(s).H);
        var width = Math.Max(400, ActualWidth - 90);
        _map.Width = width;
        _scale = Math.Min(width / Math.Max(1, maxX - minX + 400), _map.Height / Math.Max(1, maxY - minY + 400));

        foreach (var screen in _plan)
        {
            var (w, h) = SizeOf(screen);
            var card = new Border
            {
                Width = Math.Max(30, w * _scale),
                Height = Math.Max(20, h * _scale),
                Background = Ui.Brush(Color.FromRgb(0x1A, 0x1A, 0x26)),
                BorderBrush = ReferenceEquals(screen, _selected) ? Ui.Accent
                    : screen.Connected ? Ui.Brush(Color.FromRgb(0x3A, 0x3A, 0x52)) : Ui.Muted,
                BorderThickness = new Thickness(ReferenceEquals(screen, _selected) ? 2.5 : 1.5),
                CornerRadius = new CornerRadius(4),
                Opacity = screen.Connected ? 1.0 : 0.45,
                Tag = screen,
                Cursor = Cursors.Hand
            };

            // thumbnail: the screen's surfaces in the previewed display state —
            // a surface scoped to the other state vanishes, exactly like at runtime
            var thumb = new Canvas { ClipToBounds = true };
            foreach (var surface in SurfacesOf(screen))
            {
                if (!surface.ActiveIn(_previewState)) continue;
                var color = CategoryColors.TryGetValue(surface.Category, out var c) ? c : Colors.Gray;
                var activeCount = surface.Components.Count(comp => comp.Visible
                    && (comp.When == "both" || comp.When.Equals(_previewState, StringComparison.OrdinalIgnoreCase)));
                var rect = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(activeCount > 0 ? 0x66 : 0x22), color.R, color.G, color.B)),
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = 1,
                    RadiusX = 2,
                    RadiusY = 2,
                    IsHitTestVisible = false
                };
                var sx = surface.IsFullscreen ? 0 : (double)(surface.X ?? 0) / w;
                var sy = surface.IsFullscreen ? 0 : (double)(surface.Y ?? 0) / h;
                var sw = surface.IsFullscreen ? 1 : (double)(surface.Width ?? w) / w;
                var sh = surface.IsFullscreen ? 1 : (double)(surface.Height ?? h) / h;
                rect.Width = Math.Max(4, sw * card.Width - 4);
                rect.Height = Math.Max(4, sh * card.Height - 4);
                Canvas.SetLeft(rect, sx * card.Width + 2);
                Canvas.SetTop(rect, sy * card.Height + 2);
                thumb.Children.Add(rect);
            }
            var label = new TextBlock
            {
                Text = screen.Name + (screen.Connected ? "" : L.T(" (absent)", " (absent)")),
                Foreground = Ui.Foreground,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 0 }
            };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, 2);
            thumb.Children.Add(label);
            card.Child = thumb;

            Canvas.SetLeft(card, (screen.PhysicalX - minX + 200) * _scale);
            Canvas.SetTop(card, (screen.PhysicalY - minY + 200) * _scale);
            _map.Children.Add(card);
        }

        // physical DMD node: same visual language, click = its settings.
        // Rendered at 4× its real pixels — a 128×32 panel would be invisible.
        if (_physicalDmd.Present)
        {
            var color = CategoryColors["dmd-virtual"];
            var node = new Border
            {
                Width = Math.Max(40, _physicalDmd.W * 4 * _scale),
                Height = Math.Max(16, _physicalDmd.H * 4 * _scale),
                Background = Ui.Brush(Color.FromRgb(0x1A, 0x12, 0x12)),
                BorderBrush = new SolidColorBrush(color),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(4),
                Tag = "dmd",
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = $"DMD {_physicalDmd.Model} · {_physicalDmd.W}×{_physicalDmd.H}",
                    Foreground = new SolidColorBrush(color),
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                }
            };
            Canvas.SetLeft(node, (maxX - minX + 240) * _scale);
            Canvas.SetTop(node, 200 * _scale);
            _map.Children.Add(node);
        }
    }

    private void Map_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(_map);
        var hit = _map.Children.OfType<Border>().Reverse()
            .FirstOrDefault(card => position.X >= Canvas.GetLeft(card) && position.X <= Canvas.GetLeft(card) + card.Width
                                    && position.Y >= Canvas.GetTop(card) && position.Y <= Canvas.GetTop(card) + card.Height);
        if (hit?.Tag is string toolTag)
        {
            // pseudo-screens (physical DMD node) open their settings directly
            if (toolTag == "dmd")
            {
                OpenToolWindow(L.T("DMD physique", "Physical DMD"), new DmdView(_pluginRoot));
            }
            e.Handled = true;
            return;
        }
        if (hit?.Tag is not ScreenModel screen) return;

        _selected = screen;
        _dragging = screen;
        _dragStart = position;
        _dragOrigin = (screen.PhysicalX, screen.PhysicalY);
        _dragMoved = false;
        _map.CaptureMouse();
        RenderMap();
        RenderScreenPanel();
        e.Handled = true;
    }

    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(_map);
        if (Math.Abs(position.X - _dragStart.X) + Math.Abs(position.Y - _dragStart.Y) > 4) _dragMoved = true;
        // snap the physical plan to a 40 px grid — tidy layouts by default
        _dragging.PhysicalX = Math.Round((_dragOrigin.X + (position.X - _dragStart.X) / _scale) / 40) * 40;
        _dragging.PhysicalY = Math.Round((_dragOrigin.Y + (position.Y - _dragStart.Y) / _scale) / 40) * 40;
        RenderMap();
    }

    private void EndDrag()
    {
        if (_dragging == null) return;
        var clicked = !_dragMoved ? _dragging : null;
        _dragging = null;
        _map.ReleaseMouseCapture();

        // a plain CLICK (no drag) drills straight into the surfaces editor —
        // including the RetroBat screen (add/remove surfaces with the ES warning)
        if (clicked is { Connected: true })
        {
            OpenDivision(clicked);
        }
    }

    // ================= screen drill-down panel =================

    private void RenderScreenPanel()
    {
        _screenPanel.Children.Clear();
        if (_selected == null) return;
        var screen = _selected;
        var (w, h) = SizeOf(screen);

        var card = new StackPanel();
        var title = Ui.Label($"{screen.Name} — {w}×{h}"
                             + (screen.Connected ? $"  ·  {L.T("écran Windows", "Windows screen")} {screen.WindowsIndex}" : L.T("  ·  absent", "  ·  absent")), 15);
        title.FontWeight = FontWeights.Bold;
        card.Children.Add(title);

        // rename + identify + test pattern
        var tools = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var name = Ui.TextBox(screen.Name, 180);
        name.TextChanged += (_, _) => screen.Name = name.Text.Trim();
        tools.Children.Add(name);
        if (screen.Connected && screen.WindowsIndex >= 0 && screen.WindowsIndex < _detected.Count)
        {
            var info = _detected[screen.WindowsIndex];
            tools.Children.Add(Ui.Button(L.T("Afficher la mire", "Show test pattern"), (_, _) =>
                new TestPatternWindow($"{screen.Name}", info.Bounds.X, info.Bounds.Y, info.Bounds.Width, info.Bounds.Height).Show()));
            if (info.Touch == TouchSupport.Touch)
            {
                tools.Children.Add(Ui.Button(L.T("Tactile (IC card)…", "Touch (IC card)…"), (_, _) =>
                    OpenToolWindow(L.T("IC card tactile", "Touch IC card"), new TouchView(_pluginRoot))));
            }
        }
        card.Children.Add(tools);

        // ZERO-CONFIG type
        var typeRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var typeLabel = Ui.MutedLabel(L.T("Type de cet écran :", "This screen's type:"));
        typeLabel.Margin = new Thickness(0, 0, 6, 0);
        typeRow.Children.Add(typeLabel);
        var typePicker = Ui.ComboBox(280);
        foreach (var (key, fr, en) in ScreenTypes)
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            typePicker.Items.Add(item);
            if (key.Equals(screen.Usage, StringComparison.OrdinalIgnoreCase)) typePicker.SelectedItem = item;
        }
        if (typePicker.SelectedItem == null) typePicker.SelectedIndex = SuggestTypeIndex(screen);
        typeRow.Children.Add(typePicker);
        typeRow.Children.Add(Ui.Button(L.T("Appliquer le type (tout configurer)", "Apply type (configure everything)"), (_, _) =>
        {
            if ((typePicker.SelectedItem as ComboBoxItem)?.Tag is string type)
            {
                ApplyScreenType(screen, type);
            }
        }, primary: true));
        card.Children.Add(typeRow);
        card.Children.Add(Ui.MutedLabel(L.T(
            "Choisir un type pose surface(s), composants et flux par défaut — fonctionnel immédiatement, retouchable ensuite.",
            "Picking a type lays default surface(s), components and streams — functional at once, tweakable after.")));

        // surfaces of this screen
        card.Children.Add(Ui.SectionHeader(L.T("Surfaces de cet écran", "This screen's surfaces")));
        var hosted = SurfacesOf(screen).ToList();
        if (hosted.Count == 0)
        {
            card.Children.Add(Ui.MutedLabel(L.T("Aucune surface — appliquez un type ci-dessus.", "No surface — apply a type above.")));
        }
        foreach (var surface in hosted)
        {
            var row = new WrapPanel { Margin = new Thickness(8, 2, 0, 2) };
            var color = CategoryColors.TryGetValue(surface.Category, out var c) ? c : Colors.Gray;
            row.Children.Add(new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(color), Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var whenTag = surface.When.ToLowerInvariant() switch
            {
                "navigation" => L.T(" · Navigation ES seulement", " · ES browsing only"),
                "ingame" => L.T(" · En jeu seulement", " · Ingame only"),
                _ => ""
            };
            var info = Ui.Label($"{surface.Id} ({surface.Category})"
                                + (surface.IsFullscreen ? L.T(" · plein écran", " · fullscreen") : $" · {surface.Width}×{surface.Height} @ {surface.X},{surface.Y}")
                                + whenTag, 12);
            info.Width = 320;
            row.Children.Add(info);
            var activeCount = surface.Components.Count(comp => comp.Visible
                && (comp.When == "both" || comp.When.Equals(_previewState, StringComparison.OrdinalIgnoreCase)));
            row.Children.Add(Ui.MutedLabel(L.T($"{activeCount} composant(s) en {StateLabel()}", $"{activeCount} component(s) in {StateLabel()}")));
            row.Children.Add(Ui.Button(L.T("Composer", "Compose"), (_, _) => OpenComposition(surface)));
            card.Children.Add(row);
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (screen.Connected)
        {
            actions.Children.Add(Ui.Button(L.T("Éditer les surfaces de cet écran", "Edit this screen's surfaces"), (_, _) => OpenDivision(screen)));
        }
        if (!screen.Connected)
        {
            actions.Children.Add(Ui.Button(L.T("Oublier cet écran", "Forget this screen"), (_, _) =>
            {
                _plan.Remove(screen);
                _selected = null;
                RenderMap();
                RenderScreenPanel();
            }));
        }
        card.Children.Add(actions);
        _screenPanel.Children.Add(Ui.Card(card));
    }

    private string StateLabel() => _previewState == "ingame" ? L.T("jeu", "game") : "navigation";

    private int SuggestTypeIndex(ScreenModel screen)
    {
        if (!screen.Connected || screen.WindowsIndex < 0 || screen.WindowsIndex >= _detected.Count) return ScreenTypes.Length - 1;
        var info = _detected[screen.WindowsIndex];
        if (info.Primary) return 0;
        var suggestion = info.Suggestion.ToLowerInvariant();
        for (var i = 0; i < ScreenTypes.Length; i++)
        {
            if (suggestion.Contains(ScreenTypes[i].Key)) return i;
        }
        if (info.Ratio > 3) return 1;                       // banner → marquee
        if (info.Orientation.Contains("portrait", StringComparison.OrdinalIgnoreCase)) return 5; // vertical → mixed
        return ScreenTypes.Length - 1;
    }

    /// <summary>ZERO-CONFIG: replaces this screen's surfaces with the type's
    /// functional defaults (streams, components, touch) and saves.</summary>
    private void ApplyScreenType(ScreenModel screen, string type)
    {
        if (screen.WindowsIndex < 0)
        {
            _status.Text = L.T("Cet écran est absent — rebranchez-le d'abord.", "This screen is absent — reconnect it first.");
            _status.Foreground = Ui.Error;
            return;
        }

        screen.Usage = type;
        var (w, h) = SizeOf(screen);
        SurfacesStore.ProvisionScreenType(_surfaces, screen.WindowsIndex, w, h, type);

        SaveAll();
        _status.Text = L.T($"Type « {type} » appliqué — l'écran est fonctionnel. Retouchez les surfaces si besoin.",
            $"Type “{type}” applied — the screen is functional. Tweak the surfaces if needed.");
        _status.Foreground = Ui.Ok;
        RenderMap();
        RenderScreenPanel();
    }

    private void OpenDivision(ScreenModel screen)
    {
        if (screen.WindowsIndex < 0 || screen.WindowsIndex >= _detected.Count) return;
        // the editor adds/removes surfaces itself — an empty screen is fine,
        // and the RetroBat screen opens with the ES masking warning
        var hostsGame = screen.Usage.Equals("game", StringComparison.OrdinalIgnoreCase)
                        || _detected[screen.WindowsIndex].Primary;
        var editor = new ScreenCompositor(screen.WindowsIndex, _detected[screen.WindowsIndex], _surfaces,
            SurfacesOf(screen).FirstOrDefault(), hostsGame)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() == true)
        {
            SaveAll();
        }
        RenderMap();
        RenderScreenPanel();
    }

    private void OpenComposition(SurfaceModel surface)
    {
        var editor = new CompositionEditor(_pluginRoot, surface, ScreenAspect(surface), _previewState)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() == true)
        {
            SaveAll();
            RenderMap();
            RenderScreenPanel();
        }
    }

    private double ScreenAspect(SurfaceModel surface)
    {
        if (surface is { Width: > 0, Height: > 0 })
            return (double)surface.Width.Value / surface.Height.Value;
        var index = surface.Screens.Count > 0 ? surface.Screens[0] : -1;
        if (index >= 0 && index < _detected.Count && _detected[index].Bounds.Height > 0)
            return (double)_detected[index].Bounds.Width / _detected[index].Bounds.Height;
        return 4.0;
    }

    private void OpenToolWindow(string title, UserControl content)
    {
        var window = new Window
        {
            Title = title,
            Width = 1000,
            Height = 700,
            Content = content,
            Background = Ui.Background,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }

    private void SaveAll()
    {
        _store.Save(_surfaces, _plan);
        if (MarqueeManagerProcess.IsRunning()
            && MessageBox.Show(
                L.T("Redémarrer MarqueeManager avec cette configuration ?", "Restart MarqueeManager with this configuration?"),
                "MarqueeManagerSetup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            MarqueeManagerProcess.Stop();
            MarqueeManagerProcess.Start(_pluginRoot);
        }
    }
}
