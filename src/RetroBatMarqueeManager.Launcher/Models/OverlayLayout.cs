using System;
using System.Collections.Generic;

namespace RetroBatMarqueeManager.Launcher.Models
{
    public class OverlayLayout
    {
        public Dictionary<string, OverlayItem> DmdItems { get; set; } = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, OverlayItem> MpvItems { get; set; } = new Dictionary<string, OverlayItem>(StringComparer.OrdinalIgnoreCase);

        public int MarqueeWidth { get; set; }
        public int MarqueeHeight { get; set; }
        public int DmdWidth { get; set; }
        public int DmdHeight { get; set; }
    }

    public class OverlayItem
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int ZOrder { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string TextColor { get; set; } = "#FFFFD700";
        public float FontSize { get; set; } = 0;
    }
}
