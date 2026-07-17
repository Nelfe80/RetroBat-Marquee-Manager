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
    private GameIdentityIndex? _identity;
    private readonly Dictionary<string, Dictionary<string, string>> _namesCache = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, HashSet<string>> _present = new(StringComparer.OrdinalIgnoreCase);
    private int _openSequence;

    private readonly ComboBox _systems = Ui.ComboBox(180);
    private readonly TextBox _search = new() { FontSize = 12, Padding = new Thickness(8, 6, 26, 6) };
    private readonly TextBlock _searchPlaceholder = Ui.MutedLabel(L.T("Rechercher un jeu…", "Search a game…"));
    private readonly ListBox _results = new() { MaxHeight = 220, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _gameHost = new();
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
            "La fiche complète d'un jeu : composition du marquee, médias en ligne, effets ingame (politique, allocation, mes effets), lampes et profil d'éclairage.",
            "A game's full sheet: marquee composition, online media, ingame effects (policy, allocation, my effects), lamps and light profile.")));

        if (!_media.IsAvailable)
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "La bibliothèque média d'APIExpose est introuvable (plugins\\APIExpose\\media). Installez APIExpose pour utiliser cette vue.",
                "The APIExpose media library was not found (plugins\\APIExpose\\media). Install APIExpose to use this view."))));
            Content = Ui.Page(page);
            return;
        }

        var iniBoot = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));
        _identity = new GameIdentityIndex(pluginRoot, iniBoot.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));

        // ---- picker: system + search ----
        var picker = new StackPanel();
        var pickerRow = new WrapPanel();
        // the system list fills once the physical-presence index is built: only
        // systems with INSTALLED roms show up ("all systems" was unusably long)
        _systems.Items.Add(new ComboBoxItem { Content = L.T("(détection des roms…)", "(detecting roms…)"), Tag = "" });
        _systems.SelectedIndex = 0;
        _systems.SelectionChanged += (_, _) =>
        {
            _ = EnsureNamesAsync(SelectedSystem());
            RefreshResults();
        };

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

        picker.Children.Add(_results);
        page.Children.Add(Ui.Card(picker));

        // ---- per-game host ----
        page.Children.Add(_gameHost);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);

        // rom index + physical-presence index built off the UI thread (~5000 folders)
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            var games = _media.ListGames();
            var present = _media.ListPresentRoms(pluginRoot);
            Dispatcher.Invoke(() =>
            {
                if (_disposed) return;
                _allGames = games;
                _present = present;

                // fill the system picker with the INSTALLED systems only
                _systems.Items.Clear();
                foreach (var system in _media.ListSystems()
                             .Where(s => present.ContainsKey(s) && games.Any(g => g.System == s && present[s].Contains(g.Rom))))
                {
                    _systems.Items.Add(new ComboBoxItem { Content = system, Tag = system });
                }
                if (_systems.Items.Count == 0)
                {
                    _systems.Items.Add(new ComboBoxItem { Content = L.T("(aucune rom détectée)", "(no rom detected)"), Tag = "" });
                }
                _systems.SelectedIndex = 0;
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

    private string SelectedSystem() => (_systems.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    /// <summary>Display names (ES gamelist / pack) loaded once per system, off the
    /// UI thread — the search matches rom AND name as soon as they arrive.</summary>
    private async Task EnsureNamesAsync(string system)
    {
        if (system.Length == 0 || _identity == null || _namesCache.ContainsKey(system)) return;
        try
        {
            var names = await Task.Run(() => _identity.NamesAsync(system));
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var identity in names) map.TryAdd(identity.Rom, identity.Name);
            if (!_disposed)
            {
                _namesCache[system] = map;
                RefreshResults();
            }
        }
        catch
        {
            // names unavailable: rom-only search keeps working
        }
    }

    private string DisplayNameOf(GameEntry game)
        => _namesCache.TryGetValue(game.System, out var names) && names.TryGetValue(game.Rom, out var name)
            ? name
            : game.Rom;

    /// <summary>"sea wolf" must find the rom "seawolf": queries and roms compare
    /// with everything but letters and digits stripped.</summary>
    private static string Normalize(string text)
        => new(text.Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Only games whose rom physically exists in RetroBat's roms\ folders
    /// (a system without any roms folder is left unfiltered).</summary>
    private bool IsPresent(GameEntry game)
        => !_present.TryGetValue(game.System, out var roms) || roms.Contains(game.Rom);

    private void RefreshResults()
    {
        _searchPlaceholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var query = _search.Text.Trim();
        if (query.Length < 2)
        {
            _results.Visibility = Visibility.Collapsed;
            return;
        }

        var system = SelectedSystem();
        if (system.Length == 0)
        {
            _results.Visibility = Visibility.Collapsed;
            return;
        }
        _ = EnsureNamesAsync(system);

        var normalized = Normalize(query);
        var matches = _allGames
            .Where(g => g.System.Equals(system, StringComparison.OrdinalIgnoreCase))
            .Where(IsPresent)
            .Where(g => Normalize(g.Rom).Contains(normalized, StringComparison.OrdinalIgnoreCase)
                        || DisplayNameOf(g).Contains(query, StringComparison.OrdinalIgnoreCase)
                        || Normalize(DisplayNameOf(g)).Contains(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(100)
            .ToList();

        _results.Items.Clear();
        foreach (var game in matches)
        {
            var name = DisplayNameOf(game);
            var item = new ListBoxItem
            {
                Content = name.Equals(game.Rom, StringComparison.OrdinalIgnoreCase)
                    ? $"{game.Rom} — {game.System}"
                    : $"{name} ({game.Rom}) — {game.System}",
                Tag = game,
                FontSize = 12
            };
            // open on CLICK (not SelectionChanged): re-picking the same entry or
            // searching again after a selection always works
            item.PreviewMouseLeftButtonUp += (_, _) => OpenGame(game);
            _results.Items.Add(item);
        }
        _results.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= per-game cards =================

    /// <summary>Everything the game sheet needs, read OFF the UI thread — the
    /// click shows a spinner instantly instead of freezing on I/O.</summary>
    private sealed record GamePreload(
        string Name, string? Genre, string? GenreIds,
        IReadOnlyList<GameAsset> Assets, IReadOnlyList<MemSignal> Signals,
        string ApiUrl, (int Width, int Height, string Label) MarqueeSize,
        string? MemPath);

    private void OpenGame(GameEntry entry)
    {
        _results.Visibility = Visibility.Collapsed;
        _results.Items.Clear();
        _results.SelectedItem = null;
        _search.Text = "";
        _current = entry;
        _status.Text = "";
        DisposeCards();
        _gameHost.Children.Clear();
        _gameHost.Children.Add(Ui.Card(Ui.Spinner(L.T("Chargement du jeu…", "Loading the game…"))));
        var sequence = ++_openSequence;
        _ = OpenGameAsync(entry, sequence);
    }

    private async Task OpenGameAsync(GameEntry entry, int sequence)
    {
        GamePreload data;
        try
        {
            data = await Task.Run(() =>
            {
                var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
                var memFile = _mem.FindMemFile(entry.System, entry.Rom);
                return new GamePreload(
                    _media.ReadDisplayName(entry.System, entry.Rom) ?? entry.Rom,
                    _media.ReadGenre(entry.System, entry.Rom),
                    _media.ReadGenreIds(entry.System, entry.Rom),
                    _media.ListAssets(entry.System, entry.Rom),
                    memFile != null ? _mem.ReadSignals(memFile) : Array.Empty<MemSignal>(),
                    ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"),
                    ResolveMarqueeSize(),
                    memFile);
            });
        }
        catch (Exception ex)
        {
            if (_disposed || sequence != _openSequence) return;
            _gameHost.Children.Clear();
            _status.Text = L.T($"Chargement impossible : {ex.Message}", $"Load failed: {ex.Message}");
            _status.Foreground = Ui.Error;
            return;
        }

        if (_disposed || sequence != _openSequence) return;
        DisposeCards();
        _gameHost.Children.Clear();

        // header
        var header = new StackPanel();
        var title = Ui.Label(data.Name, 16);
        title.FontWeight = FontWeights.Bold;
        header.Children.Add(title);
        var subtitle = Ui.MutedLabel($"{entry.Rom} · {entry.System}" + (data.Genre is { Length: > 0 } ? $" · {data.Genre}" : ""));
        header.Children.Add(subtitle);
        _gameHost.Children.Add(Ui.Card(header));

        // card order: fetch media online FIRST (it feeds the composer), then the
        // compositions, lamps, lighting — and the ingame effects LAST
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var scraper = new MediaScraperService(_pluginRoot, key => ini.Get("Scraper", key, ""));
        _gameHost.Children.Add(Ui.Card(new ScrapeCard(scraper, entry.System, entry.Rom, data.Name,
            (path, _) =>
            {
                _status.Text = L.T($"Téléchargé : {Path.GetFileName(path)} — proposé dans le compositeur (médias téléchargés).",
                    $"Downloaded: {Path.GetFileName(path)} — offered in the composer (downloaded media).");
                _status.Foreground = Ui.Ok;
            })));

        BuildComposerCard(entry, data);

        // scene lamps only make sense where MAME outputs exist; the generated
        // marquee is the preferred lamp background, with a selector when several
        if (entry.System is "arcade" or "mame" or "hbmame")
        {
            var backgrounds = new List<(string Label, string Path)>();
            var generated = Path.Combine(_media.GameRoot(entry.System, entry.Rom), "artwork", "marquee", "generated-marquee.png");
            if (File.Exists(generated)) backgrounds.Add((L.T("Marquee généré", "Generated marquee"), generated));
            if (_projects.HasComposition(entry.System, entry.Rom))
                backgrounds.Add((L.T("Ma composition", "My composition"), _projects.PngPath(entry.System, entry.Rom)));
            foreach (var asset in data.Assets.Where(a => a.Key is "marquee" or "screenmarquee"))
                backgrounds.Add((asset.Label, asset.Path));
            _gameHost.Children.Add(Ui.Card(new SceneLampsCard(_pluginRoot, entry.System, entry.Rom, backgrounds)));
        }
        _gameHost.Children.Add(Ui.Card(new LightingProfileCard(_pluginRoot, entry.System, entry.Rom)));

        _gameHost.Children.Add(Ui.Card(new EffectsCard(_pluginRoot, entry.System, entry.Rom,
            data.Signals, data.Genre, data.GenreIds, data.ApiUrl, data.MemPath)));
    }

    private void BuildComposerCard(GameEntry entry, GamePreload data)
    {
        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Composer le marquee", "Compose the marquee")));

        // one composition per surface family (marquee / topper / DMD): every
        // existing one shows here — click it to edit THAT one
        var categories = new (string Category, string Fr, string En)[]
        {
            ("marquees", "Marquee", "Marquee"),
            ("toppers", "Topper", "Topper"),
            ("dmd", "DMD", "DMD")
        };
        var found = 0;
        foreach (var (category, fr, en) in categories)
        {
            var store = new MarqueeProjectStore(_pluginRoot, category);
            if (!store.HasComposition(entry.System, entry.Rom)) continue;
            found++;

            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            var preview = new Image
            {
                MaxHeight = 84, MaxWidth = 420,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand,
                ToolTip = L.T("Cliquer pour éditer", "Click to edit")
            };
            var pngPath = store.PngPath(entry.System, entry.Rom);
            _ = Task.Run(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(pngPath);
                    bitmap.DecodePixelWidth = 640;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    Dispatcher.BeginInvoke(() => preview.Source = bitmap);
                }
                catch
                {
                    // preview unavailable
                }
            });
            preview.MouseLeftButtonDown += (_, _) => OpenComposer(entry, data, category);
            row.Children.Add(preview);

            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Ui.Label(L.T(fr, en), 12);
            label.FontWeight = FontWeights.Bold;
            side.Children.Add(label);
            var sideButtons = new WrapPanel();
            sideButtons.Children.Add(Ui.Button(L.T("Éditer…", "Edit…"), (_, _) => OpenComposer(entry, data, category)));
            sideButtons.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) =>
            {
                store.Delete(entry.System, entry.Rom);
                _status.Text = L.T($"Composition {L.T(fr, en)} supprimée — la chaîne de sources reprend la main.",
                    $"{L.T(fr, en)} composition deleted — the source chain takes over again.");
                _status.Foreground = Ui.Muted;
                if (_current != null) OpenGame(_current);
            }));
            side.Children.Add(sideButtons);
            row.Children.Add(side);
            card.Children.Add(row);
        }

        card.Children.Add(Ui.MutedLabel(found > 0
            ? L.T("Une composition manuelle prime sur toutes les autres sources de sa catégorie.",
                "A manual composition overrides every other source of its category.")
            : L.T("Pas encore de composition manuelle : le compositeur assemble fanart, logo, gradients, textes et médias téléchargés — une composition par surface (marquee, topper, DMD).",
                "No manual composition yet: the composer assembles fanart, logo, gradients, texts and downloaded media — one composition per surface (marquee, topper, DMD).")));

        var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(Ui.Button(
            found > 0 ? L.T("Nouvelle composition…", "New composition…") : L.T("Ouvrir le compositeur…", "Open the composer…"),
            (_, _) => OpenComposer(entry, data, null), primary: true));
        card.Children.Add(actions);
        _gameHost.Children.Add(Ui.Card(card));
    }

    private void OpenComposer(GameEntry entry, GamePreload data, string? category)
    {
        var window = new Controls.GameComposerWindow(_pluginRoot, entry.System, entry.Rom, data.Name, data.Assets, category)
        {
            Owner = Window.GetWindow(this)
        };
        window.ShowDialog();
        if (_current != null) OpenGame(_current); // refresh the previews
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
