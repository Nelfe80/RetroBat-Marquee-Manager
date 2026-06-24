namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IRaBadgeProvider
    {
        Task<string?> GetBadgePath(int gameId, int achievementId);
        Task<string?> GetBadgeLockPath(int gameId, int achievementId);
    }
}
