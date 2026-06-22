using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using RetroBatMarqueeManager.Infrastructure.Api;
using RetroBatMarqueeManager.Infrastructure.Configuration;
using System.Text.RegularExpressions;

namespace RetroBatMarqueeManager.Application.Services
{
    /// <summary>
    /// EN: Event args for achievement unlocked
    /// FR: Arguments d'événement pour succès débloqué
    /// </summary>
        public class AchievementUnlockedEventArgs : EventArgs
    {
        public Achievement Achievement { get; set; } = new();
        public int GameId { get; set; }
        public string GameTitle { get; set; } = string.Empty;
        public bool IsNewUnlock { get; set; } = true;
        public bool IsHardcore { get; set; } = false;
    }

    /// <summary>
    /// EN: Event args for game started
    /// FR: Arguments d'événement pour jeu démarré
    /// </summary>
    /// <summary>
    /// EN: Event args for game started
    /// FR: Arguments d'événement pour jeu démarré
    /// </summary>
    public class GameStartedEventArgs : EventArgs
    {
        public int GameId { get; set; }
        public GameInfo? GameInfo { get; set; }
        public UserProgress? UserProgress { get; set; }
        public bool IsHardcore { get; set; }
    }

    /// <summary>
    /// EN: Service to monitor RetroArch log for RetroAchievements events
    /// FR: Service pour surveiller le log RetroArch pour les événements RetroAchievements
    /// </summary>
    public class RetroAchievementsService : IHostedService, IDisposable
    {
        private readonly IConfigService _config;
        private readonly IEsSettingsService _esSettings;
        private readonly RetroAchievementsApiClient _apiClient;
        private readonly ILogger<RetroAchievementsService> _logger;
        private ImageConversionService? _imageService; // EN: Lazy loaded for grayscale generation / FR: Chargé paresseusement pour génération grayscale

        private readonly Dictionary<string, long> _logOffsets = new();
        private readonly List<FileSystemWatcher> _watchers = new();
        private System.Threading.Timer? _pollingTimer; // EN: Fallback timer for locked files / FR: Timer de secours pour fichiers verrouillés
        private int? _currentGameId;
        private string? _currentUsername;
        private UserProgress? _currentProgress;
        private bool _isEnabled;
        private readonly object _logLock = new(); // EN: Prevent concurrent log processing / FR: Empêcher le traitement concurrent des logs
        private int? _loadingGameId; // EN: Prevent multiple loads for same game / FR: Empêcher chargements multiples pour le même jeu
        
        // Challenge State Tracking
        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ChallengeState> _activeChallenges = new();
        // EN: Store recent badge downloads to associate with challenges (Url -> LocalPath)
        // FR: Stocker téléchargements récents de badges pour associer aux défis
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Path, DateTime Time)> _recentBadgeDownloads = new();
        
        private int _richPresenceUpdateCount = 0; // EN: Skip first few boring updates / FR: Ignorer les premières mises à jour ennuyeuses
        
        // EN: Track achievements unlocked during this session to prevent over-counting from multiple logs or "already unlocked" lines
        // FR: Suivre les succès débloqués durant cette session pour éviter le sur-comptage via logs multiples ou lignes "already unlocked"
        private readonly HashSet<int> _unlockedInSession = new();
        
        // EN: Preserve session unlocks across false "Game Unload/Load" cycles (common in DuckStation/PCSX2)
        // FR: Préserver les déblocages de session à travers les faux cycles "Déchargement/Chargement" (fréquent sur DuckStation/PCSX2)
        private readonly Dictionary<int, HashSet<int>> _preservedSessionUnlocks = new();
        
        private readonly object _achievementLock = new(); // EN: Lock for thread-safe achievement processing / FR: Verrou pour traitement thread-safe des succès

        // EN: Generalized regex patterns to support multiple emulators (prefix flexible)
        // FR: Patterns regex généralisés pour supporter plusieurs émulateurs (préfixe flexible)
        // EN: Stricter pattern to avoid false positives with achievement names containing quotes (e.g. "weren't")
        // FR: Motif plus strict pour éviter les faux positifs avec les noms de succès contenant des guillemets
        private static readonly Regex UserLoginPattern = new(@"(?:RCHEEVOS\]|Achievements:|I/Achievements:)\s*(?:Attempting token login with user '(.+?)'|(.+?)\s+logged in successfully)", RegexOptions.Compiled);

        private static readonly Regex GameLoadPattern = new(@"Identified game:\s*(\d+)|Fetching data for game\s*(\d+)|Achievement .+? for game (\d+) unlocked|Game\s+(\d+)\s+loaded", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Challenge & Progress Regex
        // "Show challenge indicator for 86096 (Mad Dash Melody)"
        private static readonly Regex ChallengeStartPattern = new(@"(?:Achievements:|I/Achievements:)\s*Show challenge indicator for (\d+) \((.+?)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Hide challenge indicator for 86095 (Dash-A-Long)"
        private static readonly Regex ChallengeEndPattern = new(@"(?:Achievements:|I/Achievements:)\s*Hide challenge indicator for (\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Showing progress indicator: 539264 (Good Habits Start Early): 1/10"
        private static readonly Regex ProgressShowPattern = new(@"(?:Achievements:|I/Achievements:)\s*(?:Showing|Updating) progress indicator: (\d+) \((.+?)\): (.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Hiding progress indicator" / "Remove progress indicator"
        private static readonly Regex ProgressHidePattern = new(@"(?:Achievements:|I/Achievements:)\s*(?:Hiding progress indicator|Remove progress indicator)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Started HTTP request for 'https://media.retroachievements.org/Badge/94068.png'" OR PPSSPP "Downloading image: .../Badge/..."
        private static readonly Regex BadgeDownloadPattern = new(@"(?:HTTPDownloader:)?\s*(?:Started HTTP request for '.*?/Badge/|Downloading image: .*?/Badge/|HTTP GET request: .*?/Badge/)(\d+)\.png", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GameStopPattern = new(@"\[Core\]: Unloading game\.\.|Achievements: Unloading game", RegexOptions.Compiled);
        // EN: Refined pattern to exclude "already has this achievement unlocked" messages
        // FR: Motif affiné pour exclure les messages "already has this achievement unlocked"
        private static readonly Regex AchievementPattern = new(@"^(?!.*already has this achievement unlocked)(?:.*(?:Unlocked|Awarding) (?:unofficial |official )?achievement\s+(\d+):|.*Achievement .+? \((\d+)\) for game \d+ unlocked)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HardcorePattern = new(@"(?:Hardcore mode|hardcore) (?:enabled|paused|disabled)|cheevos_hardcore_mode_enable = ""(true|false)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // EN: Regex for Rich Presence update
        // FR: Regex pour la mise à jour de la Rich Presence
        // EN: PPSSPP Specific Regex (ID-less) / FR: Regex spécifiques PPSSPP (sans ID)
        // Matches: "04:12:319 Main thread  I[ACHIEVEMENTS]: Core\RetroAchievements.cpp:401 Challenge indicator show: Pacifist Racer"
        private static readonly Regex ChallengeStartPatternPpsspp = new(@"(?:Achievements:|I\[ACHIEVEMENTS\]:|I/Achievements:).*?Challenge indicator show: (.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ChallengeEndPatternPpsspp = new(@"(?:Achievements:|I\[ACHIEVEMENTS\]:|I/Achievements:).*?Challenge indicator hide: (.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // "Progress indicator show: Magic Missile!, progress: '1%' (1.000000)" or "Progress indicator update: ..."
        private static readonly Regex ProgressShowPatternPpsspp = new(@"(?:Achievements:|I\[ACHIEVEMENTS\]:|I/Achievements:).*?Progress indicator (?:show|update): (.+?), progress: '(.+?)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ProgressHidePatternPpsspp = new(@"(?:Achievements:|I\[ACHIEVEMENTS\]:|I/Achievements:).*?Progress indicator hide", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // EN: Leaderboard Regexes (Standard RetroArch/Rcheevos)
        // [INFO] [RCHEEVOS]: Leaderboard 104676 started: South Island Survivor
        private static readonly Regex LeaderboardStartPattern = new(@"(?:RCHEEVOS\]:|Achievements:|I/Achievements:)\s*Leaderboard (\d+) started: (.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // [INFO] [RCHEEVOS]: Leaderboard 104676 canceled
        private static readonly Regex LeaderboardFailedPattern = new(@"(?:RCHEEVOS\]:|Achievements:|I/Achievements:)\s*Leaderboard (\d+) canceled", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // [INFO] [RCHEEVOS]: Leaderboard 104676 submitted: 1234
        private static readonly Regex LeaderboardSuccessPattern = new(@"(?:RCHEEVOS\]:|Achievements:|I/Achievements:)\s*Leaderboard (\d+) submitted", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex RichPresencePattern = new(@"(?:Rich presence updated:|RPC:)\s*(.+)", RegexOptions.Compiled);
        
        // Dolphin Regex
        // Match: 40:38:060 Core\AchievementManager.cpp:81 I[RetroAchievements]: Unloading game 25428
        // Group 1: The message content
        private static readonly Regex DolphinRaPattern = new(@"I\[RetroAchievements\]:\s+(.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private bool _isHardcoreMode = false;
        public bool IsHardcoreMode => _isHardcoreMode;

        // EN: Events / FR: Événements
        public event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
        public event EventHandler? AchievementDetected; // EN: Fired immediately on log detection / FR: Déclenché immédiatement à la détection logs
        public event EventHandler<GameStartedEventArgs>? GameStarted;
        public event EventHandler? GameStopped;
        public event EventHandler<bool>? HardcoreStatusChanged;
        public event EventHandler<string>? RichPresenceUpdated;
        public event EventHandler<ChallengeUpdatedEventArgs>? ChallengeUpdated;

        // EN: Public Accessors for Overlay Data / FR: Accesseurs publics pour données Overlay
        public int CurrentGameUserPoints => _currentProgress?.Achievements?.Values.Where(a => _isHardcoreMode ? a.DateEarnedHardcore.HasValue : a.Unlocked).Sum(a => a.Points) ?? 0;
        public int CurrentGameTotalPoints => _currentProgress?.Achievements?.Values.Sum(a => a.Points) ?? 0;
        public Dictionary<string, Achievement>? CurrentGameAchievements => _currentProgress?.Achievements;
        public int? CurrentGameId => _currentGameId;

        public RetroAchievementsService(
            IConfigService config,
            IEsSettingsService esSettings,
            RetroAchievementsApiClient apiClient,
            ILogger<RetroAchievementsService> logger)
        {
            _config = config;
            _esSettings = esSettings;
            _apiClient = apiClient;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // EN: Check if RetroAchievements is enabled / FR: Vérifier si RetroAchievements est activé
            _isEnabled = _config.GetSetting("MarqueeRetroAchievements", "false").Equals("true", StringComparison.OrdinalIgnoreCase);
            
            if (!_isEnabled)
            {
                _logger.LogInformation("[RA Service] RetroAchievements is disabled in config.ini");
                return Task.CompletedTask;
            }

            _logger.LogInformation("[RA Service] Starting RetroAchievements monitoring...");

            // EN: Get credentials / FR: Récupérer identifiants
            _currentUsername = _esSettings.GetSetting("global.retroachievements.username");
            
            // EN: Define log files to monitor
            // FR: Définir les fichiers logs à surveiller
            var esLogDir = Path.Combine(_config.RetroBatPath, "emulationstation", ".emulationstation");
            var esLogPath = Path.Combine(esLogDir, "es_launch_stdout.log");
            
            var pcsx2LogPath = _config.Pcsx2LogPath;
            // var pcsx2LogDir = Path.GetDirectoryName(pcsx2LogPath); // Unused

            var duckStationLogPath = _config.DuckStationLogPath;

            // EN: PPSSPP Log Path determination
            // FR: Détermination du chemin de log PPSSPP
            var ppssppLogPath = Path.Combine(_config.RetroBatPath, "saves", "psp", "SYSTEM", "DUMP", "log.txt");

            // EN: Dolphin Log Path
            // FR: Chemin du log Dolphin
            var dolphinLogPath = Path.Combine(_config.RetroBatPath, "emulators", "dolphin-emu", "User", "Logs", "dolphin.log");

            var logsToMonitor = new List<string> { esLogPath };
            if (!string.IsNullOrEmpty(pcsx2LogPath)) logsToMonitor.Add(pcsx2LogPath);
            if (!string.IsNullOrEmpty(duckStationLogPath)) logsToMonitor.Add(duckStationLogPath);
            
            // EN: Check if PPSSPP log directory exists (it might be created by emulator later, but we try)
            // FR: Vérifier si le dossier log PPSSPP existe (il peut être créé par l'émulateur plus tard, mais on essaie)
            // Note: We monitor the file if the parent directory exists, similar to logic below.
            logsToMonitor.Add(ppssppLogPath);
            logsToMonitor.Add(dolphinLogPath);

            foreach (var logPath in logsToMonitor)
            {
                if (string.IsNullOrEmpty(logPath)) continue;
                
                var dir = Path.GetDirectoryName(logPath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

                try
                {
                    // EN: Set initial offset to end of file
                    // FR: Positionner l'offset à la fin du fichier
                    if (File.Exists(logPath))
                    {
                        using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            _logOffsets[logPath] = fs.Length;
                        }
                    }
                    else
                    {
                        _logOffsets[logPath] = 0;
                    }

                    var watcher = new FileSystemWatcher(dir, Path.GetFileName(logPath))
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };

                    watcher.Changed += (s, e) => ProcessLogFile(logPath);
                    watcher.Created += (s, e) => ProcessLogFile(logPath);
                    _watchers.Add(watcher);

                    _logger.LogInformation($"[RA Service] Started monitoring: {logPath} (Initial Offset: {_logOffsets[logPath]})");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA Service] Failed to monitor {logPath}: {ex.Message}");
                }
            }

            // EN: Start polling timer as fallback / FR: Démarrer timer de polling comme fallback
            _pollingTimer = new System.Threading.Timer(_ => ProcessAllLogs(), null, 1000, 1000);

            // EN: Setup RetroArch logging
            EnsureRetroArchLogging();
            // EN: Setup DuckStation logging
            EnsureDuckStationLogging();
            // EN: Setup PPSSPP logging
            EnsurePpssppLogging();
            // EN: Setup Dolphin logging
            EnsureDolphinLogging();

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            
            foreach (var watcher in _watchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
            _logger.LogInformation("[RA Service] Stopped monitoring");
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// EN: Ensure RetroArch has logging enabled
        /// FR: S'assurer que RetroArch a le logging activé
        /// </summary>
        private void EnsureRetroArchLogging()
        {
            try
            {
                var cfgPath = Path.Combine(_config.RetroBatPath, "emulators", "retroarch", "retroarch.cfg");
                _logger.LogInformation($"[RA Service] Checking retroarch.cfg at: {cfgPath}");
                
                if (!File.Exists(cfgPath))
                {
                    _logger.LogWarning($"[RA Service] retroarch.cfg NOT FOUND at: {cfgPath}");
                    return;
                }

                _logger.LogInformation("[RA Service] retroarch.cfg found, reading content...");
                var lines = File.ReadAllLines(cfgPath).ToList();
                _logger.LogInformation($"[RA Service] Read {lines.Count} lines from retroarch.cfg");
                
                bool changed = false;

                void SetKey(string key, string val)
                {
                    int idx = lines.FindIndex(l => l.StartsWith(key + " "));
                    if (idx != -1)
                    {
                        if (!lines[idx].Contains(val))
                        {
                            _logger.LogInformation($"[RA Service] Updating existing key: {key} = \"{val}\"");
                            lines[idx] = $"{key} = \"{val}\"";
                            changed = true;
                        }
                        else
                        {
                            _logger.LogInformation($"[RA Service] Key already correct: {key} = \"{val}\"");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"[RA Service] Adding new key: {key} = \"{val}\"");
                        lines.Add($"{key} = \"{val}\"");
                        changed = true;
                    }
                }

                SetKey("log_to_file", "true");
                SetKey("log_to_file_timestamp", "false");
                SetKey("log_verbosity", "true");

                if (changed)
                {
                    _logger.LogInformation($"[RA Service] Writing {lines.Count} lines back to retroarch.cfg...");
                    File.WriteAllLines(cfgPath, lines);
                    _logger.LogInformation("[RA Service] ✅ retroarch.cfg updated successfully!");
                }
                else
                {
                    _logger.LogInformation("[RA Service] retroarch.cfg already has correct settings, no changes needed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] ❌ FAILED to update retroarch.cfg: {ex.Message}");
                _logger.LogError($"[RA Service] Exception details: {ex}");
            }
        }

        /// <summary>
        /// EN: Ensure DuckStation has logging enabled
        /// FR: S'assurer que DuckStation a le logging activé
        /// </summary>
        /// <summary>
        /// EN: Ensure DuckStation has logging enabled with optimized filtering
        /// FR: S'assurer que DuckStation a le logging activé avec un filtrage optimisé
        /// </summary>
        private void EnsureDuckStationLogging()
        {
            try
            {
                var cfgPath = _config.DuckStationSettingsPath;
                if (string.IsNullOrEmpty(cfgPath) || !File.Exists(cfgPath)) return;

                _logger.LogInformation($"[RA Service] Checking DuckStation settings at: {cfgPath}");
                var lines = File.ReadAllLines(cfgPath).ToList();
                bool changed = false;

                // EN: Enforced settings to reduce noise but ensure detection
                // FR: Paramètres forcés pour réduire le bruit mais assurer la détection
                var enforcedSettings = new Dictionary<string, string>
                {
                    { "LogToFile", "true" },
                    { "LogLevel", "Dev" }, // Keep Dev for info, but filter below
                    
                    // Critical for RA Detection
                    { "Achievements", "true" },
                    { "System", "true" },      // For 'System booted' (Stop pattern)
                    { "GameList", "true" },    // For 'Identified game'
                    { "GameDatabase", "true" }, // Helper for ID
                    { "Host", "true" },        // Core events
                    
                    // Reduce Noise (Disable high frequency logs)
                    // FR: Réduire le bruit (Désactiver logs haute fréquence)
                    { "Bus", "false" },
                    { "CDImage", "false" },
                    { "CDROM", "false" },
                    { "CDROMAsyncReader", "false" },
                    { "CPU", "false" },
                    { "DMA", "false" },
                    { "GPU", "false" },
                    { "GPUDevice", "false" },
                    { "GPUDump", "false" },
                    { "GPUThread", "false" },
                    { "GPU_SW", "false" },
                    { "GPU_HW", "false" },
                    { "InterruptController", "false" },
                    { "MDEC", "false" },
                    { "Recompiler", "false" },
                    { "SPU", "false" },
                    { "Timers", "false" },
                    { "TimingEvents", "false" },
                    { "Pad", "false" },
                    { "MemoryCard", "false" },
                    { "AudioStream", "false" },
                    { "CubebAudioStream", "false" },
                    { "InputManager", "false" },
                    { "XInputSource", "false" },
                    { "DInputSource", "false" },
                    { "SDL", "false" },
                    { "BIOS", "false" },
                    { "CodeCache", "false" },
                    { "PerfMon", "false" },
                    { "Controller", "false" },
                    { "ImGuiManager", "false" },
                    { "ImGuiFullscreen", "false" },
                    { "Multitap", "false" }
                };

                int loggingSectionIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals("[Logging]", StringComparison.OrdinalIgnoreCase))
                    {
                        loggingSectionIndex = i;
                        break;
                    }
                }

                if (loggingSectionIndex == -1)
                {
                    lines.Add("");
                    lines.Add("[Logging]");
                    loggingSectionIndex = lines.Count - 1;
                    changed = true;
                }

                // Parse existing keys in [Logging] section to update them
                int currentIndex = loggingSectionIndex + 1;
                var processedKeys = new HashSet<string>();

                while (currentIndex < lines.Count)
                {
                    var line = lines[currentIndex].Trim();
                    if (string.IsNullOrWhiteSpace(line)) 
                    {
                        currentIndex++;
                        continue;
                    }
                    if (line.StartsWith("[")) break; // Next section

                    var parts = line.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        // If this key is one we want to enforce
                        if (enforcedSettings.ContainsKey(key))
                        {
                            var targetValue = enforcedSettings[key];
                            var currentValue = parts[1].Trim();
                            
                            if (!currentValue.Equals(targetValue, StringComparison.OrdinalIgnoreCase))
                            {
                                lines[currentIndex] = $"{key} = {targetValue}";
                                changed = true;
                                _logger.LogInformation($"[RA Service] DuckStation: Updated {key} to {targetValue}");
                            }
                            processedKeys.Add(key);
                        }
                    }
                    currentIndex++;
                }

                // Add missing enforced keys
                foreach (var kvp in enforcedSettings)
                {
                    if (!processedKeys.Contains(kvp.Key))
                    {
                        lines.Insert(loggingSectionIndex + 1, $"{kvp.Key} = {kvp.Value}");
                        changed = true;
                        _logger.LogInformation($"[RA Service] DuckStation: Added {kvp.Key} = {kvp.Value}");
                    }
                }

                if (changed)
                {
                    File.WriteAllLines(cfgPath, lines);
                    _logger.LogInformation("[RA Service] DuckStation settings updated with optimized filters.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] Failed to update DuckStation settings: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Ensure PPSSPP has logging enabled with optimized settings
        /// FR: S'assurer que PPSSPP a le logging activé avec des paramètres optimisés
        /// </summary>
        private void EnsurePpssppLogging()
        {
            try
            {
                var ppssppIniPath = Path.Combine(_config.RetroBatPath, "saves", "psp", "SYSTEM", "ppsspp.ini");
                if (!File.Exists(ppssppIniPath))
                {
                    _logger.LogWarning($"[RA Service] PPSSPP ini NOT FOUND at: {ppssppIniPath}");
                    return;
                }

                _logger.LogInformation($"[RA Service] Checking PPSSPP settings at: {ppssppIniPath}");
                var lines = File.ReadAllLines(ppssppIniPath).ToList();
                bool changed = false;

                // Helper to update specific key in a specific section
                void UpdateKey(string section, string key, string value)
                {
                    int sectionIdx = -1;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Trim().Equals(section, StringComparison.OrdinalIgnoreCase))
                        {
                            sectionIdx = i;
                            break;
                        }
                    }

                    if (sectionIdx == -1) return; // Section not found, skip (PPSSPP creates default sections usually)

                    // Find key within section (until next section)
                    int keyIdx = -1;
                    int insertIdx = -1;
                    for (int i = sectionIdx + 1; i < lines.Count; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        if (line.StartsWith("[")) // Next section
                        {
                            insertIdx = i;
                            break;
                        }

                        var parts = line.Split('=', 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            keyIdx = i;
                            break;
                        }
                    }

                    if (keyIdx != -1)
                    {
                        // Update existing
                        var currentLine = lines[keyIdx];
                        var parts = currentLine.Split('=', 2);
                        if (parts.Length == 2 && !parts[1].Trim().Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[keyIdx] = $"{key} = {value}";
                            changed = true;
                            _logger.LogInformation($"[RA Service] PPSSPP: Updated {key} to {value}");
                        }
                    }
                    else
                    {
                        // Insert new key
                        int targetIdx = (insertIdx != -1) ? insertIdx : lines.Count;
                        lines.Insert(targetIdx, $"{key} = {value}");
                        changed = true;
                        _logger.LogInformation($"[RA Service] PPSSPP: Added {key} = {value}");
                    }
                }

                // 1. [General] Section
                UpdateKey("[General]", "Enable Logging", "True");
                UpdateKey("[General]", "FileLogging", "True");

                // 2. [Log] Section - Batch processing
                int logSectionIdx = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals("[Log]", StringComparison.OrdinalIgnoreCase))
                    {
                        logSectionIdx = i;
                        break;
                    }
                }

                if (logSectionIdx != -1)
                {
                    for (int i = logSectionIdx + 1; i < lines.Count; i++)
                    {
                        var line = lines[i].Trim();
                        if (line.StartsWith("[")) break; // End of section
                        
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            string key = parts[0].Trim();
                            string val = parts[1].Trim();

                            // Target keys we want to FORCE to True/6
                            if (key.Equals("ACHIEVEMENTSEnabled", StringComparison.OrdinalIgnoreCase) ||
                                key.Equals("HTTPEnabled", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!val.Equals("True", StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = $"{key} = True";
                                    changed = true;
                                    _logger.LogInformation($"[RA Service] PPSSPP: Enabled {key}");
                                }
                            }
                            else if (key.Equals("ACHIEVEMENTSLevel", StringComparison.OrdinalIgnoreCase) ||
                                     key.Equals("HTTPLevel", StringComparison.OrdinalIgnoreCase))
                            {
                                if (val != "6")
                                {
                                    lines[i] = $"{key} = 6";
                                    changed = true;
                                    _logger.LogInformation($"[RA Service] PPSSPP: Set {key} level to 6");
                                }
                            }
                            // Target keys we want to FORCE to False (All other *Enabled)
                            else if (key.EndsWith("Enabled", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!val.Equals("False", StringComparison.OrdinalIgnoreCase))
                                {
                                    lines[i] = $"{key} = False";
                                    changed = true;
                                    // _logger.LogInformation($"[RA Service] PPSSPP: Disabled {key}"); // Reduce log spam
                                }
                            }
                        }
                    }
                }

                if (changed)
                {
                    File.WriteAllLines(ppssppIniPath, lines);
                    _logger.LogInformation("[RA Service] PPSSPP settings updated successfully.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] Failed to update PPSSPP settings: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Ensure Dolphin has logging enabled (Verbosity=4 + RetroAchievements=True)
        /// FR: S'assurer que Dolphin a le logging activé
        /// </summary>
        private void EnsureDolphinLogging()
        {
            try
            {
                var loggerIniPath = Path.Combine(_config.RetroBatPath, "emulators", "dolphin-emu", "User", "Config", "Logger.ini");
                
                // EN: If file doesn't exist, we might want to create it or skip.
                // FR: Si le fichier n'existe pas, on peut vouloir le créer ou passer.
                // Dolphin usually creates it on first run, but we can try editing if dir exists.
                var loggerDir = Path.GetDirectoryName(loggerIniPath);
                if (!Directory.Exists(loggerDir)) return;

                _logger.LogInformation($"[RA Service] Checking Dolphin settings at: {loggerIniPath}");
                
                List<string> lines = new List<string>();
                if (File.Exists(loggerIniPath))
                    lines = File.ReadAllLines(loggerIniPath).ToList();
                
                bool changed = false;

                // Sections needed: [Options] and [Logs]
                // 1. [Options] -> Verbosity = 4, WriteToConsole=True, WriteToFile=True, WriteToWindow=True
                // 2. [Logs] -> RetroAchievements = True

                int optionsIdx = -1;
                int logsIdx = -1;

                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals("[Options]", StringComparison.OrdinalIgnoreCase)) optionsIdx = i;
                    if (lines[i].Trim().Equals("[Logs]", StringComparison.OrdinalIgnoreCase)) logsIdx = i;
                }

                // Ensure [Options] exists
                if (optionsIdx == -1)
                {
                    lines.Add("");
                    lines.Add("[Options]");
                    optionsIdx = lines.Count - 1;
                    changed = true;
                }

                // Ensure [Logs] exists
                if (logsIdx == -1)
                {
                    lines.Add("");
                    lines.Add("[Logs]");
                    logsIdx = lines.Count - 1;
                    changed = true;
                }
                
                // Re-scan indices in case we added sections
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].Trim().Equals("[Options]", StringComparison.OrdinalIgnoreCase)) optionsIdx = i;
                    if (lines[i].Trim().Equals("[Logs]", StringComparison.OrdinalIgnoreCase)) logsIdx = i;
                }

                // Helper to set key in section
                void SetKeyInSection(int sectionStartIdx, string key, string value)
                {
                    // Find end of section (next [Section] or EOF)
                    int sectionEnd = lines.Count;
                    for (int k = sectionStartIdx + 1; k < lines.Count; k++)
                    {
                        if (lines[k].Trim().StartsWith("["))
                        {
                            sectionEnd = k;
                            break;
                        }
                    }

                    int keyIdx = -1;
                    for (int k = sectionStartIdx + 1; k < sectionEnd; k++)
                    {
                        var parts = lines[k].Split('=', 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                        {
                            keyIdx = k;
                            break;
                        }
                    }

                    if (keyIdx != -1)
                    {
                        var currentVal = lines[keyIdx].Split('=', 2)[1].Trim();
                        if (!currentVal.Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[keyIdx] = $"{key} = {value}";
                            changed = true;
                            _logger.LogInformation($"[RA Service] Dolphin: Updated {key} to {value}");
                        }
                    }
                    else
                    {
                        // Insert at start of section
                        lines.Insert(sectionStartIdx + 1, $"{key} = {value}");
                        changed = true;
                        _logger.LogInformation($"[RA Service] Dolphin: Added {key} = {value}");
                        
                        // Adjust logsIdx if we inserted before it
                        if (sectionStartIdx < logsIdx) logsIdx++;
                    }
                }

                SetKeyInSection(optionsIdx, "Verbosity", "4"); // Warning Level (seems to include Info/Debug for Enabled Logs?)
                // User requested Verbosity=4
                SetKeyInSection(optionsIdx, "WriteToConsole", "True");
                SetKeyInSection(optionsIdx, "WriteToFile", "True");
                SetKeyInSection(optionsIdx, "WriteToWindow", "True");

                SetKeyInSection(logsIdx, "RetroAchievements", "True");

                if (changed)
                {
                    File.WriteAllLines(loggerIniPath, lines);
                    _logger.LogInformation("[RA Service] Dolphin settings updated successfully.");
                }

                // EN: Clear Dolphin Log (User Request: Dolphin doesn't clear it, leading to old logs detection)
                // FR: Vider le log Dolphin (Demande utilisateur : Dolphin ne le vide pas, causant la détection de vieux logs)
                var dolphinLogPath = Path.Combine(_config.RetroBatPath, "emulators", "dolphin-emu", "User", "Logs", "dolphin.log");
                if (File.Exists(dolphinLogPath))
                {
                    try 
                    {
                        // EN: Truncate file
                        File.WriteAllText(dolphinLogPath, string.Empty);
                        _logger.LogInformation($"[RA Service] Dolphin: Cleared log file at {dolphinLogPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[RA Service] Dolphin: Could not clear log file (might be in use?): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] Failed to update Dolphin settings: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Process all monitored logs
        /// FR: Traiter tous les logs surveillés
        /// </summary>
        private void ProcessAllLogs()
        {
            foreach (var logPath in _logOffsets.Keys.ToList())
            {
                ProcessLogFile(logPath);
            }
        }

        /// <summary>
        /// EN: Process change on a specific log file
        /// FR: Traiter changement sur un fichier log spécifique
        /// </summary>
        private void ProcessLogFile(string logPath)
        {
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;

            lock (_logLock)
            {
                try
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var fileSize = stream.Length;

                    if (!_logOffsets.TryGetValue(logPath, out long lastPosition))
                    {
                        lastPosition = 0;
                    }

                    // rotation detected
                    if (fileSize < lastPosition)
                    {
                        _logger.LogInformation($"[RA Service] Log rotation detected for {Path.GetFileName(logPath)} (Size: {fileSize} < Last: {lastPosition}), resetting position to 0");
                        lastPosition = 0;
                        // EN: Important: We keep _currentGameId etc. because we might be in the middle of a session.
                        // FR: Important: On garde _currentGameId etc. car on est peut-être en milieu de session.
                    }

                    stream.Seek(lastPosition, SeekOrigin.Begin);

                    using var reader = new StreamReader(stream);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        ProcessLogLine(line, logPath);
                    }

                    // EN: Special handling for Dolphin: if log is empty or small, line reading might work, 
                    // but we rely on Regex detection inside ProcessLogLine.
                    
                    _logOffsets[logPath] = stream.Position;

                    _logOffsets[logPath] = stream.Position;
                }
                catch (IOException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA Service] Error processing log {logPath}: {ex.Message}");
                }
            }
        }



        private void ProcessLogLine(string line, string sourceLog)
        {
            // EN: Dolphin Specific Extraction
            // FR: Extraction spécifique Dolphin
            var dolphinMatch = DolphinRaPattern.Match(line);
            if (dolphinMatch.Success)
            {
                // Inject the inner content as a new line to process standard patterns (Login, etc.)
                // Dolphin line: "I[RetroAchievements]: Login successful"
                // Extracted: "Login successful" -> This might not match standard "User Login Pattern" which expects "Attempting token login..."
                // But let's check what patterns we have.
                // UserLoginPattern: "(?:RCHEEVOS\]|Achievements:|I/Achievements:)\s*(?:Attempting token login with user '(.+?)'|(.+?)\s+logged in successfully)"
                
                // If text is "Login successful", it might not match strict patterns.
                // We might need to normalize it or add specific handlers.
                // Let's assume standard messages like "Game loaded" match what we need or pass it recursively.
                
                var content = dolphinMatch.Groups[1].Value.Trim();
                
                // Log the passthrough for debug
                // _logger.LogInformation($"[RA Service] Dolphin Event: {content}");
                
                // Treat content as a line for standard regexes?
                // The standard regexes might expect prefixes like "Achievements:". 
                // Let's prepend a fake prefix to satisfy them if needed, or rely on loose matching.
                // UserLoginPattern has (?:RCHEEVOS\]|Achievements:|I/Achievements:) prefix group.
                // So "Login successful" alone won't match.
                // We should synthesis a line that matches.
                
                string syntheticLine = $"I/Achievements: {content}";
                ProcessLogLine(syntheticLine, sourceLog + " (Internal)");
                return;
            }

            // EN: User login / FR: Connexion utilisateur
            var loginMatch = UserLoginPattern.Match(line);
            if (loginMatch.Success)
            {
                var rawUser = loginMatch.Groups[1].Success ? loginMatch.Groups[1].Value : loginMatch.Groups[2].Value;
                // EN: Robust trimming: Remove leading colons or spaces which cause 422 API errors
                // FR: Nettoyage robuste : Enlever les deux-points ou espaces en tête qui causent des erreurs API 422
                var user = rawUser.Trim(':', ' ', '\t');
                
                if (_currentUsername == user && _currentProgress != null)
                {
                    // EN: Already logged in with valid profile, skip re-load to avoid redundant API calls
                    return;
                }

                _currentUsername = user;
                _logger.LogInformation($"[RA Service] User logged in: '{_currentUsername}' (Raw: '{rawUser}' from {Path.GetFileName(sourceLog)})");
                _ = LoadUserProfileAsync(_currentUsername);
                return;
            }



            // EN: Game load / FR: Chargement jeu
            var gameMatch = GameLoadPattern.Match(line);
            if (gameMatch.Success)
            {
                var gameIdStr = gameMatch.Groups[1].Success ? gameMatch.Groups[1].Value : 
                                (gameMatch.Groups[2].Success ? gameMatch.Groups[2].Value : 
                                (gameMatch.Groups[3].Success ? gameMatch.Groups[3].Value : gameMatch.Groups[4].Value));
                int gameId = int.Parse(gameIdStr);

                if (_currentGameId == gameId && _currentProgress != null)
                {
                    // EN: Same game already loaded, skip redundant work (log re-read or multiple logs)
                    return;
                }

                _currentGameId = gameId;
                _logger.LogInformation($"[RA Service] Game loaded: {_currentGameId} (from {Path.GetFileName(sourceLog)})");
                
                string? emulatorHint = null;
                if (sourceLog.Contains("dolphin", StringComparison.OrdinalIgnoreCase)) emulatorHint = "dolphin";
                else if (sourceLog.Contains("retroarch", StringComparison.OrdinalIgnoreCase)) emulatorHint = "retroarch";
                else if (sourceLog.Contains("duckstation", StringComparison.OrdinalIgnoreCase)) emulatorHint = "duckstation";
                else if (sourceLog.Contains("pcsx2", StringComparison.OrdinalIgnoreCase)) emulatorHint = "pcsx2";
                else if (sourceLog.Contains("ppsspp", StringComparison.OrdinalIgnoreCase)) emulatorHint = "ppsspp";

                // EN: Check for Hardcore status on specific emulator lines (PCSX2/DuckStation)
                // FR: Vérifier statut Hardcore sur lignes émulateurs spécifiques (PCSX2/DuckStation)
                if (line.Contains("hardcore enabled", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_isHardcoreMode)
                    {
                        _isHardcoreMode = true;
                        _logger.LogInformation($"[RA Service] Hardcore Mode DETECTED (via GameLoad line): ON");
                        HardcoreStatusChanged?.Invoke(this, _isHardcoreMode);
                    }
                }
                else if (emulatorHint == "duckstation" || emulatorHint == "retroarch")
                {
                    // EN: Proactively check config files if the log line is ambiguous (DuckStation starts logs with OFF then ON)
                    // FR: Vérifier proactivement les fichiers de config si la ligne de log est ambiguë
                    bool hcConfig = CheckHardcoreSettings(emulatorHint);
                    if (_isHardcoreMode != hcConfig)
                    {
                        _isHardcoreMode = hcConfig;
                        _logger.LogInformation($"[RA Service] Hardcore Mode DETECTED (via Config Check on GameLoad): {(_isHardcoreMode ? "ON" : "OFF")}");
                        HardcoreStatusChanged?.Invoke(this, _isHardcoreMode);
                    }
                }

                _ = LoadGameDataAsync(_currentGameId.Value, emulatorHint);
                return;
            }

            // EN: Game stop / FR: Arrêt jeu
            // EN: Game stop / FR: Arrêt jeu
            var stopMatch = GameStopPattern.Match(line);
            if (stopMatch.Success)
            {
                _logger.LogInformation($"[RA Service] Game stopped (Log Pattern detected in {Path.GetFileName(sourceLog)}): {line}");
                
                string? emulatorHint = null;
                if (sourceLog.Contains("dolphin", StringComparison.OrdinalIgnoreCase)) emulatorHint = "dolphin";
                else if (sourceLog.Contains("retroarch", StringComparison.OrdinalIgnoreCase)) emulatorHint = "retroarch";

                ResetState(emulatorHint);
                return;
            }

            // EN: Hardcore Mode Detection
            // FR: Détection Mode Hardcore
            var hardcoreMatch = HardcorePattern.Match(line);
            if (hardcoreMatch.Success)
            {
                var status = hardcoreMatch.Value.ToLowerInvariant();
                bool newStatus = status.Contains("enabled") || status.Contains("true");
                
                if (_isHardcoreMode != newStatus)
                {
                    _isHardcoreMode = newStatus;
                    _logger.LogInformation($"[RA Service] Hardcore Mode DETECTED: {(_isHardcoreMode ? "ON" : "OFF")}");
                    HardcoreStatusChanged?.Invoke(this, _isHardcoreMode);
                }
                return;
            }


            // EN: Achievement Unlocked
            // FR: Succès débloqué
            var achievementMatch = AchievementPattern.Match(line);
            if (achievementMatch.Success)
            {
                // EN: Immediate signal to suppress RP / FR: Signal immédiat pour supprimer RP
                AchievementDetected?.Invoke(this, EventArgs.Empty);

                int achieveId = int.Parse(achievementMatch.Groups[1].Value);
                _ = HandleAchievementUnlockedAsync(achieveId);
                return;
            }

            // EN: Badge Download (Cache for Challenge Association)
            // FR: Téléchargement Badge (Cache pour association Challenge)
            var badgeMatch = BadgeDownloadPattern.Match(line);
            if (badgeMatch.Success)
            {
                var badgeUrlId = badgeMatch.Groups[1].Value; // e.g. "94068"
                // Store with timestamp
                var cachePath = Path.Combine(_apiClient.BadgeCachePath, badgeUrlId + ".png");
                // Note: File might not exist yet (download started), but we know the intent
                _recentBadgeDownloads[badgeUrlId] = (cachePath, DateTime.Now);
                
                // Cleanup old entries (> 10s)
                var threshold = DateTime.Now.AddSeconds(-10);
                foreach (var kvp in _recentBadgeDownloads)
                {
                    if (kvp.Value.Time < threshold) 
                        _recentBadgeDownloads.TryRemove(kvp.Key, out _);
                }
                return;
            }

            // EN: Challenge Start (Timer)
            // FR: Début Challenge (Timer)
            var challengeStart = ChallengeStartPattern.Match(line);
            if (challengeStart.Success)
            {
                int achieveId = int.Parse(challengeStart.Groups[1].Value);
                string title = challengeStart.Groups[2].Value;
                
                // Construct state
                var state = new ChallengeState
                {
                    AchievementId = achieveId,
                    Title = title,
                    Type = ChallengeType.Timer, // Default heuristic, refined below / FR: Heuristique par défaut, affinée ci-dessous
                    StartTime = DateTime.Now,
                    IsActive = true
                };

                // EN: Try to get detailed metadata if available (Badge, Title, Description)
                // FR: Essayer de récupérer des métadonnées détaillées si disponibles (Badge, Titre, Description)
                if (_currentProgress?.Achievements != null && _currentProgress.Achievements.TryGetValue(achieveId.ToString(), out var achieve))
                {
                    state.Title = achieve.Title;
                    state.Description = achieve.Description;
                    state.BadgePath = achieve.BadgeName;

                    // EN: Heuristic to detect if it's really a timed challenge (MM:SS or keywords)
                    // FR: Heuristique pour détecter si c'est vraiment un défi chronométré
                    bool hasTimeWords = achieve.Description.Contains(":", StringComparison.OrdinalIgnoreCase) || 
                                       achieve.Description.Contains("seconds", StringComparison.OrdinalIgnoreCase) || 
                                       achieve.Description.Contains("minutes", StringComparison.OrdinalIgnoreCase) ||
                                       achieve.Description.Contains("temps", StringComparison.OrdinalIgnoreCase) ||
                                       achieve.Description.Contains("secondes", StringComparison.OrdinalIgnoreCase);

                    if (!hasTimeWords)
                    {
                        // EN: If it doesn't look like a timer challenge, just use Progress/Status type for static display
                        // FR: Si ça ne ressemble pas à un défi chrono, utiliser le type Progress/Status pour affichage statique
                        state.Type = ChallengeType.Progress; 
                        state.Progress = "ACTIVE";
                    }
                }
                
                // 2. Fallback: Recent download heuristic for Badge
                if (string.IsNullOrEmpty(state.BadgePath) || !File.Exists(state.BadgePath))
                {
                    var recent = _recentBadgeDownloads.Values.OrderByDescending(v => v.Time).FirstOrDefault();
                    if (!string.IsNullOrEmpty(recent.Path))
                    {
                        state.BadgePath = recent.Path;
                    }
                }

                _activeChallenges[achieveId] = state;
                ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                return;
            }

            // EN: PPSSPP Challenge Start (Title-based)
            // FR: Début Challenge PPSSPP (Basé sur le titre)
            var ppssppChallengeStart = ChallengeStartPatternPpsspp.Match(line);
            if (ppssppChallengeStart.Success)
            {
                string title = ppssppChallengeStart.Groups[1].Value.Trim();
                
                // Lookup ID by Title
                var achievement = _currentProgress?.Achievements?.Values.FirstOrDefault(a => a.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
                
                if (achievement != null)
                {
                    int achieveId = achievement.ID;
                    // Construct state
                    var state = new ChallengeState
                    {
                        AchievementId = achieveId,
                        Title = achievement.Title,
                        Description = achievement.Description,
                        BadgePath = achievement.BadgeName,
                        StartTime = DateTime.Now,
                        IsActive = true
                    };

                    // Heuristic for Timer
                    bool hasTimeWords = achievement.Description.Contains(":", StringComparison.OrdinalIgnoreCase) || 
                                       achievement.Description.Contains("seconds", StringComparison.OrdinalIgnoreCase) || 
                                       achievement.Description.Contains("minutes", StringComparison.OrdinalIgnoreCase) ||
                                       achievement.Description.Contains("temps", StringComparison.OrdinalIgnoreCase) ||
                                       achievement.Description.Contains("secondes", StringComparison.OrdinalIgnoreCase);

                    state.Type = hasTimeWords ? ChallengeType.Timer : ChallengeType.Progress;
                    if (state.Type == ChallengeType.Progress) state.Progress = "ACTIVE";

                    _activeChallenges[achieveId] = state;
                    ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                    return;
                }
            }

            // EN: Challenge End (Timer)
            // FR: Fin Challenge (Timer)
            var challengeEnd = ChallengeEndPattern.Match(line);
            if (challengeEnd.Success)
            {
                int achieveId = int.Parse(challengeEnd.Groups[1].Value);
                if (_activeChallenges.TryRemove(achieveId, out var state))
                {
                    state.IsActive = false;
                    ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                }
                return;
            }

            // EN: PPSSPP Challenge End
            var ppssppChallengeEnd = ChallengeEndPatternPpsspp.Match(line);
            if (ppssppChallengeEnd.Success)
            {
                string title = ppssppChallengeEnd.Groups[1].Value.Trim();
                 // Lookup ID by Title
                var achievement = _currentProgress?.Achievements?.Values.FirstOrDefault(a => a.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

                if (achievement != null)
                {
                    int achieveId = achievement.ID;
                    if (_activeChallenges.TryRemove(achieveId, out var state))
                    {
                        state.IsActive = false;
                        ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                    }
                }
                return;
            }

            // EN: Progress Show/Update
            // FR: Affichage/Mise à jour Progrès
            var progressShow = ProgressShowPattern.Match(line);
            if (progressShow.Success)
            {
                int achieveId = int.Parse(progressShow.Groups[1].Value);
                string title = progressShow.Groups[2].Value;
                string progress = progressShow.Groups[3].Value; // "1/10"

                var state = _activeChallenges.GetOrAdd(achieveId, id => new ChallengeState
                {
                    AchievementId = id,
                    Title = title,
                    Type = ChallengeType.Progress,
                    StartTime = DateTime.Now
                });

                state.Progress = progress;
                state.IsActive = true;
                
                // Badge Logic
                if (string.IsNullOrEmpty(state.BadgePath))
                {
                   if (_currentProgress?.Achievements != null && _currentProgress.Achievements.TryGetValue(achieveId.ToString(), out var achieve))
                   {
                       state.BadgePath = achieve.BadgeName;
                   }
                }

                ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                return;
            }

            // EN: PPSSPP Progress Show/Update
            var ppssppProgressShow = ProgressShowPatternPpsspp.Match(line);
            if (ppssppProgressShow.Success)
            {
                string title = ppssppProgressShow.Groups[1].Value.Trim();
                string progress = ppssppProgressShow.Groups[2].Value.Trim();
                
                 // Lookup ID by Title
                var achievement = _currentProgress?.Achievements?.Values.FirstOrDefault(a => a.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

                if (achievement != null)
                {
                    int achieveId = achievement.ID;
                    var state = _activeChallenges.GetOrAdd(achieveId, id => new ChallengeState
                    {
                        AchievementId = id,
                        Title = achievement.Title,
                        BadgePath = achievement.BadgeName,
                        Type = ChallengeType.Progress,
                        StartTime = DateTime.Now
                    });

                    state.Progress = progress; // e.g. "1%"
                    state.IsActive = true;
                    
                    // Fallback: Recent download heuristic for Badge if missing from API data
                    if (string.IsNullOrEmpty(state.BadgePath) || !File.Exists(state.BadgePath))
                    {
                        var recent = _recentBadgeDownloads.Values.OrderByDescending(v => v.Time).FirstOrDefault();
                        if (!string.IsNullOrEmpty(recent.Path))
                        {
                             state.BadgePath = recent.Path;
                        }
                    }

                    ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                }
                return;
            }

            // EN: Progress Hide
            // FR: Masquage Progrès
            var progressHide = ProgressHidePattern.Match(line);
            if (progressHide.Success)
            {
                // Logic: Remove ALL Progress type challenges or just the last?
                // Logs suggest purely sequential focus for Progress.
                // We'll remove all active *Progress* types to be safe and clean.
                var progressKeys = _activeChallenges.Where(kvp => kvp.Value.Type == ChallengeType.Progress).Select(k => k.Key).ToList();
                foreach (var key in progressKeys)
                {
                    if (_activeChallenges.TryRemove(key, out var state))
                    {
                        state.IsActive = false;
                        ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                    }
                }
                return;
            }
            
            // EN: PPSSPP Progress Hide
            var ppssppProgressHide = ProgressHidePatternPpsspp.Match(line);
            if (ppssppProgressHide.Success)
            {
                // Remove all Progress types, similar to standard logic
                var progressKeys = _activeChallenges.Where(kvp => kvp.Value.Type == ChallengeType.Progress).Select(k => k.Key).ToList();
                foreach (var key in progressKeys)
                {
                    if (_activeChallenges.TryRemove(key, out var state))
                    {
                        state.IsActive = false;
                        ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                    }
                }
                return;
            }

            // EN: Leaderboard Start/Cancel/Submit
            // FR: Démarrage/Annulation/Soumission Leaderboard
            var lbStart = LeaderboardStartPattern.Match(line);
            if (lbStart.Success)
            {
                int id = int.Parse(lbStart.Groups[1].Value);
                string title = lbStart.Groups[2].Value.Trim();

                var state = _activeChallenges.GetOrAdd(id, k => new ChallengeState
                {
                    AchievementId = k,
                    Title = title,
                    Type = ChallengeType.Leaderboard,
                    StartTime = DateTime.Now
                });

                // EN: Force update properties in case we retrieved an existing inactive state
                // FR: Forcer la mise à jour des propriétés au cas où on récupère un état inactif existant
                state.Type = ChallengeType.Leaderboard;
                state.Title = title;
                state.StartTime = DateTime.Now;

                state.IsActive = true;
                state.Progress = "Attempt"; // Initial text

                // EN: Try to get leaderboard badge from current progress
                // FR: Essayer de récupérer le badge du leaderboard depuis la progression actuelle
                if (_currentProgress?.Leaderboards != null)
                {
                    var lbData = _currentProgress.Leaderboards.FirstOrDefault(l => l.ID == id);
                    if (lbData != null && !string.IsNullOrEmpty(lbData.BadgeName) && File.Exists(lbData.BadgeName))
                    {
                        state.BadgePath = lbData.BadgeName;
                        state.Title = lbData.Title;
                        state.Description = lbData.Description;
                    }
                }

                ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                return;
            }

            var lbFail = LeaderboardFailedPattern.Match(line);
            var lbSuccess = LeaderboardSuccessPattern.Match(line);

            if (lbFail.Success || lbSuccess.Success)
            {
                var match = lbFail.Success ? lbFail : lbSuccess;
                int id = int.Parse(match.Groups[1].Value);

                if (_activeChallenges.TryRemove(id, out var state))
                {
                    state.IsActive = false;
                    ChallengeUpdated?.Invoke(this, new ChallengeUpdatedEventArgs { State = state });
                }
                return;
            }

            // EN: Rich Presence Update
            // FR: Mise à jour Rich Presence
            var richPresenceMatch = RichPresencePattern.Match(line);
            if (richPresenceMatch.Success)
            {
                var rpText = richPresenceMatch.Groups[1].Value.Trim();
                _richPresenceUpdateCount++;

                if (_richPresenceUpdateCount < 3)
                {
                    // EN: Skip initial updates (Loading, etc.)
                    // FR: Sauter les mises à jour initiales (Chargement, etc.)
                    _logger.LogInformation($"[RA Service] RP Update #{_richPresenceUpdateCount} [SKIPPED]: {rpText}");
                    return; 
                }
                else
                {
                     // _logger.LogInformation($"[RA Service] Rich Presence Updated: {rpText}"); // Verbose
                }

                RichPresenceUpdated?.Invoke(this, rpText);
                return;
            }
        }

        /// <summary>
        /// EN: Reset the current game state
        /// FR: Réinitialiser l'état du jeu actuel
        /// </summary>
        public void ResetState(string? emulatorHint = null)
        {
            _logger.LogInformation($"[RA Service] Resetting state... (EmulatorHint: {emulatorHint ?? "None"})");
            bool wasRunning = _currentGameId.HasValue;
            
            // EN: Save ID before resetting for unlock preservation logic
            // FR: Sauvegarder l'ID avant réinitialisation pour la logique de préservation des déblocages
            int? prevGameId = _currentGameId;
            
            _currentGameId = null;
            _currentProgress = null;
            _isHardcoreMode = CheckHardcoreSettings(emulatorHint);
            _richPresenceUpdateCount = 0;

            lock (_achievementLock)
            {
                // EN: Backup session unlocks before clearing, in case this is a false unload (re-load of same game)
                // FR: Sauvegarder les déblocages de session avant effacement, au cas où c'est un faux déchargement (rechargement du même jeu)
                if (wasRunning && prevGameId.HasValue && _unlockedInSession.Count > 0)
                {
                    _preservedSessionUnlocks[prevGameId.Value] = new HashSet<int>(_unlockedInSession);
                    _logger.LogInformation($"[RA Service] Preserved {_unlockedInSession.Count} session unlocks for game {prevGameId.Value}");
                }
                
                _unlockedInSession.Clear();
            }

            if (wasRunning)
            {
                GameStopped?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// EN: Check if Hardcore Mode is enabled in emulator configs (RetroArch/DuckStation)
        /// FR: Vérifier si le mode Hardcore est activé dans les configs émulateurs (RetroArch/DuckStation)
        /// </summary>
        private bool CheckHardcoreSettings(string? emulatorHint = null)
        {
            try
            {
                // 1. Check RetroArch
                if (emulatorHint == null || emulatorHint.Equals("retroarch", StringComparison.OrdinalIgnoreCase))
                {
                    var raCfg = Path.Combine(_config.RetroBatPath, "emulators", "retroarch", "retroarch.cfg");
                    if (File.Exists(raCfg))
                    {
                        // EN: Quick check for the line
                        // FR: Vérification rapide de la ligne
                        foreach (var line in File.ReadLines(raCfg))
                        {
                            if (line.Contains("cheevos_hardcore_mode_enable = \"true\"")) return true;
                        }
                    }
                }

                // 2. Check DuckStation
                if (emulatorHint == null || emulatorHint.Equals("duckstation", StringComparison.OrdinalIgnoreCase))
                {
                    var dsCfg = _config.DuckStationSettingsPath;
                if (!string.IsNullOrEmpty(dsCfg) && File.Exists(dsCfg))
                {
                    bool inAchievements = false;
                    foreach (var line in File.ReadLines(dsCfg))
                    {
                        var trimmed = line.Trim();
                        if (trimmed == "[Achievements]") inAchievements = true;
                        else if (trimmed.StartsWith("[")) inAchievements = false;

                        if (inAchievements && trimmed == "HardcoreMode = true") return true;
                    }
                }
                
                } // End of DuckStation check
                
                // 3. PCSX2: Removed incorrect config check. Relies on "Game loaded, hardcore enabled" log line.

            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RA Service] Failed to pre-check Hardcore settings: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// EN: Load user profile from API
        /// FR: Charger profil utilisateur depuis l'API
        /// </summary>
        private async Task LoadUserProfileAsync(string username)
        {
            try
            {
                var profile = await _apiClient.GetUserProfileAsync(username);
                if (profile != null)
                {
                    _logger.LogInformation($"[RA Service] Loaded profile for {username}: {profile.TotalPoints} points");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] Error loading user profile: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Load game data and user progress
        /// FR: Charger données jeu et progression utilisateur
        /// </summary>
        private async Task LoadGameDataAsync(int gameId, string? emulatorHint = null)
        {
            // EN: Prevent multiple concurrent loads for the same game
            // FR: Empêcher plusieurs chargements simultanés pour le même jeu
            if (_loadingGameId == gameId) return;
            
            // EN: If already loaded, skip
            // FR: Si déjà chargé, ignorer
            if (_currentGameId == gameId && _currentProgress != null) return;

            try
            {
                _loadingGameId = gameId;

                // EN: Reset state before loading new data to ensure no stale data remains if API fails
                // FR: Réinitialiser l'état avant de charger de nouvelles données pour s'assurer qu'aucune donnée obsolète ne reste si l'API échoue
                ResetState(emulatorHint);
                _currentGameId = gameId; // Restore ID for current load attempt

                if (_currentUsername == null)
                {
                    _logger.LogWarning("[RA Service] Cannot load game data: no user logged in");
                    return;
                }

                var gameInfo = await _apiClient.GetGameInfoAsync(gameId);
                var progress = await _apiClient.GetUserProgressAsync(gameId, _currentUsername);

                _currentProgress = progress;

                // EN: Check emulator caches for game icon before relying on API downloaded one
                // FR: Vérifier les caches des émulateurs pour l'icône du jeu avant de se fier à celle de l'API
                if (gameInfo != null)
                {
                    var emulatorIcon = ResolveGameIconPath(gameId, gameInfo.ImageIcon);
                    if (!string.IsNullOrEmpty(emulatorIcon) && File.Exists(emulatorIcon))
                    {
                        _logger.LogInformation($"[RA Service] Using game icon from emulator cache: {emulatorIcon}");
                        gameInfo.ImageIcon = emulatorIcon;
                        if (progress?.GameInfo != null) progress.GameInfo.ImageIcon = emulatorIcon;
                    }
                }

                _logger.LogInformation($"[RA Service] Loaded game: {gameInfo?.Title ?? $"ID {gameId}"} - {progress?.UserCompletion ?? "0%"} complete");

                GameStarted?.Invoke(this, new GameStartedEventArgs
                {
                    GameId = gameId,
                    GameInfo = gameInfo,
                    UserProgress = progress,
                    IsHardcore = _isHardcoreMode
                });
                
                // EN: Restore preserved session unlocks if any (Fix for Score Revert bug)
                // FR: Restaurer les déblocages de session préservés s'il y en a (Correctif pour le bug de retour en arrière du score)
                if (_preservedSessionUnlocks.ContainsKey(gameId))
                {
                    lock (_achievementLock)
                    {
                        var preserved = _preservedSessionUnlocks[gameId];
                        _logger.LogInformation($"[RA Service] Restoring {preserved.Count} preserved local unlocks for game {gameId}");
                        
                        foreach (var achId in preserved)
                        {
                            _unlockedInSession.Add(achId);
                            if (progress?.Achievements != null && progress.Achievements.TryGetValue(achId.ToString(), out var ach))
                            {
                                if (!ach.Unlocked)
                                {
                                    ach.Unlocked = true;
                                    if (_isHardcoreMode) ach.DateEarnedHardcore = DateTime.Now;
                                    else ach.DateEarned = DateTime.Now;
                                }
                            }
                        }
                        // Clean up
                        _preservedSessionUnlocks.Remove(gameId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Service] Error loading game data: {ex.Message}");
            }
            finally
            {
                if (_loadingGameId == gameId) _loadingGameId = null;
            }
        }

        /// <summary>
        /// EN: Handle achievement unlocked event
        /// FR: Gérer événement succès débloqué
        /// </summary>
        private async Task HandleAchievementUnlockedAsync(int achievementId)
        {
            await Task.Yield();
            if (_currentGameId == null || _currentUsername == null) return;

            lock (_achievementLock)
            {
                if (_unlockedInSession.Contains(achievementId))
                {
                    _logger.LogInformation($"[RA Service] Achievement {achievementId} already processed in this session. Skipping.");
                    return;
                }
                _unlockedInSession.Add(achievementId);

                bool isNew = false; // EN: Track if this is a fresh unlock / FR: Suivre si c'est un nouveau déblocage
                try
                {
                    // EN: Use local cache to prevent API race conditions and lag
                    // FR: Utiliser le cache local pour éviter les conditions de course et le lag de l'API
                    // _currentProgress = await _apiClient.GetUserProgressAsync(_currentGameId.Value, _currentUsername);

                    Achievement? achievement = null;
                    if (_currentProgress?.Achievements != null)
                    {
                        _currentProgress.Achievements.TryGetValue(achievementId.ToString(), out achievement);
                    }

                    if (achievement == null)
                    {
                        _logger.LogInformation($"[RA Service] Achievement {achievementId} not in API data (unofficial) - creating fallback");
                        // We do the async work outside the lock later if needed, but for now fallback is fast
                        achievement = new Achievement
                        {
                            ID = achievementId,
                            Title = $"Achievement #{achievementId}",
                            Description = "Unlocked achievement (unofficial)",
                            Points = 0,
                            Unlocked = true
                        };
                        isNew = true; // EN: Unofficial ones are treated as new for the session / FR: Les non-officiels sont traités comme nouveaux pour la session
                    }
                    else
                    {
                        _logger.LogInformation($"[RA Service] Achievement {achievementId} found in API data (official)");
                        
                        // EN: Only mark as new if it wasn't already unlocked in the account
                        // FR: Marquer comme nouveau seulement s'il n'était pas déjà débloqué sur le compte
                        bool alreadyAwardedHc = achievement.DateEarnedHardcore.HasValue;
                        bool alreadyAwardedSc = achievement.DateEarned.HasValue;

                        if (!achievement.Unlocked)
                        {
                            _logger.LogInformation($"[RA Service] Achievement {achievementId} was locked in API, unlocking locally.");
                            achievement.Unlocked = true;
                            isNew = true;
                            if (_isHardcoreMode) achievement.DateEarnedHardcore = DateTime.Now;
                            else achievement.DateEarned = DateTime.Now;
                        }
                        else if (_isHardcoreMode && !alreadyAwardedHc)
                        {
                            // EN: Hardcore Upgrade! / FR: Nouveau déblocage Hardcore !
                            _logger.LogInformation($"[RA Service] Achievement {achievementId} was previously earned in SC, now earned in HC. Triggering refresh.");
                            achievement.DateEarnedHardcore = DateTime.Now;
                            isNew = true; // Trigger refresh for golden border
                        }
                        else
                        {
                            _logger.LogInformation($"[RA Service] Achievement {achievementId} already unlocked in API (Encore/Earned previously), re-triggering for display only.");
                            isNew = false;
                        }
                    }

                    _logger.LogInformation($"[RA Service] Achievement unlocked: {achievement.Title} ({achievement.Points} pts) [IsNew: {isNew}]");

                    // EN: Badge resolution can happen here, but better if we don't hold the lock too long
                    // FR: Résolution du badge ici, mais mieux de ne pas tenir le verrou trop longtemps
                    _ = Task.Run(async () =>
                    {
                        if (achievement != null)
                        {
                            var badgePath = await ResolveBadgePathAsync(achievement.ID, _currentGameId);
                            if (!string.IsNullOrEmpty(badgePath)) achievement.BadgeName = badgePath;

                            AchievementUnlocked?.Invoke(this, new AchievementUnlockedEventArgs
                            {
                                Achievement = achievement,
                                GameId = _currentGameId.Value,
                                GameTitle = _currentProgress?.GameInfo?.Title ?? "Unknown Game",
                                IsNewUnlock = isNew,
                                IsHardcore = _isHardcoreMode
                            });
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA Service] Error handling achievement unlock: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// EN: Resolve path for a badge, checking emulator caches then local cache then API
        /// FR: Résoudre le chemin d'un badge, vérifiant caches émulateurs puis cache local puis API
        /// </summary>
        private async Task<string?> ResolveBadgePathAsync(int badgeId, int? gameId)
        {
            // 1.1 - RetroArch Cache
            var retroArchBadgesDir = Path.Combine(_config.RetroBatPath, "emulators", "retroarch", "thumbnails", "cheevos", "badges");
            if (gameId.HasValue)
            {
                var gameBadgesDir = Path.Combine(retroArchBadgesDir, gameId.Value.ToString());
                var badgePath = Path.Combine(gameBadgesDir, $"{badgeId}.png");
                if (File.Exists(badgePath)) return badgePath;
            }
            
            // 1.2 - PCSX2 Cache
            var pcsx2CacheDir = _config.Pcsx2BadgeCachePath;
            if (!string.IsNullOrEmpty(pcsx2CacheDir) && Directory.Exists(pcsx2CacheDir))
            {
                var pcsx2BadgePath = Path.Combine(pcsx2CacheDir, $"{badgeId}.png");
                if (File.Exists(pcsx2BadgePath)) return pcsx2BadgePath;
            }
            
            // 2 - Local App Cache
            var localCacheDir = gameId.HasValue
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", "retroachievements", "badges", gameId.Value.ToString())
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", "retroachievements", "badges");
            
            var localBadgePath = Path.Combine(localCacheDir, $"{badgeId}.png");
            if (File.Exists(localBadgePath)) return localBadgePath;

            // 3 - Download from RA API (Method RetroArch style download fallback)
            _logger.LogInformation($"[RA Badge] Not found locally, downloading: {badgeId}");
            try
            {
                Directory.CreateDirectory(localCacheDir);
                var badgeUrl = $"https://media.retroachievements.org/Badge/{badgeId}.png";
                
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                var response = await httpClient.GetAsync(badgeUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var badgeData = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(localBadgePath, badgeData);
                    return localBadgePath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Badge] Download failed for {badgeId}: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// EN: Resolve path for game icon, checking emulator caches
        /// FR: Résoudre le chemin de l'icône du jeu, vérifiant les caches des émulateurs
        /// </summary>
        private string? ResolveGameIconPath(int gameId, string? fallbackPath)
        {
            // 1. PCSX2 Cache (game_{id}.png)
            var pcsx2CacheDir = _config.Pcsx2BadgeCachePath;
            if (!string.IsNullOrEmpty(pcsx2CacheDir) && Directory.Exists(pcsx2CacheDir))
            {
                var pcsx2IconPath = Path.Combine(pcsx2CacheDir, $"game_{gameId}.png");
                if (File.Exists(pcsx2IconPath)) return pcsx2IconPath;
            }

            // 2. RetroArch Cache
            var raIconPath = Path.Combine(_config.RetroBatPath, "emulators", "retroarch", "thumbnails", "cheevos", "badges", $"{gameId}.png");
            if (File.Exists(raIconPath)) return raIconPath;

            // 3. Fallback to what we already have (App Cache or API)
            return fallbackPath;
        }

        /// <summary>
        /// EN: Get path to badge image (.png)
        /// FR: Obtenir le chemin vers l'image de badge (.png)
        /// </summary>
        public async Task<string?> GetBadgePath(int gameId, int achievementId)
        {
            int badgeId = achievementId; 
            if (_currentProgress?.Achievements != null && _currentProgress.Achievements.TryGetValue(achievementId.ToString(), out var achievement))
            {
                var badgeName = Path.GetFileNameWithoutExtension(achievement.BadgeName);
                if (int.TryParse(badgeName, out var parsedBadgeId)) badgeId = parsedBadgeId;
            }

            return await ResolveBadgePathAsync(badgeId, gameId);
        }

        /// <summary>
        /// EN: Get path to locked badge image (_lock.png)
        /// FR: Obtenir le chemin vers l'image de badge verrouillé (_lock.png)
        /// </summary>
        public async Task<string?> GetBadgeLockPath(int gameId, int achievementId)
        {
            // EN: Generate from normal badge (grayscale + darkened)
            try
            {
                var normalBadgePath = await GetBadgePath(gameId, achievementId);
                if (string.IsNullOrEmpty(normalBadgePath)) return null;

                if (_imageService == null)
                {
                    _logger.LogWarning($"[RA Badge] ImageConversionService not initialized, cannot generate lock badge");
                    return null;
                }

                return _imageService.GenerateBadgeLockFromNormal(normalBadgePath, gameId, achievementId);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Badge] Error generating lock badge {achievementId}: {ex.Message}");
                return null;
            }
        }

        public void SetImageConversionService(ImageConversionService imageService) => _imageService = imageService;

        public void Dispose()
        {
            _pollingTimer?.Dispose();
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }
}
