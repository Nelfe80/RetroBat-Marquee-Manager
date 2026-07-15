using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Per-game workshop: pick a game (system + autocomplete search over the APIExpose
/// media library), then compose its marquee, wire its MEM signals to light effects,
/// and tune its scene. Same navigation pattern as LedManagerSetup's GamesView.
/// </summary>
public sealed class GamesView : UserControl, IDisposable
{
    private readonly string _pluginRoot;
    private readonly GameMediaCatalog _media;
    private readonly MemSignalCatalog _mem;
    private readonly MarqueeProjectStore _projects;

    private IReadOnlyList<GameEntry> _allGames = Array.Empty<GameEntry>();
    private GameEntry? _current;

    private readonly ComboBox _systems = Ui.ComboBox(180);
    private readonly TextBox _search = new() { FontSize = 12, Padding = new Thickness(8, 6, 26, 6) };
    private readonly TextBlock _searchPlaceholder = Ui.MutedLabel(L.T("Rechercher un jeu…", "Search a game…"));
    private readonly ListBox _results = new() { MaxHeight = 220, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _gameHost = new();
    private MarqueeComposer? _composer;
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private bool _disposed;

    public GamesView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        _media = new GameMediaCatalog(pluginRoot);
        _mem = new MemSignalCatalog(pluginRoot);
        _projects = new MarqueeProjectStore(pluginRoot);

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mes jeux", "My games")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Composez le marquee d'un jeu à partir de ses médias, et liez ses signaux de jeu (.MEM) à des effets lumière.",
            "Compose a game's marquee from its media, and wire its in-game signals (.MEM) to light effects.")));

        if (!_media.IsAvailable)
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "La bibliothèque média d'APIExpose est introuvable (plugins\\APIExpose\\media). Installez APIExpose pour utiliser cette vue.",
                "The APIExpose media library was not found (plugins\\APIExpose\\media). Install APIExpose to use this view."))));
            Content = Ui.Page(page);
            return;
        }

        // ---- picker: system + search ----
        var picker = new StackPanel();
        var pickerRow = new WrapPanel();
        _systems.Items.Add(new ComboBoxItem { Content = L.T("Tous les systèmes", "All systems"), Tag = "" });
        foreach (var system in _media.ListSystems())
        {
            _systems.Items.Add(new ComboBoxItem { Content = system, Tag = system });
        }
        _systems.SelectedIndex = 0;
        _systems.SelectionChanged += (_, _) => RefreshResults();
        pickerRow.Children.Add(_systems);

        var searchHost = new Grid { Width = 320, Margin = new Thickness(0, 2, 0, 2) };
        _search.TextChanged += (_, _) => RefreshResults();
        _search.PreviewKeyDown += Search_KeyDown;
        searchHost.Children.Add(_search);
        _searchPlaceholder.Margin = new Thickness(10, 0, 0, 0);
        _searchPlaceholder.IsHitTestVisible = false;
        _searchPlaceholder.VerticalAlignment = VerticalAlignment.Center;
        searchHost.Children.Add(_searchPlaceholder);
        var magnifier = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Foreground = Ui.Muted,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0),
            IsHitTestVisible = false
        };
        searchHost.Children.Add(magnifier);
        pickerRow.Children.Add(searchHost);
        picker.Children.Add(pickerRow);

        _results.SelectionChanged += (_, _) =>
        {
            if (_results.SelectedItem is ListBoxItem { Tag: GameEntry entry })
            {
                OpenGame(entry);
            }
        };
        picker.Children.Add(_results);
        page.Children.Add(Ui.Card(picker));

        // ---- per-game host ----
        page.Children.Add(_gameHost);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);

        // rom index built off the UI thread (~5000 folders)
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var games = _media.ListGames();
            Dispatcher.Invoke(() =>
            {
                if (!_disposed)
                {
                    _allGames = games;
                }
            });
        });
    }

    public void Dispose()
    {
        _disposed = true;
        DisposeCards();
    }

    private void DisposeCards()
    {
        foreach (var child in _gameHost.Children)
        {
            var content = child is Border { Child: { } inner } ? inner : child;
            if (content is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    // ================= search =================

    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (_results.Visibility != Visibility.Visible || _results.Items.Count == 0)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _results.SelectedIndex = Math.Min(_results.SelectedIndex + 1, _results.Items.Count - 1);
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _results.SelectedIndex = Math.Max(_results.SelectedIndex - 1, 0);
                _results.ScrollIntoView(_results.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter when _results.SelectedItem is ListBoxItem { Tag: GameEntry entry }:
                OpenGame(entry);
                e.Handled = true;
                break;
        }
    }

    private void RefreshResults()
    {
        _searchPlaceholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var query = _search.Text.Trim();
        if (query.Length < 2)
        {
            _results.Visibility = Visibility.Collapsed;
            return;
        }

        var system = (_systems.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        var matches = _allGames
            .Where(g => system.Length == 0 || g.System.Equals(system, StringComparison.OrdinalIgnoreCase))
            .Where(g => g.Rom.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(40)
            .ToList();

        _results.Items.Clear();
        foreach (var game in matches)
        {
            _results.Items.Add(new ListBoxItem
            {
                Content = $"{game.Rom} — {game.System}",
                Tag = game,
                FontSize = 12
            });
        }
        _results.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= per-game cards =================

    private void OpenGame(GameEntry entry)
    {
        _results.Visibility = Visibility.Collapsed;
        _search.Text = "";
        _current = entry;
        _status.Text = "";
        DisposeCards();
        _gameHost.Children.Clear();

        var name = _media.ReadDisplayName(entry.System, entry.Rom) ?? entry.Rom;
        var genre = _media.ReadGenre(entry.System, entry.Rom);

        // header
        var header = new StackPanel();
        var title = Ui.Label($"{name}", 16);
        title.FontWeight = FontWeights.Bold;
        header.Children.Add(title);
        var subtitle = Ui.MutedLabel($"{entry.Rom} · {entry.System}" + (genre is { Length: > 0 } ? $" · {genre}" : ""));
        header.Children.Add(subtitle);
        _gameHost.Children.Add(Ui.Card(header));

        BuildComposerCard(entry);
        BuildEffectsCard(entry, genre);

        // scene lamps only make sense where MAME outputs exist
        if (entry.System is "arcade" or "mame" or "hbmame")
        {
            var marquee = _media.ListAssets(entry.System, entry.Rom)
                .FirstOrDefault(a => a.Key is "marquee" or "screenmarquee")?.Path;
            _gameHost.Children.Add(Ui.Card(new SceneLampsCard(_pluginRoot, entry.System, entry.Rom, marquee)));
        }
        _gameHost.Children.Add(Ui.Card(new LightingProfileCard(_pluginRoot, entry.System, entry.Rom)));
    }

    private void BuildEffectsCard(GameEntry entry, string? genre)
    {
        var memFile = _mem.FindMemFile(entry.System, entry.Rom);
        var signals = memFile != null ? _mem.ReadSignals(memFile) : Array.Empty<MemSignal>();
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var apiUrl = ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345");
        _gameHost.Children.Add(Ui.Card(new EffectsCard(_pluginRoot, entry.System, entry.Rom,
            signals, genre, _media.ReadGenreIds(entry.System, entry.Rom), apiUrl)));
    }

    private void BuildComposerCard(GameEntry entry)
    {
        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Composer le marquee", "Compose the marquee")));

        var (width, height, sourceLabel) = ResolveMarqueeSize();
        card.Children.Add(Ui.MutedLabel(sourceLabel));

        var mediaRoot = Path.GetFullPath(Path.Combine(_pluginRoot, "..", "APIExpose", "media", "systems"));
        _composer = new MarqueeComposer(width, height, mediaRoot);

        // asset palette: click a thumbnail to add it as a layer
        var assets = _media.ListAssets(entry.System, entry.Rom);
        if (assets.Count == 0)
        {
            card.Children.Add(Ui.Label(L.T("Aucun média disponible pour ce jeu.", "No media available for this game.")));
        }
        else
        {
            card.Children.Add(Ui.MutedLabel(L.T("Cliquez sur un média pour l'ajouter en calque :", "Click a media to add it as a layer:")));
            var palette = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
            foreach (var asset in assets)
            {
                palette.Children.Add(AssetThumb(asset));
            }
            palette.Children.Add(TextThumb(entry));
            card.Children.Add(palette);
        }

        card.Children.Add(_composer);

        // background controls
        var backgroundRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var bgLabel = Ui.MutedLabel(L.T("Fond :", "Background:"));
        bgLabel.Margin = new Thickness(0, 0, 6, 0);
        backgroundRow.Children.Add(bgLabel);
        var bgKind = Ui.ComboBox(170);
        bgKind.Items.Add(new ComboBoxItem { Content = L.T("Noir", "Black"), Tag = "solid" });
        bgKind.Items.Add(new ComboBoxItem { Content = L.T("Dégradé sombre", "Dark gradient"), Tag = "gradient" });
        bgKind.Items.Add(new ComboBoxItem { Content = L.T("Fanart flouté", "Blurred fanart"), Tag = "media" });
        bgKind.SelectedIndex = 0;
        bgKind.SelectionChanged += (_, _) =>
        {
            if (_composer == null || (bgKind.SelectedItem as ComboBoxItem)?.Tag is not string kind)
            {
                return;
            }

            var fanart = assets.FirstOrDefault(a => a.Key is "fanart" or "mix" or "screenshot");
            _composer.SetBackground(new MarqueeBackground
            {
                Kind = kind,
                Color = kind == "gradient" ? "#101020" : "#000000",
                Color2 = "#283048",
                Source = kind == "media" && fanart != null ? fanart.Path : null
            });
        };
        backgroundRow.Children.Add(bgKind);
        card.Children.Add(backgroundRow);

        // load an existing project
        var project = _projects.LoadProject(entry.System, entry.Rom);
        if (project != null)
        {
            _composer.LoadProject(project);
            SyncBackgroundPicker(bgKind, project.Background.Kind);
        }

        // actions
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer ma composition", "Save my composition"), (_, _) => SaveComposition(entry), primary: true));
        if (_projects.HasComposition(entry.System, entry.Rom))
        {
            actions.Children.Add(Ui.Button(L.T("Supprimer ma composition", "Delete my composition"), (_, _) => DeleteComposition(entry)));
        }
        card.Children.Add(actions);
        card.Children.Add(Ui.MutedLabel(L.T(
            "La composition est enregistrée dans media\\marquees et remplace le marquee scrapé/généré pour ce jeu.",
            "The composition is saved to media\\marquees and replaces the scraped/generated marquee for this game.")));

        _gameHost.Children.Add(Ui.Card(card));
    }

    private static void SyncBackgroundPicker(ComboBox picker, string kind)
    {
        foreach (var item in picker.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag as string == kind)
            {
                picker.SelectedItem = item;
                return;
            }
        }
    }

    private FrameworkElement AssetThumb(GameAsset asset)
    {
        var thumb = new StackPanel { Width = 92, Margin = new Thickness(0, 0, 8, 6) };
        var border = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 54,
            Cursor = Cursors.Hand
        };
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(asset.Path);
            bitmap.DecodePixelWidth = 180;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            border.Child = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
        }
        catch
        {
            border.Child = Ui.MutedLabel("?");
        }
        border.MouseLeftButtonDown += (_, _) => _composer?.AddMediaLayer(asset.Path, asset.Key);
        thumb.Children.Add(border);
        var label = Ui.MutedLabel(asset.Label, 10);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        thumb.Children.Add(label);
        return thumb;
    }

    private FrameworkElement TextThumb(GameEntry entry)
    {
        var thumb = new StackPanel { Width = 92, Margin = new Thickness(0, 0, 8, 6) };
        var border = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 54,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "Aa",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Ui.Foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        border.MouseLeftButtonDown += (_, _) =>
            _composer?.AddTextLayer(_media.ReadDisplayName(entry.System, entry.Rom) ?? entry.Rom);
        thumb.Children.Add(border);
        var label = Ui.MutedLabel(L.T("Texte", "Text"), 10);
        label.TextAlignment = TextAlignment.Center;
        thumb.Children.Add(label);
        return thumb;
    }

    /// <summary>Real marquee surface: [Screens] MarqueeBounds when set, otherwise the
    /// full resolution of the marquee screen, otherwise a 1920×360 banner.</summary>
    private (int Width, int Height, string Label) ResolveMarqueeSize()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var bounds = ini.Get("Screens", "MarqueeBounds", "");
        var parts = bounds.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 4
            && int.TryParse(parts[2], out var w) && int.TryParse(parts[3], out var h)
            && w > 0 && h > 0)
        {
            return (w, h, L.T($"Calé sur la zone marquee configurée ({w}×{h}).",
                $"Locked to the configured marquee area ({w}×{h})."));
        }

        var screenIndex = ini.GetInt("Screens", "MarqueeScreen", -1);
        var screens = Detection.ScreenProbe.Detect();
        if (screenIndex >= 0 && screenIndex < screens.Count)
        {
            var screen = screens[screenIndex];
            return (screen.Bounds.Width, screen.Bounds.Height,
                L.T($"Calé sur l'écran marquee {screenIndex} ({screen.Bounds.Width}×{screen.Bounds.Height}).",
                    $"Locked to marquee screen {screenIndex} ({screen.Bounds.Width}×{screen.Bounds.Height})."));
        }

        return (1920, 360, L.T("Aucun écran marquee configuré — format bandeau 1920×360 par défaut.",
            "No marquee screen configured — defaulting to a 1920×360 banner."));
    }

    private void SaveComposition(GameEntry entry)
    {
        if (_composer == null)
        {
            return;
        }

        if (!_composer.HasLayers)
        {
            _status.Text = L.T("Ajoutez au moins un calque avant d'enregistrer.", "Add at least one layer before saving.");
            _status.Foreground = Ui.Error;
            return;
        }

        if (!_projects.IsOwnedBySetup(entry.System, entry.Rom))
        {
            var answer = MessageBox.Show(
                L.T("Le projet existant n'a pas été créé par MarqueeManagerSetup. L'écraser ?",
                    "The existing project was not created by MarqueeManagerSetup. Overwrite it?"),
                "MarqueeManagerSetup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            _projects.SaveProject(_composer.BuildProject(entry.System, entry.Rom));
            _composer.RenderPng(_projects.PngPath(entry.System, entry.Rom));
            _status.Text = L.T(
                $"Composition enregistrée : {_projects.PngPath(entry.System, entry.Rom)}. Elle s'affichera à la prochaine sélection du jeu.",
                $"Composition saved: {_projects.PngPath(entry.System, entry.Rom)}. It will show up on the next game selection.");
            _status.Foreground = Ui.Ok;
        }
        catch (Exception ex)
        {
            _status.Text = L.T($"Échec de l'enregistrement : {ex.Message}", $"Save failed: {ex.Message}");
            _status.Foreground = Ui.Error;
        }
    }

    private void DeleteComposition(GameEntry entry)
    {
        _projects.Delete(entry.System, entry.Rom);
        _status.Text = L.T("Composition supprimée — le marquee scrapé/généré reprend la main.",
            "Composition deleted — the scraped/generated marquee takes over again.");
        _status.Foreground = Ui.Muted;
        if (_current != null)
        {
            OpenGame(_current);
        }
    }
}
