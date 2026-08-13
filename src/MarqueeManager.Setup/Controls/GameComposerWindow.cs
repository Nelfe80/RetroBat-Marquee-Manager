using MarqueeManager.Compositions.Core.Composition;
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
    private sealed record Target(string Label, string Category, string SurfaceId, int W, int H, bool Suspended = false);

    private readonly string _pluginRoot;
    private readonly string _system;
    private readonly string _rom;
    private readonly string _displayName;
    private readonly IReadOnlyList<GameAsset> _assets;
    private readonly string _sampleSystem;
    private readonly string _sampleRom;
    /// <summary>Placeholders by default: a template is authored on TYPES. Ticking the box
    /// swaps in one entry's real media to judge the layout against a real picture.</summary>
    private bool _showSamples;
    private readonly bool _hasSystemAssets;
    private readonly string _downloadsDir;
    private readonly string _mediaRoot;
    // gabarit mode: the loaded layout's media is remapped to the CURRENT example
    // system's assets (by AssetKey), so the preview always shows the picked system
    private readonly bool _gabaritMode;

    private readonly List<Target> _targets = new();
    private readonly Border _composerHost = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private MarqueeComposer _composer = null!;
    private Target _target = null!;

    /// <summary>Graphic creation window. Every creation belongs to ONE surface
    /// (media\&lt;cat&gt;\surfaces\&lt;surfaceId&gt;\&lt;system&gt;\&lt;rom&gt;.png): creation A on
    /// surface 1 and creation B on surface 2 can coexist for the same game.
    /// System scope: system="systems", rom=&lt;system id&gt;. initialSurfaceId binds
    /// the target selector to the surface picked in the calling view.</summary>
    /// <param name="sample">Entry the PREVIEW resolves against. A gabarit is stored under
    /// a synthetic identity ("__gabarit__" / "game-arcade"), so resolving tokens against
    /// it found no metadata at all and every one of them fell back to its own name —
    /// the preview read "developer", "genre", "year" instead of the game's values.</param>
    public GameComposerWindow(string pluginRoot, string system, string rom, string displayName,
        IReadOnlyList<GameAsset> assets, string? initialSurfaceId = null, bool gabaritMode = false,
        (string System, string Rom)? sample = null)
    {
        _pluginRoot = pluginRoot;
        _system = system;
        _rom = rom;
        _sampleSystem = sample?.System ?? system;
        _sampleRom = sample?.Rom ?? rom;
        _displayName = displayName;
        _assets = assets;
        _hasSystemAssets = true; // system logo/fanart/marquee exist for every system
        _gabaritMode = gabaritMode;
        _downloadsDir = Path.Combine(pluginRoot, "media", "marquees", "downloads", Safe(system), Safe(rom));
        _mediaRoot = Path.GetFullPath(Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems"));

        Title = L.T($"Création graphique — {displayName}", $"Graphic creation — {displayName}");
        Width = 1180;
        Height = 760;
        WindowState = WindowState.Maximized;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Background;

        BuildTargets();
        _target = (initialSurfaceId != null
                      ? _targets.FirstOrDefault(t => t.SurfaceId.Equals(initialSurfaceId, StringComparison.OrdinalIgnoreCase))
                      : null)
                  ?? _targets[0];

        var root = new DockPanel { Margin = new Thickness(14) };

        // ===== top: target surface + actions =====
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var barLeft = new WrapPanel();
        var targetLabel = Ui.MutedLabel(L.T("Création pour :", "Creation for:"));
        targetLabel.Margin = new Thickness(0, 0, 6, 0);
        targetLabel.VerticalAlignment = VerticalAlignment.Center;
        barLeft.Children.Add(targetLabel);
        var targetPicker = Ui.ComboBox(340);
        foreach (var target in _targets)
        {
            // a surface on a screen MarqueeManager does not manage never displays
            // anything: offering it as a target only invites composing into the void
            targetPicker.Items.Add(new ComboBoxItem { Content = target.Label, Tag = target });
        }
        // select by TAG, never by index: the list is filtered, so an index into the
        // unfiltered set pointed at another surface — or at nothing, leaving the picker
        // blank
        targetPicker.SelectedItem = targetPicker.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => ReferenceEquals(i.Tag, _target)) ?? targetPicker.Items.Cast<ComboBoxItem>().FirstOrDefault();
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
        // destructive action next to its counterpart, not buried at the bottom of the
        // left rail where it was found by accident
        _deleteButton = Ui.Button(L.T("Supprimer ma création graphique", "Delete my graphic creation"), (_, _) => DeleteComposition());
        _deleteButton.Visibility = Visibility.Collapsed;
        barRight.Children.Add(_deleteButton);
        barRight.Children.Add(Ui.Button(L.T("Enregistrer ma création graphique", "Save my graphic creation"), (_, _) => Save(), primary: true));
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
        RefreshDeleteButton();

        // the canvas follows the REAL window width (the window may not open
        // maximized everywhere): remount on significant size changes
        SizeChanged += (_, _) =>
        {
            if (!IsLoaded) return;
            var wanted = Math.Max(640, ActualWidth - 590);
            if (Math.Abs(wanted - _lastCanvasWidth) < 60) return;
            var carried = _composer.HasLayers ? _composer.BuildProject(_system, _rom) : LoadProjectFor(_target);
            MountComposer(carried);
        };
    }

    private double _lastCanvasWidth;

    private readonly StackPanel _layersPanel = new();
    private readonly StackPanel _inspectorPanel = new();

    // ================= right panel: layers + inspector =================

    private void RenderSidePanels()
    {
        RenderLayersPanel();
        RenderInspectorPanel();
    }

    /// <summary>The SAME wording as the palette button that placed it. A layer called
    /// "wheel" when the button said "Logo (wheel)" forces the user to translate between
    /// two vocabularies for one thing.</summary>
    private string LayerName(MarqueeLayer layer)
    {
        if (layer.Source == "text") return $"{L.T("Texte", "Text")} « {layer.Text} »";
        // A layer is named EXACTLY as the palette button that placed it — the two lists
        // read side by side, so they must speak the same words.
        if (GabaritAssets.Palette.FirstOrDefault(
                e => e.Key.Equals(layer.AssetKey, StringComparison.OrdinalIgnoreCase)) is { } entry)
        {
            return L.T(entry.Fr, entry.En);
        }
        return layer.AssetKey.ToLowerInvariant() switch
        {
            "gradient" => L.T("Gradient", "Gradient"),
            "import" => L.T("Image importée", "Imported image"),
            "download" => L.T("Média téléchargé", "Downloaded media"),
            _ => layer.AssetKey.Length > 0 ? layer.AssetKey : L.T("Calque", "Layer")
        };
    }

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
        _layersPanel.Children.Add(Ui.MutedLabel(L.T("Glissez une ligne pour changer l'ordre.",
            "Drag a row to change the order."), 9));

        foreach (var layer in models.Reverse())
        {
            var isSelected = ReferenceEquals(layer, _composer.SelectedLayer);
            // selection = accent text + left accent tick, no dark cartridge
            var row = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = isSelected ? Ui.Accent : Brushes.Transparent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(4, 2, 2, 2),
                Margin = new Thickness(0, 1, 0, 1),
                AllowDrop = true,
                Tag = layer
            };
            var line = new DockPanel();

            // left icons, Photoshop style: eye (hide) + padlock (lock). Re-render the
            // panel after a toggle so the icon reflects the new state immediately.
            var eye = Ui.Button(layer.Hidden ? "🚫" : "👁", (_, _) =>
            {
                _composer.ApplyToLayer(layer, l => l.Hidden = !l.Hidden);
                RenderSidePanels();
            });
            eye.Padding = new Thickness(4, 1, 4, 1);
            eye.Opacity = layer.Hidden ? 0.5 : 1.0;
            eye.ToolTip = layer.Hidden ? L.T("Masqué — cliquer pour afficher", "Hidden — click to show")
                                       : L.T("Visible — cliquer pour masquer", "Visible — click to hide");
            line.Children.Add(eye);
            var padlock = Ui.Button(layer.Locked ? "🔒" : "🔓", (_, _) =>
            {
                _composer.ApplyToLayer(layer, l => l.Locked = !l.Locked);
                RenderSidePanels();
            });
            padlock.Padding = new Thickness(4, 1, 4, 1);
            padlock.ToolTip = layer.Locked ? L.T("Verrouillé — cliquer pour déverrouiller", "Locked — click to unlock")
                                           : L.T("Déverrouillé — cliquer pour verrouiller", "Unlocked — click to lock");
            line.Children.Add(padlock);

            // up/down arrows to reorder (alternative to drag & drop)
            var up = Ui.Button("▲", (_, _) =>
            {
                var i = _composer.LayerModels.ToList().FindIndex(l => ReferenceEquals(l, layer));
                if (i >= 0) _composer.MoveLayerTo(layer, i + 1);
            });
            up.Padding = new Thickness(3, 1, 3, 1);
            up.ToolTip = L.T("Monter (vers l'avant)", "Move up (towards front)");
            line.Children.Add(up);
            var down = Ui.Button("▼", (_, _) =>
            {
                var i = _composer.LayerModels.ToList().FindIndex(l => ReferenceEquals(l, layer));
                if (i >= 0) _composer.MoveLayerTo(layer, i - 1);
            });
            down.Padding = new Thickness(3, 1, 3, 1);
            down.ToolTip = L.T("Descendre (vers l'arrière)", "Move down (towards back)");
            line.Children.Add(down);

            var name = Ui.Label(LayerName(layer), 11);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(6, 0, 0, 0);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Opacity = layer.Hidden ? 0.45 : 1.0;
            if (isSelected)
            {
                name.Foreground = Ui.Accent;
                name.FontWeight = FontWeights.Bold;
            }
            name.Cursor = System.Windows.Input.Cursors.Hand;
            line.Children.Add(name);
            row.Child = line;

            // click = select ; sustained move = drag & drop reorder
            row.MouseLeftButtonDown += (_, _) => _composer.SelectLayer(layer);
            row.MouseMove += (_, e) =>
            {
                if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                {
                    DragDrop.DoDragDrop(row, new DataObject("marquee-layer", layer), DragDropEffects.Move);
                }
            };
            row.Drop += (_, e) =>
            {
                if (e.Data.GetData("marquee-layer") is not MarqueeLayer dragged
                    || ReferenceEquals(dragged, layer)) return;
                // the dragged layer takes THIS row's slot
                var without = _composer.LayerModels.Where(l => !ReferenceEquals(l, dragged)).ToList();
                _composer.MoveLayerTo(dragged, without.IndexOf(layer));
                e.Handled = true;
            };
            row.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent("marquee-layer") ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
            };

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

        // a text box is sized by its handles; "Size" would fight them
        if (!layer.IsTextBox)
        {
            SliderRow(L.T("Taille", "Size"), 0.05, 3.0, layer.Scale, v => layer.Scale = v);
        }
        SliderRow("Rotation", -180, 180, layer.Rotation, v => layer.Rotation = v);
        SliderRow(L.T("Opacité", "Opacity"), 0.05, 1.0, layer.Opacity, v => layer.Opacity = v);

        if (layer.IsTextBox)
        {
            // the handles resize the RECTANGLE, so the reading size lives here — and
            // nowhere else. Mixing the two is what made the box only grow downwards.
            SliderRow(L.T("Corps du texte", "Type size"), 0.02, 0.30, layer.FontSize, v => layer.FontSize = v);

            void AlignRow(string label, (string Key, string Fr, string En)[] options, string current, Action<string> onPick)
            {
                _inspectorPanel.Children.Add(Ui.MutedLabel(label, 11));
                var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var (key, fr, en) in options)
                {
                    var button = Ui.Button(L.T(fr, en), (_, _) =>
                    {
                        _composer.ApplyToLayer(layer, _ => onPick(key));
                        RenderSidePanels();
                    }, primary: key.Equals(current, StringComparison.OrdinalIgnoreCase));
                    button.Margin = new Thickness(0, 0, 4, 0);
                    button.Padding = new Thickness(8, 3, 8, 3);
                    row.Children.Add(button);
                }
                _inspectorPanel.Children.Add(row);
            }

            AlignRow(L.T("Alignement horizontal", "Horizontal alignment"),
                new[] { ("left", "Gauche", "Left"), ("center", "Centre", "Center"), ("right", "Droite", "Right") },
                layer.HAlign, key => layer.HAlign = key);
            AlignRow(L.T("Alignement vertical", "Vertical alignment"),
                new[] { ("top", "Haut", "Top"), ("middle", "Milieu", "Middle"), ("bottom", "Bas", "Bottom") },
                layer.VAlign, key => layer.VAlign = key);
        }

        if (layer.Source == "text")
        {
            // A layer holding nothing but a token has no editable content: it is filled
            // per entry at render time. Offering the raw "{developer}" as a text field
            // invited editing something that is not text.
            if (MarqueeComposer.IsTokenOnly(layer.Text))
            {
                _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Contenu", "Content"), 11));
                _inspectorPanel.Children.Add(Ui.MutedLabel(
                    L.T($"{TokenLabel(layer.Text)} — rempli pour chaque jeu.",
                        $"{TokenLabel(layer.Text)} — filled in per game.")));
            }
            else
            {
                var textBox = Ui.TextBox(layer.Text ?? "", 200);
                textBox.TextChanged += (_, _) => _composer.ApplyToLayer(layer, l => l.Text = textBox.Text);
                _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Texte", "Text"), 11));
                _inspectorPanel.Children.Add(textBox);
            }
            var colorBox = Ui.TextBox(layer.TextColor, 100);
            colorBox.TextChanged += (_, _) => _composer.ApplyToLayer(layer, l => l.TextColor = colorBox.Text.Trim());
            _inspectorPanel.Children.Add(Ui.MutedLabel(L.T("Couleur", "Color"), 11));
            _inspectorPanel.Children.Add(colorBox);
            var palette = Ui.ColorPalette(colorBox);
            palette.Margin = new Thickness(0, 4, 0, 0);
            _inspectorPanel.Children.Add(palette);
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

    private Button? _deleteButton;

    /// <summary>The delete button only exists when there IS a creation to delete, and
    /// it must follow the target the user switches to.</summary>
    private void RefreshDeleteButton()
    {
        if (_deleteButton == null) return;
        _deleteButton.Visibility = StoreFor(_target).HasComposition(_system, _rom)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void BuildTargets()
    {
        try
        {
            var store = new SurfacesStore(_pluginRoot);
            var surfaces = store.Load();
            var screens = ScreenProbe.Detect();
            // the stored screens carry the "managed by MarqueeManager" flag; the probe
            // only knows the physical layout
            var configured = store.LoadScreens();
            foreach (var surface in surfaces)
            {
                var category = surface.Category.ToLowerInvariant() switch
                {
                    "topper" => "toppers",
                    "dmd-virtual" => "dmd",
                    _ => "marquees"
                };
                var screenIndex = surface.Screens.Count > 0 ? surface.Screens[0] : -1;
                // a screen the user excluded from MarqueeManager gets no window at all
                var suspended = screenIndex < 0
                                || screenIndex >= screens.Count
                                || configured.FirstOrDefault(sc => sc.WindowsIndex == screenIndex) is { ManagedByMarqueeManager: false };
                var w = surface.Width ?? (screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex].Bounds.Width : 1920);
                var h = surface.Height ?? (screenIndex >= 0 && screenIndex < screens.Count ? screens[screenIndex].Bounds.Height : 360);
                if (w <= 0 || h <= 0) continue;
                if (suspended) continue; // never displays anything: not a target at all
                _targets.Add(new Target(
                    L.T($"Surface {surface.Id} ({surface.Category}) — écran {screenIndex}, {w}×{h}",
                        $"Surface {surface.Id} ({surface.Category}) — screen {screenIndex}, {w}×{h}"),
                    category, surface.Id, w, h, suspended));
            }
        }
        catch
        {
            // no surfaces.json: fallback below
        }
        if (_targets.Count == 0)
        {
            _targets.Add(new Target(L.T("Marquee (défaut 1920×360)", "Marquee (default 1920×360)"), "marquees", "", 1920, 360));
        }
    }

    /// <summary>The creation is stored PER SURFACE.</summary>
    private MarqueeProjectStore StoreFor(Target target) => new(_pluginRoot, target.Category, target.SurfaceId);

    /// <summary>Surface creation first; the category-level file (legacy /
    /// shared default) seeds the editor when the surface has none yet.</summary>
    private MarqueeProject? LoadProjectFor(Target target)
        => StoreFor(target).LoadProject(_system, _rom)
           ?? new MarqueeProjectStore(_pluginRoot, target.Category).LoadProject(_system, _rom);

    private void MountComposer(MarqueeProject? project)
    {
        // the canvas takes all the width left by the palette (200) and the
        // layers/inspector column (270)
        var canvasWidth = Math.Max(640, (IsLoaded && ActualWidth > 0 ? ActualWidth : SystemParameters.WorkArea.Width) - 590);
        _lastCanvasWidth = canvasWidth;
        _composer = new MarqueeComposer(_target.W, _target.H, _mediaRoot, canvasWidth)
        {
            InlineInspector = false // the window hosts the layers panel + inspector
        };
        _composer.StackChanged += RenderSidePanels;
        if (project != null)
        {
            if (_gabaritMode) RemapForGabarit(project);
            _composer.LoadProject(project);
        }
        _composer.Tokens = SampleTokens();
        _composerHost.Child = _composer;
        RenderSidePanels();
    }

    /// <summary>Metadata of the entry being previewed, so the canvas shows a real title
    /// instead of "{name}". Missing values fall back to a readable placeholder rather
    /// than to nothing — an empty layer cannot be positioned.</summary>
    private Dictionary<string, string> SampleTokens()
    {
        var catalog = new GameMediaCatalog(_pluginRoot);
        string Field(string field, string fallback)
        {
            try
            {
                var value = catalog.ReadMetadata(_sampleSystem, _sampleRom, field);
                return string.IsNullOrWhiteSpace(value) ? fallback : value!;
            }
            catch { return fallback; }
        }

        var release = Field("releasedate", "");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = Field("name", _sampleRom),
            ["developer"] = Field("developer", L.T("développeur", "developer")),
            ["publisher"] = Field("publisher", L.T("éditeur", "publisher")),
            ["year"] = release.Length >= 4 ? release[..4] : L.T("année", "year"),
            ["system"] = _sampleSystem,
            // A real description runs 500 to 1500 characters. When the sample has none,
            // stand in with filler of that LENGTH rather than a short sentence: the box
            // is sized against what will actually land in it.
            ["desc"] = Field("desc", LoremIpsum),
            ["genre"] = Field("genre", L.T("genre", "genre")),
            ["players"] = Field("players", "1-2"),
            ["rating"] = Field("rating", "14"),
        };
    }

    /// <summary>A gabarit stores the paths of whatever entry it was last composed on,
    /// but the layout is generic: a resolvable layer shows its coloured placeholder,
    /// or the sample's own medium while samples are on. Never another entry's picture —
    /// that is how a Sonic template ended up on every Mega Drive game.</summary>
    private void RemapForGabarit(MarqueeProject project)
    {
        foreach (var layer in project.Layers)
        {
            if (!GabaritAssets.IsResolvable(layer.AssetKey)) continue;
            var asset = _assets.FirstOrDefault(a => a.Key.Equals(layer.AssetKey, StringComparison.OrdinalIgnoreCase));
            layer.Source = _showSamples && asset is not null && File.Exists(asset.Path) ? asset.Path : "";
        }
    }

    /// <summary>Import an image file: copy it into media\imports (a stable path) and
    /// place it as a layer. Its key "import" never matches a system asset, so a
    /// gabarit uses it for EVERY system; a creation uses it for that creation only.</summary>
    private void ImportImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("Importer une image", "Import an image"),
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var dir = Path.Combine(_pluginRoot, "media", "imports");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            File.Copy(dialog.FileName, dest, overwrite: true);
            _composer.AddMediaLayer(dest, "import");
            _status.Text = L.T("Image importée et posée en calque.", "Image imported and placed as a layer.");
            _status.Foreground = Ui.Muted;
        }
        catch (Exception ex)
        {
            _status.Text = L.T($"Import impossible : {ex.Message}", $"Import failed: {ex.Message}");
            _status.Foreground = Ui.Error;
        }
    }

    private void SwitchTarget(Target target)
    {
        // A composition belongs to ITS surface: carrying the layers over dropped a
        // marquee composition onto the topper, which has nothing to do with it. An
        // empty target opens empty.
        _target = target;
        MountComposer(LoadProjectFor(target));
        _status.Text = L.T($"Cible : {target.Label} — la création est propre à CETTE surface.",
            $"Target: {target.Label} — the creation belongs to THIS surface.");
        _status.Foreground = Ui.Muted;
        RefreshDeleteButton();
    }

    // ================= media palette =================

    /// <summary>Places a type: the sample's real medium when samples are shown and it
    /// has one, otherwise the coloured placeholder. Either way the layer carries the
    /// KEY, so the runtime resolves it against whatever entry it renders for.</summary>
    private void PlaceType(GabaritAssets.PaletteEntry entry)
    {
        var label = L.T(entry.Fr, entry.En);
        if (_showSamples && _assets.Any(a => a.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase)))
        {
            PickAndPlace(entry.Key, label);
            return;
        }
        _composer.AddPlaceholderLayer(entry);
        if (!entry.Served)
        {
            _status.Text = L.T(
                $"{label} : le flux APIExpose ne transporte pas ce média — le calque ne s'affichera pas sur la surface.",
                $"{label}: the APIExpose stream does not carry this medium — the layer will not display on the surface.");
            _status.Foreground = Ui.Error;
            return;
        }
        _status.Text = _showSamples
            ? L.T($"{label} : l'échantillon n'en a pas — posé en repère, résolu jeu par jeu.",
                  $"{label}: the sample has none — placed as a marker, resolved per entry.")
            : L.T($"{label} posé. Cochez « Afficher les échantillons » pour voir de vrais médias.",
                  $"{label} placed. Tick “Show samples” to see real media.");
        _status.Foreground = Ui.Muted;
    }

    private void ToggleSamples(bool on)
    {
        _showSamples = on;
        _composer.ShowSamples(key =>
            _assets.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } asset
            && File.Exists(asset.Path) ? asset.Path : null, on);
    }

    /// <summary>~900 characters of filler — the middle of the range a scraped
    /// description occupies, so a box sized on it holds a real one.</summary>
    private const string LoremIpsum =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor "
        + "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis "
        + "nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. "
        + "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu "
        + "fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in "
        + "culpa qui officia deserunt mollit anim id est laborum. Sed ut perspiciatis unde "
        + "omnis iste natus error sit voluptatem accusantium doloremque laudantium, totam "
        + "rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi architecto "
        + "beatae vitae dicta sunt explicabo. Nemo enim ipsam voluptatem quia voluptas sit "
        + "aspernatur aut odit aut fugit, sed quia consequuntur magni dolores eos qui "
        + "ratione voluptatem sequi nesciunt.";

    /// <summary>Human name of the single token a layer carries.</summary>
    private static string TokenLabel(string? text) => (text ?? "").Trim().Trim('{', '}').ToLowerInvariant() switch
    {
        "name" => L.T("Nom du jeu", "Game name"),
        "desc" => L.T("Description", "Description"),
        "developer" => L.T("Développeur", "Developer"),
        "publisher" => L.T("Éditeur", "Publisher"),
        "year" => L.T("Année", "Year"),
        "genre" => L.T("Genre", "Genre"),
        "players" => L.T("Joueurs", "Players"),
        "rating" => L.T("Note", "Rating"),
        "system" => L.T("Système", "System"),
        var other => other
    };

    private static Color ParseHex(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    private FrameworkElement BuildPalette()
    {
        var panel = new StackPanel { Width = 200 };
        panel.Children.Add(Ui.SectionHeader(L.T("Médias", "Media")));
        panel.Children.Add(Ui.MutedLabel(L.T("Un type → choisir la version → posé en calque.",
            "One type → pick the version → placed as a layer.")));
        // Say WHICH entry the palette is judging: greyed types are the ones this sample
        // lacks, and nothing about that should have to be guessed.
        var availableTypes = _assets.Select(a => a.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        panel.Children.Add(Ui.MutedLabel(L.T($"Échantillon : {_sampleSystem} / {_sampleRom} — {availableTypes} type(s) disponible(s).",
            $"Sample: {_sampleSystem} / {_sampleRom} — {availableTypes} type(s) available.")));

        // EVERY composable type is offered, always. Building the palette from what one
        // sample game owns made a whole system's template offer four buttons — the
        // template is generic, the picture comes from the entry it renders for.
        var samples = new CheckBox
        {
            Content = L.T("Afficher les échantillons", "Show samples"),
            Foreground = Ui.Muted,
            Margin = new Thickness(0, 4, 0, 6),
            IsChecked = _showSamples
        };
        samples.Checked += (_, _) => ToggleSamples(true);
        samples.Unchecked += (_, _) => ToggleSamples(false);
        panel.Children.Add(samples);

        foreach (var entry in GabaritAssets.Palette)
        {
            if (entry.Scope == "system" && !_hasSystemAssets) continue;
            var label = L.T(entry.Fr, entry.En);
            var owned = _assets.Any(a => a.Key.Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            var button = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new Border
                        {
                            Width = 12, Height = 12, CornerRadius = new CornerRadius(2),
                            Background = Ui.Brush(ParseHex(entry.Color)),
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 6, 0)
                        },
                        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center },
                        // marks a type the surface will never show, right where it is picked
                        new TextBlock
                        {
                            Text = entry.Served ? "" : "  ⃠",
                            Foreground = Ui.Error,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                },
                ToolTip = !entry.Served
                    ? L.T($"{label} — le flux APIExpose ne le transporte pas : composable ici, mais il ne s'affichera pas sur la surface.",
                          $"{label} — the APIExpose stream does not carry it: composable here, but it will not display on the surface.")
                    : owned
                        ? L.T($"{label} — l'échantillon en possède un", $"{label} — the sample has one")
                        : L.T($"{label} — posé en repère, résolu jeu par jeu",
                              $"{label} — placed as a marker, resolved per entry"),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Ui.Brush(Color.FromRgb(0x2A, 0x2A, 0x3E)),
                Foreground = owned ? Brushes.White : Ui.Muted,
                BorderBrush = Ui.Brush(Color.FromRgb(0x3A, 0x3A, 0x52)),
                BorderThickness = new Thickness(1)
            };
            var captured = entry;
            button.Click += (_, _) => PlaceType(captured);
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

        // static gradients (tools\compositing): readability helpers when the
        // logo lacks contrast against the fanart
        foreach (var (file, fr, en) in new[]
                 {
                     ("gradient_black.png", "Gradient noir", "Black gradient"),
                     ("gradient_white.png", "Gradient blanc", "White gradient")
                 })
        {
            var path = Path.Combine(_pluginRoot, "tools", "compositing", file);
            if (!File.Exists(path)) continue;
            var gradient = Ui.Button(L.T(fr, en), (_, _) =>
            {
                _composer.AddMediaLayer(path, "gradient");
                _status.Text = L.T("Gradient posé — étirez-le sur la zone du logo pour la lisibilité.",
                    "Gradient placed — stretch it over the logo area for readability.");
                _status.Foreground = Ui.Muted;
            });
            gradient.Margin = new Thickness(0, 2, 0, 2);
            gradient.HorizontalAlignment = HorizontalAlignment.Stretch;
            gradient.HorizontalContentAlignment = HorizontalAlignment.Left;
            panel.Children.Add(gradient);
        }

        panel.Children.Add(Ui.SectionHeader(L.T("Autres", "Other")));
        // A template's text is a TEMPLATE, never a frozen string: placing the window's
        // display name wrote "General template — arcade games" onto the marquee, and the
        // runtime drew it verbatim on every game of the system.
        // every palette button reads the same way: full width, label on the left
        void AddPaletteButton(FrameworkElement button)
        {
            if (button is Button b)
            {
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.HorizontalContentAlignment = HorizontalAlignment.Left;
                b.Margin = new Thickness(0, 2, 0, 2);
            }
            panel.Children.Add(button);
        }

        AddPaletteButton(Ui.Button(L.T("Texte : nom du jeu", "Text: game name"), (_, _) => _composer.AddTextLayer("{name}")));
        AddPaletteButton(Ui.Button(L.T("Texte : développeur", "Text: developer"), (_, _) => _composer.AddTextLayer("{developer}")));
        AddPaletteButton(Ui.Button(L.T("Texte : éditeur", "Text: publisher"), (_, _) => _composer.AddTextLayer("{publisher}")));
        AddPaletteButton(Ui.Button(L.T("Texte : année", "Text: year"), (_, _) => _composer.AddTextLayer("{year}")));
        // these come from the entry's text block: they arrive on the streams that print
        // something about a game, and land in a BOX because a description is 500 to
        // 1500 characters long
        AddPaletteButton(Ui.Button(L.T("Texte : description", "Text: description"),
            (_, _) => _composer.AddTextLayer("{desc}", wrapWidth: 0.8)));
        AddPaletteButton(Ui.Button(L.T("Texte : genre", "Text: genre"), (_, _) => _composer.AddTextLayer("{genre}")));
        AddPaletteButton(Ui.Button(L.T("Texte : joueurs", "Text: players"), (_, _) => _composer.AddTextLayer("{players}")));
        AddPaletteButton(Ui.Button(L.T("Texte : note", "Text: rating"), (_, _) => _composer.AddTextLayer("{rating}")));

        // import your own image — used for EVERY system in a gabarit (its key never
        // matches a system asset, so it is not remapped), specific to a creation
        var import = Ui.Button(L.T("Importer une image…", "Import an image…"), (_, _) => ImportImage());
        import.Margin = new Thickness(0, 2, 0, 2);
        import.HorizontalAlignment = HorizontalAlignment.Stretch;
        import.HorizontalContentAlignment = HorizontalAlignment.Left;
        panel.Children.Add(import);
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
                $"✔ Création graphique enregistrée pour cette surface : {store.PngPath(_system, _rom)} — affichée à la prochaine sélection.",
                $"✔ Graphic creation saved for this surface: {store.PngPath(_system, _rom)} — shown on the next selection.");
            _status.Foreground = Ui.Ok;
            RefreshDeleteButton();
        }
        catch (Exception ex)
        {
            _status.Text = L.T($"Échec de l'enregistrement : {ex.Message}", $"Save failed: {ex.Message}");
            _status.Foreground = Ui.Error;
        }
    }

    private void DeleteComposition()
    {
        // per-surface file AND the category-level legacy file — otherwise the
        // old creation seeds the editor again and "keeps coming back"
        StoreFor(_target).Delete(_system, _rom);
        new MarqueeProjectStore(_pluginRoot, _target.Category).Delete(_system, _rom);
        _status.Text = L.T("Création graphique supprimée — la chaîne de sources reprend la main.",
            "Graphic creation deleted — the source chain takes over again.");
        _status.Foreground = Ui.Muted;
        RefreshDeleteButton();
    }

    private static string Safe(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
    }
}
