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
}
