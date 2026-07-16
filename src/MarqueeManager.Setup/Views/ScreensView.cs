using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Phase 1 of the spec: enumerate the Windows screens (same order as the runtime),
/// let the user identify each one visually, show a test pattern, and produce the
/// readable detection report (screens + DMD stack + APIExpose + runtime state).
/// </summary>
public sealed class ScreensView : UserControl
{
    private readonly string _pluginRoot;
    private readonly StackPanel _cards = new();
    private readonly TextBlock _report = Ui.MutedLabel("", 12);
    private IReadOnlyList<ScreenInfo> _screens = Array.Empty<ScreenInfo>();

    public ScreensView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Écrans détectés", "Detected screens")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Les numéros ci-dessous sont les index Windows utilisés par config.ini (MarqueeScreen, TopperScreen…). "
            + "Utilisez « Identifier » pour afficher le numéro sur chaque écran physique.",
            "The numbers below are the Windows indices used by config.ini (MarqueeScreen, TopperScreen…). "
            + "Use \"Identify\" to display the number on each physical screen.")));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(Ui.Button(L.T("Identifier les écrans", "Identify screens"), (_, _) => IdentifyWindow.ShowAll(_screens), primary: true));
        actions.Children.Add(Ui.Button(L.T("Rafraîchir la détection", "Refresh detection"), (_, _) => Refresh()));
        page.Children.Add(actions);

        page.Children.Add(_cards);

        page.Children.Add(Ui.SectionHeader(L.T("Rapport de détection", "Detection report")));
        _report.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(Ui.Card(_report));

        Content = Ui.Page(page);
        Refresh();
    }

    private void Refresh()
    {
        _screens = ScreenProbe.Detect();
        _cards.Children.Clear();

        foreach (var screen in _screens)
        {
            _cards.Children.Add(BuildScreenCard(screen));
        }

        _ = RefreshReportAsync();
    }

    private Border BuildScreenCard(ScreenInfo screen)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var info = new StackPanel();
        var title = Ui.Label(L.T("ÉCRAN", "SCREEN") + $" {screen.Index}" + (screen.Primary ? L.T("  ·  principal", "  ·  primary") : ""), 15);
        title.FontWeight = FontWeights.Bold;
        info.Children.Add(title);
        info.Children.Add(Ui.MutedLabel(
            $"{screen.DeviceName}  ·  {screen.Bounds.Width}x{screen.Bounds.Height}"
            + $"  ·  ratio {screen.Ratio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
            + $"  ·  {screen.Orientation}"
            + $"  ·  position {screen.Bounds.X},{screen.Bounds.Y}"
            + screen.Touch switch
            {
                TouchSupport.Touch => L.T("  ·  tactile", "  ·  touch"),
                TouchSupport.None => "",
                _ => L.T("  ·  tactile inconnu", "  ·  touch unknown")
            }));
        var suggestion = Ui.MutedLabel(screen.Suggestion);
        suggestion.Foreground = Ui.Accent;
        suggestion.Margin = new Thickness(0, 4, 0, 0);
        info.Children.Add(suggestion);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.Button(L.T("Afficher la mire", "Show test pattern"), (_, _) =>
            new TestPatternWindow(L.T("ÉCRAN", "SCREEN") + $" {screen.Index}", screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height).Show()));
        buttons.Children.Add(Ui.Button(L.T("Composer cet écran", "Compose this screen"), (_, _) => ComposeScreen(screen)));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return Ui.Card(grid);
    }

    /// <summary>Opens the visual compositor on every surface hosted by this
    /// screen — the place where surface x,y positions are edited.</summary>
    private void ComposeScreen(ScreenInfo screen)
    {
        var store = new Data.SurfacesStore(_pluginRoot);
        var surfaces = store.Load();
        var hosted = surfaces.Where(s => s.Screens.Contains(screen.Index)).ToList();
        if (hosted.Count == 0)
        {
            MessageBox.Show(
                L.T($"Aucune surface n'est affectée à l'écran {screen.Index}. Créez-en une dans la vue Surfaces.",
                    $"No surface is assigned to screen {screen.Index}. Create one in the Surfaces view."),
                "MarqueeManagerSetup", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var editor = new ScreenCompositor(screen.Index, screen, hosted, hosted[0])
        {
            Owner = Window.GetWindow(this)
        };
        if (editor.ShowDialog() == true)
        {
            store.Save(surfaces);
        }
    }

    private async Task RefreshReportAsync()
    {
        var lines = _screens.Select(screen => screen.Describe()).ToList();

        var dmd = DmdProbe.Inspect(_pluginRoot);
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        lines.Add("");
        lines.Add(dmd.DmdDeviceDllFound
            ? L.T("DMD : pile DmdDevice présente", "DMD: DmdDevice stack present") + $" ({ini.Get("DMD", "Model", "zedmd")}, {ini.Get("DMD", "Width", "128")}x{ini.Get("DMD", "Height", "32")})."
            : L.T("DMD : pile DmdDevice introuvable dans tools\\dmd.", "DMD: DmdDevice stack not found in tools\\dmd."));
        lines.Add(dmd.SerialPorts.Count > 0
            ? L.T("Ports série : ", "Serial ports: ") + string.Join(", ", dmd.SerialPorts) + "."
            : L.T("Ports série : aucun détecté.", "Serial ports: none detected."));
        lines.Add(MarqueeManagerProcess.IsRunning()
            ? L.T("MarqueeManager : en cours d'exécution.", "MarqueeManager: running.")
            : L.T("MarqueeManager : arrêté.", "MarqueeManager: stopped."));
        _report.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine
            + L.T("APIExpose : test en cours…", "APIExpose: testing…");

        var alive = await ApiExposeProbe.IsAliveAsync(ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));
        lines.Add(alive
            ? L.T("APIExpose : connecté.", "APIExpose: connected.")
            : L.T("APIExpose : injoignable (RetroBat/APIExpose arrêté ?).", "APIExpose: unreachable (RetroBat/APIExpose not running?)."));
        _report.Text = string.Join(Environment.NewLine, lines);
    }
}
