using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// The composition window for ONE game (or one system): the same editing
/// experience everywhere. Top: pick WHICH surface of WHICH screen the
/// composition targets (canvas locks to its real resolution, the export lands in
/// that category's media folder). Left: the media BY TYPE — clicking "Fanart"
/// opens a preview modal of every available fanart by source, click to place.
/// Center: the layer canvas (drag, wheel = scale, inspector). Saving renders the
/// PNG + project JSON and confirms in place.
/// </summary>
public sealed class GameComposerWindow : Window
{
    private sealed record Target(string Label, string Category, int W, int H);

    private readonly string _pluginRoot;
    private readonly string _system;
    private readonly string _rom;
    private readonly string _displayName;
    private readonly IReadOnlyList<GameAsset> _assets;
    private readonly string _downloadsDir;
    private readonly string _mediaRoot;

    private readonly List<Target> _targets = new();
    private readonly Border _composerHost = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private MarqueeComposer _composer = null!;
    private Target _target = null!;

    /// <summary>Game composition: media\marquees\&lt;system&gt;\&lt;rom&gt;.png.
    /// System composition (system sheet): system="systems", rom=&lt;system id&gt;.</summary>
    public GameComposerWindow(string pluginRoot, string system, string rom, string displayName,
        IReadOnlyList<GameAsset> assets)
    {
        _pluginRoot = pluginRoot;
        _system = system;
        _rom = rom;
        _displayName = displayName;
        _assets = assets;
        _downloadsDir = Path.Combine(pluginRoot, "media", "marquees", "downloads", Safe(system), Safe(rom));
        _mediaRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems"));

        Title = L.T($"Composer — {displayName}", $"Compose — {displayName}");
        Width = 1180;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        BuildTargets();
        _target = _targets[0];

        var root = new DockPanel { Margin = new Thickness(14) };

        // ===== top: target surface + actions =====
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var barLeft = new WrapPanel();
        var targetLabel = Ui.MutedLabel(L.T("Composer pour :", "Compose for:"));
        targetLabel.Margin = new Thickness(0, 0, 6, 0);
        targetLabel.VerticalAlignment = VerticalAlignment.Center;
        barLeft.Children.Add(targetLabel);
        var targetPicker = Ui.ComboBox(340);
        foreach (var target in _targets)
        {
            targetPicker.Items.Add(new ComboBoxItem { Content = target.Label, Tag = target });
        }
        targetPicker.SelectedIndex = 0;
        targetPicker.SelectionChanged += (_, _) =>
        {
            if ((targetPicker.SelectedItem as ComboBoxItem)?.Tag is Target target && target != _target)
            {
                SwitchTarget(target);
            }
        };
        barLeft.Children.Add(targetPicker);
        bar.Children.Add(barLeft);

        var barRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        barRight.Children.Add(Ui.Button(L.T("Fermer", "Close"), (_, _) => Close()));
        barRight.Children.Add(Ui.Button(L.T("Enregistrer ma composition", "Save my composition"), (_, _) => Save(), primary: true));
        DockPanel.SetDock(barRight, Dock.Right);
        bar.Children.Add(barRight);
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);

        // status right below the actions — the feedback is impossible to miss
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 6);
        DockPanel.SetDock(_status, Dock.Top);
        root.Children.Add(_status);

        // ===== left: media by type =====
        var palette = BuildPalette();
        DockPanel.SetDock(palette, Dock.Left);
        root.Children.Add(palette);

        // ===== right: layers (front → back) + inspector, RetroCreator layout =====
        var right = new Grid { Width = 270, Margin = new Thickness(10, 0, 0, 0) };
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58, GridUnitType.Star) });
        var layersBox = new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 8),
            Child = new ScrollViewer { Content = _layersPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        right.Children.Add(layersBox);
        var inspectorBox = new Border
        {
            Background = Ui.Panel, BorderBrush = Ui.PanelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10),
            Child = new ScrollViewer { Content = _inspectorPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        Grid.SetRow(inspectorBox, 1);
        right.Children.Add(inspectorBox);
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);

        // ===== center: the composer canvas =====
        _composerHost.Margin = new Thickness(10, 0, 0, 0);
        root.Children.Add(new ScrollViewer
        {
            Content = _composerHost,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10, 0, 0, 0)
        });

        Content = root;
        MountComposer(LoadProjectFor(_target));
    }

    private readonly StackPanel _layersPanel = new();
    private readonly StackPanel _inspectorPanel = new();

    // ================= right panel: layers + inspector =================

    private void RenderSidePanels()
    {
        RenderLayersPanel();
        RenderInspectorPanel();
    }

    private string LayerName(MarqueeLayer layer)
        => layer.Source == "text" ? $"{L.T("Texte", "Text")} « {layer.Text} »" : layer.AssetKey;

    private void RenderLayersPanel()
    {
        _layersPanel.Children.Clear();
        var title = Ui.MutedLabel(L.T("CALQUES (avant → arrière)", "LAYERS (front → back)"), 10);
        title.FontWeight = FontWeights.Bold;
        _layersPanel.Children.Add(title);

        var models = _composer.LayerModels; // back → front
        if (models.Count == 0)
        {
            _layersPanel.Children.Add(Ui.MutedLabel(L.T("Aucun calque — piochez un média à gauche.",
                "No layer yet — pick a media on the left.")));
            return;
        }

        foreach (var layer in models.Reverse())
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var up = Ui.Button("↑", (_, _) => _composer.ReorderLayer(layer, +1)); // toward front
            up.Padding = new Thickness(6, 2, 6, 2);
            buttons.Children.Add(up);
            var down = Ui.Button("↓", (_, _) => _composer.ReorderLayer(layer, -1));
            down.Padding = new Thickness(6, 2, 6, 2);
            buttons.Children.Add(down);
            DockPanel.SetDock(buttons, Dock.Right);
            row.Children.Add(buttons);

            var isSelected = ReferenceEquals(layer, _composer.SelectedLayer);
            var name = Ui.Label(LayerName(layer), 11);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            if (isSelected)
            {
                name.Foreground = Ui.Accent;
                name.FontWeight = FontWeights.Bold;
            }
            name.Cursor = System.Windows.Input.Cursors.Hand;
            name.MouseLeftButtonDown += (_, _) => _composer.SelectLayer(layer);
            row.Children.Add(name);
            _layersPanel.Children.Add(row);
        }
    }

    private void RenderInspectorPanel()
    {
        _inspectorPanel.Children.Clear();
        var layer = _composer.SelectedLayer;
        if (layer == null)
        {
            _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Sélectionnez un calque (clic sur le canvas ou dans la liste).",
                "Select a layer (click the canvas or the list).")));
            return;
        }

        var header = Ui.Label(LayerName(layer), 13);
        header.FontWeight = FontWeights.Bold;
        _inspectorPanel.Children.Add(header);

        void SliderRow(string label, double min, double max, double value, Action<double> onChange)
        {
            var text = Ui.MutedLabel(label, 11);
            _inspectorPanel.Children.Add(text);
            var slider = new Slider
            {
                Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
                Margin = new Thickness(0, 0, 0, 4)
            };
            slider.ValueChanged += (_, args) => _composer.ApplyToLayer(layer, l => onChange(args.NewValue));
            _inspectorPanel.Children.Add(slider);
        }

        SliderRow(L.T("Taille", "Size"), 0.05, 3.0, layer.Scale, v => layer.Scale = v);
        SliderRow("Rotation", -180, 180, layer.Rotation, v => layer.Rotation = v);
        SliderRow(L.T("Opacité", "Opacity"), 0.05, 1.0, layer.Opacity, v => layer.Opacity = v);

        if (layer.Source == "text")
        {
            var textBox = Ui.TextBox(layer.Text ?? "", 200);
            textBox.TextChanged += (_, _) => _composer.ApplyToLayer(layer, l => l.Text = textBox.Text);
            _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Texte", "Text"), 11));
            _inspectorPanel.Children.Add(textBox);
            var colorBox = Ui.TextBox(layer.TextColor, 100);
            colorBox.TextChanged += (_, _) => _composer.ApplyToLayer(layer, l => l.TextColor = colorBox.Text.Trim());
            _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Couleur (#RRGGBB)", "Color (#RRGGBB)"), 11));
            _inspectorPanel.Children.Add(colorBox);
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Miroir", "Mirror"), (_, _) =>
            _composer.ApplyToLayer(layer, l => l.FlipH = !l.FlipH)));
        actions.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) => _composer.DeleteLayer(layer)));
        _inspectorPanel.Children.Add(actions);
        _inspectorPanel.Children.Add(Ui.MutedLabel(L.T(
            "Canvas : glisser = déplacer, molette = taille, Maj+molette = rotation.",
            "Canvas: drag = move, wheel = size, Shift+wheel = rotate."), 10));
    }

    // ================= targets =================

    private void BuildTargets()
    {
        try
        {
            var surfaces = new SurfacesStore(_pluginRoot).Load();
            var screens = ScreenProbe.Detect();
            foreach (var surface in surfaces)
            {
                var category = surface.Category.ToLowerInvariant() switch
                {
                    "topper" => "toppers",
                    "dmd-virtual" => "dmd",
                    _ => "marquees"
                };
                var screenIndex = surface.Screens.Count > 0 ? surface.Screens[0] : -1;
                var w = surface.Width ?? (screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex].Bounds.Width : 1920);
                var h = surface.Height ?? (screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex].Bounds.Height : 360);
                if (w <= 0 || h <= 0) continue;
                _targets.Add(new Target(
                    L.T($"{surface.Id} ({surface.Category}) — écran {screenIndex}, {w}×{h}",
                        $"{surface.Id} ({surface.Category}) — screen {screenIndex}, {w}×{h}"),
                    category, w, h));
            }
        }
        catch
        {
            // no surfaces.json: fallback below
        }
        if (_targets.Count == 0)
        {
            _targets.Add(new Target(L.T("Marquee (défaut 1920×360)", "Marquee (default 1920×360)"), "marquees", 1920, 360));
        }
    }

    private MarqueeProjectStore StoreFor(Target target) => new(_pluginRoot, target.Category);

    private MarqueeProject? LoadProjectFor(Target target) => StoreFor(target).LoadProject(_system, _rom);

    private void MountComposer(MarqueeProject? project)
    {
        _composer = new MarqueeComposer(_target.W, _target.H, _mediaRoot)
        {
            InlineInspector = false // the window hosts the layers panel + inspector
        };
        _composer.StackChanged += RenderSidePanels;
        if (project != null) _composer.LoadProject(project);
        _composerHost.Child = _composer;
        RenderSidePanels();
    }

    private void SwitchTarget(Target target)
    {
        // carry the current layers over — the fractions adapt to the new ratio
        var carried = _composer.HasLayers ? _composer.BuildProject(_system, _rom) : null;
        _target = target;
        MountComposer(LoadProjectFor(target) ?? carried);
        _status.Text = L.T($"Cible : {target.Label} — l'enregistrement ira dans media\\{target.Category}.",
            $"Target: {target.Label} — saving goes to media\\{target.Category}.");
        _status.Foreground = Ui.Muted;
    }

    // ================= media palette =================

    private FrameworkElement BuildPalette()
    {
        var panel = new StackPanel { Width = 200 };
        panel.Children.Add(Ui.SectionHeader(L.T("Médias", "Media")));
        panel.Children.Add(Ui.MutedLabel(L.T("Un type → choisir la version → posé en calque.",
            "One type → pick the version → placed as a layer.")));

        foreach (var kind in _assets.Select(a => (a.Key, a.Label)).Distinct())
        {
            var button = Ui.Button(kind.Label, (_, _) => PickAndPlace(kind.Key, kind.Label));
            button.Margin = new Thickness(0, 2, 0, 2);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            panel.Children.Add(button);
        }

        if (ListDownloads(null).Count > 0)
        {
            var downloaded = Ui.Button(L.T("Téléchargés (tous)", "Downloaded (all)"), (_, _) => PickAndPlace(null, L.T("Téléchargés", "Downloaded")));
            downloaded.Margin = new Thickness(0, 2, 0, 2);
            downloaded.HorizontalAlignment = HorizontalAlignment.Stretch;
            downloaded.HorizontalContentAlignment = HorizontalAlignment.Left;
            panel.Children.Add(downloaded);
        }

        panel.Children.Add(Ui.SectionHeader(L.T("Autres", "Other")));
        var text = Ui.Button(L.T("Texte (titre du jeu)", "Text (game title)"), (_, _) => _composer.AddTextLayer(_displayName));
        text.HorizontalAlignment = HorizontalAlignment.Stretch;
        text.HorizontalContentAlignment = HorizontalAlignment.Left;
        panel.Children.Add(text);
        var recipe = Ui.Button(L.T("Gabarit auto (fanart + logo 50 %)", "Auto recipe (fanart + 50 % logo)"), (_, _) =>
            _composer.ApplyTemplatePreset(
                _assets.FirstOrDefault(a => a.Key == "fanart")?.Path,
                _assets.FirstOrDefault(a => a.Key == "wheel")?.Path));
        recipe.Margin = new Thickness(0, 2, 0, 2);
        recipe.HorizontalAlignment = HorizontalAlignment.Stretch;
        recipe.HorizontalContentAlignment = HorizontalAlignment.Left;
        panel.Children.Add(recipe);

        // background
        panel.Children.Add(Ui.SectionHeader(L.T("Fond", "Background")));
        var background = Ui.ComboBox(180);
        background.Items.Add(new ComboBoxItem { Content = L.T("Noir", "Black"), Tag = "solid" });
        background.Items.Add(new ComboBoxItem { Content = L.T("Dégradé sombre", "Dark gradient"), Tag = "gradient" });
        background.Items.Add(new ComboBoxItem { Content = L.T("Fanart flouté", "Blurred fanart"), Tag = "media" });
        background.SelectedIndex = 0;
        background.SelectionChanged += (_, _) =>
        {
            if ((background.SelectedItem as ComboBoxItem)?.Tag is not string kind) return;
            var fanart = _assets.FirstOrDefault(a => a.Key is "fanart" or "mix" or "screenshot");
            _composer.SetBackground(new MarqueeBackground
            {
                Kind = kind,
                Color = kind == "gradient" ? "#101020" : "#000000",
                Color2 = "#283048",
                Source = kind == "media" && fanart != null ? fanart.Path : null
            });
        };
        panel.Children.Add(background);

        if (StoreFor(_target).HasComposition(_system, _rom))
        {
            var delete = Ui.Button(L.T("Supprimer ma composition", "Delete my composition"), (_, _) => DeleteComposition());
            delete.Margin = new Thickness(0, 12, 0, 0);
            panel.Children.Add(delete);
        }

        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>All the candidates of a kind: the APIExpose asset first, then the
    /// downloaded files whose name carries that kind. kind=null → every download.</summary>
    private List<MediaCandidate> CandidatesFor(string? kind)
    {
        var candidates = new List<MediaCandidate>();
        if (kind != null)
        {
            foreach (var asset in _assets.Where(a => a.Key.Equals(kind, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(new MediaCandidate(L.T("Bibliothèque APIExpose", "APIExpose library"), asset.Path));
            }
        }
        foreach (var file in ListDownloads(kind))
        {
            candidates.Add(new MediaCandidate(L.T("Médias téléchargés", "Downloaded media"), file));
        }
        return candidates;
    }

    private List<string> ListDownloads(string? kind)
    {
        try
        {
            if (!Directory.Exists(_downloadsDir)) return new List<string>();
            return Directory.EnumerateFiles(_downloadsDir)
                .Where(f => kind == null || Path.GetFileName(f).Contains(kind, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private void PickAndPlace(string? kind, string label)
    {
        var candidates = CandidatesFor(kind);
        if (candidates.Count == 1)
        {
            _composer.AddMediaLayer(candidates[0].Path, kind ?? "download");
            return;
        }
        var picker = new MediaPickerDialog(label, candidates) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath is { } path)
        {
            _composer.AddMediaLayer(path, kind ?? "download");
        }
    }

    // ================= save =================

    private void Save()
    {
        if (!_composer.HasLayers)
        {
            _status.Text = L.T("Ajoutez au moins un calque avant d'enregistrer.", "Add at least one layer before saving.");
            _status.Foreground = Ui.Error;
            return;
        }

        var store = StoreFor(_target);
        if (!store.IsOwnedBySetup(_system, _rom)
            && MessageBox.Show(
                L.T("Le projet existant n'a pas été créé par MarqueeManagerSetup. L'écraser ?",
                    "The existing project was not created by MarqueeManagerSetup. Overwrite it?"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            store.SaveProject(_composer.BuildProject(_system, _rom));
            _composer.RenderPng(store.PngPath(_system, _rom));
            _status.Text = L.T(
                $"✔ Composition enregistrée : {store.PngPath(_system, _rom)} — affichée à la prochaine sélection.",
                $"✔ Composition saved: {store.PngPath(_system, _rom)} — shown on the next selection.");
            _status.Foreground = Ui.Ok;
        }
        catch (Exception ex)
        {
            _status.Text = L.T($"Échec de l'enregistrement : {ex.Message}", $"Save failed: {ex.Message}");
            _status.Foreground = Ui.Error;
        }
    }

    private void DeleteComposition()
    {
        StoreFor(_target).Delete(_system, _rom);
        _status.Text = L.T("Composition supprimée — le marquee scrapé/généré reprend la main.",
            "Composition deleted — the scraped/generated marquee takes over again.");
        _status.Foreground = Ui.Muted;
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
