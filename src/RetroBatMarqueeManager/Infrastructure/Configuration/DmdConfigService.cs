using RetroBatMarqueeManager.Core.Interfaces;
using System.Text;

namespace RetroBatMarqueeManager.Infrastructure.Configuration
{
    public class DmdConfigService : IDmdConfigService
    {
        private readonly string _configPath;
        private readonly ILogger<DmdConfigService> _logger;
        
        public string Port { get; set; } = "COM3";
        public int BaudRate { get; set; } = 921600;
        public bool IsEnabled => true; // Dependent on main config 'ActiveDMD' usually, but this config manages the hardware settings.

        public DmdConfigService(ILogger<DmdConfigService> logger)
        {
            _logger = logger;
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dmd", "config.dmd");
            Load();
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Save(); // Create default
                    return;
                }

                var lines = File.ReadAllLines(_configPath);
                foreach (var line in lines)
                {
                    if (line.StartsWith("port=")) Port = line.Split('=')[1].Trim();
                    if (line.StartsWith("baudrate="))
                    {
                        if (int.TryParse(line.Split('=')[1].Trim(), out var b) && b > 0)
                        {
                            BaudRate = b;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load DMD config: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("[CONNECTION]");
                sb.AppendLine($"port={Port}");
                sb.AppendLine($"baudrate={BaudRate}");
                
                File.WriteAllText(_configPath, sb.ToString());
                _logger.LogInformation($"Saved DMD config to {_configPath}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to save DMD config: {ex.Message}");
            }
        }
    }
}
