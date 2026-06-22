using Microsoft.Extensions.Logging;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using System.Text.Json;
using System.IO;

namespace RetroBatMarqueeManager.Application.Services
{
    /// <summary>
    /// EN: Service to manage custom overlay layouts via JSON template
    /// FR: Service pour gérer les mises en page d'overlays personnalisées via un template JSON
    /// </summary>
    public class OverlayTemplateService : IOverlayTemplateService
    {
        private readonly IConfigService _config;
        private readonly ILogger<OverlayTemplateService> _logger;
        private OverlayLayout? _layout;
        private readonly object _lock = new object();

        public OverlayTemplateService(IConfigService config, ILogger<OverlayTemplateService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public void Reload()
        {
            lock (_lock)
            {
                _layout = null;
                GetLayout();
            }
        }

        public OverlayLayout GetLayout()
        {
            lock (_lock)
            {
                if (_layout != null) return _layout;

                var path = _config.OverlayTemplatePath;
                if (!File.Exists(path))
                {
                    _logger.LogDebug($"[OverlayTemplate] Template file not found: {path}. Using engine defaults.");
                    _layout = GetDefaultLayout();
                    return _layout;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var loadedLayout = JsonSerializer.Deserialize<OverlayLayout>(json, options);
                    
                    // EN: Instantiate defaults to ensure we have a fallback for every item
                    // FR: Instancier les défauts pour s'assurer d'avoir un repli pour chaque élément
                    var defaults = GetDefaultLayout();

                    if (loadedLayout == null)
                    {
                         _layout = defaults;
                    }
                    else
                    {
                        // EN: Merge defaults into loaded layout (fill missing items)
                        // FR: Fusionner les défauts dans le layout chargé (remplir les éléments manquants)
                        MergeLayouts(loadedLayout, defaults);
                        _layout = loadedLayout;
                    }

                    // EN: Migration for Legacy Defaults (Fix DMD Centering)
            // FR: Migration pour les défauts hérités (Corriger le centrage DMD)
            var dmdW = _config.DmdWidth > 0 ? _config.DmdWidth : 128;
            var dmdH = _config.DmdHeight > 0 ? _config.DmdHeight : 32;
            bool migrated = false;

            // EN: Ensure resolution is persisted if missing
            // FR: S'assurer que la résolution est persistée si manquante
            if (_layout.DmdWidth == 0 || _layout.DmdHeight == 0 || _layout.MarqueeWidth == 0 || _layout.MarqueeHeight == 0)
            {
                _layout.DmdWidth = dmdW;
                _layout.DmdHeight = dmdH;
                _layout.MarqueeWidth = _config.MarqueeWidth > 0 ? _config.MarqueeWidth : 1920;
                _layout.MarqueeHeight = _config.MarqueeHeight > 0 ? _config.MarqueeHeight : 360;
                migrated = true;
            }

            int targetCenterX = (dmdW - 64) / 2;
            if (_layout.DmdItems.TryGetValue("count", out var countItem) && (Math.Abs(countItem.X - targetCenterX) > 2 || countItem.Width != 64))
            {
                countItem.Width = 64;
                countItem.X = targetCenterX;
                migrated = true;
                _logger.LogInformation("[OverlayTemplate] Auto-migrated 'count' to center/64w.");
            }
            if (_layout.DmdItems.TryGetValue("score", out var scoreItem) && (Math.Abs(scoreItem.X - targetCenterX) > 2 || scoreItem.Width != 64)) 
            {
                 scoreItem.Width = 64;
                 scoreItem.X = targetCenterX;
                 migrated = true;
                 _logger.LogInformation("[OverlayTemplate] Auto-migrated 'score' to center/64w.");
            }
            
            // Migration for Badge Ribbon (DMD) - If it covers more than half height, reduce to 50%
            if (_layout.DmdItems.TryGetValue("badges", out var badgesItem) && badgesItem.Height > (dmdH / 2 + 2) && dmdH > 0)
            {
                badgesItem.Height = dmdH / 2;
                badgesItem.Y = dmdH / 2;
                migrated = true;
                _logger.LogInformation("[OverlayTemplate] Auto-migrated DMD 'badges' to 50% height (detected large height).");
            }

            // Migration for Badge Ribbon (MPV) - Fix gap at bottom
            var mpvH = _config.MarqueeHeight > 0 ? _config.MarqueeHeight : 360;
            if (_layout.MpvItems.TryGetValue("badges", out var mpvBadges))
            {
                int currentBottom = mpvBadges.Y + mpvBadges.Height;
                // EN: Be more aggressive with gap detection (up to 12px)
                // FR: Être plus agressif avec la détection de l'écart (jusqu'à 12px)
                if (currentBottom < mpvH && currentBottom >= mpvH - 12)
                {
                    mpvBadges.Y = mpvH - mpvBadges.Height;
                    migrated = true;
                    _logger.LogInformation("[OverlayTemplate] Auto-migrated MPV 'badges' to be flush with bottom.");
                }
            }

            if (migrated)
            {
                _logger.LogInformation("[OverlayTemplate] Persisting auto-migrated layout to disk...");
                SaveLayout(_layout);
            }
                    

                    _logger.LogInformation($"[OverlayTemplate] Layout loaded from {path}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[OverlayTemplate] Load failed: {ex.Message}");
                    _layout = GetDefaultLayout();
                }

                return _layout;
            }
        }

        private void MergeLayouts(OverlayLayout target, OverlayLayout source)
        {
            // Merge DMD Items
            if (target.DmdItems == null) target.DmdItems = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);
            
            // source.DmdItems shouldn't be null coming from GetDefaultLayout()
            if (source.DmdItems != null)
            {
                foreach (var kvp in source.DmdItems)
                {
                    // If target is missing specific item, add default
                    if (!target.DmdItems.ContainsKey(kvp.Key))
                    {
                        target.DmdItems[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Merge MPV Items
            if (target.MpvItems == null) target.MpvItems = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);

            if (source.MpvItems != null)
            {
                foreach (var kvp in source.MpvItems)
                {
                    if (!target.MpvItems.ContainsKey(kvp.Key))
                    {
                        target.MpvItems[kvp.Key] = kvp.Value;
                    }
                }
            }
        }



        public OverlayLayout GetDefaultLayout()
        {
            var layout = new OverlayLayout
            {
                MarqueeWidth = _config.MarqueeWidth,
                MarqueeHeight = _config.MarqueeHeight,
                DmdWidth = _config.DmdWidth,
                DmdHeight = _config.DmdHeight
            };

            layout.DmdItems = GetDefaultItems(true, layout.DmdWidth, layout.DmdHeight);
            layout.MpvItems = GetDefaultItems(false, layout.MarqueeWidth, layout.MarqueeHeight);

            return layout;
        }

        private Dictionary<string, OverlayItem> GetDefaultItems(bool isDmd, int w, int h)
        {
            var dict = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);
            if (isDmd)
            {
                // EN: Standard DMD Layout (128x32 logic) / FR: Layout DMD Standard
                if (w <= 0) w = 128;
                if (h <= 0) h = 32;

                // EN: Centered ~45% width/height (Original Default - Tight Fit)
                // FR: Centré ~45% largeur/hauteur (Défaut Original - Ajusté)
                int itemW = 64; // EN: Restored to 64 for tight fit / FR: Rétabli à 64 pour ajustement précis
                int itemH = (int)(h * 0.45);
                int itemX = (w - itemW) / 2;
                int itemY = (h - itemH) / 2;

                dict["count"] = new OverlayItem { X = itemX, Y = itemY, Width = itemW, Height = itemH, ZOrder = 1 };
                dict["score"] = new OverlayItem { X = itemX, Y = itemY, Width = itemW, Height = itemH, ZOrder = 2 };
                
                dict["badges"] = new OverlayItem { X = 0, Y = h / 2, Width = w, Height = h / 2, ZOrder = 3 };
                
                dict["unlock"] = new OverlayItem { X = 0, Y = 0, Width = w, Height = h, ZOrder = 4 };
                dict["challenge"] = new OverlayItem { X = 0, Y = 0, Width = w, Height = h, ZOrder = 4 };
                
                // DMD RP Items - Keep as is (custom layouts usually) or adjust if needed.
                dict["rp_score"] = new OverlayItem { X = (int)(w * 0.62), Y = (int)(h * 0.03), Width = (int)(w * 0.36), Height = (int)(h * 0.31), ZOrder = 5 }; // Top Right
                dict["rp_lives"] = new OverlayItem { X = (int)(w * 0.015), Y = (int)(h * 0.03), Width = (int)(w * 0.31), Height = (int)(h * 0.31), ZOrder = 5 }; // Top Left
                dict["rp_narration"] = new OverlayItem { X = 0, Y = (int)(h * 0.68), Width = w, Height = (int)(h * 0.31), ZOrder = 5 }; // Bottom
                dict["rp_stat"] = new OverlayItem { X = 0, Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5 }; // Middle Left
                dict["rp_weapon"] = new OverlayItem { X = (int)(w * 0.5), Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5 }; // Middle Right
                
                // New Racing Defaults (Share slots with Score/Lives)
                dict["rp_lap"] = new OverlayItem { X = (int)(w * 0.015), Y = (int)(h * 0.03), Width = (int)(w * 0.31), Height = (int)(h * 0.31), ZOrder = 5 }; // Top Left (Same as Lives)
                dict["rp_rank"] = new OverlayItem { X = (int)(w * 0.62), Y = (int)(h * 0.03), Width = (int)(w * 0.36), Height = (int)(h * 0.31), ZOrder = 5 }; // Top Right (Same as Score)

                // New Crystal/Gem Defaults
                dict["rp_crystal"] = new OverlayItem { X = 0, Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5 }; // Middle Left (Same as Stat)
                dict["rp_gem"] = new OverlayItem { X = (int)(w * 0.5), Y = (int)(h * 0.34), Width = (int)(w * 0.5), Height = (int)(h * 0.31), ZOrder = 5 }; // Middle Right (Same as Weapon)
            }
            else
            {
                // EN: MPV Adaptive Layout / FR: Layout MPV Adaptatif
                if (w <= 0) w = 1920;
                if (h <= 0) h = 360;

                // Unlock (Achievements): 50% width
                int unlockW = (int)(w * 0.5);
                int unlockH = (int)(h * ((double)w / h > 3.0 ? 0.6 : 0.4));
                dict["unlock"] = new OverlayItem { X = (w - unlockW) / 2, Y = (h - unlockH) / 2, Width = unlockW, Height = unlockH, ZOrder = 5 };
                
                // Badges (Ribbon): Bottom 1/4.5 of height (~22% for visibility)
                int badgeH = (int)(h / 4.5);
                dict["badges"] = new OverlayItem { X = 0, Y = h - badgeH, Width = w, Height = badgeH, ZOrder = 1 };
                
                // Achievement Indicators (Count & Score) - Matched to Hardcoded Defaults
                dict["count"] = new OverlayItem { X = 20, Y = 20, Width = 200, Height = 60, ZOrder = 2 };
                dict["score"] = new OverlayItem { X = w - 220, Y = 20, Width = 200, Height = 60, ZOrder = 3 };

                // Challenge
                dict["challenge"] = new OverlayItem { X = 20, Y = (h - 120) / 2, Width = 350, Height = 120, ZOrder = 4 };

                // Rich Presence (Safe Defaults - No Overlap with RA Count/Score)
                
                // Score: Top-Right (Below RA Score at Y=20..80) -> Y=100
                dict["rp_score"] = new OverlayItem { X = w - 420, Y = 100, Width = 400, Height = 80, ZOrder = 5 };
                
                // Lives: Top-Right (Below Score) -> Y=190
                dict["rp_lives"] = new OverlayItem { X = w - 420, Y = 190, Width = 400, Height = 80, ZOrder = 5 };
                
                // Stat: Top-Left (Below Count) -> Y=100
                dict["rp_stat"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5 };
                
                // Weapon: Top-Left (Below Stat) -> Y=190
                dict["rp_weapon"] = new OverlayItem { X = 20, Y = 190, Width = 400, Height = 80, ZOrder = 5 };
                
                // Narration: Center-Center (50% Width)
                int narrationW = (int)(w * 0.50);
                int narrationH = 100;
                dict["rp_narration"] = new OverlayItem { X = (w - narrationW) / 2, Y = (h - narrationH) / 2, Width = narrationW, Height = narrationH, ZOrder = 6 };

                // New Racing Defaults
                dict["rp_lap"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5 }; // Top-Left (Same as Stat)
                dict["rp_rank"] = new OverlayItem { X = w - 420, Y = 100, Width = 400, Height = 80, ZOrder = 5 }; // Top-Right (Same as Score)
                
                // New Crystal/Gem Defaults
                dict["rp_crystal"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5 }; // Top-Left (Same as Stat)
                dict["rp_gem"] = new OverlayItem { X = 20, Y = 100, Width = 400, Height = 80, ZOrder = 5 }; // Top-Left (Same as Stat)
            }
            return dict;
        }

        public void SaveLayout(OverlayLayout layout)
        {
            lock (_lock)
            {
                _layout = layout;
                var path = _config.OverlayTemplatePath;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(layout, options);
                    File.WriteAllText(path, json);
                    _logger.LogInformation($"[OverlayTemplate] Layout saved to {path}");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[OverlayTemplate] Save failed: {ex.Message}");
                }
            }
        }

        public OverlayItem? GetItem(string screenType, string overlayType)
        {
            var layout = GetLayout();
            if (screenType.Equals("dmd", StringComparison.OrdinalIgnoreCase))
            {
                return layout.DmdItems.TryGetValue(overlayType, out var item) ? item : null;
            }
            if (screenType.Equals("mpv", StringComparison.OrdinalIgnoreCase))
            {
                return layout.MpvItems.TryGetValue(overlayType, out var item) ? item : null;
            }
            return null;
        }
    }
}
