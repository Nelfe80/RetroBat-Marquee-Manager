using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarqueeManager.Setup.Config;
using MarqueeManager.Setup.Controls;
using MarqueeManager.Setup.Detection;
using MarqueeManager.Setup.Localization;
using Path = System.IO.Path;

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
    private readonly CheckBox _dmdMirror;
    private readonly CheckBox _sound;
    private readonly Slider _soundVolume;
    private readonly TextBlock _soundVolumeLabel = Ui.MutedLabel("");
    private readonly Slider _tubeOpacity;
    private readonly TextBlock _tubeOpacityLabel = Ui.MutedLabel("");
    private readonly Slider _tubeThickness;
    private readonly TextBlock _tubeThicknessLabel = Ui.MutedLabel("");
    private readonly Slider _tubeBlur;
    private readonly TextBlock _tubeBlurLabel = Ui.MutedLabel("");
    private readonly Slider _tubeEndFade;
    private readonly TextBlock _tubeEndFadeLabel = Ui.MutedLabel("");
    private readonly TextBox _tubeColorBox;
    private readonly Image _tubePreviewImage = new() { Stretch = System.Windows.Media.Stretch.Fill, Opacity = 0.9 };
    private readonly Canvas _tubePreviewCanvas = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly Grid _tubePreviewHost = new();
    private const double TubePreviewWidth = 560;
    private double _tubePreviewHeight = 140;
    private bool _tubePreviewTwoTubes;
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
        lighting.Children.Add(Ui.MutedLabel(L.T(
            "Interrupteur général — le rendu vit sur les surfaces qui portent le composant « Rendu lumineux », "
            + "réglable par surface et par état d'affichage dans Mon setup.",
            "Master switch — the render lives on the surfaces carrying the “Lighting” component, "
            + "set per surface and per display state in My setup.")));

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
                ? L.T("tube invisible", "invisible tube")
                : $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Tube néon visible", "Visible neon tube"), tubeLine,
            L.T("halo + tube entrevu derrière l'affiche quand il vacille", "halo + tube glimpsed behind the print when it flickers")));

        (_tubeThickness, var thicknessLine) = PercentSlider(ini.GetDouble("Lighting", "TubeThickness", 1.0), 0.4, 2.0,
            _tubeThicknessLabel, v => $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Épaisseur du tube", "Tube thickness"), thicknessLine));

        (_tubeBlur, var blurLine) = PercentSlider(ini.GetDouble("Lighting", "TubeBlur", 1.0), 0.0, 2.0,
            _tubeBlurLabel, v => v <= 0 ? L.T("bords nets", "sharp edges") : $"{(int)(v * 100)} %");
        lighting.Children.Add(Ui.Row(L.T("Flou du tube", "Tube blur"), blurLine,
            L.T("un néon n'est jamais un trait net", "a neon is never a sharp line")));

        (_tubeEndFade, var endFadeLine) = PercentSlider(ini.GetDouble("Lighting", "TubeEndFade", 0.10), 0.0, 0.45,
            _tubeEndFadeLabel, v => v <= 0
                ? L.T("extrémités pleines", "full-length glow")
                : L.T($"{(int)(v * 100)} % par côté", $"{(int)(v * 100)} % per side"));
        lighting.Children.Add(Ui.Row(L.T("Extrémités assombries", "Dimmed ends"), endFadeLine,
            L.T("un tube vieillissant n'éclaire plus franchement ses extrémités", "an aging tube no longer lights its ends cleanly")));

        _tubeColorBox = Ui.TextBox(ini.Get("Lighting", "TubeColor", "#FFE0B2"), 100);
        var tubeColorLine = new WrapPanel();
        tubeColorLine.Children.Add(_tubeColorBox);
        tubeColorLine.Children.Add(Ui.ColorPalette(_tubeColorBox));
        lighting.Children.Add(Ui.Row(L.T("Couleur du néon", "Neon color"), tubeColorLine));

        // live preview of the tube over the marquee currently displayed by the runtime
        _tubePreviewHost.Width = TubePreviewWidth;
        _tubePreviewHost.Height = _tubePreviewHeight;
        _tubePreviewHost.Children.Add(_tubePreviewImage);
        _tubePreviewHost.Children.Add(_tubePreviewCanvas);
        lighting.Children.Add(new Border
        {
            Background = Ui.Viewport,
            BorderBrush = Ui.PanelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0),
            Margin = new Thickness(0, 8, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _tubePreviewHost
        });
        lighting.Children.Add(Ui.MutedLabel(L.T(
            "Prévisualisation (approchée) sur le marquee actuellement affiché par le runtime.",
            "Preview (approximate) over the marquee the runtime currently displays.")));
        LoadTubePreviewBackground();
        foreach (var slider in new[] { _tubeOpacity, _tubeThickness, _tubeBlur, _tubeEndFade })
        {
            slider.ValueChanged += (_, _) => RenderTubePreview();
        }
        _tubeColorBox.TextChanged += (_, _) => RenderTubePreview();
        RenderTubePreview();

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

        // --- layouts MAME (.lay), section [DOF] historique du config.ini ---
        page.Children.Add(Ui.SectionHeader(L.T("Compatibilité .lay (layouts MAME)", ".lay compatibility (MAME layouts)")));
        var dof = new StackPanel();
        dof.Children.Add(Ui.MutedLabel(L.T(
            "Le marquee d'un jeu est porté par sa scène lumineuse quand elle existe (Mon marquee dynamique Arcade) ; "
            + "le .lay garde alors uniquement sa vue DMD dédiée. Sans scène, ses vues marquee/topper/iccard restent servies.",
            "A game's marquee is owned by its light scene when one exists (My dynamic Arcade marquee); "
            + "the .lay then only keeps its dedicated DMD view. Without a scene, its marquee/topper/iccard views still serve.")));
        _dofEnabled = Ui.CheckBox(L.T("Lire les fichiers .lay MAME (marquee, topper, iccard, DMD)",
                "Read MAME .lay files (marquee, topper, iccard, DMD)"),
            ini.GetBool("DOF", "Enabled", true));
        dof.Children.Add(_dofEnabled);
        _dofMarquee = Ui.CheckBox(L.T("Autoriser les vues .lay sur les surfaces", "Allow .lay views on the surfaces"), ini.GetBool("DOF", "MarqueeEnabled", true));
        dof.Children.Add(_dofMarquee);
        _dofDmd = Ui.CheckBox(L.T("Autoriser les frames .lay sur le DMD", "Allow .lay frames on the DMD"), ini.GetBool("DOF", "DmdEnabled", true));
        dof.Children.Add(_dofDmd);
        page.Children.Add(Ui.Card(dof));

        // --- RetroAchievements ---
        page.Children.Add(Ui.SectionHeader("RetroAchievements"));
        var ra = new StackPanel();
        _raEnabled = Ui.CheckBox(L.T("Afficher les RetroAchievements (via APIExpose, aucune connexion directe)",
                "Show RetroAchievements (through APIExpose, no direct connection)"),
            ini.GetBool("RetroAchievements", "Enabled", false));
        ra.Children.Add(_raEnabled);
        ra.Children.Add(Ui.MutedLabel(L.T(
            "Ici on active les flux ; l'endroit où ils s'affichent se choisit par surface dans Mon setup (composants RetroAchievements).",
            "This enables the feeds; where they display is chosen per surface in My setup (RetroAchievements components).")));
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
        live.Children.Add(Ui.MutedLabel(L.T(
            "Ici on active les flux ; l'endroit où ils s'affichent se choisit par surface dans Mon setup (composants Score / Timer).",
            "This enables the feeds; where they display is chosen per surface in My setup (Score / Timer components).")));
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
            "Clés utilisées par « Récupérer des médias en ligne » (Mes jeux) et par le composant vidéo live. "
            + "Arcade Database ne demande aucune clé.",
            "Keys used by “Fetch media online” (My games) and by the live video component. "
            + "Arcade Database needs no key.")));
        foreach (var (key, label) in new[]
                 {
                     ("SteamGridDbApiKey", "SteamGridDB — API key"),
                     ("TheGamesDbApiKey", "TheGamesDB — API key"),
                     ("TwitchClientId", "Twitch — Client ID"),
                     ("TwitchClientSecret", "Twitch — Client Secret"),
                     ("YouTubeApiKey", "YouTube — Data API key")
                 })
        {
            online.Children.Add(TestableKeyRow(ini, key, label));
        }

        // ScreenScraper: the account resolves from config.ini or EmulationStation
        // and is displayed masked, never editable here — the developer credentials
        // resolve silently (env / APIExpose .env / build-embedded).
        var (esUser, esPassword) = Data.ScreenScraperCredentials.ResolveUser(pluginRoot, key => ini.Get("Scraper", key, ""));
        var fromEs = ini.Get("Scraper", "ScreenScraperUser", "").Length == 0 && esUser.Length > 0;
        var ssUserBox = Ui.TextBox(esUser.Length > 0 ? esUser : L.T("(aucun compte détecté)", "(no account detected)"), 280);
        ssUserBox.IsReadOnly = true;
        online.Children.Add(Ui.Row(L.T("ScreenScraper — utilisateur", "ScreenScraper — username"), ssUserBox,
            hint: fromEs ? L.T("(repris d'EmulationStation)", "(picked up from EmulationStation)") : null));
        var ssPassBox = Ui.TextBox(esPassword.Length > 0 ? "********" : "", 280);
        ssPassBox.IsReadOnly = true;
        var ssLine = new StackPanel { Orientation = Orientation.Horizontal };
        ssLine.Children.Add(ssPassBox);
        var ssResult = Ui.MutedLabel("");
        ssResult.VerticalAlignment = VerticalAlignment.Center;
        ssLine.Children.Add(Ui.Button(L.T("Tester", "Test"), async (_, _) =>
        {
            ssResult.Text = L.T("test…", "testing…");
            ssResult.Foreground = Ui.Muted;
            var ok = await TestSourceAsync("ScreenScraper");
            ssResult.Text = ok ? "✓" : L.T("✗ compte refusé", "✗ account rejected");
            ssResult.Foreground = ok ? Ui.Ok : Ui.Error;
        }));
        ssLine.Children.Add(ssResult);
        online.Children.Add(Ui.Row(L.T("ScreenScraper — mot de passe", "ScreenScraper — password"), ssLine));
        page.Children.Add(Ui.Card(online));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 6) };
        actions.Children.Add(Ui.Button(L.T("Enregistrer dans config.ini", "Save to config.ini"), OnSave, primary: true));
        page.Children.Add(actions);
        _status.TextWrapping = TextWrapping.Wrap;
        page.Children.Add(_status);

        Content = Ui.Page(page);
    }

    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // ================= neon tube preview =================

    /// <summary>The marquee the RUNTIME currently displays, read from the tail of
    /// its log ("Displaying image on target marquee … : path"). Best effort.</summary>
    private string? FindCurrentMarqueePath()
    {
        try
        {
            var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
            var logPath = Path.Combine(_pluginRoot, ini.Get("Settings", "LogFilePath", @".log\debug.log"));
            if (!File.Exists(logPath)) return null;
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Seek(Math.Max(0, stream.Length - 262_144), SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            string? found = null;
            while (reader.ReadLine() is { } line)
            {
                if (!line.Contains("Displaying image on target marquee", StringComparison.OrdinalIgnoreCase)) continue;
                var marker = line.LastIndexOf("): ", StringComparison.Ordinal);
                if (marker > 0) found = line[(marker + 3)..].Trim();
            }
            return found != null && File.Exists(found) ? found : null;
        }
        catch
        {
            return null;
        }
    }

    private void LoadTubePreviewBackground()
    {
        var path = FindCurrentMarqueePath();
        if (path == null) return;
        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            _tubePreviewImage.Source = bitmap;
            if (bitmap.PixelWidth > 0)
            {
                _tubePreviewHeight = Math.Clamp(TubePreviewWidth * bitmap.PixelHeight / bitmap.PixelWidth, 80, 220);
                _tubePreviewHost.Height = _tubePreviewHeight;
                // same rule as the runtime: wide marquee = one center tube, tall = two
                _tubePreviewTwoTubes = (double)bitmap.PixelWidth / bitmap.PixelHeight < 2.2;
            }
        }
        catch
        {
            // unreadable image: the dark viewport stays as background
        }
    }

    /// <summary>WPF approximation of the runtime's vector tube (halo, blurred gas
    /// column, overexposed core, dimmed ends) refreshed on every slider move.</summary>
    private void RenderTubePreview()
    {
        var canvas = _tubePreviewCanvas;
        canvas.Children.Clear();
        var opacity = _tubeOpacity.Value;
        if (opacity <= 0) return;
        var w = TubePreviewWidth;
        var h = _tubePreviewHeight;
        var color = ParsePreviewColor(_tubeColorBox.Text);
        var hot = System.Windows.Media.Color.FromRgb(
            (byte)(color.R + (255 - color.R) * 0.78), (byte)(color.G + (255 - color.G) * 0.78),
            (byte)(color.B + (255 - color.B) * 0.78));
        var thickness = (_tubePreviewTwoTubes ? 0.13 : 0.16) * h * _tubeThickness.Value;
        var blur = _tubeBlur.Value;
        var endFade = _tubeEndFade.Value;
        var left = 0.045 * w;
        var width = 0.91 * w;

        System.Windows.Media.LinearGradientBrush? endFadeMask = null;
        if (endFade > 0.01)
        {
            endFadeMask = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5)
            };
            endFadeMask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(30, 255, 255, 255), 0));
            endFadeMask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.White, endFade));
            endFadeMask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.White, 1 - endFade));
            endFadeMask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(30, 255, 255, 255), 1));
            endFadeMask.Freeze();
        }

        foreach (var tubeY in _tubePreviewTwoTubes ? new[] { 0.30, 0.70 } : new[] { 0.50 })
        {
            var centerY = tubeY * h;

            // halo bathing the print
            var haloHalf = thickness * 2.4;
            var halo = new System.Windows.Shapes.Rectangle
            {
                Width = width, Height = haloHalf * 2,
                Fill = new System.Windows.Media.LinearGradientBrush(
                    new System.Windows.Media.GradientStopCollection
                    {
                        new(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 0),
                        new(System.Windows.Media.Color.FromArgb((byte)(opacity * 110), color.R, color.G, color.B), 0.5),
                        new(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 1)
                    }, 90)
            };
            Canvas.SetLeft(halo, left);
            Canvas.SetTop(halo, centerY - haloHalf);
            canvas.Children.Add(halo);

            // blurred gas column
            var column = new System.Windows.Shapes.Rectangle
            {
                Width = width, Height = thickness,
                RadiusX = thickness / 2, RadiusY = thickness / 2,
                Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb((byte)(opacity * 170), color.R, color.G, color.B))
            };
            if (blur > 0.01) column.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = thickness * 0.5 * blur };
            if (endFadeMask != null) column.OpacityMask = endFadeMask;
            Canvas.SetLeft(column, left);
            Canvas.SetTop(column, centerY - thickness / 2);
            canvas.Children.Add(column);

            // overexposed core
            var core = new System.Windows.Shapes.Rectangle
            {
                Width = Math.Max(4, width - thickness * 0.8), Height = thickness * 0.46,
                RadiusX = thickness * 0.23, RadiusY = thickness * 0.23,
                Fill = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb((byte)(opacity * 235), hot.R, hot.G, hot.B))
            };
            if (blur > 0.01) core.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = thickness * 0.35 * blur };
            if (endFadeMask != null) core.OpacityMask = endFadeMask;
            Canvas.SetLeft(core, left + thickness * 0.4);
            Canvas.SetTop(core, centerY - thickness * 0.23);
            canvas.Children.Add(core);
        }
    }

    private static System.Windows.Media.Color ParsePreviewColor(string hex)
    {
        try
        {
            return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex.Trim());
        }
        catch
        {
            return System.Windows.Media.Color.FromRgb(255, 224, 178);
        }
    }

    /// <summary>API key field + "Tester" button: a successful probe shows ✓ and
    /// saves the key to config.ini on the spot (no separate save step needed).</summary>
    private UIElement TestableKeyRow(IniFile ini, string key, string label)
    {
        var box = Ui.TextBox(ini.Get("Scraper", key, ""), 280);
        _scraperKeys[key] = box;
        var result = Ui.MutedLabel("");
        result.VerticalAlignment = VerticalAlignment.Center;
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(box);
        line.Children.Add(Ui.Button(L.T("Tester", "Test"), async (_, _) =>
        {
            result.Text = L.T("test…", "testing…");
            result.Foreground = Ui.Muted;
            var ok = await TestSourceAsync(key);
            if (ok)
            {
                // the Twitch token needs BOTH halves: a green check stores the pair
                SaveScraperKeys(key.StartsWith("Twitch", StringComparison.Ordinal)
                    ? new[] { "TwitchClientId", "TwitchClientSecret" }
                    : new[] { key });
                result.Text = "✓ " + L.T("enregistré", "saved");
                result.Foreground = Ui.Ok;
            }
            else
            {
                result.Text = L.T("✗ clé refusée", "✗ key rejected");
                result.Foreground = Ui.Error;
            }
        }));
        line.Children.Add(result);
        return Ui.Row(label, line);
    }

    private void SaveScraperKeys(string[] keys)
    {
        var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
        foreach (var key in keys)
        {
            if (_scraperKeys.TryGetValue(key, out var box))
            {
                ini.Set("Scraper", key, box.Text.Trim());
            }
        }
        ini.Save();
    }

    /// <summary>Cheap authenticated probe of each online source: one tiny request,
    /// valid credentials = success status.</summary>
    private async Task<bool> TestSourceAsync(string key)
    {
        try
        {
            switch (key)
            {
                case "SteamGridDbApiKey":
                {
                    var value = _scraperKeys[key].Text.Trim();
                    if (value.Length == 0) return false;
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,
                        "https://www.steamgriddb.com/api/v2/search/autocomplete/mario");
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", value);
                    using var response = await Http.SendAsync(request);
                    return response.IsSuccessStatusCode;
                }
                case "TheGamesDbApiKey":
                {
                    var value = _scraperKeys[key].Text.Trim();
                    if (value.Length == 0) return false;
                    using var response = await Http.GetAsync(
                        $"https://api.thegamesdb.net/v1.1/Games/ByGameName?apikey={Uri.EscapeDataString(value)}&name=mario");
                    return response.IsSuccessStatusCode;
                }
                case "TwitchClientId":
                case "TwitchClientSecret":
                {
                    var id = _scraperKeys["TwitchClientId"].Text.Trim();
                    var secret = _scraperKeys["TwitchClientSecret"].Text.Trim();
                    if (id.Length == 0 || secret.Length == 0) return false;
                    using var content = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["client_id"] = id,
                        ["client_secret"] = secret,
                        ["grant_type"] = "client_credentials"
                    });
                    using var response = await Http.PostAsync("https://id.twitch.tv/oauth2/token", content);
                    return response.IsSuccessStatusCode;
                }
                case "YouTubeApiKey":
                {
                    var value = _scraperKeys[key].Text.Trim();
                    if (value.Length == 0) return false;
                    using var response = await Http.GetAsync(
                        $"https://www.googleapis.com/youtube/v3/videos?part=id&id=dQw4w9WgXcQ&key={Uri.EscapeDataString(value)}");
                    return response.IsSuccessStatusCode;
                }
                case "ScreenScraper":
                {
                    var (devId, devPassword) = Data.ScreenScraperCredentials.ResolveDev(_pluginRoot);
                    var ini = IniFile.Load(PluginPaths.ConfigPath(_pluginRoot));
                    var (user, password) = Data.ScreenScraperCredentials.ResolveUser(_pluginRoot, k => ini.Get("Scraper", k, ""));
                    if (devId.Length == 0 || user.Length == 0) return false;
                    var url = "https://api.screenscraper.fr/api2/ssuserInfos.php?output=json"
                              + $"&devid={Uri.EscapeDataString(devId)}&devpassword={Uri.EscapeDataString(devPassword)}"
                              + "&softname=RetroBat-MarqueeManager"
                              + $"&ssid={Uri.EscapeDataString(user)}&sspassword={Uri.EscapeDataString(password)}";
                    using var response = await Http.GetAsync(url);
                    if (!response.IsSuccessStatusCode) return false;
                    var body = await response.Content.ReadAsStringAsync();
                    return body.Contains("\"ssuser\"", StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        catch
        {
            // network failure counts as a failed probe
        }
        return false;
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
        // PreferGeneratedMarquee : plus exposé ici — l'ordre de la chaîne de
        // sources (Mes systèmes) porte cette intention ; la clé ini reste honorée
        ini.Set("Lighting", "DmdMirror", B(_dmdMirror));
        ini.Set("Lighting", "SoundEnabled", B(_sound));
        ini.Set("Lighting", "SoundVolume", D(_soundVolume));
        ini.Set("Lighting", "TubeVisualOpacity", D(_tubeOpacity));
        ini.Set("Lighting", "TubeThickness", D(_tubeThickness));
        ini.Set("Lighting", "TubeBlur", D(_tubeBlur));
        ini.Set("Lighting", "TubeEndFade", D(_tubeEndFade));
        var tubeColor = _tubeColorBox.Text.Trim();
        if (tubeColor.StartsWith('#') && tubeColor.Length == 7)
        {
            ini.Set("Lighting", "TubeColor", tubeColor);
        }
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
