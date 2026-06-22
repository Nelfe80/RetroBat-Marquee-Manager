using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Configuration;

namespace RetroBatMarqueeManager.Application.Services
{
    /// <summary>
    /// Service to find marquee files (systems, collections, games)
    /// FR: Service pour trouver les fichiers marquee (systèmes, collections, jeux)
    /// </summary>
    public class MarqueeFileFinderService : IMarqueeFileFinder
    {
        private readonly IConfigService _config;
        private readonly IEsSettingsService _esSettings;
        private readonly ImageConversionService _imageService;
        private readonly IScraperManager _scraperManager;
        private readonly OffsetStorageService _offsetService;
        private readonly ILogger<MarqueeFileFinderService> _logger;
        
        private bool _isInitialized = true; // Assumed initialized if constructed via DI with settings
        
        // State for refreshing composition
        private string _lastSystem = "";
        private string _lastGame = "";
        private string _lastTitle = "";
        private string _lastRom = "";

        public MarqueeFileFinderService(
            IConfigService config,
            IEsSettingsService esSettings,
            ImageConversionService imageService,
            OffsetStorageService offsetService,
            IScraperManager scraperManager,
            ILogger<MarqueeFileFinderService> logger)
        {
            _config = config;
            _esSettings = esSettings;
            _imageService = imageService;
            _offsetService = offsetService;
            _scraperManager = scraperManager;
            _logger = logger;
        }

        public Task InitializeAsync(string esSettingsPath)
        {
            // _esSettings is already initialized via DI
            _logger.LogInformation("MarqueeFileFinder initialized (Settings loaded via Service)");
            return Task.CompletedTask;
        }

        public async Task<string?> FindMarqueeFileAsync(string eventType, string param1, string param2, string param3, string param4)
        {
            if (!_isInitialized)
            {
                _logger.LogWarning("MarqueeFileFinder not initialized, using default image");
                return _config.DefaultImagePath;
            }

            return eventType.ToLowerInvariant() switch
            {
                "system-selected" => FindSystemMarquee(param1),
                "game-selected" => await FindGameMarqueeAsync(param1, param2, param3, param4, allowVideoGeneration: false, isGameStart: false), // User Request: No gen on selected
                "game-start" => await FindGameMarqueeAsync(param1, param2, param3, param4, allowVideoGeneration: true, isGameStart: true),     // Gen only on start
                _ => _config.DefaultImagePath
            };
        }

        private List<string> GetThemeSearchPaths(string primaryPath)
        {
            var paths = new List<string> { primaryPath };
            // Fallback for GitHub downloaded themes (es-theme-carbon-master)
            if (primaryPath.Contains("es-theme-carbon") && !primaryPath.Contains("es-theme-carbon-master"))
            {
               paths.Add(primaryPath.Replace("es-theme-carbon", "es-theme-carbon-master"));
            }
            return paths;
        }

        private string? FindSystemMarquee(string systemName, bool raw = false)
        {
            _logger.LogInformation($"FindSystemMarquee: {systemName} (raw: {raw})");

            // Collections start with underscore in ES events (e.g. "_favorites")
            if (systemName.StartsWith("_"))
            {
                // Remove underscore for processing
                return FindCollectionMarquee(systemName.TrimStart('_'), raw);
            }
            
            // Check Correlation (User request: all -> allgames)
            if (_config.CollectionCorrelation.TryGetValue(systemName, out var correlatedName))
            {
                _logger.LogInformation($"Mapped system '{systemName}' to '{correlatedName}' via CollectionCorrelation");
                systemName = correlatedName;
            }

            var languageCode = _esSettings.GetLanguageCode(); // ex: "fr"
            
            // Determine all system name candidates (Original + Aliases)
            var systemCandidates = new List<string> { systemName };
            if (_config.SystemAliases.TryGetValue(systemName, out var alias))
            {
                _logger.LogInformation($"Found alias for '{systemName}': '{alias}'");
                systemCandidates.Add(alias);
            }

            // Build paths to search for ALL candidates
            var baseNames = new List<string>();
            
            foreach (var sys in systemCandidates)
            {
                // Priority per candidate:
                if (!_config.MarqueeAutoConvert)
                {
                    // User Request: Prioritize Topper for System if AutoConvert is FALSE
                    baseNames.Add($"{sys}-topper"); 
                    baseNames.Add($"{sys}-topper-w");
                }

                baseNames.Add($"{sys}-{languageCode}-w");
                baseNames.Add($"{sys}-w");
                baseNames.Add($"{sys}-{languageCode}");
                baseNames.Add($"{sys}");
                
                // Auto patterns
                baseNames.Add($"auto-{sys}-{languageCode}");
                baseNames.Add($"auto-{sys}");
                
                // Custom patterns
                baseNames.Add($"custom-{sys}-{languageCode}");
                baseNames.Add($"custom-{sys}");
            }
            
            // PASS 1: Check Custom Path (Strict Priority)
            if (!string.IsNullOrEmpty(_config.SystemCustomMarqueePath))
            {
                foreach (var baseName in baseNames)
                {
                    foreach (var ext in _config.AcceptedFormats)
                    {
                        var fileName = $"{baseName}.{ext}";
                        var customPath = Path.Combine(_config.SystemCustomMarqueePath, fileName);
                        
                        if (File.Exists(customPath))
                        {
                             _logger.LogInformation($"Found system marquee in custom path (Priority): {customPath}");
                             if (raw) return customPath;
                             return _imageService.ProcessImage(customPath, subFolder: "systems");
                        }
                    }
                }
            }

            // PASS 2: Check System/Theme Path (Fallback)
            foreach (var baseName in baseNames)
            {
                // 2. Check in System Path (AND Fallback to -master)
                var searchPaths = GetThemeSearchPaths(_config.SystemMarqueePath);
                
                // Add Collection path if different (and its fallback)
                if (_config.SystemMarqueePath != _config.CollectionMarqueePath)
                {
                    searchPaths.AddRange(GetThemeSearchPaths(_config.CollectionMarqueePath));
                }

                foreach (var basePath in searchPaths)
                {
                    foreach (var ext in _config.AcceptedFormats)
                    {
                        var fileName = $"{baseName}.{ext}";
                        var path = Path.Combine(basePath, fileName);
                        
                        // _logger.LogInformation($"[Trace] Checking system marquee: {path}"); 
                        if (File.Exists(path))
                        {
                            _logger.LogInformation($"Found system marquee: {path}");
                            if (raw) return path;
                            return _imageService.ProcessImage(path, subFolder: "systems");
                        }
                    }
                }
            }


            _logger.LogWarning($"No system marquee found for {systemName}, using default");
            return _config.DefaultImagePath;
        }

        /// <summary>
        /// Find collection marquee (with underscore prefix removed)
        /// FR: Trouver le marquee d'une collection (avec préfixe underscore retiré)
        /// </summary>
        private string? FindCollectionMarquee(string cleanName, bool raw = false) // cleanName = "favorites" (from "_favorites")
        {
            var languageCode = _esSettings.GetLanguageCode(); // ex: "fr"

            _logger.LogInformation($"FindCollectionMarquee: {cleanName} (lang: {languageCode}, raw: {raw})");

            var patterns = new List<string>
            {
                $"auto-{cleanName}-{languageCode}.svg",
                $"auto-{cleanName}.svg",
                $"custom-{cleanName}-{languageCode}.svg",
                $"custom-{cleanName}.svg",
                
                // Fallbacks to PNG?
                $"auto-{cleanName}-{languageCode}.png",
                $"auto-{cleanName}.png",
                
                // Fallbacks to standard names
                $"{cleanName}-{languageCode}.svg",
                $"{cleanName}.svg"
            };

            var searchPaths = GetThemeSearchPaths(_config.CollectionMarqueePath);

            foreach (var searchPath in searchPaths)
            {
                foreach (var pattern in patterns)
                {
                    var fullPath = Path.Combine(searchPath, pattern);
                    if (File.Exists(fullPath))
                    {
                         if (raw) return fullPath;
                         return _imageService.ProcessImage(fullPath, subFolder: "systems");
                    }
                }
                
                // Fallback: Use standard Finder if specific patterns failed (backward compatibility)
                 var specificBase = Path.Combine(searchPath, $"auto-{cleanName}");
                 var found = FindFile(specificBase, subFolder: "systems");
                 if (found != null) return found;
            }

            _logger.LogWarning($"No collection marquee found for {cleanName}, using default");
            return _config.DefaultImagePath;
        }

        /// <summary>
        /// Find game marquee with fallback chain and scraping
        /// FR: Trouver le marquee d'un jeu avec chaîne de fallback et scraping
        /// </summary>
        private async Task<string?> FindGameMarqueeAsync(string system, string gameName, string gameTitle, string romPath, bool isPreview = false, bool allowVideoGeneration = true, bool isGameStart = false)
        {
            _logger.LogInformation($"FindGameMarqueeAsync: system={system}, game={gameName}, rom={romPath} [Preview:{isPreview}]");
            
            // Define System Candidates (Original + Alias)
            var systemCandidates = new List<string> { system };
            if (_config.SystemAliases.TryGetValue(system, out var alias))
            {
                _logger.LogInformation($"Adding system alias for search: {system} -> {alias}");
                systemCandidates.Add(alias);
            }
            
            // Extract ROM filename
            string romFileName = "";
            if (!string.IsNullOrEmpty(romPath))
            {
                romFileName = Path.GetFileNameWithoutExtension(romPath);
            }
            
            // EN: Extract real system from ROM path for custom collections
            // FR: Extraire le vrai système depuis le chemin ROM pour collections personnalisées
            var realSystemFromPath = ExtractSystemFromRomPath(romPath);
            if (!string.IsNullOrEmpty(realSystemFromPath) && realSystemFromPath != system)
            {
                _logger.LogInformation($"[ROM PATH] Adding real system '{realSystemFromPath}' from ROM path (priority over ES system '{system}')");
                systemCandidates.Insert(0, realSystemFromPath);
            }
            
            // Special Handler for ES Menu (RetroBat System Local Files)
            if (!string.IsNullOrEmpty(romPath) && romPath.Contains("es_menu", StringComparison.OrdinalIgnoreCase))
            {
                  var dir = Path.GetDirectoryName(romPath);
                  if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(romFileName))
                  {
                      var mediaDir = Path.Combine(dir, "media");
                      if (Directory.Exists(mediaDir))
                      {
                          // 1. Try with -logo suffix
                          var logoBase = Path.Combine(mediaDir, romFileName + "-logo");
                          var logoFound = FindFile(logoBase, subFolder: system);
                          if (logoFound != null) return logoFound;

                          // 2. Try exact filename
                          var exactBase = Path.Combine(mediaDir, romFileName);
                          var exactFound = FindFile(exactBase, subFolder: system);
                          if (exactFound != null) return exactFound;
                      }
                  }
            }
            
            var safeRomName = Sanitize(romFileName);
            var safeGameName = Sanitize(gameName);
            
            // Capture Context for Refresh
            _lastSystem = system;
            _lastGame = gameName;
            _lastRom = romPath;
            _lastTitle = gameTitle;

            // --- PRIORITY 0: CUSTOM PATH (Super Priority) ---
            // User Request: Custom path is priority on everything (System dependent)
            if (!string.IsNullOrEmpty(_config.GameCustomMarqueePath))
            {
                 // Create temp candidates for search including aliases
                 foreach (var sysCand in systemCandidates)
                 {
                      var sysCustomDir = Path.Combine(_config.GameCustomMarqueePath, sysCand);
                      if (Directory.Exists(sysCustomDir))
                      {
                           // Check RomName, SafeRomName, GameName, SafeGameName
                           // We use TryFind logic manually here or just reusing TryFind logic is hard because TryFind intermixes default paths?
                           // Actually TryFind handles Custom Path internally priority 0. 
                           // So we can just call TryFind for all candidates but strict on Custom?
                           // No, TryFind will fall through.
                           
                           // Let's rely on manual check for speed and correctness here
                           var customCandidates = new List<string> { romFileName, safeRomName, gameName, safeGameName };
                           if (!string.IsNullOrEmpty(romFileName)) customCandidates.Add($"{romFileName}-marquee");
                           if (!string.IsNullOrEmpty(romFileName)) customCandidates.Add($"{romFileName}-marquee_composed");

                           foreach(var cand in customCandidates)
                           {
                               if (string.IsNullOrEmpty(cand)) continue;
                               var customFile = FindFile(Path.Combine(sysCustomDir, cand), subFolder: system);
                               if (customFile != null)
                               {
                                   _logger.LogInformation($"[Priority 0] Found Custom Path media: {customFile}");
                                   return customFile;
                               }
                           }
                      }
                 }
            }

            // --- PRIORITY 1: AUTO-SCRAPING (If Enabled) ---
            // Fix: Ignorer le scraping si nous sommes en Game-Start (isGameStart=true) OU preview
            if (_config.MarqueeAutoScraping && !string.IsNullOrEmpty(romPath) && !isPreview && !isGameStart)
            {
                 // EN: Determine effective system for scraping (Priority: Real System from Path > Alias > System)
                 // FR: Déterminer le système effectif pour le scraping (Priorité : Vrai système du chemin > Alias > Système)
                 var effectiveScrapSystem = system;
                 if (!string.IsNullOrEmpty(realSystemFromPath))
                 {
                     effectiveScrapSystem = realSystemFromPath;
                 }
                 else if (_config.SystemAliases.TryGetValue(system, out var scrapAlias))
                 {
                     effectiveScrapSystem = scrapAlias;
                 }

                 // Try scraping for 'mpv' media type
                 _logger.LogInformation($"[Scraping Priority] Checking configured scrapers for {gameName} ({effectiveScrapSystem})...");
                 // Pass effectiveScrapSystem instead of system
                 var scrapedPath = await _scraperManager.CheckAndScrapeAsync(effectiveScrapSystem, gameName, romPath, "mpv");
                 if (scrapedPath != null)
                 {
                     _logger.LogInformation($"[Scraping Priority] Success: {scrapedPath}");
                     return _imageService.ProcessImage(scrapedPath, subFolder: system); // Cache still uses original 'system' folder for consistency with ES
                 }

                 // EN: If scraping is in progress, return placeholder
                 // FR: Si le scraping est en cours, retourner l'image provisoire
                 // Fix: Use romFileName for key matching
                 // Use effectiveScrapSystem for checking progress too
                 if (_scraperManager.IsScraping(effectiveScrapSystem, gameName, "mpv"))
                 {
                     string? scraperName = _scraperManager.GetActiveScraperName(effectiveScrapSystem, gameName, "mpv");
                     _logger.LogInformation($"[Scraping] {gameName} ({effectiveScrapSystem}) is currently being scraped by {scraperName}. Returning placeholder.");
                     return _imageService.GetScrapingPlaceholder("mpv", scraperName);
                 }
            }


            // --- MAIN LOGIC --- 
            
            // --- PRIORITY 1: TOPPER (Custom or Scraped) ---
            string? topper = null;
            if (isGameStart)
            {
                topper = FindGameStartMedia(system, romFileName, gameName);
                if (topper != null) 
                {
                    // Fix: Do not immmediately return scraped images, to allow Video Generation (Priority 2) to run.
                    // Return immediately only if Custom Override or Video.
                    var ext = Path.GetExtension(topper).ToLowerInvariant();
                    var isVideo = new[] { ".mp4", ".avi", ".webm", ".mkv", ".mov", ".gif" }.Contains(ext);
                    var isCustomStart = !string.IsNullOrEmpty(_config.GameStartMediaPath) && topper.Contains(_config.GameStartMediaPath, StringComparison.OrdinalIgnoreCase);

                    if (isVideo || isCustomStart)
                    {
                        _logger.LogInformation($"Found topper (Priority 1 - Video/Custom): {topper}");
                        return topper;
                    }
                    _logger.LogInformation($"Found topper (Scraped Image). Holding for Video Generation check: {topper}");
                }
            }

            // EN: Find raw marquee first to check for custom priority or video bypass
            // FR: Trouver le marquee brut d'abord pour vérifier la priorité custom ou le bypass vidéo
            string? rawMarquee = TryFind(romFileName, systemCandidates, raw: true) 
                              ?? TryFind(safeRomName, systemCandidates, raw: true)
                              ?? TryFind(gameName, systemCandidates, raw: true)
                              ?? TryFind(safeGameName, systemCandidates, raw: true);

            if (rawMarquee != null)
            {
                bool isCustom = !string.IsNullOrEmpty(_config.GameCustomMarqueePath) && rawMarquee.Contains(_config.GameCustomMarqueePath, StringComparison.OrdinalIgnoreCase);
                bool isVideo = new[] { ".mp4", ".avi", ".webm", ".mkv", ".mov", ".gif" }.Contains(Path.GetExtension(rawMarquee).ToLowerInvariant());

                if (isCustom || isVideo)
                {
                    _logger.LogInformation($"[Priority Bypass] Found existing Custom/Video media (Bypassing composition): {rawMarquee}");
                    return _imageService.ProcessImage(rawMarquee, subFolder: system);
                }
            }

            // Standard marquee (will be used if composition is disabled or as fallback)
            string? basicMarquee = rawMarquee != null ? _imageService.ProcessImage(rawMarquee, subFolder: system) : null;
            
            // Get Offsets and Scales
            var (offX, offY, logoX, logoY, fanartScale, logoScale) = _offsetService.GetOffset(system, gameName); 

            // --- PRIORITY 2: VIDEO GENERATION (If Enabled) ---
            // If basicMarquee is already a video/gif, use it (skip generation)
            bool isAlreadyVideo = false;
            if (basicMarquee != null)
            {
                var ext = Path.GetExtension(basicMarquee).ToLowerInvariant();
                isAlreadyVideo = new[] { ".gif", ".mp4", ".avi", ".webm", ".mkv", ".mov" }.Contains(ext);
            }
            
            // Fix: User request - Do not display video during browsing (game-selected) ONLY if it is a GENERATED video.
            // Custom videos (in standard folders) should still play.
            if (!allowVideoGeneration && isAlreadyVideo)
            {
                 var genFolder = _config.GenerateMarqueeVideoFolder;
                 if (string.IsNullOrWhiteSpace(genFolder)) genFolder = "generated_videos";
                 
                 // Check if the file is inside the generated folder
                 if (basicMarquee != null && basicMarquee.Contains(genFolder, StringComparison.OrdinalIgnoreCase))
                 {
                     _logger.LogInformation($"[Browsing] Ignoring GENERATED video marquee for {gameName} (Static only requested).");
                     basicMarquee = null;
                     isAlreadyVideo = false;
                 }
            }

            if (allowVideoGeneration && _config.MarqueeVideoGeneration && !isAlreadyVideo)
            {
                _logger.LogInformation($"[Video Gen] Video generation enabled. Searching video source for: {gameName}");
                var videoSource = TryFindVideo(romFileName, systemCandidates) ?? TryFindVideo(safeRomName, systemCandidates) ?? TryFindVideo(gameName, systemCandidates) ?? TryFindVideo(safeGameName, systemCandidates);
                if (videoSource != null)
                {
                    _logger.LogInformation($"[Video Gen] Found video source: {videoSource}");
                    // Find Logo and Background for Generation
                    var logoRaw = TryFind(romFileName, systemCandidates, "-marquee", raw: true) 
                               ?? TryFind(safeRomName, systemCandidates, "-marquee", raw: true)
                               ?? TryFind(romFileName, systemCandidates, raw: true) 
                               ?? TryFind(safeRomName, systemCandidates, raw: true)
                               ?? TryFind(gameName, systemCandidates, raw: true)
                               ?? TryFind(safeGameName, systemCandidates, raw: true)
                               ?? FindSystemMarquee(system, raw: true);

                    if (logoRaw == _config.DefaultImagePath) logoRaw = "";


                    _logger.LogWarning($"[DEBUG CHECK] GenerateMarqueeVideo called with gameName='{romFileName}'"); // Previous log
                    _logger.LogError($"[CRITICAL DEBUG] romFileName: '{romFileName}', safeRomName: '{safeRomName}'"); // New critical log
                    var generatedVideo = _imageService.GenerateMarqueeVideo(videoSource, logoRaw ?? "", system, romFileName);
                    if (generatedVideo != null)
                    {
                        return generatedVideo;
                    }
                }
            }
            

            // Fix: If we held a topper (Priority 1), and Video Gen (Priority 2) didn't return, return topper now.
            // This prevents Composition (Priority 3) or Basic Marquee (Priority 4) from overriding the Topper.
            if (topper != null)
            {
                 _logger.LogInformation($"Using topper (Fallback after Video Gen): {topper}");
                 return topper;
            }

            // --- PRIORITY 3: COMPOSITION (If Enabled) ---
            if (_config.MarqueeCompose)
            {
                string? backgroundInfo = null;

                if (_config.ComposeMedia.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    backgroundInfo = TryFind(romFileName, systemCandidates, "-image", raw: true) 
                                  ?? TryFind(safeRomName, systemCandidates, "-image", raw: true)
                                  ?? TryFind(romFileName, systemCandidates, "-thumb", raw: true)
                                  ?? TryFind(safeRomName, systemCandidates, "-thumb", raw: true)
                                  ?? TryFind(romFileName, systemCandidates, "-mix", raw: true)
                                  ?? TryFind(safeRomName, systemCandidates, "-mix", raw: true);

                    if (backgroundInfo == null)
                    {
                        backgroundInfo = TryFind(romFileName, systemCandidates, "", raw: true) ?? TryFind(safeRomName, systemCandidates, "", raw: true);
                    }
                
                    if (backgroundInfo == null)
                         backgroundInfo = TryFindFanart(romFileName, systemCandidates) ?? TryFindFanart(safeRomName, systemCandidates) ?? TryFindFanart(gameName, systemCandidates) ?? TryFindFanart(safeGameName, systemCandidates);
                }
                else
                {
                    backgroundInfo = TryFindFanart(romFileName, systemCandidates) ?? TryFindFanart(safeRomName, systemCandidates) ?? TryFindFanart(gameName, systemCandidates) ?? TryFindFanart(safeGameName, systemCandidates);
                }

                if (backgroundInfo != null)
                {
                    var topperRaw = TryFind(romFileName, systemCandidates, "-topper", raw: true) ?? TryFind(safeRomName, systemCandidates, "-topper", raw: true) ?? TryFind(gameName, systemCandidates, "-topper", raw: true) ?? TryFind(safeGameName, systemCandidates, "-topper", raw: true);
                    if (topperRaw != null)
                    {
                         _logger.LogInformation($"Auto-Generating Marquee: Background ({_config.ComposeMedia}) {backgroundInfo} + Topper {topperRaw}");
                         return _imageService.GenerateComposition(backgroundInfo, topperRaw, system, offX, offY, logoX, logoY, fanartScale, logoScale, isPreview);
                    }
                    
                    var basicRaw = TryFind(romFileName, systemCandidates, raw: true) ?? TryFind(safeRomName, systemCandidates, raw: true) ?? TryFind(gameName, systemCandidates, raw: true) ?? TryFind(safeGameName, systemCandidates, raw: true);
                    
                    if (basicRaw != null && !string.Equals(basicRaw, backgroundInfo, StringComparison.OrdinalIgnoreCase))
                    {
                         _logger.LogInformation($"Auto-Generating Marquee: Background {backgroundInfo} + Game Logo {basicRaw}");
                         return _imageService.GenerateComposition(backgroundInfo, basicRaw, system, offX, offY, logoX, logoY, fanartScale, logoScale, isPreview);
                    }

                    var systemLogoRaw = FindSystemMarquee(system, raw: true);
                    
                    if (systemLogoRaw != null && systemLogoRaw != _config.DefaultImagePath)
                    {
                         _logger.LogInformation($"Auto-Generating Marquee: Background {backgroundInfo} + System {systemLogoRaw}");
                         return _imageService.GenerateComposition(backgroundInfo, systemLogoRaw, system, offX, offY, logoX, logoY, fanartScale, logoScale, isPreview);
                    }
                    
                    return _imageService.GenerateComposition(backgroundInfo, "", system, offX, offY, logoX, logoY, fanartScale, logoScale, isPreview);
                }
            }

            // --- PRIORITY 4: STATIC FILES ---
            if (basicMarquee != null)
            {
                 if (_config.MarqueeCompose) _logger.LogInformation($"Existing marquee found: {basicMarquee}. (Fanart not found).");
                 else _logger.LogInformation($"Found game marquee: {basicMarquee}");
                 return basicMarquee;
            }

            if (topper != null)
            {
                _logger.LogInformation($"Found game topper: {topper}");
                return topper;
            }

            var systemMarquee = FindSystemMarquee(system);
            if (systemMarquee != _config.DefaultImagePath)
            {
                _logger.LogInformation($"Using system marquee for game: {systemMarquee}");
                return systemMarquee;
            }

            _logger.LogWarning($"No marquee found for game {gameName} ({romFileName}), using default");
            return _config.DefaultImagePath;
        }

        private string? FindFile(string basePath, string subFolder = "", bool isSystem = false)
        {
            foreach (var ext in _config.AcceptedFormats)
            {
                var fullPath = $"{basePath}.{ext.Trim()}";
                if (File.Exists(fullPath))
                {
                    // Check if conversion is needed
                    // Always convert if it's SVG, or if AutoConvert is enabled
                    bool isSvg = ext.Trim().Equals("svg", StringComparison.OrdinalIgnoreCase);
                    
                    if (isSvg || _config.MarqueeAutoConvert)
                    {
                         // isSystem dictates "systems" subfolder if subFolder is empty?
                         // Ideally call site provides subFolder.
                         var finalSubFolder = string.IsNullOrEmpty(subFolder) ? (isSystem ? "systems" : "") : subFolder;
                         return _imageService.ProcessImage(fullPath, finalSubFolder);
                    }
                    
                    return fullPath;
                }
            }

            return null;
        }
        
        /// <summary>
        /// Just find the file without processing/converting
        /// </summary>
        private string? TryFind(string nameComponent, List<string> systemCandidates, string suffix = "", string subFolderOverride = "", bool raw = false)
        {
            if (string.IsNullOrEmpty(nameComponent)) return null;

            foreach (var sysCand in systemCandidates)
            {
                var activeSystem = string.IsNullOrEmpty(subFolderOverride) ? sysCand : subFolderOverride;

                // 0. Check Custom Path (Priority)
                if (!string.IsNullOrEmpty(_config.GameCustomMarqueePath))
                {
                    var sysCustomDir = Path.Combine(_config.GameCustomMarqueePath, activeSystem);
                    try { if (!Directory.Exists(sysCustomDir)) Directory.CreateDirectory(sysCustomDir); } catch {}

                    var searchPatterns = new List<string>();
                    
                    // User Request: Prioritize Topper if AutoConvert is FALSE
                    if (!_config.MarqueeAutoConvert && string.IsNullOrEmpty(suffix))
                    {
                        searchPatterns.Add(nameComponent + "-topper");
                    }

                    searchPatterns.Add(nameComponent + suffix);
                    if (string.IsNullOrEmpty(suffix)) 
                    {
                        searchPatterns.Add(nameComponent + "-marquee");
                        // User Request: Add -marquee_composed
                        searchPatterns.Add(nameComponent + "-marquee_composed");
                        
                        // Fallback Topper if not prioritized (or AutoConvert is true - actually keep it as fallback anyway?)
                        // User requested priority explicitly if false. If true, standard behavior.
                    }
                    
                    foreach (var searchPattern in searchPatterns)
                    {
                        var sysCustomPath = Path.Combine(sysCustomDir, searchPattern);
                        var foundSys = raw ? FindSourceFile(sysCustomPath) : FindFile(sysCustomPath, subFolder: activeSystem);
                        if (foundSys != null) { _logger.LogInformation($"Found custom game file: {foundSys}"); return foundSys; }
                    }
                }
                
                // 1. Try Configured Topper First
                if (!_config.MarqueeAutoConvert && string.IsNullOrEmpty(suffix))
                {
                    var basicPattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent);
                    var topperPattern = basicPattern.Contains("-marquee") 
                        ? basicPattern.Replace("-marquee", "-topper") 
                        : basicPattern + "-topper";

                    var topperPath = Path.Combine(_config.MarqueeImagePath, topperPattern);
                    var foundTopper = raw ? FindSourceFile(topperPath) : FindFile(topperPath, subFolder: activeSystem);
                    if (foundTopper != null) return foundTopper;
                }

                // 1. Try Custom/Configured Path
                var pattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent) + suffix;
                var path = Path.Combine(_config.MarqueeImagePath, pattern);
                var found = raw ? FindSourceFile(path) : FindFile(path, subFolder: activeSystem);
                if (found != null) return found;
                
                // 2. Try Default Path (Topper Priority if applicable)
                if (!_config.MarqueeAutoConvert && string.IsNullOrEmpty(suffix))
                {
                    // 2a. Try Default Topper First
                    var basicDefPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent);
                    var defTopperPattern = basicDefPattern.Contains("-marquee") 
                        ? basicDefPattern.Replace("-marquee", "-topper") 
                        : basicDefPattern + "-topper";
                    
                    var defTopperPath = Path.Combine(_config.MarqueeImagePathDefault, defTopperPattern);
                    var foundDefTopper = raw ? FindSourceFile(defTopperPath) : FindFile(defTopperPath, subFolder: activeSystem);
                    if (foundDefTopper != null) return foundDefTopper;
                }

                // 2b. Try Default Path Standard
                var defaultPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent);
                if (!string.IsNullOrEmpty(suffix))
                {
                     if (defaultPattern.Contains("-marquee")) defaultPattern = defaultPattern.Replace("-marquee", suffix);
                     else defaultPattern += suffix;
                }
                var defaultPath = Path.Combine(_config.MarqueeImagePathDefault, defaultPattern);
                var foundDef = raw ? FindSourceFile(defaultPath) : FindFile(defaultPath, subFolder: activeSystem);
                if (foundDef != null) return foundDef;

                // 3. Fuzzy Search (Only if name is long enough)
                if (nameComponent.Length > 4)
                {
                     string dirPattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", "").TrimEnd('/', '\\');
                     var searchDir = Path.Combine(_config.MarqueeImagePath, dirPattern);
                     var fuzzy = FindFuzzy(searchDir, nameComponent, suffix);
                     if (fuzzy != null) return fuzzy;
                     
                     string defDirPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", "").TrimEnd('/', '\\');
                     var defSearchDir = Path.Combine(_config.MarqueeImagePathDefault, defDirPattern);
                     var fuzzyDef = FindFuzzy(defSearchDir, nameComponent, suffix);
                     if (fuzzyDef != null) return fuzzyDef;
                }
            }
            
            return null;
        }

        private string? TryFindVideo(string nameComponent, List<string> systemCandidates)
        {
            if (string.IsNullOrEmpty(nameComponent)) return null;
            var videoExts = new[] { "mp4", "avi", "webm", "mkv", "mov" };

            // EN: Debug logging
            // FR: Log de débogage
            _logger.LogInformation($"[TryFindVideo] Searching for gameplay video for: {nameComponent}");

            foreach (var sysCand in systemCandidates)
            {
                var searchDirs = new List<string>
                {
                    Path.Combine(_config.RomsPath, sysCand, "videos"),
                    Path.Combine(_config.RomsPath, sysCand, "video"),
                    Path.Combine(_config.RomsPath, sysCand, "images"),
                    Path.Combine(_config.RetroBatPath, "roms", sysCand, "videos"),
                    Path.Combine(_config.RetroBatPath, "roms", sysCand, "video"),
                    Path.Combine(_config.RetroBatPath, "roms", sysCand, "images"),
                    Path.Combine(_config.ScreenScraperCachePath, sysCand)
                };

                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;

                    foreach (var ext in videoExts)
                    {
                        var v1 = Path.Combine(dir, nameComponent + "-video." + ext);
                        if (File.Exists(v1)) 
                        {
                            _logger.LogInformation($"[TryFindVideo] Found video: {v1}");
                            return v1;
                        }
                        var v2 = Path.Combine(dir, nameComponent + "." + ext);
                        if (File.Exists(v2)) 
                        {
                            _logger.LogInformation($"[TryFindVideo] Found video: {v2}");
                            return v2;
                        }
                    }
                }
            }
            return null;
        }

        private string? TryFindFanart(string nameComponent, List<string> systemCandidates)
        {
            if (string.IsNullOrEmpty(nameComponent)) return null;
            
            foreach (var sysCand in systemCandidates)
            {
                // Config pattern:
                var pattern1 = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent) + "-fanart";
                var f1 = FindSourceFile(Path.Combine(_config.MarqueeImagePath, pattern1));
                if (f1 != null) return f1;
                
                // Default Pattern
                var defPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", nameComponent);
                if (defPattern.Contains("-marquee")) defPattern = defPattern.Replace("-marquee", "-fanart");
                else defPattern += "-fanart";
                
                var f2 = FindSourceFile(Path.Combine(_config.MarqueeImagePathDefault, defPattern));
                if (f2 != null) return f2;
            }
            return null;
        }

        private string? FindFuzzy(string directory, string targetName, string suffix)
        {
            if (!Directory.Exists(directory)) return null;
            var safeTarget = Sanitize(targetName);
            if (safeTarget.Length < 1) return null;
            var prefix = safeTarget.Length >= 3 ? safeTarget.Substring(0, 3) : safeTarget.Substring(0, 1);
            
            try
            {
                var candidates = Directory.EnumerateFiles(directory, prefix + "*.*", SearchOption.TopDirectoryOnly);
                string? bestMatch = null;
                int bestDist = int.MaxValue;
                
                foreach(var cand in candidates)
                {
                    var fname = Path.GetFileNameWithoutExtension(cand);
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        if (!fname.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
                        fname = fname.Substring(0, fname.Length - suffix.Length);
                    }
                    var safeCand = Sanitize(fname);
                    int dist = ComputeLevenshteinDistance(safeTarget, safeCand);
                    if (dist < bestDist && dist <= 3) 
                    {
                        bestDist = dist;
                        bestMatch = cand;
                    }
                }
                if (bestMatch != null)
                     _logger.LogInformation($"[Fuzzy] Match Found! '{targetName}' -> '{Path.GetFileName(bestMatch)}' (Dist: {bestDist})");
                return bestMatch;
            }
            catch (Exception ex) { _logger.LogError($"[Fuzzy] Error scanning {directory}: {ex.Message}"); return null; }
        }

        private int ComputeLevenshteinDistance(string s, string t)
        {
            int n = s.Length; int m = t.Length;
            var d = new int[n + 1, m + 1];
            if (n == 0) return m; if (m == 0) return n;
            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }
            for (int i = 1; i <= n; i++) {
                for (int j = 1; j <= m; j++) {
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[n, m];
        }
        private string? FindGameFanart(string system, string gameName, string romFileName)
        {
            var systemCandidates = new List<string> { system };
            if (_config.SystemAliases.TryGetValue(system, out var alias)) systemCandidates.Add(alias);

            // Generate candidates list: RomName, SafeRomName, GameName, SafeGameName
            var nameCandidates = new List<string>();
            if (!string.IsNullOrEmpty(romFileName)) 
            {
                nameCandidates.Add(romFileName);
                nameCandidates.Add(Path.GetFileNameWithoutExtension(romFileName));
                nameCandidates.Add(Sanitize(romFileName));
            }
            if (!string.IsNullOrEmpty(gameName))
            {
                nameCandidates.Add(gameName);
                nameCandidates.Add(Sanitize(gameName));
            }

            foreach (var sysCand in systemCandidates)
            {
                 foreach (var cand in nameCandidates.Distinct())
                 {
                     if (string.IsNullOrEmpty(cand)) continue;

                     // 1. Configured Path
                     var rawPattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", cand);
                     if (rawPattern.Contains("-marquee")) rawPattern = rawPattern.Replace("-marquee", "-fanart");
                     else rawPattern += "-fanart";
                     
                     var f1 = FindSourceFile(Path.Combine(_config.MarqueeImagePath, rawPattern));
                     if (f1 != null) return f1;
                     
                     // 2. Default Path
                     var defPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", cand);
                     if (defPattern.Contains("-marquee")) defPattern = defPattern.Replace("-marquee", "-fanart");
                     else defPattern += "-fanart";
                     
                     var f2 = FindSourceFile(Path.Combine(_config.MarqueeImagePathDefault, defPattern));
                     if (f2 != null) return f2;
                 }
            }

            return null;
        }

        private string? FindGameImage(string system, string gameName, string romFileName)
        {
             // Helper to find source file
            string? FindSourceFile(string bashPath)
            {
                 foreach (var ext in _config.AcceptedFormats)
                {
                    var fullPath = $"{bashPath}.{ext.Trim()}";
                    if (File.Exists(fullPath)) return fullPath;
                }
                return null;
            }

            var systemCandidates = new List<string> { system };
            if (_config.SystemAliases.TryGetValue(system, out var alias)) systemCandidates.Add(alias);
            
            // Generate candidates list: RomName, SafeRomName, GameName, SafeGameName
            var nameCandidates = new List<string>();
            if (!string.IsNullOrEmpty(romFileName)) 
            {
                nameCandidates.Add(romFileName);
                nameCandidates.Add(Path.GetFileNameWithoutExtension(romFileName));
                nameCandidates.Add(Sanitize(romFileName));
            }
            if (!string.IsNullOrEmpty(gameName))
            {
                nameCandidates.Add(gameName);
                nameCandidates.Add(Sanitize(gameName));
            }
            
            // Suffixes in priority order (Screenshot, Thumb, Mix, Raw)
            var suffixes = new[] { "-image", "-thumb", "-mix", "" };

            foreach (var sysCand in systemCandidates)
            {
                foreach (var suffix in suffixes)
                {
                     foreach (var cand in nameCandidates.Distinct()) // Distinct to avoid duplicates
                     {
                         if (string.IsNullOrEmpty(cand)) continue;

                         // 1. Configured Path
                         var rawPattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", cand);
                         if (rawPattern.Contains("-marquee")) rawPattern = rawPattern.Replace("-marquee", suffix);
                         else rawPattern += suffix;
                         
                         var f1 = FindSourceFile(Path.Combine(_config.MarqueeImagePath, rawPattern));
                         if (f1 != null) return f1;

                         // 2. Default Path
                         var defPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", cand);
                         if (defPattern.Contains("-marquee")) defPattern = defPattern.Replace("-marquee", suffix);
                         else defPattern += suffix;
                         
                         var f2 = FindSourceFile(Path.Combine(_config.MarqueeImagePathDefault, defPattern));
                         if (f2 != null) return f2;
                     }
                }
            }

            return null;

        }

        public async Task<string?> RefreshCompositionAsync(int dx, int dy, bool isLogo)
        {
            if (string.IsNullOrEmpty(_lastSystem) || string.IsNullOrEmpty(_lastGame))
            {
                return null;
            }

            _logger.LogInformation($"Refeshing composition for {_lastGame} (Delta: {dx}, {dy}, Logo: {isLogo})");

            // Update Offset Storage
            _offsetService.UpdateOffset(_lastSystem, _lastGame, dx, dy, isLogo);

            // Special case for Game Over refresh
            if (_lastSystem == "system-game-over")
            {
                return FindGameOverMarquee(isPreview: true);
            }

            // Re-run FindGameMarquee
            // This will pick up new offsets and force regeneration (GenerateComposition handles cache invalidation via unique name with offsets)
            return await FindGameMarqueeAsync(_lastSystem, _lastGame, _lastTitle, _lastRom, isPreview: true, allowVideoGeneration: false, isGameStart: true);
        }

        public async Task<string?> RefreshScaleAsync(double delta, bool isLogo)
        {
            if (string.IsNullOrEmpty(_lastSystem) || string.IsNullOrEmpty(_lastGame))
            {
                return null;
            }

            _logger.LogInformation($"Refreshing scale for {_lastGame} (Delta: {delta:F2}, Logo: {isLogo})");

            // Update Scale Storage
            _offsetService.UpdateScale(_lastSystem, _lastGame, delta, isLogo);

            // Special case for Game Over refresh
            if (_lastSystem == "system-game-over")
            {
                return FindGameOverMarquee(isPreview: true);
            }

            // Re-run FindGameMarquee to regenerate with new scale
            // EN: Disable video generation during scale refresh to ensure we see the composition updates
            // FR: Désactiver la génération vidéo pendant le rafraîchissement du scale pour assurer qu'on voit les mises à jour de composition
            return await FindGameMarqueeAsync(_lastSystem, _lastGame, _lastTitle, _lastRom, isPreview: true, allowVideoGeneration: false, isGameStart: true);
        }

        /// <summary>
        /// EN: Find game logo (marquee) for video preview composition
        /// FR: Trouver le logo (marquee) d'un jeu pour la composition preview vidéo
        /// </summary>
        public string? FindGameLogo(string system, string gameName, string romFileName, bool raw = true)
        {
            // EN: Build system candidates (same logic as FindGameMarqueeAsync)
            // FR: Construire les candidats système (même logique que FindGameMarqueeAsync)
            var systemCandidates = new List<string> { system };
            if (_config.SystemAliases.TryGetValue(system, out var alias))
            {
                systemCandidates.Add(alias);
            }
            
            var safeRomName = Sanitize(romFileName);
            var safeGameName = Sanitize(gameName);
            
            // EN: Search logo with same priority as FindGameMarqueeAsync lines 360-366
            // FR: Chercher le logo avec même priorité que FindGameMarqueeAsync lignes 360-366
            return TryFind(romFileName, systemCandidates, "-marquee", raw: raw) 
                ?? TryFind(safeRomName, systemCandidates, "-marquee", raw: raw)
                ?? TryFind(romFileName, systemCandidates, raw: raw) 
                ?? TryFind(safeRomName, systemCandidates, raw: raw)
                ?? TryFind(gameName, systemCandidates, raw: raw)
                ?? TryFind(safeGameName, systemCandidates, raw: raw);
        }

        public string? FindGameOverMarquee(bool isPreview = false)
        {
            // EN: Set internal state to allow RefreshComposition to work (Hotkeys)
            // FR: Définir l'état interne pour permettre à RefreshComposition de fonctionner (Raccourcis)
            _lastSystem = "system-game-over";
            _lastGame = "default";
            _lastRom = "GameOver";
            _lastTitle = "Game Over";

            if (!_config.MarqueeCompose) return null;

            string defaultImagesPath = Path.Combine(_config.MarqueeImagePath, "GameOver");
            
            // Fix: User Request - Prioritize MP4/Video for Game Over (for MPV)
            var videoExtensions = new[] { ".mp4", ".avi", ".mkv", ".webm", ".mov" };
            foreach (var ext in videoExtensions)
            {
                var vidPath = Path.Combine(defaultImagesPath, "game-over" + ext);
                if (File.Exists(vidPath))
                {
                    _logger.LogInformation($"Found GAME OVER video for MPV: {vidPath}");
                    return vidPath;
                }
            }

            string gameOverMarquee = Path.Combine(defaultImagesPath, "game-over-marquee.png");
            string gameOverFanart = Path.Combine(defaultImagesPath, "game-over-fanart.png");

            // Support both .png and .jpg for fanart
            if (!File.Exists(gameOverFanart))
                gameOverFanart = Path.Combine(defaultImagesPath, "game-over-fanart.jpg");

            if (File.Exists(gameOverMarquee) && File.Exists(gameOverFanart))
            {
                 _logger.LogInformation("Found GAME OVER assets. Generating composition...");
                 
                 // Get Offsets and Scales for "system-game-over"
                 var (offX, offY, logoX, logoY, fanartScale, logoScale) = _offsetService.GetOffset(_lastSystem, _lastGame);

                 return _imageService.GenerateComposition(
                     gameOverFanart, 
                     gameOverMarquee, 
                     _lastSystem,
                     offX,
                     offY,
                     logoX,
                     logoY,
                     fanartScale,
                     logoScale,
                     isPreview);
            }
            
            return null;
        }

        public string? FindGameStartMedia(string system, string romFileName, string gameName)
        {
            // EN: Priority 1 is EXCLUSIVE: Only trigger if paths are explicitly configured (alimenté)
            // FR: La Priorité 1 est EXCLUSIVE : Ne se déclenche que si les chemins sont explicitement configurés (alimenté)
            string customPath = _config.GameStartMediaPath;
            string scrapType = _config.MPVScrapMediaType;

            if (string.IsNullOrEmpty(customPath) && string.IsNullOrEmpty(scrapType))
            {
                return null;
            }

            var extensions = new[] { ".mp4", ".avi", ".mkv", ".webm", ".mov", ".gif", ".png", ".jpg" };
            var candidates = new List<string>();
            // Patterns: EXACT name, -topper, -marquee, -marquee_composed
            string[] suffixes = { "", "-topper", "-marquee", "-marquee_composed" };

            if (!string.IsNullOrEmpty(romFileName))
            {
                foreach (var s in suffixes) candidates.Add(romFileName + s);
            }
            if (!string.IsNullOrEmpty(gameName))
            {
                foreach (var s in suffixes) candidates.Add(gameName + s);
            }

            // 1. Search Custom Path (if fed)
            if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
            {
                var searchDirs = new List<string>();
                if (!string.IsNullOrEmpty(system)) searchDirs.Add(Path.Combine(customPath, system));
                searchDirs.Add(customPath);

                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var cand in candidates)
                    {
                        foreach (var ext in extensions)
                        {
                            var p = Path.Combine(dir, cand + ext);
                            if (File.Exists(p))
                            {
                                _logger.LogInformation($"[Priority 1] Found Custom Loading Media: {p}");
                                return p;
                            }
                        }
                    }
                }
            }

            // --- PRIORITY 2: VIDEO GENERATION (If Enabled) ---
            // EN: Special priority for Game Start: If no custom media found, prefer generated video over scraped static image.
            if (_config.MarqueeVideoGeneration)
            {
                 var systemCandidates = new List<string> { system };
                 var videoSource = TryFindVideo(romFileName, systemCandidates) 
                                ?? TryFindVideo(Sanitize(romFileName), systemCandidates) 
                                ?? TryFindVideo(gameName, systemCandidates) 
                                ?? TryFindVideo(Sanitize(gameName), systemCandidates);

                 if (videoSource != null)
                 {
                      var logoRaw = TryFind(romFileName, systemCandidates, "-marquee", raw: true) 
                                 ?? TryFind(gameName, systemCandidates, raw: true)
                                 ?? FindSystemMarquee(system, raw: true);
                      
                      if (logoRaw == _config.DefaultImagePath) logoRaw = "";

                      _logger.LogInformation($"[DMD GameStart] Found video source for generation: {videoSource}");
                      var generatedVideo = _imageService.GenerateMarqueeVideo(videoSource, logoRaw ?? "", system, romFileName);
                      if (generatedVideo != null) 
                      {
                          return generatedVideo;
                      }
                 }
            }

            // 3. Search Scrap Cache Path (if scrap type is fed)
            if (!string.IsNullOrEmpty(scrapType) && !string.IsNullOrEmpty(_config.ScreenScraperCachePath) && Directory.Exists(_config.ScreenScraperCachePath))
            {
                var scrapDir = Path.Combine(_config.ScreenScraperCachePath, system);
                if (Directory.Exists(scrapDir))
                {
                    foreach (var cand in candidates)
                    {
                        foreach (var ext in extensions)
                        {
                            // Try with Scrap Type suffix (Priority for Skraper)
                            var pScrap = Path.Combine(scrapDir, $"{cand}_{scrapType}{ext}");
                            if (File.Exists(pScrap)) return pScrap;

                            // Try standard candidates in scrap folder
                            var p = Path.Combine(scrapDir, cand + ext);
                            if (File.Exists(p)) return p;
                        }
                    }
                }
            }

            return null;
        }

        public async Task<string?> FindDmdImageAsync(string system, string gameName, string romFileName, string romPath = "", bool allowVideo = true, bool allowScraping = true)
        {
            if (!_config.DmdEnabled) return null;
            
            // EN: Extract real system from ROM path for custom collections (priority)
            // FR: Extraire le vrai système depuis le chemin ROM pour collections personnalisées (priorité)
            var realSystemFromPath = ExtractSystemFromRomPath(romPath);
            if (!string.IsNullOrEmpty(realSystemFromPath))
            {
                _logger.LogInformation($"[ROM PATH] DMD: Using real system '{realSystemFromPath}' from ROM path instead of ES system '{system}'");
                system = realSystemFromPath; // Override with real system
            }
            
            // Check Correlation
            if (_config.CollectionCorrelation.TryGetValue(system, out var correlatedName))
            {
                _logger.LogInformation($"[DMD] Mapped system '{system}' to '{correlatedName}' via CollectionCorrelation");
                system = correlatedName;
            }

            // Check Aliases
            var systemCandidates = new List<string> { system };
            if (_config.SystemAliases.TryGetValue(system, out var alias) && !string.IsNullOrEmpty(alias))
            {
                _logger.LogInformation($"[DMD] Found alias for '{system}': '{alias}'");
                systemCandidates.Add(alias);
            }

            var safeRom = Sanitize(romFileName);
            var safeGame = Sanitize(gameName);

            // --- PRIORITY 0: CUSTOM DMD PATH (Super Priority) ---
            // 1. Check Custom DmdMediaPath (Priority)
            if (!string.IsNullOrEmpty(_config.DmdMediaPath) && Directory.Exists(_config.DmdMediaPath))
            {
                 var candidates = new List<string>();
                 // User Request: ALLOW custom videos on DMD even in browsing.
                 // We only block GENERATED content later.
                 string[] exts = { ".gif", ".mp4", ".avi", ".png", ".jpg", ".bmp" };
                 
                  // Priority: [RomName]-marquee, [RomName]-topper, [RomName], [GameName]-marquee, [GameName]-topper, [GameName]
                  if (!string.IsNullOrEmpty(romFileName)) {
                      candidates.Add($"{romFileName}-marquee");
                      candidates.Add($"{romFileName}-marquee_composed"); // Added
                      candidates.Add($"{romFileName}-topper");
                      candidates.Add(romFileName); 
                  }
                  if (!string.IsNullOrEmpty(gameName)) {
                      candidates.Add($"{gameName}-marquee");
                      candidates.Add($"{gameName}-marquee_composed"); // Added
                      candidates.Add($"{gameName}-topper");
                      candidates.Add(gameName);
                  }
                  
                  if (safeRom != romFileName) {
                      candidates.Add($"{safeRom}-marquee");
                      candidates.Add($"{safeRom}-marquee_composed"); // Added
                      candidates.Add($"{safeRom}-topper");
                      candidates.Add(safeRom);
                  }
                  
                  if (safeGame != gameName) {
                      candidates.Add($"{safeGame}-marquee");
                      candidates.Add($"{safeGame}-marquee_composed"); // Added
                      candidates.Add($"{safeGame}-topper");
                      candidates.Add(safeGame);
                  }

                 var searchDirs = new List<string>();
                 var sysDir = Path.Combine(_config.DmdMediaPath, system);
                 if (Directory.Exists(sysDir)) searchDirs.Add(sysDir);
                 searchDirs.Add(_config.DmdMediaPath);

                 foreach (var dir in searchDirs)
                 {
                     foreach (var cand in candidates)
                     {
                         foreach (var ext in exts)
                         {
                             var path = Path.Combine(dir, cand + ext);
                             if (File.Exists(path))
                             {
                                 _logger.LogInformation($"[DMD] Found custom media (Priority): {path}");
                                 return _imageService.ProcessDmdImage(path, subFolder: system);
                             }
                         }
                     }
                 }
            }




            // --- PRIORITY 1: AUTO-SCRAPING (If Enabled) ---
            if (_config.MarqueeAutoScraping && !string.IsNullOrEmpty(romPath) && allowScraping)
            {
                _logger.LogInformation($"[DMD Scraping Priority] Checking configured scrapers for {gameName} ({system})...");
                var scrapedPath = await _scraperManager.CheckAndScrapeAsync(system, gameName, romPath, "dmd");
                if (scrapedPath != null)
                {
                    _logger.LogInformation($"[DMD Scraping Priority] Success: {scrapedPath}");
                    return _imageService.ProcessDmdImage(scrapedPath, subFolder: system);
                }

                if (_scraperManager.IsScraping(system, gameName, "dmd"))
                {
                    string? scraperName = _scraperManager.GetActiveScraperName(system, gameName, "dmd");
                    _logger.LogInformation($"[DMD Scraping] {gameName} ({system}) is currently being scraped by {scraperName}. Returning placeholder.");
                    return _imageService.GetScrapingPlaceholder("dmd", scraperName);
                }
            }

            // --- PRIORITY 2: VIDEO GENERATION (If Enabled) ---
            if (_config.MarqueeVideoGeneration)
            {
                var videoSource = TryFindVideo(romFileName, systemCandidates) ?? TryFindVideo(safeRom, systemCandidates) ?? TryFindVideo(gameName, systemCandidates) ?? TryFindVideo(safeGame, systemCandidates);
                if (videoSource != null)
                {
                    var logoRaw = TryFind(romFileName, systemCandidates, "-marquee", raw: true) 
                               ?? TryFind(safeRom, systemCandidates, "-marquee", raw: true)
                               ?? TryFind(romFileName, systemCandidates, raw: true) 
                               ?? TryFind(safeRom, systemCandidates, raw: true)
                               ?? TryFind(gameName, systemCandidates, raw: true)
                               ?? TryFind(safeGame, systemCandidates, raw: true)
                               ?? FindSystemMarquee(system, raw: true);

                    if (logoRaw == _config.DefaultImagePath) logoRaw = "";


                    _logger.LogWarning($"[DMD GEN DEBUG] GenerateMarqueeVideo (DMD path) called with gameName='{romFileName}'");
                    var generatedVideo = _imageService.GenerateMarqueeVideo(videoSource, logoRaw ?? "", system, romFileName);
                    if (generatedVideo != null)
                    {
                        // Fix: If browsing (allowVideo=false), do not use the GENERATED video on DMD either
                        if (!allowVideo) 
                        {
                            _logger.LogInformation($"[DMD] Ignoring generated video for DMD during browsing: {generatedVideo}");
                        }
                        else
                        {
                            // PlayAsync will handle MP4 -> GIF conversion for DMD
                            _logger.LogInformation($"[DMD] Using generated marquee video (priority over cache): {generatedVideo}");
                            return generatedVideo;
                        }
                    }
                }
            }

            // 3. Check Default Cache Path
            string dmdCacheSystem = Path.Combine(_config.CachePath, "dmd", system);
            
            var cacheCandidates = new List<string>();
            if (!string.IsNullOrEmpty(romFileName)) 
            {
                cacheCandidates.Add(romFileName);
                cacheCandidates.Add(Sanitize(romFileName));
            }
            if (!string.IsNullOrEmpty(gameName)) 
            {
                cacheCandidates.Add(gameName);
                cacheCandidates.Add(Sanitize(gameName));
            }
            
            foreach (var cand in cacheCandidates.Distinct())
            {
                // EN: If Composition is enabled, we prioritize finding a pre-composed image.
                // FR: Si la composition est activée, on priorise la recherche d'une image pré-composée.
                if (_config.DmdCompose)
                {
                     // Check for variants of composed filenames
                     // Matches ImageConversionService: {logoName}_composed.png
                     var possibleComposed = new[] 
                     { 
                         $"{cand}_composed.png", 
                         $"{cand}-marquee_composed.png",
                         $"{cand}-topper_composed.png"
                     };
                     
                     _logger.LogInformation($"[DMD Cache Debug] Checking composed candidates for '{cand}' (DmdCompose=True)...");

                     foreach(var composedName in possibleComposed)
                     {
                         string composedPath = Path.Combine(dmdCacheSystem, composedName);
                         if (File.Exists(composedPath))
                         {
                             _logger.LogInformation($"[DMD] Found cached media (Composed): {composedPath}");
                             return composedPath;
                         }
                     }
                     
                     // EN: If DmdCompose is TRUE, but we didn't find a composed image, 
                     // we should SKIP returning the simple "{cand}.png" if it exists.
                     // This forces the code to fall through to the Scan/Fallback logic (lines below),
                     // which will attempt to FIND fanart and GENERATE a composition.
                     // If that fails, it will just regenerate/return the simple image anyway.
                     // This fixes the issue where a "Logo Only" cache blocks new Composition generation.
                     continue; 
                }

                // Standard Behavior (No Composition or Not Enabled)
                string cachedPath = Path.Combine(dmdCacheSystem, $"{cand}.png");
                if (File.Exists(cachedPath))
                {
                    _logger.LogInformation($"[DMD] Found cached media: {cachedPath}");
                    return cachedPath;
                }
            }

            // 3. Fallback: Find Standard Media & Generate
            
            // Get offsets (Shared with Marquee)
            var (offX, offY, logoX, logoY, _, _) = _offsetService.GetOffset(system, gameName); 

            foreach (var sysCand in systemCandidates)
            {
                 // Check Configured Marquee Path
                 var pattern = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", gameName);
                 var logoRaw = FindSourceFile(Path.Combine(_config.MarqueeImagePath, pattern));
                 
                 if (logoRaw != null) 
                 {
                     _logger.LogInformation($"[DMD] Found RAW logo source for composition: {logoRaw}");
                     if (_config.DmdCompose)
                     {
                         string? bg = null;
                         if (_config.ComposeMedia.Equals("image", StringComparison.OrdinalIgnoreCase))
                         {
                              bg = FindGameImage(sysCand, gameName, romFileName);
                         }
                         if (bg == null) bg = FindGameFanart(sysCand, gameName, romFileName);
                         
                         if (bg != null)
                             return _imageService.ProcessDmdComposition(bg, logoRaw, system, offX, offY, logoX, logoY);
                            else
                                return _imageService.ProcessDmdImage(logoRaw, "defaults", system, gameName, logoX, logoY);
                    }
                    return _imageService.ProcessDmdImage(logoRaw, "defaults", system, gameName); 
                 }
                 
                 if (!string.IsNullOrEmpty(romFileName))
                 {
                     var pRom = _config.MarqueeFilePath.Replace("{system_name}", sysCand).Replace("{game_name}", romFileName);
                     var logoRomRaw = FindSourceFile(Path.Combine(_config.MarqueeImagePath, pRom));
                     if (logoRomRaw != null) 
                     {
                         _logger.LogInformation($"[DMD] Found RAW logo source for generation (ROM): {logoRomRaw}");
                         if (_config.DmdCompose)
                         {
                             string? bg = null;
                             if (_config.ComposeMedia.Equals("image", StringComparison.OrdinalIgnoreCase))
                             {
                                  bg = FindGameImage(sysCand, gameName, romFileName);
                             }
                             if (bg == null) bg = FindGameFanart(sysCand, gameName, romFileName);

                             if (bg != null)
                                 return _imageService.ProcessDmdComposition(bg, logoRomRaw, system, offX, offY, logoX, logoY);
                             else
                                 return _imageService.ProcessDmdImage(logoRomRaw, "defaults", system, gameName, logoX, logoY);
                        }
                        return _imageService.ProcessDmdImage(logoRomRaw, "defaults", system, gameName, logoX, logoY);
                     }
                 }

                 // Check Default Path (RetroBat/roms/...)
                 var defPattern = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", gameName);
                 var logoDefRaw = FindSourceFile(Path.Combine(_config.MarqueeImagePathDefault, defPattern));
                 if (logoDefRaw != null) 
                 {
                     _logger.LogInformation($"[DMD] Found RAW default logo source for generation: {logoDefRaw}");
                     if (_config.DmdCompose)
                     {
                         string? bg = null;
                         if (_config.ComposeMedia.Equals("image", StringComparison.OrdinalIgnoreCase))
                         {
                              bg = FindGameImage(sysCand, gameName, romFileName);
                         }
                         if (bg == null) bg = FindGameFanart(sysCand, gameName, romFileName);
 
                         if (bg != null)
                             return _imageService.ProcessDmdComposition(bg, logoDefRaw, system, offX, offY, logoX, logoY);
                            else
                                return _imageService.ProcessDmdImage(logoDefRaw, "defaults", system, gameName, logoX, logoY);
                    }
                    return _imageService.ProcessDmdImage(logoDefRaw, "defaults", system, gameName, logoX, logoY);
                 }
 
                 if (!string.IsNullOrEmpty(romFileName))
                 {
                     var defPatternRom = _config.MarqueeFilePathDefault.Replace("{system_name}", sysCand).Replace("{game_name}", romFileName);
                     var logoDefRomRaw = FindSourceFile(Path.Combine(_config.MarqueeImagePathDefault, defPatternRom));
                     if (logoDefRomRaw != null) 
                     {
                         _logger.LogInformation($"[DMD] Found RAW default logo source for generation (ROM): {logoDefRomRaw}");
                         if (_config.DmdCompose)
                         {
                             string? bg = null;
                             if (_config.ComposeMedia.Equals("image", StringComparison.OrdinalIgnoreCase))
                             {
                                  bg = FindGameImage(sysCand, gameName, romFileName);
                             }
                             if (bg == null) bg = FindGameFanart(sysCand, gameName, romFileName);
                             
                             if (bg != null)
                                 return _imageService.ProcessDmdComposition(bg, logoDefRomRaw, system, offX, offY, logoX, logoY);
                             else
                                return _imageService.ProcessDmdImage(logoDefRomRaw, "defaults", system, gameName, logoX, logoY);
                        }
                        return _imageService.ProcessDmdImage(logoDefRomRaw, "defaults", system, gameName, logoX, logoY);
                     }
                 }
            }
            
            // 4. Try System Logo as last resort
            // Only if romFileName is empty (System View) or Fallback?
            // Usually FindDmdImage is called for games. 
            // If it returns null, calling workflow handles system fallback.
            
            if (string.IsNullOrEmpty(romFileName))
            {
                // EN: Check Custom System DMD Path first (respect aliases)
                // FR: Vérifier d'abord le chemin Custom System DMD (respecter les alias)
                if (!string.IsNullOrEmpty(_config.SystemCustomDMDPath))
                {
                    foreach (var sysCand in systemCandidates)
                    {
                        foreach (var ext in _config.AcceptedFormats)
                        {
                            var customPath = Path.Combine(_config.SystemCustomDMDPath, $"{sysCand}.{ext}");
                            if (File.Exists(customPath))
                            {
                                _logger.LogInformation($"[DMD] Found system logo in custom DMD path: {customPath}");
                                return _imageService.ProcessDmdImage(customPath, "systems", null, null, logoX, logoY);
                            }
                        }
                    }
                }

                // Fallback to theme or default
                foreach (var sysCand in systemCandidates)
                {
                    var systemLogoRaw = FindSystemMarquee(sysCand, raw: true);
                    if (systemLogoRaw != null && systemLogoRaw != _config.DefaultImagePath)
                    {
                         _logger.LogInformation($"[DMD] Found system logo for generation: {systemLogoRaw}");
                         // Force "systems" subfolder for cache
                         return _imageService.ProcessDmdImage(systemLogoRaw, "systems", null, null, logoX, logoY);
                    }
                }
            }
            
            return null;
        }

        // Helper for independent usage (copied from FindGameMarquee scope for now, should refactor if needed)
        // EN: Stricter sanitization for DMD/Cache filenames - replace special chars and collapse underscores
        // FR: Désinfection plus stricte pour les noms de fichiers DMD/Cache - remplace les caractères spéciaux et réduit les underscores
        private string Sanitize(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            var result = input;
            
            // 1. Replace invalid filename chars
            var invalids = Path.GetInvalidFileNameChars();
            foreach (var c in invalids) result = result.Replace(c, '_');

            // 2. EN: Replace spaces and common delimiters that might cause issues with DMD drivers/CLI
            // FR: Remplacer les espaces et délimiteurs communs qui pourraient causer des soucis avec drivers DMD/CLI
            char[] delimiters = { ' ', '(', ')', '[', ']', '-', '.', ',' };
            foreach (var c in delimiters) result = result.Replace(c, '_');

            // 3. EN: Collapse multiple underscores into one
            // FR: Réduire les underscores multiples en un seul
            while (result.Contains("__")) result = result.Replace("__", "_");

            return result.Trim('_').Trim();
        }
        
        /// <summary>
        /// EN: Extract real system folder from ROM path
        /// FR: Extraire le vrai dossier système depuis le chemin ROM
        /// Example: S:/RetroBat7.x_test/roms/mame/bbredux.zip -> "mame"
        /// </summary>
        private string? ExtractSystemFromRomPath(string romPath)
        {
            if (string.IsNullOrEmpty(romPath)) return null;
            
            // Skip if this is just a game name without path (e.g., "donkeykong")
            if (!romPath.Contains("/") && !romPath.Contains("\\")) return null;
            
            try
            {
                var parts = romPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var romsIndex = Array.FindLastIndex(parts, p => p.Equals("roms", StringComparison.OrdinalIgnoreCase));
                
                if (romsIndex >= 0 && romsIndex + 1 < parts.Length)
                {
                    var extractedSystem = parts[romsIndex + 1];
                    _logger.LogInformation($"[ROM PATH] Extracted system '{extractedSystem}' from ROM path: {romPath}");
                    return extractedSystem;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ROM PATH] Failed to extract system from path '{romPath}': {ex.Message}");
            }
            
            return null;
        }

        public string? FindDmdGameStartMedia(string system, string romFileName, string gameName)
        {
            // EN: Priority 1 is EXCLUSIVE: Only trigger if paths/scrap are explicitly configured (alimenté)
            // FR: La Priorité 1 est EXCLUSIVE : Ne se déclenche que si les chemins/scrap sont explicitement configurés (alimenté)
            string customPath = _config.DmdGameStartMediaPath;
            string scrapType = _config.DMDScrapMediaType;

            if (string.IsNullOrEmpty(customPath) && string.IsNullOrEmpty(scrapType))
            {
                return null;
            }

            var extensions = new[] { ".mp4", ".avi", ".webm", ".mkv", ".gif", ".png", ".jpg" };
            var candidates = new List<string>();
            // Patterns: EXACT name, -topper, -marquee, -marquee_composed
            string[] suffixes = { "", "-topper", "-marquee", "-marquee_composed" };

            if (!string.IsNullOrEmpty(romFileName))
            {
                foreach (var s in suffixes) candidates.Add(romFileName + s);
            }
            if (!string.IsNullOrEmpty(gameName))
            {
                foreach (var s in suffixes) candidates.Add(gameName + s);
            }

            // 1. Search Custom Path (if fed)
            if (!string.IsNullOrEmpty(customPath) && Directory.Exists(customPath))
            {
                var searchDirs = new List<string>();
                if (!string.IsNullOrEmpty(system)) searchDirs.Add(Path.Combine(customPath, system));
                searchDirs.Add(customPath);

                foreach (var dir in searchDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var cand in candidates)
                    {
                        foreach (var ext in extensions)
                        {
                            var p = Path.Combine(dir, cand + ext);
                            if (File.Exists(p)) 
                            {
                                 _logger.LogInformation($"[DMD Priority 1] Found Custom Loading Media: {p}");
                                 // EN: Use distinct subfolder for start media to prevent cache collision if filename is identical to game marquee
                                 // FR: Utiliser un sous-dossier distinct pour éviter collision de cache si nom identique
                                 return _imageService.ProcessDmdImage(p, subFolder: system + "_start");
                            }
                        }
                    }
                }
            }


            // --- PRIORITY 2: VIDEO GENERATION (If Enabled) ---
            // EN: If no custom "loading" media found, check for generated video
            // FR: Si pas de média de chargement custom, vérifier la vidéo générée
            // Note: This logic duplicates FindDmdImageAsync generation logic but is specific for game-start to prioritize over scraped images found below.
            if (_config.MarqueeVideoGeneration)
            {
                var systemCandidates = new List<string> { system };
                var videoSource = TryFindVideo(romFileName, systemCandidates) ?? TryFindVideo(Sanitize(romFileName), systemCandidates) ?? TryFindVideo(gameName, systemCandidates) ?? TryFindVideo(Sanitize(gameName), systemCandidates);
                
                if (videoSource != null)
                {
                    // Minimal logo search for generation context
                    var logoRaw = TryFind(romFileName, systemCandidates, "-marquee", raw: true) 
                               ?? TryFind(gameName, systemCandidates, raw: true)
                               ?? FindSystemMarquee(system, raw: true);

                    if (logoRaw == _config.DefaultImagePath) logoRaw = "";

                    _logger.LogInformation($"[DMD GameStart] Found video source for generation: {videoSource}");
                    var generatedVideo = _imageService.GenerateMarqueeVideo(videoSource, logoRaw ?? "", system, romFileName);
                    if (generatedVideo != null)
                    {
                         // ProcessDmdImage will handle conversion/caching
                         // We use "system_start" folder to keep it separate? Or share cache?
                         // Ideally share cache if it's the same video, but "game-start" might want specific separate cache context? 
                         // GenerateMarqueeVideo usually returns the video path. We pass it to ProcessDmdImage or just return it? 
                         // Check return type: FindDmdGameStartMedia returns string.
                         // FindDmdImageAsync calls _imageService.GenerateMarqueeVideo then returns it (since PlayAsync handles mp4).
                         return generatedVideo;
                    }
                }
            }

            // --- PRIORITY 3: Search Scrap Cache Path (if scrap type is fed) ---
            if (!string.IsNullOrEmpty(scrapType) && !string.IsNullOrEmpty(_config.ScreenScraperCachePath) && Directory.Exists(_config.ScreenScraperCachePath))
            {
                var scrapDir = Path.Combine(_config.ScreenScraperCachePath, system);
                if (Directory.Exists(scrapDir))
                {
                    foreach (var cand in candidates)
                    {
                        foreach (var ext in extensions)
                        {
                            // Try with Scrap Type suffix
                            var pScrap = Path.Combine(scrapDir, $"{cand}_{scrapType}{ext}");
                            if (File.Exists(pScrap)) return _imageService.ProcessDmdImage(pScrap, subFolder: system + "_start");

                            // Try standard candidates in scrap folder
                            var p = Path.Combine(scrapDir, cand + ext);
                            if (File.Exists(p)) return _imageService.ProcessDmdImage(p, subFolder: system + "_start");
                        }
                    }
                }
            }



            return null;
        }

        // --- HELPER METHODS ---



        public string? FindDmdGameOverMarquee()
        {
             var candidatesMarquee = new List<string> { "game-over", "game-over-marquee" };
             var candidatesFanart = new List<string> { "game-over-fanart" };
             var extensions = new[] { ".mp4", ".gif", ".png", ".jpg" }; 
             
             // 1. Custom DMD Path
             if (!string.IsNullOrEmpty(_config.DmdMediaPath) && Directory.Exists(_config.DmdMediaPath))
             {
                 var (start, fanart) = CheckForGameOverInDir(_config.DmdMediaPath, candidatesMarquee, candidatesFanart, extensions);
                 if (start != null)
                 {
                     if (fanart != null) return _imageService.ProcessDmdComposition(fanart, start, "GameOver");
                     return _imageService.ProcessDmdImage(start, subFolder: "GameOver");
                 }
             }

             // 2. Default System Path
             var defaultGameOverDir = Path.Combine(_config.MarqueeImagePath, "GameOver");
             if (Directory.Exists(defaultGameOverDir))
             {
                 var (start, fanart) = CheckForGameOverInDir(defaultGameOverDir, candidatesMarquee, candidatesFanart, extensions);
                 if (start != null)
                 {
                     if (fanart != null) return _imageService.ProcessDmdComposition(fanart, start, "GameOver");
                     return _imageService.ProcessDmdImage(start, subFolder: "GameOver");
                 }
             }
             
             return null;
        }

        private (string? marquee, string? fanart) CheckForGameOverInDir(string dir, List<string> marquees, List<string> fanarts, string[] exts)
        {
            string? m = null;
            string? f = null;

            foreach(var cand in marquees)
            {
                foreach(var ext in exts)
                {
                    var file = Path.Combine(dir, cand + ext);
                    if (File.Exists(file)) 
                    {
                        m = file;
                        break;
                    }
                }
                if (m != null) break;
            }

            foreach(var cand in fanarts)
            {
                foreach(var ext in exts)
                {
                    var file = Path.Combine(dir, cand + ext);
                    if (File.Exists(file)) 
                    {
                        f = file;
                        break;
                    }
                }
                if (f != null) break;
            }
            return (m, f);
        }

        private string? FindSourceFile(string basePath)
        {
            foreach (var ext in _config.AcceptedFormats)
            {
                var fullPath = $"{basePath}.{ext.Trim()}";
                if (File.Exists(fullPath)) return fullPath;
            }
            return null;
        }
    }
}
