using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;

namespace MarqueeManager.Setup.Views;

/// <summary>
/// Everything that isn't screens or DMD: connection ([Settings]), visual rendering
/// ([Lighting]), MAME layouts ([DOF]), RetroAchievements and live score/timer
/// overlays. Presented as user-facing switches, saved back comment-preserving.
/// </summary>
public sealed class OptionsView : UserControl
{
    private readonly string _pluginRoot;
    private readonly TextBlock _status = Ui.MutedLabel("", 12);
    private readonly TextBox _apiUrl;
    private readonly TextBlock _apiResult = Ui.MutedLabel("");
    private readonly CheckBox _tray;
    private readonly CheckBox _logToFile;
    private readonly CheckBox _lightingEnabled;
    private readonly Slider _renderScale;
    private readonly TextBlock _renderScaleLabel = Ui.MutedLabel("");
    private readonly Slider _fillHeight;
    private readonly TextBlock _fillHeightLabel = Ui.MutedLabel("");
    private readonly Slider _glass;
    private readonly TextBlock _glassLabel = Ui.MutedLabel("");
    private readonly CheckBox _preferGenerated;
    private readonly CheckBox _dmdMirror;
    private readonly CheckBox _sound;
    private readonly CheckBox _dofEnabled;
    private readonly CheckBox _dofMarquee;
    private readonly CheckBox _dofDmd;
    private readonly CheckBox _raEnabled;
    private readonly CheckBox _raMarquee;
    private readonly CheckBox _raDmd;
    private readonly CheckBox _raBadgeTray;
    private readonly CheckBox _raTakeover;
    private readonly CheckBox _liveScore;
    private readonly CheckBox _liveTimer;
    private readonly CheckBox _liveMarquee;
    private readonly CheckBox _liveDmd;

    public OptionsView(string pluginRoot)
    {
        _pluginRoot = pluginRoot;
        var ini = IniFile.Load(PluginPaths.ConfigPath(pluginRoot));

        var page = new StackPanel();
        page.Children.Add(Ui.Title("Options"));
        page.Children.Add(Ui.Subtitle(L.T(
            "Connexion APIExpose, rendu lumineux du marquee, layouts MAME, RetroAchievements et données live. "
            + "Les réglages fins restent éditables dans config.ini — les commentaires y sont préservés.",
            "APIExpose connection, marquee lighting, MAME layouts, RetroAchievements and live data. "
            + "Fine-grained settings remain editable in config.ini — its comments are preserved.")));

        // --- connexion ---
        page.Children.Add(Ui.SectionHeader(L.T("Connexion", "Connection")));
        var connexion = new StackPanel();
        _apiUrl = Ui.TextBox(ini.Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"), 260);
        var apiLine = new StackPanel { Orientation = Orientation.Horizontal };
        apiLine.Children.Add(_apiUrl);
        apiLine.Children.Add(Ui.Button(L.T("Tester la connexion", "Test the connection"), async (_, _) =>
        {
            _apiResult.Text = L.T("test en cours…", "testing…");
            var alive = await ApiExposeProbe.IsAliveAsync(_apiUrl.Text.Trim());
            _apiResult.Text = alive
                ? L.T("connecté ✓", "connected ✓")
                : L.T("injoignable (RetroBat/APIExpose lancé ?)", "unreachable (RetroBat/APIExpose running?)");
            _apiResult.Foreground = alive ? Ui.Ok : Ui.Error;
        }));
        apiLine.Children.Add(_apiResult);
        connexion.Children.Add(Ui.Row(L.T("Adresse APIExpose", "APIExpose address"), apiLine));
        _tray = Ui.CheckBox(L.T("Réduire dans la zone de notification", "Minimize to the notification area"), ini.GetBool("Settings", "MinimizeToTray", true));
        connexion.Children.Add(_tray);
        _logToFile = Ui.CheckBox(L.T("Écrire les logs dans .log\\debug.log", "Write logs to .log\\debug.log"), ini.GetBool("Settings", "LogToFile", true));
        connexion.Children.Add(_logToFile);
        page.Children.Add(Ui.Card(connexion));

        // --- lighting ---
        page.Children.Add(Ui.SectionHeader(L.T("Rendu lumineux du marquee (Lighting Engine)", "Marquee lighting (Lighting Engine)")));
        var lighting = new StackPanel();
        _lightingEnabled = Ui.CheckBox(L.T("Activer le rendu lumineux (allumage fluorescent, rétroéclairage vivant)",
                "Enable the lighting render (fluorescent ignition, living backlight)"),
            ini.GetBool("Lighting", "Enabled", false));
        lighting.Children.Add(_lightingEnabled);

        (_renderScale, var renderLine) = PercentSlider(ini.GetDouble("Lighting", "RenderScale", 0.75), 0.25, 1.0,
            _renderScaleLabel, v => $"{(int)(v * 100)} % — " + L.T("qualité/performance", "quality/performance"));
        lighting.Children.Add(Ui.Row(L.T("Résolution interne", "Internal resolution"), renderLine,
            L.T("baisser si le CPU ne tient pas 60 FPS", "lower it if the CPU can't hold 60 FPS")));

        (_fillHeight, var fillLine) = PercentSlider(ini.GetDouble("Lighting", "FillHeightMaxCrop", 0.30), 0.0, 0.6,
            _fillHeightLabel, v => v <= 0
                ? L.T("letterbox systématique", "always letterbox")
                : L.T("rognage max ", "max crop ") + $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Cadrage (remplir la hauteur)", "Framing (fill the height)"), fillLine));

        (_glass, var glassLine) = PercentSlider(ini.GetDouble("Lighting", "GlassReflection", 0.06), 0.0, 0.3,
            _glassLabel, v => v <= 0
                ? L.T("pas de vitre", "no glass")
                : $"{(v * 100).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)} %");
        lighting.Children.Add(Ui.Row(L.T("Reflet de la vitre", "Glass reflection"), glassLine));

        _preferGenerated = Ui.CheckBox(L.T("Préférer le marquee généré au scan réel", "Prefer the generated marquee over the real scan"),
            ini.GetBool("Lighting", "PreferGeneratedMarquee", false));
        lighting.Children.Add(_preferGenerated);
        _dmdMirror = Ui.CheckBox(L.T("Miroir de l'animation lumineuse sur le DMD physique (sinon média dédié, recommandé décoché)",
                "Mirror the lighting animation on the physical DMD (otherwise dedicated media, recommended unchecked)"),
            ini.GetBool("Lighting", "DmdMirror", false));
        lighting.Children.Add(_dmdMirror);
        _sound = Ui.CheckBox(L.T("Sons des tubes fluorescents", "Fluorescent tube sounds"), ini.GetBool("Lighting", "SoundEnabled", true));
        lighting.Children.Add(_sound);
        page.Children.Add(Ui.Card(lighting));

        // --- DOF / layouts MAME ---
        page.Children.Add(Ui.SectionHeader(L.T("Layouts MAME (.lay)", "MAME layouts (.lay)")));
        var dof = new StackPanel();
        _dofEnabled = Ui.CheckBox(L.T("Lire les fichiers .lay MAME (marquee, topper, iccard, DMD)",
                "Read MAME .lay files (marquee, topper, iccard, DMD)"),
            ini.GetBool("DOF", "Enabled", true));
        dof.Children.Add(_dofEnabled);
        _dofMarquee = Ui.CheckBox(L.T("Autoriser les vues .lay sur les surfaces WPF", "Allow .lay views on the WPF surfaces"), ini.GetBool("DOF", "MarqueeEnabled", true));
        dof.Children.Add(_dofMarquee);
        _dofDmd = Ui.CheckBox(L.T("Autoriser les frames .lay DMD", "Allow .lay DMD frames"), ini.GetBool("DOF", "DmdEnabled", true));
        dof.Children.Add(_dofDmd);
        page.Children.Add(Ui.Card(dof));

        // --- RetroAchievements ---
        page.Children.Add(Ui.SectionHeader("RetroAchievements"));
        var ra = new StackPanel();
        _raEnabled = Ui.CheckBox(L.T("Afficher les RetroAchievements (via APIExpose, aucune connexion directe)",
                "Show RetroAchievements (through APIExpose, no direct connection)"),
            ini.GetBool("RetroAchievements", "Enabled", false));
        ra.Children.Add(_raEnabled);
        _raMarquee = Ui.CheckBox(L.T("Sur le marquee", "On the marquee"), ini.GetBool("RetroAchievements", "MarqueeEnabled", true));
        ra.Children.Add(_raMarquee);
        _raDmd = Ui.CheckBox(L.T("Sur le DMD", "On the DMD"), ini.GetBool("RetroAchievements", "DmdEnabled", true));
        ra.Children.Add(_raDmd);
        _raBadgeTray = Ui.CheckBox(L.T("Badges d'achievements en bas du marquee", "Achievement badges at the bottom of the marquee"),
            ini.GetBool("RetroAchievements", "BadgeTrayEnabled", true));
        ra.Children.Add(_raBadgeTray);
        _raTakeover = Ui.CheckBox(L.T("Plein écran animé sur un unlock (ignoré en speedrun)", "Animated fullscreen on an unlock (ignored during speedruns)"),
            ini.GetBool("RetroAchievements", "UnlockTakeoverEnabled", true));
        ra.Children.Add(_raTakeover);
        page.Children.Add(Ui.Card(ra));

        // --- LiveData ---
        page.Children.Add(Ui.SectionHeader(L.T("Score et timer live", "Live score and timer")));
        var live = new StackPanel();
        _liveScore = Ui.CheckBox(L.T("Score live", "Live score"), ini.GetBool("LiveData", "ScoreEnabled", true));
        live.Children.Add(_liveScore);
        _liveTimer = Ui.CheckBox(L.T("Timer live", "Live timer"), ini.GetBool("LiveData", "TimerEnabled", true));
        live.Children.Add(_liveTimer);
        _liveMarquee = Ui.CheckBox(L.T("Sur le marquee", "On the marquee"), ini.GetBool("LiveData", "MarqueeEnabled", true));
        live.Children.Add(_liveMarquee);
        _liveDmd = Ui.CheckBox(L.T("Sur le DMD", "On the DMD"), ini.GetBool("LiveData", "DmdEnabled", true));
        live.Children.Add(_liveDmd);
        page.Children.Add(Ui.Card(live));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer dans config.ini", "Save to config.ini"), OnSave, primary: true));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
    }

    private static (Slider slider, StackPanel line) PercentSlider(
        double value, double min, double max, TextBlock label, Func<double, string> format)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Width = 180,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        label.Text = format(slider.Value);
        slider.ValueChanged += (_, _) => label.Text = format(slider.Value);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(slider);
        line.Children.Add(label);
        return (slider, line);
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        static string B(CheckBox box) => (box.IsChecked == true).ToString().ToLowerInvariant();
        static string D(Slider slider, int decimals = 2)
            => Math.Round(slider.Value, decimals).ToString(System.Globalization.CultureInfo.InvariantCulture);

        var url = _apiUrl.Text.Trim();
        if (!url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = L.T("Adresse APIExpose invalide : attendu ws://hôte:port (ex. ws://127.0.0.1:12345).",
                "Invalid APIExpose address: expected ws://host:port (e.g. ws://127.0.0.1:12345).");
            return;
        }

        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        ini.Set("Settings", "ApiExposeBaseUrl", url);
        ini.Set("Settings", "MinimizeToTray", B(_tray));
        ini.Set("Settings", "LogToFile", B(_logToFile));

        ini.Set("Lighting", "Enabled", B(_lightingEnabled));
        ini.Set("Lighting", "RenderScale", D(_renderScale));
        ini.Set("Lighting", "FillHeightMaxCrop", D(_fillHeight));
        ini.Set("Lighting", "GlassReflection", D(_glass));
        ini.Set("Lighting", "PreferGeneratedMarquee", B(_preferGenerated));
        ini.Set("Lighting", "DmdMirror", B(_dmdMirror));
        ini.Set("Lighting", "SoundEnabled", B(_sound));

        ini.Set("DOF", "Enabled", B(_dofEnabled));
        ini.Set("DOF", "MarqueeEnabled", B(_dofMarquee));
        ini.Set("DOF", "DmdEnabled", B(_dofDmd));

        ini.Set("RetroAchievements", "Enabled", B(_raEnabled));
        ini.Set("RetroAchievements", "MarqueeEnabled", B(_raMarquee));
        ini.Set("RetroAchievements", "DmdEnabled", B(_raDmd));
        ini.Set("RetroAchievements", "BadgeTrayEnabled", B(_raBadgeTray));
        ini.Set("RetroAchievements", "UnlockTakeoverEnabled", B(_raTakeover));

        ini.Set("LiveData", "ScoreEnabled", B(_liveScore));
        ini.Set("LiveData", "TimerEnabled", B(_liveTimer));
        ini.Set("LiveData", "MarqueeEnabled", B(_liveMarquee));
        ini.Set("LiveData", "DmdEnabled", B(_liveDmd));

        ini.Save();
        _status.Text = L.T("Options enregistrées (sauvegarde .bak créée). Redémarrez MarqueeManager pour appliquer.",
            "Options saved (.bak backup created). Restart MarqueeManager to apply.");
    }
}
