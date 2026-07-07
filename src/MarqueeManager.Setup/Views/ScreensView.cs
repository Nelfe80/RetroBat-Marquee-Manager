using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
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
        page.Children.Add(Ui.Title("Écrans détectés"));
        page.Children.Add(Ui.Subtitle(
            "Les numéros ci-dessous sont les index Windows utilisés par config.ini (MarqueeScreen, TopperScreen…). "
            + "Utilisez « Identifier » pour afficher le numéro sur chaque écran physique."));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        actions.Children.Add(Ui.Button("Identifier les écrans", (_, _) => IdentifyWindow.ShowAll(_screens), primary: true));
        actions.Children.Add(Ui.Button("Rafraîchir la détection", (_, _) => Refresh()));
        page.Children.Add(actions);

        page.Children.Add(_cards);

        page.Children.Add(Ui.SectionHeader("Rapport de détection"));
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
        var title = Ui.Label($"ÉCRAN {screen.Index}" + (screen.Primary ? "  ·  principal" : ""), 15);
        title.FontWeight = FontWeights.Bold;
        info.Children.Add(title);
        info.Children.Add(Ui.MutedLabel(
            $"{screen.DeviceName}  ·  {screen.Bounds.Width}x{screen.Bounds.Height}"
            + $"  ·  ratio {screen.Ratio.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}"
            + $"  ·  {screen.Orientation}"
            + $"  ·  position {screen.Bounds.X},{screen.Bounds.Y}"
            + screen.Touch switch
            {
                TouchSupport.Touch => "  ·  tactile",
                TouchSupport.None => "",
                _ => "  ·  tactile inconnu"
            }));
        var suggestion = Ui.MutedLabel(screen.Suggestion);
        suggestion.Foreground = Ui.Accent;
        suggestion.Margin = new Thickness(0, 4, 0, 0);
        info.Children.Add(suggestion);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.Button("Afficher la mire", (_, _) =>
            new TestPatternWindow($"ÉCRAN {screen.Index}", screen.Bounds.X, screen.Bounds.Y,
                screen.Bounds.Width, screen.Bounds.Height).Show()));
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

        return Ui.Card(grid);
    }

    private async Task RefreshReportAsync()
    {
        var lines = _screens.Select(screen => screen.Describe()).ToList();

        var dmd = DmdProbe.Inspect(_pluginRoot);
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        lines.Add("");
        lines.Add(dmd.DmdDeviceDllFound
            ? $"DMD : pile DmdDevice présente ({ini.Get("DMD", "Model", "zedmd")}, {ini.Get("DMD", "Width", "128")}x{ini.Get("DMD", "Height", "32")})."
            : "DMD : pile DmdDevice introuvable dans tools\\dmd.");
        lines.Add(dmd.SerialPorts.Count > 0
            ? "Ports série : " + string.Join(", ", dmd.SerialPorts) + "."
            : "Ports série : aucun détecté.");
        lines.Add(MarqueeManagerProcess.IsRunning()
            ? "MarqueeManager : en cours d'exécution."
            : "MarqueeManager : arrêté.");
        _report.Text = string.Join(Environment.NewLine, lines) + Environment.NewLine + "APIExpose : test en cours…";

        var alive = await ApiExposeProbe.IsAliveAsync(ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));
        lines.Add(alive ? "APIExpose : connecté." : "APIExpose : injoignable (RetroBat/APIExpose arrêté ?).");
        _report.Text = string.Join(Environment.NewLine, lines);
    }
}
