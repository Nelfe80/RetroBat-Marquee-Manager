using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace RetroBatMarqueeManager.Launcher.Helpers
{
    /// <summary>
    /// EN: Manages multi-language translation system
    /// FR: Gère le système de traduction multilingue
    /// </summary>
    public class TranslationManager
    {
        private Dictionary<string, string> _translations;
        private readonly string _languagesFolder;
        private string _currentLanguage;

        public TranslationManager(string languagesFolder)
        {
            _languagesFolder = languagesFolder;
            _translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _currentLanguage = "en"; // Default language
        }

        /// <summary>
        /// EN: Load language file (format: KEY=Translation)
        /// FR: Charge un fichier de langue (format: CLE=Traduction)
        /// </summary>
        public bool LoadLanguage(string languageCode)
        {
            var langFile = Path.Combine(_languagesFolder, $"{languageCode}.lang");
            
            if (!File.Exists(langFile))
            {
                // Fallback to English
                langFile = Path.Combine(_languagesFolder, "en.lang");
                if (!File.Exists(langFile))
                {
                    return false;
                }
            }

            try
            {
                _translations.Clear();
                
                foreach (var line in File.ReadAllLines(langFile))
                {
                    var trimmed = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                        continue;

                    var equalIndex = trimmed.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        var key = trimmed.Substring(0, equalIndex).Trim();
                        var value = trimmed.Substring(equalIndex + 1).Trim();
                        _translations[key] = value;
                    }
                }

                _currentLanguage = languageCode;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// EN: Get translation for a key, with fallback to key itself
        /// FR: Obtient la traduction d'une clé, avec repli sur la clé elle-même
        /// </summary>
        public string Translate(string key)
        {
            if (_translations.TryGetValue(key, out var translation))
            {
                return translation;
            }
            
            // Fallback: return the key itself
            return key;
        }

        /// <summary>
        /// EN: Apply translations to all controls in a form recursively
        /// FR: Applique les traductions à tous les contrôles d'un formulaire récursivement
        /// </summary>
        public void ApplyTranslations(Control control)
        {
            // Translate the control's Text property if it has a translation key
            // Convention: control.Name should be the translation key
            if (!string.IsNullOrEmpty(control.Name))
            {
                var translationKey = control.Name;
                
                // Check if translation exists
                if (_translations.ContainsKey(translationKey))
                {
                    control.Text = Translate(translationKey);
                }
            }

            // Recursively apply to child controls
            foreach (Control child in control.Controls)
            {
                ApplyTranslations(child);
            }

            // Handle MenuStrip separately
            if (control is MenuStrip menuStrip)
            {
                foreach (ToolStripMenuItem item in menuStrip.Items)
                {
                    ApplyTranslationsToMenuItem(item);
                }
            }
        }

        /// <summary>
        /// EN: Apply translations to menu items
        /// FR: Applique les traductions aux éléments de menu
        /// </summary>
        private void ApplyTranslationsToMenuItem(ToolStripMenuItem item)
        {
            if (!string.IsNullOrEmpty(item.Name))
            {
                if (_translations.ContainsKey(item.Name))
                {
                    item.Text = Translate(item.Name);
                }
            }

            // Recursively apply to dropdown items
            foreach (ToolStripItem dropdownItem in item.DropDownItems)
            {
                if (dropdownItem is ToolStripMenuItem menuItem)
                {
                    ApplyTranslationsToMenuItem(menuItem);
                }
            }
        }

        /// <summary>
        /// EN: Get list of available languages
        /// FR: Obtient la liste des langues disponibles
        /// </summary>
        public List<string> GetAvailableLanguages()
        {
            var languages = new List<string>();
            
            if (Directory.Exists(_languagesFolder))
            {
                var langFiles = Directory.GetFiles(_languagesFolder, "*.lang");
                foreach (var file in langFiles)
                {
                    var langCode = Path.GetFileNameWithoutExtension(file);
                    languages.Add(langCode);
                }
            }

            return languages;
        }

        public string CurrentLanguage => _currentLanguage;
    }
}
