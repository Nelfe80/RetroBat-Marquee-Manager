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

                // fill the system picker with EVERY system that has installed
                // roms (media presence not required; arcade family grouped) —
                // nothing preselected: the user picks explicitly
                _systems.Items.Clear();
                _systems.Items.Add(new ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = "" });
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
                // LedManager cascade: API gamelist → roms\<sys>\gamelist.xml → pack
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var identity in await _identity.NamesAsync(system)) result.TryAdd(identity.Rom, identity.Name);

                // supplementary source: the media library metadata fills the roms
                // the cascade missed (bounded — one small json per missing rom)
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

    private void BuildComposerCard(GameEntry entry, GamePreload data)
    {
        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Mon marquee", "My marquee")));

        // the marquee CURRENTLY displayed for this game, resolved through the
        // system's priority chain — never a black box
        var assignments = new CompositionAssignments(_pluginRoot);
        var resolved = ChainPreview.Resolve(_pluginRoot, _media, assignments, "marquee", entry.System, entry.Rom);
        if (resolved.Path != null)
        {
            var currentRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            currentRow.Children.Add(ThumbImage(resolved.Path));
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var sourceLine = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            sourceLine.Inlines.Add(new System.Windows.Documents.Run(L.T("Source affichée : ", "Displayed source: ")) { Foreground = Ui.Muted });
            sourceLine.Inlines.Add(new System.Windows.Documents.Run(resolved.Label) { Foreground = Ui.Accent, FontWeight = FontWeights.SemiBold });
            side.Children.Add(sourceLine);
            var rules = Ui.MutedLabel(L.T("selon la règle de priorité du système — la modifier dans Mes systèmes",
                "per the system's priority rule — change it in My systems"), 11);
            rules.TextDecorations = TextDecorations.Underline;
            rules.Cursor = Cursors.Hand;
            rules.MouseLeftButtonDown += (_, _) => _navigate?.Invoke("systems");
            side.Children.Add(rules);
            if (resolved.Deletable)
            {
                var deleteRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                deleteRow.Children.Add(Ui.Button(L.T("Supprimer ce marquee", "Delete this marquee"), (_, _) =>
                {
                    try
                    {
                        File.Delete(resolved.Path);
                        if (resolved.Source == "composition")
                        {
                            _projects.Delete(entry.System, entry.Rom); // project json too
                        }
                        _status.Text = L.T("Marquee supprimé — la source suivante de la chaîne reprend la main.",
                            "Marquee deleted — the next source in the chain takes over.");
                        _status.Foreground = Ui.Muted;
                    }
                    catch (Exception ex)
                    {
                        _status.Text = L.T($"Suppression impossible : {ex.Message}", $"Delete failed: {ex.Message}");
                        _status.Foreground = Ui.Error;
                    }
                    if (_current != null) OpenGame(_current);
                }));
                side.Children.Add(deleteRow);
            }
            currentRow.Children.Add(side);
            card.Children.Add(currentRow);
        }
        else
        {
            card.Children.Add(Ui.MutedLabel(L.T("Aucun média résolu par la chaîne pour ce jeu (le flux d'origine s'affiche).",
                "No media resolved by the chain for this game (the stream default shows).")));
        }

        // surface picker + entry point, UNDER the game: the creation targets the
        // picked surface, and its creation can be deleted right here
        var surfaces = new SurfacesStore(_pluginRoot).Load();
        var surfaceRow = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        var surfaceLabel = Ui.MutedLabel(L.T("Surface :", "Surface:"));
        surfaceLabel.Margin = new Thickness(0, 0, 6, 0);
        surfaceLabel.VerticalAlignment = VerticalAlignment.Center;
        surfaceRow.Children.Add(surfaceLabel);
        var surfacePicker = Ui.ComboBox(210);
        foreach (var surface in surfaces)
        {
            var item = new ComboBoxItem { Content = $"{surface.Id} ({surface.Category})", Tag = surface.Id };
            surfacePicker.Items.Add(item);
            if (surface.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase)) surfacePicker.SelectedItem = item;
        }
        if (surfacePicker.SelectedItem == null && surfacePicker.Items.Count > 0) surfacePicker.SelectedIndex = 0;
        _selectedSurfaceId = (surfacePicker.SelectedItem as ComboBoxItem)?.Tag as string;
        surfaceRow.Children.Add(surfacePicker);
        surfaceRow.Children.Add(Ui.Button(
            L.T("Ouvrir l'interface de création graphique", "Open the graphic creation interface"),
            (_, _) => OpenComposer(entry, data, _selectedSurfaceId), primary: true));

        var deleteButton = Ui.Button(L.T("Supprimer la création de cette surface", "Delete this surface's creation"), (_, _) =>
        {
            var surface = surfaces.FirstOrDefault(s => s.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
            if (surface == null) return;
            new MarqueeProjectStore(_pluginRoot, CategoryOfSurface(surface), surface.Id).Delete(entry.System, entry.Rom);
            _status.Text = L.T($"Création de la surface {surface.Id} supprimée.", $"Surface {surface.Id} creation deleted.");
            _status.Foreground = Ui.Muted;
            if (_current != null) OpenGame(_current);
        });
        void RefreshDeleteButton()
        {
            var surface = surfaces.FirstOrDefault(s => s.Id.Equals(_selectedSurfaceId, StringComparison.OrdinalIgnoreCase));
            deleteButton.Visibility = surface != null
                && new MarqueeProjectStore(_pluginRoot, CategoryOfSurface(surface), surface.Id).HasComposition(entry.System, entry.Rom)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        surfacePicker.SelectionChanged += (_, _) =>
        {
            _selectedSurfaceId = (surfacePicker.SelectedItem as ComboBoxItem)?.Tag as string;
            RefreshDeleteButton();
        };
        surfaceRow.Children.Add(deleteButton);
        RefreshDeleteButton();
        card.Children.Add(surfaceRow);

        // each graphic creation is INDEPENDENT per surface: creation A on
        // surface 1, creation B on surface 2, for the same game
        var creations = 0;
        foreach (var surface in surfaces)
        {
            var category = CategoryOfSurface(surface);
            var store = new MarqueeProjectStore(_pluginRoot, category, surface.Id);
            if (!store.HasComposition(entry.System, entry.Rom)) continue;
            creations++;

            var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            var thumb = ThumbImage(store.PngPath(entry.System, entry.Rom), 64);
            thumb.Cursor = Cursors.Hand;
            thumb.ToolTip = L.T("Cliquer pour éditer", "Click to edit");
            thumb.MouseLeftButtonDown += (_, _) => OpenComposer(entry, data, surface.Id);
            row.Children.Add(thumb);
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var label = Ui.Label(L.T($"Création — surface {surface.Id}", $"Creation — surface {surface.Id}"), 12);
            label.FontWeight = FontWeights.Bold;
            side.Children.Add(label);
            var sideButtons = new WrapPanel();
            sideButtons.Children.Add(Ui.Button(L.T("Éditer…", "Edit…"), (_, _) => OpenComposer(entry, data, surface.Id)));
            sideButtons.Children.Add(Ui.Button(L.T("Supprimer", "Delete"), (_, _) =>
            {
                store.Delete(entry.System, entry.Rom);
                _status.Text = L.T($"Création de la surface {surface.Id} supprimée.",
                    $"Surface {surface.Id} creation deleted.");
                _status.Foreground = Ui.Muted;
                if (_current != null) OpenGame(_current);
            }));
            side.Children.Add(sideButtons);
            row.Children.Add(side);
            card.Children.Add(row);
        }

        if (creations == 0)
        {
            card.Children.Add(Ui.MutedLabel(L.T(
                "Pas encore de création graphique : chaque surface peut recevoir la sienne (fanart, logo, gradients, textes, médias téléchargés).",
                "No graphic creation yet: each surface can carry its own (fanart, logo, gradients, texts, downloaded media).")));
        }

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
