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

        // manual PER-SYSTEM composition (media\marquees\systems\<sys>.png) —
        // first thing on the page: same composer window as the games, fed with
        // the system's own media
        var composeCard = new StackPanel();
        composeCard.Children.Add(Ui.SectionHeader(L.T("Composition d'un système", "System composition")));
        composeCard.Children.Add(Ui.MutedLabel(L.T(
            "Le marquee affiché quand un SYSTÈME est sélectionné dans ES. Composez-le visuellement (logo, marquee généré, textes) — il prime sur le rendu automatique.",
            "The marquee shown when a SYSTEM is selected in ES. Compose it visually (logo, generated marquee, texts) — it overrides the automatic render.")));
        var composeRow = new WrapPanel { Margin = new System.Windows.Thickness(0, 4, 0, 0) };
        var systemPicker = Ui.ComboBox(200);
        foreach (var system in media.ListSystems())
        {
            systemPicker.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = system, Tag = system });
        }
        if (systemPicker.Items.Count > 0) systemPicker.SelectedIndex = 0;
        composeRow.Children.Add(systemPicker);
        composeRow.Children.Add(Ui.Button(L.T("Composer ce système…", "Compose this system…"), (_, _) =>
        {
            if ((systemPicker.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag is not string system) return;
            var window = new GameComposerWindow(pluginRoot, "systems", system, system, SystemAssets(pluginRoot, system))
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            window.ShowDialog();
        }, primary: true));
        composeCard.Children.Add(composeRow);
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

        // APIExpose ships no system-level fanart: fall back to the first game
        // fanart of the system so the composer always has a background to offer
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
}
