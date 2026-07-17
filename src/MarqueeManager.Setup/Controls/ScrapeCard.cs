using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Controls;

/// <summary>
/// "Récupérer des médias en ligne" panel of the game sheet: pick the sources,
/// search, preview thumbnails, click to download into media\marquees\downloads\
/// and hand the file to the composer as a new layer.
/// </summary>
public sealed class ScrapeCard : UserControl
{
    // ScreenScraper appears only when DEV credentials resolve (env / APIExpose
    // .env / build-embedded); unchecked by default — APIExpose mirrors it locally.
    private static readonly (string Key, string Label, bool DefaultChecked)[] Sources =
    {
        ("adb", "Arcade Database", true),
        ("steamgriddb", "SteamGridDB", true),
        ("thegamesdb", "TheGamesDB", true),
        ("screenscraper", "ScreenScraper", false)
    };

    private readonly MediaScraperService _scraper;
    private readonly string _system;
    private readonly string _rom;
    private readonly string _gameName;
    private readonly Action<string, string> _addLayer; // (path, kind)
    private readonly List<CheckBox> _sourceBoxes = new();
    private readonly WrapPanel _results = new();
    private readonly TextBlock _status = Ui.MutedLabel("", 12);

    public ScrapeCard(MediaScraperService scraper, string system, string rom, string gameName,
        Action<string, string> addLayer)
    {
        _scraper = scraper;
        _system = system;
        _rom = rom;
        _gameName = gameName;
        _addLayer = addLayer;

        var card = new StackPanel();
        card.Children.Add(Ui.SectionHeader(L.T("Récupérer des médias en ligne", "Fetch media online")));

        var row = new WrapPanel();
        foreach (var (key, label, defaultChecked) in Sources)
        {
            var hasKey = _scraper.HasKey(key);
            if (key == "screenscraper" && !hasKey) continue; // no dev creds → hidden, not "missing key"
            var box = Ui.CheckBox(label + (hasKey ? "" : L.T(" (clé manquante)", " (missing key)")),
                hasKey && defaultChecked);
            box.IsEnabled = hasKey;
            box.Tag = key;
            box.Margin = new Thickness(0, 2, 14, 2);
            _sourceBoxes.Add(box);
            row.Children.Add(box);
        }
        row.Children.Add(Ui.Button(L.T("Rechercher", "Search"), (_, _) => _ = SearchAsync(), primary: true));
        card.Children.Add(row);
        card.Children.Add(Ui.MutedLabel(L.T(
            "Clés API dans Options → Sources en ligne. Cliquez sur un résultat pour le télécharger et l'ajouter en calque.",
            "API keys in Options → Online sources. Click a result to download it and add it as a layer.")));
        card.Children.Add(_results);
        card.Children.Add(Ui.MutedLabel(L.T(
            "Cliquez sur un média pour l'importer et pouvoir l'utiliser dans le compositeur.",
            "Click a media to import it and use it in the composer.")));
        _status.TextWrapping = TextWrapping.Wrap;
        card.Children.Add(_status);
        Content = card;
    }

    private async Task SearchAsync()
    {
        var sources = _sourceBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag).ToList();
        if (sources.Count == 0)
        {
            _status.Text = L.T("Cochez au moins une source.", "Check at least one source.");
            _status.Foreground = Ui.Error;
            return;
        }

        _results.Children.Clear();
        _results.Children.Add(Ui.Spinner(L.T("Recherche en ligne…", "Searching online…")));
        _status.Text = "";

        var found = await _scraper.SearchAsync(sources, _system, _rom, _gameName);
        _results.Children.Clear();
        if (found.Count == 0)
        {
            _status.Text = L.T("Aucun média trouvé.", "No media found.");
            _status.Foreground = Ui.Muted;
            return;
        }

        foreach (var result in found)
        {
            _results.Children.Add(ResultThumb(result));
        }
        _status.Text = L.T($"{found.Count} média(s) trouvé(s).", $"{found.Count} media found.");
        _status.Foreground = Ui.Muted;
    }

    private FrameworkElement ResultThumb(ScrapeResult result)
    {
        var host = new StackPanel { Width = 120, Margin = new Thickness(0, 4, 8, 4) };
        var border = new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 68,
            Cursor = Cursors.Hand
        };
        var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(3) };
        border.Child = image;
        try
        {
            // http thumbnails download asynchronously on their own
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(result.ThumbUrl);
            bitmap.DecodePixelWidth = 220;
            bitmap.EndInit();
            image.Source = bitmap;
        }
        catch
        {
            border.Child = Ui.MutedLabel("?");
        }

        border.MouseLeftButtonDown += async (_, _) =>
        {
            _status.Text = L.T("Téléchargement…", "Downloading…");
            _status.Foreground = Ui.Muted;
            try
            {
                var path = await _scraper.DownloadAsync(result, _system, _rom);
                _addLayer(path, result.Kind);
                _status.Text = L.T($"Ajouté en calque : {System.IO.Path.GetFileName(path)}",
                    $"Added as a layer: {System.IO.Path.GetFileName(path)}");
                _status.Foreground = Ui.Ok;
            }
            catch (Exception ex)
            {
                _status.Text = L.T($"Téléchargement impossible : {ex.Message}", $"Download failed: {ex.Message}");
                _status.Foreground = Ui.Error;
            }
        };

        host.Children.Add(border);
        var label = Ui.MutedLabel($"{result.Source} · {result.Kind}", 10);
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        host.Children.Add(label);
        return host;
    }
}
