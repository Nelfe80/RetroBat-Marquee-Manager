using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Application.Services
{
    public class ScreenScraperService : IScraperService
    {
        public string Name => "ScreenScraper";
        public event Action<string, string, string?>? OnScrapeCompleted;
        private readonly ILogger<ScreenScraperService> _logger;
        private readonly IConfigService _config;
        private static readonly HttpClient _httpClient;
        
        // EN: API request queue - ScreenScraper basic accounts only allow 1 concurrent request
        // FR: File d'attente requêtes API - comptes de base ScreenScraper limitent à 1 requête simultanée
        private readonly SemaphoreSlim _apiLock;
        private static readonly Dictionary<string, Task<string?>> _pendingDownloads = new();
        private static readonly object _pendingLock = new object();
        
        // EN: Session cache for failed scraps to prevent endless retries on NotFound/Errors
        // FR: Cache de session pour les scraps échoués afin d'éviter les retries infinis sur NotFound/Erreurs
        private static readonly HashSet<string> _failedScraps = new();
        private static readonly object _failedLock = new object();
        private readonly string _failedScrapsPath;
        
        private int _dynamicThreadLimit;
        private int _activeRequests = 0;
        private readonly object _limitLock = new object();

        static ScreenScraperService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.93 Safari/537.36");
        }
        private Dictionary<string, string>? _systemIdMapping;
        private readonly string _systemsMapPath;

        // Dev credentials (should ideally be secure or passed in, but using generic specific ones for this app if available, or user provided)
        // Using generic dev info for now or relying on User provided params.
        private const string SoftName = "CustomRetroBatMarqueeManager";

        public ScreenScraperService(ILogger<ScreenScraperService> logger, IConfigService config)
        {
            _logger = logger;
            _config = config;
            
            // EN: Initialize semaphore with configured number of threads
            // FR: Initialiser le sémaphore avec le nombre de threads configuré
            int threadCount = _config.ScreenScraperThreads;
            _dynamicThreadLimit = threadCount;
            _apiLock = new SemaphoreSlim(threadCount, threadCount);
            _logger.LogInformation($"[ScreenScraper] Queue initialized with {threadCount} concurrent thread(s).");

            _systemsMapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "systems.scrap");
            _failedScrapsPath = Path.Combine(_config.MarqueeImagePath, "_cache", "scraps_failed.json");
            LoadSystemMapping();
            LoadFailedScraps();
        }

        private void LoadFailedScraps()
        {
            try
            {
                lock (_failedLock)
                {
                    if (File.Exists(_failedScrapsPath))
                    {
                        var json = File.ReadAllText(_failedScrapsPath);
                        var list = JsonSerializer.Deserialize<List<string>>(json);
                        if (list != null)
                        {
                            foreach (var item in list) _failedScraps.Add(item);
                            _logger.LogInformation($"Loaded {_failedScraps.Count} persistent scraping failures.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load failed scraps cache: {ex.Message}");
            }
        }

        private void SaveFailedScraps()
        {
            try
            {
                lock (_failedLock)
                {
                    var dir = Path.GetDirectoryName(_failedScrapsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var json = JsonSerializer.Serialize(_failedScraps.ToList(), new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_failedScrapsPath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to save failed scraps cache: {ex.Message}");
            }
        }

        public void ClearFailedScraps()
        {
            try
            {
                lock (_failedLock)
                {
                    _failedScraps.Clear();
                    if (File.Exists(_failedScrapsPath)) File.Delete(_failedScrapsPath);
                }
                _logger.LogInformation("Persistent scraping failure cache cleared.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error clearing failed scraps: {ex.Message}");
            }
        }

        private void LoadSystemMapping()
        {
            try
            {
                if (File.Exists(_systemsMapPath))
                {
                    var json = File.ReadAllText(_systemsMapPath);
                    _systemIdMapping = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    _logger.LogInformation($"Loaded {_systemIdMapping?.Count ?? 0} systems from systems.scrap");
                }
                else
                {
                    _logger.LogWarning($"systems.scrap not found at {_systemsMapPath}. Scraping might fail for system lookups.");
                    _systemIdMapping = new Dictionary<string, string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading systems.scrap: {ex.Message}");
                _systemIdMapping = new Dictionary<string, string>();
            }
        }

        public async Task<string?> CheckAndScrapeAsync(string systemName, string gameName, string gamePath, string mediaType)
        {
            await Task.CompletedTask;
            // mediaType: "mpv" or "dmd" (which maps to config types)
            
            if (!_config.MarqueeAutoScraping) 
                return null;

            if (string.IsNullOrWhiteSpace(_config.ScreenScraperUser) || string.IsNullOrWhiteSpace(_config.ScreenScraperPass))
            {
                _logger.LogWarning("ScreenScraper credentials missing. Skipping auto-scraping.");
                return null;
            }

            // Determine wanted media type from config
            string scrapMediaType = mediaType == "mpv" ? _config.MPVScrapMediaType : _config.DMDScrapMediaType;
            
            _logger.LogInformation($"[DEBUG] CheckAndScrapeAsync called: mediaType={mediaType}, scrapMediaType='{scrapMediaType}' (length={scrapMediaType?.Length ?? -1})");
            
            // If media type is empty, scraping is disabled by user choice
            if (string.IsNullOrWhiteSpace(scrapMediaType))
            {
                _logger.LogInformation($"[SCRAPING DISABLED] {mediaType.ToUpper()} scraping is OFF (MediaType config is empty)");
                return null;
            }

            string downloadKey = $"{systemName}_{gameName}_{mediaType}";

            // EN: If already failed in this session, do not try again (avoid "Scraping..." placeholder)
            // FR: Si déjà échoué dans cette session, ne pas retenter (évite le placeholder "Scraping...")
            lock (_failedLock)
            {
                if (_failedScraps.Contains(downloadKey))
                {
                    return null;
                }
            }
            
            // Map system name to ID
            if (_systemIdMapping == null || !_systemIdMapping.TryGetValue(systemName.ToLowerInvariant(), out var systemId))
            {
                // Try direct use if not mapped? Or fail safely
                _logger.LogWarning($"System '{systemName}' not found in systems.scrap mapping. Skipping.");
                return null;
            }

            // Target Cache File
            // User Request: Organize into system subfolders (medias/screenscraper/{system}/...)
            string cacheFileName = $"{Path.GetFileNameWithoutExtension(gamePath)}_{scrapMediaType}.{(scrapMediaType.Contains("video") ? "mp4" : "png")}";
            string cacheFilePath = Path.Combine(_config.ScreenScraperCachePath, systemName, cacheFileName);

            // Check if already cached
            if (File.Exists(cacheFilePath))
            {
                _logger.LogDebug($"Scraped media found in cache: {cacheFilePath}");
                return cacheFilePath;
            }

            // Ensure cache directory (including system subfolder) exists
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFilePath)!);

            // Check if download already in progress for this exact media type
            lock (_pendingLock)
            {
                if (_pendingDownloads.ContainsKey(downloadKey))
                {
                    _logger.LogDebug($"Download already in progress for {gameName} ({mediaType}), returning null (will show placeholder)");
                    return null; // Caller will show placeholder
                }
            }

            // Launch background download and return null immediately
            var downloadTask = Task.Run(async () => await DownloadInBackgroundAsync(systemName, gameName, gamePath, mediaType, scrapMediaType, cacheFilePath, downloadKey));
            
            lock (_pendingLock)
            {
                _pendingDownloads[downloadKey] = downloadTask;
            }
            
            _logger.LogInformation($"Queued background scraping for {gameName} ({systemName}) - {mediaType}");
            return null; // Show placeholder while downloading
        }

        private async Task<string?> DownloadInBackgroundAsync(string systemName, string gameName, string gamePath, string mediaType, string scrapMediaType, string cacheFilePath, string downloadKey)
        {
            // EN: Respect the API limit (adaptive based on account capacity)
            // FR: Respecter la limite API (adaptatif selon la capacité du compte)
            await _apiLock.WaitAsync();
            
            // Secondary gate for dynamic downscaling
            while (true)
            {
                lock (_limitLock)
                {
                    if (_activeRequests < _dynamicThreadLimit)
                    {
                        _activeRequests++;
                        break;
                    }
                }
                await Task.Delay(1000);
            }
            
            try
            {
                _logger.LogInformation($"Starting background scraping for {gameName} ({systemName})...");
                
                // Compute CRC32 of ROM (Matching Python Script preference)
                string? crc = CalculateCRC32(gamePath);
                if (string.IsNullOrEmpty(crc))
                {
                    _logger.LogInformation($"[Background Scrap] CRC32 unavailable for {gamePath} (might be a directory). Falling back to name-based search.");
                }

                // Call API
                // 1. Credentials
                var devId = _config.ScreenScraperDevId;
                var devPassword = _config.ScreenScraperDevPassword;
                var softName = SoftName;

                if (string.IsNullOrWhiteSpace(devId) || string.IsNullOrWhiteSpace(devPassword))
                {
                     _logger.LogInformation("[ScreenScraper] Dev credentials missing in config, using default app credentials.");
                     // EN: Default App Credentials
                     // FR: Identifiants par défaut de l'application
                     devId = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("BASE64_DEV_ID_HERE")); // TODO: Replace with new DevID Base64 if provided
                     devPassword = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("BASE64_DEV_PASSWORD_HERE")); // TODO: Replace with new Password Base64
                     softName = "RetroBatMarqueeManager"; 
                }
                else
                {
                     _logger.LogInformation($"[ScreenScraper] Using custom Dev credentials from config (SoftName: {softName})");
                }

                var fileInfo = new FileInfo(gamePath);
                var romSize = fileInfo.Exists ? fileInfo.Length.ToString() : "0";
                var romName = Path.GetFileName(gamePath);
                var romType = "rom";
                
                if (_systemIdMapping == null || !_systemIdMapping.TryGetValue(systemName.ToLowerInvariant(), out var systemId))
                {
                    _logger.LogWarning($"System '{systemName}' not found in systems.scrap mapping.");
                    return null;
                }

                var parameters = new Dictionary<string, string>
                {
                    { "devid", devId },
                    { "devpassword", devPassword },
                    { "softname", softName },
                    { "output", "json" },
                    { "ssid", _config.ScreenScraperUser ?? "" },
                    { "sspassword", _config.ScreenScraperPass ?? "" },
                    { "systemeid", systemId },
                    { "romnom", romName },
                    { "romtaille", romSize },
                    { "romtype", romType }
                };

                // EN: Add CRC only if available
                // FR: Ajouter le CRC seulement si disponible
                if (!string.IsNullOrEmpty(crc))
                {
                    parameters["crc"] = crc;
                }

                string? mediaUrl = null;
                string? mediaExt = null;
                (string? url, string? ext, string? title, string? system)? currentGlobal = null;

                // Stage 1: CRC Search (Strict)
                if (!string.IsNullOrEmpty(crc))
                {
                    _logger.LogInformation($"[Background Scrap] Stage 1 attempt (CRC): {crc}...");
                    var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(parameters, scrapMediaType, isSearch: false, forceStrict: true));
                    mediaUrl = found.mediaUrl;
                    mediaExt = found.mediaExt;
                    currentGlobal = found.bestGlobal;
                }
 
                // Stage 2: ROM Name (Strict)
                if (string.IsNullOrEmpty(mediaUrl))
                {
                    string romNameNoExt = Path.GetFileNameWithoutExtension(romName);
                    _logger.LogInformation($"[Background Scrap] Media not found for {gameName} (Strict). Stage 2 attempt (ROM Name): {romNameNoExt}...");
                    var retryParams = new Dictionary<string, string>(parameters);
                    retryParams.Remove("crc");
                    retryParams["romnom"] = romNameNoExt;
                    
                    var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(retryParams, scrapMediaType, isSearch: false, forceStrict: true));
                    mediaUrl = found.mediaUrl;
                    mediaExt = found.mediaExt;
                    if (currentGlobal == null) currentGlobal = found.bestGlobal;
                }

                // Prepare for Stage 3-5
                string cleanedName = CleanGameName(gameName);

                // Stage 3: Cleaned Name Search (Strict)
                if (string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(gameName))
                {
                    _logger.LogInformation($"[Background Scrap] Media still not found for {gameName} (Strict). Stage 3 attempt (Cleaned Name): {cleanedName}...");
                    
                    var searchParams = new Dictionary<string, string>(parameters);
                    searchParams.Remove("crc");
                    searchParams.Remove("romnom");
                    searchParams["recherche"] = cleanedName;

                    var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(searchParams, scrapMediaType, isSearch: true, forceStrict: true));
                    mediaUrl = found.mediaUrl;
                    mediaExt = found.mediaExt;
                    if (currentGlobal == null) currentGlobal = found.bestGlobal;
                }
 
                // Stage 4: Full Name Search (Strict) - ONLY if different from cleaned name
                if (string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(gameName) && !string.Equals(cleanedName, gameName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation($"[Background Scrap] Media still not found for {gameName} (Strict). Stage 4 attempt (Full Name): {gameName}...");
                    
                    var searchParams = new Dictionary<string, string>(parameters);
                    searchParams.Remove("crc");
                    searchParams.Remove("romnom");
                    searchParams["recherche"] = gameName;
 
                    var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(searchParams, scrapMediaType, isSearch: true, forceStrict: true));
                    mediaUrl = found.mediaUrl;
                    mediaExt = found.mediaExt;
                    if (currentGlobal == null) currentGlobal = found.bestGlobal;
                }
 
                // Stage 5: Full Name + System Search (Strict)
                if (string.IsNullOrEmpty(mediaUrl) && !string.IsNullOrEmpty(gameName))
                {
                    _logger.LogInformation($"[Background Scrap] Media still not found for {gameName} (Strict). Stage 5 attempt (System Name): {gameName} {systemName}...");
                    
                    var searchParams = new Dictionary<string, string>(parameters);
                    searchParams.Remove("crc");
                    searchParams.Remove("romnom");
                    searchParams["recherche"] = $"{gameName} {systemName}";
 
                    var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(searchParams, scrapMediaType, isSearch: true, forceStrict: true));
                    mediaUrl = found.mediaUrl;
                    mediaExt = found.mediaExt;
                    if (currentGlobal == null) currentGlobal = found.bestGlobal;
                }
 
                // FINAL GLOBAL FALLBACK: Use cached global result or do a search without system filter (if enabled)
                if (string.IsNullOrEmpty(mediaUrl) && _config.MarqueeGlobalScraping)
                {
                    if (currentGlobal != null && currentGlobal.Value.url != null)
                    {
                        var (gUrl, gExt, gTitle, gSysId) = currentGlobal.Value;
                        _logger.LogInformation($"[Background Scrap] Reusing cached global fallback from system {gSysId ?? "unknown"}: {gTitle}");
                        mediaUrl = gUrl;
                        mediaExt = gExt;
                    }
                    else
                    {
                        _logger.LogInformation($"[Background Scrap] No global match found during strict stages. Attempting FINAL GLOBAL FALLBACK SEARCH for: {cleanedName}...");
                        var searchParams = new Dictionary<string, string>(parameters);
                        searchParams.Remove("crc");
                        searchParams.Remove("romnom");
                        searchParams["recherche"] = cleanedName;
 
                        var found = await CallWithRetryAsync(() => CallApiAndExtractMedia(searchParams, scrapMediaType, isSearch: true, forceStrict: false));
                        mediaUrl = found.mediaUrl;
                        mediaExt = found.mediaExt;
                    }
                }

                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    // Success!
                    if (!string.IsNullOrEmpty(mediaExt) && !cacheFilePath.EndsWith(mediaExt, StringComparison.OrdinalIgnoreCase))
                    {
                        cacheFilePath = Path.ChangeExtension(cacheFilePath, mediaExt);
                    }

                    await DownloadFileAsync(mediaUrl, cacheFilePath);
                    _logger.LogInformation($"[Background Scrap Success] Downloaded media to {cacheFilePath}");
                    OnScrapeCompleted?.Invoke(systemName, gameName, cacheFilePath);
                    return cacheFilePath;
                }
                else
                {
                    _logger.LogWarning($"[Background Scrap] Media '{scrapMediaType}' not found for {gameName} after all fallback attempts (CRC, Name, DisplayName Search).");
                    lock (_failedLock) { _failedScraps.Add(downloadKey); }
                    SaveFailedScraps();
                    
                    // Notify failure so listeners can unblock/fallback
                    OnScrapeCompleted?.Invoke(systemName, gameName, null);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Background scraping error for {gameName}: {ex.Message}");
                // Notify failure on error too
                OnScrapeCompleted?.Invoke(systemName, gameName, null);
            }
            finally
            {
                lock (_limitLock)
                {
                    _activeRequests--;
                }
                _apiLock.Release();
                lock (_pendingLock)
                {
                    _pendingDownloads.Remove(downloadKey);
                }
            }
            return null;
        }

        private string CleanGameName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return name;
            // Remove text in parentheses () and brackets []
            // FR: Correction de la Regex pour les crochets [ ]
            string cleaned = Regex.Replace(name, @"\(.*?\)|\[.*?\]", "");
            // Standardize spaces
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        private string? GetSystemIdFromJeu(JsonElement jeu)
        {
            if (jeu.TryGetProperty("systeme", out var sys))
            {
                if (sys.TryGetProperty("id", out var idProp))
                {
                    return idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.GetRawText();
                }
            }
            
            if (jeu.TryGetProperty("system", out var sysAlt))
            {
                if (sysAlt.TryGetProperty("id", out var idProp))
                {
                    return idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.GetRawText();
                }
            }
            
            return null;
        }

        private string GetGameTitleFromJeu(JsonElement jeu)
        {
            // EN: Try to find a name, prioritize English "en", French "fr", or first one
            // FR: Chercher un nom, priorité Anglais, Français, ou le premier trouvé
            if (jeu.TryGetProperty("noms", out var noms) && noms.ValueKind == JsonValueKind.Array)
            {
                string? fallbackName = null;
                foreach (var nomItem in noms.EnumerateArray())
                {
                    if (nomItem.TryGetProperty("nom", out var nProp))
                    {
                        string name = nProp.GetString() ?? "";
                        if (string.IsNullOrEmpty(fallbackName)) fallbackName = name;

                        if (nomItem.TryGetProperty("langue", out var lProp))
                        {
                            string lang = lProp.GetString() ?? "";
                            if (lang == "en" || lang == "fr") return name;
                        }
                    }
                }
                return fallbackName ?? "Untitled";
            }
            return "Untitled";
        }

        private async Task<(string? mediaUrl, string? mediaExt, (string? url, string? ext, string? title, string? system)? bestGlobal, bool threadLimitHit)> CallApiAndExtractMedia(Dictionary<string, string> parameters, string scrapMediaType, bool isSearch = false, bool forceStrict = false)
        {
            try
            {
                var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
                var endpoint = isSearch ? "jeuRecherche.php" : "jeuInfos.php";
                
                // FR: Si la recherche globale est désactivée, on force l'identifiant du système
                if (isSearch && !_config.MarqueeGlobalScraping && parameters.TryGetValue("systemeid", out var systemId))
                {
                    // For jeuRecherche, filtering by system ensures we stay in the chosen system
                    // But if it's already in parameters (which it is), we are fine. 
                    // HOWEVER, if the search returns multiple, we should be sure we filter.
                }

                var url = $"https://api.screenscraper.fr/api2/{endpoint}?{queryString}";
                
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("response", out var resp))
                    {
                        string? targetSystemId = parameters.TryGetValue("systemeid", out var sid) ? sid : null;

                        // jeuRecherche returns objects inside "jeux", jeuInfos returns a single "jeu"
                        if (isSearch)
                        {
                            if (resp.TryGetProperty("jeux", out var jeux) && jeux.ValueKind == JsonValueKind.Array)
                            {
                                (string? url, string? ext) globalFallback = (null, null);
                                string? globalFallbackTitle = null;
                                string? globalFallbackSystem = null;

                                foreach (var jeuItem in jeux.EnumerateArray())
                                {
                                    string? jeuSystemId = GetSystemIdFromJeu(jeuItem);
                                    bool isMatchingSystem = (targetSystemId != null && jeuSystemId != null && jeuSystemId.Trim() == targetSystemId.Trim());

                                    if (isMatchingSystem)
                                    {
                                        var result = ExtractMediaFromJeu(jeuItem, scrapMediaType);
                                        if (result.url != null)
                                        {
                                            _logger.LogInformation($"[Background Scrap] Found prioritized match in target system {jeuSystemId}: {GetGameTitleFromJeu(jeuItem)}");
                                            return (result.url, result.ext, globalFallback.url != null ? (globalFallback.url, globalFallback.ext, globalFallbackTitle, globalFallbackSystem) : null, false);
                                        }
                                    }
                                    else if (_config.MarqueeGlobalScraping && globalFallback.url == null)
                                    {
                                        // Store first global result as fallback (even in strict mode, we'll return it for potential future use)
                                        var result = ExtractMediaFromJeu(jeuItem, scrapMediaType);
                                        if (result.url != null)
                                        {
                                            globalFallback = result;
                                            globalFallbackTitle = GetGameTitleFromJeu(jeuItem);
                                            globalFallbackSystem = jeuSystemId;
                                        }
                                    }

                                    if (!isMatchingSystem && !forceStrict)
                                    {
                                        string title = GetGameTitleFromJeu(jeuItem);
                                        _logger.LogInformation($"[Background Scrap] Skipping search result '{title}' from system {jeuSystemId ?? "unknown"} (Target: {targetSystemId})");
                                    }
                                }

                                if (globalFallback.url != null)
                                {
                                    if (!forceStrict)
                                    {
                                        _logger.LogInformation($"[Background Scrap] No target system match. Using global fallback from {globalFallbackSystem ?? "unknown"}: {globalFallbackTitle}");
                                        return (globalFallback.url, globalFallback.ext, null, false);
                                    }
                                    return (null, null, (globalFallback.url, globalFallback.ext, globalFallbackTitle, globalFallbackSystem), false);
                                }
                            }
                        }
                        else if (resp.TryGetProperty("jeu", out var jeu))
                        {
                            string? jeuSystemId = GetSystemIdFromJeu(jeu);
                            bool isMatchingSystem = (targetSystemId != null && jeuSystemId != null && jeuSystemId.Trim() == targetSystemId.Trim());

                            if (_config.MarqueeGlobalScraping || isMatchingSystem)
                            {
                                if (forceStrict && !isMatchingSystem)
                                {
                                    var result = ExtractMediaFromJeu(jeu, scrapMediaType);
                                    return (null, null, result.url != null ? (result.url, result.ext, GetGameTitleFromJeu(jeu), jeuSystemId) : null, false);
                                }
                                
                                var finalResult = ExtractMediaFromJeu(jeu, scrapMediaType);
                                return (finalResult.url, finalResult.ext, null, false);
                            }
                            else
                            {
                                _logger.LogInformation($"[Background Scrap] Skipping result from system {jeuSystemId ?? "unknown"} (Target: {targetSystemId}) and global scraping disabled.");
                            }
                        }
                    }
                    return (null, null, null, false);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    bool isThreadLimit = response.StatusCode == (System.Net.HttpStatusCode)429 || errorBody.Contains("threads", StringComparison.OrdinalIgnoreCase);
                    
                    if (isThreadLimit)
                    {
                        _logger.LogWarning($"[ScreenScraper] Account concurrency limit REACHED! (HTTP {(int)response.StatusCode}). Adjusting capacity automatically.");
                        return (null, null, null, true);
                    }

                    _logger.LogWarning($"[Background Scrap] API Error {(isSearch ? "Search" : "jeuInfos.php")}: {response.StatusCode} - {errorBody}");
                    return (null, null, null, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[Background Scrap] API Call/Extract failed (isSearch={isSearch}): {ex.Message}");
                return (null, null, null, false);
            }
        }

        private async Task<(string? mediaUrl, string? mediaExt, (string? url, string? ext, string? title, string? system)? bestGlobal)> CallWithRetryAsync(Func<Task<(string? mediaUrl, string? mediaExt, (string? url, string? ext, string? title, string? system)? bestGlobal, bool threadLimitHit)>> apiCall)
        {
            int retryCount = 0;
            const int MaxRetries = 2;

            while (retryCount <= MaxRetries)
            {
                var result = await apiCall();
                if (result.threadLimitHit)
                {
                    lock (_limitLock)
                    {
                        // Dynamically reduce the limit to the current active requests - 1 (min 1)
                        _dynamicThreadLimit = Math.Max(1, _activeRequests - 1);
                        _logger.LogWarning($"[Adaptive Concurrency] Reducing thread capacity to {_dynamicThreadLimit} based on API feedback.");
                    }

                    if (retryCount < MaxRetries)
                    {
                        retryCount++;
                        int delay = 3000 * retryCount; // 3s, 6s...
                        await Task.Delay(delay);
                        continue;
                    }
                }
                
                return (result.mediaUrl, result.mediaExt, result.bestGlobal);
            }

            return (null, null, null);
        }

        private (string? url, string? ext) ExtractMediaFromJeu(JsonElement jeu, string targetScrapMediaType)
        {
            if (jeu.TryGetProperty("medias", out var medias))
            {
                foreach (var media in medias.EnumerateArray())
                {
                    if (media.TryGetProperty("type", out var typeProp) && typeProp.GetString() == targetScrapMediaType)
                    {
                        if (media.TryGetProperty("url", out var urlProp))
                        {
                            string? mediaUrl = urlProp.GetString();
                            string? mediaExt = media.TryGetProperty("format", out var fmtProp) ? fmtProp.GetString() : null;
                            return (mediaUrl, mediaExt);
                        }
                    }
                }
            }
            return (null, null);
        }

        private async Task DownloadFileAsync(string url, string path)
        {
            var data = await _httpClient.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(path, data);
        }

        private string? CalculateMD5(string filename)
        {
            try
            {
                using (var md5 = MD5.Create())
                {
                    using (var stream = File.OpenRead(filename))
                    {
                        var hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error calculating MD5: {ex.Message}");
                return null;
            }
        }

        private string? CalculateCRC32(string filename)
        {
            try
            {
                // EN: Check if it's a directory (PS3 games, etc.)
                // FR: Vérifier si c'est un dossier (jeux PS3, etc.)
                if (Directory.Exists(filename))
                {
                    _logger.LogInformation($"[CRC32] {filename} is a directory. Skipping hash calculation.");
                    return null;
                }

                // Simple Table-Driven CRC32 Implementation
                uint crc = 0xffffffff;
                uint[] table = new uint[256];
                const uint poly = 0xedb88320;
                for (uint i = 0; i < 256; i++)
                {
                    uint temp = i;
                    for (int j = 0; j < 8; j++)
                    {
                        if ((temp & 1) == 1) temp = (temp >> 1) ^ poly;
                        else temp >>= 1;
                    }
                    table[i] = temp;
                }

                using (var fs = File.OpenRead(filename))
                {
                    var buffer = new byte[8192];
                    int bytesRead;
                    while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        for (int i = 0; i < bytesRead; i++)
                        {
                            byte index = (byte)(((crc) & 0xff) ^ buffer[i]);
                            crc = (crc >> 8) ^ table[index];
                        }
                    }
                }
                return (~crc).ToString("X8");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error calculating CRC32: {ex.Message}");
                return null;
            }
        }
        public bool IsScraping(string systemName, string gameName, string mediaType)
        {
            string downloadKey = $"{systemName}_{gameName}_{mediaType}";
            lock (_pendingLock)
            {
                return _pendingDownloads.ContainsKey(downloadKey);
            }
        }
    }
    
    // Extension helper for HttpClient if needed, otherwise normal GetAsync
    internal static class HttpClientExtensions
    {
        public static async Task<HttpResponseMessage> GetSettingsAsync(this HttpClient client, string url)
        {
            // Add appropriate headers if needed, currently straightforward
            return await client.GetAsync(url);
        }
    }
}
