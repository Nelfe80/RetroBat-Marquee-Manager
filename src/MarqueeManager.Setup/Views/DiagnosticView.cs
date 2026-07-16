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
/// Why is my screen black? — human-readable health: detection report (screens,
/// DMD stack, serial ports with LED-panel labels), source status (APIExpose,
/// online keys) and the tail of the runtime log.
/// </summary>
public sealed class DiagnosticView : UserControl
{
    private readonly string _pluginRoot;
    private readonly TextBlock _report = Ui.MutedLabel("", 12);
    private readonly TextBlock _sources = Ui.MutedLabel("", 12);
    private readonly TextBlock _logs = Ui.MutedLabel("", 11);

    public DiagnosticView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;

        var page = new StackPanel();
        page.Children.Add(Ui.Title("Diagnostic"));
        page.Children.Add(Ui.Subtitle(L.T(
            "L'état complet de l'installation : détection matérielle, sources de données et derniers événements du runtime.",
            "The full installation health: hardware detection, data sources and the runtime's latest events.")));

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        actions.Children.Add(Ui.Button(L.T("Rafraîchir", "Refresh"), (_, _) => _ = RefreshAsync(), primary: true));
        actions.Children.Add(Ui.Button(L.T("Identifier les écrans", "Identify screens"), (_, _) =>
            IdentifyWindow.ShowAll(ScreenProbe.Detect())));
        page.Children.Add(actions);

        page.Children.Add(Ui.SectionHeader(L.T("Détection matérielle", "Hardware detection")));
        _report.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(Ui.Card(_report));

        page.Children.Add(Ui.SectionHeader(L.T("Sources de données", "Data sources")));
        _sources.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(Ui.Card(_sources));

        page.Children.Add(Ui.SectionHeader(L.T("Derniers événements du runtime", "Latest runtime events")));
        _logs.TextWrapping = TextWrapping.Wrap;
        _logs.FontFamily = new System.Windows.Media.FontFamily("Consolas");
        page.Children.Add(Ui.Card(_logs));

        Content = Ui.Page(page);
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var lines = ScreenProbe.Detect().Select(screen => screen.Describe()).ToList();
        lines.Add("");
        lines.Add(DmdProbe.Inspect(_pluginRoot).Describe());
        lines.Add("");
        lines.Add(MarqueeManagerProcess.IsRunning()
            ? L.T("MarqueeManager : en cours d'exécution.", "MarqueeManager: running.")
            : L.T("MarqueeManager : arrêté.", "MarqueeManager: stopped."));
        var surfacesPath = Path.Combine(_pluginRoot, "state", "surfaces.json");
        lines.Add(File.Exists(surfacesPath)
            ? L.T("surfaces.json : présent (modèle dynamique actif).", "surfaces.json: present (dynamic model active).")
            : L.T("surfaces.json : absent (mode [Screens] hérité).", "surfaces.json: absent (legacy [Screens] mode)."));
        _report.Text = string.Join(Environment.NewLine, lines);

        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        var sourceLines = new List<string> { L.T("APIExpose : test en cours…", "APIExpose: testing…") };
        _sources.Text = string.Join(Environment.NewLine, sourceLines);

        var apiUrl = ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345");
        var alive = await ApiExposeProbe.IsAliveAsync(apiUrl);
        sourceLines.Clear();
        sourceLines.Add(alive
            ? L.T($"APIExpose : connecté ({apiUrl}).", $"APIExpose: connected ({apiUrl}).")
            : L.T($"APIExpose : injoignable ({apiUrl}) — les surfaces resteront vides.",
                $"APIExpose: unreachable ({apiUrl}) — surfaces will stay empty."));
        foreach (var (key, label) in new[]
                 {
                     ("SteamGridDbApiKey", "SteamGridDB"), ("TheGamesDbApiKey", "TheGamesDB"),
                     ("TwitchClientId", "Twitch"), ("YouTubeApiKey", "YouTube")
                 })
        {
            sourceLines.Add(ini.Get("Scraper", key, "").Length > 0
                ? L.T($"{label} : clé renseignée.", $"{label}: key set.")
                : L.T($"{label} : pas de clé (source inactive).", $"{label}: no key (source inactive)."));
        }
        _sources.Text = string.Join(Environment.NewLine, sourceLines);

        try
        {
            var logPath = Path.Combine(_pluginRoot, ini.Get("Settings", "LogFilePath", @".log\debug.log"));
            _logs.Text = File.Exists(logPath)
                ? string.Join(Environment.NewLine, ReadLastLines(logPath, 25))
                : L.T("Aucun fichier de log (LogToFile désactivé ?).", "No log file (LogToFile disabled?).");
        }
        catch (Exception ex)
        {
            _logs.Text = L.T($"Logs illisibles : {ex.Message}", $"Unreadable logs: {ex.Message}");
        }
    }

    /// <summary>Tail without loading the whole file (the runtime log grows).</summary>
    private static IEnumerable<string> ReadLastLines(string path, int count)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = stream.Length;
        var chunk = Math.Min(length, 64 * 1024);
        stream.Seek(-chunk, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return lines.Skip(Math.Max(0, lines.Count - count));
    }
}
