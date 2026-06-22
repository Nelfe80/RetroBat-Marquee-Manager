using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RetroBatMarqueeManager.Launcher.Helpers
{
    /// <summary>
    /// EN: Manages loading and saving of config.ini file
    /// FR: Gère le chargement et la sauvegarde du fichier config.ini
    /// </summary>
    public class ConfigManager
    {
        private readonly string _configPath;

        public ConfigManager(string configPath)
        {
            _configPath = configPath;
        }

        /// <summary>
        /// EN: Load config.ini into a dictionary of sections and key-value pairs
        /// FR: Charge config.ini dans un dictionnaire de sections et paires clé-valeur
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> LoadConfig()
        {
            var config = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            
            if (!File.Exists(_configPath))
            {
                return new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            string currentSection = "Settings"; // Default section
            config[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var line in File.ReadAllLines(_configPath))
            {
                var trimmed = line.Trim();
                
                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                // Section header
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Substring(1, trimmed.Length - 2);
                    if (!config.ContainsKey(currentSection))
                    {
                        config[currentSection] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                    continue;
                }

                // Key=Value pair
                var equalIndex = trimmed.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = trimmed.Substring(0, equalIndex).Trim();
                    var value = trimmed.Substring(equalIndex + 1).Trim();
                    config[currentSection][key] = value;
                }
            }

            return config;
        }

        /// <summary>
        /// EN: Save configuration dictionary to config.ini (preserving comments and structure)
        /// FR: Sauvegarde le dictionnaire de configuration dans config.ini (en préservant commentaires et structure)
        /// </summary>
        public void SaveConfig(Dictionary<string, Dictionary<string, string>> config)
        {
            var lines = new List<string>();
            
            // EN: If file exists, read it line by line and update values while preserving comments
            // FR: Si le fichier existe, le lire ligne par ligne et mettre à jour les valeurs en préservant les commentaires
            if (File.Exists(_configPath))
            {
                var originalLines = File.ReadAllLines(_configPath);
                string currentSection = "Settings";
                
                foreach (var line in originalLines)
                {
                    var trimmed = line.Trim();
                    
                    // Preserve empty lines and comments
                    if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    {
                        lines.Add(line);
                        continue;
                    }
                    
                    // Section header
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        currentSection = trimmed.Substring(1, trimmed.Length - 2);
                        lines.Add(line);
                        continue;
                    }
                    
                    // Key=Value pair
                    var equalIndex = trimmed.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        var key = trimmed.Substring(0, equalIndex).Trim();
                        
                        // Check if this key exists in our config dictionary
                        if (config.ContainsKey(currentSection) && config[currentSection].ContainsKey(key))
                        {
                            // Update the value while preserving indentation
                            var indent = line.TakeWhile(char.IsWhiteSpace).Count();
                            var indentStr = new string(' ', indent);
                            lines.Add($"{indentStr}{key}={config[currentSection][key]}");
                            
                            // Mark this key as written
                            config[currentSection].Remove(key);
                        }
                        else
                        {
                            // Keep original line if key not in our config (commented out key, old key, etc.)
                            lines.Add(line);
                        }
                    }
                    else
                    {
                        // Unknown line format - preserve it
                        lines.Add(line);
                    }
                }
                
                // EN: Append any new keys that weren't in the original file
                // FR: Ajouter les nouvelles clés qui n'étaient pas dans le fichier original
                
                // EN: Build list of remaining keys by section / FR: Liste des clés restantes par section
                var remainingKeys = new Dictionary<string, List<KeyValuePair<string, string>>>();
                foreach (var section in config)
                {
                    if (section.Value.Count > 0)
                    {
                        remainingKeys[section.Key] = section.Value.ToList();
                    }
                }
                
                // EN: Insert remaining keys into their sections / FR: Insérer clés restantes dans leurs sections
                if (remainingKeys.Count > 0)
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        var line = lines[i];
                        var trimmed = line.Trim();
                        
                        // Find section headers
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            var sectionName = trimmed.Substring(1, trimmed.Length - 2);
                            
                            if (remainingKeys.ContainsKey(sectionName) && remainingKeys[sectionName].Count > 0)
                            {
                                // Find end of this section (next section or end of file)
                                int insertIndex = i + 1;
                                while (insertIndex < lines.Count)
                                {
                                    var nextLine = lines[insertIndex].Trim();
                                    if (nextLine.StartsWith("[") && nextLine.EndsWith("]"))
                                    {
                                        // Found next section, insert before it
                                        break;
                                    }
                                    insertIndex++;
                                }
                                
                                // Insert remaining keys for this section
                                foreach (var kvp in remainingKeys[sectionName])
                                {
                                    // Skip empty values to avoid pollution
                                    if (!string.IsNullOrWhiteSpace(kvp.Value))
                                    {
                                        lines.Insert(insertIndex, $"{kvp.Key}={kvp.Value}");
                                        insertIndex++;
                                    }
                                }
                                
                                // Mark as processed
                                remainingKeys[sectionName].Clear();
                            }
                        }
                    }
                    
                    // EN: Add sections that don't exist in original file / FR: Ajouter sections inexistantes
                    foreach (var section in remainingKeys)
                    {
                        if (section.Value.Count > 0)
                        {
                            lines.Add("");
                            lines.Add($"[{section.Key}]");
                            foreach (var kvp in section.Value)
                            {
                                if (!string.IsNullOrWhiteSpace(kvp.Value))
                                {
                                    lines.Add($"{kvp.Key}={kvp.Value}");
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                // EN: File doesn't exist - create from scratch
                // FR: Fichier n'existe pas - créer depuis zéro
                foreach (var section in config)
                {
                    lines.Add($"[{section.Key}]");
                    
                    foreach (var kvp in section.Value)
                    {
                        lines.Add($"{kvp.Key}={kvp.Value}");
                    }
                    
                    lines.Add(""); // Empty line between sections
                }
            }
            
            // Backup existing config
            if (File.Exists(_configPath))
            {
                var backupPath = _configPath + ".bak";
                File.Copy(_configPath, backupPath, overwrite: true);
            }

            File.WriteAllLines(_configPath, lines, Encoding.UTF8);
        }

        /// <summary>
        /// EN: Wait for config.ini to be created by the main app
        /// FR: Attend que config.ini soit créé par l'application principale
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <returns>True if config was created, false if timeout</returns>
        public bool WaitForConfigCreation(int timeoutMs = 10000)
        {
            var startTime = DateTime.Now;
            
            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (File.Exists(_configPath))
                {
                    // Wait a bit more to ensure file is fully written
                    Thread.Sleep(500);
                    return true;
                }
                
                Thread.Sleep(100);
            }

            return false;
        }

        /// <summary>
        /// EN: Get a value from a specific section and key
        /// FR: Obtient une valeur d'une section et clé spécifiques
        /// </summary>
        public string GetValue(Dictionary<string, Dictionary<string, string>> config, string section, string key, string defaultValue = "")
        {
            if (config.TryGetValue(section, out var sectionDict))
            {
                if (sectionDict.TryGetValue(key, out var value))
                {
                    return value;
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// EN: Get all key-value pairs from a specific section
        /// FR: Obtient toutes les paires clé-valeur d'une section spécifique
        /// </summary>
        public Dictionary<string, string> GetSection(Dictionary<string, Dictionary<string, string>> config, string section)
        {
            if (config.TryGetValue(section, out var sectionDict))
            {
                return sectionDict;
            }
            return null;
        }

        /// <summary>
        /// EN: Set a value in a specific section and key
        /// FR: Définit une valeur dans une section et clé spécifiques
        /// </summary>
        public void SetValue(Dictionary<string, Dictionary<string, string>> config, string section, string key, string value)
        {
            if (!config.ContainsKey(section))
            {
                config[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            
            config[section][key] = value;
        }
    }
}
