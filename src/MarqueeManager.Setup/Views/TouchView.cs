using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.TouchProfile;
using TouchAction = MarqueeManager.Setup.TouchProfile.TouchAction;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Touch configuration for the instruction card: simple / center-toggle /
/// dual-player / free zones, with a live preview of the tap zones. Produces
/// state\surfaces.profile.json, consumed by the runtime's touch support.
/// </summary>
public sealed class TouchView : UserControl
{
    private static readonly (string Value, string Display)[] Modes =
    {
        ("simple", "Simple — un tap passe à la carte suivante"),
        ("center-toggle", "Centre → IC2 — un appui au centre affiche la carte secondaire"),
        ("dual-player", "Dual player — chaque moitié affiche la carte de son joueur"),
        ("zones", "Zones libres — dessinez vos propres zones tactiles")
    };

    private static readonly (string Value, string Display)[] Actions =
    {
        ("cycle-card", "Carte suivante"),
        ("show-card", "Afficher une carte précise"),
        ("show-player-card", "Carte du joueur"),
        ("default-card", "Retour à la carte par défaut")
    };

    private readonly string _pluginRoot;
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly CheckBox _enabled;
    private readonly ComboBox _mode;
    private readonly TextBox _returnMs;
    private readonly TextBox _centerCard;
    private readonly TextBox _centerDurationMs;
    private readonly Slider _centerWidth;
    private readonly TextBlock _centerWidthLabel = Ui.MutedLabel("");
    private readonly CheckBox _dualCenter;
    private readonly StackPanel _simplePanel = new();
    private readonly StackPanel _centerPanel = new();
    private readonly StackPanel _dualPanel = new();
    private readonly StackPanel _zonesPanel = new();
    private readonly Canvas _preview;
    private readonly Border _previewBorder;
    private readonly ListBox _zoneList;
    private readonly TextBox _zoneCard;
    private readonly ComboBox _zoneAction;
    private readonly ComboBox _zonePlayer;
    private readonly TextBox _zoneDurationMs;
    private readonly List<TouchZone> _freeZones = new();
    private System.Windows.Point? _dragStart;
    private Rectangle? _dragRect;
    private bool _updatingZoneEditor;

    public TouchView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        var profile = TouchProfileDocument.LoadOrNew(PluginPaths.TouchProfilePath(pluginRoot));
        var touch = profile.Surface("iccard")?.Touch;

        var page = new StackPanel();
        page.Children.Add(Ui.Title("Instruction card tactile"));
        page.Children.Add(Ui.Subtitle(
            "Rend l'écran instruction card interactif : taper l'écran change de carte (how-to-play, moves, "
            + "carte par joueur…). Le réglage est écrit dans state\\surfaces.profile.json et lu par MarqueeManager "
            + "au démarrage. La souris fonctionne comme le tactile pour tester sans écran tactile."));

        _enabled = Ui.CheckBox("Activer le tactile sur l'instruction card", touch?.Enabled == true);
        page.Children.Add(_enabled);

        _mode = Ui.ComboBox(420);
        foreach (var (value, display) in Modes)
        {
            _mode.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }

        var modeIndex = Array.FindIndex(Modes, m => m.Value.Equals(touch?.Mode ?? "simple", StringComparison.OrdinalIgnoreCase));
        _mode.SelectedIndex = modeIndex >= 0 ? modeIndex : 0;
        _mode.SelectionChanged += (_, _) => SyncModePanels();
        page.Children.Add(Ui.Row("Mode", _mode));

        _returnMs = Ui.TextBox((touch?.ReturnToDefaultMs ?? 0).ToString(), 80);
        page.Children.Add(Ui.Row("Retour carte par défaut (ms)", _returnMs, "0 = rester sur la carte affichée"));

        // --- per-mode panels ---
        _simplePanel.Children.Add(Ui.MutedLabel(
            "Un tap n'importe où sur l'écran affiche la carte suivante du jeu (ic1 → ic2 → …)."));

        _centerWidth = new Slider
        {
            Minimum = 10, Maximum = 50, Value = 16, Width = 180, TickFrequency = 2, IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _centerWidth.ValueChanged += (_, _) =>
        {
            _centerWidthLabel.Text = $"{(int)_centerWidth.Value} % de la largeur";
            RefreshPreview();
        };
        var widthLine = new StackPanel { Orientation = Orientation.Horizontal };
        widthLine.Children.Add(_centerWidth);
        widthLine.Children.Add(_centerWidthLabel);
        _centerPanel.Children.Add(Ui.Row("Largeur de la zone centrale", widthLine));
        _centerCard = Ui.TextBox("ic2", 100);
        _centerPanel.Children.Add(Ui.Row("Carte affichée au centre", _centerCard, "ic2 = deuxième instruction card"));
        _centerDurationMs = Ui.TextBox("8000", 80);
        _centerPanel.Children.Add(Ui.Row("Durée d'affichage (ms)", _centerDurationMs, "retour auto à la carte principale"));

        _dualCenter = Ui.CheckBox("Zone centrale commune (règles générales / move list commune)", false);
        _dualCenter.Checked += (_, _) => RefreshPreview();
        _dualCenter.Unchecked += (_, _) => RefreshPreview();
        _dualPanel.Children.Add(Ui.MutedLabel(
            "Moitié gauche = carte du joueur 1, moitié droite = carte du joueur 2 (VS fighting)."));
        _dualPanel.Children.Add(_dualCenter);

        _zonesPanel.Children.Add(Ui.MutedLabel(
            "Dessinez une zone directement sur l'aperçu (cliquer-glisser), puis choisissez son action ci-dessous."));
        _zoneList = new ListBox
        {
            Height = 110,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x18)),
            Foreground = Ui.Foreground,
            BorderBrush = Ui.PanelBorder,
            FontSize = 12
        };
        _zoneList.SelectionChanged += (_, _) =>
        {
            LoadZoneEditor();
            RefreshPreview();
        };
        _zonesPanel.Children.Add(_zoneList);

        _zoneAction = Ui.ComboBox(240);
        foreach (var (value, display) in Actions)
        {
            _zoneAction.Items.Add(new ComboBoxItem { Content = display, Tag = value });
        }

        _zoneAction.SelectionChanged += (_, _) => ApplyZoneEditor();
        _zoneCard = Ui.TextBox("", 100);
        _zoneCard.TextChanged += (_, _) => ApplyZoneEditor();
        _zonePlayer = Ui.ComboBox(80);
        _zonePlayer.Items.Add(new ComboBoxItem { Content = "1", Tag = 1 });
        _zonePlayer.Items.Add(new ComboBoxItem { Content = "2", Tag = 2 });
        _zonePlayer.SelectionChanged += (_, _) => ApplyZoneEditor();
        _zoneDurationMs = Ui.TextBox("", 80);
        _zoneDurationMs.TextChanged += (_, _) => ApplyZoneEditor();

        _zonesPanel.Children.Add(Ui.Row("Action au tap", _zoneAction));
        _zonesPanel.Children.Add(Ui.Row("Carte (si « afficher »)", _zoneCard, "ex. ic2, moves"));
        _zonesPanel.Children.Add(Ui.Row("Joueur (si carte joueur)", _zonePlayer));
        _zonesPanel.Children.Add(Ui.Row("Durée (ms, vide = permanent)", _zoneDurationMs));
        var zoneButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        zoneButtons.Children.Add(Ui.Button("Supprimer la zone", (_, _) => DeleteSelectedZone()));
        zoneButtons.Children.Add(Ui.Button("Tout effacer", (_, _) =>
        {
            _freeZones.Clear();
            RefreshZoneList();
            RefreshPreview();
        }));
        _zonesPanel.Children.Add(zoneButtons);

        var modeCard = new StackPanel();
        modeCard.Children.Add(_simplePanel);
        modeCard.Children.Add(_centerPanel);
        modeCard.Children.Add(_dualPanel);
        modeCard.Children.Add(_zonesPanel);
        page.Children.Add(Ui.Card(modeCard));

        // --- preview ---
        page.Children.Add(Ui.SectionHeader("Aperçu des zones tactiles"));
        _preview = new Canvas
        {
            Width = 560,
            Height = 200,
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0C, 0x14))
        };
        _preview.MouseLeftButtonDown += Preview_MouseDown;
        _preview.MouseMove += Preview_MouseMove;
        _preview.MouseLeftButtonUp += Preview_MouseUp;
        _previewBorder = new Border
        {
            Child = _preview,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10)
        };
        page.Children.Add(_previewBorder);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
        actions.Children.Add(Ui.Button("Enregistrer le profil tactile", OnSave, primary: true));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);

        // restore saved state
        if (touch != null)
        {
            RestoreFromProfile(touch);
        }

        AdjustPreviewRatio();
        SyncModePanels();
    }

    private string SelectedMode => (string)((ComboBoxItem)_mode.SelectedItem).Tag;

    private TouchZone? SelectedZone
        => _zoneList.SelectedIndex >= 0 && _zoneList.SelectedIndex < _freeZones.Count
            ? _freeZones[_zoneList.SelectedIndex]
            : null;

    /// <summary>Preview keeps the aspect ratio of the configured iccard zone (or screen).</summary>
    private void AdjustPreviewRatio()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        double ratio = 16.0 / 9.0;
        var bounds = ini.Get("Screens", "IcCardBounds", "");
        var parts = bounds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4
            && int.TryParse(parts[2], out var w) && int.TryParse(parts[3], out var h)
            && w > 0 && h > 0)
        {
            ratio = (double)w / h;
        }
        else if (int.TryParse(ini.Get("Screens", "IcCardScreen", "-1"), out var screenIndex) && screenIndex >= 0)
        {
            var screens = Detection.ScreenProbe.Detect();
            if (screenIndex < screens.Count)
            {
                ratio = Math.Max(0.2, (double)screens[screenIndex].Bounds.Width / screens[screenIndex].Bounds.Height);
            }
        }

        _preview.Width = 560;
        _preview.Height = Math.Clamp(560 / ratio, 90, 420);
    }

    private void RestoreFromProfile(TouchSettings touch)
    {
        switch (touch.Mode)
        {
            case "center-toggle":
            {
                var center = touch.Zones.FirstOrDefault(z => z.Id == "center");
                if (center != null && center.TryGetFractions(out _, out _, out var w, out _))
                {
                    _centerWidth.Value = Math.Clamp(w * 100, _centerWidth.Minimum, _centerWidth.Maximum);
                }

                _centerCard.Text = center?.Tap?.Card ?? "ic2";
                _centerDurationMs.Text = (center?.Tap?.DurationMs ?? 8000).ToString();
                break;
            }
            case "dual-player":
                _dualCenter.IsChecked = touch.Zones.Any(z => z.Id == "center");
                break;
            case "zones":
                _freeZones.AddRange(touch.Zones);
                RefreshZoneList();
                break;
        }
    }

    private void SyncModePanels()
    {
        _simplePanel.Visibility = SelectedMode == "simple" ? Visibility.Visible : Visibility.Collapsed;
        _centerPanel.Visibility = SelectedMode == "center-toggle" ? Visibility.Visible : Visibility.Collapsed;
        _dualPanel.Visibility = SelectedMode == "dual-player" ? Visibility.Visible : Visibility.Collapsed;
        _zonesPanel.Visibility = SelectedMode == "zones" ? Visibility.Visible : Visibility.Collapsed;
        _centerWidthLabel.Text = $"{(int)_centerWidth.Value} % de la largeur";
        RefreshPreview();
    }

    /// <summary>The zones the current settings would produce, in hit-test order.</summary>
    private List<TouchZone> BuildZones()
    {
        switch (SelectedMode)
        {
            case "simple":
                return new List<TouchZone>
                {
                    new()
                    {
                        Id = "all", Label = "Carte suivante", Rect = "0,0,100%,100%",
                        Tap = new TouchAction { Action = "cycle-card" }
                    }
                };
            case "center-toggle":
            {
                var width = (int)_centerWidth.Value;
                var x = Math.Max(0, (100 - width) / 2);
                int.TryParse(_centerDurationMs.Text.Trim(), out var duration);
                return new List<TouchZone>
                {
                    new()
                    {
                        Id = "center", Label = "IC2", Rect = $"{x},0,{width}%,100%",
                        Tap = new TouchAction
                        {
                            Action = "show-card",
                            Card = string.IsNullOrWhiteSpace(_centerCard.Text) ? "ic2" : _centerCard.Text.Trim(),
                            DurationMs = duration > 0 ? duration : 8000
                        }
                    },
                    new()
                    {
                        Id = "default", Label = "IC principale", Rect = "0,0,100%,100%",
                        Tap = new TouchAction { Action = "default-card" }
                    }
                };
            }
            case "dual-player":
            {
                var zones = new List<TouchZone>();
                if (_dualCenter.IsChecked == true)
                {
                    zones.Add(new TouchZone
                    {
                        Id = "center", Label = "Commun", Rect = "42,0,16%,100%",
                        Tap = new TouchAction { Action = "show-card", Card = "ic2", DurationMs = 8000 }
                    });
                }

                zones.Add(new TouchZone
                {
                    Id = "p1", Label = "Joueur 1", Rect = "0,0,50%,100%",
                    Tap = new TouchAction { Action = "show-player-card", Player = 1 }
                });
                zones.Add(new TouchZone
                {
                    Id = "p2", Label = "Joueur 2", Rect = "50,0,50%,100%",
                    Tap = new TouchAction { Action = "show-player-card", Player = 2 }
                });
                return zones;
            }
            default:
                return _freeZones.ToList();
        }
    }

    private void RefreshPreview()
    {
        if (_preview == null)
        {
            return;
        }

        _preview.Children.Clear();
        var zones = BuildZones();
        var selected = SelectedZone;
        var palette = new[]
        {
            Color.FromRgb(0xFF, 0xB3, 0x00), Color.FromRgb(0x4C, 0xC9, 0x6E),
            Color.FromRgb(0x5C, 0x9C, 0xE8), Color.FromRgb(0xE8, 0x5C, 0x5C),
            Color.FromRgb(0xB0, 0x6C, 0xE8), Color.FromRgb(0x4C, 0xC9, 0xC9)
        };

        for (var i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!zone.TryGetFractions(out var x, out var y, out var w, out var h))
            {
                continue;
            }

            var color = palette[i % palette.Length];
            var isSelected = SelectedMode == "zones" && ReferenceEquals(zone, selected);
            var rect = new Rectangle
            {
                Width = Math.Max(2, w * _preview.Width),
                Height = Math.Max(2, h * _preview.Height),
                Fill = new SolidColorBrush(Color.FromArgb(70, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color),
                StrokeThickness = isSelected ? 3 : 1.5
            };
            Canvas.SetLeft(rect, x * _preview.Width);
            Canvas.SetTop(rect, y * _preview.Height);
            _preview.Children.Add(rect);

            var label = new TextBlock
            {
                Text = zone.Label ?? zone.Id,
                Foreground = new SolidColorBrush(color),
                FontSize = 11,
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(label, x * _preview.Width + 6);
            Canvas.SetTop(label, y * _preview.Height + 4);
            _preview.Children.Add(label);
        }
    }

    // --- free zone drawing ---

    private void Preview_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SelectedMode != "zones")
        {
            return;
        }

        var pos = e.GetPosition(_preview);

        // click inside an existing zone selects it (drawing starts on empty space)
        for (var i = _freeZones.Count - 1; i >= 0; i--)
        {
            if (_freeZones[i].TryGetFractions(out var x, out var y, out var w, out var h)
                && pos.X >= x * _preview.Width && pos.X <= (x + w) * _preview.Width
                && pos.Y >= y * _preview.Height && pos.Y <= (y + h) * _preview.Height)
            {
                _zoneList.SelectedIndex = i;
                return;
            }
        }

        _dragStart = pos;
        _dragRect = new Rectangle
        {
            Stroke = Ui.Accent,
            StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xB3, 0x00))
        };
        Canvas.SetLeft(_dragRect, pos.X);
        Canvas.SetTop(_dragRect, pos.Y);
        _preview.Children.Add(_dragRect);
        _preview.CaptureMouse();
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart == null || _dragRect == null)
        {
            return;
        }

        var pos = e.GetPosition(_preview);
        var x = Math.Clamp(Math.Min(pos.X, _dragStart.Value.X), 0, _preview.Width);
        var y = Math.Clamp(Math.Min(pos.Y, _dragStart.Value.Y), 0, _preview.Height);
        _dragRect.Width = Math.Min(Math.Abs(pos.X - _dragStart.Value.X), _preview.Width - x);
        _dragRect.Height = Math.Min(Math.Abs(pos.Y - _dragStart.Value.Y), _preview.Height - y);
        Canvas.SetLeft(_dragRect, x);
        Canvas.SetTop(_dragRect, y);
    }

    private void Preview_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _preview.ReleaseMouseCapture();
        if (_dragStart == null || _dragRect == null)
        {
            return;
        }

        var width = _dragRect.Width / _preview.Width;
        var height = _dragRect.Height / _preview.Height;
        var x = Canvas.GetLeft(_dragRect) / _preview.Width;
        var y = Canvas.GetTop(_dragRect) / _preview.Height;
        _dragStart = null;
        _dragRect = null;

        if (width < 0.03 || height < 0.03)
        {
            RefreshPreview();
            return;
        }

        var zone = new TouchZone
        {
            Id = "zone" + (_freeZones.Count + 1),
            Label = "Zone " + (_freeZones.Count + 1),
            Rect = TouchZone.RectFromFractions(x, y, width, height),
            Tap = new TouchAction { Action = "cycle-card" }
        };
        _freeZones.Add(zone);
        RefreshZoneList();
        _zoneList.SelectedIndex = _freeZones.Count - 1;
    }

    private void RefreshZoneList()
    {
        var selected = _zoneList.SelectedIndex;
        _zoneList.Items.Clear();
        foreach (var zone in _freeZones)
        {
            _zoneList.Items.Add($"{zone.Label ?? zone.Id} — {zone.Rect} — {zone.Tap?.Describe() ?? "aucune action"}");
        }

        if (selected >= 0 && selected < _freeZones.Count)
        {
            _zoneList.SelectedIndex = selected;
        }
    }

    private void LoadZoneEditor()
    {
        var zone = SelectedZone;
        if (zone == null)
        {
            return;
        }

        _updatingZoneEditor = true;
        var action = zone.Tap?.Action ?? "cycle-card";
        _zoneAction.SelectedIndex = Math.Max(0, Array.FindIndex(Actions, a => a.Value == action));
        _zoneCard.Text = zone.Tap?.Card ?? "";
        _zonePlayer.SelectedIndex = (zone.Tap?.Player ?? 1) == 2 ? 1 : 0;
        _zoneDurationMs.Text = zone.Tap?.DurationMs?.ToString() ?? "";
        _updatingZoneEditor = false;
    }

    private void ApplyZoneEditor()
    {
        if (_updatingZoneEditor || SelectedZone is not { } zone)
        {
            return;
        }

        var action = (string)((ComboBoxItem)_zoneAction.SelectedItem!).Tag;
        zone.Tap = new TouchAction
        {
            Action = action,
            Card = action == "show-card" && !string.IsNullOrWhiteSpace(_zoneCard.Text) ? _zoneCard.Text.Trim() : null,
            Player = action == "show-player-card"
                ? (int)((ComboBoxItem)_zonePlayer.SelectedItem!).Tag
                : null,
            DurationMs = int.TryParse(_zoneDurationMs.Text.Trim(), out var ms) && ms > 0 ? ms : null
        };
        RefreshZoneList();
        RefreshPreview();
    }

    private void DeleteSelectedZone()
    {
        if (SelectedZone is { } zone)
        {
            _freeZones.Remove(zone);
            RefreshZoneList();
            RefreshPreview();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var path = PluginPaths.TouchProfilePath(_pluginRoot);
        if (!TouchProfileDocument.IsOwnedBySetup(path))
        {
            var confirm = MessageBox.Show(
                "Un profil avancé existe déjà et n'a pas été créé par cet outil. L'écraser ?",
                "MarqueeManager Setup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                _status.Text = "Enregistrement annulé : le profil existant est conservé.";
                return;
            }
        }

        int.TryParse(_returnMs.Text.Trim(), out var returnMs);

        var profile = TouchProfileDocument.LoadOrNew(path);
        var surface = profile.Surface("iccard");
        if (surface == null)
        {
            surface = new SurfaceProfile { Id = "ic-touch", Kind = "iccard" };
            profile.Surfaces.Add(surface);
        }

        surface.Touch = new TouchSettings
        {
            Enabled = _enabled.IsChecked == true,
            Mode = SelectedMode,
            DefaultCard = "ic1",
            ReturnToDefaultMs = Math.Max(0, returnMs),
            Zones = BuildZones()
        };
        profile.Save(path);

        _status.Text = "Profil tactile enregistré dans state\\surfaces.profile.json (sauvegarde .bak créée). "
                       + "Redémarrez MarqueeManager pour l'appliquer.";
    }
}
