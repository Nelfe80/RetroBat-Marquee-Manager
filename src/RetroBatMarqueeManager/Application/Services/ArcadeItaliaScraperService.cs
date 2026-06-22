using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Application.Services
{
    public class ArcadeItaliaScraperService : IScraperService
    {
        public string Name => "arcadeitalia";
        public event Action<string, string, string?>? OnScrapeCompleted;

        private readonly ILogger<ArcadeItaliaScraperService> _logger;
        private readonly IConfigService _config;
        private static readonly HttpClient _httpClient;
        private static readonly Dictionary<string, Task<string?>> _pendingDownloads = new();

        private static readonly object _pendingLock = new object();

        // Failed Scraps Cache
        private static readonly HashSet<string> _failedScraps = new();
        private static readonly object _failedLock = new object();
        private readonly string _failedScrapsPath;

        static ArcadeItaliaScraperService()
        {
            _httpClient = new HttpClient();
            // ADB requires a user agent usually
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/90.0.4430.93 Safari/537.36");
        }

        public ArcadeItaliaScraperService(ILogger<ArcadeItaliaScraperService> logger, IConfigService config)
        {
            _logger = logger;
            _config = config;
            _failedScrapsPath = Path.Combine(_config.MarqueeImagePath, "_cache", "arcadeitalia_failed.json");
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
                            _logger.LogInformation($"[ArcadeItalia] Loaded {_failedScraps.Count} persistent scraping failures.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ArcadeItalia] Failed to load failed scraps cache: {ex.Message}");
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
                    var json = JsonSerializer.Serialize(_failedScraps.ToList());
                    File.WriteAllText(_failedScrapsPath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[ArcadeItalia] Failed to save failed scraps cache: {ex.Message}");
            }
        }

        public async Task<string?> CheckAndScrapeAsync(string systemName, string gameName, string gamePath, string mediaType)
        {
            await Task.CompletedTask; // Silence CS1998 (Synchronous check + Background Task)
            // Only perform scraping if MarqueeAutoScraping is enabled globally
            if (!_config.MarqueeAutoScraping)
                return null;

            // Check if this scraper is specifically configured/enabled via MediaType
            // If ArcadeItaliaMediaType is empty, we assume disabled
            if (string.IsNullOrWhiteSpace(_config.ArcadeItaliaMediaType))
                return null;

            // Currently only supporting 'marquee' for ArcadeItalia as per requirements/plan
            // Media type requested must match what we are configured to scrape (or generic 'marquee' mapping)
            // But usually mediaType passed here is "mpv" or "dmd". 
            // We need to decide if we scrape based on the requested output (mpv/dmd) mapping to marquee.
            
            // EN: Logic: The caller (Manager) asks for "mpv" or "dmd".
            // We assume ArcadeItalia provides "images" (marquees).
            
            // REMOVED: Dependency on ScreenScraper config keys (MPVScrapMediaType/DMDScrapMediaType)
            // ArcadeItalia uses its own ArcadeItaliaMediaType config.

            // ADB is primarily for MAME/Arcade. check system?
            // "arcade", "mame", "neogeo", "fba", "fbneo" are likely candidates.
            // For now, we won't strictly filter by system unless required, but ADB is arcade specific.
            // Let's rely on the result being found or not.

            string romName = Path.GetFileNameWithoutExtension(gamePath);
            // EN: Include gameName in key to allow IsScraping to find it via Contains(gameName)
            // FR: Inclure gameName dans la clé pour permettre à IsScraping de le trouver via Contains(gameName)
            // EN: Removed mediaType to deduplicate downloads since we always fetch the same 'marquee' file regardless of type logic here.
            string downloadKey = $"{Name}_{systemName}_{romName}_{gameName}";

            // Check Negative Cache
            lock (_failedLock)
            {
                // We use a broader key for failure (System + RomName) to avoid retrying different media types if ROM is invalid?
                // Actually mediaType matters (e.g. might have marquee but not video).
                // Let's use downloadKey.
                if (_failedScraps.Contains(downloadKey))
                {
                    // _logger.LogDebug($"[ArcadeItalia] Skipping {romName} (in negative cache).");
                    return null;
                }
            }

            // Check cache
            string cacheFileName = $"{romName}_arcadeitalia.png";
            string cacheFilePath = Path.Combine(_config.MarqueeImagePath, "arcadeitalia", systemName, cacheFileName);

            if (File.Exists(cacheFilePath))
            {
                _logger.LogDebug($"[ArcadeItalia] Media found in cache: {cacheFilePath}");
                return cacheFilePath;
            }

            // Check pending
            lock (_pendingLock)
            {
                if (_pendingDownloads.ContainsKey(downloadKey))
                    return null;
            }

            // Start background download
            var downloadTask = Task.Run(async () => await DownloadInBackgroundAsync(systemName, romName, cacheFilePath, downloadKey));
            
            lock (_pendingLock)
            {
                _pendingDownloads[downloadKey] = downloadTask;
            }

            return null;
        }

        private async Task<string?> DownloadInBackgroundAsync(string systemName, string romName, string cacheFilePath, string downloadKey)
        {
            try
            {
                _logger.LogInformation($"[ArcadeItalia] Starting background scrap for {romName} ({systemName})...");
                
                // Construct ADB direct URL for marquee
                // Pattern: http://adb.arcadeitalia.net/media/mame.current/marquees/{romName}.png
                // This is a common pattern for valid MAME roms.
                // We might need to handle jpg/png.
                
                string[] extensions = { ".png", ".jpg" };
                string baseUrl = _config.ArcadeItaliaUrl.TrimEnd('/'); 
                // Ensure URL structure. If config is just the domain, we append path.
                // Default: http://adb.arcadeitalia.net
                
                // Try PNG first
                string imageUrl = $"{baseUrl}/media/mame.current/marquees/{romName}.png";
                
                bool found = await TryDownloadAsync(imageUrl, cacheFilePath);
                if (!found)
                {
                    // Try JPG
                    imageUrl = $"{baseUrl}/media/mame.current/marquees/{romName}.jpg";
                    found = await TryDownloadAsync(imageUrl, cacheFilePath);
                }

                if (found)
                {
                    _logger.LogInformation($"[ArcadeItalia] Successfully scraped: {cacheFilePath}");
                    
                    // Fix: Remove from pending BEFORE invoking event to avoid race condition where listener checks IsScraping and sees true
                    lock (_pendingLock) { _pendingDownloads.Remove(downloadKey); }
                    
                    OnScrapeCompleted?.Invoke(systemName, romName, cacheFilePath);
                    return cacheFilePath;
                }
                else
                {
                    _logger.LogWarning($"[ArcadeItalia] Media not found for {romName}");
                    lock (_failedLock)
                    {
                        _failedScraps.Add(downloadKey);
                        SaveFailedScraps();
                    }
                    
                    // Fix: Remove from pending BEFORE invoking event
                    lock (_pendingLock) { _pendingDownloads.Remove(downloadKey); }
                    
                    // Notify failure
                    OnScrapeCompleted?.Invoke(systemName, romName, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[ArcadeItalia] Error scraping {romName}: {ex.Message}");
                
                // Fix: Remove from pending BEFORE invoking event
                lock (_pendingLock) { _pendingDownloads.Remove(downloadKey); }
                
                // Notify failure
                OnScrapeCompleted?.Invoke(systemName, romName, null);
            }
            finally
            {
                // Safety net: Ensure it's removed if not already
                lock (_pendingLock)
                {
                    if (_pendingDownloads.ContainsKey(downloadKey))
                        _pendingDownloads.Remove(downloadKey);
                }
            }
            return null;
        }

        private async Task<bool> TryDownloadAsync(string url, string targetPath)
        {
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    // Ensure content type is image
                    if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image") == true)
                    {
                        var data = await response.Content.ReadAsByteArrayAsync();
                        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                        await File.WriteAllBytesAsync(targetPath, data);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"[ArcadeItalia] Failed to download {url}: {ex.Message}");
            }
            return false;
        }

        public bool IsScraping(string systemName, string gameName, string mediaType)
        {
             string romName = gameName; // Simplification, usually gameName is passed as rom name or we extract it. 
             // Ideally CheckAndScrapeAsync uses romPath to get romName. 
             // Here we might mismatch if gameName != romName. 
             // But for IsScraping checks, we just need consistency.
             // We'll use the same key logic if possible, but gamePath is not available here?
             // Dictionary key used 'romName' extracted from path.
             // We should probably rely on the caller passing consistent identifiers.
             // For now, iterate keys or try to match.
             
             lock (_pendingLock)
             {
                 // Scan keys for partial match if needed or assumes strict usage
                 foreach(var key in _pendingDownloads.Keys)
                 {
                     if (key.Contains(systemName) && key.Contains(gameName)) return true;
                 }
                 return false;
             }
        }
    }
}
