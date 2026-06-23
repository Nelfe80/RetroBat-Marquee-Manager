using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Installation
{
    /// <summary>
    /// Removes legacy direct EmulationStation hooks. APIExpose owns ES events and
    /// MarqueeManager consumes the resulting WebSocket snapshots.
    /// </summary>
    public class ScriptInstallerService
    {
        private readonly IConfigService _config;
        private readonly ILogger<ScriptInstallerService> _logger;

        public ScriptInstallerService(IConfigService config, ILogger<ScriptInstallerService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public void InstallScriptsIfNeeded()
        {
            try
            {
                var esScriptsPath = Path.Combine(_config.RetroBatPath, "emulationstation", ".emulationstation", "scripts");
                
                if (!Directory.Exists(esScriptsPath))
                {
                    _logger.LogWarning($"EmulationStation scripts directory not found: {esScriptsPath}");
                    return;
                }

                var eventFolders = new[]
                {
                    "game-selected",
                    "game-start",
                    "game-end",
                    "system-selected"
                };

                foreach (var folder in eventFolders)
                {
                    var batPath = Path.Combine(
                        esScriptsPath,
                        folder,
                        "ESEventRetroBatMarqueeManager.bat");
                    if (File.Exists(batPath))
                    {
                        File.Delete(batPath);
                        _logger.LogInformation(
                            "Removed legacy direct ES hook; WebSocket snapshots are authoritative: {Path}",
                            batPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to remove legacy ES scripts: {ex.Message}");
            }
        }
    }
}
