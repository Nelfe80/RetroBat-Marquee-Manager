using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace RetroBatMarqueeManager.Application.Services
{
    // EN: Manages video marquee offsets with hierarchical structure (System → Game → Offsets)
    // FR: Gère les offsets de marquee vidéo avec structure hiérarchique (System → Game → Offsets)
    public class VideoOffsetStorageService
    {
        private readonly string _globalStoragePath;
        private string _individualBaseDir;
        private readonly ILogger<VideoOffsetStorageService> _logger;
        
        // EN: Hierarchical structure matching OffsetStorageService: System → Game → Offsets
        // FR: Structure hiérarchique similaire à OffsetStorageService : System →Game → Offsets
        private Dictionary<string, SystemVideoOffsetData> _globalOffsets = new();

        public VideoOffsetStorageService(ILogger<VideoOffsetStorageService> logger)
        {
            _logger = logger;
            _globalStoragePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "video_offsets.json");
            _individualBaseDir = string.Empty;
            LoadGlobalOffsets();
        }

        public void SetIndividualBaseDirectory(string baseDir)
        {
            _individualBaseDir = baseDir;
            _logger.LogInformation($"[VideoOffsets] Individual base directory set to: {baseDir}");
        }

        // EN: Get offsets for system/game (individual takes priority over global)
        // FR: Obtenir offsets pour system/game (individuel prioritaire sur global)
        public VideoOffsetData GetOffsets(string system, string game)
        {
            // Try individual file first
            var individualPath = GetIndividualOffsetPath(system, game);
            if (!string.IsNullOrEmpty(individualPath) && File.Exists(individualPath))
            {
                try
                {
                    var json = File.ReadAllText(individualPath);
                    var data = JsonSerializer.Deserialize<VideoOffsetData>(json);
                    if (data != null) return data;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[VideoOffsets] Failed to load individual for {system}/{game}: {ex.Message}");
                }
            }

            // Fallback to global
            if (_globalOffsets.TryGetValue(system, out var sysData))
            {
                if (sysData.Games.TryGetValue(game, out var gameData))
                {
                    return gameData;
                }
            }

            return new VideoOffsetData();
        }

        // EN: Get global offsets for system/game
        // FR: Obtenir offsets globaux pour system/game
        public VideoOffsetData GetGlobalOffsets(string system, string game)
        {
            if (_globalOffsets.TryGetValue(system, out var sysData))
            {
                if (sysData.Games.TryGetValue(game, out var gameData))
                {
                    return gameData;
                }
            }
            return new VideoOffsetData();
        }

        // EN: Update global offsets for system/game
        // FR: Mettre à jour offsets globaux pour system/game
        public void UpdateGlobalOffsets(string system, string game, VideoOffsetData offsets)
        {
            if (!_globalOffsets.TryGetValue(system, out var sysData))
            {
                sysData = new SystemVideoOffsetData();
                _globalOffsets[system] = sysData;
            }

            sysData.Games[game] = offsets;
            SaveGlobalOffsets();
            
            _logger.LogInformation($"[VideoOffsets] Updated global for {system}/{game}");
        }

        // EN: Get individual offsets for system/game (returns null if not found)
        // FR: Obtenir offsets individuels pour system/game (retourne null si non trouvé)
        public VideoOffsetData? GetIndividualOffsets(string system, string game)
        {
            var individualPath = GetIndividualOffsetPath(system, game);
            if (string.IsNullOrEmpty(individualPath) || !File.Exists(individualPath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(individualPath);
                return JsonSerializer.Deserialize<VideoOffsetData>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoOffsets] Failed to load individual for {system}/{game}: {ex.Message}");
                return null;
            }
        }

        public void SaveIndividualOffsets(string system, string game, VideoOffsetData offsets)
        {
            var path = GetIndividualOffsetPath(system, game);
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(offsets, options);
                File.WriteAllText(path, json);
                
                _logger.LogInformation($"[VideoOffsets] Saved individual: {path}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoOffsets] Save failed: {ex.Message}");
            }
        }

        public bool HasOffsetsChanged(string system, string game)
        {
            var globalOffsets = GetGlobalOffsets(system, game);
            var individualPath = GetIndividualOffsetPath(system, game);
            
            if (string.IsNullOrEmpty(individualPath) || !File.Exists(individualPath))
            {
                return !IsDefaultOffsets(globalOffsets);
            }

            try
            {
                var json = File.ReadAllText(individualPath);
                var individualOffsets = JsonSerializer.Deserialize<VideoOffsetData>(json);
                
                if (individualOffsets == null) return true;
                
                return !OffsetsEqual(globalOffsets, individualOffsets);
            }
            catch
            {
                return true;
            }
        }

        public void DeleteIndividualOffsets(string system, string game)
        {
            var path = GetIndividualOffsetPath(system, game);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                    _logger.LogInformation($"[VideoOffsets] Deleted: {path}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[VideoOffsets] Delete failed: {ex.Message}");
                }
            }
        }

        private string? GetIndividualOffsetPath(string system, string game)
        {
            if (string.IsNullOrEmpty(_individualBaseDir)) return null;
            
            var systemDir = Path.Combine(_individualBaseDir, system);
            return Path.Combine(systemDir, $"{game}_video_offset.json");
        }

        private void LoadGlobalOffsets()
        {
            if (!File.Exists(_globalStoragePath))
            {
                _globalOffsets = new Dictionary<string, SystemVideoOffsetData>();
                return;
            }

            try
            {
                var json = File.ReadAllText(_globalStoragePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, SystemVideoOffsetData>>(json);
                if (data != null) _globalOffsets = data;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoOffsets] Load failed: {ex.Message}");
                _globalOffsets = new Dictionary<string, SystemVideoOffsetData>();
            }
        }

        private void SaveGlobalOffsets()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_globalOffsets, options);
                File.WriteAllText(_globalStoragePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[VideoOffsets] Save failed: {ex.Message}");
            }
        }

        private bool IsDefaultOffsets(VideoOffsetData offsets)
        {
            return offsets.CropX == 0 && offsets.CropY == 0 && 
                   offsets.CropWidth == 0 && offsets.CropHeight == 0 &&
                   Math.Abs(offsets.Zoom - 1.0) < 0.01 &&
                   offsets.LogoX == 10 && offsets.LogoY == 10 &&
                   Math.Abs(offsets.LogoScale - 1.0) < 0.01 &&
                   offsets.StartTime == 0.0 && offsets.EndTime == 0.0;
        }

        private bool OffsetsEqual(VideoOffsetData a, VideoOffsetData b)
        {
            return a.CropX == b.CropX && a.CropY == b.CropY &&
                   a.CropWidth == b.CropWidth && a.CropHeight == b.CropHeight &&
                   Math.Abs(a.Zoom - b.Zoom) < 0.01 &&
                   a.LogoX == b.LogoX && a.LogoY == b.LogoY &&
                   Math.Abs(a.LogoScale - b.LogoScale) < 0.01 &&
                   Math.Abs(a.StartTime - b.StartTime) < 0.1 && 
                   Math.Abs(a.EndTime - b.EndTime) < 0.1;
        }

        public class SystemVideoOffsetData
        {
            public Dictionary<string, VideoOffsetData> Games { get; set; } = new();
        }
    }

    public class VideoOffsetData
    {
        public int CropX { get; set; } = 0;
        public int CropY { get; set; } = 0;
        public int CropWidth { get; set; } = 0;
        public int CropHeight { get; set; } = 0;
        public double Zoom { get; set; } = 1.0;
        public int LogoX { get; set; } = 10;
        public int LogoY { get; set; } = 10;
        public double LogoScale { get; set; } = 1.0;
        public double StartTime { get; set; } = 0.0;
        public double EndTime { get; set; } = 0.0;
    }
}
