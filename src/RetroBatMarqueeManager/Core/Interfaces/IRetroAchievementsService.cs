using RetroBatMarqueeManager.Core.Models.RetroAchievements;
using RetroBatMarqueeManager.Application.Services;

namespace RetroBatMarqueeManager.Core.Interfaces
{
    public interface IRetroAchievementsService : IRaBadgeProvider
    {
        event EventHandler<AchievementUnlockedEventArgs>? AchievementUnlocked;
        event EventHandler<GameStartedEventArgs>? GameStarted;
        event EventHandler<bool>? HardcoreStatusChanged;
        event EventHandler<string>? RichPresenceUpdated;
        event EventHandler<ChallengeUpdatedEventArgs>? ChallengeUpdated;
        event EventHandler? AchievementDetected;

        int CurrentGameUserPoints { get; }
        int CurrentGameTotalPoints { get; }
        bool IsHardcoreMode { get; }
        int? CurrentGameId { get; }
        Dictionary<string, Achievement>? CurrentGameAchievements { get; }

        void ResetState();
        void SetImageConversionService(ImageConversionService imageService);
    }
}
