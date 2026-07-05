using System.Text;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Configuration;

public sealed class IniConfigService : IConfigService
{
    private const int CurrentVersion = 2;
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _flat = new(StringComparer.OrdinalIgnoreCase);

    public IniConfigService(string? path = null)
    {
        BaseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        ConfigPath = path ?? Path.Combine(BaseDirectory, "config.ini");
        Load();
        if (!int.TryParse(GetSetting("ConfigVersion", "0"), out var version) || version < CurrentVersion || !HasV2Shape())
        {
            MigrateToV2();
            Load();
        }
    }

    public string ConfigPath { get; }
    public string BaseDirectory { get; }
    public string ApiExposeWebSocketBaseUrl => NormalizeWs(Get("Settings", "ApiExposeBaseUrl", "ws://127.0.0.1:12345"));
    public bool MinimizeToTray => Bool("Settings", "MinimizeToTray", true);
    public bool LogToFile => Bool("Settings", "LogToFile", true);
    public string LogFilePath => Absolute(Get("Settings", "LogFilePath", @".log\debug.log"));

    public bool DmdEnabled => Bool("DMD", "Enabled", BoolFlat("DmdEnabled", false));
    public string DmdModel => Get("DMD", "Model", GetSetting("DmdModel", "zedmd"));
    public string DmdExePath => Absolute(Get("DMD", "ExePath", GetSetting("DmdExePath", @"tools\dmd\dmdext.exe")));
    public int DmdWidth => Int("DMD", "Width", IntFlat("DmdWidth", 128));
    public int DmdHeight => Int("DMD", "Height", IntFlat("DmdHeight", 32));
    public string ZeDmdPort => Get("DMD", "ZeDmdPort", GetSetting("ZeDmdPort", ""));
    public bool DmdOptimizeZeDmd => Bool("DMD", "OptimizeZeDmd", BoolFlat("OptimizeZeDmd", true));
    public int DmdBrightness => Int("DMD", "Brightness", IntFlat("DmdBrightness", -1));
    public int ZeDmdUsbPackageSize => Int("DMD", "UsbPackageSize", IntFlat("ZeDmdUsbPackageSize", 0));
    public int ZeDmdPanelMinRefreshRate => Int("DMD", "PanelMinRefreshRate", IntFlat("ZeDmdPanelMinRefreshRate", 0));
    public int DmdMinimumBlockDisplayMs => PositiveInt("DMD", "MinimumBlockDisplayMs", 3000, 250);
    public IReadOnlySet<string> ActiveSystemsDmd => Get("DMD", "ActiveSystemsDMD", GetSetting("ActiveSystemsDMD", ""))
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.ToLowerInvariant())
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool LayEnabled => Bool("DOF", "Enabled", BoolFlat("LayEnabled", true));
    public bool LayLcdEnabled => Bool("DOF", "MarqueeEnabled", BoolFlat("LayLcdEnabled", true));
    public bool LayDmdEnabled => Bool("DOF", "DmdEnabled", BoolFlat("LayDmdEnabled", true));
    public string LayDofPath => Absolute(Get("DOF", "Path", GetSetting("LayDofPath", @"resources\dof\mame")));

    public bool RetroAchievementsEnabled => Bool("RetroAchievements", "Enabled", BoolFlat("MarqueeRetroAchievements", false));
    public bool RetroAchievementsMarqueeEnabled => Bool("RetroAchievements", "MarqueeEnabled", true);
    public bool RetroAchievementsDmdEnabled => Bool("RetroAchievements", "DmdEnabled", true);
    public bool RetroAchievementsPersistentEnabled => Bool("RetroAchievements", "PersistentEnabled", true);
    public bool RetroAchievementsNotificationsEnabled => Bool("RetroAchievements", "NotificationsEnabled", true);
    public bool RetroAchievementsModeEnabled => Bool("RetroAchievements", "ModeEnabled", false);
    public bool RetroAchievementsScoreEnabled => Bool("RetroAchievements", "ScoreEnabled", true);
    public bool RetroAchievementsUnlockEnabled => Bool("RetroAchievements", "UnlockEnabled", true);
    public bool RetroAchievementsWarningEnabled => Bool("RetroAchievements", "WarningEnabled", true);
    public bool RetroAchievementsChallengeEnabled => Bool("RetroAchievements", "ChallengeEnabled", true);
    public bool RetroAchievementsLeaderboardEnabled => Bool("RetroAchievements", "LeaderboardEnabled", true);
    public int RetroAchievementsScoreDurationMs => PositiveInt("RetroAchievements", "ScoreDurationMs", 6000, 250);
    public int RetroAchievementsUnlockDurationMs => PositiveInt("RetroAchievements", "UnlockDurationMs", 6000, 250);
    public int RetroAchievementsWarningDurationMs => PositiveInt("RetroAchievements", "WarningDurationMs", 5000, 250);
    public int RetroAchievementsLeaderboardDurationMs => PositiveInt("RetroAchievements", "LeaderboardDurationMs", 6000, 250);
    public int RetroAchievementsSpeedrunUsersPerSecond => Math.Clamp(Int("RetroAchievements", "SpeedrunUsersPerSecond", 4), 1, 20);
    public bool RetroAchievementsBadgeTrayEnabled => Bool("RetroAchievements", "BadgeTrayEnabled", true);
    public bool RetroAchievementsUnlockTakeoverEnabled => Bool("RetroAchievements", "UnlockTakeoverEnabled", true);
    public int RetroAchievementsSpeedrunResultDurationMs => PositiveInt("RetroAchievements", "SpeedrunResultDurationMs", 5000, 500);

    public bool LightingEnabled => Bool("Lighting", "Enabled", false);
    public bool LightingTestPattern => Bool("Lighting", "TestPattern", false);
    public int LightingFpsLimit => Math.Clamp(Int("Lighting", "FpsLimit", 60), 15, 240);
    public bool LightingShowFps => Bool("Lighting", "ShowFps", true);
    public double LightingRenderScale => Math.Clamp(Double("Lighting", "RenderScale", 0.5), 0.25, 1.0);
    public double LightingFillHeightMaxCrop => Math.Clamp(Double("Lighting", "FillHeightMaxCrop", 0.30), 0.0, 0.6);
    public bool LightingSoundEnabled => Bool("Lighting", "SoundEnabled", true);
    public double LightingSoundVolume => Math.Clamp(Double("Lighting", "SoundVolume", 0.30), 0.0, 0.30);
    public double LightingGlassReflection => Math.Clamp(Double("Lighting", "GlassReflection", 0.06), 0.0, 0.3);
    public double LightingTubeVisualOpacity => Math.Clamp(Double("Lighting", "TubeVisualOpacity", 0.0), 0.0, 0.5);
    public bool LightingPreferGeneratedMarquee => Bool("Lighting", "PreferGeneratedMarquee", false);
    public bool LightingDmdMirror => Bool("Lighting", "DmdMirror", false);

    public bool LiveScoreEnabled => Bool("LiveData", "ScoreEnabled", true);
    public bool LiveTimerEnabled => Bool("LiveData", "TimerEnabled", true);
    public bool LiveDataMarqueeEnabled => Bool("LiveData", "MarqueeEnabled", true);
    public bool LiveDataDmdEnabled => Bool("LiveData", "DmdEnabled", true);
    public int LiveScoreDurationMs => PositiveInt("LiveData", "ScoreDurationMs", 4000, 250);
    public int LiveTimerDurationMs => PositiveInt("LiveData", "TimerDurationMs", 4000, 250);

    public IReadOnlyList<int> GetScreenIndices(string target)
    {
        var key = target.ToLowerInvariant() switch
        {
            "marquee" => "MarqueeScreen",
            "topper" => "TopperScreen",
            "iccard" => "IcCardScreen",
            "dmd" => "DmdScreen",
            "lcd" => "LcdScreen",
            _ => target + "Screen"
        };
        var raw = Get("Screens", key, target.Equals("marquee", StringComparison.OrdinalIgnoreCase) ? "1" : "-1");
        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var index) ? index : -1)
            .Where(index => index >= 0)
            .Distinct()
            .ToArray();
    }

    public TargetBounds? GetTargetBounds(string target)
    {
        var key = char.ToUpperInvariant(target[0]) + target[1..].ToLowerInvariant() + "Bounds";
        if (target.Equals("iccard", StringComparison.OrdinalIgnoreCase)) key = "IcCardBounds";
        var raw = Get("Screens", key, "");
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return null;
        var values = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], out values[i])) return null;
        if (values[2] <= 0 || values[3] <= 0) return null;
        return new TargetBounds(values[0], values[1], values[2], values[3]);
    }

    public IReadOnlyList<string> GetTargetsForContent(string source)
    {
        var result = new List<string>();
        foreach (var target in new[] { "Marquee", "Topper", "IcCard", "Dmd", "Lcd" })
        {
            var configured = Get("Screens", target + "Content", target.ToLowerInvariant());
            if (configured.Equals(source, StringComparison.OrdinalIgnoreCase) && GetScreenIndices(target).Count > 0)
                result.Add(target.ToLowerInvariant());
        }
        return result;
    }

    public string GetSetting(string key, string defaultValue = "")
        => _flat.TryGetValue(key, out var value) ? value : defaultValue;

    private void Load()
    {
        _sections.Clear();
        _flat.Clear();
        if (!File.Exists(ConfigPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath) ?? BaseDirectory);
            File.WriteAllText(ConfigPath, BuildV2(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)), Encoding.UTF8);
        }

        var section = "Settings";
        foreach (var sourceLine in File.ReadAllLines(ConfigPath))
        {
            var line = sourceLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var split = line.IndexOf('=');
            if (split <= 0) continue;
            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (!_sections.TryGetValue(section, out var values))
                _sections[section] = values = new(StringComparer.OrdinalIgnoreCase);
            values[key] = value;
            _flat[key] = value;
        }
    }

    private void MigrateToV2()
    {
        var legacy = new Dictionary<string, string>(_flat, StringComparer.OrdinalIgnoreCase);
        var backup = ConfigPath + ".v1.bak";
        if (File.Exists(ConfigPath) && !File.Exists(backup)) File.Copy(ConfigPath, backup);
        File.WriteAllText(ConfigPath, BuildV2(legacy), Encoding.UTF8);
    }

    private string BuildV2(IReadOnlyDictionary<string, string> old)
    {
        string V(string key, string fallback) => old.TryGetValue(key, out var value) ? value : fallback;
        var sb = new StringBuilder();
        sb.AppendLine("[Settings]");
        sb.AppendLine($"ConfigVersion={CurrentVersion}");
        sb.AppendLine($"ApiExposeBaseUrl={V("ApiExposeBaseUrl", "ws://127.0.0.1:12345")}");
        sb.AppendLine($"MinimizeToTray={V("MinimizeToTray", "true")}");
        sb.AppendLine($"LogToFile={V("LogToFile", "true")}");
        sb.AppendLine($"LogFilePath={V("LogFilePath", @".log\debug.log")}");
        sb.AppendLine();
        sb.AppendLine("[Screens]");
        sb.AppendLine("; <Cible>Bounds=x,y,largeur,hauteur : fenetre positionnee dans l'ecran cible");
        sb.AppendLine("; (pixels, relatif au coin haut-gauche de l'ecran). Absent = plein ecran.");
        sb.AppendLine("; Permet plusieurs fenetres (marquee, iccard...) sur un meme ecran vertical.");
        foreach (var name in new[] { "Marquee", "Topper", "IcCard", "Dmd", "Lcd" })
        {
            sb.AppendLine($"{name}Screen={V(name + "Screen", name == "Marquee" ? "1" : "-1")}");
            sb.AppendLine($"{name}Content={V(name + "Content", name.ToLowerInvariant())}");
            var bounds = V(name + "Bounds", "");
            if (bounds.Length > 0) sb.AppendLine($"{name}Bounds={bounds}");
        }
        sb.AppendLine();
        sb.AppendLine("[DMD]");
        sb.AppendLine($"Enabled={V("DmdEnabled", V("Enabled", "false"))}");
        sb.AppendLine($"Model={V("Model", V("DmdModel", "zedmd"))}");
        sb.AppendLine($"ExePath={V("DmdExePath", @"tools\dmd\dmdext.exe")}");
        sb.AppendLine($"Width={V("DmdWidth", V("Width", "128"))}");
        sb.AppendLine($"Height={V("DmdHeight", V("Height", "32"))}");
        sb.AppendLine($"ZeDmdPort={V("ZeDmdPort", "")}");
        sb.AppendLine($"OptimizeZeDmd={V("OptimizeZeDmd", "true")}");
        sb.AppendLine($"Brightness={V("DmdBrightness", V("Brightness", "-1"))}");
        sb.AppendLine($"UsbPackageSize={V("ZeDmdUsbPackageSize", V("UsbPackageSize", "0"))}");
        sb.AppendLine($"PanelMinRefreshRate={V("ZeDmdPanelMinRefreshRate", V("PanelMinRefreshRate", "0"))}");
        sb.AppendLine("MinimumBlockDisplayMs=3000");
        sb.AppendLine($"ActiveSystemsDMD={V("ActiveSystemsDMD", "fpinball,pinballfx,pinballfx2,pinballfx3,pinballfm,vpinball,zaccariapinball")}");
        sb.AppendLine();
        sb.AppendLine("[DOF]");
        sb.AppendLine($"Enabled={V("LayEnabled", "true")}");
        sb.AppendLine($"MarqueeEnabled={V("LayLcdEnabled", "true")}");
        sb.AppendLine($"DmdEnabled={V("LayDmdEnabled", "true")}");
        sb.AppendLine($"Path={V("LayDofPath", @"resources\dof\mame")}");
        sb.AppendLine();
        sb.AppendLine("[RetroAchievements]");
        sb.AppendLine($"Enabled={V("MarqueeRetroAchievements", "false")}");
        sb.AppendLine("MarqueeEnabled=true");
        sb.AppendLine("DmdEnabled=true");
        sb.AppendLine("PersistentEnabled=true");
        sb.AppendLine("NotificationsEnabled=true");
        sb.AppendLine("ModeEnabled=false");
        sb.AppendLine("ScoreEnabled=true");
        sb.AppendLine("UnlockEnabled=true");
        sb.AppendLine("WarningEnabled=true");
        sb.AppendLine("ChallengeEnabled=true");
        sb.AppendLine("LeaderboardEnabled=true");
        sb.AppendLine("ScoreDurationMs=6000");
        sb.AppendLine("UnlockDurationMs=6000");
        sb.AppendLine("WarningDurationMs=5000");
        sb.AppendLine("LeaderboardDurationMs=6000");
        sb.AppendLine("SpeedrunUsersPerSecond=4");
        sb.AppendLine("; true = affiche les badges en bas du marquee, remonte progressivement sur unlock.");
        sb.AppendLine("BadgeTrayEnabled=true");
        sb.AppendLine("; true = plein écran coupe+badge animé sur un unlock (ignoré en mode speedrun).");
        sb.AppendLine("UnlockTakeoverEnabled=true");
        sb.AppendLine("; Durée d'affichage du résultat speedrun (temps + rank + diff) après submit.");
        sb.AppendLine("SpeedrunResultDurationMs=5000");
        sb.AppendLine();
        sb.AppendLine("[Lighting]");
        sb.AppendLine("; Moteur de marquee a lumiere dynamique (RBMarquee Lighting Engine).");
        sb.AppendLine("Enabled=false");
        sb.AppendLine("; true = motif de test shader (validation Phase 0), remplace le contenu du marquee.");
        sb.AppendLine("TestPattern=false");
        sb.AppendLine("FpsLimit=60");
        sb.AppendLine("; true = compteur FPS incruste dans la surface de rendu.");
        sb.AppendLine("ShowFps=true");
        sb.AppendLine("; Resolution interne de rendu (0.25 a 1.0). 0.5 recommande en raster CPU.");
        sb.AppendLine("RenderScale=0.5");
        sb.AppendLine("; Cadrage : remplit la hauteur de la fenetre (rogne les cotes, centre) si la");
        sb.AppendLine("; perte de matiere reste sous ce seuil (0 a 0.6). 0 = toujours letterbox.");
        sb.AppendLine("FillHeightMaxCrop=0.30");
        sb.AppendLine("; Sons des tubes (Resources\\sounds), synchronises sur le scintillement.");
        sb.AppendLine("SoundEnabled=true");
        sb.AppendLine("; Volume maitre, plafonne a 0.30.");
        sb.AppendLine("SoundVolume=0.30");
        sb.AppendLine("; Reflet de la vitre du marquee (0 a 0.3). 0 = pas de vitre.");
        sb.AppendLine("GlassReflection=0.06");
        sb.AppendLine("; Visibilite du tube physique derriere la vitre (0 a 0.5). 0 = invisible.");
        sb.AppendLine("TubeVisualOpacity=0");
        sb.AppendLine("; false = prefere le marquee scrape reel au generated quand les deux existent.");
        sb.AppendLine("; true = garde le generated (utile si le scan reel est de mauvaise qualite).");
        sb.AppendLine("PreferGeneratedMarquee=false");
        sb.AppendLine("; true = miroir de l'animation lumineuse du marquee sur le DMD physique.");
        sb.AppendLine("; false recommande : le DMD affiche le media fourni par le flux (gif anime");
        sb.AppendLine("; en priorite, sinon dmd generated / logo), plus lumineux et mieux adapte.");
        sb.AppendLine("DmdMirror=false");
        sb.AppendLine();
        sb.AppendLine("[LiveData]");
        sb.AppendLine("ScoreEnabled=true");
        sb.AppendLine("TimerEnabled=true");
        sb.AppendLine("MarqueeEnabled=true");
        sb.AppendLine("DmdEnabled=true");
        sb.AppendLine("ScoreDurationMs=4000");
        sb.AppendLine("TimerDurationMs=4000");
        return sb.ToString();
    }

    private string Get(string section, string key, string fallback)
        => _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : fallback;
    private bool HasV2Shape()
        => _sections.TryGetValue("DMD", out var dmd) && dmd.ContainsKey("Model")
           && _sections.TryGetValue("Settings", out var settings) && settings.ContainsKey("ConfigVersion")
           && _sections.ContainsKey("RetroAchievements")
           && _sections.ContainsKey("LiveData");
    private bool Bool(string section, string key, bool fallback) => bool.TryParse(Get(section, key, fallback.ToString()), out var value) ? value : fallback;
    private bool BoolFlat(string key, bool fallback) => bool.TryParse(GetSetting(key, fallback.ToString()), out var value) ? value : fallback;
    private int Int(string section, string key, int fallback) => int.TryParse(Get(section, key, fallback.ToString()), out var value) ? value : fallback;
    private double Double(string section, string key, double fallback) => double.TryParse(Get(section, key, fallback.ToString(System.Globalization.CultureInfo.InvariantCulture)), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private int PositiveInt(string section, string key, int fallback, int minimum)
        => Math.Max(minimum, Int(section, key, fallback));
    private int IntFlat(string key, int fallback) => int.TryParse(GetSetting(key, fallback.ToString()), out var value) ? value : fallback;
    private string Absolute(string path) => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(BaseDirectory, path));
    private static string NormalizeWs(string value) => value.TrimEnd('/').Replace("http://", "ws://", StringComparison.OrdinalIgnoreCase).Replace("https://", "wss://", StringComparison.OrdinalIgnoreCase);
}
