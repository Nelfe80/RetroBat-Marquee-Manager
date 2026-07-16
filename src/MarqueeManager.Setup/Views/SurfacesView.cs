using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Dynamic surfaces editor (state\surfaces.json): create as many categorized
/// surfaces as needed, pick their screen, size them (width × height only — the
/// x,y position is set visually in the screen compositor), bind streams and
/// stack components. Replaces the fixed five-target view.
/// </summary>
public sealed class SurfacesView : UserControl
{
    private static readonly (string Key, string Fr, string En)[] Categories =
    {
        ("marquee", "Marquee", "Marquee"),
        ("topper", "Topper", "Topper"),
        ("iccard", "Instruction card", "Instruction card"),
        ("dmd-virtual", "DMD virtuel", "Virtual DMD"),
        ("lcd", "LCD / panel", "LCD / panel"),
        ("custom", "Libre", "Custom")
    };

    private static readonly (string Key, string Fr, string En)[] KnownStreams =
    {
        ("marquee", "Flux marquee", "Marquee stream"),
        ("topper", "Flux topper", "Topper stream"),
        ("iccard", "Cartes d'instructions", "Instruction cards"),
        ("dmd", "Flux DMD", "DMD stream"),
        ("lcd", "Flux LCD / panel", "LCD / panel stream")
    };

    private static readonly (string Key, string Fr, string En)[] ComponentTypes =
    {
        ("media.flux", "Média du flux", "Stream media"),
        ("media.logo", "Logo (wheel)", "Logo (wheel)"),
        ("media.fanart", "Fanart", "Fanart"),
        ("media.image", "Image (kind au choix)", "Image (any kind)"),
        ("media.video", "Vidéo du jeu", "Game video"),
        ("text.meta", "Texte méta (nom, année…)", "Meta text (name, year…)"),
        ("text.custom", "Texte libre", "Custom text"),
        ("overlay.hiscore", "Hiscores", "Hiscores"),
        ("overlay.live.score", "Score live", "Live score"),
        ("overlay.live.timer", "Timer live", "Live timer"),
        ("overlay.ra.info", "RetroAchievements", "RetroAchievements"),
        ("overlay.ra.badges", "Badges RA", "RA badges"),
        ("overlay.ra.speedrun", "Speedrun RA", "RA speedrun"),
        ("lighting.engine", "Rendu lumineux (Lighting)", "Lighting engine"),
        ("lamps.scene", "Lampes rbmarquee", "rbmarquee lamps"),
        ("iccard.static", "Carte fixe", "Static card"),
        ("iccard.cycle", "Carte variable", "Cycling card"),
        ("external.web", "Web embarqué (Twitch, YouTube…)", "Embedded web (Twitch, YouTube…)")
    };

    private readonly string _pluginRoot;
    private readonly SurfacesStore _store;
    private readonly List<SurfaceModel> _surfaces;
    private readonly StackPanel _list = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly IReadOnlyList<ScreenInfo> _screens;

    public SurfacesView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _store = new SurfacesStore(pluginRoot);
        _surfaces = _store.Load();
        _screens = ScreenProbe.Detect();

        var page = new StackPanel();
        page.Children.Add(Ui.Title("Surfaces"));
        page.Children.Add(Ui.Subtitle(L.T(
            "Créez autant de surfaces que nécessaire, catégorisez-les, liez-les à des flux et empilez des composants. "
            + "La position x,y se règle visuellement dans « Écrans → Composer cet écran » ; ici une zone = largeur × hauteur.",
            "Create as many surfaces as you need, categorize them, bind streams and stack components. "
            + "The x,y position is set visually in “Screens → Compose this screen”; a zone here is width × height only.")));

        if (SurfacesStore.MigratedThisSession)
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "✓ Votre configuration [Screens] a été convertie automatiquement en surfaces dynamiques (state\\surfaces.json). Le comportement est identique — vous pouvez maintenant l'enrichir.",
                "✓ Your [Screens] configuration was automatically converted to dynamic surfaces (state\\surfaces.json). Behavior is identical — you can now build on it."))));
        }

        if (!_store.IsOwnedBySetup())
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "surfaces.json n'a pas été créé par MarqueeManagerSetup — les modifications l'écraseront.",
                "surfaces.json was not created by MarqueeManagerSetup — saving will overwrite it."))));
        }

        page.Children.Add(_list);

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Ajouter une surface", "Add a surface"), (_, _) =>
        {
            _surfaces.Add(NewSurface());
            RebuildList();
        }));
        actions.Children.Add(Ui.Button(L.T("Enregistrer les surfaces", "Save surfaces"), (_, _) => OnSave(), primary: true));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
        RebuildList();
    }

    private SurfaceModel NewSurface()
    {
        var surface = new SurfaceModel { Id = UniqueId("surface"), Category = "custom" };
        surface.Components.Add(new ComponentModel { Type = "media.flux" });
        return surface;
    }

    private string UniqueId(string stem)
    {
        var id = stem;
        var n = 2;
        while (_surfaces.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            id = $"{stem}-{n++}";
        return id;
    }

    // ================= list =================

    private void RebuildList()
    {
        _list.Children.Clear();
        if (_surfaces.Count == 0)
        {
            _list.Children.Add(Ui.Card(Ui.Label(L.T(
                "Aucune surface pour l'instant — ajoutez-en une.",
                "No surface yet — add one."))));
            return;
        }

        foreach (var surface in _surfaces.ToList())
        {
            _list.Children.Add(Ui.Card(BuildSurfaceCard(surface)));
        }
    }

    private StackPanel BuildSurfaceCard(SurfaceModel surface)
    {
        var card = new StackPanel();

        // header: name + category + duplicate/delete
        var header = new WrapPanel();
        var name = Ui.TextBox(surface.Id, 160);
        name.TextChanged += (_, _) => surface.Id = name.Text.Trim();
        header.Children.Add(name);

        var category = Ui.ComboBox(170);
        foreach (var (key, fr, en) in Categories)
        {
            var item = new ComboBoxItem { Content = L.T(fr, en), Tag = key };
            category.Items.Add(item);
            if (key.Equals(surface.Category, StringComparison.OrdinalIgnoreCase)) category.SelectedItem = item;
        }
        if (category.SelectedItem == null) category.SelectedIndex = 0;
        category.SelectionChanged += (_, _) =>
        {
            if ((category.SelectedItem as ComboBoxItem)?.Tag is string key) surface.Category = key;
        };
        header.Children.Add(category);

        header.Children.Add(Ui.Button(L.T("Dupliquer", "Duplicate"), (_, _) =>
        {
            var copy = CloneSurface(surface);
            copy.Id = UniqueId(surface.Id);
            _surfaces.Add(copy);
            RebuildList();
        }));
        header.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) =>
        {
            _surfaces.Remove(surface);
            RebuildList();
        }));
        card.Children.Add(header);

        // screen + zone (width × height only, per design)
        var placement = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var screenLabel = Ui.MutedLabel(L.T("Écran", "Screen"));
        screenLabel.Margin = new Thickness(0, 0, 6, 0);
        placement.Children.Add(screenLabel);
        var screen = Ui.ComboBox(200);
        screen.Items.Add(new ComboBoxItem { Content = L.T("Désactivée", "Disabled"), Tag = -1 });
        for (var i = 0; i < _screens.Count; i++)
        {
            var info = _screens[i];
            var item = new ComboBoxItem { Content = $"{L.T("ÉCRAN", "SCREEN")} {i} — {info.Bounds.Width}×{info.Bounds.Height}", Tag = i };
            screen.Items.Add(item);
            if (surface.Screens.Contains(i)) screen.SelectedItem = item;
        }
        if (screen.SelectedItem == null) screen.SelectedIndex = 0;
        screen.SelectionChanged += (_, _) =>
        {
            surface.Screens.Clear();
            if ((screen.SelectedItem as ComboBoxItem)?.Tag is int index && index >= 0)
                surface.Screens.Add(index);
        };
        placement.Children.Add(screen);

        var widthLabel = Ui.MutedLabel(L.T("Largeur", "Width"));
        widthLabel.Margin = new Thickness(8, 0, 6, 0);
        placement.Children.Add(widthLabel);
        var width = Ui.TextBox(surface.Width?.ToString() ?? "", 60);
        width.TextChanged += (_, _) => surface.Width = int.TryParse(width.Text, out var v) && v > 0 ? v : null;
        placement.Children.Add(width);
        var heightLabel = Ui.MutedLabel(L.T("Hauteur", "Height"));
        heightLabel.Margin = new Thickness(0, 0, 6, 0);
        placement.Children.Add(heightLabel);
        var height = Ui.TextBox(surface.Height?.ToString() ?? "", 60);
        height.TextChanged += (_, _) => surface.Height = int.TryParse(height.Text, out var v) && v > 0 ? v : null;
        placement.Children.Add(height);
        placement.Children.Add(Ui.MutedLabel(L.T("vide = plein écran", "empty = fullscreen")));

        placement.Children.Add(Ui.Button(L.T("Positionner sur l'écran", "Position on screen"), (_, _) => OpenCompositor(surface)));
        card.Children.Add(placement);

        if (!surface.IsFullscreen)
        {
            card.Children.Add(Ui.MutedLabel(L.T(
                $"Position actuelle : x={surface.X ?? 0}, y={surface.Y ?? 0} (réglée dans l'éditeur d'écran)",
                $"Current position: x={surface.X ?? 0}, y={surface.Y ?? 0} (set in the screen editor)")));
        }

        // streams
        card.Children.Add(Ui.MutedLabel(L.T("Flux affichés :", "Displayed streams:")));
        var streams = new WrapPanel();
        foreach (var (key, fr, en) in KnownStreams)
        {
            var box = Ui.CheckBox(L.T(fr, en), surface.Streams.Contains(key, StringComparer.OrdinalIgnoreCase));
            box.Margin = new Thickness(0, 2, 14, 2);
            box.Checked += (_, _) =>
            {
                if (!surface.Streams.Contains(key, StringComparer.OrdinalIgnoreCase)) surface.Streams.Add(key);
            };
            box.Unchecked += (_, _) => surface.Streams.RemoveAll(s => s.Equals(key, StringComparison.OrdinalIgnoreCase));
            streams.Children.Add(box);
        }
        card.Children.Add(streams);

        // components
        card.Children.Add(Ui.MutedLabel(L.T("Composants (zones en fractions de la surface) :", "Components (zones as fractions of the surface):")));
        var componentList = new StackPanel();
        BuildComponentRows(surface, componentList);
        card.Children.Add(componentList);

        var addRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var picker = Ui.ComboBox(240);
        foreach (var (key, fr, en) in ComponentTypes)
            picker.Items.Add(new ComboBoxItem { Content = L.T(fr, en), Tag = key });
        picker.SelectedIndex = 0;
        addRow.Children.Add(picker);
        addRow.Children.Add(Ui.Button(L.T("Ajouter le composant", "Add component"), (_, _) =>
        {
            if ((picker.SelectedItem as ComboBoxItem)?.Tag is string type)
            {
                surface.Components.Add(new ComponentModel { Type = type });
                BuildComponentRows(surface, componentList);
            }
        }));
        card.Children.Add(addRow);

        return card;
    }

    private void BuildComponentRows(SurfaceModel surface, StackPanel host)
    {
        host.Children.Clear();
        foreach (var component in surface.Components.ToList())
        {
            var row = new WrapPanel { Margin = new Thickness(8, 2, 0, 2) };
            var label = Ui.Label(ComponentLabel(component.Type), 12);
            label.Width = 220;
            row.Children.Add(label);

            row.Children.Add(FractionBox("x", component.X, v => component.X = v));
            row.Children.Add(FractionBox("y", component.Y, v => component.Y = v));
            row.Children.Add(FractionBox("w", component.W, v => component.W = v));
            row.Children.Add(FractionBox("h", component.H, v => component.H = v));

            // free-form option field for the types that need one
            var optionKey = component.Type switch
            {
                "external.web" => "url",
                "media.image" => "kind",
                "text.custom" => "text",
                "text.meta" => "template",
                "iccard.static" => "card",
                _ => null
            };
            if (optionKey != null)
            {
                var optLabel = Ui.MutedLabel(optionKey);
                optLabel.Margin = new Thickness(6, 0, 4, 0);
                row.Children.Add(optLabel);
                var opt = Ui.TextBox(component.Options.TryGetValue(optionKey, out var value) ? value : "", 180);
                opt.TextChanged += (_, _) => component.Options[optionKey] = opt.Text;
                row.Children.Add(opt);
            }

            row.Children.Add(Ui.Button("↑", (_, _) => MoveComponent(surface, component, -1, host)));
            row.Children.Add(Ui.Button("↓", (_, _) => MoveComponent(surface, component, +1, host)));
            row.Children.Add(Ui.Button("✕", (_, _) =>
            {
                surface.Components.Remove(component);
                BuildComponentRows(surface, host);
            }));
            host.Children.Add(row);
        }
    }

    private void MoveComponent(SurfaceModel surface, ComponentModel component, int direction, StackPanel host)
    {
        var index = surface.Components.IndexOf(component);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= surface.Components.Count) return;
        (surface.Components[index], surface.Components[target]) = (surface.Components[target], surface.Components[index]);
        BuildComponentRows(surface, host);
    }

    private static StackPanel FractionBox(string label, double value, Action<double> onChange)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 6, 0) };
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 3, 0);
        host.Children.Add(text);
        var box = Ui.TextBox(value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), 44);
        box.TextChanged += (_, _) =>
        {
            if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                onChange(Math.Clamp(parsed, -0.5, 1.5));
        };
        host.Children.Add(box);
        return host;
    }

    private static string ComponentLabel(string type)
        => ComponentTypes.FirstOrDefault(c => c.Key.Equals(type, StringComparison.OrdinalIgnoreCase)) is { Key.Length: > 0 } match
            ? L.T(match.Fr, match.En)
            : type;

    private static SurfaceModel CloneSurface(SurfaceModel source)
    {
        var copy = new SurfaceModel
        {
            Id = source.Id,
            Category = source.Category,
            X = source.X, Y = source.Y, Width = source.Width, Height = source.Height
        };
        copy.Screens.AddRange(source.Screens);
        copy.Streams.AddRange(source.Streams);
        foreach (var (key, value) in source.Params) copy.Params[key] = value;
        foreach (var component in source.Components)
        {
            var c = new ComponentModel { Type = component.Type, X = component.X, Y = component.Y, W = component.W, H = component.H };
            foreach (var (key, value) in component.Options) c.Options[key] = value;
            copy.Components.Add(c);
        }
        return copy;
    }

    // ================= compositor bridge =================

    private void OpenCompositor(SurfaceModel surface)
    {
        var screenIndex = surface.Screens.Count > 0 ? surface.Screens[0] : -1;
        if (screenIndex < 0 || screenIndex >= _screens.Count)
        {
            _status.Text = L.T("Affectez d'abord un écran à cette surface.", "Assign a screen to this surface first.");
            _status.Foreground = Ui.Error;
            return;
        }

        var editor = new ScreenCompositor(screenIndex, _screens[screenIndex],
            _surfaces.Where(s => s.Screens.Contains(screenIndex)).ToList(), surface)
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() == true)
        {
            RebuildList();
            _status.Text = L.T("Positions mises à jour — pensez à enregistrer.", "Positions updated — remember to save.");
            _status.Foreground = Ui.Muted;
        }
    }

    // ================= save =================

    private void OnSave()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in _surfaces)
        {
            if (surface.Id.Trim().Length == 0)
            {
                _status.Text = L.T("Chaque surface doit avoir un nom.", "Every surface needs a name.");
                _status.Foreground = Ui.Error;
                return;
            }
            if (!ids.Add(surface.Id))
            {
                _status.Text = L.T($"Nom de surface en double : {surface.Id}.", $"Duplicate surface name: {surface.Id}.");
                _status.Foreground = Ui.Error;
                return;
            }
        }

        _store.Save(_surfaces);
        _status.Foreground = Ui.Ok;
        _status.Text = L.T("Surfaces enregistrées (state\\surfaces.json, .bak créé).",
            "Surfaces saved (state\\surfaces.json, .bak created).");

        if (MarqueeManagerProcess.IsRunning()
            && MessageBox.Show(
                L.T("Redémarrer MarqueeManager avec les nouvelles surfaces ?", "Restart MarqueeManager with the new surfaces?"),
                "MarqueeManagerSetup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            MarqueeManagerProcess.Stop();
            MarqueeManagerProcess.Start(_pluginRoot);
        }
    }
}
