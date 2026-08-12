using MarqueeManager.Compositions.Core.Composition;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarqueeManager.Compositions.Core.Resolution;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
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

    /// <summary>Surface picked in the "Mon marquee" card — the graphic creation
    /// targets THIS surface (each creation is independent per surface).</summary>
    private string? _selectedSurfaceId;

    private readonly ComboBox _systems = Ui.ComboBox(180);
    private readonly TextBox _search = new() { FontSize = 12, Padding = new Thickness(8, 6, 26, 6) };
    private readonly TextBlock _searchPlaceholder = Ui.MutedLabel(L.T("Rechercher un jeu…", "Search a game…"));
    private readonly ListBox _results = new() { MaxHeight = 220, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel _gameHost = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly Action<string>? _navigate;
    private GamePreload? _currentPreload;
    private bool _disposed;

    public GamesView(string pluginRoot, Action<string>? navigate = null)
    {
        _pluginRoot = pluginRoot;
        _navigate = navigate;
        _media = new GameMediaCatalog(pluginRoot);
        _mem = new MemSignalCatalog(pluginRoot);
        _projects = new MarqueeProjectStore(pluginRoot);

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mes jeux", "My games")));
        page.Children.Add(Ui.Subtitle(L.T(
            "La fiche complète d'un jeu : création graphique du marquee, médias en ligne, effets ingame (politique, allocation, mes effets), lampes et profil d'éclairage.",
            "A game's full sheet: marquee graphic creation, online media, ingame effects (policy, allocation, my effects), lamps and light profile.")));

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
        _systems.Items.Add(new ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = "" });
        _systems.SelectedIndex = 0;
        _systems.SelectionChanged += (_, _) =>
        {
            _ = EnsureNamesAsync(SelectedSystem());
            _search.IsEnabled = SelectedSystem() != GabaritIdentity.AllSentinel;
            RefreshResults();
            // a system with no game picked is a level of its own: it is where the
            // template that serves EVERY game of that system is edited
            _current = null;
            ShowSystemLevel();
        };

        pickerRow.Children.Add(_systems);

        var searchHost = new Grid { Width = 320, Margin = new Thickness(0, 2, 0, 2) };
        _search.TextChanged += (_, _) => RefreshResults();
        _search.PreviewKeyDown += Search_KeyDown;
        searchHost.Children.Add(_search);
        // border 1 px + padding 8 = the caret sits at 9 px; the placeholder must
        // sit at the exact same x or the text "jumps" on the first keystroke
        _searchPlaceholder.Margin = new Thickness(9, 0, 0, 0);
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

                // fill the system picker with EVERY system that has installed
                // roms (media presence not required; arcade family grouped) —
                // nothing preselected: the user picks explicitly
                _systems.Items.Clear();
                _systems.Items.Add(new ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = "" });
                // the whole library, above the separator: where the template that dresses
                // EVERY game is composed, whatever its system
                _systems.Items.Add(new ComboBoxItem { Content = L.T("Tous les jeux", "All games"), Tag = GabaritIdentity.AllSentinel });
                _systems.Items.Add(new Separator());
                foreach (var system in present.Keys
                             .Where(s => present[s].Count > 0)
                             .OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    _systems.Items.Add(new ComboBoxItem { Content = system, Tag = system });
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
            var map = await Task.Run(async () =>
            {
                // LedManager cascade: API gamelist → roms\<sys>\gamelist.xml
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var identity in await _identity.NamesAsync(system)) result.TryAdd(identity.Rom, identity.Name);

                // the gamelist pack fills the rest — comprehensive for arcade families
                // whose members split across roms folders (llander → "Lunar Lander"),
                // so a search by name finds games the per-folder gamelist never listed
                foreach (var (rom, name) in _identity.PackNames(system)) result.TryAdd(rom, name);

                // last resort: the media library metadata fills any rom still missing
                // (bounded — one small json per missing rom)
                var mediaRoms = _allGames.Where(g => g.System.Equals(system, StringComparison.OrdinalIgnoreCase))
                    .Select(g => g.Rom);
                var budget = 800;
                foreach (var rom in mediaRoms)
                {
                    if (result.ContainsKey(rom) || budget-- <= 0) continue;
                    if (_media.ReadDisplayName(system, rom) is { Length: > 0 } name) result[rom] = name;
                }
                return result;
            });
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

    /// <summary>
    /// The "system, no game" level of My games: the general template that serves EVERY
    /// game of this system. It used to be reachable only from inside a game's sheet,
    /// where it sat among cards about that one game — the scope mix that made "who does
    /// what" unreadable.
    /// </summary>
    /// <summary>Decoded fully on load: the file must not stay locked, the renderer
    /// rewrites it behind us.</summary>
    private static BitmapImage? LoadPreview(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    /// <summary>The entry a generic template is composed against: the catalog knows
    /// which game of the system actually carries media.</summary>
    private GameEntry? RichestSample(string system)
    {
        if (_media.RichestSampleRom(system) is not { Length: > 0 } rom) return null;
        return _allGames.FirstOrDefault(g => g.System.Equals(system, StringComparison.OrdinalIgnoreCase)
                                             && g.Rom.Equals(rom, StringComparison.OrdinalIgnoreCase))
               ?? new GameEntry(system, rom);
    }

    /// <summary>The sample for the library-wide template: the best-served game of any
    /// system, so a template that dresses everything is not judged on whichever system
    /// happens to come first.</summary>
    private GameEntry? RichestSampleAnywhere()
    {
        GameEntry? best = null;
        var bestCount = 0;
        foreach (var system in _allGames.Select(g => g.System).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (RichestSample(system) is not { } sample) continue;
            var count = _media.ListAssets(sample.System, sample.Rom).Count;
            if (count <= bestCount) continue;
            best = sample;
            bestCount = count;
            if (bestCount >= 10) break; // amply served: no need to weigh the rest
        }
        return best;
    }


    private void ShowSystemLevel()
    {
        DisposeCards();
        _gameHost.Children.Clear();
        var system = SelectedSystem();
        if (system.Length == 0) return;
        var allGames = system == GabaritIdentity.AllSentinel;

        var panel = new StackPanel();
        panel.Children.Add(Ui.SectionHeader(allGames
            ? L.T("Tous les jeux", "All games")
            : L.T($"Tous les jeux de « {system} »", $"All games of “{system}”")));
        panel.Children.Add(Ui.MutedLabel(allGames
            ? L.T("Le gabarit de dernier recours : la mise en page appliquée à un jeu dont ni sa fiche ni son système ne dit rien, avec les médias de CE jeu.",
                  "The template of last resort: the layout applied to a game neither its own card nor its system speaks for, resolved with THAT game's media.")
            : L.T("Le gabarit général de ce système : la mise en page appliquée à chaque jeu, avec les médias de CE jeu. Choisissez un jeu ci-dessus pour ses réglages propres.",
                  "This system's general template: the layout applied to every game, resolved with THAT game's media. Pick a game above for its own settings.")));

        // ONLY the surfaces that actually display something: a surface on a screen
        // MarqueeManager does not manage never shows anything, and offering it here only
        // invites configuring into the void.
        var store = new SurfacesStore(_pluginRoot);
        var unmanaged = store.LoadScreens()
            .Where(x => !x.ManagedByMarqueeManager && x.WindowsIndex >= 0)
            .Select(x => x.WindowsIndex)
            .ToHashSet();
        var surfaces = store.Load()
            .Where(x => x.Screens.Count > 0 && !x.Screens.All(unmanaged.Contains))
            .ToList();
        if (surfaces.Count == 0)
        {
            panel.Children.Add(Ui.MutedLabel(L.T(
                "Aucune surface active — activez un écran dans « Mon setup ».",
                "No active surface — enable a screen in “My setup”.")));
            _gameHost.Children.Add(Ui.Card(panel));
            return;
        }

        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = Ui.MutedLabel(L.T("Surface :", "Surface:"));
        label.Margin = new Thickness(0, 0, 6, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var picker = Ui.ComboBox(240);
        foreach (var surface in surfaces)
            picker.Items.Add(new ComboBoxItem { Content = $"{surface.Id} ({surface.Category})", Tag = surface.Id });
        // default to the FIRST ACTIVE surface, not to whatever was remembered
        var remembered = surfaces.FindIndex(x => x.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
        picker.SelectedIndex = remembered >= 0 ? remembered : 0;
        _selectedSurfaceId = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
        picker.SelectionChanged += (_, _) =>
        {
            _selectedSurfaceId = (picker.SelectedItem as ComboBoxItem)?.Tag as string;
            ShowSystemLevel();
        };
        row.Children.Add(picker);
        panel.Children.Add(row);

        var surfaceId = _selectedSurfaceId;
        if (surfaceId != null)
        {
            var cat = surfaces.FirstOrDefault(x => x.Id.Equals(surfaceId, StringComparison.OrdinalIgnoreCase))?.Category
                          .ToLowerInvariant() switch
            {
                "topper" => "toppers",
                "dmd-virtual" => "dmd",
                _ => "marquees"
            };
            var scope = allGames ? GabaritIdentity.GameScope : GabaritIdentity.GameScopeFor(system);
            var has = GabaritRenderer.HasGabarit(_pluginRoot, cat, surfaceId, scope);
            panel.Children.Add(Ui.MutedLabel((has, allGames) switch
            {
                (true, true) => L.T("✓ Un gabarit existe pour tous les jeux.", "✓ A template exists for all games."),
                (true, false) => L.T("✓ Un gabarit général existe pour ce système.", "✓ A general template exists for this system."),
                (false, true) => L.T("Aucun gabarit pour tous les jeux — chaque système, puis chaque jeu, répond pour lui-même.",
                                     "No template for all games — each system, then each game, answers for itself."),
                (false, false) => L.T("Aucun gabarit général pour ce système — chaque jeu utilise ses propres sources.",
                                      "No general template for this system — each game uses its own sources.")
            }));
            if (!allGames)
            {
                panel.Children.Add(Ui.MutedLabel(L.T("Il l'emporte sur le gabarit « Tous les jeux ».",
                    "It outranks the “All games” template.")));
            }

            // what this template currently produces, on this surface, for a sample game
            var sampleGame = allGames ? RichestSampleAnywhere() : RichestSample(system);
            string? preview = null;
            if (has && sampleGame != null)
            {
                preview = GabaritRenderer.GameCachePath(_pluginRoot, cat, surfaceId, sampleGame.System, sampleGame.Rom);
                // render it on demand: the runtime only bakes the games you browse, so a
                // preview that waits for the cache is a preview you never see
                if (!System.IO.File.Exists(preview))
                {
                    var dims = MediaResolutionPreview.TargetOf(
                        new SurfacesStore(_pluginRoot).Load().First(x => x.Id.Equals(surfaceId, StringComparison.OrdinalIgnoreCase)),
                        ScreenProbe.Detect());
                    preview = GabaritRenderer.RenderGame(_pluginRoot, cat, surfaceId, sampleGame.System, sampleGame.Rom,
                        dims.Width, dims.Height, _media.ListAssets(sampleGame.System, sampleGame.Rom));
                }
            }
            if (preview != null && System.IO.File.Exists(preview))
            {
                panel.Children.Add(Ui.MutedLabel(L.T($"Aperçu (exemple : {sampleGame!.Rom})", $"Preview (sample: {sampleGame!.Rom})")));
                panel.Children.Add(new Image
                {
                    Source = LoadPreview(preview),
                    Stretch = Stretch.Uniform,
                    MaxHeight = 220,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 4)
                });
            }

            var edit = Ui.Button(has
                ? L.T("Modifier le gabarit général", "Edit the general template")
                : L.T("Créer le gabarit général", "Create the general template"), (_, _) =>
            {
                var sample = allGames ? RichestSampleAnywhere() : RichestSample(system);
                var assets = sample != null ? _media.ListAssets(sample.System, sample.Rom) : new List<GameAsset>();
                new GameComposerWindow(_pluginRoot, GabaritIdentity.SystemId, scope,
                    allGames
                        ? L.T("Gabarit — tous les jeux", "Template — all games")
                        : L.T($"Gabarit général — jeux {system}", $"General template — {system} games"),
                    assets, surfaceId, gabaritMode: true)
                {
                    Owner = Window.GetWindow(this)
                }.ShowDialog();
                GabaritRenderer.InvalidateSurface(_pluginRoot, cat, surfaceId);
                ShowSystemLevel();
            }, primary: true);
            edit.Margin = new Thickness(0, 8, 0, 0);
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            panel.Children.Add(edit);
        }

        _gameHost.Children.Add(Ui.Card(panel));
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

        var system = SelectedSystem();
        // "All games" is a level, not a system: there is no entry to search for there,
        // and a box that answers nothing is worse than one that is plainly shut
        if (system.Length == 0 || system == GabaritIdentity.AllSentinel)
        {
            _results.Visibility = Visibility.Collapsed;
            return;
        }
        _ = EnsureNamesAsync(system);

        // LedManager engine: the candidates are the INSTALLED ROMS of the system
        // (media presence is NOT required — llander has no media folder and must
        // still be findable), matched on rom OR display name once names load
        var candidates = _present.TryGetValue(system, out var installed)
            ? (IEnumerable<string>)installed
            : _allGames.Where(g => g.System.Equals(system, StringComparison.OrdinalIgnoreCase)).Select(g => g.Rom);
        var names = _namesCache.TryGetValue(system, out var loaded)
            ? loaded
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var normalized = Normalize(query);
        var matches = candidates
            .Where(rom => Normalize(rom).Contains(normalized, StringComparison.OrdinalIgnoreCase)
                          || (names.TryGetValue(rom, out var n)
                              && (n.Contains(query, StringComparison.OrdinalIgnoreCase)
                                  || Normalize(n).Contains(normalized, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(rom => rom, StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList();

        _results.Items.Clear();
        foreach (var rom in matches)
        {
            var game = new GameEntry(system, rom);
            // LedManager presentation: game name prominent, rom as a discreet line
            var content = new StackPanel();
            var hasName = names.TryGetValue(rom, out var name) && !rom.Equals(name, StringComparison.OrdinalIgnoreCase);
            content.Children.Add(new TextBlock
            {
                Text = hasName ? name : rom,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (hasName)
            {
                content.Children.Add(new TextBlock { Text = rom, FontSize = 10.5, Opacity = 0.62 });
            }
            var item = new ListBoxItem { Tag = game, Content = content };
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
        _currentPreload = data;
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
            // FIRST and default when it exists: the artwork the lamp regions were
            // measured on. The runtime lights THAT image whatever the resolution chain
            // produced, so it is the only background on which placing lamps means
            // anything — offering anything else first would let the user aim at an
            // image that is never displayed.
            var calibrated = SceneLampsCard.CalibratedBackground(_pluginRoot, entry.Rom);
            if (calibrated != null)
                backgrounds.Add((L.T("Artwork calibré (affiché en jeu)", "Calibrated artwork (shown in game)"), calibrated));
            // then the surface's own composition, which is what the lighting engine
            // actually lights when the surface stacks layers under it. Lamps are placed
            // in FRACTIONS of the lit image: aiming them at another picture puts them
            // beside their target.
            var composed = ComposedBackground(entry.System, entry.Rom);
            if (composed != null)
                backgrounds.Add((L.T("Ma composition (éclairée)", "My composition (lit)"), composed));
            var generated = Path.Combine(_media.GameRoot(entry.System, entry.Rom), "artwork", "marquee", "generated-marquee.png");
            if (File.Exists(generated)) backgrounds.Add((L.T("Marquee généré", "Generated marquee"), generated));
            if (_projects.HasComposition(entry.System, entry.Rom))
                backgrounds.Add((L.T("Ma création graphique", "My graphic creation"), _projects.PngPath(entry.System, entry.Rom)));
            foreach (var asset in data.Assets.Where(a => a.Key is "marquee" or "screenmarquee"))
                backgrounds.Add((asset.Label, asset.Path));
            _gameHost.Children.Add(Ui.Card(new SceneLampsCard(_pluginRoot, entry.System, entry.Rom, backgrounds)));
        }
        _gameHost.Children.Add(Ui.Card(new LightingProfileCard(_pluginRoot, entry.System, entry.Rom)));

        _gameHost.Children.Add(Ui.Card(new EffectsCard(_pluginRoot, entry.System, entry.Rom,
            data.Signals, data.Genre, data.GenreIds, data.ApiUrl, data.MemPath)));
    }

    private static Image ThumbImage(string path, double maxHeight = 84)
    {
        var image = new Image
        {
            MaxHeight = maxHeight, MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 10, 0)
        };
        _ = Task.Run(() =>
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 640;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                image.Dispatcher.BeginInvoke(() => image.Source = bitmap);
            }
            catch
            {
                // preview unavailable
            }
        });
        return image;
    }

    private static string CategoryOfSurface(SurfaceModel surface) => surface.Category.ToLowerInvariant() switch
    {
        "topper" => "toppers",
        "dmd-virtual" => "dmd",
        _ => "marquees"
    };

    /// <summary>The flattened composition of the SELECTED surface for this game — the
    /// image the lighting engine lights when the surface stacks bakeable layers under
    /// it. Rendered lazily by the runtime, so it exists once the game has been browsed.
    /// Both system spellings are tried (the runtime names the folder after what the
    /// stream sent it: "mame" where the view says "arcade").</summary>
    private string? ComposedBackground(string system, string rom)
    {
        if (string.IsNullOrEmpty(_selectedSurfaceId)) return null;
        var safe = new Func<string, string>(name =>
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            return new string(name.ToLowerInvariant().Where(c => !invalid.Contains(c)).ToArray());
        });
        foreach (var sys in new[] { system, system.Equals("mame", StringComparison.OrdinalIgnoreCase) ? "arcade" : "mame" })
        {
            var path = System.IO.Path.Combine(_pluginRoot, "media", "marquees", ".cache", "surfaces",
                safe(_selectedSurfaceId!), "games", safe(sys), safe(rom), "navigation.png");
            if (System.IO.File.Exists(path)) return path;
        }
        return null;
    }

    private void BuildComposerCard(GameEntry entry, GamePreload data)
    {
        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Mon marquee", "My marquee")));

        var assignments = new CompositionAssignments(_pluginRoot);

        // surface picker only — the preview and the compose/delete actions live on
        // the resolution cards below (like Mes systèmes)
        var surfacesStore = new SurfacesStore(_pluginRoot);
        var surfaces = surfacesStore.Load();
        // a surface whose screen(s) the user excluded from MarqueeManager is SUSPENDED
        // and kept out of the picker by default, exactly like Mes systèmes
        var unmanagedScreens = surfacesStore.LoadScreens()
            .Where(s => !s.ManagedByMarqueeManager && s.WindowsIndex >= 0)
            .Select(s => s.WindowsIndex)
            .ToHashSet();
        bool IsSuspended(SurfaceModel s) => s.Screens.Count > 0 && s.Screens.All(unmanagedScreens.Contains);

        var surfaceRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var surfaceLabel = Ui.MutedLabel(L.T("Surface :", "Surface:"));
        surfaceLabel.Margin = new Thickness(0, 0, 6, 0);
        surfaceLabel.VerticalAlignment = VerticalAlignment.Center;
        surfaceRow.Children.Add(surfaceLabel);
        var surfacePicker = Ui.ComboBox(210);
        var showSuspended = Ui.CheckBox(L.T("Afficher les surfaces suspendues", "Show suspended surfaces"), false);
        showSuspended.Margin = new Thickness(12, 0, 0, 0);
        showSuspended.VerticalAlignment = VerticalAlignment.Center;

        void RebuildSurfacePicker()
        {
            var previous = _selectedSurfaceId;
            surfacePicker.Items.Clear();
            foreach (var surface in surfaces)
            {
                var suspended = IsSuspended(surface);
                if (suspended && showSuspended.IsChecked != true) continue;
                var item = new ComboBoxItem
                {
                    Content = $"{surface.Id} ({surface.Category})" + (suspended ? L.T("  · suspendue", "  · suspended") : ""),
                    Tag = surface.Id
                };
                surfacePicker.Items.Add(item);
                if (surface.Id.Equals(previous, StringComparison.OrdinalIgnoreCase)) surfacePicker.SelectedItem = item;
            }
            if (surfacePicker.SelectedItem == null && surfacePicker.Items.Count > 0) surfacePicker.SelectedIndex = 0;
            _selectedSurfaceId = (surfacePicker.SelectedItem as ComboBoxItem)?.Tag as string;
        }
        RebuildSurfacePicker();
        surfaceRow.Children.Add(surfacePicker);
        surfaceRow.Children.Add(showSuspended);
        card.Children.Add(surfaceRow);

        // shared block card: what displays for THIS game on the picked surface, and
        // the per-source "Use" overrides — persisted PER GAME in media-presentation.json
        var engine = new MediaResolutionPreview(_pluginRoot, _media, assignments);
        var screens = ScreenProbe.Detect();
        var resolutionCard = new ResolutionCard(engine);
        void UpdateResolutionCard()
        {
            var surface = surfaces.FirstOrDefault(s => s.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
            // render the surface's game gabarit for THIS game once (cached) so the
            // "Générée" card reflects the general template, like Mes systèmes
            if (surface != null)
            {
                var cat = CategoryOfSurface(surface);
                // covered by its system's template OR by the one for all games
                if (GabaritRenderer.HasGameGabarit(_pluginRoot, cat, surface.Id, entry.System)
                    && !System.IO.File.Exists(GabaritRenderer.GameCachePath(_pluginRoot, cat, surface.Id, entry.System, entry.Rom)))
                {
                    var dims = MediaResolutionPreview.TargetOf(surface, screens);
                    GabaritRenderer.RenderGame(_pluginRoot, cat, surface.Id, entry.System, entry.Rom, dims.Width, dims.Height, data.Assets);
                }
            }
            ResolutionContext? ctx = surface != null ? engine.GameContext(surface, screens, entry.System, entry.Rom) : null;
            resolutionCard.Update(ctx,
                composePersonal: () => OpenComposer(entry, data, _selectedSurfaceId),
                editGabarit: () =>
                {
                    var s = surfaces.FirstOrDefault(x => x.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
                    if (s == null) return;
                    // general template composed with the current game's assets as a
                    // concrete preview; it applies to every game of THIS system (per-system)
                    new GameComposerWindow(_pluginRoot, GabaritIdentity.SystemId, GabaritIdentity.GameScopeFor(entry.System),
                        L.T($"Gabarit général — jeux {entry.System} (aperçu : {entry.Rom})", $"General template — {entry.System} games (preview: {entry.Rom})"),
                        data.Assets, s.Id, gabaritMode: true)
                    {
                        Owner = Window.GetWindow(this)
                    }.ShowDialog();
                    // the recipe changed → drop the cached renders so they regenerate
                    GabaritRenderer.InvalidateSurface(_pluginRoot, CategoryOfSurface(s), s.Id);
                    if (_current != null) OpenGame(_current);
                },
                deletePersonal: () =>
                {
                    var target = surfaces.FirstOrDefault(s => s.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
                    if (target == null) return;
                    new MarqueeProjectStore(_pluginRoot, CategoryOfSurface(target), target.Id).Delete(entry.System, entry.Rom);
                    new MarqueeProjectStore(_pluginRoot, CategoryOfSurface(target)).Delete(entry.System, entry.Rom);
                    if (_current != null) OpenGame(_current);
                });
        }
        surfacePicker.SelectionChanged += (_, _) =>
        {
            _selectedSurfaceId = (surfacePicker.SelectedItem as ComboBoxItem)?.Tag as string;
            UpdateResolutionCard();
        };
        showSuspended.Checked += (_, _) => { RebuildSurfacePicker(); UpdateResolutionCard(); };
        showSuspended.Unchecked += (_, _) => { RebuildSurfacePicker(); UpdateResolutionCard(); };
        UpdateResolutionCard();
        card.Children.Add(resolutionCard);

        // no separate list below: the "Ma création" card shows this surface's creation
        // in its own preview with Édite/Supprimer — switch the surface to see each one

        _gameHost.Children.Add(Ui.Card(card));
    }

    private void OpenComposer(GameEntry entry, GamePreload data, string? surfaceId)
    {
        var window = new Controls.GameComposerWindow(_pluginRoot, entry.System, entry.Rom, data.Name, data.Assets, surfaceId)
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
        _status.Text = L.T("Création graphique supprimée — le marquee scrapé/généré reprend la main.",
            "Composition deleted — the scraped/generated marquee takes over again.");
        _status.Foreground = Ui.Muted;
        if (_current != null)
        {
            OpenGame(_current);
        }
    }
}
