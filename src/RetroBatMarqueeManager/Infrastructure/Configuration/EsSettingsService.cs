using System.Xml.Linq;

namespace RetroBatMarqueeManager.Infrastructure.Configuration
{
    public interface IEsSettingsService
    {
        string? GetSetting(string key);
        bool GetBool(string key, bool defaultValue = false);
        string GetLanguageCode();
    }

    public class EsSettingsService : IEsSettingsService
    {
        private readonly string _settingsPath;
        private readonly ILogger<EsSettingsService> _logger;
        private Dictionary<string, string> _settings = new Dictionary<string, string>();

        public EsSettingsService(string retroBatPath, ILogger<EsSettingsService> logger)
        {
            _logger = logger;
            // Common path: emulationstation/.emulationstation/es_settings.cfg
            // config.ini provided RetroBatPath usually points to root C:\RetroBat
            _settingsPath = Path.Combine(retroBatPath, "emulationstation", ".emulationstation", "es_settings.cfg");
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (!File.Exists(_settingsPath))
            {
                _logger.LogWarning($"ES Settings file not found at: {_settingsPath}");
                return;
            }

            try
            {
                using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var doc = XDocument.Load(stream);
                var root = doc.Root;
                if (root == null) return;

                foreach (var element in root.Elements())
                {
                    var name = element.Attribute("name")?.Value;
                    var value = element.Attribute("value")?.Value;

                    if (!string.IsNullOrEmpty(name) && value != null)
                    {
                        _settings[name] = value;
                    }
                }
                
                _logger.LogInformation($"Loaded {_settings.Count} settings from es_settings.cfg");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error loading ES settings: {ex.Message}");
            }
        }

        public string? GetSetting(string key)
        {
            return _settings.TryGetValue(key, out var val) ? val : null;
        }

        public bool GetBool(string key, bool defaultValue = false)
        {
             if (_settings.TryGetValue(key, out var val))
             {
                 return val.Equals("true", StringComparison.OrdinalIgnoreCase) || val.Equals("1");
             }
             return defaultValue;
        }

        public string GetLanguageCode()
        {
            var lang = GetSetting("Language"); // e.g. "fr_FR"
            if (string.IsNullOrEmpty(lang)) return "en";
            var parts = lang.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0].ToLowerInvariant() : "en";
        }
    }
}
