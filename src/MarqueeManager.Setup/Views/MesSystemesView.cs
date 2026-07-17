using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Per-SYSTEM settings, LedManager style: composition templates, source priority
/// chains, user drop folder, coverage and bulk pre-generation. Extracted from
/// the former "Mes composants" library section.
/// </summary>
public sealed class MesSystemesView : UserControl
{
    public MesSystemesView(string pluginRoot)
    {
        var media = new GameMediaCatalog(pluginRoot);
        var ini = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));
        var identity = new GameIdentityIndex(pluginRoot,
            ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Mes systèmes", "My systems")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Par système : quelles sources s'affichent et dans quel ordre, quel template automatique, votre dossier de médias, et la pré-génération de masse.",
            "Per system: which sources display and in what order, which automatic template, your media folder, and bulk pre-generation.")));

        if (!media.IsAvailable)
        {
            page.Children.Add(Ui.Card(Ui.Label(L.T(
                "La bibliothèque média d'APIExpose est introuvable (plugins\\APIExpose\\media).",
                "The APIExpose media library was not found (plugins\\APIExpose\\media)."))));
            Content = Ui.Page(page);
            return;
        }

        // PER-SYSTEM graphic creation — first thing on the page: same creation
        // window as the games, fed with the system's own media. Each creation
        // is INDEPENDENT per surface.
        var composeCard = new StackPanel();
        composeCard.Children.Add(Ui.SectionHeader(L.T("Création graphique d'un système", "System graphic creation")));
        composeCard.Children.Add(Ui.MutedLabel(L.T(
            "Le marquee affiché quand un SYSTÈME est sélectionné dans ES. Créez-le visuellement (logo, marquee généré, fanart du thème, textes) — il prime sur le rendu automatique.",
            "The marquee shown when a SYSTEM is selected in ES. Create it visually (logo, generated marquee, theme fanart, texts) — it overrides the automatic render.")));
        var composeRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        var systemPicker = Ui.ComboBox(200);
        // nothing preselected; only systems with at least one INSTALLED game;
        // mame/fbneo stay listed (they carry their own chains and creations)
        systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = L.T("- sélectionner -", "- select -"), Tag = null });
        var present = media.ListPresentRoms(pluginRoot);
        bool HasGames(string system) => GameMediaCatalog.ArcadeAliases.Contains(system)
            ? present.TryGetValue("arcade", out var arcade) && arcade.Count > 0
            : present.TryGetValue(system, out var roms) && roms.Count > 0;
        foreach (var system in media.ListSystems().Where(HasGames))
        {
            systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = system, Tag = system });
        }
        systemPicker.SelectedIndex = 0;
        composeRow.Children.Add(systemPicker);

        // surface picker: the creation targets ONE surface
        var surfacePicker = Ui.ComboBox(200);
        foreach (var surface in new SurfacesStore(pluginRoot).Load())
        {
            surfacePicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = $"{surface.Id} ({surface.Category})", Tag = surface.Id });
        }
        if (surfacePicker.Items.Count > 0) surfacePicker.SelectedIndex = 0;
        if (surfacePicker.Items.Count > 1) composeRow.Children.Add(surfacePicker);

        // live preview: the marquee CURRENTLY displayed for the picked system
        var preview = new System.Windows.Controls.Image
        {
            MaxHeight = 100,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new System.Windows.Thickness(0, 6, 0, 0)
        };
        var previewCaption = Ui.MutedLabel("");
        void RefreshPreview()
        {
            var system = (systemPicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            preview.Source = null;
            if (system == null)
            {
                previewCaption.Text = ""; // nothing shown until an explicit pick
                return;
            }
            var path = media.CurrentSystemMarquee(pluginRoot, system);
            previewCaption.Text = path == null
                ? L.T("Aucun marquee système pour l'instant.", "No system marquee yet.")
                : System.IO.Path.GetFileName(path).StartsWith("generated", StringComparison.OrdinalIgnoreCase)
                    ? L.T("Affiché actuellement : marquee généré.", "Currently displayed: generated marquee.")
                    : L.T("Affiché actuellement : votre création graphique.", "Currently displayed: your graphic creation.");
            if (path == null) return;
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.DecodePixelWidth = 640;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                preview.Source = bitmap;
            }
            catch
            {
                // unreadable image: caption only
            }
        }
        systemPicker.SelectionChanged += (_, _) => RefreshPreview();
        composeRow.Children.Add(Ui.Button(L.T("Ouvrir l'interface de création graphique", "Open the graphic creation interface"), (_, _) =>
        {
            if ((systemPicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag is not string system) return;
            var surfaceId = (surfacePicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var window = new GameComposerWindow(pluginRoot, "systems", system, system, SystemAssets(pluginRoot, system), surfaceId)
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            window.ShowDialog();
        }, primary: true));
        composeCard.Children.Add(composeRow);
        composeCard.Children.Add(preview);
        composeCard.Children.Add(previewCaption);
        RefreshPreview();
        page.Children.Add(Ui.Card(composeCard));

        var templates = new StackPanel();
        templates.Children.Add(Ui.SectionHeader(L.T("Templates de composition", "Composition templates")));
        templates.Children.Add(Ui.MutedLabel(L.T(
            "4 gabarits automatiques (fanart + gradient selon la luminance + logo) : 1920×360, 1280×400, 920×360 et vertical 1080×1920. "
            + "Affectez-les dans les priorités (« Template … ») : chaque jeu du système reçoit sa composition, rendue en tâche de fond ou pré-générée en masse.",
            "4 automatic recipes (fanart + luminance-driven gradient + logo): 1920×360, 1280×400, 920×360 and vertical 1080×1920. "
            + "Assign them in the priorities (“Template …”): every game of the system gets its composition, rendered in the background or pre-generated in bulk.")));
        page.Children.Add(Ui.Card(templates));

        page.Children.Add(Ui.Card(new PrioritiesCard(pluginRoot, media, identity)));

        Content = Ui.Page(page);
    }

    /// <summary>System-level media: theme logo (wheel), generated marquee/DMD, fanart when present.</summary>
    private static IReadOnlyList<GameAsset> SystemAssets(string pluginRoot, string system)
    {
        var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(pluginRoot, "..", "APIExpose", "media", "systems", system));
        var assets = new List<GameAsset>();
        void Add(string key, string fr, string en, params string[] relative)
        {
            foreach (var rel in relative)
            {
                var path = System.IO.Path.Combine(root, rel);
                if (System.IO.File.Exists(path))
                {
                    assets.Add(new GameAsset(key, L.T(fr, en), path));
                    return;
                }
            }
        }
        Add("fanart", "Fanart du système", "System fanart", @"artwork\fanart.jpg", @"artwork\fanart.png");
        Add("wheel", "Logo du système", "System logo", @"ui\wheels\wheel.png");
        Add("marquee", "Marquee généré", "Generated marquee", @"artwork\marquee\generated-system-marquee.png");
        Add("dmd", "DMD généré", "Generated DMD", @"artwork\marquee\generated-system-dmd.png");

        // system fanart: the ACTIVE ES THEME carries it (APIExpose's own cascade:
        // art/background/<system>.* etc. — carbon ships 338 of them). Same lookup
        // here, so the composer offers exactly what the runtime would show.
        if (assets.All(a => a.Key != "fanart") && ThemeSystemFanart(pluginRoot, system) is { } themeFanart)
        {
            assets.Insert(0, new GameAsset("fanart",
                L.T("Fanart du système (thème ES)", "System fanart (ES theme)"), themeFanart));
        }

        // very last resort: the first game fanart of the system
        if (assets.All(a => a.Key != "fanart"))
        {
            try
            {
                var games = System.IO.Path.Combine(root, "games");
                if (System.IO.Directory.Exists(games))
                {
                    foreach (var dir in System.IO.Directory.EnumerateDirectories(games).Take(60))
                    {
                        foreach (var candidate in new[] { @"artwork\fanart.jpg", @"artwork\fanart.png" })
                        {
                            var path = System.IO.Path.Combine(dir, candidate);
                            if (System.IO.File.Exists(path))
                            {
                                assets.Insert(0, new GameAsset("fanart",
                                    L.T($"Fanart (jeu : {System.IO.Path.GetFileName(dir)})",
                                        $"Fanart (game: {System.IO.Path.GetFileName(dir)})"), path));
                                return assets;
                            }
                        }
                    }
                }
            }
            catch
            {
                // no fallback fanart: the palette simply skips it
            }
        }
        return assets;
    }

    /// <summary>Mirror of APIExpose's theme fanart cascade for the ACTIVE theme
    /// (es_settings ThemeSet): &lt;theme&gt;\&lt;sys&gt;\art\background, &lt;theme&gt;\art\background,
    /// _systemmedia variants — first &lt;system&gt;.* image wins.</summary>
    private static string? ThemeSystemFanart(string pluginRoot, string system)
    {
        try
        {
            var esRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                pluginRoot, "..", "..", "emulationstation", ".emulationstation"));
            var settings = System.IO.Path.Combine(esRoot, "es_settings.cfg");
            if (!System.IO.File.Exists(settings)) return null;
            var themeSet = System.Xml.Linq.XDocument.Load(settings).Root?
                .Elements("string")
                .FirstOrDefault(e => (string?)e.Attribute("name") == "ThemeSet")
                ?.Attribute("value")?.Value;
            if (string.IsNullOrWhiteSpace(themeSet)) return null;
            var themeRoot = System.IO.Path.Combine(esRoot, "themes", themeSet);
            if (!System.IO.Directory.Exists(themeRoot)) return null;

            var names = GameMediaCatalog.ArcadeAliases.Contains(system)
                ? new[] { system, "arcade" }
                : new[] { system };
            foreach (var name in names)
            {
                foreach (var directory in new[]
                         {
                             System.IO.Path.Combine(themeRoot, name, "art", "background"),
                             System.IO.Path.Combine(themeRoot, name, "background"),
                             System.IO.Path.Combine(themeRoot, "art", "background"),
                             System.IO.Path.Combine(themeRoot, "_systemmedia", "fanartsyst"),
                             System.IO.Path.Combine(themeRoot, "_systemmedia", "background")
                         })
                {
                    if (!System.IO.Directory.Exists(directory)) continue;
                    var match = System.IO.Directory.EnumerateFiles(directory, name + ".*")
                        .FirstOrDefault(f => System.IO.Path.GetExtension(f).ToLowerInvariant()
                            is ".jpg" or ".jpeg" or ".png" or ".webp");
                    if (match != null) return match;
                }
            }
        }
        catch
        {
            // theme unreadable: no theme fanart
        }
        return null;
    }
}
