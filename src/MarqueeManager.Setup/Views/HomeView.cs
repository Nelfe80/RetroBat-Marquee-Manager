using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Data;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using MarqueeManager.Setup.Processes;
using Path = System.IO.Path;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Landing view, LedManagerSetup layout: one status card per link of the chain
/// (runtime, APIExpose, screens &amp; surfaces, physical DMD, user content), each a
/// dot + name + description + actions row, refreshed without blocking. Shortcuts
/// and documentation below.
/// </summary>
public sealed class HomeView : UserControl
{
    private sealed record StatusCard(Ellipse Dot, TextBlock Text, StackPanel Actions);

    private readonly string _pluginRoot;
    private readonly Action<string>? _navigate;

    private readonly StatusCard _runtime;
    private readonly StatusCard _api;
    private readonly StatusCard _screens;
    private readonly StatusCard _dmd;
    private readonly StatusCard _content;

    public HomeView(string pluginRoot, Action<string>? navigate = null)
    {
        _pluginRoot = pluginRoot;
        _navigate = navigate;

        var page = new StackPanel();
        page.Children.Add(Ui.Title(L.T("État de l'installation", "Installation status")));
        page.Children.Add(Ui.Subtitle(L.T(
            "Chaque maillon de la chaîne, du flux APIExpose jusqu'à vos écrans.",
            "Every link of the chain, from the APIExpose stream to your screens.")));

        _runtime = AddCard(page, "MarqueeManager");
        _api = AddCard(page, "APIExpose");
        _screens = AddCard(page, L.T("Écrans & surfaces", "Screens & surfaces"));
        _dmd = AddCard(page, L.T("DMD physique", "Physical DMD"));
        _content = AddCard(page, L.T("Mes contenus", "My content"));

        // ---- shortcuts ----
        if (navigate != null)
        {
            var shortcuts = new StackPanel();
            shortcuts.Children.Add(Ui.SectionHeader(L.T("Raccourcis", "Shortcuts")));
            var buttons = new WrapPanel();
            buttons.Children.Add(Ui.Button(L.T("Configurer mon setup", "Configure my setup"), (_, _) => _navigate?.Invoke("setup")));
            buttons.Children.Add(Ui.Button(L.T("Créer mes marquees", "Create my marquees"), (_, _) => _navigate?.Invoke("games")));
            buttons.Children.Add(Ui.Button(L.T("Options du runtime", "Runtime options"), (_, _) => _navigate?.Invoke("options")));
            buttons.Children.Add(Ui.Button(L.T("Diagnostic", "Diagnostics"), (_, _) => _navigate?.Invoke("diagnostic")));
            buttons.Children.Add(Ui.Button(L.T("Relancer l'assistant de démarrage", "Rerun the startup wizard"), (_, _) =>
            {
                if (new OnboardingWizard(pluginRoot) { Owner = Window.GetWindow(this) }.ShowDialog() == true)
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
        // mkdocs i18n: FR is the default language and lives at the site ROOT,
        // EN under /en/ (there is no /fr/ path — it 404s)
        var wiki = Ui.Button(L.T("Ouvrir le wiki en ligne", "Open the online wiki"), (_, _) => OpenUrl(
            L.French
                ? "https://nelfe80.github.io/RetroBat-Marquee-Manager/"
                : "https://nelfe80.github.io/RetroBat-Marquee-Manager/en/"));
        var linksRow = new WrapPanel();
        linksRow.Children.Add(wiki);
        links.Children.Add(linksRow);
        page.Children.Add(Ui.Card(links));

        Content = Ui.Page(page);
        Refresh();
    }

    // ================= card factory (LedManager pattern) =================

    private static StatusCard AddCard(StackPanel parent, string title)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse { Width = 12, Height = 12, Fill = Ui.Muted, VerticalAlignment = VerticalAlignment.Center };
        grid.Children.Add(dot);

        var name = Ui.Label(title, 13);
        name.FontWeight = FontWeights.Bold;
        name.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);

        var text = Ui.MutedLabel("…", 12);
        text.TextWrapping = TextWrapping.Wrap;
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        parent.Children.Add(Ui.Card(grid, padding: 12));
        return new StatusCard(dot, text, actions);
    }

    private static void SetState(StatusCard card, bool? ok, string text)
    {
        card.Dot.Fill = ok switch { true => Ui.Ok, false => Ui.Error, _ => Ui.Muted };
        card.Text.Text = text;
    }

    /// <summary>Orange dot: configured but currently degraded (e.g. DMD unplugged).</summary>
    private static void SetWarning(StatusCard card, string text)
    {
        card.Dot.Fill = Ui.Accent;
        card.Text.Text = text;
    }

    // ================= refresh =================

    private void Refresh()
    {
        RefreshRuntime();
        RefreshScreens();
        RefreshDmd();
        RefreshContent();
        SetState(_api, null, L.T("test en cours…", "probing…"));
        _ = ProbeApiAsync();
    }

    private void RefreshRuntime()
    {
        var running = MarqueeManagerProcess.IsRunning();
        SetState(_runtime, running, running
            ? L.T("En cours d'exécution — vos écrans suivent RetroBat.", "Running — your screens follow RetroBat.")
            : L.T("Arrêté. Il démarre normalement avec RetroBat.", "Stopped. It normally starts with RetroBat."));
        _runtime.Actions.Children.Clear();
        _runtime.Actions.Children.Add(Ui.Button(
            running ? L.T("Arrêter", "Stop") : L.T("Démarrer", "Start"), (_, _) =>
            {
                if (MarqueeManagerProcess.IsRunning()) MarqueeManagerProcess.Stop();
                else MarqueeManagerProcess.Start(_pluginRoot);
                _ = Dispatcher.InvokeAsync(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(800);
                    RefreshRuntime();
                });
            }));
    }

    private async System.Threading.Tasks.Task ProbeApiAsync()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var url = ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345");
        var alive = await ApiExposeProbe.IsAliveAsync(url);
        if (Dispatcher.HasShutdownStarted) return;
        SetState(_api, alive, alive
            ? L.T($"Répond ({url}) — médias et données en direct.", $"Up ({url}) — live media and data.")
            : L.T($"Ne répond pas ({url}) — les surfaces resteront vides.", $"Not responding ({url}) — surfaces will stay empty."));
    }

    private void RefreshScreens()
    {
        try
        {
            var detected = ScreenProbe.Detect();
            var store = new SurfacesStore(_pluginRoot);
            var surfaces = store.Load();
            var ok = surfaces.Count > 0;
            SetState(_screens, ok, ok
                ? L.T($"{detected.Count} écran(s) détecté(s) · {surfaces.Count} surface(s) configurée(s) ({string.Join(", ", surfaces.Select(s => s.Id))}).",
                    $"{detected.Count} screen(s) detected · {surfaces.Count} configured surface(s) ({string.Join(", ", surfaces.Select(s => s.Id))}).")
                : L.T($"{detected.Count} écran(s) détecté(s) — aucune surface configurée pour l'instant.",
                    $"{detected.Count} screen(s) detected — no surface configured yet."));
        }
        catch
        {
            SetState(_screens, false, L.T("Configuration illisible.", "Unreadable configuration."));
        }
        _screens.Actions.Children.Clear();
        if (_navigate != null)
        {
            _screens.Actions.Children.Add(Ui.Button(L.T("Mon setup", "My setup"), (_, _) => _navigate?.Invoke("setup")));
        }
    }

    private void RefreshDmd()
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var enabled = ini.GetBool("DMD", "Enabled", false);
        var model = ini.Get("DMD", "Model", "");

        if (!enabled)
        {
            SetState(_dmd, null, L.T("Aucun panneau configuré (optionnel).", "No panel configured (optional)."));
        }
        else
        {
            // REAL health: the ZeDMD handshake through libzedmd — the lib sends
            // the "ZeDMD" magic frame, only a real panel answers with its
            // identity (dims + firmware). A free COM port or the LedManager
            // Pico never passes. Runs async (the port scan takes a moment).
            SetState(_dmd, null, L.T("test du panneau (handshake ZeDMD)…", "probing the panel (ZeDMD handshake)…"));
            var forcedPort = ini.Get("DMD", "ZeDmdPort", "").Trim();
            var runtimeRunning = MarqueeManagerProcess.IsRunning();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                var result = ZeDmdProbe.Probe(_pluginRoot, forcedPort.Length > 0 ? forcedPort : null);
                Dispatcher.BeginInvoke(() =>
                {
                    if (result.Found)
                    {
                        SetState(_dmd, true, L.T(
                            $"{model} détecté sur {result.Port} — {result.Width}×{result.Height} px, firmware {result.Firmware}.",
                            $"{model} detected on {result.Port} — {result.Width}×{result.Height} px, firmware {result.Firmware}."));
                    }
                    else if (runtimeRunning)
                    {
                        // the runtime may hold the port: unreachable ≠ unplugged
                        SetWarning(_dmd, L.T(
                            $"{model} : panneau non joignable (handshake sans réponse) — port tenu par le runtime en cours, ou panneau débranché. Arrêtez MarqueeManager pour un test fiable.",
                            $"{model}: panel unreachable (handshake unanswered) — port held by the running runtime, or panel unplugged. Stop MarqueeManager for a reliable test."));
                    }
                    else
                    {
                        SetWarning(_dmd, L.T(
                            $"{model} : aucun panneau ne répond au handshake ZeDMD — débranché ?" + (result.Error != null ? $" ({result.Error})" : ""),
                            $"{model}: no panel answers the ZeDMD handshake — unplugged?" + (result.Error != null ? $" ({result.Error})" : "")));
                    }
                });
            });
        }
        _dmd.Actions.Children.Clear();
        if (_navigate != null)
        {
            _dmd.Actions.Children.Add(Ui.Button(L.T("Mon setup", "My setup"), (_, _) => _navigate?.Invoke("setup")));
        }
    }

    private void RefreshContent()
    {
        try
        {
            var compositions = 0;
            foreach (var category in new[] { "marquees", "toppers", "dmd" })
            {
                var root = Path.Combine(_pluginRoot, "media", category);
                if (Directory.Exists(root))
                {
                    compositions += Directory.EnumerateFiles(root, "*.project.json", SearchOption.AllDirectories).Count();
                }
            }
            var library = new EffectsLibraryStore(_pluginRoot);
            var effects = library.Load();
            var custom = effects.Keys.Count(name => !library.IsOfficial(name));
            SetState(_content, compositions + custom > 0 ? true : null, L.T(
                $"{compositions} composition(s) manuelle(s) · {custom} effet(s) personnel(s) · {effects.Count} effet(s) en bibliothèque.",
                $"{compositions} manual composition(s) · {custom} personal effect(s) · {effects.Count} library effect(s)."));
        }
        catch
        {
            SetState(_content, null, L.T("Contenu non lisible.", "Unreadable content."));
        }
        _content.Actions.Children.Clear();
        if (_navigate != null)
        {
            _content.Actions.Children.Add(Ui.Button(L.T("Mes jeux", "My games"), (_, _) => _navigate?.Invoke("games")));
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
