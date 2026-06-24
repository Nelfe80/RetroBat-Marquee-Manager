using RetroBatMarqueeManager.Core.Models.RetroAchievements;

namespace RetroBatMarqueeManager.Application.Services
{
    public class AchievementUnlockedEventArgs : EventArgs
    {
        public Achievement Achievement { get; set; } = new();
        public int GameId { get; set; }
        public string GameTitle { get; set; } = string.Empty;
        public bool IsNewUnlock { get; set; } = true;
        public bool IsHardcore { get; set; } = false;
    }

    public class GameStartedEventArgs : EventArgs
    {
        public int GameId { get; set; }
        public GameInfo? GameInfo { get; set; }
        public UserProgress? UserProgress { get; set; }
        public bool IsHardcore { get; set; }
    }
}
