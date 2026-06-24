using System.Threading.Tasks;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;

namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IConfigService
    {
        // Core Paths
        string RetroBatPath { get; }
        string RomsPath { get; }
        string IMPath { get; }
        string ConfigPath { get; }
        
        // Marquee Paths
        string MarqueeImagePath { get; }
        string MarqueeImagePathDefault { get; }
        string CachePath { get; }
        string DefaultImagePath { get; }
        string DefaultDmdPath { get; }
        string DefaultFanartPath { get; }
        string Pcsx2LogPath { get; }
        string Pcsx2BadgeCachePath { get; }

        string DuckStationLogPath { get; }
        string DuckStationSettingsPath { get; }
        
        // Templates configurables (EN/FR: Configurable templates)
        string MarqueeFilePath { get; } // Template: {system_name}\{game_name}
        string MarqueeFilePathDefault { get; } // Template: {system_name}\images\{game_name}-marquee
        string SystemFilePath { get; } // Template: {system_name}
        string CollectionFilePath { get; } // Template: auto-{collection_name}
        
        // Chemins des thèmes (EN/FR: Theme paths)
        string SystemMarqueePath { get; } // Path to system logos in theme
        string SystemCustomMarqueePath { get; } // Custom user path for system logos
        string SystemCustomDMDPath { get; } // Custom user path for system DMD logos
        string CollectionMarqueePath { get; } // Path to collection logos in theme
        
        string GameCustomMarqueePath { get; } // Custom user path for game marquees (toppers etc)
        
        bool MarqueeAutoGeneration { get; }
        bool MarqueeVideoGeneration { get; }
        string GenerateMarqueeVideoFolder { get; }
        string FfmpegHwEncoding { get; }
        bool MarqueeCompose { get; }
        bool MarqueeAutoScraping { get; }
        string MPVScrapMediaType { get; }
        string DMDScrapMediaType { get; }
        bool MarqueeGlobalScraping { get; }
        // Scraper Priority Manager
        List<string> ScraperPriorities { get; } // [ScrapersSource] PrioritySource=...
        // ArcadeItalia Settings
        string ArcadeItaliaUrl { get; }
        string ArcadeItaliaMediaType { get; }

        string ScreenScraperUser { get; }
        string ScreenScraperPass { get; }
        string ScreenScraperDevId { get; }
        string ScreenScraperDevPassword { get; }
        string ScreenScraperCachePath { get; }
        int ScreenScraperThreads { get; }
        int ScreenScraperQueueLimit { get; } // EN: Max games in download queue / FR: Nombre max de jeux en file d'attente
        int ScreenScraperQueueKeep { get; } // EN: Games to keep when pruning queue / FR: Jeux à conserver lors de l'élagage
        bool MarqueeAutoConvert { get; }
        
        // Logging
        bool LogToFile { get; }
        string LogFilePath { get; }
        string[] AcceptedFormats { get; }
        
        // Styling / Properties
        string MarqueeBackgroundColor { get; } // Guaranteed Hex
        int MarqueeWidth { get; }
        int MarqueeHeight { get; }
        int MarqueeBorder { get; }
        
        int MarqueeScreen { get; }
        int TopperScreen { get; }
        int DmdScreen { get; }
        int IcCardScreen { get; }
        int LcdScreen { get; }
        
        // Commands (Templates)
        string IMConvertCommand { get; }
        string IMConvertCommandSVG { get; }
        string IMConvertCommandMarqueeGen { get; } // Composition
        
        // Game-Start Custom Media (EN: Custom media path for game-start toppers / FR: Chemin custom pour médias topper au lancement)
        string GameStartMediaPath { get; }
        
        // Composition Settings
        string ComposeMedia { get; } // "fanart" or "image"
        string MarqueeLayout { get; } // "standard", "gradient-left", "gradient-right"
        
        // RetroAchievements Settings
        bool MpvRetroAchievementsNotifications { get; }
        bool DmdRetroAchievementsNotifications { get; }
        string? RetroAchievementsWebApiKey { get; }
        string MarqueeRetroAchievementsOverlays { get; }
        string MpvRetroAchievementsOverlays { get; }
        string DmdRetroAchievementsOverlays { get; }
        string MarqueeRetroAchievementsDisplayTarget { get; } // EN: Target for RA display (dmd, mpv, both)
        string RAFontFamily { get; }
        string OverlayTemplatePath { get; }
        
        // DOF / MAME .lay Settings
        bool LayEnabled { get; }
        bool LayLcdEnabled { get; }
        bool LayDmdEnabled { get; }
        string LayDofPath { get; }

        // DMD Settings
        bool DmdEnabled { get; }
        bool DmdCompose { get; }
        string DmdModel { get; } // e.g. virtual, pin2dmd, zedmd
        string DmdExePath { get; } // Path to dmdext.exe
        string DmdFormat { get; } // e.g. rgb24, gray2...
        string DmdMediaPath { get; }
        string DmdGameStartMediaPath { get; } // Custom path for DMD loading screen
        string DmdArguments { get; }
        int DmdWidth { get; }
        int DmdHeight { get; }
        double DmdDotSize { get; } // Virtual DMD pixel scaling factor

        string IMConvertCommandMarqueeGenLogo { get; } // Logo overlay
        
        string MpvCustomCommand { get; }
        
        // System / Collection Configs
        Dictionary<string, string> CollectionCorrelation { get; }
        Dictionary<string, string> SystemAliases { get; }
        string GetSetting(string key, string defaultValue = "");

    }

    public interface IProcessService
    {
        Task RunProcessAsync(string fileName, string arguments, string? workingDirectory = null);
        void StartProcess(string fileName, string arguments, string? workingDirectory = null, bool waitForExit = true);
        void StartProcess(string fileName, IEnumerable<string> arguments, string? workingDirectory = null, bool waitForExit = true);
        void StartProcessWithLogging(string fileName, string arguments, string? workingDirectory = null);
        void StartProcessWithLogging(string fileName, IEnumerable<string> arguments, string? workingDirectory = null);
        bool IsProcessRunning(string processName);
        void KillProcess(string processName);
        bool FocusProcess(string processName);
    }

    public interface IMarqueeFileFinder
    {
        Task InitializeAsync(string esSettingsPath);
        Task<string?> FindMarqueeFileAsync(string eventType, string param1, string param2, string param3, string param4);
        Task<string?> FindDmdImageAsync(string system, string gameName, string romFileName, string romPath = "", bool allowVideo = true, bool allowScraping = true);
        string? FindDmdGameStartMedia(string system, string romFileName, string gameName);
        string? FindGameStartMedia(string system, string romFileName, string gameName);
        
        // Composition Refresh
        Task<string?> RefreshCompositionAsync(int dx, int dy, bool isLogo);
        Task<string?> RefreshScaleAsync(double delta, bool isLogo);
        
        // Video Offset Preview - Find game logo for composition
        string? FindGameLogo(string system, string gameName, string romFileName, bool raw = true);

        // Game Over
        string? FindGameOverMarquee(bool isPreview = false);
        string? FindDmdGameOverMarquee();
    }

    public interface IMarqueeWorkFlow
    {
        Task ProcessEventAsync(string eventName, string param1, string param2, string param3, string param4, System.Threading.CancellationToken token = default); 
    }

    public interface IDmdService
    {
        bool IsSequencePlaying { get; }
        Task InitializeAsync();
        Task PlayAsync(string mediaPath, string? system = null, string? gameName = null);
        Task SetOverlayAsync(string imagePath, int durationMs = 5000);
        Task PlayAchievementSequenceAsync(string cupPath, string achOverlayPath, int totalDurationMs = 10000);
        Task PlayRichPresenceNotificationAsync(string text, int durationMs = 5000, int? yOverride = null, int? heightOverride = null, string? textColor = null, float? fontSize = null); // EN: Play RP notification (scrolling) / FR: Jouer notification RP (défilement)
        Task PlayDmdStaticNotificationAsync(string text, int durationMs = 3000); // EN: Play static RP notification / FR: Jouer notification RP statique
        Task SetDmdPersistentScoreAsync(string scoreText); // EN: Set a persistent score overlay / FR: Définir un score permanent
        Task SetDmdPersistentLayoutAsync(byte[] layoutBytes); // EN: Set a persistent full layout overlay / FR: Définir un overlay complet permanent
        Task PlayChallengeNotificationAsync(ChallengeState state, bool isHardcore = false, string? ribbonPath = null); // EN: Play Challenge Overlay / FR: Jouer Overlay Défi
        Task PlayFullPreviewAsync(); // EN: Play a full preview of all templates / FR: Jouer une prévisualisation complète de tous les templates
        Task<(bool handled, bool suspendMPV)> CheckAndRunPinballAsync(string system, string gameName);
        Task SetPriorityOverlayAsync(string imagePath, int durationMs);
        void ClearOverlay(); // EN: Clear active overlay / FR: Effacer overlay actif
        void Stop();
        string PrepareConfig(); // Generates DmdDevice.ini if needed
        Task WaitForExternalReleaseAsync(int timeoutMs = 2000); // Wait for external control to finish
    }

    public interface IOverlayTemplateService
    {
        OverlayLayout GetLayout();
        OverlayLayout GetDefaultLayout();
        void Reload();
        void SaveLayout(OverlayLayout layout);
        OverlayItem? GetItem(string screenType, string overlayType); // screenType: "dmd" or "mpv"
    }
}
