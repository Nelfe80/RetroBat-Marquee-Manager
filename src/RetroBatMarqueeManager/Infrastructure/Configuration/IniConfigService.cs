using RetroBatMarqueeManager.Core.Interfaces;
using System.Text.RegularExpressions;

namespace RetroBatMarqueeManager.Infrastructure.Configuration
{
    public class IniConfigService : IConfigService
    {
        private readonly Dictionary<string, string> _settings;
        private readonly ILogger<IniConfigService>? _logger;
        private readonly string _iniPath;

        public IniConfigService(ILogger<IniConfigService>? logger)
        {
            _logger = logger;
            _iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            _settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            LoadConfig();
            MigrateOverlayTemplate();
        }

        private void MigrateOverlayTemplate()
        {
            try
            {
                string oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", "overlays.json");
                string newPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlays.json");

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    _logger?.LogInformation($"[Migration] Moving overlay template from {oldPath} to {newPath}");
                    File.Move(oldPath, newPath);
                    // Optional: remove config folder if empty? (Better not to touch other files)
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"[Migration] Failed to migrate overlay template: {ex.Message}");
            }
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            return _settings.TryGetValue(key, out var val) ? val : defaultValue;
        }

        /// <summary>Updates a value in memory and persists it to config.ini immediately.</summary>
        public void SetValue(string key, string value)
        {
            _settings[key] = value;
            try
            {
                if (!File.Exists(_iniPath)) return;
                var lines = File.ReadAllLines(_iniPath).ToList();
                bool found = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    var trimmed = lines[i].TrimStart();
                    if (trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith(key + " =", StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = $"{key}={value}";
                        found = true;
                        break;
                    }
                }
                if (!found) lines.Add($"{key}={value}");
                File.WriteAllLines(_iniPath, lines, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // Paths
        // Paths
        public string ConfigPath => _iniPath;
        public string RetroBatPath => GetRetroBatPath();
        public string RomsPath => GetAbsolutePath(GetValue("RomsPath", Path.Combine(RetroBatPath, "roms")));
        public string IMPath => GetAbsolutePath(GetValue("IMPath", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "imagemagick", "convert.exe")));
        // MarqueeManager is a pure WS consumer — no local media store.
        // MarqueeImagePath is optional; only used if user explicitly sets it in config.
        public string MarqueeImagePath => GetAbsolutePath(GetValue("MarqueeImagePath", ""));
        public string MarqueeImagePathDefault => GetAbsolutePath(GetValue("MarqueeImagePathDefault", ""));
        public string CachePath => string.IsNullOrEmpty(MarqueeImagePath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_cache")
            : Path.Combine(MarqueeImagePath, "_cache");

        public string DefaultImagePath
        {
            get
            {
                // 1. Explicit config key
                var explicitPath = GetValue("DefaultImagePath", "");
                if (!string.IsNullOrEmpty(explicitPath)) return GetAbsolutePath(explicitPath);

                // 2. Search in plugin root directory (no sub-folder created)
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var extensions = new[] { ".mp4", ".gif", ".png", ".jpg", ".jpeg" };
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(baseDir, "default" + ext);
                    if (File.Exists(path)) return path;
                }

                // 3. Search in medias sub-folder if explicitly configured
                if (!string.IsNullOrEmpty(MarqueeImagePath) && Directory.Exists(MarqueeImagePath))
                {
                    foreach (var ext in extensions)
                    {
                        var path = Path.Combine(MarqueeImagePath, "default" + ext);
                        if (File.Exists(path)) return path;
                    }
                }

                return ""; // No default image — show black screen until WS event arrives
            }
        }

        public string DefaultDmdPath
        {
            get
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var extensions = new[] { ".mp4", ".gif", ".png", ".jpg", ".jpeg" };
                foreach (var ext in extensions)
                {
                    var path = Path.Combine(baseDir, "default-dmd" + ext);
                    if (File.Exists(path)) return path;
                }
                if (!string.IsNullOrEmpty(MarqueeImagePath) && Directory.Exists(MarqueeImagePath))
                {
                    foreach (var ext in extensions)
                    {
                        var path = Path.Combine(MarqueeImagePath, "default-dmd" + ext);
                        if (File.Exists(path)) return path;
                    }
                }
                return "";
            }
        }

        public string DefaultFanartPath => GetAbsolutePath(GetValue("DefaultFanartPath", ""));
        
        // PCSX2 Paths
        public string Pcsx2LogPath => Path.Combine(RetroBatPath, "emulators", "pcsx2", "logs", "emulog.txt");
        public string Pcsx2BadgeCachePath => Path.Combine(RetroBatPath, "emulators", "pcsx2", "cache", "achievement_images");

        // DuckStation Paths
        public string DuckStationLogPath => Path.Combine(RetroBatPath, "emulators", "duckstation", "duckstation.log");
        public string DuckStationSettingsPath => Path.Combine(RetroBatPath, "emulators", "duckstation", "settings.ini");

        public string[] AcceptedFormats => GetValue("AcceptedFormats", "mp4,gif,jpg,png,svg").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        
        // Templates
        public string MarqueeFilePath => GetValue("MarqueeFilePath", "{system_name}\\{game_name}");
        public string MarqueeFilePathDefault => GetValue("MarqueeFilePathDefault", "{system_name}\\images\\{game_name}-marquee");
        public string SystemFilePath => GetValue("SystemFilePath", "{system_name}");
        public string CollectionFilePath => GetValue("CollectionFilePath", "auto-{collection_name}");
        
        // Chemins thèmes
        public string SystemMarqueePath => GetAbsolutePath(GetValue("SystemMarqueePath", Path.Combine(RetroBatPath, "emulationstation", ".emulationstation", "themes", "es-theme-carbon", "art", "logos")));
        public string SystemCustomMarqueePath => GetAbsolutePath(GetValue("SystemCustomMarqueePath", "")); // Empty by default
        public string SystemCustomDMDPath => GetAbsolutePath(GetValue("SystemCustomDMDPath", "")); // Empty by default
        public string CollectionMarqueePath => GetAbsolutePath(GetValue("CollectionMarqueePath", Path.Combine(RetroBatPath, "emulationstation", ".emulationstation", "themes", "es-theme-carbon", "art", "logos")));        
        public string GameCustomMarqueePath => GetAbsolutePath(GetValue("GameCustomMarqueePath", "")); // Empty by default
        public string GameStartMediaPath => GetAbsolutePath(GetValue("GameStartMediaPath", "")); // Empty by default
        
        // Composition Settings
        public string ComposeMedia => GetValue("ComposeMedia", "fanart"); // fanart or image
        public string MarqueeLayout => GetValue("MarqueeLayout", "gradient-standard"); // standard, gradient-left, gradient-right, gradient-standard

        // DMD Settings
        public bool DmdEnabled => GetValue("DmdEnabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public string DmdModel => GetValue("DmdModel", "virtual");
        public string DmdExePath => GetAbsolutePath(GetValue("DmdExePath", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "dmd", "dmdext.exe")));
        public string DmdMediaPath => GetAbsolutePath(GetValue("DmdMediaPath", ""));
        public string DmdGameStartMediaPath => GetAbsolutePath(GetValue("DmdGameStartMediaPath", "")); // Empty by default
        public string DmdFormat => GetValue("DmdFormat", "rgb24"); // Default format
        public bool DmdCompose => GetValue("DmdCompose", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public int DmdWidth => int.TryParse(GetValue("DmdWidth", "128"), out var w) ? w : 128;
        public int DmdHeight => int.TryParse(GetValue("DmdHeight", "32"), out var h) ? h : 32;
        public double DmdDotSize => double.TryParse(GetValue("DmdDotSize", "8"), out var d) ? d : 8.0;

        public string DmdArguments 
        {
            get
            {
                var args = GetValue("DmdArguments", "");
                
                if (args.Contains("--virtual-position"))
                {
                   var w = DmdWidth;
                   var h = DmdHeight;
                   
                   // Match --virtual-position X Y (and optional W H)
                   // We want to force W and H to be our configured values
                   
                   // Regex explanation:
                   // --virtual-position\s+
                   // (\d+)\s+(\d+)   <- Capture X and Y
                   // (?:\s+\d+\s+\d+)? <- Optional W and H (non-capturing group)
                   
                   var pattern = @"(--virtual-position\s+(\d+)\s+(\d+))(?:\s+\d+\s+\d+)?";
                   
                   if (Regex.IsMatch(args, pattern))
                   {
                       args = Regex.Replace(args, pattern, $"$1 {w} {h}");
                   }
                }

                // Force removal of --no-virtual-resize if present (fix for dotted/pixelated rendering)
                if (args.Contains("--no-virtual-resize"))
                {
                    args = args.Replace("--no-virtual-resize", "").Trim();
                    // Clean up any double spaces created by removal
                    while (args.Contains("  ")) args = args.Replace("  ", " ");
                }

                return args;
            }
        }

        // ScreenScraper Settings
        // EN: Auto-scraping from ScreenScraper API
        // FR: Scraping automatique depuis l'API ScreenScraper
        public bool MarqueeAutoScraping => GetValue("MarqueeAutoScraping", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public bool MarqueeGlobalScraping => GetValue("MarqueeGlobalScraping", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public string MPVScrapMediaType => GetValue("MPVScrapMediaType", "");
        public string DMDScrapMediaType => GetValue("DMDScrapMediaType", "");
        public string ScreenScraperUser => GetValue("ScreenScraperUser", "");
        public string ScreenScraperPass => GetValue("ScreenScraperPass", "");
        // Default to generic or empty logic if user doesn't provide one. "Maca" was invalid.
        public string ScreenScraperDevId => GetValue("ScreenScraperDevId", "");
        public string ScreenScraperDevPassword => GetValue("ScreenScraperDevPassword", "");
        
        // RetroAchievements Settings
        // EN: Web API Key from https://retroachievements.org/settings / FR: Clé Web API depuis https://retroachievements.org/settings
        public bool MpvRetroAchievementsNotifications => GetValue("MpvRetroAchievementsNotifications", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        public bool DmdRetroAchievementsNotifications => GetValue("DmdRetroAchievementsNotifications", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        public string? RetroAchievementsWebApiKey => GetValue("RetroAchievementsWebApiKey", "");
        public string MarqueeRetroAchievementsOverlays => GetValue("MarqueeRetroAchievementsOverlays", "score,badges,count,items,challenge");
        public string MpvRetroAchievementsOverlays 
        {
            get
            {
                var val = GetValue("MpvRetroAchievementsOverlays", "");
                return string.IsNullOrWhiteSpace(val) ? MarqueeRetroAchievementsOverlays : val;
            }
        }
        public string DmdRetroAchievementsOverlays 
        {
            get
            {
                var val = GetValue("DmdRetroAchievementsOverlays", "");
                return string.IsNullOrWhiteSpace(val) ? MarqueeRetroAchievementsOverlays : val;
            }
        }
        public string MarqueeRetroAchievementsDisplayTarget => GetValue("MarqueeRetroAchievementsDisplayTarget", "both").ToLowerInvariant();
        public string RAFontFamily 
        {
            get
            {
                var val = GetValue("RAFontFamily", "");
                // EN: If user specified something indicating a preference (not default Arial), respect it?
                // FR: Si l'utilisateur a spécifié quelque chose (pas Arial par défaut), on respecte ?
                // The user requested: "if not set, use the first available in retroachievements\fonts"
                // So if it IS "Arial", we check if we have a better custom font available.
                
                if (!string.IsNullOrEmpty(val) && !val.Equals("Arial", StringComparison.OrdinalIgnoreCase))
                {
                    return val;
                }

                try 
                {
                    // Search in retroachievements/fonts
                    // Path relative to AppDomain or MarqueeImagePath? User said "retroachievements\fonts"
                    // We assume AppDomain root structure based on "native root" comment
                    var fontDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "retroachievements", "fonts");
                    if (Directory.Exists(fontDir))
                    {
                        var fontFile = Directory.GetFiles(fontDir, "*.ttf").FirstOrDefault() ?? 
                                       Directory.GetFiles(fontDir, "*.otf").FirstOrDefault();
                        
                        if (!string.IsNullOrEmpty(fontFile))
                        {
                            return fontFile; // Return absolute path
                        }
                    }
                }
                catch {}

                return "Arial"; // Fallback
            }
        }
        public string OverlayTemplatePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlays.json");
        public string ScreenScraperCachePath => Path.Combine(MarqueeImagePath, "screenscraper");
        public int ScreenScraperThreads => int.TryParse(GetValue("ScreenScraperThreads", "1"), out var ssThreads) ? Math.Max(1, ssThreads) : 1;
        
        // EN: Queue management settings / FR: Paramètres de gestion de la file d'attente
        public int ScreenScraperQueueLimit => int.TryParse(GetValue("ScreenScraperQueueLimit", "5"), out var limit) ? Math.Max(1, limit) : 5;
        public int ScreenScraperQueueKeep => int.TryParse(GetValue("ScreenScraperQueueKeep", "3"), out var keep) ? Math.Max(1, keep) : 3;

        // Scraper Priority Manager
        public List<string> ScraperPriorities 
        {
            get
            {
                var val = GetValue("PrioritySource", "ScreenScraper");
                return val.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }
        }

        // ArcadeItalia Settings
        public string ArcadeItaliaUrl => GetValue("ArcadeItaliaUrl", "http://adb.arcadeitalia.net");
        public string ArcadeItaliaMediaType => GetValue("ArcadeItaliaMediaType", "marquee"); // marquee, snapshot, title, cabinet

          // Logic Flags
        // MarqueeAutoGeneration moved to alias below
        // ... (Other properties remain the same) ...

        private string GetRetroBatPath()
        {
            // 1. Check Config
            if (_settings.TryGetValue("RetroBatPath", out var val) && !string.IsNullOrWhiteSpace(val))
            {
                return val;
            }

            // 2. Check Relative Path (Plugin Mode) - PRIORITY OVER REGISTRY
            // If we are in "Available\plugins\RetroBatMarqueeManager" or "plugins\RetroBatMarqueeManager"
            // We expect RetroBat root to be 2 levels up.
            var baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parent = Directory.GetParent(baseDir);
            if (parent != null && parent.Name.Equals("plugins", StringComparison.OrdinalIgnoreCase))
            {
                var grandParent = parent.Parent;
                if (grandParent != null && Directory.Exists(Path.Combine(grandParent.FullName, "emulationstation")))
                {
                    // Found valid RetroBat root structure relative to plugin
                    _logger?.LogInformation($"Detected RetroBat path from relative plugin location: {grandParent.FullName}");
                    return grandParent.FullName;
                }
            }

            // 2. Check Registry
            if (OperatingSystem.IsWindows())
            {
                try 
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\RetroBat");
                    if (key != null)
                    {
                        var path = key.GetValue("LatestKnownInstallPath") as string;
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning($"Registry check failed: {ex.Message}");
                }
            }

            // 3. Fallback to default C:\RetroBat
            return @"C:\RetroBat";
        }

        private string GetAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (path.StartsWith("\"") && path.EndsWith("\"")) path = path.Trim('"');
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (Path.IsPathRooted(path)) return path;
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private string GetValue(string key, string defaultValue)
        {
            if (_settings.TryGetValue(key, out var val))
            {
                // If key exists, return the actual value (even if empty), stripping quotes
                var result = val.Trim('"');
                if (key.Contains("ScrapMediaType"))
                {
                    _logger?.LogInformation($"[CONFIG DEBUG] GetValue('{key}') -> found in file: '{val}' -> after trim: '{result}'");
                }
                return result;
            }
            
            // Key doesn't exist, return default
            if (key.Contains("ScrapMediaType"))
            {
                _logger?.LogInformation($"[CONFIG DEBUG] GetValue('{key}') -> NOT found, returning default: '{defaultValue}'");
            }
            return defaultValue;
        }

        private string GetRawValue(string key, string defaultValue)
        {
             if (_settings.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val)) return val;
             return defaultValue;
        }
        // Logic Flags
        // MarqueeAutoGeneration moved to alias below
        public bool MarqueeAutoConvert => GetValue("MarqueeAutoConvert", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        public bool MarqueeVideoGeneration => GetValue("MarqueeVideoGeneration", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

        public string GenerateMarqueeVideoFolder => GetValue("GenerateMarqueeVideoFolder", "generated_videos");
        public string FfmpegHwEncoding => GetValue("FfmpegHwEncoding", ""); // Default: empty (software/x264)
        public bool MarqueeCompose => GetValue("MarqueeCompose", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        // Alias for backward compatibility if code used it, or mapped to same key
        public bool MarqueeAutoGeneration => MarqueeCompose;
        
        // Logging
        public bool LogToFile => GetValue("LogToFile", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
        
        // UI
        public bool MinimizeToTray => GetValue("MinimizeToTray", "true").Equals("true", StringComparison.OrdinalIgnoreCase);
        public string LogFilePath => GetAbsolutePath(GetValue("LogFilePath", "logs\\debug.log")); 
        
        // Dimensions
        public int MarqueeWidth => int.TryParse(GetValue("MarqueeWidth", "1920"), out var w) ? w : 1920;
        public int ScreenNumber => int.TryParse(GetValue("ScreenNumber", "2"), out var s) ? s : 2;
        
        public int MarqueeScreen => int.TryParse(GetValue("MarqueeScreen", GetValue("ScreenNumber", "2")), out var s) ? s : 2;
        public int TopperScreen => int.TryParse(GetValue("TopperScreen", "-1"), out var s) ? s : -1;
        public int DmdScreen => int.TryParse(GetValue("DmdScreen", "-1"), out var s) ? s : -1;
        public int IcCardScreen => int.TryParse(GetValue("IcCardScreen", "-1"), out var s) ? s : -1;
        public int LcdScreen => int.TryParse(GetValue("LcdScreen", "-1"), out var s) ? s : -1;

        public bool IsMpvEnabled
        {
            get
            {
                var val = GetValue("ScreenNumber", "2");
                if (val.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
                return true;
            }
        }
        
        public string MarqueeComposeVal => GetValue("MarqueeCompose", "false");
        public int MarqueeHeight => int.TryParse(GetValue("MarqueeHeight", "360"), out var h) ? h : 360;
        public int MarqueeBorder => int.TryParse(GetValue("MarqueeBorder", "0"), out var b) ? b : 0;

        // Robust Color Handling (The Fix)
        public string MarqueeBackgroundColor
        {
            get
            {
                // Priority 1: Read explicit hex code from MarqueeBackgroundCodeColor
                var hexCode = GetValue("MarqueeBackgroundCodeColor", "");
                if (!string.IsNullOrEmpty(hexCode) && hexCode.StartsWith("#"))
                {
                    return hexCode; // Direct hex code like #444444
                }
                
                // Priority 2: Read named color from MarqueeBackgroundColor and convert
                var val = GetValue("MarqueeBackgroundColor", "Black");
                
                // Convert common color names to hex
                return val.ToLowerInvariant() switch
                {
                    "black" => "#000000",
                    "white" => "#FFFFFF",
                    "red" => "#FF0000",
                    "green" => "#00FF00",
                    "blue" => "#0000FF",
                    "yellow" => "#FFFF00",
                    "cyan" => "#00FFFF",
                    "magenta" => "#FF00FF",
                    "gray" or "grey" => "#808080",
                    "none" or "transparent" => "#00000000",  // Transparent
                    _ => val.StartsWith("#") ? val : "#000000"  // If hex, use it; else default to black
                };
            }
        }

        // Command Templates
        public string IMConvertCommand => GetValue("IMConvertCommand", "");
        public string IMConvertCommandSVG => GetValue("IMConvertCommandSVG", "");
        public string IMConvertCommandMarqueeGen => GetValue("IMConvertCommandMarqueeGen", "");
        public string IMConvertCommandMarqueeGenLogo => GetValue("IMConvertCommandMarqueeGenLogo", "");


        public string MpvCustomCommand => GetRawValue("MPVCustomCommand", "");

        // Collections
        public Dictionary<string, string> CollectionCorrelation
        {
            get
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                // Updated defaults from user
                var defaultCorrelations = "all:allgames,favorites:favorites,windowsgames:windows,2players:auto-at2players,4players:auto-at4players,zsegastv:segastv,ztaito:taito,zgaelco:gaelco,recent:auto-lastplayed,vertical:auto-verticalarcade,zmodel2:model2,zmodel3:model3,zcps1:cps1,zsega:sega";
                var raw = GetValue("CollectionCorrelation", defaultCorrelations);
                foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split(':');
                    if (pair.Length == 2)
                    {
                        dict[pair[0].Trim()] = pair[1].Trim();
                    }
                }
                return dict;
        }
        }

        public Dictionary<string, string> SystemAliases
        {
            get
            {
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var defaultAliases = "gamecube:gc,gw:gameandwatch,segamegadrive:megadrive,atarijaguargroup:atarijaguar,dos:pc"; 
                var raw = GetValue("SystemAliases", defaultAliases);
                foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pair = part.Split(':');
                    if (pair.Length == 2)
                    {
                        dict[pair[0].Trim()] = pair[1].Trim();
                    }
                }
                return dict;
            }
        }



        private void LoadConfig()
        {
            if (!File.Exists(_iniPath))
            {
                CreateDefaultConfig();
            }

            try
            {
                var lines = File.ReadAllLines(_iniPath);
                foreach (var line in lines)
                {
                    var trim = line.Trim();
                    if (string.IsNullOrEmpty(trim) || trim.StartsWith(";") || trim.StartsWith("[")) continue;

                    var parts = trim.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var val = parts[1].Trim();
                        // Store value RAW (do not strip quotes here)
                        _settings[key] = val;
                    }
                }
                
                // Ensure new keys exist in file
                UpdateConfigWithMissingKeys();
                
                // Import from ES Settings if needed
                ImportFromEsSettings();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Config load failed: {ex.Message}");
            }
        }
        
        private void ImportFromEsSettings()
        {
             // Always try to sync from es_settings.cfg if valid credentials are found there
             try
             {
                 var retroBatPath = RetroBatPath;
                 var esSettingsPath = Path.Combine(retroBatPath, "emulationstation", ".emulationstation", "es_settings.cfg");
                 
                 if (!File.Exists(esSettingsPath)) return;
                 
                 using var stream = new FileStream(esSettingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                 var doc = System.Xml.Linq.XDocument.Load(stream);
                 var root = doc.Root;
                 if (root == null) return;
                 
                 string? user = null;
                 string? pass = null;

                 foreach (var element in root.Elements())
                 {
                     var name = element.Attribute("name")?.Value;
                     var value = element.Attribute("value")?.Value;
                     
                     if (name == "ScreenScraperUser") user = value;
                     if (name == "ScreenScraperPass") pass = value;
                 }
                 
                 _logger?.LogInformation($"[ES Settings Sync] Found in {esSettingsPath}: User='{user}', Pass={(string.IsNullOrEmpty(pass) ? "[Empty]" : "[Set]")}");

                 // Sync logic: Force config.ini to match es_settings.cfg
                 // If ES has it (or has it empty), we apply it.
                 // This ensures full synchronization (including clearing credentials).
                 
                 bool updated = false;
                 
                 var currentUser = GetSetting("ScreenScraperUser");
                 var currentPass = GetSetting("ScreenScraperPass");

                 // Normalize nulls to empty strings for comparison
                 user ??= "";
                 pass ??= "";
                 currentUser ??= "";
                 currentPass ??= "";

                 if (user != currentUser)
                 {
                     _settings["ScreenScraperUser"] = user;
                     updated = true;
                     _logger?.LogInformation($"Synced ScreenScraperUser from ES (Old: '{currentUser}' -> New: '{user}')");
                 }
                 
                 if (pass != currentPass)
                 {
                     _settings["ScreenScraperPass"] = pass;
                     updated = true;
                     _logger?.LogInformation("Synced ScreenScraperPass from ES (Password updated/cleared)");
                 }
                 
                 if (updated)
                 {
                     RewriteConfig();
                 }
             }
             catch (Exception ex)
             {
                 _logger?.LogWarning($"Failed to import from ES settings: {ex.Message}");
             }
        }
        
        /// <summary>
        /// EN: Add missing configuration keys without overwriting existing values
        /// FR: Ajouter les clés de configuration manquantes sans écraser les valeurs existantes
        /// </summary>
        private void UpdateConfigWithMissingKeys()
        {
            // EN: Define all expected keys with their default values / FR: Définir toutes les clés attendues avec leurs valeurs par défaut
            var expectedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // Core Settings
                { "IMPath", @"tools\imagemagick\convert.exe" },
                { "MPVPath", @"tools\mpv\mpv.exe" },
                { "MarqueeWidth", "1920" },
                { "MarqueeHeight", "360" },
                { "MarqueeBackgroundColor", "Black" },
                { "MarqueeBackgroundCodeColor", "#000000" },
                { "MarqueeCompose", "false" },
                { "ComposeMedia", "fanart" },
                { "MarqueeLayout", "gradient-standard" },
                { "MarqueeAutoConvert", "false" },
                { "MarqueeVideoGeneration", "false" },
                { "MarqueeAutoScraping", "false" },
                { "MPVScrapMediaType", "" },
                { "DMDScrapMediaType", "" },
                { "ScreenScraperUser", "" },
                { "ScreenScraperPass", "" },
                { "ScreenScraperDevId", "" },
                { "ScreenScraperDevPassword", "" },
                { "ScreenScraperThreads", "1" },
                { "ScreenScraperQueueLimit", "5" },
                { "ScreenScraperQueueKeep", "3" },
                { "MarqueeGlobalScraping", "false" },
                { "AcceptedFormats", "png,jpg,jpeg,svg,mp4,gif" },
                { "MinimizeToTray", "true" },
                { "AutoStart", "false" },
                { "LogToFile", "false" },
                { "LogFilePath", @"logs\debug.log" },
                { "SystemCustomMarqueePath", @"medias\customs\systems" },
                { "GameCustomMarqueePath", @"medias\customs\games" },
                { "GameStartMediaPath", @"medias\customs\games-start" },
                
                // RetroAchievements (NEW)
                { "RetroAchievementsWebApiKey", "" },
                { "MarqueeRetroAchievementsDisplayTarget", "both" },
                { "MarqueeRetroAchievementsOverlays", "score,badges,count,items,challenge" },
                { "MpvRetroAchievementsOverlays", "" },
                { "DmdRetroAchievementsOverlays", "" },
                { "RAFontFamily", "" },

                
                // Collections
                { "CollectionCorrelation", "all:allgames,favorites:favorites,windowsgames:windows,2players:auto-at2players,4players:auto-at4players,zsegastv:segastv,ztaito:taito,zgaelco:gaelco,recent:auto-lastplayed,vertical:auto-verticalarcade,zmodel2:model2,zmodel3:model3,zcps1:cps1,zsega:sega" },
                { "SystemAliases", "gamecube:gc,gw:gameandwatch,segamegadrive:megadrive,atarijaguargroup:atarijaguar,dos:pc" },
                
                // DMD Settings
                { "DmdEnabled", "false" },
                { "DmdModel", "virtualdmd" },
                { "DmdExePath", @"tools\dmd\dmdext.exe" },
                { "DmdMediaPath", @"medias\customs\games" },
                { "SystemCustomDMDPath", @"medias\customs\systems" },
                { "DmdGameStartMediaPath", @"medias\customs\games-start" },
                { "DmdCompose", "true" },
                { "DmdFormat", "rgb24" },
                { "DmdWidth", "128" },
                { "DmdHeight", "32" },
                { "DmdDotSize", "8" },

                // ScrapersSource (NEW)
                { "PrioritySource", "" },
                
                // ArcadeItalia (NEW)
                { "ArcadeItaliaUrl", "http://adb.arcadeitalia.net" },
                { "ArcadeItaliaMediaType", "marquee" },
                
                // IPC  
                { "ScreenNumber", "1" },
                { "MarqueeScreen", "2" },
                { "TopperScreen", "-1" },
                { "IcCardScreen", "-1" },
                { "DmdScreen", "-1" },
                { "LcdScreen", "-1" },
                { "HwDecoding", "no" },
                
                // Pinball
                { "pinballfx", "True" },
                { "pinballfx2", "dmdext.exe mirror --source=pinballfx2 -d {DmdModel} --quit-when-done" },
                { "pinballfx3", "dmdext.exe mirror --source=pinballfx3 -d {DmdModel} --colorize --quit-when-done" },
                { "fpinball", "dmdext.exe mirror --source=futurepinball -d {DmdModel} --colorize --quit-when-done" },
                { "zaccariapinball", "dmdext.exe mirror --source=screen --position !POSITION! -d {DmdModel};False;True" },
                { "custom1", "" }
            };
            
            bool needsUpdate = false;
            
            
            // EN: Check each expected key and add if missing / FR: Vérifier chaque clé attendue et ajouter si manquante
            foreach (var kvp in expectedKeys)
            {
                if (!_settings.ContainsKey(kvp.Key))
                {
                    _settings[kvp.Key] = kvp.Value;
                    needsUpdate = true;
                    _logger?.LogInformation($"[Config Migration] Added missing key: {kvp.Key}");
                }
            }
            
            // EN: Write back to config.ini if any keys were added / FR: Réécrire config.ini si des clés ont été ajoutées
            if (needsUpdate)
            {
                _logger?.LogInformation("[Config Migration] Updating config.ini with missing keys...");
                RewriteConfig();
            }
        }

        private void CreateDefaultConfig()
        {
            // Just populate _settings with defaults if empty, then write
            if (_settings.Count == 0)
            {
                // Core
                _settings["IMPath"] = @"tools\imagemagick\convert.exe";
                _settings["MPVPath"] = @"tools\mpv\mpv.exe";
                _settings["MarqueeWidth"] = "1920";
                _settings["MarqueeHeight"] = "360";
                _settings["MarqueeBackgroundColor"] = "Black";
                _settings["MarqueeBackgroundCodeColor"] = "#000000";
                _settings["MarqueeCompose"] = "false";
                _settings["ComposeMedia"] = "fanart";
                _settings["MarqueeLayout"] = "gradient-standard";
                _settings["MarqueeAutoConvert"] = "false";
                _settings["MarqueeVideoGeneration"] = "false"; // Added
                _settings["GenerateMarqueeVideoFolder"] = "generated_videos"; // Added
                _settings["FfmpegHwEncoding"] = ""; // Added
                _settings["MarqueeAutoScraping"] = "false";
                _settings["MPVScrapMediaType"] = "";
                _settings["DMDScrapMediaType"] = "";
                _settings["ScreenScraperUser"] = "";
                _settings["ScreenScraperPass"] = "";
                _settings["ScreenScraperDevId"] = "";
                _settings["ScreenScraperDevPassword"] = "";
                _settings["MarqueeGlobalScraping"] = "false";
                _settings["ScreenScraperThreads"] = "1";
                _settings["AcceptedFormats"] = "png,jpg,jpeg,svg,mp4,gif";
                _settings["MinimizeToTray"] = "true";
                _settings["AutoStart"] = "false";
                _settings["LogToFile"] = "false";
                _settings["LogFilePath"] = @"logs\debug.log";
                _settings["SystemCustomMarqueePath"] = @"medias\customs\systems";
                _settings["GameCustomMarqueePath"] = @"medias\customs\games";
                _settings["GameStartMediaPath"] = @"medias\customs\games-start";
                _settings["CollectionCorrelation"] = "all:allgames,favorites:favorites,windowsgames:windows,2players:auto-at2players,4players:auto-at4players,zsegastv:segastv,ztaito:taito,zgaelco:gaelco,recent:auto-lastplayed,vertical:auto-verticalarcade,zmodel2:model2,zmodel3:model3,zcps1:cps1,zsega:sega";
                _settings["SystemAliases"] = "gamecube:gc,gw:gameandwatch,segamegadrive:megadrive,atarijaguargroup:atarijaguar,dos:pc";

                // ScrapersSource
                _settings["PrioritySource"] = "";

                // ArcadeItalia
                _settings["ArcadeItaliaUrl"] = "http://adb.arcadeitalia.net";
                _settings["ArcadeItaliaMediaType"] = "marquee";

                // RetroAchievements
                _settings["RetroAchievementsWebApiKey"] = "";
                _settings["MarqueeRetroAchievementsDisplayTarget"] = "both";
                _settings["MarqueeRetroAchievementsOverlays"] = "score,badges,count,items,challenge";
                _settings["MpvRetroAchievementsOverlays"] = "";
                _settings["DmdRetroAchievementsOverlays"] = "";
                _settings["RAFontFamily"] = "";


                // DMD
                _settings["DmdEnabled"] = "false";
                _settings["DmdModel"] = "virtualdmd";
                _settings["DmdExePath"] = @"tools\dmd\dmdext.exe";
                _settings["DmdMediaPath"] = @"medias\customs\games";
                _settings["SystemCustomDMDPath"] = @"medias\customs\systems";
                _settings["DmdGameStartMediaPath"] = @"medias\customs\games-start";
                _settings["DmdCompose"] = "true";
                _settings["DmdFormat"] = "rgb24";
                _settings["DmdWidth"] = "128";
                _settings["DmdHeight"] = "32";
                // DmdArguments removed from here so it falls back to WriteKey's "commented default" logic
                
                // Screens (was ScreenMPV)
                
                // Pinball
                _settings["pinballfx"] = "True";
                _settings["pinballfx2"] = "dmdext.exe mirror --source=pinballfx2 -d {DmdModel} --quit-when-done";
                _settings["pinballfx3"] = "dmdext.exe mirror --source=pinballfx3 -d {DmdModel} --colorize --quit-when-done";
                _settings["fpinball"] = "dmdext.exe mirror --source=futurepinball -d {DmdModel} --colorize --quit-when-done";
                _settings["zaccariapinball"] = "dmdext.exe mirror --source=screen --position !POSITION! -d {DmdModel};False;True";
                _settings["custom1"] = "";
            }

            RewriteConfig();
        }

        private void RewriteConfig()
        {
            try
            {
                var sb = new System.Text.StringBuilder();

                // --- [Settings] ---
                sb.AppendLine("[Settings]");
                sb.AppendLine("; Core Paths (Uncomment to override Registry detection)");
                WriteKey(sb, "RetroBatPath", @"C:\RetroBat", true);
                WriteKey(sb, "RomsPath", @"C:\RetroBat\roms", true);
                WriteKey(sb, "IMPath", @"tools\imagemagick\convert.exe");

                sb.AppendLine("; Marquee Settings");
                WriteKey(sb, "MarqueeBackgroundColor", "Black");
                WriteKey(sb, "MarqueeBackgroundCodeColor", "#000000");
                WriteKey(sb, "MarqueeCompose", "false");
                
                sb.AppendLine("; Options: fanart | image (boxart/screenshot)");
                WriteKey(sb, "ComposeMedia", "fanart");
                
                sb.AppendLine("; Options: standard | gradient-left | gradient-right | gradient-standard");
                WriteKey(sb, "MarqueeLayout", "gradient-standard");
                WriteKey(sb, "MarqueeAutoConvert", "false");
                WriteKey(sb, "MarqueeVideoGeneration", "false"); // Added

                WriteKey(sb, "GenerateMarqueeVideoFolder", "generated_videos"); // Added
                sb.AppendLine("; FFmpeg Hardware Encoding (empty=software, h264_nvenc, h264_amf, h264_qsv)");
                WriteKey(sb, "FfmpegHwEncoding", ""); // Added
                
                sb.AppendLine("; RetroAchievements");
                WriteKey(sb, "MarqueeRetroAchievements", "false"); // Ensure key exists
                
                sb.AppendLine("; Options: both | mpv | dmd");
                WriteKey(sb, "MarqueeRetroAchievementsDisplayTarget", "both");

                sb.AppendLine("; Options: score,badges,count,items,challenge");
                WriteKey(sb, "MarqueeRetroAchievementsOverlays", "score,badges,count,items,challenge");
                sb.AppendLine("; Specific overlays for MPV/DMD (if empty, uses MarqueeRetroAchievementsOverlays)");
                WriteKey(sb, "MpvRetroAchievementsOverlays", "");
                WriteKey(sb, "DmdRetroAchievementsOverlays", "");
                sb.AppendLine("; Display Achievements/Challenges popups on MPV or DMD");
                WriteKey(sb, "MpvRetroAchievementsNotifications", "true");
                WriteKey(sb, "DmdRetroAchievementsNotifications", "true");
                sb.AppendLine("; Generate your Web API Key at: https://retroachievements.org/settings");
            WriteKey(sb, "RetroAchievementsWebApiKey", "");
            WriteKey(sb, "RAFontFamily", "");

                
                WriteKey(sb, "MinimizeToTray", "true");
                
                sb.AppendLine("; AutoStart mode: false (disabled) | windows (Windows Startup) | retrobat (RetroBat start script)");
                WriteKey(sb, "AutoStart", "false");
                WriteKey(sb, "AcceptedFormats", "png,jpg,jpeg,svg,mp4,gif");
                
                sb.AppendLine("; Logging");
                WriteKey(sb, "LogToFile", "false");
                WriteKey(sb, "LogFilePath", @"logs\debug.log");
                
                sb.AppendLine("; Advanced");
                
                WriteKey(sb, "CollectionCorrelation", "all:allgames,favorites:favorites,windowsgames:windows,2players:auto-at2players,4players:auto-at4players,zsegastv:segastv,ztaito:taito,zgaelco:gaelco,recent:auto-lastplayed,vertical:auto-verticalarcade,zmodel2:model2,zmodel3:model3,zcps1:cps1,zsega:sega");
                WriteKey(sb, "SystemAliases", "gamecube:gc,gw:gameandwatch,segamegadrive:megadrive,atarijaguargroup:atarijaguar,dos:pc");

                // --- [ScrapersSource] ---
                sb.AppendLine();
                sb.AppendLine("[ScrapersSource]");
                sb.AppendLine("; Master switch for scraping (true/false)");
                WriteKey(sb, "MarqueeAutoScraping", "false");
                sb.AppendLine("; Order of usage for scrapers (ScreenScraper, arcadeitalia)");
                WriteKey(sb, "PrioritySource", "");

                // --- [arcadeitalia] ---
                sb.AppendLine();
                sb.AppendLine("[arcadeitalia]");
                WriteKey(sb, "ArcadeItaliaUrl", "http://adb.arcadeitalia.net");
                sb.AppendLine("; Options: marquee, snapshot, title, cabinet");
                WriteKey(sb, "ArcadeItaliaMediaType", "marquee");

                // --- [ScreenScraper] ---
                sb.AppendLine();
                sb.AppendLine("[ScreenScraper]");
                WriteKey(sb, "MarqueeGlobalScraping", "false");
                sb.AppendLine("; ScreenScraper Media Types (Keys are case-sensitive!)");
                sb.AppendLine("; Common Marquees: screenmarquee, screenmarqueesmall, wheel, wheel-carbon, wheel-steel, steamgrid");
                sb.AppendLine("; Others: box-2D, box-3D, sstitle, fanart, video");
                sb.AppendLine("; Default: empty (MPV disabled), screenmarqueesmall (DMD)");
                WriteKey(sb, "MPVScrapMediaType", "");
                WriteKey(sb, "DMDScrapMediaType", "");
                WriteKey(sb, "ScreenScraperUser", "");
                WriteKey(sb, "ScreenScraperPass", "");
                sb.AppendLine("; Dev Credentials (Required for API access if generic keys are blocked/invalid)");
                WriteKey(sb, "ScreenScraperDevId", "");
                WriteKey(sb, "ScreenScraperDevPassword", "");
                sb.AppendLine("; Number of concurrent scraping threads (Premium accounts)");
                WriteKey(sb, "ScreenScraperThreads", "1");
                sb.AppendLine("; EN: Max games in download queue (0=unlimited) / FR: Nombre max de jeux en file d'attente (0=illimité)");
                WriteKey(sb, "ScreenScraperQueueLimit", "5");
                sb.AppendLine("; EN: Games to keep when queue is full / FR: Jeux à conserver quand la file est pleine");
                WriteKey(sb, "ScreenScraperQueueKeep", "3");

                // --- [DMD] ---
                sb.AppendLine();
                sb.AppendLine("[DMD]");
                WriteKey(sb, "DmdEnabled", "false");
                sb.AppendLine("; Options: virtual, virtualdmd, pin2dmd, zedmd, zedmdhd, zedmdwifi, zedmdhdwifi, pindmdv1, pindmdv2, pindmdv3, pixelcade, alphanumeric, pinup, video, rawoutput, networkstream, browserstream, vpdbstream");
                WriteKey(sb, "DmdModel", "virtualdmd");
                WriteKey(sb, "DmdExePath", @"tools\dmd\dmdext.exe");
                WriteKey(sb, "DmdMediaPath", "");
                WriteKey(sb, "SystemCustomDMDPath", "");
                WriteKey(sb, "DmdGameStartMediaPath", "");
                WriteKey(sb, "DmdCompose", "true");
                
                sb.AppendLine("; Frame format (rgb24 [default], gray2, gray4, coloredgray2, coloredgray4, coloredgray6)");
                WriteKey(sb, "DmdFormat", "rgb24", true); // Default commented (Automatic)

                // DmdArguments forced commented by default
                // User requirement: ;DmdArguments=--virtual-position 0 0 --virtual-stay-on-top
                // We check if it exists in current settings (uncommented). If not, we write the commented default.
                if (_settings.ContainsKey("DmdArguments"))
                {
                    WriteKey(sb, "DmdArguments", "");
                }
                else
                {
                    sb.AppendLine(";DmdArguments=--virtual-position 0 0 --virtual-stay-on-top");
                }
                
                WriteKey(sb, "DmdWidth", "128");
                WriteKey(sb, "DmdHeight", "32");
                sb.AppendLine("; Virtual DMD dot size (pixel scaling factor, default: 8)");
                WriteKey(sb, "DmdDotSize", "8");

                // --- [Screens] ---
                sb.AppendLine();
                sb.AppendLine("[Screens]");
                sb.AppendLine("; Screen index (Windows display index: 0=primary, 1=first secondary, etc. ; -1 = disabled)");
                sb.AppendLine("; Comma-separated for multiple screens on the same target: MarqueeScreen=1,3");
                WriteKey(sb, "MarqueeScreen", "2");
                WriteKey(sb, "TopperScreen", "-1");
                WriteKey(sb, "IcCardScreen", "-1");
                WriteKey(sb, "DmdScreen", "-1");
                WriteKey(sb, "LcdScreen", "-1");

                // --- [Pinball] ---
                sb.AppendLine();
                sb.AppendLine("[Pinball]");
                sb.AppendLine("; Pinball System Commands");
                sb.AppendLine("; ");
                sb.AppendLine("; Special case: system=True → External app handles everything (e.g., PinballFX uses DLL)");
                sb.AppendLine("; Standard format: system=command;handleDMD;suspendMPV");
                sb.AppendLine(";   - handleDMD=True → Internal DMD stopped (external app expected)");
                sb.AppendLine(";   - suspendMPV=True → MPV marquee temporarily stopped during game");
                sb.AppendLine(";   - Both parameters are optional, default=False");
                sb.AppendLine("; ");
                WriteKey(sb, "pinballfx", "True");
                WriteKey(sb, "pinballfx2", "dmdext.exe mirror --source=pinballfx2 -d {DmdModel} --quit-when-done");
                WriteKey(sb, "pinballfx3", "dmdext.exe mirror --source=pinballfx3 -d {DmdModel} --colorize --quit-when-done");
                WriteKey(sb, "fpinball", "dmdext.exe mirror --source=futurepinball -d {DmdModel} --colorize --quit-when-done");
                WriteKey(sb, "zaccariapinball", "dmdext.exe mirror --source=screen --position !POSITION! -d {DmdModel};False;True");
                sb.AppendLine("; Define your own:");
                WriteKey(sb, "custom1", "");

                // Handle ANY OTHER KEYS (Custom Pinball entries or deprecated keys)
                var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "RetroBatPath", "RomsPath", "IMPath", "MPVPath", 
                    "MarqueeWidth", "MarqueeHeight", "MarqueeBackgroundColor", "MarqueeBackgroundCodeColor", "MarqueeCompose", "ComposeMedia", "MarqueeLayout", "MarqueeAutoConvert", "MarqueeVideoGeneration", "MarqueeRetroAchievements", "MinimizeToTray", "AutoStart", "MarqueeAutoScraping", "AcceptedFormats", "LogToFile", "LogFilePath", "SystemCustomMarqueePath", "GameCustomMarqueePath", "GameStartMediaPath", "CollectionCorrelation", "SystemAliases",
                    "DmdEnabled", "DmdModel", "DmdExePath", "DmdMediaPath", "SystemCustomDMDPath", "DmdGameStartMediaPath", "DmdCompose", "DmdFormat", "DmdArguments", "DmdWidth", "DmdHeight", "DmdDotSize",
                    "ScreenNumber", "MarqueeScreen", "TopperScreen", "IcCardScreen", "DmdScreen", "LcdScreen",
                    "pinballfx", "pinballfx2", "pinballfx3", "fpinball", "zaccariapinball", "custom1",
                    "MPVScrapMediaType", "DMDScrapMediaType", "ScreenScraperUser", "ScreenScraperPass", "ScreenScraperDevId", "ScreenScraperDevPassword", "MarqueeGlobalScraping", "ScreenScraperThreads",
                    "ScreenScraperQueueLimit", "ScreenScraperQueueKeep",
                    "RetroAchievementsWebApiKey", "GenerateMarqueeVideoFolder", "FfmpegHwEncoding", "MarqueeRetroAchievementsOverlays", "RAFontFamily", "PrioritySource", "ArcadeItaliaUrl", "ArcadeItaliaMediaType", "MarqueeRetroAchievementsDisplayTarget",
                    "MpvRetroAchievementsOverlays", "DmdRetroAchievementsOverlays",
                    "MpvRetroAchievementsNotifications", "DmdRetroAchievementsNotifications",
                    "HwDecoding"
                };

                foreach(var kvp in _settings)
                {
                    if (!handledKeys.Contains(kvp.Key))
                    {
                        // Assume it's a custom pinball entry if not known
                        sb.AppendLine($"{kvp.Key}={kvp.Value}");
                    }
                }

                File.WriteAllText(_iniPath, sb.ToString());
                _logger?.LogInformation($"Reorganized config file at {_iniPath}");
            }
            catch(Exception ex)
            {
                _logger?.LogError($"Failed to rewrite config: {ex.Message}");
            }
        }

        private void WriteKey(System.Text.StringBuilder sb, string key, string defaultValue = "", bool commentIfEmpty = false)
        {
            var exists = _settings.TryGetValue(key, out var val);
            
            // Note: If val is empty string but key exists, we typically trust user set it to empty.
            // But if we want to "default to commented", we check 'exists'.
            
            if (!exists)
            {
                 // Key missing entirely -> Use default
                 if (commentIfEmpty) sb.AppendLine($";{key}={defaultValue}");
                 else sb.AppendLine($"{key}={defaultValue}");
            }
            else
            {
                // Key exists (even if empty value)
                // However, user might want to re-comment empty values if commentIfEmpty is true?
                // Standard behavior: preserve what's loaded. 
                // But LoadConfig skipped commented lines. So if it exists in _settings, it WAS uncommented.
                sb.AppendLine($"{key}={val}");
            }
        }
    }
}
