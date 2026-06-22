namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: Challenge State for Timers and Progress Overlays
    /// FR: État de Défi pour Overlays de Timers et Progrès
    /// </summary>
    public class ChallengeState
    {
        public int AchievementId { get; set; }
        public string Title { get; set; } = "";
        public ChallengeType Type { get; set; }
        public string Progress { get; set; } = ""; // "1/10"
        public long CurrentValue { get; set; }
        public long TargetValue { get; set; }
        public string Description { get; set; } = ""; // Achievement description (EN/FR)
        public DateTime StartTime { get; set; } = DateTime.Now;
        public string? BadgePath { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public enum ChallengeType { Timer, Progress, Leaderboard }

    public class ChallengeUpdatedEventArgs : EventArgs
    {
        public ChallengeState State { get; set; } = new();
    }
}
