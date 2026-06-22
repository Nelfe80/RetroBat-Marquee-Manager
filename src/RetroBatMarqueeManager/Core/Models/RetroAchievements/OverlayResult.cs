namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    public class OverlayResult
    {
        public string Path { get; set; } = string.Empty;
        
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        
        /// <summary>
        /// EN: Duration in milliseconds for which this overlay should be displayed (0 = infinite/default)
        /// FR: Durée en millisecondes pour laquelle cet overlay doit être affiché (0 = infini/défaut)
        /// </summary>
        public int DurationMs { get; set; } = 0;

        public bool IsValid => !string.IsNullOrEmpty(Path);
    }
}
