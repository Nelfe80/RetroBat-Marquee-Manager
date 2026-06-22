using System.Xml.Linq;

namespace RetroBatMarqueeManager.Infrastructure.Configuration
{
    /// <summary>
    /// Service to parse EmulationStation settings (es_settings.cfg XML file)
    /// FR: Service pour parser les paramètres d'EmulationStation (fichier XML es_settings.cfg)
    /// </summary>
    public class EsSettingsParser
    {
        private readonly ILogger<EsSettingsParser> _logger;
        private string? _language;
        private string? _themeSet;
        private bool _isParsed = false;

        public EsSettingsParser(ILogger<EsSettingsParser> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Parse the es_settings.cfg file to extract Language and ThemeSet
        /// FR: Parse le fichier es_settings.cfg pour extraire Language et ThemeSet
        /// </summary>
        public async Task<bool> ParseAsync(string esSettingsPath)
        {
            try
            {
                if (!File.Exists(esSettingsPath))
                {
                    _logger.LogWarning($"es_settings.cfg not found at {esSettingsPath}");
                    return false;
                }

                var doc = await Task.Run(() => XDocument.Load(esSettingsPath));
                var config = doc.Element("config");
                if (config == null)
                {
                    _logger.LogError("Invalid es_settings.cfg format: <config> root element not found");
                    return false;
                }

                // Extract Language (ex: "fr_FR")
                var languageElement = config.Elements("string")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == "Language");
                _language = languageElement?.Attribute("value")?.Value ?? "en_US";

                // Extract ThemeSet (ex: "es-theme-carbon")
                var themeElement = config.Elements("string")
                    .FirstOrDefault(e => e.Attribute("name")?.Value == "ThemeSet");
                _themeSet = themeElement?.Attribute("value")?.Value ?? "es-theme-carbon";

                _logger.LogInformation($"Parsed es_settings.cfg: Language={_language}, ThemeSet={_themeSet}");
                _isParsed = true;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse es_settings.cfg: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get the language code (ex: "fr_FR" -> "fr")
        /// FR: Obtenir le code de langue (ex: "fr_FR" -> "fr")
        /// </summary>
        public string GetLanguageCode()
        {
            if (!_isParsed || string.IsNullOrEmpty(_language))
            {
                return "en";
            }

            // Extract language code from format "fr_FR" -> "fr"
            var parts = _language.Split('_');
            return parts[0].ToLowerInvariant();
        }

        /// <summary>
        /// Get the full language string (ex: "fr_FR")
        /// FR: Obtenir la langue complète (ex: "fr_FR")
        /// </summary>
        public string GetLanguage() => _language ?? "en_US";

        /// <summary>
        /// Get the theme set name (ex: "es-theme-carbon")
        /// FR: Obtenir le nom du thème (ex: "es-theme-carbon")
        /// </summary>
        public string GetThemeSet() => _themeSet ?? "es-theme-carbon";

        /// <summary>
        /// Build theme paths with fallbacks (Carbon -> Carbon-master -> ThemeSet)
        /// FR: Construire les chemins de thème avec fallbacks (Carbon -> Carbon-master -> ThemeSet)
        /// </summary>
        public List<string> GetThemePaths(string themesBasePath)
        {
            var paths = new List<string>();

            // Priority 1: es-theme-carbon (nouveau)
            paths.Add(Path.Combine(themesBasePath, "es-theme-carbon"));

            // Priority 2: es-theme-carbon-master (ancien)
            paths.Add(Path.Combine(themesBasePath, "es-theme-carbon-master"));

            // Priority 3: Theme from es_settings.cfg (if different)
            if (_themeSet != null && 
                _themeSet != "es-theme-carbon" && 
                _themeSet != "es-theme-carbon-master")
            {
                paths.Add(Path.Combine(themesBasePath, _themeSet));
            }

            return paths;
        }
    }
}
