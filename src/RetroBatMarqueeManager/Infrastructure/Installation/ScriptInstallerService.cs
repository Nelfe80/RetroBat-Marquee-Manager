using RetroBatMarqueeManager.Core.Interfaces;

namespace RetroBatMarqueeManager.Infrastructure.Installation
{
    /// <summary>
    /// EN: Service to auto-install EmulationStation script hooks on first launch
    /// FR: Service pour installer automatiquement les scripts EmulationStation au premier lancement
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

                // Define script types and their target directories
                var scriptDef = new[]
                {
                    ("game-selected", "-game-selected"),
                    ("game-start", "-game-start"),
                    ("game-end", "-game-end"),
                    ("system-selected", "-system-selected")
                };

                foreach (var (folder, cmdArg) in scriptDef)
                {
                    var targetDir = Path.Combine(esScriptsPath, folder);
                    Directory.CreateDirectory(targetDir); // Ensure directory exists

                    var batPath = Path.Combine(targetDir, "ESEventRetroBatMarqueeManager.bat");

                    // Only create if it doesn't exist (non-destructive)
                    var batContent = GenerateBatScript(cmdArg);
                    
                    // Check if update is needed
                    bool needsUpdate = true;
                    if (File.Exists(batPath))
                    {
                        var existingContent = File.ReadAllText(batPath);
                        if (existingContent == batContent) needsUpdate = false;
                    }

                    if (needsUpdate)
                    {
                        File.WriteAllText(batPath, batContent);
                        _logger.LogInformation($"Updated/Created script: {batPath}");
                    }
                    else
                    {
                        _logger.LogInformation($"Script already exists: {batPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to install scripts: {ex.Message}");
            }
        }

        private string GenerateBatScript(string commandArg)
        {
            // Path relative to script location: scripts\{event}\ESEventRetroBatMarqueeManager.bat
            // To RetroBat root: ..\..\..\..\
            // Then to plugin: plugins\RetroBatMarqueeManager\RetroBatMarqueeManager.exe
            return "@echo off\r\n" +
                   "chcp 65001 > nul\r\n" +
                   ":: Direct App Entry Point (No Launcher Overhead)\r\n" +
                   $"\"%~dp0..\\..\\..\\..\\plugins\\RetroBatMarqueeManager\\RetroBatMarqueeManager.App.exe\" {commandArg} %*\r\n";
        }
    }
}
