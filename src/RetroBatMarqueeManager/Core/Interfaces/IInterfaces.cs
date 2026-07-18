using RetroBatMarqueeManager.Application.Services;

namespace RetroBatMarqueeManager.Core.Interfaces;

/// <summary>
/// Window placement relative to the target screen's top-left corner, in pixels.
/// Lets several target windows (marquee, iccard, topper…) share one physical
/// screen — typically a vertical display — instead of each going fullscreen.
/// </summary>
public sealed record TargetBounds(int X, int Y, int Width, int Height);

public interface IConfigService
{
    string ConfigPath { get; }
    string BaseDirectory { get; }
    string ApiExposeWebSocketBaseUrl { get; }
    string LogFilePath { get; }
    bool LogToFile { get; }
    bool MinimizeToTray { get; }

    IReadOnlyList<int> GetScreenIndices(string target);
    IReadOnlyList<string> GetTargetsForContent(string source);
    TargetBounds? GetTargetBounds(string target);

    /// <summary>Raw section/key access (used by the legacy→surfaces converter).</summary>
    string GetValue(string section, string key, string fallback = "");

    /// <summary>The dynamic surfaces (state\surfaces.json) or their legacy
    /// [Screens] equivalent — the runtime always consumes this shape.</summary>
    IReadOnlyList<Core.Surfaces.SurfaceDefinition> GetSurfaces();

    bool DmdEnabled { get; }
    string DmdModel { get; }
    string DmdExePath { get; }
    int DmdWidth { get; }
    int DmdHeight { get; }
    string ZeDmdPort { get; }
    bool DmdOptimizeZeDmd { get; }
    int DmdBrightness { get; }
    int ZeDmdUsbPackageSize { get; }
    int ZeDmdPanelMinRefreshRate { get; }
    int DmdMinimumBlockDisplayMs { get; }
    IReadOnlySet<string> ActiveSystemsDmd { get; }

    bool LayEnabled { get; }
    bool LayLcdEnabled { get; }
    bool LayDmdEnabled { get; }
    string LayDofPath { get; }

    bool RetroAchievementsEnabled { get; }
    bool RetroAchievementsMarqueeEnabled { get; }
    bool RetroAchievementsDmdEnabled { get; }
    bool RetroAchievementsPersistentEnabled { get; }
    bool RetroAchievementsNotificationsEnabled { get; }
    bool RetroAchievementsModeEnabled { get; }
    bool RetroAchievementsScoreEnabled { get; }
    bool RetroAchievementsUnlockEnabled { get; }
    bool RetroAchievementsWarningEnabled { get; }
    bool RetroAchievementsChallengeEnabled { get; }
    bool RetroAchievementsLeaderboardEnabled { get; }
    int RetroAchievementsScoreDurationMs { get; }
    int RetroAchievementsUnlockDurationMs { get; }
    int RetroAchievementsWarningDurationMs { get; }
    int RetroAchievementsLeaderboardDurationMs { get; }
    int RetroAchievementsSpeedrunUsersPerSecond { get; }
    bool RetroAchievementsBadgeTrayEnabled { get; }
    bool RetroAchievementsUnlockTakeoverEnabled { get; }
    int RetroAchievementsSpeedrunResultDurationMs { get; }

    bool LightingEnabled { get; }
    bool LightingTestPattern { get; }
    int LightingFpsLimit { get; }
    bool LightingShowFps { get; }
    double LightingRenderScale { get; }
    double LightingFillHeightMaxCrop { get; }
    bool LightingSoundEnabled { get; }
    double LightingSoundVolume { get; }
    double LightingGlassReflection { get; }
    double LightingTubeVisualOpacity { get; }
    double LightingTubeThickness { get; }
    double LightingTubeBlur { get; }
    double LightingTubeEndFade { get; }
    string LightingTubeColor { get; }
    bool LightingPreferGeneratedMarquee { get; }
    bool LightingDmdMirror { get; }

    bool LiveScoreEnabled { get; }
    bool LiveTimerEnabled { get; }
    bool LiveDataMarqueeEnabled { get; }
    bool LiveDataDmdEnabled { get; }
    int LiveScoreDurationMs { get; }
    int LiveTimerDurationMs { get; }

    string GetSetting(string key, string defaultValue = "");
}

public interface IDmdService : IDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SetBaseMediaAsync(string path, CancellationToken cancellationToken = default, string? textBackgroundPath = null);
    Task SetPersistentTextAsync(string owner, string text, int priority, CancellationToken cancellationToken = default, string? rotationGroup = null, string? detailColor = null, int rotationDurationMs = 4000, bool focusOnChange = false);
    Task ShowNotificationAsync(string owner, string title, string detail, string? badgePath, int durationMs, int priority, CancellationToken cancellationToken = default);
    void SetStatusBadges(IReadOnlyList<string> badgePaths);
    void SetLayoutFrame(byte[] pixels);
    void ClearOwner(string owner);
    void SetExternalControl(bool active);
    void Stop();
}
