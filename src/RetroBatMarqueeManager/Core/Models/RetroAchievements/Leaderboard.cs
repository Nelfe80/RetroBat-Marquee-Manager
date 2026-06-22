namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: Data for a leaderboard
    /// FR: Données pour un leaderboard
    /// </summary>
    public class Leaderboard
    {
        public int ID { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int LowerIsBetter { get; set; }
        public string BadgeName { get; set; } = string.Empty;
    }
}
