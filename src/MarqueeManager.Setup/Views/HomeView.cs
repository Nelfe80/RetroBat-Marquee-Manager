using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Landing view: health at a glance (runtime, APIExpose, config), the configured
/// surfaces summary, and shortcuts to the other views. Same spirit as the
/// LedManagerSetup home page.
/// </summary>
public sealed class HomeView : UserControl
{
    private readonly string _pluginRoot;
    private readonly Action<string>? _navigate;

    private readonly TextBlock _runtimeStatus = Ui.Label("…");
    private readonly TextBlock _apiStatus = Ui.Label("…");
    private readonly Button _runtimeButton;

    public HomeView(string pluginRoot, Action<string>? navigate = null)
    {
        _pluginRoot = pluginRoot;
        _navigate = navigate;

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("Accueil", "Home")));
        page.Children.Add(Ui.Subtitle(L.T(
            "État de votre installation Marquee Manager en un coup d'œil.",
            "Your Marquee Manager installation health at a glance.")));

        // ---- health card ----
        var health = new StackPanel();
        health.Children.Add(Ui.SectionHeader(L.T("État", "Health")));

        _runtimeButton = Ui.Button("…", OnToggleRuntime);
        var runtimeRow = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        DockPanel.SetDock(_runtimeButton, Dock.Right);
        runtimeRow.Children.Add(_runtimeButton);
        runtimeRow.Children.Add(_runtimeStatus);
        health.Children.Add(runtimeRow);
        health.Children.Add(_apiStatus);

        var configPath = PluginPaths.ConfigPath(pluginRoot);
        var configStatus = Ui.Label(File.Exists(configPath)
            ? L.T("✓ config.ini trouvé", "✓ config.ini found")
            : L.T("✗ config.ini introuvable !", "✗ config.ini not found!"));
        configStatus.Foreground = File.Exists(configPath) ? Ui.Ok : Ui.Error;
        health.Children.Add(configStatus);
        page.Children.Add(Ui.Card(health));

        // ---- configuration summary ----
        var summary = new StackPanel();
        summary.Children.Add(Ui.SectionHeader(L.T("Ma configuration", "My configuration")));
        foreach (var line in DescribeConfiguration())
        {
            summary.Children.Add(Ui.Label(line));
        }
        page.Children.Add(Ui.Card(summary));

        // ---- shortcuts ----
        if (navigate != null)
        {
            var shortcuts = new StackPanel();
            shortcuts.Children.Add(Ui.SectionHeader(L.T("Raccourcis", "Shortcuts")));
            var buttons = new WrapPanel();
            buttons.Children.Add(Ui.Button(L.T("Configurer mon setup", "Configure my setup"), (_, _) => _navigate?.Invoke("setup")));
            buttons.Children.Add(Ui.Button(L.T("Composer mes marquees", "Compose my marquees"), (_, _) => _navigate?.Invoke("games")));
            buttons.Children.Add(Ui.Button(L.T("Options du runtime", "Runtime options"), (_, _) => _navigate?.Invoke("options")));
            buttons.Children.Add(Ui.Button(L.T("Diagnostic", "Diagnostics"), (_, _) => _navigate?.Invoke("diagnostic")));
            buttons.Children.Add(Ui.Button(L.T("Relancer l'assistant de démarrage", "Rerun the startup wizard"), (_, _) =>
            {
                if (new Controls.OnboardingWizard(pluginRoot) { Owner = System.Windows.Window.GetWindow(this) }.ShowDialog() == true)
                {
                    _navigate?.Invoke("setup");
                }
            }));
            shortcuts.Children.Add(buttons);
            page.Children.Add(Ui.Card(shortcuts));
        }

        // ---- links ----
        var links = new StackPanel();
        links.Children.Add(Ui.SectionHeader(L.T("Documentation", "Documentation")));
        var wiki = Ui.Button(L.T("Ouvrir le wiki en ligne", "Open the online wiki"), (_, _) => OpenUrl(
            L.French
                ? "https://nelfe80.github.io/RetroBat-Marquee-Manager/fr/"
                : "https://nelfe80.github.io/RetroBat-Marquee-Manager/"));
        var linksRow = new WrapPanel();
        linksRow.Children.Add(wiki);
        links.Children.Add(linksRow);
        page.Children.Add(Ui.Card(links));

        Content = Ui.Page(page);
        RefreshHealth();
    }

    private void RefreshHealth()
    {
        var running = MarqueeManagerProcess.IsRunning();
        _runtimeStatus.Text = running
            ? L.T("● MarqueeManager est en cours d'exécution", "● MarqueeManager is running")
            : L.T("○ MarqueeManager est arrêté", "○ MarqueeManager is stopped");
        _runtimeStatus.Foreground = running ? Ui.Ok : Ui.Muted;
        _runtimeButton.Content = running
            ? L.T("Arrêter", "Stop")
            : L.T("Démarrer", "Start");

        _apiStatus.Text = L.T("… APIExpose : test en cours", "… APIExpose: probing");
        _apiStatus.Foreground = Ui.Muted;
        _ = ProbeApiAsync();
    }

    private async System.Threading.Tasks.Task ProbeApiAsync()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var url = ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345");
        var alive = await ApiExposeProbe.IsAliveAsync(url);
        if (!Dispatcher.HasShutdownStarted)
        {
            _apiStatus.Text = alive
                ? L.T($"● APIExpose répond ({url})", $"● APIExpose is up ({url})")
                : L.T($"○ APIExpose ne répond pas ({url})", $"○ APIExpose is not responding ({url})");
            _apiStatus.Foreground = alive ? Ui.Ok : Ui.Error;
        }
    }

    private void OnToggleRuntime(object sender, RoutedEventArgs e)
    {
        if (MarqueeManagerProcess.IsRunning())
        {
            MarqueeManagerProcess.Stop();
        }
        else
        {
            MarqueeManagerProcess.Start(_pluginRoot);
        }
        // the process needs a moment to appear/disappear from the process list
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(800);
            RefreshHealth();
        });
    }

    private IEnumerable<string> DescribeConfiguration()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var screens = ScreenProbe.Detect();
        var any = false;
        foreach (var (key, label) in new[]
                 {
                     ("Marquee", L.T("Marquee", "Marquee")),
                     ("Topper", L.T("Topper", "Topper")),
                     ("IcCard", L.T("IC card", "IC card")),
                     ("Dmd", L.T("DMD virtuel", "Virtual DMD")),
                     ("Lcd", L.T("LCD", "LCD"))
                 })
        {
            var raw = ini.Get("Screens", key + "Screen", "-1");
            if (raw.Trim() == "-1" || raw.Trim().Length == 0)
            {
                continue;
            }

            any = true;
            var bounds = ini.Get("Screens", key + "Bounds", "");
            var detail = bounds.Length > 0
                ? L.T($"zone {bounds}", $"area {bounds}")
                : L.T("plein écran", "fullscreen");
            var screenLabel = L.T($"écran {raw}", $"screen {raw}");
            if (int.TryParse(raw.Trim(), out var index) && index >= 0 && index < screens.Count)
            {
                var s = screens[index];
                screenLabel += $" ({s.Bounds.Width}×{s.Bounds.Height})";
            }
            yield return $"• {label} → {screenLabel}, {detail}";
        }

        if (!any)
        {
            yield return L.T("Aucune surface configurée pour l'instant.", "No surface configured yet.");
        }

        if (ini.GetBool("DMD", "Enabled", false))
        {
            yield return L.T($"• DMD physique : {ini.Get("DMD", "Model", "?")}",
                $"• Physical DMD: {ini.Get("DMD", "Model", "?")}");
        }
        if (ini.GetBool("Lighting", "Enabled", false))
        {
            yield return L.T("• Effets lumière (Lighting Engine) : activés",
                "• Light effects (Lighting Engine): enabled");
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // no browser: non-fatal
        }
    }
}
