using System;
using System.Collections.Generic;

namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: Represents custom layout for RetroAchievements overlays
    /// FR: Représente une mise en page personnalisée pour les overlays RetroAchievements
    /// </summary>
    public class OverlayLayout
    {
        public Dictionary<string, OverlayItem> DmdItems { get; set; } = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OverlayItem> MpvItems { get; set; } = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);

        // EN: Stored resolutions for scaling detection / FR: Résolutions stockées pour la détection du scaling
        public int MarqueeWidth { get; set; }
        public int MarqueeHeight { get; set; }
        public int DmdWidth { get; set; }
        public int DmdHeight { get; set; }
    }

    /// <summary>
    /// EN: Represents an individual overlay item position and size
    /// FR: Représente la position et la taille d'un élément d'overlay individuel
    /// </summary>
    public class OverlayItem
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZOrder { get; set; } // EN: Display order / FR: Ordre d'affichage
        public bool IsEnabled { get; set; } = true;
        public string TextColor { get; set; } = "#FFFFD700"; // EN: Hex ARGB Gold / FR: Hex ARGB Or
        public float FontSize { get; set; } = 0; // EN: Font Size (0=Auto) / FR: Taille Police (0=Auto)
    }
}
