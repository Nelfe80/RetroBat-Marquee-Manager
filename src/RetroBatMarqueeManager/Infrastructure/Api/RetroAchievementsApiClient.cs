using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using RetroBatMarqueeManager.Infrastructure.Configuration;
using System.Text.Json;

namespace RetroBatMarqueeManager.Infrastructure.Api
{
    /// <summary>
    /// EN: HTTP client for RetroAchievements.org API
    /// FR: Client HTTP pour l'API RetroAchievements.org
    /// </summary>
    public class RetroAchievementsApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly IConfigService _config;
        private readonly IEsSettingsService _esSettings;
        private readonly ILogger<RetroAchievementsApiClient> _logger;

        private const string BaseUrl = "https://retroachievements.org/API";
        private const string MediaBaseUrl = "https://media.retroachievements.org";

        // EN: Persistent data directories / FR: Dossiers de données persistantes
        private readonly string _cacheBaseDir;
        private readonly string _apiCacheDir;
        private readonly string _badgesCacheDir;
        private readonly string _gameImagesCacheDir;
        private readonly string _userImagesCacheDir;

        public string BadgeCachePath => _badgesCacheDir;

        public RetroAchievementsApiClient(
            HttpClient httpClient,
            IConfigService config,
            IEsSettingsService esSettings,
            ILogger<RetroAchievementsApiClient> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _esSettings = esSettings;
            _logger = logger;

            // EN: Setup persistent RA data directories (NOT in cache to prevent deletion)
            // FR: Configurer dossiers données RA persistants (PAS dans cache pour éviter suppression)
            var mediasPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "medias");
            _cacheBaseDir = Path.Combine(mediasPath, "retroachievements");
            _apiCacheDir = Path.Combine(_cacheBaseDir, "api");
            _badgesCacheDir = Path.Combine(_cacheBaseDir, "badges");
            _gameImagesCacheDir = Path.Combine(_cacheBaseDir, "game_images");
            _userImagesCacheDir = Path.Combine(_cacheBaseDir, "user_images");

            Directory.CreateDirectory(_apiCacheDir);
            Directory.CreateDirectory(_badgesCacheDir);
            Directory.CreateDirectory(_gameImagesCacheDir);
            Directory.CreateDirectory(_userImagesCacheDir);
        }

        /// <summary>
        /// EN: Get credentials (username from ES, Web API Key from config.ini)
        /// FR: Récupérer identifiants (username depuis ES, Web API Key depuis config.ini)
        /// </summary>
        private (string username, string webApiKey) GetCredentials()
        {
            // EN: Username from ES settings / FR: Nom d'utilisateur depuis paramètres ES
            var username = _esSettings.GetSetting("global.retroachievements.username") ?? string.Empty;
            
            // EN: Web API Key from config.ini / FR: Clé Web API depuis config.ini
            var webApiKey = _config.RetroAchievementsWebApiKey ?? string.Empty;

            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("[RA API] Missing username in es_settings.cfg");
                return (string.Empty, string.Empty);
            }

            if (string.IsNullOrEmpty(webApiKey))
            {
                _logger.LogWarning("[RA API] Missing RetroAchievements Web API Key in config.ini");
                _logger.LogWarning("[RA API] Generate your Web API Key at: https://retroachievements.org/settings");
                _logger.LogWarning("[RA API] Add it to config.ini: RetroAchievementsWebApiKey=YOUR_KEY_HERE");
                return (string.Empty, string.Empty);
            }

            return (username, webApiKey);
        }


        /// <summary>
        /// EN: Generic API call with caching
        /// FR: Appel API générique avec cache
        /// </summary>
        private async Task<T?> CallApiAsync<T>(string endpoint, Dictionary<string, string> parameters, string cacheKey, bool useCache = true)
        {
            var (username, token) = GetCredentials();
            if (string.IsNullOrEmpty(username))
            {
                return default;
            }

            // EN: Add authentication / FR: Ajouter authentification
            parameters["z"] = username;
            parameters["y"] = token;

            // EN: Check cache first / FR: Vérifier cache d'abord
            if (useCache)
            {
                var cached = LoadFromCache<T>(cacheKey);
                if (cached != null)
                {
                    _logger.LogInformation($"[RA API] Cache hit: {cacheKey}");
                    return cached;
                }
            }

            // EN: Build URL / FR: Construire URL
            var queryString = string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
            var url = $"{BaseUrl}/{endpoint}?{queryString}";

            try
            {
                _logger.LogInformation($"[RA API] Calling: {endpoint}");
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
                    {
                        _logger.LogWarning($"[RA API] Game not found or not supported by RA (Status 422): {endpoint}");
                    }
                    else
                    {
                        _logger.LogWarning($"[RA API] Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}) for {endpoint}");
                    }
                    return default;
                }

                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                // EN: Save to cache / FR: Sauvegarder dans cache
                if (result != null && useCache)
                {
                    SaveToCache(cacheKey, result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA API] Error calling {endpoint}: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// EN: Load JSON response from cache
        /// FR: Charger réponse JSON depuis cache
        /// </summary>
        private T? LoadFromCache<T>(string cacheKey)
        {
            var cachePath = Path.Combine(_apiCacheDir, $"{cacheKey}.json");
            if (!File.Exists(cachePath))
            {
                return default;
            }

            try
            {
                var json = File.ReadAllText(cachePath);
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RA API] Failed to load cache {cacheKey}: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// EN: Save JSON response to cache
        /// FR: Sauvegarder réponse JSON dans cache
        /// </summary>
        private void SaveToCache<T>(string cacheKey, T data)
        {
            var cachePath = Path.Combine(_apiCacheDir, $"{cacheKey}.json");
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(cachePath, json);
                _logger.LogInformation($"[RA API] Saved cache: {cacheKey}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[RA API] Failed to save cache {cacheKey}: {ex.Message}");
            }
        }

        /// <summary>
        /// EN: Download and cache an image
        /// FR: Télécharger et mettre en cache une image
        /// </summary>
        private async Task<string?> DownloadImageAsync(string relativeUrl, string category, int? gameId = null)
        {
            if (string.IsNullOrEmpty(relativeUrl))
            {
                return null;
            }

            var fileName = Path.GetFileName(relativeUrl);
            
            // EN: Organize by type and game ID / FR: Organiser par type et ID jeu
            string cacheDir;
            if (category == "badge" && gameId.HasValue)
            {
                // EN: badges/9962/47042.png / FR: badges/9962/47042.png
                var gameFolder = Path.Combine(_badgesCacheDir, gameId.Value.ToString());
                Directory.CreateDirectory(gameFolder);
                cacheDir = gameFolder;
            }
            else if (category == "badge")
            {
                // EN: Fallback for badges without game ID / FR: Fallback pour badges sans ID jeu
                cacheDir = _badgesCacheDir;
            }
            else if (category == "game_image" && gameId.HasValue)
            {
                // EN: game_images/9962/icon.png / FR: game_images/9962/icon.png
                var gameFolder = Path.Combine(_gameImagesCacheDir, gameId.Value.ToString());
                Directory.CreateDirectory(gameFolder);
                cacheDir = gameFolder;
            }
            else if (category == "userpic" || category == "user_image")
            {
                // EN: User avatars stay flat / FR: Avatars utilisateurs restent flat
                cacheDir = _userImagesCacheDir;
            }
            else
            {
                // EN: Default fallback / FR: Fallback par défaut
                _logger.LogWarning($"[RA API] Unknown category '{category}', using user_images as fallback");
                cacheDir = _userImagesCacheDir;
            }
            
            var localPath = Path.Combine(cacheDir, fileName);

            // EN: Return if already cached / FR: Retourner si déjà en cache
            if (File.Exists(localPath))
            {
                return localPath;
            }

            try
            {
                var url = $"{MediaBaseUrl}{relativeUrl}";
                _logger.LogInformation($"[RA API] Downloading image: {fileName}");

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(localPath, imageBytes);

                return localPath;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[RA API] Failed to download image {relativeUrl}: {ex.Message}");
                return null;
            }
        }

        // ============= PUBLIC API METHODS =============

        /// <summary>
        /// EN: Get user profile information
        /// FR: Récupérer informations de profil utilisateur
        /// </summary>
        public async Task<UserProfile?> GetUserProfileAsync(string username)
        {
            var parameters = new Dictionary<string, string>
            {
                { "u", username }
            };

            var cacheKey = $"user_profile_{username}";
            var profile = await CallApiAsync<UserProfile>("API_GetUserProfile.php", parameters, cacheKey);

            // EN: Download user picture / FR: Télécharger image utilisateur
            if (profile != null && !string.IsNullOrEmpty(profile.UserPic))
            {
                var localPath = await DownloadImageAsync(profile.UserPic, "userpic");
                if (localPath != null)
                {
                    profile.UserPic = localPath;
                }
            }

            return profile;
        }

        /// <summary>
        /// EN: Get game information
        /// FR: Récupérer informations de jeu
        /// </summary>
        public async Task<GameInfo?> GetGameInfoAsync(int gameId)
        {
            var parameters = new Dictionary<string, string>
            {
                { "i", gameId.ToString() }
            };

            var cacheKey = $"game_info_{gameId}";
            var gameInfo = await CallApiAsync<GameInfo>("API_GetGame.php", parameters, cacheKey);

            // EN: Download game images / FR: Télécharger images du jeu
            if (gameInfo != null)
            {
                if (!string.IsNullOrEmpty(gameInfo.GameIcon))
                {
                    gameInfo.GameIcon = await DownloadImageAsync(gameInfo.GameIcon, "game_image", gameId) ?? gameInfo.GameIcon;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageIcon))
                {
                    gameInfo.ImageIcon = await DownloadImageAsync(gameInfo.ImageIcon, "game_image", gameId) ?? gameInfo.ImageIcon;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageTitle))
                {
                    gameInfo.ImageTitle = await DownloadImageAsync(gameInfo.ImageTitle, "game_image", gameId) ?? gameInfo.ImageTitle;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageIngame))
                {
                    gameInfo.ImageIngame = await DownloadImageAsync(gameInfo.ImageIngame, "game_image", gameId) ?? gameInfo.ImageIngame;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageBoxArt))
                {
                    gameInfo.ImageBoxArt = await DownloadImageAsync(gameInfo.ImageBoxArt, "game_image", gameId) ?? gameInfo.ImageBoxArt;
                }
            }

            return gameInfo;
        }

        /// <summary>
        /// EN: Get user progress for a specific game (ALWAYS fresh, no cache)
        /// FR: Récupérer progression utilisateur pour un jeu (TOUJOURS frais, pas de cache)
        /// </summary>
        public async Task<UserProgress?> GetUserProgressAsync(int gameId, string username)
        {
            var parameters = new Dictionary<string, string>
            {
                { "g", gameId.ToString() },
                { "u", username }
            };

            var cacheKey = $"user_progress_{gameId}_{username}";
            
            // EN: ALWAYS fetch fresh data for progress / FR: TOUJOURS récupérer données fraîches pour progression
            var progress = await CallApiAsync<UserProgress>("API_GetGameInfoAndUserProgress.php", parameters, cacheKey, useCache: false);

            // EN: Download achievement badges and set Unlocked status
            // FR: Télécharger badges des succès et définir statut Unlocked
            if (progress?.Achievements != null)
            {
                foreach (var achievement in progress.Achievements.Values)
                {
                    // EN: Determine Unlocked status based on DateEarned
                    // FR: Déterminer statut Unlocked basé sur DateEarned
                    achievement.Unlocked = achievement.DateEarned.HasValue || achievement.DateEarnedHardcore.HasValue;

                    if (!string.IsNullOrEmpty(achievement.BadgeName))
                    {
                        var badgeUrl = $"/Badge/{achievement.BadgeName}.png";
                        var localPath = await DownloadImageAsync(badgeUrl, "badge", gameId); // EN: Pass game ID / FR: Passer ID jeu
                        if (localPath != null)
                        {
                            achievement.BadgeName = localPath;
                        }
                    }
                }
            }

            // EN: Download leaderboard badges
            // FR: Télécharger badges des leaderboards
            if (progress?.Leaderboards != null)
            {
                foreach (var lb in progress.Leaderboards)
                {
                    if (!string.IsNullOrEmpty(lb.BadgeName))
                    {
                        var badgeUrl = $"/Badge/{lb.BadgeName}.png";
                        var localPath = await DownloadImageAsync(badgeUrl, "badge", gameId);
                        if (localPath != null)
                        {
                            lb.BadgeName = localPath;
                        }
                    }
                }
            }

            // EN: Download game images if embedded / FR: Télécharger images du jeu si intégrées
            if (progress?.GameInfo != null)
            {
                var gameInfo = progress.GameInfo;
                if (!string.IsNullOrEmpty(gameInfo.ImageIcon))
                {
                    gameInfo.ImageIcon = await DownloadImageAsync(gameInfo.ImageIcon, "game_image", gameId) ?? gameInfo.ImageIcon;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageTitle))
                {
                    gameInfo.ImageTitle = await DownloadImageAsync(gameInfo.ImageTitle, "game_image", gameId) ?? gameInfo.ImageTitle;
                }
                if (!string.IsNullOrEmpty(gameInfo.ImageIngame))
                {
                    gameInfo.ImageIngame = await DownloadImageAsync(gameInfo.ImageIngame, "game_image", gameId) ?? gameInfo.ImageIngame;
                }
            }

            return progress;
        }
    }
}
