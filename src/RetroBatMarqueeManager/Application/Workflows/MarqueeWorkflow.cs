using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Application.Services;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Application.Workflows
{
    public class MarqueeWorkflow : IMarqueeWorkFlow, IDisposable
    {
        private readonly IConfigService _config;
        private readonly ImageConversionService _imageService;
        private readonly Infrastructure.Processes.MarqueeController _mpv;
        private readonly IMarqueeFileFinder _marqueeFinder;
        private readonly IDmdService _dmdService;
        private readonly ILogger<MarqueeWorkflow> _logger;
        private readonly ConcurrentQueue<string> _eventQueue = new();
        
        // State
        private string? _lastSelectedSystem;
        private bool _gameRunning = false; // True while game is running
        private DateTime _ignoreEventsUntil = DateTime.MinValue; // Ignore game-selected events until this time
        private string? _currentGameSystem; // System of currently running game
        private string? _currentGameName; // Name of currently running game
        private string? _currentGameRom; // ROM path of currently running game
        private bool _mpvSuspendedForPinball = false; // EN: True if MPV was stopped for pinball / FR: Vrai si MPV a Ã©tÃ© arrÃªtÃ© pour pinball
        private readonly IOverlayTemplateService _templateService;

        private readonly IInputService _inputService;
        private readonly IProcessService _processService;
        
        // EN: Video Offset Management (new services)
        // FR: Gestion des offsets vidÃ©o (nouveaux services)
        private readonly VideoMarqueeService _videoService;
        private readonly VideoOffsetStorageService _videoOffsetStorage;
        private readonly RetroBatMarqueeManager.Core.Interfaces.IRetroAchievementsService? _raService = null; // RA events consumed from APIExpose WS
        
        // EN: Video adjustment mode state
        // FR: Ã‰tat du mode ajustement vidÃ©o
        private bool _isVideoAdjustmentMode = false;
        private string? _currentVideoPath = null;
        private string? _currentVideoPreviewFrame = null;
        private VideoOffsetData _currentVideoOffsets = new VideoOffsetData();
        private string? _lastPlayedVideoPath = null; // EN: Track last played video to avoid reloading in loop / FR: Suivre derniÃ¨re vidÃ©o pour Ã©viter rechargement en boucle
        private CancellationTokenSource? _badgeCycleCts; // EN: For DMD badge cycling / FR: Pour cycle badges DMD
        private CancellationTokenSource? _mpvBadgeCycleCts; // EN: For MPV badge cycling / FR: Pour cycle badges MPV
        private CancellationTokenSource? _dmdChallengeCycleCts; // EN: For DMD challenge cycling / FR: Pour cycle challenges DMD
        private CancellationTokenSource? _dmdRpStatRotationCts; // EN: For DMD RP stat rotation / FR: Pour rotation stats RP DMD
        private int _dmdRpStatIndex = 0; // EN: Current index for RP stat rotation / FR: Index actuel pour rotation stats RP
        private Dictionary<string, string> _currentDmdRpStats = new(); // EN: Stored stats for rotation / FR: Stats stockÃ©es pour la rotation
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _challengeRefreshCts = new(); // EN: For real-time timer refresh / FR: Pour rafraÃ®chissement temps rÃ©el timer
        private ConcurrentDictionary<int, ChallengeState> _activeChallenges = new(); // EN: Storage for all active challenges / FR: Stockage pour tous les dÃ©fis actifs (Now ConcurrentDictionary)
        private CancellationTokenSource? _mpvChallengeCycleCts; // EN: For MPV challenge cycling / FR: Pour cycle challenges MPV        
        private CancellationTokenSource? _narrationCts;
        private Guid _currentNarrationId = Guid.Empty;
        private string? _activeNarrationPath;
        private string? _activeNarrationPosition;
        // EN: For MPV narration timeout / FR: Pour timeout narration MPV
        // Active Media Paths
        private string? _currentMarqueePath; 
        private string? _currentDmdPath;
        private string? _currentDmdRibbonPath; // EN: Track last DMD ribbon for composition / FR: Suivre le dernier ruban DMD pour composition

        public MarqueeWorkflow(
            IConfigService config,
            ImageConversionService imageService,
            Infrastructure.Processes.MarqueeController mpv,
            IMarqueeFileFinder marqueeFinder,
            IDmdService dmdService,
            IInputService inputService,
            IProcessService processService,
            VideoMarqueeService videoService,
            VideoOffsetStorageService videoOffsetStorage,
            IOverlayTemplateService templateService,
            ILogger<MarqueeWorkflow> logger)
        {
            _config = config;
            _imageService = imageService;
            _mpv = mpv;
            _marqueeFinder = marqueeFinder;
            _dmdService = dmdService;
            _inputService = inputService;
            _processService = processService;
            _videoService = videoService;
            _videoOffsetStorage = videoOffsetStorage;
            _templateService = templateService;
            _logger = logger;
            
            _inputService.OnMoveCommand += HandleMoveCommand;
            _inputService.OnScaleCommand += HandleScaleCommand;
            _inputService.OnVideoAdjustmentMode += HandleVideoAdjustmentMode;
            _inputService.OnTogglePlayback += HandleTogglePlayback;
            _inputService.OnTrimStart += () => HandleTrimCommand(true);
            _inputService.OnTrimEnd += () => HandleTrimCommand(false);
            
            // EN: Subscribe to RetroAchievements events / FR: S'abonner aux Ã©vÃ©nements RetroAchievements
            if (_raService != null)
            {
                _raService.AchievementUnlocked += OnAchievementUnlocked;
                _raService.GameStarted += OnGameStarted;
                _raService.HardcoreStatusChanged += OnHardcoreStatusChanged;
                // EN: Subscribe to Rich Presence
                _raService.RichPresenceUpdated += OnRichPresenceUpdated;
                _raService.ChallengeUpdated += OnChallengeUpdated;
                _raService.AchievementDetected += OnAchievementDetected; // EN: Subscribe to immediate detection / FR: S'abonner Ã  la dÃ©tection immÃ©diate
                _raService.SetImageConversionService(_imageService); // EN: For grayscale badge generation / FR: Pour gÃ©nÃ©ration badge niveaux de gris
                _logger.LogInformation("[RA Workflow] Subscribed to RetroAchievements events");
            }
        }

        private bool _isProcessingAchievement = false; // EN: Flag to suppress RP during achievement sequences / FR: Flag pour supprimer RP pendant sÃ©quences succÃ¨s
        private int _activeAchievementCount = 0; // EN: Track number of achievements being processed / FR: Suivre le nombre de succÃ¨s en cours de traitement
        private readonly SemaphoreSlim _dmdNotificationSemaphore = new(1, 1); // EN: Serialize DMD achievment overlays / FR: SÃ©rialiser les overlays de succÃ¨s DMD

        private void OnAchievementDetected(object? sender, EventArgs e)
        {
            Interlocked.Increment(ref _activeAchievementCount);
            if (!_isProcessingAchievement)
            {
                _logger.LogInformation($"[RA Workflow] Achievement detected! (Active: {_activeAchievementCount}). Suppressing RP updates.");
                _isProcessingAchievement = true;
                
                // EN: Safety timeout to reset flag in case event/sequence fails
                // FR: Timeout de sÃ©curitÃ© pour reset le flag si l'Ã©vÃ©nement/la sÃ©quence Ã©choue
                _ = Task.Delay(20000).ContinueWith(_ => 
                {
                    if (_isProcessingAchievement && _activeAchievementCount == 0)
                    {
                        _isProcessingAchievement = false;
                        _logger.LogWarning("[RA Workflow] Achievement processing flag timed out - RP restored.");
                    }
                });
            }
        }

        // Throttling State
        private bool _isRendering = false;
        private int _pendingFanartX = 0;
        private int _pendingFanartY = 0;
        private int _pendingLogoX = 0;
        private int _pendingLogoY = 0;
        private double _pendingFanartScale = 0.0;
        private double _pendingLogoScale = 0.0;
        private bool _isVideoPlaybackMode = false; // EN: True if playing source video for trimming / FR: Vrai si lecture vidÃ©o source pour dÃ©coupe
        private RichPresenceState? _lastRpState; // EN: Track last RP state / FR: Suivre dernier Ã©tat RP
        private DateTime _lastDmdRpUpdate = DateTime.MinValue; // EN: Debounce for DMD RP / FR: Limitation frÃ©quence DMD RP
        private DateTime _dmdRpDisplayExpiry = DateTime.MinValue; // EN: Expiry for showing RP on DMD / FR: Expiration de l'affichage RP sur DMD
        private string _lastDmdText = string.Empty; // EN: Track last text sent to DMD / FR: Suivre dernier texte envoyÃ© au DMD

        private void HandleMoveCommand(int dx, int dy, bool isLogo)
        {
            // EN: Check if in video adjustment mode
            // FR: VÃ©rifier si en mode ajustement vidÃ©o
            if (_isVideoAdjustmentMode)
            {
                HandleVideoMove(dx, dy, isLogo);
                return;
            }
            
            // Accumulate input deltas (Image Composition Mode)
            if (isLogo)
            {
                Interlocked.Add(ref _pendingLogoX, dx);
                Interlocked.Add(ref _pendingLogoY, dy);
            }
            else
            {
                Interlocked.Add(ref _pendingFanartX, dx);
                Interlocked.Add(ref _pendingFanartY, dy);
            }

            // Trigger Render Loop (Fire and Forget)
            _ = TriggerRender();
        }

        private void HandleScaleCommand(double delta, bool isLogo)
        {
            // EN: Check if in video adjustment mode
            // FR: VÃ©rifier si en mode ajustement vidÃ©o
            if (_isVideoAdjustmentMode)
            {
                HandleVideoScale(delta, isLogo);
                return;
            }
            
            // Accumulate scale deltas (Image Composition Mode)
            if (isLogo)
            {
                lock (this) { _pendingLogoScale += delta; }
            }
            else
            {
                lock (this) { _pendingFanartScale += delta; }
            }

            // Trigger Render Loop (Fire and Forget)
            _ = TriggerRender();
        }

        private async Task TriggerRender()
        {
            // If already rendering, the loop will pick up the new accumulated values
            if (_isRendering) return;
            
            _isRendering = true;

            try
            {
                while (_pendingFanartX != 0 || _pendingFanartY != 0 || _pendingLogoX != 0 || _pendingLogoY != 0 || _pendingFanartScale != 0.0 || _pendingLogoScale != 0.0)
                {
                    // Capture current snapshot
                    int fDx = Interlocked.Exchange(ref _pendingFanartX, 0);
                    int fDy = Interlocked.Exchange(ref _pendingFanartY, 0);
                    int lDx = Interlocked.Exchange(ref _pendingLogoX, 0);
                    int lDy = Interlocked.Exchange(ref _pendingLogoY, 0);
                    double fScale, lScale;
                    lock (this)
                    {
                        fScale = _pendingFanartScale;
                        lScale = _pendingLogoScale;
                        _pendingFanartScale = 0.0;
                        _pendingLogoScale = 0.0;
                    }

                    string? newPath = null;

                    // Apply Fanart Scale
                    if (fScale != 0.0)
                    {
                        newPath = await _marqueeFinder.RefreshScaleAsync(fScale, false);
                    }

                    // Apply Logo Scale
                    if (lScale != 0.0)
                    {
                        newPath = await _marqueeFinder.RefreshScaleAsync(lScale, true) ?? newPath;
                    }

                    // Apply Fanart Updates
                    if (fDx != 0 || fDy != 0)
                    {
                        newPath = await _marqueeFinder.RefreshCompositionAsync(fDx, fDy, false) ?? newPath;
                    }

                    // Apply Logo Updates (might override newPath, but result is the same final composition)
                    if (lDx != 0 || lDy != 0)
                    {
                        newPath = await _marqueeFinder.RefreshCompositionAsync(lDx, lDy, true);
                    }

                    if (!string.IsNullOrEmpty(newPath))
                    {
                        _currentMarqueePath = newPath; // EN: Update stored path for refresh / FR: Mettre Ã  jour le chemin stockÃ© pour le rafraÃ®chissement
                        await _mpv.DisplayImage(newPath);

                        // EN: Also refresh DMD if Composition is enabled
                        // FR: RafraÃ®chir aussi le DMD si la composition est activÃ©e
                        if (_config.DmdEnabled && _config.DmdCompose && !string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
                        {
                            var romName = !string.IsNullOrEmpty(_currentGameRom) ? Path.GetFileNameWithoutExtension(_currentGameRom) : "";
                            var dmdPath = await _marqueeFinder.FindDmdImageAsync(_currentGameSystem, _currentGameName, romName);
                            if (dmdPath != null) await _dmdService.PlayAsync(dmdPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in Render Loop: {ex.Message}");
            }
            finally
            {
                _isRendering = false;
                
                // Double check if new input arrived while we were exiting
                if (_pendingFanartX != 0 || _pendingFanartY != 0 || _pendingLogoX != 0 || _pendingLogoY != 0)
                {
                     _ = TriggerRender();
                }
            }
        }

        // ========== VIDEO ADJUSTMENT MODE HANDLERS ==========

        /// <summary>
        /// EN: Handle Ctrl+V to enter video adjustment mode
        /// FR: GÃ©rer Ctrl+V pour entrer en mode ajustement vidÃ©o
        /// </summary>
        private async void HandleVideoAdjustmentMode()
        {
            // EN: Can only adjust if game is running and video is being displayed
            // FR: Ajustement possible uniquement si jeu en cours et vidÃ©o affichÃ©e
            if (!_gameRunning || string.IsNullOrEmpty(_currentVideoPath))
            {
                _logger.LogWarning("[VideoAdjustment] Cannot enter video adjustment mode: No game running or video playing");
                return;
            }

            // EN: Verify system and game name are available
            // FR: VÃ©rifier que system et nom du jeu sont disponibles
            if (string.IsNullOrEmpty(_currentGameSystem) || string.IsNullOrEmpty(_currentGameName))
            {
                _logger.LogWarning("[VideoAdjustment] Cannot enter video adjustment mode: Missing system or game name");
                return;
            }

            // EN: Check if current video is a generated marquee video
            // FR: VÃ©rifier si la vidÃ©o actuelle est une marquee gÃ©nÃ©rÃ©e
            if (!_videoService.IsGeneratedVideo(_currentVideoPath))
            {
                _logger.LogWarning($"[VideoAdjustment] Current video is not a generated marquee: {_currentVideoPath}");
                return;
            }

            _logger.LogInformation($"[VideoAdjustment] Entering video adjustment mode for: {_currentVideoPath}");
            _isVideoAdjustmentMode = true;
            _isVideoPlaybackMode = false; // Always start in preview mode
            _lastPlayedVideoPath = null; // Reset playback state

            // EN: Load global offsets as starting point (or initialize with video generation defaults)
            // FR: Charger les offsets globaux comme point de dÃ©part (ou initialiser avec valeurs par dÃ©faut de gÃ©nÃ©ration vidÃ©o)
            _currentVideoOffsets = _videoOffsetStorage.GetGlobalOffsets(_currentGameSystem, _currentGameName);
            
            // EN: IMPORTANT: If logo position is 0,0, set to default 10,10 (video generation default)
            // FR: IMPORTANT: Si position logo est 0,0, dÃ©finir Ã  10,10 par dÃ©faut (dÃ©faut gÃ©nÃ©ration vidÃ©o)
            // This ensures the logo appears in the same position as when the video was originally generated
            if (_currentVideoOffsets.LogoX == 0 && _currentVideoOffsets.LogoY == 0)
            {
                _logger.LogInformation("[VideoAdjustment] Setting logo to default position 10,10");
                _currentVideoOffsets.LogoX = 10;
                _currentVideoOffsets.LogoY = 10;
            }

            // EN: Find SOURCE video (not the generated one) to capture full-size frame
            // FR: Trouver la vidÃ©o SOURCE (pas celle gÃ©nÃ©rÃ©e) pour capturer frame plein format
            // EN: Find SOURCE video (not the generated one) to capture full-size frame
            // FR: Trouver la vidÃ©o SOURCE (pas celle gÃ©nÃ©rÃ©e) pour capturer frame plein format
            var romFileName = Path.GetFileNameWithoutExtension(_currentVideoPath);
            var videoSourcePath = FindSourceVideo(_currentGameSystem, romFileName);
            
            // Try with game name if different from filename (sometimes scraping changes names)
            if (string.IsNullOrEmpty(videoSourcePath) && !string.Equals(romFileName, _currentGameName, StringComparison.OrdinalIgnoreCase))
            {
                 videoSourcePath = FindSourceVideo(_currentGameSystem, _currentGameName);
            }
            
            if (string.IsNullOrEmpty(videoSourcePath))
            {
                _logger.LogWarning($"[VideoAdjustment] Source video not found, using generated video for capture (may be cropped)");
                videoSourcePath = _currentVideoPath;
            }
            else
            {
                _logger.LogInformation($"[VideoAdjustment] Found source video: {videoSourcePath}");
            }

            // EN: Capture frame from SOURCE video for preview
            // FR: Capturer une frame de la vidÃ©o SOURCE pour l'aperÃ§u
            _currentVideoPreviewFrame = _videoService.CaptureVideoFrame(videoSourcePath, 5.0);
            
            if (string.IsNullOrEmpty(_currentVideoPreviewFrame))
            {
                _logger.LogError("[VideoAdjustment] Failed to capture video frame");
                _isVideoAdjustmentMode = false;
                return;
            }

            // EN: Generate preview with logo overlay
            // FR: GÃ©nÃ©rer l'aperÃ§u avec overlay du logo
            await RefreshVideoPreview();

            // EN: Display OSD Helper for Shortcuts
            // FR: Afficher l'aide OSD pour les raccourcis
            var osdCmd = System.Text.Json.JsonSerializer.Serialize(new { 
                command = new[] { 
                    "show-text", 
                    "Video Adjustment (Ctrl+V): Arrows/Zoom = Crop/Logo\nTrimming Mode (Ctrl+P): Ctrl+I (Start), Ctrl+O (End)\nEsc: Quit", 
                    "15000" // Display for 15 seconds
                } 
            });
            await _mpv.SendCommandAsync(osdCmd);
        }

        /// <summary>
        /// EN: Handle move command in video adjustment mode
        /// FR: GÃ©rer la commande de dÃ©placement en mode ajustement vidÃ©o
        /// </summary>
        private async void HandleVideoMove(int dx, int dy, bool isLogo)
        {
            if (isLogo)
            {
                _currentVideoOffsets.LogoX += dx;
                _currentVideoOffsets.LogoY += dy;
                _logger.LogInformation($"[VideoAdjustment] Logo moved: ({_currentVideoOffsets.LogoX}, {_currentVideoOffsets.LogoY})");
            }
            else
            {
                _currentVideoOffsets.CropX += dx;
                _currentVideoOffsets.CropY += dy;
                _logger.LogInformation($"[VideoAdjustment] Crop moved: ({_currentVideoOffsets.CropX}, {_currentVideoOffsets.CropY})");
            }


            // EN: Update global offsets storage
            // FR: Mettre Ã  jour le stockage des offsets globaux
            if (!string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
            {
                _videoOffsetStorage.UpdateGlobalOffsets(_currentGameSystem, _currentGameName, _currentVideoOffsets);
            }

            // EN: Refresh preview
            // FR: RafraÃ®chir l'aperÃ§u
            await RefreshVideoPreview();
        }

        /// <summary>
        /// EN: Handle scale command in video adjustment mode
        /// FR: GÃ©rer la commande de zoom en mode ajustement vidÃ©o
        /// </summary>
        private async void HandleVideoScale(double delta, bool isLogo)
        {
            if (isLogo)
            {
                _currentVideoOffsets.LogoScale = Math.Max(0.1, Math.Min(5.0, _currentVideoOffsets.LogoScale + delta));
                _logger.LogInformation($"[VideoAdjustment] Logo scale: {_currentVideoOffsets.LogoScale:F2}");
            }
            else
            {
                _currentVideoOffsets.Zoom = Math.Max(0.5, Math.Min(3.0, _currentVideoOffsets.Zoom + delta));
                _logger.LogInformation($"[VideoAdjustment] Video zoom: {_currentVideoOffsets.Zoom:F2}");
            }

            // EN: Update global offsets storage
            // FR: Mettre Ã  jour le stockage des offsets globaux
            if (!string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
            {
                _videoOffsetStorage.UpdateGlobalOffsets(_currentGameSystem, _currentGameName, _currentVideoOffsets);
            }

            // EN: Refresh preview
            // FR: RafraÃ®chir l'aperÃ§u
            await RefreshVideoPreview();
        }

        /// <summary>
        /// EN: Refresh video preview with current offsets applied to frame + logo overlay
        /// FR: RafraÃ®chir l'aperÃ§u vidÃ©o avec offsets appliquÃ©s Ã  la frame + overlay logo
        /// </summary>
        private async Task RefreshVideoPreview()
        {
            if (string.IsNullOrEmpty(_currentVideoPreviewFrame) || string.IsNullOrEmpty(_currentGameSystem) || string.IsNullOrEmpty(_currentGameName))
            {
                _logger.LogWarning($"[VideoAdjustment] Cannot refresh preview: Frame={!string.IsNullOrEmpty(_currentVideoPreviewFrame)}, System={!string.IsNullOrEmpty(_currentGameSystem)}, Game={!string.IsNullOrEmpty(_currentGameName)}");
                return;
            }

            // EN: If in Playback Mode, play the SOURCE video directly
            // FR: Si en Mode Lecture, jouer la vidÃ©o SOURCE directement
            if (_isVideoPlaybackMode)
            {
                var romFileName = Path.GetFileNameWithoutExtension(_currentVideoPath) ?? "";
                var videoSourcePath = FindSourceVideo(_currentGameSystem, romFileName);
                
                // Try with game name if different from filename
                if (string.IsNullOrEmpty(videoSourcePath) && !string.Equals(romFileName, _currentGameName, StringComparison.OrdinalIgnoreCase))
                {
                     videoSourcePath = FindSourceVideo(_currentGameSystem, _currentGameName);
                }
                
                if (!string.IsNullOrEmpty(videoSourcePath))
                {
                     // EN: Optimization: If already playing this video, do nothing
                     // FR: Optimisation: Si cette vidÃ©o est dÃ©jÃ  en lecture, ne rien faire
                     if (videoSourcePath == _lastPlayedVideoPath)
                     {
                         return;
                     }

                     // Play source video looping
                     _currentMarqueePath = videoSourcePath; // EN: Update stored path / FR: Mettre Ã  jour le chemin stockÃ©
                     await _mpv.DisplayImage(videoSourcePath, loop: true);
                     _lastPlayedVideoPath = videoSourcePath; // Update state

                     // FR: Activer la barre de progression (OSC) pour visualiser le trimming
                     await _mpv.SendCommandAsync($"{{\"command\":[\"script-message\", \"osc-visibility\", \"always\", \"no-osd\"]}}");
                     await _mpv.SendCommandAsync($"{{\"command\":[\"show-text\", \"Playback Mode - Press I/O to Trim\", 3000]}}");
                     return;
                }
            }
            else
            {
                _lastPlayedVideoPath = null; // Reset state when exiting playback mode
                // FR: DÃ©sactiver l'OSC si on quitte le mode lecture
                await _mpv.SendCommandAsync($"{{\"command\":[\"script-message\", \"osc-visibility\", \"never\", \"no-osd\"]}}");
            }

            _logger.LogInformation($"[VideoAdjustment] RefreshVideoPreview called with offsets: Crop({_currentVideoOffsets.CropX},{_currentVideoOffsets.CropY}), Zoom={_currentVideoOffsets.Zoom:F2}");

            try
            {
                // EN: Find logo using MarqueeFinder's logic (same as all other media)
                // FR: Trouver le logo en utilisant la logique de MarqueeFinder (comme tous les autres mÃ©dias)
                var romFileName = Path.GetFileNameWithoutExtension(_currentVideoPath) ?? "";
                var logoPath = _marqueeFinder.FindGameLogo(_currentGameSystem, _currentGameName, romFileName, raw: true);

                if (string.IsNullOrEmpty(logoPath))
                {
                    _logger.LogWarning($"[VideoAdjustment] Logo not found for {romFileName}, displaying frame only");
                    _currentMarqueePath = _currentVideoPreviewFrame; // EN: Update stored path / FR: Mettre Ã  jour le chemin stockÃ©
                    await _mpv.DisplayImage(_currentVideoPreviewFrame);
                    return;
                }

                _logger.LogInformation($"[VideoAdjustment] Composing: frame={Path.GetFileName(_currentVideoPreviewFrame)}, logo={Path.GetFileName(logoPath)}");

                // EN: Compose frame + logo with current offsets using ImageMagick (same as MarqueeCompose)
                // FR: Composer frame + logo avec offsets actuels via ImageMagick (comme MarqueeCompose)
                var composedPath = _imageService.GenerateComposition(
                    fanartPath: _currentVideoPreviewFrame,
                    logoPath: logoPath,
                    subFolder: "video_preview",
                    offsetX: _currentVideoOffsets.CropX,
                    offsetY: _currentVideoOffsets.CropY,
                    logoOffsetX: _currentVideoOffsets.LogoX,
                    logoOffsetY: _currentVideoOffsets.LogoY,
                    fanartScale: _currentVideoOffsets.Zoom,
                    logoScale: _currentVideoOffsets.LogoScale,
                    isPreview: true
                );

                if (!string.IsNullOrEmpty(composedPath) && File.Exists(composedPath))
                {
                    _currentMarqueePath = composedPath; // EN: Update stored path / FR: Mettre Ã  jour le chemin stockÃ©
                    await _mpv.DisplayImage(composedPath);
                    _logger.LogInformation($"[VideoAdjustment] âœ“ Preview refreshed successfully");
                }
                else
                {
                    _logger.LogWarning("[VideoAdjustment] GenerateComposition returned null/invalid path");
                    await _mpv.DisplayImage(_currentVideoPreviewFrame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoAdjustment] Exception: {ex.Message}");
                _logger.LogError($"[VideoAdjustment] Stack: {ex.StackTrace}");
                await _mpv.DisplayImage(_currentVideoPreviewFrame);
            }
        }

        /// <summary>
        /// EN: Helper to find the original source video for a game
        /// FR: Aide pour trouver la vidÃ©o source originale d'un jeu
        /// </summary>
        private string? FindSourceVideo(string system, string romFileName)
        {
            // EN: RetroBat standard: only look in roms/[system]/videos
            // FR: Standard RetroBat : chercher uniquement dans roms/[system]/videos
            var videoDir = Path.Combine(_config.RomsPath, system, "videos");
            
            if (!Directory.Exists(videoDir)) return null;
            
            var videoExts = new[] { ".mp4", ".avi", ".webm", ".mkv", ".mov" };
            
            foreach (var ext in videoExts)
            {
                // Check strict naming first
                var testPath = Path.Combine(videoDir, romFileName + "-video" + ext);
                if (File.Exists(testPath)) return testPath;
                
                testPath = Path.Combine(videoDir, romFileName + ext);
                if (File.Exists(testPath)) return testPath;
            }
            
            return null;
        }

        /// <summary>
        /// EN: Check if video offsets exist and trigger regeneration if needed
        /// FR: VÃ©rifier si des offsets vidÃ©o existent et dÃ©clencher la rÃ©gÃ©nÃ©ration si nÃ©cessaire
        /// </summary>
        private void CheckAndTriggerVideoRegeneration(string system, string gameName)
        {
            try
            {
                // EN: Check if global video offsets exist for this game
                // FR: VÃ©rifier si des offsets vidÃ©o globaux existent pour ce jeu
                var offsets = _videoOffsetStorage.GetGlobalOffsets(system, gameName);
                
                // EN: Check if any offset is non-default (not all zeros and zoom=1)
                // FR: VÃ©rifier si un offset est non-par-dÃ©faut (pas tous zeros et zoom=1)
                bool hasCustomOffsets = offsets.CropX != 0 || offsets.CropY != 0 || 
                                       Math.Abs(offsets.Zoom - 1.0) > 0.01 ||
                                       offsets.LogoX != 10 || offsets.LogoY != 10 ||
                                       Math.Abs(offsets.LogoScale - 1.0) > 0.01 ||
                                       offsets.StartTime > 0.01 || offsets.EndTime > 0.01;
                
                if (!hasCustomOffsets)
                {
                    return; // No custom offsets, nothing to do
                }
                
                _logger.LogInformation($"[VideoRegen] Custom offsets detected for {gameName}");
                
                // EN: Check if individual offset marker exists and compare with current offsets
                // FR: VÃ©rifier si le marqueur d'offset individuel existe et comparer avec offsets actuels
                var individualOffsets = _videoOffsetStorage.GetIndividualOffsets(system, gameName);
                if (individualOffsets != null)
                {
                    // EN: Compare individual (last regenerated) with global (current desired)
                    // FR: Comparer individuels (derniÃ¨re rÃ©gÃ©nÃ©ration) avec globaux (actuels dÃ©sirÃ©s)
                    bool offsetsMatch = individualOffsets.CropX == offsets.CropX &&
                                       individualOffsets.CropY == offsets.CropY &&
                                       Math.Abs(individualOffsets.Zoom - offsets.Zoom) < 0.01 &&
                                       individualOffsets.LogoX == offsets.LogoX &&
                                       individualOffsets.LogoY == offsets.LogoY &&
                                       Math.Abs(individualOffsets.LogoScale - offsets.LogoScale) < 0.01 &&
                                       Math.Abs(individualOffsets.StartTime - offsets.StartTime) < 0.1 &&
                                       Math.Abs(individualOffsets.EndTime - offsets.EndTime) < 0.1;
                    
                    if (offsetsMatch)
                    {
                        _logger.LogInformation($"[VideoRegen] Offsets unchanged, video already regenerated");
                        return;
                    }
                    else
                    {
                        _logger.LogInformation($"[VideoRegen] Offsets changed since last regeneration, regenerating");
                    }
                }
                
                // EN: Find and delete the generated video to force regeneration
                // FR: Trouver et supprimer la vidÃ©o gÃ©nÃ©rÃ©e pour forcer la rÃ©gÃ©nÃ©ration
                var subFolder = _config.GenerateMarqueeVideoFolder;
                if (string.IsNullOrWhiteSpace(subFolder)) subFolder = "generated_videos";
                
                var parentDir = Directory.GetParent(_config.CachePath);
                if (parentDir == null) return;
                
                // EN: IMPORTANT: Use exact ROM filename to match video generation logic
                // FR: IMPORTANT: Utiliser le nom exact du fichier ROM pour correspondre Ã  la logique de gÃ©nÃ©ration vidÃ©o
                // Use _currentGameRom if available to ensure we match the file system naming (spaces vs underscores)
                var romFileName = !string.IsNullOrEmpty(_currentGameRom) 
                    ? Path.GetFileNameWithoutExtension(_currentGameRom) 
                    : gameName;

                // EN: Use ROM filename directly to respect original naming (spaces vs underscores)
                // FR: Utiliser le nom du fichier ROM directement pour respecter le nommage d'origine (espaces vs tirets bas)
                var sanitizedFileName = romFileName;

                var systemVideoDir = Path.Combine(parentDir.FullName, subFolder, system);
                if (!Directory.Exists(systemVideoDir))
                {
                    _logger.LogInformation($"[VideoRegen] Video directory doesn't exist yet: {systemVideoDir}");
                    return;
                }
                
                // EN: Find source video and logo to regenerate with new offsets
                // FR: Trouver vidÃ©o source et logo pour rÃ©gÃ©nÃ©rer avec nouveaux offsets
                // romFileName is used for output, but for finding source we might need both patterns
                var videoSource = FindSourceVideo(system, romFileName);
                
                if (string.IsNullOrEmpty(videoSource))
                {
                    _logger.LogWarning($"[VideoRegen] Source video not found for {gameName}, cannot regenerate");
                    return;
                }
                
                // EN: Find logo
                // FR: Trouver logo
                var logoPath = _marqueeFinder.FindGameLogo(system, gameName, romFileName, raw: true);
                if (string.IsNullOrEmpty(logoPath))
                {
                    _logger.LogWarning($"[VideoRegen] Logo not found for {gameName}, regenerating without logo");
                }
                
                // EN: Delete old video and regenerate with custom offsets
                // FR: Supprimer ancienne vidÃ©o et rÃ©gÃ©nÃ©rer avec offsets personnalisÃ©s
                var videoPath = Path.Combine(systemVideoDir, $"{sanitizedFileName}.mp4");
                if (File.Exists(videoPath))
                {
                    _logger.LogInformation($"[VideoRegen] Deleting old video: {videoPath}");
                    File.Delete(videoPath);
                }
                
                _logger.LogInformation($"[VideoRegen] Regenerating video with offsets for {sanitizedFileName}...");
                var result = _imageService.GenerateMarqueeVideoWithOffsets(
                    videoSource, logoPath ?? "", system, sanitizedFileName,
                    offsets.CropX, offsets.CropY, offsets.Zoom,
                    offsets.LogoX, offsets.LogoY, offsets.LogoScale,
                    offsets.StartTime, offsets.EndTime
                );
                
                if (!string.IsNullOrEmpty(result))
                {
                    _logger.LogInformation($"[VideoRegen] Successfully regenerated: {result}");
                    // EN: Create individual offset marker to prevent future regenerations
                    // FR: CrÃ©er marqueur d'offset individuel pour Ã©viter futures rÃ©gÃ©nÃ©rations
                    _videoOffsetStorage.SaveIndividualOffsets(system, gameName, offsets);
                    _logger.LogInformation($"[VideoRegen] Created individual offset marker");
                }
                else
                {
                    _logger.LogError($"[VideoRegen] Failed to regenerate video with offsets");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoRegen] Error checking/triggering regeneration: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Toggle between Static Preview and Source Video Playback for Trimming
        /// FR: Basculer entre AperÃ§u Statique et Lecture VidÃ©o Source pour la DÃ©coupe
        /// </summary>
        private async void HandleTogglePlayback()
        {
            if (!_isVideoAdjustmentMode) return;
            
            _isVideoPlaybackMode = !_isVideoPlaybackMode;
            _logger.LogInformation($"[VideoAdjustment] Toggle Playback: {_isVideoPlaybackMode}");
            
            await RefreshVideoPreview();

            // EN: Show OSD feedback for mode toggle
            // FR: Afficher un message OSD pour le basculement de mode
            var modeText = _isVideoPlaybackMode ? "TRIMMING MODE: ON (Playback)" : "ADJUSTMENT MODE: ON (Static)";
            await _mpv.SendCommandAsync($"{{\"command\":[\"show-text\", \"{modeText}\", 3000]}}");
        }

        /// <summary>
        /// EN: Handle Trim Start/End commands
        /// FR: GÃ©rer les commandes de dÃ©but/fin de dÃ©coupe
        /// </summary>
        private async void HandleTrimCommand(bool isStart)
        {
            if (!_isVideoAdjustmentMode || !_isVideoPlaybackMode)
            {
                // Can only trim while playing
                // FR: DÃ©coupe possible uniquement en lecture
                return;
            }

            double currentTime = await _mpv.GetCurrentTime();
            
            if (isStart)
            {
                _currentVideoOffsets.StartTime = currentTime;
                _logger.LogInformation($"[VideoAdjustment] Start Time Set: {currentTime:F2}s");
                await _mpv.SendCommandAsync($"{{\"command\":[\"show-text\", \"Start Time: {currentTime:F2}s\", 2000]}}");

                // EN: Capture new preview frame at this timestamp so static mode shows the new start
                // FR: Capturer nouvelle frame d'aperÃ§u Ã  ce timestamp pour que le mode statique montre le nouveau dÃ©but
                var romFileName = Path.GetFileNameWithoutExtension(_currentVideoPath) ?? "";
                var sourcePath = FindSourceVideo(_currentGameSystem ?? "", romFileName);
                if (!string.IsNullOrEmpty(sourcePath))
                {
                     // EN: Use unique filename to bypass cache
                     // FR: Utiliser un nom de fichier unique pour contourner le cache
                     string uniquePreviewName = $"preview_frame_{DateTime.Now.Ticks}.png";
                     string cacheDir = Path.Combine(_config.CachePath, "video_preview");
                     if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);
                     string newPreviewPath = Path.Combine(cacheDir, uniquePreviewName);

                     var newFrame = _videoService.CaptureVideoFrame(sourcePath, currentTime, newPreviewPath);
                     if (!string.IsNullOrEmpty(newFrame))
                     {
                         // Cleanup old frame if it exists and is different
                         if (!string.IsNullOrEmpty(_currentVideoPreviewFrame) && File.Exists(_currentVideoPreviewFrame) && _currentVideoPreviewFrame != newFrame)
                         {
                             try { File.Delete(_currentVideoPreviewFrame); } catch { }
                         }

                         _currentVideoPreviewFrame = newFrame;
                         // Force refresh if we are currently paused to show the new frame
                         if (!_isVideoPlaybackMode) await RefreshVideoPreview();
                     }
                }
            }
            else
            {
                _currentVideoOffsets.EndTime = currentTime;
                _logger.LogInformation($"[VideoAdjustment] End Time Set: {currentTime:F2}s");
                await _mpv.SendCommandAsync($"{{\"command\":[\"show-text\", \"End Time: {currentTime:F2}s\", 2000]}}");
            }
            
             // EN: Update global offsets storage
             // FR: Mettre Ã  jour le stockage des offsets globaux
             if (!string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
             {
                 _videoOffsetStorage.UpdateGlobalOffsets(_currentGameSystem, _currentGameName, _currentVideoOffsets);
             }
        }

        // ========== END VIDEO ADJUSTMENT MODE  ==========

        public void Start()
        {
            _dmdService.InitializeAsync().Wait();
        }

        public async Task ProcessEventAsync(string eventName, string param1, string param2, string param3, string param4, CancellationToken token = default)
        {
            _logger.LogInformation($"Event: {eventName} [p1:{param1}] [p2:{param2}] [p3:{param3}] [p4:{param4}]");

            if (token.IsCancellationRequested) return;

            switch (eventName)
            {
                case "game-selected":
                    await HandleGameSelected(param1, param3, param2, token);
                    break;
                case "system-selected":
                    await HandleSystemSelected(param1, token);
                    break;
                case "game-start":
                    await HandleGameStart(param1, param2, param3);
                    break;
                case "stop-preview":
                    _logger.LogInformation("Stopping preview mode (IPC command received).");
                    // EN: Cancel running preview task to prevent it from overlaying after cleanup
                    // FR: Annuler la tÃ¢che de preview en cours pour Ã©viter qu'elle n'affiche aprÃ¨s le nettoyage
                    if (_previewCts != null) { _previewCts.Cancel(); }
                    
                    if (_bgPreviewTask != null)
                    {
                        try { await _bgPreviewTask; } catch { }
                    }
                    
                    await CleanupAllOverlays();
                    break;
                case "game-end":
                    _logger.LogInformation("Game ended. Checking for GAME OVER screen...");
                    _gameRunning = false; // EN: Set game running flag to false to prevent delayed RA overlays

                    // EN: Explicitly reset RA state on game end
                    // FR: RÃ©initialiser explicitement l'Ã©tat RA Ã  la fin du jeu
                    _raService?.ResetState();

                    // EN: Unified cleanup for all overlays and cycles
                    // FR: Nettoyage unifiÃ© pour tous les overlays et cycles
                    await CleanupAllOverlays();

                    // Resume DMD for game or system
                     // EN: Wait for external DMD control (e.g. Pinball FX3) to release before attempting to display
                     // FR: Attendre la libÃ©ration du contrÃ´le DMD externe (ex: Pinball FX3) avant d'essayer d'afficher
                     await _dmdService.WaitForExternalReleaseAsync();
                     
                     // Display DMD Game Over if found
                     var dmdGameOver = _marqueeFinder.FindDmdGameOverMarquee();
                     if (dmdGameOver != null)
                     {
                         _logger.LogInformation($"Playing DMD Game Over: {dmdGameOver}");
                         await _dmdService.PlayAsync(dmdGameOver);
                     }
                     else if (!string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
                     {
                          // Resume DMD for game or system (Fallback)
                          // Fix: Block generated videos on game-end (treat as browsing) to prevent "generated gif" reuse
                          var dmdFile = await _marqueeFinder.FindDmdImageAsync(_currentGameSystem, _currentGameName, _currentGameRom != null ? Path.GetFileNameWithoutExtension(_currentGameRom) : "", allowVideo: false);
                          if (dmdFile != null) await _dmdService.PlayAsync(dmdFile, _currentGameSystem, _currentGameName);
                     }
                    
                    bool gameOverDisplayed = false;

                    // GAME OVER composition
                    if (_config.MarqueeCompose)
                    {
                        var composedPath = _marqueeFinder.FindGameOverMarquee();
                        if (!string.IsNullOrEmpty(composedPath) && File.Exists(composedPath))
                        {
                            _currentMarqueePath = composedPath; // EN: Update stored path / FR: Mettre Ã  jour le chemin stockÃ©
                            await _mpv.DisplayImage(composedPath);
                            _logger.LogInformation($"Displayed GAME OVER composition: {composedPath}");
                            gameOverDisplayed = true;
                        }
                    }

                    if (!gameOverDisplayed)
                    {
                        // Fallback: Restore the game's marquee
                        if (!string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
                        {
                            var gameMarquee = await _marqueeFinder.FindMarqueeFileAsync("game-selected", 
                                _currentGameSystem, 
                                _currentGameName, 
                                _currentGameName, 
                                _currentGameRom ?? "");
                            
                            if (gameMarquee != null)
                            {
                                _currentMarqueePath = gameMarquee; // EN: Update stored path / FR: Mettre Ã  jour le chemin stockÃ©
                                await _mpv.DisplayImage(gameMarquee);
                                _logger.LogInformation($"Restored game marquee (Fallback): {gameMarquee}");
                            }
                            else
                            {
                                await HandleSystemSelected(_currentGameSystem);
                            }
                        }
                        else if (!string.IsNullOrEmpty(_lastSelectedSystem))
                        {
                            await HandleSystemSelected(_lastSelectedSystem);
                        }
                    }
                    
                    // EN: VIDEO REGENERATION - Check if offsets changed and regenerate if needed
                    // FR: RÃ‰GÃ‰NÃ‰RATION VIDÃ‰O - VÃ©rifier si offsets changÃ©s et rÃ©gÃ©nÃ©rer si besoin
                    if (_isVideoAdjustmentMode && !string.IsNullOrEmpty(_currentVideoPath) && 
                        !string.IsNullOrEmpty(_currentGameSystem) && !string.IsNullOrEmpty(_currentGameName))
                    {
                        _logger.LogInformation("[VideoAdjustment] Exiting video adjustment mode - checking for regeneration");
                        
                        // EN: Check if offsets have changed compared to individual file
                        // FR: VÃ©rifier si les offsets ont changÃ© par rapport au fichier individuel
                        if (_videoService.IsGeneratedVideo(_currentVideoPath) && 
                            _videoOffsetStorage.HasOffsetsChanged(_currentGameSystem, _currentGameName))
                        {
                            _logger.LogInformation($"[VideoRegeneration] Offsets changed for {_currentGameSystem}/{_currentGameName} - regenerating video");
                            
                            // EN: Find source video and logo for regeneration
                            // FR: Trouver la vidÃ©o source et le logo pour rÃ©gÃ©nÃ©ration
                            // TODO: Store source video path for regeneration (for now, skip regeneration)
                            // User will need to trigger regeneration on next game-start
                            _logger.LogWarning("[VideoRegeneration] Video will be regenerated on next game-start");
                        }
                    }
                    
                    // EN: Reset video adjustment state
                    // FR: RÃ©initialiser l'Ã©tat d'ajustement vidÃ©o
                    _isVideoAdjustmentMode = false;
                    _currentVideoPath = null;
                    _currentVideoPreviewFrame = null;
                    
                    _gameRunning = false;
                    _ignoreEventsUntil = DateTime.Now.AddSeconds(3); // Ignore events for 3 seconds
                    _currentGameSystem = null;
                    _currentGameName = null;
                    _currentGameRom = null;
                    
                    // EN: Restart MPV if it was suspended for pinball
                    // FR: RedÃ©marrer MPV s'il a Ã©tÃ© suspendu pour le pinball
                    if (_mpvSuspendedForPinball)
                    {
                        _logger.LogInformation("[Pinball] Restarting MPV after pinball game...");
                        _mpvSuspendedForPinball = false;
                        _mpv.StartMpv();
                        // Display default/system marquee
                        if (!string.IsNullOrEmpty(_lastSelectedSystem))
                        {
                            var systemMarquee = await _marqueeFinder.FindMarqueeFileAsync("system-selected", _lastSelectedSystem, "", "", "");
                            if (systemMarquee != null)
                            {
                                await _mpv.DisplayImage(systemMarquee);
                            }
                        }
                    }
                    break;
                case "preview-overlay":
                    await HandlePreviewOverlay(param1);
                    break;
            }
        }

        private async Task HandleSystemSelected(string systemName, CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return;

            _lastSelectedSystem = systemName;
            _logger.LogInformation($"Processing System Selected: {systemName}");

            try
            {
                await DisplayDmdSystemAsync(systemName);
            }
            catch (TaskCanceledException) { }
        }

        private async Task DisplayDmdSystemAsync(string systemName)
        {
             var systemDmd = await _marqueeFinder.FindDmdImageAsync(systemName, systemName, "");
             
             if (_gameRunning) return; // Prevent overwriting if game started during lookup

             if (systemDmd != null)
             {
                 await _dmdService.PlayAsync(systemDmd, systemName, systemName);
             }
             else
             {
                 _logger.LogWarning($"DMD System Lookup Failed for '{systemName}'. Loading default DMD media.");
                 await _dmdService.PlayAsync(_config.DefaultDmdPath);
             }
        }

        private async Task DisplayMpvSystemAsync(string systemName, CancellationToken token = default)
        {
             if (token.IsCancellationRequested) return;
             var marqueeFile = await _marqueeFinder.FindMarqueeFileAsync("system-selected", systemName, "", "", "");
             
             if (token.IsCancellationRequested) return;
             if (_gameRunning) return; // Prevent overwriting if game started during lookup

             if (marqueeFile != null)
             {
                 await _mpv.DisplayImage(marqueeFile, loop: true, token: token);
                 _currentMarqueePath = marqueeFile; // Store for RP composition and Preview
                 _logger.LogInformation($"Displayed system marquee: {marqueeFile}");
             }
             else
             {
                 _logger.LogWarning($"No system marquee found for {systemName}, using default");
                 await _mpv.DisplayImage(_config.DefaultImagePath, loop: true, token: token);
                 _currentMarqueePath = _config.DefaultImagePath; // Store default as current

             }
        }

        private async Task HandleGameStart(string romPath, string romFileName, string gameName)
        {
            _logger.LogInformation($"Game starting: {gameName} ({romPath})");
            
            
            string? system = null;
            if (!string.IsNullOrEmpty(romPath))
            {
                var romsIndex = romPath.IndexOf("\\roms\\", StringComparison.OrdinalIgnoreCase);
                if (romsIndex >= 0)
                {
                    var afterRoms = romPath.Substring(romsIndex + 6);
                    var nextSlash = afterRoms.IndexOf('\\');
                    if (nextSlash > 0)
                    {
                        system = afterRoms.Substring(0, nextSlash);
                    }
                }
            }

            if (string.IsNullOrEmpty(system))
            {
                _logger.LogWarning($"Could not extract system from romPath: {romPath}");
                system = _lastSelectedSystem ?? "unknown";
            }

            _currentGameSystem = system;
            // EN: IMPORTANT: Use romFileName (full name with region) for video offsets, not gameName (ES display name)
            // FR: IMPORTANT: Utiliser romFileName (nom complet avec rÃ©gion) pour offsets vidÃ©o, pas gameName (nom affichage ES)
            // romFileName = "After Burner Complete (Europe)" - matches video/image files
            // gameName = "After Burner Complete" - ES display name, might be truncated
            _currentGameName = romFileName; // Use full ROM name for consistency with file names
            _currentGameRom = romPath;
            _lastSelectedSystem = system;
            _gameRunning = true;
            
            _logger.LogInformation($"Game Started: system={system}, game={romFileName}.");

            // CHECK PINBALL / CUSTOM COMMANDS
            // If handled, we skip standard DMD loading screen logic
            var (pinballHandled, suspendMPV) = await _dmdService.CheckAndRunPinballAsync(system, gameName);

            // EN: Suspend MPV if requested by pinball config
            // FR: Suspendre MPV si demandÃ© par la config pinball
            if (suspendMPV)
            {
                _logger.LogInformation($"[Pinball] Suspending MPV for '{system}'.");
                // EN: Stop internal MPV player and hide window
                // FR: ArrÃªter le lecteur MPV interne et cacher la fenÃªtre
                await _mpv.Stop();
                _mpvSuspendedForPinball = true;
            }

            // CHECK DMD LOADING SCREEN (Only if not handled by Pinball logic)
            // Parallel Execution: Find Media First
            string? dmdStartMedia = null;
            if (!pinballHandled)
            {
                 dmdStartMedia = _marqueeFinder.FindDmdGameStartMedia(system, romFileName, gameName);
            }

            var topperMedia = _marqueeFinder.FindGameStartMedia(system, romFileName, gameName);
            string? marqueeFile = null;
            string? systemMarquee = null;

            if (topperMedia == null)
            {
                // EN: Before finding marquee, check if we need to regenerate video due to saved offsets
                // FR: Avant de trouver la marquee, vÃ©rifier si on doit rÃ©gÃ©nÃ©rer la vidÃ©o Ã  cause d'offsets sauvegardÃ©s
                CheckAndTriggerVideoRegeneration(system, gameName);
                
                marqueeFile = await _marqueeFinder.FindMarqueeFileAsync("game-start", system, gameName, gameName, romPath);
                if (marqueeFile == null)
                {
                    systemMarquee = await _marqueeFinder.FindMarqueeFileAsync("system-selected", system, "", "", "");
                }
            }

            // Execute in Parallel
            var tasks = new List<Task>();

            if (!string.IsNullOrEmpty(dmdStartMedia))
            {
                 _logger.LogInformation($"[Workflow] Playing DMD Start Media: {dmdStartMedia}");
                 await _dmdService.PlayAsync(dmdStartMedia, _currentGameSystem, _currentGameName);
            }
            else if (!pinballHandled) // EN: Only show DMD logo if pinball isn't handling externally / FR: Afficher le logo DMD seulement si le pinball ne gÃ¨re pas en externe
            {
                // Fallback: If no specific "Loading..." media is defined, play the Game's DMD (Logo)
                // This ensures the DMD isn't blank during loading.
                // Fix: Disable scraping for this fallback (allowScraping: false) because it's a game-start event.
                var defaultGameDmd = await _marqueeFinder.FindDmdImageAsync(system, gameName, romFileName, romPath, allowVideo: true, allowScraping: false);
                if (defaultGameDmd != null)
                {
                    _logger.LogInformation($"Playing Game DMD as Loading Fallback: {defaultGameDmd}");
                    tasks.Add(_dmdService.PlayAsync(defaultGameDmd, system, gameName));
                }
                else
                {
                    // EN: Fallback to System DMD if Game DMD is not found (Fix for stale media on game-start)
                    // FR: Fallback vers DMD SystÃ¨me si DMD Jeu non trouvÃ© (Correction mÃ©dia obsolÃ¨te au lancement)
                    var systemDmd = await _marqueeFinder.FindDmdImageAsync(system, system, "");
                    if (systemDmd != null)
                    {
                        _logger.LogInformation($"Playing System DMD as Loading Fallback (Game DMD missing): {systemDmd}");
                        tasks.Add(_dmdService.PlayAsync(systemDmd, system, system));
                    }
                    else
                    {
                        // Final Fallback
                        tasks.Add(_dmdService.PlayAsync(_config.DefaultDmdPath));
                    }
                }
            }

            bool layLoaded = false;
            string? layPath = null;
            if (!suspendMPV)
            {
                var romName = !string.IsNullOrEmpty(romFileName) ? romFileName : gameName;
                layPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dof", "mame", romName, "default.lay");
                if (File.Exists(layPath))
                {
                    try
                    {
                        var layout = RetroBatMarqueeManager.Application.Services.MameLayParser.Parse(layPath);
                        _mpv.LoadMameLayout(layout, "Marquee_Only");
                        _logger.LogInformation($"[MAME Layout] Loaded layout for {romName} successfully.");
                        layLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[MAME Layout] Error loading layout for {romName}: {ex.Message}");
                    }
                }
            }

            if (layLoaded && layPath != null)
            {
                _currentMarqueePath = layPath;
            }
            else if (topperMedia != null && !suspendMPV)
            {
                var ext = Path.GetExtension(topperMedia).ToLowerInvariant();
                bool shouldLoop = new[] { ".gif", ".mp4", ".avi", ".mkv", ".webm", ".mov" }.Contains(ext);
                
                _currentMarqueePath = topperMedia; // EN: Update stored path for refresh / FR: Mettre Ã  jour le chemin stockÃ© pour le rafraÃ®chissement
                tasks.Add(_mpv.DisplayImage(topperMedia, shouldLoop).ContinueWith(t => 
                    _logger.LogInformation($"Displayed game-start topper: {topperMedia} (loop={shouldLoop})")));
            }
            else if (marqueeFile != null && !suspendMPV)
            {
                _currentMarqueePath = marqueeFile; // EN: Update stored path for refresh / FR: Mettre Ã  jour le chemin stockÃ© pour le rafraÃ®chissement
                tasks.Add(_mpv.DisplayImage(marqueeFile).ContinueWith(t => 
                    _logger.LogInformation($"Displayed game marquee: {marqueeFile}")));
            }
            else if (systemMarquee != null && !suspendMPV)
            {
                 _currentMarqueePath = systemMarquee; // EN: Update stored path for refresh / FR: Mettre Ã  jour le chemin stockÃ© pour le rafraÃ®chissement
                 tasks.Add(_mpv.DisplayImage(systemMarquee).ContinueWith(t => 
                    _logger.LogInformation($"Displayed system marquee: {systemMarquee}")));
            }
            else if (!suspendMPV)
            {
                _logger.LogWarning($"No game marquee found for {gameName}, using default (System Fallback failed)");
            }

            await Task.WhenAll(tasks);
            
            // EN: Track video path for adjustment mode (if it's a generated video)
            // FR: Tracker le chemin vidÃ©o pour mode ajustement (si c'est une vidÃ©o gÃ©nÃ©rÃ©e)
            if (topperMedia != null)
            {
                _currentVideoPath = topperMedia;
            }
            else if (marqueeFile != null)
            {
                _currentVideoPath = marqueeFile;
            }
            else
            {
                _currentVideoPath = null;
            }
        }


        private async Task HandleGameSelected(string system, string gameName, string romPath, CancellationToken token = default)
        {
            if (token.IsCancellationRequested) return;

            if (_gameRunning)
            {
                _logger.LogWarning($"Ignoring game-selected event for '{gameName}' (Game is running).");
                return;
            }
            
            if (DateTime.Now < _ignoreEventsUntil)
            {
                _logger.LogWarning($"Ignoring game-selected event for '{gameName}' (Post-game cooldown).");
                return;
            }

            // EN: Pre-emptively reset RA state when browsing new games
            // FR: RÃ©initialiser prÃ©ventivement l'Ã©tat RA lors de la navigation dans de nouveaux jeux
            _raService?.ResetState();

            var romFileName = !string.IsNullOrEmpty(romPath) ? Path.GetFileNameWithoutExtension(romPath) : "";
            
            // EN: IMPORTANT: Extract real system from ROM path (not ES alias) for consistency
            // FR: IMPORTANT: Extraire vrai systÃ¨me depuis chemin ROM (pas alias ES) pour cohÃ©rence
            // ES sends "gw" but ROM path contains "gameandwatch" - we need the real folder name
            string? realSystem = null;
            if (!string.IsNullOrEmpty(romPath))
            {
                var romsIndex = romPath.IndexOf("\\roms\\", StringComparison.OrdinalIgnoreCase);
                if (romsIndex < 0) romsIndex = romPath.IndexOf("/roms/", StringComparison.OrdinalIgnoreCase);
                
                if (romsIndex >= 0)
                {
                    var afterRoms = romPath.Substring(romsIndex + 6);
                    var nextSlash = afterRoms.IndexOfAny(new[] { '\\', '/' });
                    if (nextSlash > 0)
                    {
                        realSystem = afterRoms.Substring(0, nextSlash);
                    }
                }
            }
            
            if (string.IsNullOrEmpty(realSystem))
            {
                _logger.LogWarning($"Could not extract real system from romPath: {romPath}, using ES system: {system}");
                realSystem = system;
            }
            
            _logger.LogInformation($"[Event] Game Selected: {romFileName} ({realSystem})");

            _currentGameSystem = realSystem;
            // EN: IMPORTANT: Use ROM filename (full name) for consistency with game-start
            // FR: IMPORTANT: Utiliser nom fichier ROM (nom complet) pour cohÃ©rence avec game-start
            _currentGameName = romFileName;
            _currentGameRom = romPath;

            // EN: Check if we need to regenerate video due to saved offsets
            // FR: VÃ©rifier si on doit rÃ©gÃ©nÃ©rer la vidÃ©o Ã  cause d'offsets sauvegardÃ©s
            CheckAndTriggerVideoRegeneration(realSystem, romFileName);

            // DMD only â€” marquee display is handled by WS marquee.snapshot from APIExpose
            await DisplayDmdGameAsync(system, gameName, romFileName, romPath, token);
        }

        private async Task DisplayDmdGameAsync(string system, string gameName, string romFileName, string romPath, CancellationToken token)
        {
             if (token.IsCancellationRequested) return;
             if (token.IsCancellationRequested) return;
             var dmdFile = await _marqueeFinder.FindDmdImageAsync(system, gameName, romFileName, romPath, allowVideo: false);
             _currentDmdPath = dmdFile; // Store for RP composition
             
             if (token.IsCancellationRequested) return;
             
             // CRITICAL: Check if game started while we were looking for media (Race Condition Fix)
             if (_gameRunning)
             {
                 _logger.LogWarning($"[DMD Race Check] Aborting game-selected display for '{gameName}'. Game is already running.");
                 return;
             }
             
             if (!string.IsNullOrEmpty(dmdFile))
             {
                 await _dmdService.PlayAsync(dmdFile, system, gameName);
             }
             else
             {
                 _logger.LogWarning($"DMD Game Lookup Failed for '{gameName}' ({romFileName}). Attempting Fallback to System DMD...");
                 var systemDmd = await _marqueeFinder.FindDmdImageAsync(system, system, "");
                 if (systemDmd != null) 
                 {
                     await _dmdService.PlayAsync(systemDmd, system, system);
                 }
                 else 
                 {
                     _logger.LogWarning($"DMD System Fallback Failed for '{system}'. Loading default DMD media.");
                     await _dmdService.PlayAsync(_config.DefaultDmdPath);
                 }
             }
        }

        private async Task DisplayMpvGameAsync(string system, string gameName, string romFileName, string romPath, CancellationToken token)
        {
             if (token.IsCancellationRequested) return;
             if (token.IsCancellationRequested) return;
             var marqueeFile = await _marqueeFinder.FindMarqueeFileAsync("game-selected", system, gameName, gameName, romPath);
             _currentMarqueePath = marqueeFile; // Store for RP composition

             if (token.IsCancellationRequested) return;

             // FIX: Prevent overwriting MPV if game has already started (and potentially suspended MPV)
             if (_gameRunning)
             {
                 _logger.LogWarning($"[MPV Race Check] Aborting game-selected display for '{gameName}'. Game is already running.");
                 return;
             }

             if (marqueeFile != null)
             {
                 await _mpv.DisplayImage(marqueeFile, loop: true, token: token);
                 _logger.LogInformation($"Displayed: {marqueeFile}");
             }
             else
             {
                 _logger.LogWarning($"No marquee found for {gameName}, using default");
                 await _mpv.DisplayImage(_config.DefaultImagePath, loop: true, token: token);
             }
        }

        // ===================== RETROACHIEVEMENTS EVENT HANDLERS =====================
        
        /// <summary>
        /// EN: Handle achievement unlocked event - Display badge on MPV and DMD
        /// FR: GÃ©rer Ã©vÃ©nement succÃ¨s dÃ©bloquÃ© - Afficher badge sur MPV et DMD
        /// </summary>
        private async void OnAchievementUnlocked(object? sender, AchievementUnlockedEventArgs e)
        {
            try
            {
                _logger.LogInformation($"[RA Workflow] Achievement Unlocked: {e.Achievement.Title} ({e.Achievement.Points} pts)");
                
                // EN: Badge path is already local (downloaded by API client)
                var badgePath = e.Achievement.BadgeName;
                if (string.IsNullOrEmpty(badgePath) || !File.Exists(badgePath))
                {
                    _logger.LogWarning($"[RA Workflow] Badge not found: {badgePath}");
                    return;
                }

                // Configuration shortcuts
                var target = _config.MarqueeRetroAchievementsDisplayTarget;
                var mpvNotifications = _config.MpvRetroAchievementsNotifications;
                var dmdNotifications = _config.DmdRetroAchievementsNotifications;
                var mpvOverlaysStr = _config.MpvRetroAchievementsOverlays;
                var dmdOverlaysStr = _config.DmdRetroAchievementsOverlays;
                var cupPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias", "retroachievements", "biggoldencup.png");
                
                // Consistency flags
                int currentPoints = _raService?.CurrentGameUserPoints ?? 0;
                int totalPoints = _raService?.CurrentGameTotalPoints ?? 0;
                bool isHc = _raService?.IsHardcoreMode ?? false;

                // 1. Prepare MPV Overlay
                var mpvAchOverlay = _imageService.GenerateMpvAchievementOverlay(badgePath, e.Achievement.Title, e.Achievement.Description, e.Achievement.Points, e.IsHardcore);
                if (string.IsNullOrEmpty(mpvAchOverlay)) mpvAchOverlay = badgePath;
                
                string? mpvScoreOverlay = null;
                if (!string.IsNullOrEmpty(mpvOverlaysStr) && mpvOverlaysStr.Contains("score", StringComparison.OrdinalIgnoreCase))
                    mpvScoreOverlay = _imageService.GenerateScoreOverlay(currentPoints, totalPoints, isDmd: false, isHardcore: isHc);
                
                var mpvMainOverlay = _imageService.ComposeScoreAndBadges(mpvScoreOverlay, mpvAchOverlay, null, _config.MarqueeWidth, _config.MarqueeHeight, isHardcore: isHc);
                if (string.IsNullOrEmpty(mpvMainOverlay)) mpvMainOverlay = mpvAchOverlay;

                // 2. Parallel Achievement Display
                var mpvTask = Task.Run(async () =>
                {
                    if (target == "dmd" || !mpvNotifications) return;
                    try
                    {
                        _mpvBadgeCycleCts?.Cancel();
                        await _mpv.RemoveOverlay(3, false);
                        await Task.Delay(100);
                        
                        string? countOverlay = null;
                        if (!string.IsNullOrEmpty(mpvOverlaysStr) && mpvOverlaysStr.Contains("count", StringComparison.OrdinalIgnoreCase))
                        {
                            var achievements = _raService?.CurrentGameAchievements;
                            if (achievements != null)
                            {
                                int unlockedCount = achievements.Values.Count(a => isHc ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                                countOverlay = _imageService.GenerateAchievementCountOverlay(unlockedCount, achievements.Count, isDmd: false, isHardcore: isHc);
                            }
                        }

                        if (!string.IsNullOrEmpty(countOverlay))
                        {
                            var mpvWithCount = _imageService.ComposeScoreAndBadges(mpvScoreOverlay, mpvAchOverlay, countOverlay, _config.MarqueeWidth, _config.MarqueeHeight, isHardcore: isHc);
                            if (!string.IsNullOrEmpty(mpvWithCount)) mpvMainOverlay = mpvWithCount;
                        }

                        await _mpv.ShowAchievementNotification(cupPath, mpvMainOverlay, 2000, 8000);
                    }
                    catch (Exception ex) { _logger.LogError($"[RA Workflow] MPV Notification Error: {ex.Message}"); }
                });

                var dmdTask = Task.Run(async () =>
                {
                    if (target == "mpv" || !dmdNotifications) return;
                    await _dmdNotificationSemaphore.WaitAsync();
                    try
                    {
                        _badgeCycleCts?.Cancel();
                        _dmdService.ClearOverlay();
                        await Task.Delay(100);

                        if (_config.DmdEnabled)
                        {
                             var dmdAchOverlay = _imageService.GenerateDmdAchievementOverlay(badgePath, e.Achievement.Title, e.Achievement.Description, e.Achievement.Points, isHc);
                             if (!string.IsNullOrEmpty(dmdAchOverlay))
                             {
                                 _logger.LogInformation("[RA Workflow] Playing DMD Achievement Sequence");
                                 await _dmdService.PlayAchievementSequenceAsync(cupPath, dmdAchOverlay, 10000);
                             }
                        }
                    }
                    catch (Exception ex) { _logger.LogError($"[RA Workflow] DMD Notification Error: {ex.Message}"); }
                    finally
                    {
                        _dmdNotificationSemaphore.Release();
                    }
                });

                await Task.WhenAll(mpvTask, dmdTask);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Workflow] Error displaying achievement: {ex.Message}");
            }
            finally
            {
                int remaining = Interlocked.Decrement(ref _activeAchievementCount);
                if (remaining <= 0)
                {
                    _isProcessingAchievement = false;
                    _logger.LogInformation("[RA Workflow] All achievements processed. RP and Cycles restored.");
                    
                    // EN: Restart cycles with updated values now that all achievements are processed
                    // FR: RedÃ©marre les cycles avec les valeurs mises Ã  jour maintenant que tous les achievements sont traitÃ©s
                    if (_gameRunning && _raService != null && _raService.CurrentGameId.HasValue)
                    {
                        var achievements = _raService.CurrentGameAchievements;
                        if (achievements != null && achievements.Count > 0)
                        {
                            _logger.LogInformation($"[RA Workflow] Restarting cycles with updated values: {_raService.CurrentGameUserPoints}/{_raService.CurrentGameTotalPoints}");
                            
                            // MPV Restart
                            var mpvOverlays = _config.MpvRetroAchievementsOverlays;
                            if (!string.IsNullOrEmpty(mpvOverlays) && mpvOverlays.Contains("badges", StringComparison.OrdinalIgnoreCase))
                            {
                                StartMpvBadgeRibbonCycle(_raService.CurrentGameId.Value, achievements, _raService.CurrentGameUserPoints, _raService.CurrentGameTotalPoints);
                            }

                            // DMD Restart
                            var dmdOverlays = _config.DmdRetroAchievementsOverlays;
                            if (!string.IsNullOrEmpty(dmdOverlays) && dmdOverlays.Contains("badges", StringComparison.OrdinalIgnoreCase))
                            {
                                StartBadgeRibbonCycle(_raService.CurrentGameId.Value, achievements);
                            }
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// EN: Handle game started event - Log game info
        /// FR: GÃ©rer Ã©vÃ©nement jeu dÃ©marrÃ© - Logger infos jeu
        /// </summary>
        private async void OnHardcoreStatusChanged(object? sender, bool isHardcore)
        {
            try
            {
                _logger.LogInformation($"[RA Workflow] Hardcore Mode Changed: {isHardcore}. Refreshing overlays...");
                
                // EN: If status changes during a game, we must refresh persistent overlays to update "HC" labels
                // FR: Si le statut change pendant un jeu, on doit rafraÃ®chir les overlays persistants pour mettre Ã  jour les labels "HC"
                if (_gameRunning)
                {
                    // EN: Unified cleanup before refresh
                    await CleanupAllOverlays();

                    var target = _config.MarqueeRetroAchievementsDisplayTarget;
                    var mpvOverlaysStr = _config.MpvRetroAchievementsOverlays;
                    var dmdOverlaysStr = _config.DmdRetroAchievementsOverlays;

                    // EN: Refresh persistent MPV overlays
                    // FR: RafraÃ®chir overlays MPV persistants
                    if (target != "dmd" && !string.IsNullOrWhiteSpace(mpvOverlaysStr))
                    {
                        await RefreshPersistentMpvOverlays();
                    }
                    
                    // EN: Restart cycles if active
                    if (_raService != null && _raService.CurrentGameId.HasValue)
                    {
                        var achievements = _raService.CurrentGameAchievements;
                        if (achievements != null && achievements.Count > 0)
                        {
                            if (target != "dmd" && mpvOverlaysStr.Contains("badges", StringComparison.OrdinalIgnoreCase))
                            {
                                int current = _raService.CurrentGameUserPoints;
                                int total = _raService.CurrentGameTotalPoints;
                                StartMpvBadgeRibbonCycle(_raService.CurrentGameId.Value, achievements, current, total);
                            }
                            
                            if (target != "mpv" && !string.IsNullOrWhiteSpace(dmdOverlaysStr))
                            {
                                if (dmdOverlaysStr.Contains("badges", StringComparison.OrdinalIgnoreCase))
                                {
                                    StartBadgeRibbonCycle(_raService.CurrentGameId.Value, achievements);
                                }
                                else
                                {
                                    // EN: One-shot refresh for DMD if no badges
                                    // FR: RafraÃ®chissement unique pour DMD si pas de badges
                                    if (dmdOverlaysStr.Contains("count", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var isHc = _raService.IsHardcoreMode;
                                        int unlockedCount = achievements.Values.Count(a => isHc ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                                        var dmdCount = _imageService.GenerateAchievementCountOverlay(unlockedCount, achievements.Count, isDmd: true, isHardcore: isHc);
                                        if (File.Exists(dmdCount)) await _dmdService.SetOverlayAsync(dmdCount, 5000);
                                    }
                                    if (dmdOverlaysStr.Contains("score", StringComparison.OrdinalIgnoreCase))
                                    {
                                         var dmdScore = _imageService.GenerateScoreOverlay(_raService.CurrentGameUserPoints, _raService.CurrentGameTotalPoints, isDmd: true, isHardcore: _raService.IsHardcoreMode);
                                         if (File.Exists(dmdScore)) await _dmdService.SetOverlayAsync(dmdScore, 5000);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MarqueeWorkflow] Error handling Hardcore status change: {ex.Message}");
            }
        }

        private async void OnGameStarted(object? sender, GameStartedEventArgs e)
        {
            _lastRpState = null; // EN: Reset RP state / FR: RÃ©initialiser l'Ã©tat RP
            try
            {
                var gameTitle = e.GameInfo?.Title ?? $"Game ID {e.GameId}";
                var completion = e.UserProgress?.UserCompletion ?? "0%";
                _logger.LogInformation($"[RA Workflow] Game Started: {gameTitle} - {completion} complete");

                // EN: Purge old Hardcore overlays to prevent accumulation
                // FR: Purger les anciens overlays Hardcore pour Ã©viter l'accumulation

                // EN: Purge old Hardcore overlays to prevent accumulation
                // FR: Purger les anciens overlays Hardcore pour Ã©viter l'accumulation
                _imageService.PurgeHardcoreOverlays();
                _imageService.PurgeSoftcoreOverlays(); // Added softcore purge as requested

                // EN: Ensure clean slate on game start
                // FR: Garantir un Ã©tat propre au dÃ©marrage du jeu
                await CleanupAllOverlays();
                // EN: Check if Overlays are enabled
                // FR: VÃ©rifier si les overlays sont activÃ©s
                var target = _config.MarqueeRetroAchievementsDisplayTarget;
                var mpvOverlaysStr = _config.MpvRetroAchievementsOverlays;
                var dmdOverlaysStr = _config.DmdRetroAchievementsOverlays;

                _logger.LogInformation($"[RA Workflow] Overlays Config - MPV: '{mpvOverlaysStr}', DMD: '{dmdOverlaysStr}', Target: {target}");

                if (string.IsNullOrEmpty(mpvOverlaysStr) && string.IsNullOrEmpty(dmdOverlaysStr))
                {
                    _logger.LogInformation("[RA Workflow] No overlays enabled for MPV or DMD. Skipping.");
                    return;
                }

                // Split into lists for easier checking
                var mpvOverlays = mpvOverlaysStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(o => o.Trim()).ToList();
                var dmdOverlays = dmdOverlaysStr.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(o => o.Trim()).ToList();

                // EN: Check if game has achievements
                // FR: VÃ©rifier si le jeu a des succÃ¨s
                if (e.UserProgress?.Achievements == null || e.UserProgress.Achievements.Count == 0)
                {
                    _logger.LogInformation($"[RA Workflow] Game '{gameTitle}' has no achievements. Skipping Overlays.");
                    return;
                }

                _logger.LogInformation($"[RA Workflow] Display Target: {target}");
                
                // EN: Refresh persistent MPV overlays (Score, Count)
                // FR: RafraÃ®chir les overlays persistants MPV (Score, Compteur)
                if (target != "dmd" && mpvOverlays.Any())
                {
                    await RefreshPersistentMpvOverlays();
                }
                
                // EN: DMD - Show initial sequence (count and score) if enabled
                // FR: DMD - Afficher la sÃ©quence initiale (compteur et score) si activÃ©
                if (target != "mpv" && dmdOverlays.Any())
                {
                    if (dmdOverlays.Contains("count", StringComparer.OrdinalIgnoreCase) || dmdOverlays.Contains("score", StringComparer.OrdinalIgnoreCase))
                    {
                        if (_raService == null || e.UserProgress?.Achievements == null) return;
            
                        bool isHcEvent = e.IsHardcore; // Use event's HC status
                        int current = e.UserProgress.Achievements.Values
                            .Where(a => isHcEvent ? a.DateEarnedHardcore.HasValue : a.Unlocked)
                            .Sum(a => a.Points);
                        int total = e.UserProgress.Achievements.Values.Sum(a => a.Points);
                        
                        _logger.LogInformation($"[RA Workflow] In-game points at start: {current}/{total} (HC: {isHcEvent})");
            
                        // Note: dmdScore generation moved inside specific block to ensure latest state
    
                            if (dmdOverlays.Contains("count", StringComparer.OrdinalIgnoreCase))
                            {
                                var isHardcore = _raService?.IsHardcoreMode ?? false;
                                int unlockedCount = e.UserProgress.Achievements.Values.Count(a => isHardcore ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                                var dmdCount = _imageService.GenerateAchievementCountOverlay(unlockedCount, e.UserProgress.Achievements.Count, isDmd: true, isHardcore: isHardcore);
                                    // EN: Check if an achievement notification started during delay
                                    if (_dmdService.IsSequencePlaying) return;

                                    await _dmdService.SetPriorityOverlayAsync(dmdCount, 5000); // Show for 5s (Protected)
                                    _logger.LogInformation($"[RA Workflow] Displayed DMD Count Overlay: {dmdCount}");
                                    
                                    if (!_gameRunning) return; // Prevent if game stopped during delay
                            }
                            
                            if (dmdOverlays.Contains("score", StringComparer.OrdinalIgnoreCase))
                            {
                                if (!_gameRunning) return;

                                // EN: Generate score overlay immediately before use for latest Hardcore status
                                // FR: GÃ©nÃ©rer score overlay immÃ©diatement avant usage pour dernier statut Hardcore
                                var isHardcore = _raService?.IsHardcoreMode ?? false;
                                var dmdScore = _imageService.GenerateScoreOverlay(current, total, isDmd: true, isHardcore: isHardcore);
                                
                                // EN: Check sequence playing before score
                                if (!_dmdService.IsSequencePlaying)
                                {
                                    await _dmdService.SetPriorityOverlayAsync(dmdScore, 5000); // Show for 5s (Protected)
                                    _logger.LogInformation($"[RA Workflow] Displayed DMD Score Overlay: {dmdScore}");
                                }
                            }
                        }
                    }

                    // EN: Badge Ribbon Overlay / FR: Overlay de bandeau de badges
                    bool showMpvBadges = (target != "dmd") && mpvOverlays.Contains("badges", StringComparer.OrdinalIgnoreCase);
                    bool showDmdBadges = (target != "mpv") && dmdOverlays.Contains("badges", StringComparer.OrdinalIgnoreCase);

                    if (showMpvBadges || showDmdBadges)
                    {
                        // EN: Check if game has achievements / FR: VÃ©rifier si le jeu a des succÃ¨s
                        if (e.UserProgress?.Achievements != null && e.UserProgress.Achievements.Count > 0)
                        {
                            if (_raService != null && e.GameId > 0)
                            {
                                // Generate badge ribbon overlays
                                try
                                {
                                    var isHardcore = _raService.IsHardcoreMode;
                                    
                                    if (showMpvBadges)
                                    {
                                        var mpvBadgeRibbon = await _imageService.GenerateBadgeRibbonOverlay(
                                            e.UserProgress.Achievements, e.GameId, _raService, isDmd: false, isHardcore: isHardcore);
                                            
                                        if (!string.IsNullOrEmpty(mpvBadgeRibbon) && File.Exists(mpvBadgeRibbon))
                                        {
                                            int currentScore = _raService?.CurrentGameUserPoints ?? 0;
                                            int totalScore = _raService?.CurrentGameTotalPoints ?? 0;
                                            StartMpvBadgeRibbonCycle(e.GameId, e.UserProgress.Achievements, currentScore, totalScore);
                                            _logger.LogInformation($"[RA Workflow] Started MPV Badge Ribbon Cycle");
                                        }
                                    }
                                    
                                    // EN: Start DMD cycle if badges are enabled
                                    // FR: DÃ©marrer cycle DMD si les badges sont activÃ©s
                                    if (target != "mpv" && dmdOverlaysStr.Contains("badges", StringComparison.OrdinalIgnoreCase))
                                    {
                                        StartBadgeRibbonCycle(e.GameId, e.UserProgress.Achievements);
                                        _logger.LogInformation($"[RA Workflow] Started DMD Badge Ribbon Cycle");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"[RA Workflow] Error initializing RA cycles: {ex.Message}");
                                }
                            }
                        }
                    }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Workflow] Error handling game start: {ex.Message}");
            }
        }

        private async Task RefreshMpvOverlays()
        {
             // EN: Reload the main background ONLY for static images to ensure MPV doesn't freeze.
             // FR: Recharger le fond principal UNIQUEMENT pour les images statiques pour s'assurer que MPV ne se fige pas.
             if (!string.IsNullOrEmpty(_currentMarqueePath) && File.Exists(_currentMarqueePath))
             {
                 string ext = Path.GetExtension(_currentMarqueePath).ToLowerInvariant();
                 bool isVideo = ext == ".mp4" || ext == ".mkv" || ext == ".avi" || ext == ".mov" || ext == ".wmv" || ext == ".webm";
                 
                 if (!isVideo)
                 {
                     await _mpv.DisplayImage(_currentMarqueePath);
                 }
             }

             // EN: Wait a bit for the background to load before overlays
             // FR: Attendre un peu que le fond charge
             await Task.Delay(200);

             // EN: Restore active narration if any (background reload might have cleared it)
             // FR: Restaurer la narration active si nÃ©cessaire
             if (_currentNarrationId != Guid.Empty && !string.IsNullOrEmpty(_activeNarrationPath))
             {
                 await _mpv.OverlayImage(_activeNarrationPath, 4, _activeNarrationPosition ?? "center", loopCount: 0);
             }

             await RefreshPersistentMpvOverlays();
             if (_currentMpvRpGenericStats.Count > 0)
             {
                 // EN: Use a standard offset height if not easily retrievable
                 // FR: Utiliser une hauteur d'offset standard pour la rotation
                 StartMpvRpStatRotation(60); 
             }
        }

        /// <summary>
        /// EN: Refresh persistent MPV overlays (Score and Count) by composing them into a single image
        /// FR: RafraÃ®chir les overlays persistants MPV (Score et Compteur) en les composant en une seule image
        /// </summary>
        private async Task RefreshPersistentMpvOverlays()
        {
            if (_raService == null || _imageService == null || _mpv == null) return;

            var overlays = _config.MpvRetroAchievementsOverlays;
            _logger.LogInformation($"[RA Workflow] Refreshing persistent MPV overlays with config: '{overlays}'");
            if (string.IsNullOrEmpty(overlays)) return;

            var activeOverlays = overlays.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(o => o.Trim());

            string? scoreOverlay = null;
            string? countOverlay = null;

            if (activeOverlays.Contains("score", StringComparer.OrdinalIgnoreCase))
            {
                int current = _raService.CurrentGameUserPoints;
                int total = _raService.CurrentGameTotalPoints;
                scoreOverlay = _imageService.GenerateScoreOverlay(current, total, isDmd: false, isHardcore: _raService.IsHardcoreMode);
            }

            if (activeOverlays.Contains("count", StringComparer.OrdinalIgnoreCase))
            {
                var achievements = _raService.CurrentGameAchievements;
                if (achievements != null)
                {
                    var isHardcore = _raService.IsHardcoreMode;
                    int unlockedCount = achievements.Values.Count(a => isHardcore ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                    countOverlay = _imageService.GenerateAchievementCountOverlay(unlockedCount, achievements.Count, isDmd: false, isHardcore: isHardcore);
                }
            }

            if (scoreOverlay != null || countOverlay != null)
            {
                // EN: Compose them into a single overlay (badges Ribbon is handled separately by the cycle)
                // FR: Les composer en un seul overlay (le bandeau des badges est gÃ©rÃ© sÃ©parÃ©ment par le cycle)
                var composed = _imageService.ComposeScoreAndBadges(scoreOverlay, null, countOverlay, _config.MarqueeWidth, _config.MarqueeHeight, isHardcore: _raService.IsHardcoreMode);
                if (!string.IsNullOrEmpty(composed) && File.Exists(composed))
                {
                    if (!_gameRunning) return; // Prevent race condition if game ended
                    // EN: Show indefinitely (1h) on persistent slot 1
                    // FR: Afficher indÃ©finiment (1h) sur le slot persistant 1
                    await _mpv.OverlayImage(composed, 1, "top-left");
                    _logger.LogInformation($"[RA Workflow] Refreshed persistent MPV overlays: {Path.GetFileName(composed)}");
                }
            }
        }
        

        /// <summary>
        /// EN: Start MPV badge ribbon cycling with score composition (~29 badges per frame)
        /// FR: DÃ©marrer cycle badges MPV avec composition score (~29 badges par frame)
        /// </summary>
        private void StartMpvBadgeRibbonCycle(int gameId, Dictionary<string, Achievement> achievements, int currentPoints, int totalPoints)
        {
            _mpvBadgeCycleCts?.Cancel();
            _mpvBadgeCycleCts?.Dispose();
            _mpvBadgeCycleCts = new CancellationTokenSource();
            
            var token = _mpvBadgeCycleCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    // EN: Calculate max badges per frame based on template (Ribbon Width and Ribbon Height)
                    // FR: Calculer badges max par frame selon le template (Largeur et Hauteur du Ruban)
                    var (badgeSize, maxBadgesPerFrame) = _imageService.GetBadgeRibbonCapacity(isDmd: false);
                    
                    // EN: Group achievements by calculated max / FR: Grouper achievements par max calculÃ©
                    var sortedAchievements = achievements.Values.OrderBy(a => a.DisplayOrder).ToList();
                    var groups = sortedAchievements
                        .Select((a, i) => new { Achievement = a, Index = i })
                        .GroupBy(x => x.Index / maxBadgesPerFrame)
                        .Select(g => g.Select(x => x.Achievement).ToList())
                        .ToList();
                    
                    _logger.LogInformation($"[RA MPV Cycle] Created {groups.Count} groups of ~{maxBadgesPerFrame} badges (Size: {badgeSize}px, Screen: {_config.MarqueeWidth}x{_config.MarqueeHeight})");
                    
                    // EN: Generate count overlay if enabled / FR: GÃ©nÃ©rer overlay compteur si activÃ©
                    string? countOverlay = null;
                    var overlayConfig = _config.MpvRetroAchievementsOverlays;
                    if (!string.IsNullOrEmpty(overlayConfig) && overlayConfig.Contains("count", StringComparison.OrdinalIgnoreCase))
                    {
                        var isHardcore = _raService?.IsHardcoreMode ?? false;
                        int unlockedCount = achievements.Values.Count(a => isHardcore ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                        countOverlay = _imageService.GenerateAchievementCountOverlay(unlockedCount, achievements.Count, isDmd: false, isHardcore: isHardcore);
                    }
                    
                    // EN: Pre-generate all composed overlays once to avoid file accumulation
                    // FR: PrÃ©-gÃ©nÃ©rer tous les overlays composÃ©s une fois pour Ã©viter accumulation de fichiers
                    var composedOverlays = new List<string>();
                    
                    foreach (var group in groups)
                    {
                        var groupDict = group.ToDictionary(a => a.ID.ToString(), a => a);
                        var badgeRibbon = await _imageService.GenerateBadgeRibbonOverlay(
                            groupDict, gameId, _raService!, isDmd: false, isHardcore: _raService?.IsHardcoreMode ?? false);
                        
                        string? scoreOverlay = null;
                        if (overlayConfig.Contains("score", StringComparison.OrdinalIgnoreCase))
                        {
                            scoreOverlay = _imageService.GenerateScoreOverlay(currentPoints, totalPoints, isDmd: false, isHardcore: _raService?.IsHardcoreMode ?? false);
                        }
                        
                        var composedOverlay = _imageService.ComposeScoreAndBadges(scoreOverlay, badgeRibbon, countOverlay, _config.MarqueeWidth, _config.MarqueeHeight, isHardcore: _raService?.IsHardcoreMode ?? false);
                        
                        if (!string.IsNullOrEmpty(composedOverlay))
                        {
                            composedOverlays.Add(composedOverlay);
                        }
                    }
                    
                    // EN: Generate specific overlay for pause / FR: GÃ©nÃ©rer overlay spÃ©cifique pour pause
                    string? scoreOnly = null;
                    if (overlayConfig.Contains("score", StringComparison.OrdinalIgnoreCase))
                    {
                        scoreOnly = _imageService.GenerateScoreOverlay(currentPoints, totalPoints, isDmd: false, isHardcore: _raService?.IsHardcoreMode ?? false);
                    }
                    
                    var composedScoreOnly = _imageService.ComposeScoreAndBadges(scoreOnly, null, countOverlay, _config.MarqueeWidth, _config.MarqueeHeight, isHardcore: _raService?.IsHardcoreMode ?? false);
                    
                    _logger.LogInformation($"[RA MPV Cycle] Pre-generated {composedOverlays.Count} composed overlays");
                    
                    // EN: Optimization: If only 1 group (all badges fit on screen), show static overlay and exit loop
                    // FR: Optimisation : Si 1 seul groupe (tous les badges tiennent), afficher overlay statique et quitter boucle
                    if (composedOverlays.Count <= 1)
                    {
                         string overlayToShow = composedOverlays.FirstOrDefault() ?? composedScoreOnly;
                         
                         if (File.Exists(overlayToShow))
                         {
                             // EN: Show for a very long duration on persistent slot 1
                             // FR: Afficher pour une trÃ¨s longue durÃ©e sur le slot persistant 1
                             await _mpv.OverlayImage(overlayToShow, 1, "top-left");
                             _logger.LogInformation($"[RA MPV Cycle] Single page detected. Showing static overlay: {overlayToShow}");
                         }
                         return; // EXIT LOOP
                    }
                    
                    while (!token.IsCancellationRequested)
                    {
                        // EN: Cycle through pre-generated overlays / FR: Cycler Ã  travers overlays prÃ©-gÃ©nÃ©rÃ©s
                        foreach (var overlay in composedOverlays)
                        {
                            if (token.IsCancellationRequested) break;
                            
                            if (File.Exists(overlay))
                            {
                                // EN: Use persistent slot 1 for cycle updates
                                // FR: Utiliser le slot persistant 1 pour les mises Ã  jour du cycle
                                await _mpv.OverlayImage(overlay, 1, "top-left");
                                _logger.LogDebug($"[RA MPV Cycle] Showing overlay: {Path.GetFileName(overlay)}");
                            }
                            
                            await Task.Delay(5000, token);
                        }
                        
                        // EN: During pause, show score-only / FR: Pendant la pause, afficher score seul
                        if (!string.IsNullOrEmpty(composedScoreOnly) && File.Exists(composedScoreOnly))
                        {
                             // EN: Show score-only on persistent slot 1
                             // FR: Afficher score seul sur le slot persistant 1
                            await _mpv.OverlayImage(composedScoreOnly, 1, "top-left");
                        }
                        
                        await Task.Delay(2000, token);
                    }
                    
                    // EN: Cleanup composed overlay files / FR: Nettoyer fichiers overlays composÃ©s
                    foreach (var overlay in composedOverlays)
                    {
                        try { if (File.Exists(overlay)) File.Delete(overlay); } catch { }
                    }
                    if (!string.IsNullOrEmpty(composedScoreOnly) && File.Exists(composedScoreOnly))
                    {
                        try { File.Delete(composedScoreOnly); } catch { }
                    }
                    _logger.LogInformation("[RA MPV Cycle] Cleaned up composed overlay files");
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[RA MPV Cycle] Cycle stopped");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA MPV Cycle] Error: {ex.Message}");
                }
            }, token);
        }
        
        /// <summary>
        /// EN: Start DMD badge ribbon cycling (4x4 groups, 5s each, 10s pause)
        /// FR: DÃ©marrer cycle badges DMD (groupes 4x4, 5s chacun, pause 10s)
        /// </summary>
        /// <summary>
        /// EN: Start DMD badge ribbon cycling provided badges/score/count parity
        /// FR: DÃ©marrer cycle badges DMD avec paritÃ© score/compteur
        /// </summary>
        private void StartBadgeRibbonCycle(int gameId, Dictionary<string, Achievement> achievements)
        {
            // EN: No longer skipping if timer active, as we now support composition
            // FR: On ne saute plus si un timer est actif, car on supporte maintenant la composition

            _badgeCycleCts?.Cancel();
            _badgeCycleCts?.Dispose();
            _badgeCycleCts = new CancellationTokenSource();
            
            var token = _badgeCycleCts.Token;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    // EN: Check what to show
                    var overlayConfig = _config.DmdRetroAchievementsOverlays; // score,badges,count
                    var showBadges = overlayConfig.Contains("badges", StringComparison.OrdinalIgnoreCase);
                    var showScore = overlayConfig.Contains("score", StringComparison.OrdinalIgnoreCase);
                    var showCount = overlayConfig.Contains("count", StringComparison.OrdinalIgnoreCase);

                    // EN: Group achievements by calculated capacity (if badges enabled)
                    List<List<Achievement>> groups = new List<List<Achievement>>();
                    if (showBadges && achievements != null && achievements.Count > 0)
                    {
                         var (badgeSize, maxBadgesPerFrame) = _imageService.GetBadgeRibbonCapacity(isDmd: true);
                         var sortedAchievements = achievements.Values.OrderBy(a => a.DisplayOrder).ToList();
                         groups = sortedAchievements
                            .Select((a, i) => new { Achievement = a, Index = i })
                            .GroupBy(x => x.Index / maxBadgesPerFrame)
                            .Select(g => g.Select(x => x.Achievement).ToList())
                            .ToList();
                         _logger.LogInformation($"[RA Badge Cycle] Created {groups.Count} groups of ~{maxBadgesPerFrame} badges");
                    }

                    // EN: Delay initial start if we just showed post-unlock overlays
                    // FR: DÃ©lai initial si on vient d'afficher les overlays post-dÃ©blocage
                    await Task.Delay(2000, token);

                    while (!token.IsCancellationRequested)
                    {
                        // EN: Wait if an achievement sequence is playing
                        while ((_dmdService.IsSequencePlaying || _isProcessingAchievement) && !token.IsCancellationRequested)
                        {
                            await Task.Delay(1000, token);
                        }

                        // 1. Show Badges (if any)
                        if (groups.Count > 0)
                        {
                            foreach (var group in groups)
                            {
                                if (token.IsCancellationRequested) break;
                                
                                var groupDict = group.ToDictionary(a => a.ID.ToString(), a => a);
                                var isHardcore = _raService?.IsHardcoreMode ?? false;
                                var ribbonPath = await _imageService.GenerateBadgeRibbonOverlay(
                                    groupDict, gameId, _raService!, isDmd: true, isHardcore: isHardcore);
                                
                                 if (!string.IsNullOrEmpty(ribbonPath) && File.Exists(ribbonPath))
                                {
                                    _currentDmdRibbonPath = ribbonPath; // Store for composition
                                    
                                    // EN: Only set direct overlay if NO challenges are active
                                    // FR: N'afficher l'overlay direct que si AUCUN dÃ©fi n'est actif
                                    if (_activeChallenges.Values.All(c => !c.IsActive))
                                    {
                                        await _dmdService.SetOverlayAsync(ribbonPath, 5000);
                                    }
                                    await Task.Delay(5000, token);
                                }
                            }
                        }

                        // 2. Show Count (during "Pause" phase)
                        if (showCount && !token.IsCancellationRequested)
                        {
                             // Refresh data
                             var currentAchievements = _raService?.CurrentGameAchievements;
                             if (currentAchievements != null)
                             {
                                 var isHardcore = _raService?.IsHardcoreMode ?? false;
                                 int unlocked = currentAchievements.Values.Count(a => isHardcore ? a.DateEarnedHardcore.HasValue : a.Unlocked);
                                 int total = currentAchievements.Count;
                                 var dmdCount = _imageService.GenerateAchievementCountOverlay(unlocked, total, isDmd: true, isHardcore: isHardcore);
                                 if (File.Exists(dmdCount))
                                 {
                                     await _dmdService.SetOverlayAsync(dmdCount, 4000);
                                     await Task.Delay(4000, token);
                                 }
                             }
                        }

                        // 3. Show Score (during "Pause" phase)
                        if (showScore && !token.IsCancellationRequested)
                        {
                            // Refresh data
                            int current = _raService?.CurrentGameUserPoints ?? 0;
                            int total = _raService?.CurrentGameTotalPoints ?? 0;
                            var isHardcore = _raService?.IsHardcoreMode ?? false;
                            var dmdScore = _imageService.GenerateScoreOverlay(current, total, isDmd: true, isHardcore: isHardcore);
                            if (File.Exists(dmdScore))
                            {
                                await _dmdService.SetOverlayAsync(dmdScore, 4000);
                                await Task.Delay(4000, token);
                            }
                        }

                        // 4. Brief Pause/Clear if we showed something
                        if (groups.Count > 0 || showScore || showCount)
                        {
                             _dmdService.ClearOverlay();
                             await Task.Delay(2000, token);
                        }
                        else
                        {
                            // Nothing to show? Wait logner to avoid busy loop
                            await Task.Delay(5000, token);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[RA Badge Cycle] Cycle stopped");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA Badge Cycle] Error: {ex.Message}");
                }
            }, token);
        }

        private async void OnChallengeUpdated(object? sender, ChallengeUpdatedEventArgs e)
        {
            if (!_gameRunning) return;
            
            try
            {
                // EN: Check config
                var mpvConfig = _config.MpvRetroAchievementsOverlays;
                var dmdConfig = _config.DmdRetroAchievementsOverlays;
                
                bool showOnMpv = mpvConfig.Contains("challenge", StringComparison.OrdinalIgnoreCase) || mpvConfig.Contains("all", StringComparison.OrdinalIgnoreCase);
                bool showOnDmd = dmdConfig.Contains("challenge", StringComparison.OrdinalIgnoreCase) || dmdConfig.Contains("all", StringComparison.OrdinalIgnoreCase);

                if (!showOnMpv && !showOnDmd)
                {
                    _activeChallenges.TryRemove(e.State.AchievementId, out _);
                    StopChallengeRefresh(e.State.AchievementId);
                    return;
                }

                // Update internal collection
                if (e.State.IsActive)
                    _activeChallenges[e.State.AchievementId] = e.State;
                else
                    _activeChallenges.TryRemove(e.State.AchievementId, out _);

                // MPV Overlay (ID 5 for Challenges)
                if (true && showOnMpv)
                {
                    if (e.State.IsActive)
                    {
                        // EN: If it's a timer, the cycle will handle the refresh
                        // FR: Si c'est un timer, le cycle gÃ©rera le rafraÃ®chissement
                    }
                    else
                    {
                         // Removed from active, cycle will pick it up on next iteration or cleaner will handle it
                         // If no more challenges for MPV, clear overlay
                         if (!_activeChallenges.Values.Any(c => c.IsActive)) // Check if any active left
                         {
                             _mpvChallengeCycleCts?.Cancel();
                             await _mpv.RemoveOverlay(5);
                         }
                    }
                    
                    // EN: Always ensure cycle is running if there are active challenges
                    if (!_activeChallenges.IsEmpty && (_mpvChallengeCycleCts == null || _mpvChallengeCycleCts.IsCancellationRequested))
                    {
                        StartMpvChallengeCycle();
                    }
                }
                else if (true && !showOnMpv)
                {
                    // Ensure it's cleared if it was shown before config change
                    _mpvChallengeCycleCts?.Cancel();
                    await _mpv.RemoveOverlay(5);
                }

                // DMD Overlay
                if (_config.DmdEnabled && showOnDmd)
                {
                    // EN: If any active challenges, ensure cycle is running
                    // FR: Si des dÃ©fis sont actifs, s'assurer que le cycle tourne
                    if (!_activeChallenges.IsEmpty)
                    {
                        // EN: If it's a new Timer, we must immediately interrupt existing Badge Ribbons
                        // FR: Si c'est un nouveau Timer, on doit interrompre immÃ©diatement les ribbons de badges
                        if (e.State.IsActive && e.State.Type == ChallengeType.Timer)
                        {
                            // _badgeCycleCts?.Cancel(); // No longer cancelling badge cycle, we compose now
                        }
                        
                        StartDmdChallengeCycle();
                    }
                    else
                    {
                        _dmdChallengeCycleCts?.Cancel();
                        _dmdService.ClearOverlay();
                        _currentDmdRibbonPath = null; // Clear ribbon track if challenges gone? Or keep last?
                                                      // Actually, if challenges gone, Resume Badge Cycle will handle it.

                        // EN: If no more challenges, check if we should resume badge ribbons
                        // FR: Si plus de dÃ©fis, vÃ©rifier si on doit reprendre les ribbons de badges
                        var currentAchievements = _raService?.CurrentGameAchievements;
                        if (currentAchievements != null && currentAchievements.Count > 0)
                        {
                            StartBadgeRibbonCycle(_raService!.CurrentGameId ?? 0, currentAchievements);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[MarqueeWorkflow] Error handling Challenge update: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Start DMD challenge cycling (alternating between all active challenges)
        /// FR: DÃ©marrer cycle de dÃ©fis DMD (alternance entre tous les dÃ©fis actifs)
        /// </summary>
        private void StartDmdChallengeCycle()
        {
            if (_dmdChallengeCycleCts != null && !_dmdChallengeCycleCts.IsCancellationRequested) return;

            _dmdChallengeCycleCts?.Cancel();
            _dmdChallengeCycleCts?.Dispose();
            _dmdChallengeCycleCts = new CancellationTokenSource();
            var token = _dmdChallengeCycleCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && !_activeChallenges.IsEmpty && _gameRunning)
                    {
                        // EN: Get a snapshot of active challenges
                        // FR: Prendre une capture des dÃ©fis actifs
                        var challenges = _activeChallenges.Values.ToList();
                        
                        var isHardcore = _raService?.IsHardcoreMode ?? false;
                        
                        foreach (var challenge in challenges)
                        {
                            if (token.IsCancellationRequested) break;

                            // EN: Special case: if it's a timer or leaderboard, we refresh more often (1s)
                            // FR: Cas spÃ©cial : si c'est un timer ou leaderboard, on rafraÃ®chit Ã  chaque seconde
                            if (challenge.Type == ChallengeType.Timer || challenge.Type == ChallengeType.Leaderboard)
                            {
                                int timerDuration = 5; // Show for 5 seconds but update every 1s
                                for (int i = 0; i < timerDuration; i++)
                                {
                                    if (token.IsCancellationRequested || !challenge.IsActive) break;
                                    // EN: Pass the current ribbon path if available for composite rendering
                                    // FR: Passer le chemin du ruban actuel si disponible pour le rendu composite
                                    await _dmdService.PlayChallengeNotificationAsync(challenge, isHardcore, _currentDmdRibbonPath);
                                    await Task.Delay(1000, token);
                                }
                            }
                            else
                            {
                                // EN: Progress / Other
                                // FR: ProgrÃ¨s / Autre
                                await _dmdService.PlayChallengeNotificationAsync(challenge, isHardcore, _currentDmdRibbonPath);
                                await Task.Delay(5000, token);
                            }
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA DMD Challenge Cycle] Error: {ex.Message}");
                }
                finally
                {
                    _dmdChallengeCycleCts = null;
                }
            }, token);
        }

        /// <summary>
        /// EN: Start MPV challenge cycling (alternating between all active challenges)
        /// FR: DÃ©marrer cycle de dÃ©fis MPV (alternance entre tous les dÃ©fis actifs)
        /// </summary>
        private void StartMpvChallengeCycle()
        {
            if (_mpvChallengeCycleCts != null && !_mpvChallengeCycleCts.IsCancellationRequested) return;

            _mpvChallengeCycleCts?.Cancel();
            _mpvChallengeCycleCts?.Dispose();
            _mpvChallengeCycleCts = new CancellationTokenSource();
            var token = _mpvChallengeCycleCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested && !_activeChallenges.IsEmpty && _gameRunning)
                    {
                        // EN: Get a snapshot of active challenges (Keys only to re-fetch fresh state)
                        // FR: Prendre une capture des dÃ©fis actifs (ClÃ©s seulement pour rÃ©cupÃ©rer Ã©tat frais)
                        var challengeIds = _activeChallenges.Keys.ToList();

                        if (challengeIds.Count == 0) break;

                        foreach (var id in challengeIds)
                        {
                            if (token.IsCancellationRequested) break;

                            // EN: Re-fetch string-fresh state
                            if (!_activeChallenges.TryGetValue(id, out var challenge) || !challenge.IsActive) continue;

                            // EN: Special case: if it's a timer or leaderboard, we refresh more often (1s)
                            if (challenge.Type == ChallengeType.Timer || challenge.Type == ChallengeType.Leaderboard)
                            {
                                int timerDuration = 5; // Show for 5 seconds but update every 1s
                                for (int i = 0; i < timerDuration; i++)
                                {
                                    if (token.IsCancellationRequested) break;
                                    
                                    // Refresh state again directly from dictionary for live timer updates
                                    if (_activeChallenges.TryGetValue(id, out var liveChallenge) && liveChallenge.IsActive)
                                    {
                                        await RefreshChallengeMpvOverlay(liveChallenge);
                                    }
                                    else break; // Challenge gone

                                    await Task.Delay(1000, token);
                                }
                            }
                            else
                            {
                                // EN: Progress / Other
                                await RefreshChallengeMpvOverlay(challenge);
                                await Task.Delay(5000, token);
                            }
                        }
                    }
                    
                    // Cleanup when empty
                    if (!token.IsCancellationRequested) await _mpv.RemoveOverlay(5);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"[RA MPV Challenge Cycle] Error: {ex.Message}");
                }
                finally
                {
                    _mpvChallengeCycleCts = null;
                }
            }, token);
        }

        private void StartChallengeRefresh(ChallengeState state)
        {
             // Deprecated for MPV - handled by Cycle now. Keeping empty or for DMD if needed? 
             // DMD has its own cycle. This method was likely MPV specific.
             // Leaving empty to satisfy any lingering callers safely.
        }

        private void StopChallengeRefresh(int achievementId)
        {
             // Deprecated
        }

        /// <summary>
        /// EN: Unified cleanup for all RetroAchievements overlays and background task cycles
        /// FR: Nettoyage unifiÃ© pour tous les vidÃ©os RA et cycles de tÃ¢ches en arriÃ¨re-plan
        /// </summary>
        private async Task CleanupAllOverlays()
        {
            _logger.LogInformation("[RA Workflow] Cleaning up all RA overlays and cycles...");
            
            try
            {
                // 1. Stop all background cycles/timers (Achievement cycles)
                if (_badgeCycleCts != null)
                {
                    _badgeCycleCts.Cancel();
                    _badgeCycleCts = null;
                }

                if (_dmdChallengeCycleCts != null)
                {
                    _dmdChallengeCycleCts.Cancel();
                    _dmdChallengeCycleCts = null;
                }

                if (_mpvChallengeCycleCts != null)
                {
                    _mpvChallengeCycleCts.Cancel();
                    _mpvChallengeCycleCts = null;
                }

                if (_narrationCts != null)
                {
                    _narrationCts.Cancel();
                    _narrationCts.Dispose();
                    _narrationCts = null;
                }

                _activeChallenges.Clear();

                // 2. Stop all Challenge refresh tasks (Deprecated but safe cleanup)
                foreach (var kvp in _challengeRefreshCts.ToArray())
                {
                    if (_challengeRefreshCts.TryRemove(kvp.Key, out var cts))
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                }

                // 3. Remove MPV Overlays (All slots used by RA/RP)
                if (true)
                {
                    StopMpvRpStatRotation(); // Stop MPV RP Rotation Loop
                    
                    // EN: Clear persistent generic stats
                    // FR: Effacer les stats gÃ©nÃ©riques persistantes
                    lock(_currentMpvRpGenericStats) { _currentMpvRpGenericStats.Clear(); }
                    
                    // EN: Clear slots 1-12 to be exhaustive (includes RA notifications, scores, and preview slots)
                    // FR: Nettoyer les slots 1-12 pour Ãªtre exhaustif (inclus notifications RA, scores et slots preview)
                    for (int i = 1; i <= 12; i++)
                    {
                        await _mpv.RemoveOverlay(i, cancelTimer: (i == 1 || i >= 10)); 
                    }
                    await _mpv.ClearRetroAchievementData(); // OSD Lua
                }

                // 4. Clear DMD
                if (_config.DmdEnabled)
                {
                    StopDmdRpStatRotation(); // EN: Stop RP rotation first / FR: ArrÃªter la rotation RP d'abord
                    
                    // EN: Explicitly clear persistent RP stats to prevent stale data on next game
                    // FR: Effacer explicitement les stats RP persistantes pour Ã©viter les donnÃ©es obsolÃ¨tes au prochain jeu
                    lock(_currentDmdRpStats) { _currentDmdRpStats.Clear(); }
                    
                    _dmdService.ClearOverlay();
                    await _dmdService.SetDmdPersistentScoreAsync(""); // EN: Clear persistent score / FR: Effacer le score permanent
                    await _dmdService.SetDmdPersistentLayoutAsync(Array.Empty<byte>()); // EN: Explicitly clear persistent RP layout / FR: Effacer explicitement le layout RP persistant
                }

                // 5. Cleanup physical cache (Force full purge on game stop/start)
                _imageService.CleanupCache(forceFull: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RA Workflow] Error during unified cleanup: {ex.Message}");
            }
        }

        private async Task RefreshChallengeMpvOverlay(ChallengeState state)
        {
            if (!true || !_gameRunning) return;

            bool isHc = _raService?.IsHardcoreMode ?? false;
            var overlayPath = _imageService.GenerateChallengeOverlay(state, _config.MarqueeWidth, _config.MarqueeHeight, isHc);
            if (!string.IsNullOrEmpty(overlayPath))
            {
                await _mpv.OverlayImage(overlayPath, 5);
            }
        }

        private async void OnRichPresenceUpdated(object? sender, string rpText)
        {
            // EN: If an achievement is being processed, suppress RP updates to avoid visual conflict/race conditions
            // FR: Si un succÃ¨s est en cours de traitement, supprimer les MAJ RP pour Ã©viter conflits visuels/race conditions
            if (_isProcessingAchievement)
            {
                _logger.LogInformation($"[RA Workflow] RP Update SUPPRESSED due to active achievement processing: {rpText}");
                return;
            }

            var mpvOverlays = _config.MpvRetroAchievementsOverlays;
            var dmdOverlays = _config.DmdRetroAchievementsOverlays;

            try
            {
                var isHc = _raService?.IsHardcoreMode ?? false;
                var currentState = _imageService.ParseRichPresence(rpText);
                var target = _config.MarqueeRetroAchievementsDisplayTarget; // dmd, mpv, both

                // 1. MPV Update
                if (target != "dmd" && mpvOverlays.Contains("items", StringComparison.OrdinalIgnoreCase))
                {
                    // EN: Calculate vertical offset for items
                    int persistentOverlayHeight = Math.Max(10, _config.MarqueeHeight / 16); 
                    int itemOffset = persistentOverlayHeight + 15; 

                    if (currentState.Stats.Count > 0 && currentState.IsStatsChanged(_lastRpState))
                    {
                        lock(_currentMpvRpGenericStats) { _currentMpvRpGenericStats.Clear(); }
                        
                        foreach (var kvp in currentState.Stats)
                        {
                            var key = kvp.Key.ToLowerInvariant();
                            string alignment = "top-left";
                            int slot = 8;
                            int nudge = itemOffset;
                            bool isScoreItem = false;
                            bool isGeneric = true;

                            if (key.Contains("score") || key.Contains("ðŸ’µ")) { alignment = "top-right"; slot = 3; isScoreItem = true; isGeneric = false; }
                            else if (key.Contains("lives") || key.Contains("â¤ï¸") || key.Contains("vies")) { alignment = "top-left"; slot = 6; isGeneric = false; }
                            else if (key.Contains("weapon") || key.Contains("arme") || key.Contains("ðŸ”«")) { alignment = "bottom-right"; slot = 7; nudge = 0; isGeneric = false; }
                            else if (key.Contains("lap") || key.Contains("tour")) { alignment = "top-left"; slot = 9; isGeneric = false; } 
                            else if (key.Contains("rank") || key.Contains("pos")) { alignment = "top-right"; slot = 10; isGeneric = false; }

                            if (isGeneric)
                            {
                                lock(_currentMpvRpGenericStats) { _currentMpvRpGenericStats[kvp.Key] = kvp.Value; }
                            }
                            else
                            {
                                var itemPath = _imageService.GenerateRichPresenceItemOverlay(kvp.Key, kvp.Value, isHc, _config.MarqueeWidth, _config.MarqueeHeight, isScore: isScoreItem, alignment: alignment, yOffset: nudge);
                                if (File.Exists(itemPath)) await _mpv.OverlayImage(itemPath, slot);
                            }
                        }

                        // Handle Generic Stats Rotation vs Static
                        int genericCount;
                        lock(_currentMpvRpGenericStats) { genericCount = _currentMpvRpGenericStats.Count; }

                        if (genericCount > 1)
                        {
                            StartMpvRpStatRotation(persistentOverlayHeight);
                        }
                        else if (genericCount == 1)
                        {
                            StopMpvRpStatRotation();
                            // Display the single generic item
                            var first = _currentMpvRpGenericStats.First();
                            var itemPath = _imageService.GenerateRichPresenceItemOverlay(first.Key, first.Value, isHc, _config.MarqueeWidth, _config.MarqueeHeight, isScore: false, alignment: "top-left", yOffset: itemOffset);
                            if (File.Exists(itemPath)) await _mpv.OverlayImage(itemPath, 8);
                        }
                        else
                        {
                             StopMpvRpStatRotation();
                             await _mpv.RemoveOverlay(8, false);
                        }
                    }
                    else if (currentState.Stats.Count == 0 && (_lastRpState?.Stats.Count ?? 0) > 0)
                    {
                        await _mpv.RemoveOverlay(3, false);
                        await _mpv.RemoveOverlay(6, false);
                        await _mpv.RemoveOverlay(7, false);
                        await _mpv.RemoveOverlay(8, false);
                        await _mpv.RemoveOverlay(9, false);
                        await _mpv.RemoveOverlay(10, false);
                    }

                    if (!string.IsNullOrWhiteSpace(currentState.Narrative) && currentState.IsNarrativeChanged(_lastRpState))
                    {
                        var narrationResult = await _imageService.GenerateRichPresenceOverlay(currentState.Narrative, isHc, _config.MarqueeWidth, _config.MarqueeHeight, position: "center");
                        if (narrationResult.IsValid && File.Exists(narrationResult.Path))
                        {
                            // EN: Cancel previous narration timeout
                            // FR: Annuler le timeout de la narration prÃ©cÃ©dente
                            _narrationCts?.Cancel();
                            _narrationCts?.Dispose();
                            _narrationCts = new CancellationTokenSource();
                            var token = _narrationCts.Token;
                            var narrationId = Guid.NewGuid();
                            _currentNarrationId = narrationId;

                            // EN: Use GIF path (transparency works correctly with GIF)
                            // FR: Utiliser le chemin GIF (la transparence fonctionne correctement avec GIF)
                            string mpvPath = narrationResult.Path; 
                            _activeNarrationPath = mpvPath;
                            _activeNarrationPosition = $"{narrationResult.X}:{narrationResult.Y}";
                            
                            await _mpv.OverlayImage(mpvPath, 4, _activeNarrationPosition, loopCount: 0);
                            
                            // EN: Return to transparent placeholder after duration to maintain MPV continuity
                            // FR: Revenir au placeholder transparent aprÃ¨s la durÃ©e
                            int durationMs = narrationResult.DurationMs > 0 ? narrationResult.DurationMs : 10000; // Default 10s if not set
                            
                            // EN: Cleanup narration after duration
                            // FR: Nettoyer la narration aprÃ¨s la durÃ©e
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await Task.Delay(durationMs, token);
                                    if (!token.IsCancellationRequested && _currentNarrationId == narrationId)
                                    {
                                        await _mpv.RemoveOverlay(4, false);
                                        _activeNarrationPath = null;
                                        _activeNarrationPosition = null;
                                        _currentNarrationId = Guid.Empty;
                                        _logger.LogInformation($"[RA Workflow] Narration expired ({durationMs}ms). Slot 4 removed.");
                                        
                                        // EN: Trigger a refresh of RA overlays to ensure visibility
                                        // FR: DÃ©clencher un rafraÃ®chissement des overlays RA pour assurer la visibilitÃ©
                                        await RefreshMpvOverlays();
                                    }
                                }
                                catch (OperationCanceledException) { }
                                catch (Exception ex) { _logger.LogError($"[RA Workflow] Error on narration cleanup: {ex.Message}"); }
                            }, token);
                        }
                    }
                }

                // 2. DMD Update
                if (target != "mpv" && dmdOverlays.Contains("items", StringComparison.OrdinalIgnoreCase))
                {
                    if ((DateTime.Now - _lastDmdRpUpdate).TotalSeconds >= 0.4)
                    {
                        _lastDmdRpUpdate = DateTime.Now;
                        bool statsChanged = currentState.IsStatsChanged(_lastRpState);
                        bool narrativeChanged = currentState.IsNarrativeChanged(_lastRpState);

                        if (statsChanged || narrativeChanged)
                        {
                            // EN: Always update the stored stats snapshot for the rotation loop
                            // FR: Toujours mettre Ã  jour le snapshot des stats pour la boucle de rotation
                            lock(_currentDmdRpStats)
                            {
                                _currentDmdRpStats.Clear();
                                foreach(var kvp in currentState.Stats) _currentDmdRpStats[kvp.Key] = kvp.Value;
                            }

                            // 1. Handle Stats Rotation
                            // FR: GÃ©rer la rotation des statistiques
                            var rpStats = currentState.Stats.Where(kvp => {
                                string lk = kvp.Key.ToLowerInvariant();
                                return !(lk.Contains("score") || lk.Contains("ðŸ’µ") || lk.Contains("live") || lk.Contains("â™¥") || lk.Contains("vies") || lk.Contains("weapon") || lk.Contains("arme") || lk.Contains("ðŸ”«"));
                            }).ToList();

                            if (rpStats.Count > 1)
                            {
                                StartDmdRpStatRotation(); // No need to pass stats anymore
                            }
                            else
                            {
                                StopDmdRpStatRotation();
                                // Generate Static Composition
                                bool useGrayscale = _config.GetSetting("DmdForceMono", "false") == "true";
                                var dmdComposition = _imageService.GenerateDmdRichPresenceComposition(currentState.Stats, _config.DmdWidth, _config.DmdHeight, useGrayscale);
                                if (dmdComposition != null && dmdComposition.Length > 0)
                                {
                                    await _dmdService.SetDmdPersistentLayoutAsync(dmdComposition);
                                }
                            }

                            _dmdRpDisplayExpiry = DateTime.Now.AddSeconds(10);
                            
                            // 2. Handle Narrative (Scrolling Text)
                            // FR: GÃ©rer la Narration (Texte dÃ©filant)
                            if (narrativeChanged && !string.IsNullOrEmpty(currentState.Narrative))
                            {
                                string cleanNarrative = _imageService.CleanRichPresenceString(currentState.Narrative, stripEmojis: false);
                                if (!string.IsNullOrEmpty(cleanNarrative))
                                {
                                     // Get layout definition for rp_narration
                                     var narrationItem = _templateService.GetItem("dmd", "rp_narration");
                                     int? yOverride = null;
                                     int? hOverride = null;
                                     string? colorOverride = null;
                                     float? fontSizeOverride = null;
                                     if (narrationItem != null && narrationItem.IsEnabled)
                                     {
                                         yOverride = narrationItem.Y;
                                         hOverride = narrationItem.Height;
                                         colorOverride = narrationItem.TextColor;
                                         if (narrationItem.FontSize > 0) fontSizeOverride = narrationItem.FontSize;
                                     }

                                     await _dmdService.PlayRichPresenceNotificationAsync(cleanNarrative, 5000, yOverride, hOverride, colorOverride, fontSizeOverride);

                                     // EN: Auto-clear DMD notification after duration (only clears notification layer, keeps stats)
                                     // FR: Effacement auto de la notification DMD aprÃ¨s la durÃ©e (efface seulement la couche notification)
                                     _ = Task.Delay(5500).ContinueWith(_ => {
                                         if (DateTime.Now >= _dmdRpDisplayExpiry)
                                         {
                                             _dmdService.ClearOverlay();
                                         }
                                     });
                                }
                            }
                        }
                    }
                }
                _lastRpState = currentState;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA Workflow] Failed to update Rich Presence overlay: {ex.Message}");
            }
        }

        private CancellationTokenSource? _previewCts;
        private Task? _bgPreviewTask;

        private async Task HandlePreviewOverlay(string screenType)
        {
            _logger.LogInformation($"[Preview] Dispatching live preview request for: {screenType}");

            // 1. Cancel previous preview task if any
            if (_previewCts != null)
            {
                _previewCts.Cancel();
                _previewCts.Dispose();
            }
            _previewCts = new CancellationTokenSource();
            var token = _previewCts.Token;

            // 2. Chain execution to ensure strict serialization (New task waits for Old to finish/cancel)
            var previousTask = _bgPreviewTask;
            _bgPreviewTask = Task.Run(async () =>
            {
                // Wait for previous to fully stop (it received Cancel signal above)
                if (previousTask != null)
                {
                    try { await previousTask; } catch { }
                }

                if (token.IsCancellationRequested) return;

                await HandlePreviewOverlayInternal(screenType, token);
            }, token);

            // 3. Return immediately to unblock the IPC Server!
            await Task.CompletedTask;
        }

        private async Task HandlePreviewOverlayInternal(string screenType, CancellationToken token)
        {
            _logger.LogInformation($"[Preview] Starting background generation for: {screenType}");
            
            // EN: Force reload of overlay layout to pick up latest editor changes
            // FR: Force le rechargement de la mise en page d'overlay pour rÃ©cupÃ©rer les derniers changements de l'Ã©diteur
            try
            {
                _templateService.Reload();
                _logger.LogInformation("[Preview] Layout configuration reloaded.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Preview] Failed to reload layout: {ex.Message}");
            }

            // 0. Cleanup Cache before generation to avoid bloating with previous test files
            // EN: Use false for forceFull to keep recent ones to avoid flicker risk, but clean old ones
            _imageService.CleanupCache(forceFull: false);

            // 1. Force reload of current background image to ensure MPV is active and unpaused
            // EN: Simulate "System Selected" by reloading the current image
            // FR: Simuler "System Selected" en rechargeant l'image actuelle
            try
            {
                if (true && !string.IsNullOrEmpty(_currentMarqueePath))
                {
                    _logger.LogInformation($"[Preview] Reloading current marquee to force refresh: {_currentMarqueePath}");
                    await _mpv.DisplayImage(_currentMarqueePath, loop: true); // loop=true matches standard display
                    await Task.Delay(200, token); // Wait for load
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Preview] Failed to reload background image: {ex.Message}");
            }

            // 2. Clear existing overlays immediately (Explicit loop to ensure visual clear)
            try 
            {
                if (true)
                {
                    for (int i = 1; i <= 12; i++) await _mpv.RemoveOverlay(i);
                }
                
                await Task.Delay(50, token);
            }
            catch { /* Ignore cleanup errors */ }

            // Ensure CleanupAllOverlays is still called for internal state
            await CleanupAllOverlays();

            bool isDmd = screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase);

            try
            {
                if (isDmd)
                {
                    // Full Composite Preview for DMD
                    await _dmdService.PlayFullPreviewAsync();
                }
                else
                {
                    // Full MPV Multi-Slot Preview
                    var width = _config.MarqueeWidth;
                    var height = _config.MarqueeHeight;

                    if (token.IsCancellationRequested) return;

                    // EN: Display Loading Indicator immediately
                    // FR: Afficher l'indicateur de chargement immÃ©diatement
                    var loadingPath = _imageService.GeneratePreviewStatusOverlay("Wait preview load...", width, height);
                    if (!string.IsNullOrEmpty(loadingPath) && File.Exists(loadingPath)) 
                    {
                        await _mpv.OverlayImage(loadingPath, 8);
                    }

                    // EN: Parallelize image generation
                    // FR: ParallÃ©liser la gÃ©nÃ©ration d'images
                    var dummyAchievements = new Dictionary<string, Achievement>();
                    if (_raService != null)
                    {
                        for (int i = 0; i < 10; i++)
                            dummyAchievements.Add(i.ToString(), new Achievement { ID = 0, Title = "Test", Unlocked = (i % 2 == 0), DisplayOrder = i });
                    }

                    // Launch all generation tasks
                    var t1 = Task.Run<string>(() => _imageService.GenerateMpvAchievementOverlay(string.Empty, "TEST SUCCESS", "Unlock Preview", 100, false, "overlays", true) ?? string.Empty, token);
                    var t2 = Task.Run<string>(async () => (_raService != null) ? (await _imageService.GenerateBadgeRibbonOverlay(dummyAchievements, 0, _raService, false, false, true)) ?? string.Empty : string.Empty, token); // Force Regenerate
                    var t3 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Score", "1,234,567", true, width, height, false, false, "top-right", 0, true, token), token);
                    var t4 = _imageService.GenerateRichPresenceOverlay("This is a live preview showing how all elements fit together on your Marquee.", true, width, height, null, "center", true, token);
                    var t5 = Task.Run<string>(() => _imageService.GenerateAchievementCountOverlay(15, 35, false, isHardcore: true, true) ?? string.Empty, token); // Force Regenerate
                    var t6 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Lives", "3", true, width, height, false, false, "top-left", 50, true, token), token);
                    var t7 = Task.Run<string>(() => _imageService.GenerateChallengeOverlay(new ChallengeState { IsActive = true, Title = "Badge Challenge", Description = "With Image", CurrentValue = 5, TargetValue = 10, BadgePath = "dummy.png" }, width, height, true) ?? string.Empty, token);
                    var t8 = Task.Run<string>(() => _imageService.GeneratePreviewStatusOverlay("Preview Mode Active", width, height) ?? string.Empty, token);
                    var t9 = Task.Run<string>(() => _imageService.GenerateScoreOverlay(1234, 5000, isDmd: false, isHardcore: true, true) ?? string.Empty, token); // Force Regenerate
                    var t10 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Stats", "x5 Multiplier", true, width, height, false, false, "top-left", 80, true, token), token);
                    var t11 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Weapon", "Plasma Gun", true, width, height, false, false, "bottom-right", 0, true, token), token);
                    var t12 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Lap", "Lap 2/3", true, width, height, false, false, "top-left", 120, true, token), token);
                    var t13 = Task.Run<string>(() => _imageService.GenerateRichPresenceItemOverlay("Rank", "Pos 1/12", true, width, height, false, false, "top-right", 120, true, token), token);

                    // Wait for all
                    try 
                    {
                        await Task.WhenAll(new Task[] { t1, t2, t3, t4, t5, t6, t7, t8, t9, t10, t11, t12, t13 });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[Preview] One or more generation tasks failed: {ex.Message}");
                        // We continue to try showing whatever succeeded, or rethrow?
                        // If we don't rethrow, code below executes. T.Result might throw.
                        // Better to check task status before accessing Result below.
                    }

                    if (token.IsCancellationRequested) return;

                    // Helper to safely overlay if file exists
                    async Task SafeOverlay(string? path, int slot)
                    {
                        if (token.IsCancellationRequested) return;
                        
                        // EN: Explicitly remove before adding to force refresh in MPV
                        // FR: Supprimer explicitement avant d'ajouter pour forcer le rafraÃ®chissement dans MPV
                        await _mpv.RemoveOverlay(slot);
                        await Task.Delay(50, token); // Brief pause to prevent command flooding/race

                        if (!string.IsNullOrEmpty(path) && File.Exists(path)) 
                        {
                            await _mpv.OverlayImage(path, slot);
                        }
                    }

                    // Apply Overlays Sequentially
                    // Apply Overlays Sequentially (Robustly)
                    // Helper to get result safe
                    string? GetResultOrNull(Task<string> t) => (t.Status == TaskStatus.RanToCompletion) ? t.Result : null;
                    OverlayResult? GetOverlayResultOrNull(Task<OverlayResult> t) => (t.Status == TaskStatus.RanToCompletion) ? t.Result : null;

                    await SafeOverlay(GetResultOrNull(t1), 1);
                    await SafeOverlay(GetResultOrNull(t2), 2);
                    await SafeOverlay(GetResultOrNull(t3), 3);
                    
                    var narrationRes = GetOverlayResultOrNull(t4);
                    if (narrationRes != null && narrationRes.IsValid)
                        await _mpv.OverlayImage(narrationRes.Path, 4, $"{narrationRes.X}:{narrationRes.Y}");

                    await SafeOverlay(GetResultOrNull(t5), 5);
                    await SafeOverlay(GetResultOrNull(t6), 6);
                    await SafeOverlay(GetResultOrNull(t7), 7);
                    await SafeOverlay(GetResultOrNull(t8), 8);
                    await SafeOverlay(GetResultOrNull(t9), 12); // Persistent Score (Moved to 12)
                    await SafeOverlay(GetResultOrNull(t10), 8); // Stats (Slot 8)
                    await SafeOverlay(GetResultOrNull(t11), 7); // Weapon (Slot 7)
                    await SafeOverlay(GetResultOrNull(t12), 9); // Lap (Slot 9)
                    await SafeOverlay(GetResultOrNull(t13), 10); // Rank (Slot 10)

                    // EN: RefreshPlayer removed in favor of Image Reload strategy
                    // FR: RefreshPlayer supprimÃ© au profit de la stratÃ©gie Image Reload
                    // await _mpv.RefreshPlayer();

                    // Auto-cleanup preview after 30s (cancelled if new preview starts)
                    _ = Task.Run(async () => 
                    {
                        try 
                        {
                            await Task.Delay(30000, token);
                            if (!token.IsCancellationRequested) await CleanupAllOverlays();
                        }
                        catch (OperationCanceledException) { }
                    }, token);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[Preview] Operation cancelled during internal generation.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[Preview] Failed to generate exhaustive preview: {ex.Message}");
            }
        }
        public void Dispose()
        {
            if (_inputService != null)
            {
                _inputService.OnMoveCommand -= HandleMoveCommand;
            }
            StopDmdRpStatRotation();
            StopMpvRpStatRotation();
            _badgeCycleCts?.Cancel();
            _badgeCycleCts?.Dispose();
            _mpvBadgeCycleCts?.Cancel();
            _mpvBadgeCycleCts?.Dispose();
            _dmdService.Stop();
        }

        /// <summary>
        /// EN: Start rotation for multiple RP statistics on DMD (2s interval)
        /// FR: DÃ©marrer la rotation des statistiques RP sur DMD (intervalle de 2s)
        /// </summary>
        private void StartDmdRpStatRotation()
        {
            if (_dmdRpStatRotationCts != null) return; // Already running

            _dmdRpStatRotationCts = new CancellationTokenSource();
            var token = _dmdRpStatRotationCts.Token;

            _ = Task.Run(async () =>
            {
                _logger.LogInformation("[DMD RP] Starting Stat Rotation loop.");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        Dictionary<string, string> statsClone;
                        lock(_currentDmdRpStats)
                        {
                            statsClone = new Dictionary<string, string>(_currentDmdRpStats);
                        }

                        // Identify rp_stat items to count them for rotation
                        var rpStatsCount = statsClone.Count(kvp => {
                            string lk = kvp.Key.ToLowerInvariant();
                            return !(lk.Contains("score") || lk.Contains("ðŸ’µ") || lk.Contains("live") || lk.Contains("â™¥") || lk.Contains("vies") || lk.Contains("weapon") || lk.Contains("arme") || lk.Contains("ðŸ”«"));
                        });

                        if (rpStatsCount <= 1) break; 

                        bool useGrayscale = _config.GetSetting("DmdForceMono", "false") == "true";
                        var dmdComposition = _imageService.GenerateDmdRichPresenceComposition(statsClone, _config.DmdWidth, _config.DmdHeight, useGrayscale, _dmdRpStatIndex);
                        
                        if (dmdComposition != null && dmdComposition.Length > 0)
                        {
                            await _dmdService.SetDmdPersistentLayoutAsync(dmdComposition);
                        }

                        await Task.Delay(2000, token);
                        _dmdRpStatIndex = (_dmdRpStatIndex + 1) % rpStatsCount;
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"[DMD RP] Rotation Error: {ex.Message}");
                }
                finally
                {
                    _dmdRpStatRotationCts = null;
                }
            }, token);
        }

        private void StopDmdRpStatRotation()
        {
            if (_dmdRpStatRotationCts != null)
            {
                _dmdRpStatRotationCts.Cancel();
                _dmdRpStatRotationCts.Dispose();
                _dmdRpStatRotationCts = null;
                _dmdRpStatIndex = 0;
            }
        }

        // EN: MPV Rotation Fields
        // FR: Champs pour la rotation MPV
        private CancellationTokenSource? _mpvRpStatRotationCts;
        private int _mpvRpStatIndex = 0;
        private Dictionary<string, string> _currentMpvRpGenericStats = new();

        private void StartMpvRpStatRotation(int persistentOverlayHeight)
        {
            if (_mpvRpStatRotationCts != null) return;

            _mpvRpStatRotationCts = new CancellationTokenSource();
            var token = _mpvRpStatRotationCts.Token;

            _ = Task.Run(async () =>
            {
                _logger.LogInformation("[MPV RP] Starting Stat Rotation loop.");
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        List<KeyValuePair<string, string>> statsList;
                        lock(_currentMpvRpGenericStats)
                        {
                            statsList = _currentMpvRpGenericStats.ToList();
                        }

                        if (statsList.Count <= 1) break;

                        if (_mpvRpStatIndex >= statsList.Count) _mpvRpStatIndex = 0;
                        var kvp = statsList[_mpvRpStatIndex];

                        // Generate Overlay for Slot 8
                        var isHc = _raService?.IsHardcoreMode ?? false;
                        int itemOffset = persistentOverlayHeight + 15;
                        var itemPath = _imageService.GenerateRichPresenceItemOverlay(kvp.Key, kvp.Value, isHc, _config.MarqueeWidth, _config.MarqueeHeight, isScore: false, alignment: "top-left", yOffset: itemOffset);
                        
                        if (File.Exists(itemPath))
                        {
                             await _mpv.OverlayImage(itemPath, 8);
                        }

                        await Task.Delay(2000, token);
                        _mpvRpStatIndex = (_mpvRpStatIndex + 1) % statsList.Count;
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError($"[MPV RP] Rotation Error: {ex.Message}");
                }
                finally
                {
                    _mpvRpStatRotationCts = null;
                }
            }, token);
        }

        private void StopMpvRpStatRotation()
        {
            if (_mpvRpStatRotationCts != null)
            {
                _mpvRpStatRotationCts.Cancel();
                _mpvRpStatRotationCts.Dispose();
                _mpvRpStatRotationCts = null;
                _mpvRpStatIndex = 0;
            }
        }
    }
}
