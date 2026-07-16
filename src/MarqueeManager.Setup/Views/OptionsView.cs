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
    private readonly Slider _soundVolume;
    private readonly TextBlock _soundVolumeLabel = Ui.MutedLabel("");
    private readonly Slider _tubeOpacity;
    private readonly TextBlock _tubeOpacityLabel = Ui.MutedLabel("");
    private readonly ComboBox _fpsLimit;
    private readonly CheckBox _showFps;
    private readonly CheckBox _dofEnabled;
    private readonly CheckBox _dofMarquee;
    private readonly CheckBox _dofDmd;
    private readonly CheckBox _raEnabled;
    private readonly CheckBox _raMarquee;
    private readonly CheckBox _raDmd;
    private readonly CheckBox _raBadgeTray;
    private readonly CheckBox _raTakeover;
    private readonly Dictionary<string, CheckBox> _raEvents = new();
    private readonly TextBox _raUnlockMs;
    private readonly TextBox _raScoreMs;
    private readonly TextBox _raLeaderboardMs;
    private readonly TextBox _raSpeedrunUsers;
    private readonly CheckBox _liveScore;
    private readonly CheckBox _liveTimer;
    private readonly CheckBox _liveMarquee;
    private readonly CheckBox _liveDmd;
    private readonly TextBox _liveScoreMs;
    private readonly TextBox _liveTimerMs;
    private readonly Dictionary<string, TextBox> _scraperKeys = new();

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

        (_soundVolume, var volumeLine) = PercentSlider(ini.GetDouble("Lighting", "SoundVolume", 0.30), 0.0, 0.5,
            _soundVolumeLabel, v => $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Volume des tubes", "Tube volume"), volumeLine,
            L.T("hum, buzz et claquements d'allumage", "hum, buzz and ignition ticks")));

        (_tubeOpacity, var tubeLine) = PercentSlider(ini.GetDouble("Lighting", "TubeVisualOpacity", 0.0), 0.0, 1.0,
            _tubeOpacityLabel, v => v <= 0
                ? L.T("tube invisible (recommandé)", "invisible tube (recommended)")
                : $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Tube néon visible", "Visible neon tube"), tubeLine));

        _fpsLimit = Ui.ComboBox(120);
        foreach (var fps in new[] { 30, 45, 60 })
        {
            var item = new ComboBoxItem { Content = $"{fps} FPS", Tag = fps };
            _fpsLimit.Items.Add(item);
            if (ini.GetInt("Lighting", "FpsLimit", 60) == fps)
            {
                _fpsLimit.SelectedItem = item;
            }
        }
        if (_fpsLimit.SelectedItem == null)
        {
            _fpsLimit.SelectedIndex = _fpsLimit.Items.Count - 1;
        }
        var fpsLine = new StackPanel { Orientation = Orientation.Horizontal };
        fpsLine.Children.Add(_fpsLimit);
        _showFps = Ui.CheckBox(L.T("Afficher le compteur FPS (debug)", "Show the FPS counter (debug)"),
            ini.GetBool("Lighting", "ShowFps", false));
        fpsLine.Children.Add(_showFps);
        lighting.Children.Add(Ui.Row(L.T("Cadence maximale", "Frame rate cap"), fpsLine));
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

        ra.Children.Add(Ui.MutedLabel(L.T("Événements affichés :", "Displayed events:")));
        var raEvents = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var (key, fr, en) in new[]
                 {
                     ("ScoreEnabled", "Score RA", "RA score"),
                     ("UnlockEnabled", "Unlocks", "Unlocks"),
                     ("WarningEnabled", "Avertissements", "Warnings"),
                     ("ChallengeEnabled", "Challenges", "Challenges"),
                     ("LeaderboardEnabled", "Leaderboards / speedrun", "Leaderboards / speedrun"),
                     ("NotificationsEnabled", "Notifications", "Notifications"),
                     ("PersistentEnabled", "Infos persistantes", "Persistent info")
                 })
        {
            var box = Ui.CheckBox(L.T(fr, en), ini.GetBool("RetroAchievements", key, true));
            box.Margin = new Thickness(0, 2, 16, 2);
            _raEvents[key] = box;
            raEvents.Children.Add(box);
        }
        ra.Children.Add(raEvents);

        ra.Children.Add(Ui.MutedLabel(L.T("Durées d'affichage (ms) :", "Display durations (ms):")));
        var raDurations = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        _raUnlockMs = DurationField(raDurations, L.T("Unlock", "Unlock"), ini.Get("RetroAchievements", "UnlockDurationMs", "6000"));
        _raScoreMs = DurationField(raDurations, L.T("Score", "Score"), ini.Get("RetroAchievements", "ScoreDurationMs", "6000"));
        _raLeaderboardMs = DurationField(raDurations, L.T("Leaderboard", "Leaderboard"), ini.Get("RetroAchievements", "LeaderboardDurationMs", "6000"));
        _raSpeedrunUsers = DurationField(raDurations, L.T("Vitesse défilement speedrun (users/s)", "Speedrun scroll speed (users/s)"),
            ini.Get("RetroAchievements", "SpeedrunUsersPerSecond", "4"));
        ra.Children.Add(raDurations);
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
        var liveDurations = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        _liveScoreMs = DurationField(liveDurations, L.T("Score visible (ms)", "Score visible (ms)"), ini.Get("LiveData", "ScoreDurationMs", "4000"));
        _liveTimerMs = DurationField(liveDurations, L.T("Timer visible (ms)", "Timer visible (ms)"), ini.Get("LiveData", "TimerDurationMs", "4000"));
        live.Children.Add(liveDurations);
        page.Children.Add(Ui.Card(live));

        // --- online sources (scraper + live video) ---
        page.Children.Add(Ui.SectionHeader(L.T("Sources en ligne (scrap de médias & vidéo live)", "Online sources (media scraping & live video)")));
        var online = new StackPanel();
        online.Children.Add(Ui.MutedLabel(L.T(
            "Clés utilisées par « Récupérer des médias en ligne » (Mes composants) et par le composant vidéo live. "
            + "Arcade Database ne demande aucune clé. ScreenScraper est déjà la source d'APIExpose — à réserver aux secours.",
            "Keys used by “Fetch media online” (My components) and by the live video component. "
            + "Arcade Database needs no key. ScreenScraper already feeds APIExpose — keep it as a fallback.")));
        foreach (var (key, label) in new[]
                 {
                     ("SteamGridDbApiKey", "SteamGridDB — API key"),
                     ("TheGamesDbApiKey", "TheGamesDB — API key"),
                     ("TwitchClientId", "Twitch — Client ID"),
                     ("TwitchClientSecret", "Twitch — Client Secret"),
                     ("YouTubeApiKey", "YouTube — Data API key"),
                     ("ScreenScraperUser", "ScreenScraper — utilisateur"),
                     ("ScreenScraperPassword", "ScreenScraper — mot de passe"),
                     ("ScreenScraperDevId", "ScreenScraper — dev ID"),
                     ("ScreenScraperDevPassword", "ScreenScraper — dev password")
                 })
        {
            var box = Ui.TextBox(ini.Get("Scraper", key, ""), 280);
            _scraperKeys[key] = box;
            online.Children.Add(Ui.Row(label, box));
        }
        page.Children.Add(Ui.Card(online));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer dans config.ini", "Save to config.ini"), OnSave, primary: true));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
    }

    /// <summary>Writes the key only when the field holds a valid positive number —
    /// a typo must not corrupt a working config.ini value.</summary>
    private static void SetIfNumeric(IniFile ini, string section, string key, TextBox box)
    {
        if (int.TryParse(box.Text.Trim(), out var value) && value >= 0)
        {
            ini.Set(section, key, value.ToString());
        }
    }

    /// <summary>Small labelled numeric field appended to a WrapPanel.</summary>
    private static TextBox DurationField(WrapPanel host, string label, string value)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 16, 2) };
        var text = Ui.MutedLabel(label);
        text.Margin = new Thickness(0, 0, 6, 0);
        line.Children.Add(text);
        var box = Ui.TextBox(value, 70);
        line.Children.Add(box);
        host.Children.Add(line);
        return box;
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
        ini.Set("Lighting", "SoundVolume", D(_soundVolume));
        ini.Set("Lighting", "TubeVisualOpacity", D(_tubeOpacity));
        if ((_fpsLimit.SelectedItem as ComboBoxItem)?.Tag is int fps)
        {
            ini.Set("Lighting", "FpsLimit", fps.ToString());
        }
        ini.Set("Lighting", "ShowFps", B(_showFps));

        ini.Set("DOF", "Enabled", B(_dofEnabled));
        ini.Set("DOF", "MarqueeEnabled", B(_dofMarquee));
        ini.Set("DOF", "DmdEnabled", B(_dofDmd));

        ini.Set("RetroAchievements", "Enabled", B(_raEnabled));
        ini.Set("RetroAchievements", "MarqueeEnabled", B(_raMarquee));
        ini.Set("RetroAchievements", "DmdEnabled", B(_raDmd));
        ini.Set("RetroAchievements", "BadgeTrayEnabled", B(_raBadgeTray));
        ini.Set("RetroAchievements", "UnlockTakeoverEnabled", B(_raTakeover));
        foreach (var (key, box) in _raEvents)
        {
            ini.Set("RetroAchievements", key, B(box));
        }
        SetIfNumeric(ini, "RetroAchievements", "UnlockDurationMs", _raUnlockMs);
        SetIfNumeric(ini, "RetroAchievements", "ScoreDurationMs", _raScoreMs);
        SetIfNumeric(ini, "RetroAchievements", "LeaderboardDurationMs", _raLeaderboardMs);
        SetIfNumeric(ini, "RetroAchievements", "SpeedrunUsersPerSecond", _raSpeedrunUsers);

        ini.Set("LiveData", "ScoreEnabled", B(_liveScore));
        ini.Set("LiveData", "TimerEnabled", B(_liveTimer));
        ini.Set("LiveData", "MarqueeEnabled", B(_liveMarquee));
        ini.Set("LiveData", "DmdEnabled", B(_liveDmd));
        SetIfNumeric(ini, "LiveData", "ScoreDurationMs", _liveScoreMs);
        SetIfNumeric(ini, "LiveData", "TimerDurationMs", _liveTimerMs);

        foreach (var (key, box) in _scraperKeys)
        {
            ini.Set("Scraper", key, box.Text.Trim());
        }

        ini.Save();
        _status.Text = L.T("Options enregistrées (sauvegarde .bak créée). Redémarrez MarqueeManager pour appliquer.",
            "Options saved (.bak backup created). Restart MarqueeManager to apply.");
    }
}
