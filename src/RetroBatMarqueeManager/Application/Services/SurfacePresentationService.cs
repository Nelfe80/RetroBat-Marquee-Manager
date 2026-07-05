using System.Globalization;
using System.Text.Json;
using RetroBatMarqueeManager.Core.Interfaces;
using RetroBatMarqueeManager.Infrastructure.Processes;

namespace RetroBatMarqueeManager.Application.Services;

public sealed class SurfacePresentationService
{
    private readonly IConfigService _config;
    private readonly MarqueeController _surfaces;
    private readonly IDmdService _dmd;
    private readonly ILogger<SurfacePresentationService> _logger;
    private readonly object _dedupeLock = new();
    private readonly Queue<string> _dedupeOrder = new();
    private readonly HashSet<string> _dedupe = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeRaBadges = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, LeaderboardCatalogEntry> _leaderboards = new();
    private readonly HashSet<string> _timerDmdOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _surfaceOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dmdOwners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _leaderboardScrollLock = new();
    private readonly Dictionary<int, List<LeaderboardReference>> _leaderboardReferences = new();
    private readonly HashSet<int> _activeSpeedrunLeaderboardIds = new();
    private readonly Dictionary<int, DateTime> _speedrunResultAtUtc = new();
    // Cross-path guard: a speedrun result can arrive both as an id-carrying event (routed
    // to the dedicated handler) and as an id-less terminal event (legacy path). Track when
    // any speedrun result card was last shown so the second one within the window is dropped.
    private DateTime _lastSpeedrunResultCardAtUtc = DateTime.MinValue;

    // Achievement badge tray
    private readonly Dictionary<int, string?> _achievementBadgeCache = new();
    private readonly HashSet<int> _unlockedAchievementIds = new();
    private CancellationTokenSource? _badgeTrayDebounce;

    private CancellationTokenSource? _leaderboardScrollCancellation;
    private int? _activeLeaderboardId;
    private bool _activeLeaderboardIsSpeedrun;
    private bool _activeLeaderboardIsLevelScoped;
    private string _activeLeaderboardValue = string.Empty;
    private string _activeLeaderboardTitle = "SPEEDRUN";
    private string _activeLeaderboardUser = string.Empty;
    private string? _activeLeaderboardBadgePath;
    private double _activeLeaderboardClockBaseSeconds;
    private DateTime _activeLeaderboardClockSyncedAt = DateTime.MinValue;
    private bool _activeLeaderboardClockExternallySynced;
    private bool _activeLeaderboardHasTimerSample;
    private bool _activeLeaderboardClockPaused;
    private double? _lastLevelTimerSeconds;
    // Debounce for attempt restarts: the composed level clock can dip toward 0 for a
    // single sample during a minute/second rollover. A real reset stays low on the next
    // sample; a glitch recovers. Only restart once the drop is confirmed.
    private bool _levelResetPending;
    private DateTime _lastLevelTimerSampleAtUtc = DateTime.MinValue;
    private DateTime _suppressUnlocksUntilUtc = DateTime.MinValue;
    private CancellationTokenSource? _speedrunRecapCancellation;
    private double? _activeSpeedrunRecordSeconds;
    private double? _activeSpeedrunUserRecordSeconds;
    private const double ExternalClockGraceSeconds = 1.1d;
    // When a speedrun is armed from a level marker before the game clock has produced a
    // sample, hold 00:00 through the title card for this long, then free-run so the
    // overlay never sits frozen if the game exposes no readable timer.
    private const double UnsampledFreeRunGraceSeconds = 6d;
    // Retain last known rank/reference during a leaderboard so submit.confirmed can show them
    private int? _lastLeaderboardRank;
    private string _lastLeaderboardReferenceValue = string.Empty;
    // Timer throttle: skip updates when value unchanged within 180 ms (avoids high-frequency GDI+ renders)
    private readonly Dictionary<string, (string Detail, long TicksAtPush)> _timerThrottle = new(StringComparer.OrdinalIgnoreCase);
    // Per-timer staleness: a non-effect timer whose value stops changing for 2 s disappears
    private readonly Dictionary<string, string> _timerLastDetail = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, System.Threading.Timer> _timerStaleness = new(StringComparer.OrdinalIgnoreCase);
    // Sticky single primary (non-effect) timer: the first live clock owns the slot
    // until it goes stale; other live timers are ignored so they never fight.
    private string? _primaryTimerOwner;
    private const int TimerStalenessMs = 2000;
    // Guards the timer dictionaries: the staleness Timer fires on a threadpool thread
    private readonly object _timerLock = new();
    private string _raUsername = string.Empty;
    private bool _gameActive;
    private int? _gameId;
    private int? _leaderboardCatalogGameId;
    private bool _raLeaderboardsEnabled;
    private string _lastLoggedRichPresenceKey = string.Empty;
    private string? _gameIconPath;

    public SurfacePresentationService(IConfigService config, MarqueeController surfaces, IDmdService dmd, ILogger<SurfacePresentationService> logger)
    {
        _config = config;
        _surfaces = surfaces;
        _dmd = dmd;
        _logger = logger;
    }

    public async Task HandleRetroAchievementsAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        if (!_config.RetroAchievementsEnabled) return;
        var type = Text(envelope, "Type", "type");
        var payload = Object(envelope, "Payload", "payload");
        var version = Number(payload, "ContractVersion", "contractVersion") ?? 1;
        if (version > 1)
        {
            _logger.LogWarning("Ignoring unsupported RetroAchievements contract version {Version}", version);
            return;
        }

        if (type.Equals("retroachievements.catalog.updated", StringComparison.OrdinalIgnoreCase))
        {
            var gameId = Integer(payload, "GameId", "gameId");
            if (_gameId != null && gameId != null && _gameId != gameId) ClearGameState(clearCatalog: true);
            if (gameId != null) _gameId = gameId;
            UpdateLeaderboardCatalog(payload);
            UpdateAchievementCatalog(payload);
            if (_gameActive) RefreshBadgeTray();
            return;
        }

        if (type.Equals("retroachievements.session.updated", StringComparison.OrdinalIgnoreCase))
        {
            var session = Object(payload, "Session", "session");
            var gameId = Integer(session, "GameId", "gameId");
            if (_gameId != gameId)
            {
                ClearGameState(clearCatalog: _leaderboardCatalogGameId != gameId);
                _gameId = gameId;
            }
            _gameIconPath = LocalMedia(Text(session, "GameImageIconUrl", "gameImageIconUrl"));
            if (gameId != null)
            {
                _gameActive = true;
            }
            if (!_gameActive)
            {
                _logger.LogDebug("Ignoring RetroAchievements session snapshot while no game is active: gameId={GameId}", gameId);
                return;
            }
            // Sync previously unlocked achievements for the badge tray
            var sessionUnlocked = IntArray(session, "SessionUnlockedAchievements", "sessionUnlockedAchievements");
            _unlockedAchievementIds.Clear();
            foreach (var id in sessionUnlocked) _unlockedAchievementIds.Add(id);
            RefreshBadgeTray();
            _logger.LogDebug("RetroAchievements session snapshot accepted: gameId={GameId}, active={Active}", gameId, _gameActive);
            _raLeaderboardsEnabled = Boolean(session, "LeaderboardsEnabled", "leaderboardsEnabled") ?? false;
            if (!_raLeaderboardsEnabled)
            {
                ClearLeaderboardPresentation();
            }
            UpdateRaPersistent(session, cancellationToken);
            return;
        }

        if (type.Equals("retroachievements.session.ended", StringComparison.OrdinalIgnoreCase))
        {
            ClearGameState();
            _gameId = null;
            return;
        }

        if (!_gameActive) return;

        var correlation = Text(envelope, "CorrelationId", "correlationId");
        if (type.Equals("retroachievements.achievement.unlock.confirmed", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.RetroAchievementsUnlockEnabled) return;
            var achievementIdInt = Integer(payload, "AchievementId", "achievementId");
            var achievementId = achievementIdInt?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            if (!Remember($"unlock:{_gameId}:{achievementId}:{correlation}")) return;
            var title = Text(payload, "AchievementTitle", "achievementTitle");
            var description = Text(payload, "AchievementDescription", "achievementDescription");
            var points = Integer(payload, "AchievementPoints", "achievementPoints");
            var badge = LocalMedia(Text(payload, "AchievementBadgeUrl", "achievementBadgeUrl"));
            var detail = points is > 0 ? $"{description} · {points} pts" : description;

            // Badge tray always updated, even during speedrun
            if (achievementIdInt.HasValue)
            {
                _unlockedAchievementIds.Add(achievementIdInt.Value);
                RefreshBadgeTray();
            }

            // During an active or just-finished SPEEDRUN leaderboard: suppress display
            // so the rank/timer/result stays visible; badge tray still updates.
            if (_activeLeaderboardIsSpeedrun || DateTime.UtcNow < _suppressUnlocksUntilUtc)
            {
                _logger.LogDebug("Unlock {Id} during speedrun leaderboard/result — display suppressed, badge tray updated.", achievementId);
                return;
            }

            // Surface: full takeover if not in leaderboard mode at all
            if (_config.RetroAchievementsMarqueeEnabled && _config.RetroAchievementsUnlockTakeoverEnabled && _activeLeaderboardId == null)
                _surfaces.ShowAchievementTakeover(
                    title.Length == 0 ? "Achievement unlocked" : title,
                    detail,
                    points ?? 0,
                    badge,
                    _config.RetroAchievementsUnlockDurationMs);

            // DMD: notification (priority 100)
            if (_config.RetroAchievementsDmdEnabled && _config.RetroAchievementsNotificationsEnabled)
                await _dmd.ShowNotificationAsync("ra-unlock", title.Length == 0 ? "Achievement unlocked" : title, detail, badge, _config.RetroAchievementsUnlockDurationMs, 100, cancellationToken);
            return;
        }

        if (type.Equals("retroachievements.warning.detected", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.RetroAchievementsWarningEnabled) return;
            if (!Remember($"warning:{correlation}")) return;
            await NotifyAsync("ra-warning", "RetroAchievements warning", Text(payload, "Message", "message", "GameTitle", "gameTitle"), null, _config.RetroAchievementsWarningDurationMs, cancellationToken);
            return;
        }

        if (type.Equals("retroachievements.challenge.changed", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.RetroAchievementsChallengeEnabled) return;
            var state = Text(payload, "State", "state");
            var achievementId = Integer(payload, "AchievementId", "achievementId")?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            var badge = LocalMedia(Text(payload, "BadgePath", "badgePath", "BadgeUrl", "badgeUrl"));
            if (IsTerminal(state))
            {
                _activeRaBadges.Remove("challenge:" + achievementId);
                RefreshStatusBadges();
                _surfaces.ClearInformation("ra-challenge");
                _dmd.ClearOwner("RA CHALLENGE");
                return;
            }
            if (badge != null) _activeRaBadges["challenge:" + achievementId] = badge;
            RefreshStatusBadges();
            var title = Text(payload, "Title", "title");
            var current = Decimal(payload, "CurrentValue", "currentValue");
            var target = Decimal(payload, "TargetValue", "targetValue");
            var percent = Decimal(payload, "Percent", "percent");
            var detail = current != null && target != null ? $"{current:0.##}/{target:0.##}" : percent != null ? $"{percent:0.#}%" : Text(payload, "Description", "description");
            SetPersistent("ra-challenge", "RA CHALLENGE", title, detail, 80, cancellationToken, "active-ra", rotationDurationMs: _config.RetroAchievementsScoreDurationMs);
            return;
        }

        if (type.Equals("retroachievements.leaderboard.changed", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("retroachievements.leaderboard.submit.confirmed", StringComparison.OrdinalIgnoreCase))
        {
            if (!_config.RetroAchievementsLeaderboardEnabled) return;
            if (!_raLeaderboardsEnabled)
            {
                ClearLeaderboardPresentation();
                _logger.LogDebug("Ignoring leaderboard event while ES leaderboards are disabled: type={Type}, state={State}, leaderboardId={LeaderboardId}",
                    type, Text(payload, "State", "state"), Integer(payload, "LeaderboardId", "leaderboardId"));
                return;
            }
            var state = Text(payload, "State", "state");
            var numericLeaderboardId = Integer(payload, "LeaderboardId", "leaderboardId");
            var leaderboardId = numericLeaderboardId?.ToString(CultureInfo.InvariantCulture) ?? "unknown";
            var incomingSource = Text(payload, "Source", "source");
            var incomingIsTerminal = IsTerminal(state) || type.EndsWith("submit.confirmed", StringComparison.OrdinalIgnoreCase);

            // Lock onto the running speedrun: a game can expose several leaderboards
            // (score, per-act…). While one speedrun is actively scrolling, ignore every
            // event for a DIFFERENT leaderboard so the record/chrono never switches.
            // Terminal events for the active leaderboard keep the matching id and pass.
            var catalogMatchesGame = _leaderboardCatalogGameId == null || _gameId == null || _leaderboardCatalogGameId == _gameId;
            LeaderboardCatalogEntry? catalogEntry = null;
            if (catalogMatchesGame) _leaderboards.TryGetValue(numericLeaderboardId ?? 0, out catalogEntry);
            var title = Text(payload, "Title", "title");
            if (title.Length == 0) title = catalogEntry?.Title ?? string.Empty;
            var value = Text(payload, "FormattedValue", "formattedValue", "Value", "value");
            var rank = Integer(payload, "Rank", "rank");
            var referenceRank = Integer(payload, "ReferenceRank", "referenceRank");
            var referenceUser = Text(payload, "ReferenceUser", "referenceUser");
            var referenceValue = Text(payload, "ReferenceFormattedScore", "referenceFormattedScore");
            var personalBestValue = Text(payload,
                "PersonalBestFormattedScore", "personalBestFormattedScore",
                "BestFormattedScore", "bestFormattedScore");
            var user = Text(payload, "User", "user", "Username", "username");
            if (user.Length == 0) user = _raUsername;
            // Leaderboards don't have their own badge — no fallback to _gameIconPath to avoid polluting status badges
            var leaderboardBadge = LocalMedia(Text(payload, "BadgePath", "badgePath", "BadgeUrl", "badgeUrl", "LeaderboardBadgeUrl", "leaderboardBadgeUrl"))
                ?? catalogEntry?.BadgePath;
            var references = ReadLeaderboardReferences(payload);
            if (references.Count > 0 && numericLeaderboardId is { } id)
            {
                lock (_leaderboardScrollLock) _leaderboardReferences[id] = references;
            }
            else if (numericLeaderboardId is { } fallbackId &&
                     !string.IsNullOrWhiteSpace(catalogEntry?.TopUser) &&
                     IsPositiveRaceTime(catalogEntry.TopFormattedScore))
            {
                lock (_leaderboardScrollLock)
                {
                    if (!_leaderboardReferences.ContainsKey(fallbackId))
                    {
                        _leaderboardReferences[fallbackId] =
                        [
                            new LeaderboardReference(1, catalogEntry.TopUser, catalogEntry.TopFormattedScore)
                        ];
                    }
                }
            }
            if (referenceRank == null && !string.IsNullOrWhiteSpace(catalogEntry?.TopUser)) referenceRank = 1;
            if (referenceUser.Length == 0) referenceUser = catalogEntry?.TopUser ?? string.Empty;
            if (referenceValue.Length == 0) referenceValue = catalogEntry?.TopFormattedScore ?? string.Empty;
            if (!IsPositiveRaceTime(referenceValue))
            {
                var positiveReference = references.FirstOrDefault(item => IsPositiveRaceTime(item.FormattedScore));
                referenceValue = positiveReference?.FormattedScore ?? string.Empty;
                referenceRank = positiveReference?.Rank;
                referenceUser = positiveReference?.User ?? string.Empty;
            }
            if (!IsPositiveRaceTime(personalBestValue)) personalBestValue = string.Empty;
            var reference = FormatReference(new LeaderboardReference(referenceRank, referenceUser, referenceValue));
            var detail = string.Join(" · ", new[]
            {
                value,
                rank is > 0 ? $"rank {rank}" : string.Empty,
                reference
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var leaderboardType = ResolveLeaderboardType(title, catalogEntry?.Title ?? string.Empty, value, catalogEntry?.Format ?? string.Empty);
            var isLevelScopedSpeedrun = IsLevelScopedSpeedrun(title, catalogEntry?.Description ?? string.Empty);
            var speedrunSeconds = TryParseRaceTime(value, out var parsedSpeedrunSeconds) ? parsedSpeedrunSeconds : (double?)null;
            var speedrunValue = speedrunSeconds != null ? FormatRaceTime(speedrunSeconds.Value) : "00:00.00";
            var presentationActive = _leaderboardScrollCancellation != null;
            var activePresentationId = _activeLeaderboardId;
            var canSwitchInferredLevel = presentationActive &&
                incomingSource.Equals("retroachievements.runtime.inferred", StringComparison.OrdinalIgnoreCase) &&
                state.Equals("started", StringComparison.OrdinalIgnoreCase) &&
                leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase) &&
                _lastLevelTimerSeconds is <= 2d &&
                DateTime.UtcNow - _lastLevelTimerSampleAtUtc < TimeSpan.FromSeconds(2);
            if (presentationActive &&
                !canSwitchInferredLevel &&
                activePresentationId is > 0 &&
                numericLeaderboardId is > 0 &&
                activePresentationId != numericLeaderboardId &&
                !incomingIsTerminal)
            {
                _logger.LogDebug("Ignoring background leaderboard {LeaderboardId} while {ActiveLeaderboardId} owns the speedrun overlay", numericLeaderboardId, activePresentationId);
                return;
            }

            if (leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase) && numericLeaderboardId is { } speedrunLeaderboardId)
            {
                await HandleSpeedrunLeaderboardAsync(
                    speedrunLeaderboardId,
                    state,
                    type,
                    title.Length > 0 ? title : catalogEntry?.Title ?? "SPEEDRUN",
                    value,
                    rank,
                    referenceValue,
                    user,
                    leaderboardBadge,
                    references,
                    isLevelScopedSpeedrun,
                    personalBestValue,
                    cancellationToken);
                return;
            }

            if (leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase))
            {
                var comparison = SelectSpeedrunComparison(speedrunSeconds ?? 0, references, user);
                detail = BuildLeaderboardDetail(speedrunValue, null, comparison.Reference);
            }
            if (IsTerminal(state) || type.EndsWith("submit.confirmed", StringComparison.OrdinalIgnoreCase))
            {
                var isResultEvent = RaLeaderboardPresentationRules.IsResultEvent(type, state);
                var terminalMatchesActive = numericLeaderboardId == null
                    || _activeLeaderboardId == null
                    || _activeLeaderboardId == numericLeaderboardId
                    || (isResultEvent && leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase));
                if (!terminalMatchesActive)
                {
                    _logger.LogDebug("Ignoring terminal leaderboard event for inactive leaderboard {LeaderboardId}; active={ActiveLeaderboardId}, state={State}, type={Type}",
                        numericLeaderboardId, _activeLeaderboardId, state, type);
                    return;
                }

                if (leaderboardBadge != null) _activeRaBadges.Remove("leaderboard:" + leaderboardId);
                RefreshStatusBadges();
                _surfaces.ClearInformation("ra-leaderboard");
                _dmd.ClearOwner("RA LEADERBOARD");
                StopLeaderboardScroll();
                _activeLeaderboardId = null;
                _activeLeaderboardIsSpeedrun = false;
                _activeLeaderboardIsLevelScoped = false;
                _activeLeaderboardTitle = "SPEEDRUN";
                _activeLeaderboardUser = string.Empty;
                _activeLeaderboardBadgePath = null;
                _activeLeaderboardClockBaseSeconds = 0;
                _activeLeaderboardClockSyncedAt = DateTime.MinValue;
                _activeLeaderboardClockExternallySynced = false;

                // submit.confirmed has FormattedValue but no Rank/ReferenceFormattedScore.
                // Fall back to values tracked from preceding non-terminal events, then to
                // referenceRank (which is the top-1 if available) as a last resort.
                var displayRank = rank is > 0 ? rank
                    : _lastLeaderboardRank is > 0 ? _lastLeaderboardRank
                    : referenceRank is > 0 ? referenceRank  // top entry as minimum info
                    : (int?)null;
                var displayRef = string.IsNullOrWhiteSpace(referenceValue) ? _lastLeaderboardReferenceValue : referenceValue;
                _lastLeaderboardRank = null;
                _lastLeaderboardReferenceValue = string.Empty;

                var finalTitle = user.Length > 0 ? user : (title.Length == 0 ? "LEADERBOARD" : ShortTitle(title, "LEADERBOARD"));
                var finalDetail = displayRank is > 0
                    ? $"{FormatRank(displayRank.Value)} {value}".Trim()
                    : string.IsNullOrWhiteSpace(value) ? detail : value;

                if (!isResultEvent)
                {
                    _logger.LogDebug("Cleared leaderboard {LeaderboardId} without result display: state={State}, type={Type}, source={Source}",
                        numericLeaderboardId, state, type, Text(payload, "Source", "source"));
                    return;
                }

                // Speedrun: show frozen time + rank + diff ("New record!" / no record) on surface.
                // Guard against the dedicated speedrun handler having just shown the same result
                // (id-carrying event) so an id-less terminal event does not double the card.
                if (leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase) && _config.RetroAchievementsMarqueeEnabled &&
                    DateTime.UtcNow - _lastSpeedrunResultCardAtUtc >= TimeSpan.FromSeconds(4))
                {
                    _lastSpeedrunResultCardAtUtc = DateTime.UtcNow;
                    _suppressUnlocksUntilUtc = DateTime.UtcNow.AddMilliseconds(_config.RetroAchievementsSpeedrunResultDurationMs);
                    var diff = BuildSpeedrunDiff(value, displayRef);
                    var isRecord = !string.IsNullOrWhiteSpace(diff) && diff.StartsWith("−", StringComparison.Ordinal);
                    _surfaces.ShowLeaderboardResult(
                        string.IsNullOrWhiteSpace(value) ? finalDetail : value,
                        displayRank is > 0 ? FormatRank(displayRank.Value) : string.Empty,
                        diff,
                        isRecord,
                        _config.RetroAchievementsSpeedrunResultDurationMs,
                        leaderboardBadge);
                }

                // DMD notification for all terminal leaderboard events
                if (_config.RetroAchievementsDmdEnabled && _config.RetroAchievementsNotificationsEnabled)
                    await _dmd.ShowNotificationAsync("ra-leaderboard", finalTitle, finalDetail, leaderboardBadge, _config.RetroAchievementsLeaderboardDurationMs, 100, cancellationToken);
                else if (!leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase))
                    await NotifyAsync("ra-leaderboard", finalTitle, finalDetail, leaderboardBadge, _config.RetroAchievementsLeaderboardDurationMs, cancellationToken);
            }
            else
            {
                if (leaderboardBadge != null) _activeRaBadges["leaderboard:" + leaderboardId] = leaderboardBadge;
                RefreshStatusBadges();
                var isNewAttempt = state.Equals("started", StringComparison.OrdinalIgnoreCase);

                // Track rank and reference so submit.confirmed (which lacks them) can use them
                if (rank is > 0) _lastLeaderboardRank = rank;
                if (!string.IsNullOrWhiteSpace(referenceValue)) _lastLeaderboardReferenceValue = referenceValue;
                if (string.IsNullOrWhiteSpace(_lastLeaderboardReferenceValue) && !string.IsNullOrWhiteSpace(catalogEntry?.TopFormattedScore))
                    _lastLeaderboardReferenceValue = catalogEntry.TopFormattedScore;

                // Detect speedrun from catalog top score format when FormattedValue is empty at "started"
                var catalogIsSpeedrun = catalogEntry?.TopFormattedScore is { Length: > 0 } top && top.Contains(':');
                // catalogHasData = the catalog already knows this leaderboard; we can trust its type info.
                // catalogIsSpeedrun = false here means the catalog says it's NOT time-based → respect that.
                var catalogHasData = catalogEntry != null && !string.IsNullOrWhiteSpace(catalogEntry.TopFormattedScore);
                // isLogDirect: log confirmed this leaderboard started. Use as speedrun fallback ONLY when
                // the catalog has no type data yet (TopFormattedScore empty). If the catalog already says
                // "not speedrun" (no ':' in TopFormattedScore), the log event must not override that.
                var isLogDirect = incomingSource.Equals("retroarch.log", StringComparison.OrdinalIgnoreCase);
                var isSpeedrun = leaderboardType.Equals("SPEEDRUN", StringComparison.OrdinalIgnoreCase)
                    || catalogIsSpeedrun
                    || (isLogDirect && isNewAttempt && !catalogHasData);

                if (isSpeedrun && numericLeaderboardId is { } activeId)
                {
                    _logger.LogDebug("Ignoring speedrun leaderboard {LeaderboardId} on legacy presentation path; dedicated speedrun handler owns it", activeId);
                    return;
                }
                else
                {
                    if (_leaderboardScrollCancellation != null)
                    {
                        _logger.LogDebug("Ignoring non-speedrun leaderboard {LeaderboardId} while speedrun overlay is active", numericLeaderboardId);
                        return;
                    }
                    _activeLeaderboardIsSpeedrun = false;
                    _activeLeaderboardIsLevelScoped = false;
                    _activeLeaderboardId = numericLeaderboardId;
                    StopLeaderboardScroll();
                    var focusValue = string.IsNullOrWhiteSpace(value) ? "0" : value;
                    var focusDetail = BuildLeaderboardDetail(focusValue, rank, reference);
                    _surfaceOwners.Add("ra-leaderboard");
                    _dmdOwners.Add("RA LEADERBOARD");
                    if (_config.RetroAchievementsMarqueeEnabled)
                        _surfaces.UpdateSpeedrunDisplay(leaderboardType, focusDetail, leaderboardBadge, 0, null, null, rank is > 0 ? FormatRank(rank.Value) : null, numericLeaderboardId, title);
                    if (_config.RetroAchievementsDmdEnabled)
                        _ = _dmd.SetPersistentTextAsync("RA LEADERBOARD", $"{leaderboardType}\n{focusDetail}", 80, cancellationToken, "active-ra", "leaderboards", _config.RetroAchievementsLeaderboardDurationMs);
                }
            }
        }
    }

    public Task HandleScoreAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        if (!_gameActive)
        {
            _gameActive = true;
        }
        if (!_config.LiveScoreEnabled) return Task.CompletedTask;
        var payload = Object(envelope, "Payload", "payload");
        var player = Integer(payload, "Player", "player") ?? 1;
        var score = Long(payload, "Score", "score") ?? 0;
        var kind = Text(payload, "ScoreKind", "scoreKind");
        // RA scores are rendered from the rich RA snapshot, which also carries
        // hardcore mode and progress. Do not duplicate them as a generic P1 score.
        if (kind.Equals("retroachievements", StringComparison.OrdinalIgnoreCase) ||
            kind.Equals("leaderboard", StringComparison.OrdinalIgnoreCase)) return Task.CompletedTask;
        var owner = $"live-score-p{player}";
        // a score that never exceeds 0 is a dead/misread watcher — do not display it
        if (score <= 0)
        {
            _surfaces.ClearInformation(owner);
            _dmd.ClearOwner("LIVE SCORE");
            return Task.CompletedTask;
        }
        var detail = score.ToString("N0", CultureInfo.CurrentCulture);
        var title = player > 0 ? $"P{player} SCORE" : (kind.Length == 0 ? "SCORE" : kind.ToUpperInvariant());
        SetPersistent(owner, "LIVE SCORE", title, detail, 70, cancellationToken, "status", rotationDurationMs: _config.LiveScoreDurationMs, focusOnChange: true);
        return Task.CompletedTask;
    }

    public Task HandleTimerAsync(JsonElement envelope, CancellationToken cancellationToken)
    {
        if (!_gameActive)
        {
            _gameActive = true;
        }
        if (!_config.LiveTimerEnabled) return Task.CompletedTask;
        var payload = Object(envelope, "Payload", "payload");
        var remaining = Long(payload, "Remaining", "remaining");
        var rawValue = Long(payload, "Value", "value");
        if (remaining == null && rawValue == null) return Task.CompletedTask;
        var value = remaining ?? rawValue ?? 0;
        var unit = Text(payload, "Unit", "unit");
        var role = Text(payload, "TimerRole", "timerRole");
        var sourceKey = Text(payload, "SourceKey", "sourceKey");
        if (sourceKey.Length == 0) sourceKey = "default";
        var kind = Text(payload, "TimerKind", "timerKind");
        var owner = "live-timer:" + sourceKey;
        var dmdOwner = "LIVE TIMER:" + sourceKey;

        var isLevelClock = RaLeaderboardPresentationRules.IsSpeedrunClockTimer(kind, role, sourceKey);
        var levelClockSeconds = isLevelClock
            ? RaLeaderboardPresentationRules.TimerSeconds(value, unit)
            : null;
        if (levelClockSeconds is { } sampledSeconds)
        {
            var previousSeconds = _lastLevelTimerSeconds;
            _lastLevelTimerSeconds = sampledSeconds;
            _lastLevelTimerSampleAtUtc = DateTime.UtcNow;
            var isDropToZero = previousSeconds is { } previous &&
                sampledSeconds <= 2d &&
                sampledSeconds + 2d < previous;
            if (isDropToZero && !_levelResetPending)
            {
                // First low sample after a drop: arm, but wait for confirmation so a
                // single-frame composition dip does not restart the attempt.
                _levelResetPending = true;
            }
            else if (_levelResetPending && sampledSeconds <= 3d)
            {
                // Confirmed: the clock is genuinely near 0 on a subsequent sample.
                _levelResetPending = false;
                _logger.LogInformation(
                    "Level timer reset confirmed ({Current:0.##}); restarting active leaderboard attempt {LeaderboardId}",
                    sampledSeconds,
                    _activeLeaderboardId);
                if (RestartActiveSpeedrunAttempt(sampledSeconds, cancellationToken))
                {
                    ClearTimer(owner, dmdOwner);
                    return Task.CompletedTask;
                }
            }
            else if (!isDropToZero)
            {
                // Clock recovered (glitch) or is running normally: disarm.
                _levelResetPending = false;
            }
        }

        if (_activeLeaderboardIsSpeedrun && levelClockSeconds is { } speedrunSeconds)
        {
            SyncActiveLeaderboardClock(speedrunSeconds);
            ClearTimer(owner, dmdOwner);
            return Task.CompletedTask;
        }

        if (kind.Contains("leaderboard", StringComparison.OrdinalIgnoreCase) ||
            sourceKey.Contains("leaderboard", StringComparison.OrdinalIgnoreCase))
        {
            ClearTimer(owner, dmdOwner);
            return Task.CompletedTask;
        }

        if (_activeLeaderboardId != null &&
            role.Equals("level", StringComparison.OrdinalIgnoreCase))
        {
            ClearTimer(owner, dmdOwner);
            return Task.CompletedTask;
        }

        // Keep unqualified raw signals available in APIExpose diagnostics, but do
        // not present a meaningless "UNKNOWN / 1 unknown" card to the player.
        var unknownUnit = unit.Length == 0 || unit.Equals("unknown", StringComparison.OrdinalIgnoreCase);
        if (unknownUnit && !kind.Equals("effect", StringComparison.OrdinalIgnoreCase))
        {
            ClearTimer(owner, dmdOwner);
            return Task.CompletedTask;
        }

        var temporaryEffect = kind.Equals("effect", StringComparison.OrdinalIgnoreCase) ||
                              role.Equals("powerup", StringComparison.OrdinalIgnoreCase) ||
                              role.Equals("combo", StringComparison.OrdinalIgnoreCase);

        // A timer that never exceeds 0 is a dead/misread watcher — do not display it.
        if (value <= 0)
        {
            ClearTimer(owner, dmdOwner);
            return Task.CompletedTask;
        }

        var detail = FormatTimer(value, unit);
        var title  = temporaryEffect ? EffectTimerTitle(payload, sourceKey, role) : "TIME";

        bool skip;
        lock (_timerLock)
        {
            // Sticky single primary (non-effect) timer: the first live clock wins and
            // holds the slot until it goes stale; other live timers are ignored so they
            // never clear each other in alternation. Effect timers (power-up) coexist.
            if (!temporaryEffect)
            {
                if (_primaryTimerOwner != null && !_primaryTimerOwner.Equals(owner, StringComparison.OrdinalIgnoreCase))
                    return Task.CompletedTask;
                _primaryTimerOwner = owner;
            }

            // Staleness: (re)arm a 2 s timer only when the value actually changed. If
            // it freezes, the timer is not re-armed and fires → the overlay disappears.
            var changed = !_timerLastDetail.TryGetValue(owner, out var last) || last != detail;
            _timerLastDetail[owner] = detail;
            if (changed) ArmTimerStaleness(owner, dmdOwner);

            // Throttle: skip if the formatted value hasn't changed within 180 ms.
            // High-frequency timer signals (30-60 Hz from MAME/wrapper) would otherwise
            // trigger GDI+ DMD renders and WPF BeginInvokes at game framerate.
            var nowTicks = DateTime.UtcNow.Ticks;
            skip = _timerThrottle.TryGetValue(owner, out var prev)
                   && prev.Detail == detail
                   && (nowTicks - prev.TicksAtPush) < TimeSpan.FromMilliseconds(180).Ticks;
            if (!skip)
            {
                _timerThrottle[owner] = (detail, nowTicks);
                _timerDmdOwners.Add(dmdOwner);
            }
        }
        if (skip) return Task.CompletedTask;

        SetPersistent(owner, dmdOwner, title, detail, temporaryEffect ? 90 : 75, cancellationToken, temporaryEffect ? null : "status", rotationDurationMs: _config.LiveTimerDurationMs);
        return Task.CompletedTask;
    }

    private void ClearTimer(string owner, string dmdOwner)
    {
        lock (_timerLock)
        {
            _timerDmdOwners.Remove(dmdOwner);
            _timerThrottle.Remove(owner);
            _timerLastDetail.Remove(owner);
            if (string.Equals(_primaryTimerOwner, owner, StringComparison.OrdinalIgnoreCase)) _primaryTimerOwner = null;
            if (_timerStaleness.Remove(owner, out var timer)) timer.Dispose();
        }
        _surfaces.ClearInformation(owner);
        _dmd.ClearOwner(dmdOwner);
    }

    private void ArmTimerStaleness(string owner, string dmdOwner)
    {
        lock (_timerLock)
        {
            if (_timerStaleness.Remove(owner, out var existing)) existing.Dispose();
            _timerStaleness[owner] = new System.Threading.Timer(
                _ => ClearTimer(owner, dmdOwner), null, TimerStalenessMs, System.Threading.Timeout.Infinite);
        }
    }

    public void ClearGameState(bool clearCatalog = false)
    {
        _gameActive = false;
        _badgeTrayDebounce?.Cancel();
        _badgeTrayDebounce?.Dispose();
        _badgeTrayDebounce = null;
        StopLeaderboardScroll();
        _surfaces.ClearAllInformation();
        _surfaces.ClearBadgeTray();
        foreach (var owner in new[] { "RETROACHIEVEMENTS", "RA CHALLENGE", "RA LEADERBOARD", "LIVE SCORE", "LIVE TIMER" }) _dmd.ClearOwner(owner);
        foreach (var owner in _surfaceOwners.ToArray()) _surfaces.ClearInformation(owner);
        foreach (var owner in _dmdOwners.ToArray()) _dmd.ClearOwner(owner);
        foreach (var owner in _timerDmdOwners.ToArray()) _dmd.ClearOwner(owner);
        _surfaceOwners.Clear();
        _dmdOwners.Clear();
        _timerDmdOwners.Clear();
        lock (_timerLock)
        {
            foreach (var timer in _timerStaleness.Values) timer.Dispose();
            _timerStaleness.Clear();
            _timerLastDetail.Clear();
            _primaryTimerOwner = null;
            _timerThrottle.Clear();
        }
        _activeRaBadges.Clear();
        _unlockedAchievementIds.Clear();
        if (clearCatalog)
        {
            _leaderboards.Clear();
            _leaderboardCatalogGameId = null;
            _achievementBadgeCache.Clear();
        }
        lock (_leaderboardScrollLock) _leaderboardReferences.Clear();
        _activeLeaderboardId = null;
        _activeLeaderboardIsSpeedrun = false;
        _activeLeaderboardIsLevelScoped = false;
        _activeLeaderboardValue = string.Empty;
        _activeLeaderboardTitle = "SPEEDRUN";
        _activeLeaderboardUser = string.Empty;
        _activeLeaderboardBadgePath = null;
        _activeLeaderboardClockBaseSeconds = 0;
        _activeLeaderboardClockSyncedAt = DateTime.MinValue;
        _activeLeaderboardClockExternallySynced = false;
        _activeLeaderboardHasTimerSample = false;
        _activeLeaderboardClockPaused = false;
        _lastLevelTimerSeconds = null;
        _levelResetPending = false;
        _lastLevelTimerSampleAtUtc = DateTime.MinValue;
        _activeSpeedrunRecordSeconds = null;
        _activeSpeedrunUserRecordSeconds = null;
        _scrollStartedAt = DateTime.MinValue;
        _lastLeaderboardRank = null;
        _lastLeaderboardReferenceValue = string.Empty;
        _timerThrottle.Clear();
        _gameIconPath = null;
        _raUsername = string.Empty;
        _raLeaderboardsEnabled = false;
        _lastLoggedRichPresenceKey = string.Empty;
        _suppressUnlocksUntilUtc = DateTime.MinValue;
        _dmd.SetStatusBadges(Array.Empty<string>());
        lock (_dedupeLock) { _dedupe.Clear(); _dedupeOrder.Clear(); }
    }

    public void MarkGameStarted()
    {
        _gameActive = true;
        _logger.LogDebug("Game marked active by frontend game-start.");
    }

    public void MarkGameEnded()
    {
        ClearGameState();
        _logger.LogDebug("Game marked ended by frontend game-end.");
    }

    private void UpdateRaPersistent(JsonElement session, CancellationToken cancellationToken)
    {
        if (!_config.RetroAchievementsPersistentEnabled || !_config.RetroAchievementsScoreEnabled)
        {
            _surfaces.ClearInformation("ra-score");
            _dmd.ClearOwner("RETROACHIEVEMENTS");
            return;
        }
        var hardcore = Boolean(session, "Hardcore", "hardcore") ?? false;
        _raUsername = Text(session, "Username", "username");
        var leaderboardsEnabled = Boolean(session, "LeaderboardsEnabled", "leaderboardsEnabled") ?? false;
        var score = hardcore || leaderboardsEnabled
            ? Integer(session, "Score", "score")
            : Integer(session, "SoftcoreScore", "softcoreScore") ?? Integer(session, "Score", "score");
        var total = Integer(session, "AchievementCount", "achievementCount");
        var unlocked = ArrayLength(session, "SessionUnlockedAchievements", "sessionUnlockedAchievements");
        var richPresence = Text(session, "RichPresence", "richPresence");
        var mode = leaderboardsEnabled ? "LEADERBOARDS" : hardcore ? "HARDCORE" : "SOFTCORE";
        var modeColor = leaderboardsEnabled ? "leaderboards" : hardcore ? "hardcore" : "softcore";
        var richPresenceLogKey = $"{_gameId}|{mode}|{leaderboardsEnabled}|{score}|{total}|{unlocked}|{richPresence}";
        if (!richPresenceLogKey.Equals(_lastLoggedRichPresenceKey, StringComparison.Ordinal))
        {
            _lastLoggedRichPresenceKey = richPresenceLogKey;
            _logger.LogInformation(
                "RetroAchievements presentation snapshot: gameId={GameId}, active={Active}, mode={Mode}, leaderboardsEnabled={LeaderboardsEnabled}, score={Score}, progress={Unlocked}/{Total}, richPresence={RichPresence}",
                _gameId,
                _gameActive,
                mode,
                leaderboardsEnabled,
                score,
                unlocked,
                total,
                string.IsNullOrWhiteSpace(richPresence) ? "<empty>" : richPresence);
        }

        if (_config.RetroAchievementsModeEnabled)
            SetPersistent("ra-mode", "RETROACHIEVEMENTS", "RETROACHIEVEMENTS", mode, 60, cancellationToken, "status", modeColor, _config.RetroAchievementsScoreDurationMs, surfaceEnabled: false);
        else
        {
            _surfaces.ClearInformation("ra-mode");
            _dmd.ClearOwner("RETROACHIEVEMENTS");
        }

        // Build label (title) + value (detail) so the label always describes what's shown below.
        // Nothing is displayed if we have no meaningful data yet — avoids the empty startup overlay.
        string label, detail;
        if (score != null)
        {
            // Score known: "RA SCORE" / "150 pts · 3/37"
            label = "RA SCORE";
            detail = $"{score} pts" + (total is > 0 ? $"  {unlocked}/{total}" : string.Empty);
        }
        else if (richPresence.Length > 0 && total is > 0)
        {
            // Rich presence + progress: "3/37" / rich presence text
            label = $"{unlocked}/{total}";
            detail = richPresence;
        }
        else if (richPresence.Length > 0)
        {
            // Rich presence only: mode as label / rich presence
            label = mode;
            detail = richPresence;
        }
        else if (total is > 0)
        {
            // Just achievement count: "ACHIEVEMENTS" / "3/37"
            label = "ACHIEVEMENTS";
            detail = $"{unlocked}/{total}";
        }
        else
        {
            // Nothing meaningful yet — don't show a useless overlay, wait for real data
            _surfaces.ClearInformation("ra-score");
            _dmd.ClearOwner("RA SCORE");
            return;
        }

        // Hide score from surface when a leaderboard overlay is active
        SetPersistent("ra-score", "RA SCORE", label, detail, 60, cancellationToken, "status", modeColor, _config.RetroAchievementsScoreDurationMs, focusOnChange: true, surfaceEnabled: _activeLeaderboardId == null);
    }

    private void SetPersistent(string owner, string dmdOwner, string title, string detail, int priority, CancellationToken cancellationToken, string? rotationGroup = null, string? detailColor = null, int rotationDurationMs = 4000, bool focusOnChange = false, string? badgePath = null, bool surfaceEnabled = true)
    {
        var isRa = owner.StartsWith("ra", StringComparison.OrdinalIgnoreCase);
        if (surfaceEnabled && (isRa ? _config.RetroAchievementsMarqueeEnabled : _config.LiveDataMarqueeEnabled))
        {
            _surfaceOwners.Add(owner);
            _surfaces.SetInformation(owner, title, detail, badgePath, true, 0);
        }
        if (isRa ? _config.RetroAchievementsDmdEnabled : _config.LiveDataDmdEnabled)
        {
            _dmdOwners.Add(dmdOwner);
            _ = _dmd.SetPersistentTextAsync(
                dmdOwner,
                $"{title}\n{detail}".Trim(),
                priority,
                cancellationToken,
                rotationGroup,
                detailColor,
                rotationDurationMs,
                focusOnChange);
        }
    }

    private void RefreshStatusBadges()
        => _dmd.SetStatusBadges(_activeRaBadges.Values
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray());

    // flow.lifecycle states that mean "not in active timed play" — the speedrun
    // leaderboard must not keep ticking during demos, title screens, level ends…
    private static readonly HashSet<string> IdleFlowActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "TITLE_SCREEN", "DEMO_MODE", "GAME_OVER", "CONTINUE_SCREEN", "CREDITS_SCREEN",
        "CORPORATE_SCREEN", "INTRO_SCREEN", "LOADING_SCREEN", "ATTRACT", "ATTRACT_MODE",
        "LEVEL_ENDING", "LEVEL_CLEAR", "STAGE_CLEAR"
    };

    /// <summary>
    /// ws/ingame flow change: outside active play, force the speedrun leaderboard
    /// down so its chrono/bar don't linger during demos or after a level ends.
    /// </summary>
    public void OnGameplayFlow(string action)
    {
        if (action.Equals("PAUSE_ON", StringComparison.OrdinalIgnoreCase))
        {
            lock (_leaderboardScrollLock)
            {
                if (_activeLeaderboardIsSpeedrun && _activeLeaderboardHasTimerSample)
                {
                    _activeLeaderboardClockBaseSeconds = CurrentActiveLeaderboardSecondsCore(DateTime.UtcNow);
                    _activeLeaderboardClockSyncedAt = DateTime.UtcNow;
                    _activeLeaderboardClockPaused = true;
                }
            }
            return;
        }
        if (action.Equals("PAUSE_OFF", StringComparison.OrdinalIgnoreCase))
        {
            lock (_leaderboardScrollLock)
            {
                _activeLeaderboardClockSyncedAt = DateTime.UtcNow;
                _activeLeaderboardClockPaused = false;
            }
            return;
        }
        if (!IdleFlowActions.Contains(action)) return;
        var active = _leaderboardScrollCancellation != null || _activeLeaderboardId != null;
        _logger.LogInformation("Flow idle action {Action} received (speedrun active={Active})", action, active);
        if (active && !_activeLeaderboardIsSpeedrun) ClearLeaderboardPresentation();
    }

    private async Task HandleSpeedrunLeaderboardAsync(
        int leaderboardId,
        string state,
        string type,
        string title,
        string value,
        int? rank,
        string referenceValue,
        string user,
        string? badgePath,
        List<LeaderboardReference> references,
        bool isLevelScoped,
        string personalBestValue,
        CancellationToken cancellationToken)
    {
        var isStarted = state.Equals("started", StringComparison.OrdinalIgnoreCase);
        var isResult = RaLeaderboardPresentationRules.IsResultEvent(type, state);

        if (isStarted)
        {
            _speedrunRecapCancellation?.Cancel();
            _speedrunRecapCancellation?.Dispose();
            _speedrunRecapCancellation = null;
            _speedrunResultAtUtc.Remove(leaderboardId);
            _activeSpeedrunLeaderboardIds.Add(leaderboardId);
            var catalogHasLevelSpeedrun = _leaderboards.Values.Any(item =>
                item.Format.Contains("TIME", StringComparison.OrdinalIgnoreCase) &&
                IsLevelScopedSpeedrun(item.Title, item.Description));
            if (!isLevelScoped && catalogHasLevelSpeedrun)
            {
                _logger.LogInformation("Tracking global speedrun {LeaderboardId} in background; level-scoped speedruns have display priority", leaderboardId);
                return;
            }

            if (_activeLeaderboardIsSpeedrun && _leaderboardScrollCancellation != null)
            {
                if (isLevelScoped && (!_activeLeaderboardIsLevelScoped || _activeLeaderboardId != leaderboardId))
                {
                    StopLeaderboardScroll();
                    _surfaces.ClearInformation("ra-leaderboard");
                    _dmd.ClearOwner("RA LEADERBOARD");
                }
                else
                {
                    if (badgePath != null) _activeRaBadges["leaderboard:" + leaderboardId] = badgePath;
                    RefreshStatusBadges();
                    return;
                }
            }

            _activeLeaderboardIsSpeedrun = true;
            _activeLeaderboardIsLevelScoped = isLevelScoped;
            _activeLeaderboardId = leaderboardId;
            _activeLeaderboardTitle = string.IsNullOrWhiteSpace(title) ? "SPEEDRUN" : ShortTitle(title, "SPEEDRUN");
            _activeLeaderboardUser = user;
            _activeLeaderboardBadgePath = badgePath;
            var effectiveReferences = GetLeaderboardReferencesCore(leaderboardId);
            if (effectiveReferences.Count == 0) effectiveReferences = references.ToList();
            _activeSpeedrunRecordSeconds = BestPositiveRaceTimeSeconds(effectiveReferences);
            _activeSpeedrunUserRecordSeconds = ParseRaceTimeSeconds(personalBestValue);
            if (!string.IsNullOrWhiteSpace(referenceValue)) _lastLeaderboardReferenceValue = referenceValue;
            if (badgePath != null) _activeRaBadges["leaderboard:" + leaderboardId] = badgePath;
            RefreshStatusBadges();
            // A newly started (or switched) level leaderboard is a fresh attempt: hold the
            // chrono at 00:00 and let the first real game-clock sample drive it. Never seed
            // from the previous level's residual time — that made Act 2 keep counting from
            // Act 1's finish time instead of restarting at 00:00.
            StartLeaderboardScroll(leaderboardId, _activeLeaderboardTitle, user, badgePath, cancellationToken, newAttempt: true, currentSeconds: null);
            return;
        }

        if (isResult)
        {
            var now = DateTime.UtcNow;
            if (_speedrunResultAtUtc.TryGetValue(leaderboardId, out var previousResult) &&
                now - previousResult < TimeSpan.FromSeconds(3))
            {
                _logger.LogDebug("Ignoring duplicate speedrun result for leaderboard {LeaderboardId}", leaderboardId);
                return;
            }
            _speedrunResultAtUtc[leaderboardId] = now;
            _activeSpeedrunLeaderboardIds.Remove(leaderboardId);
            if (_activeLeaderboardId != leaderboardId)
            {
                _logger.LogDebug("Ignoring speedrun result for background leaderboard {LeaderboardId}; active={ActiveLeaderboardId}", leaderboardId, _activeLeaderboardId);
                return;
            }
            var displayRank = rank is > 0 ? FormatRank(rank.Value) : string.Empty;
            var displayRef = string.IsNullOrWhiteSpace(referenceValue) ? _lastLeaderboardReferenceValue : referenceValue;
            var displayTime = string.IsNullOrWhiteSpace(value)
                ? FormatRaceTime(CurrentActiveLeaderboardSecondsCore(DateTime.UtcNow))
                : NormalizeSpeedrunTime(value);

            StopLeaderboardScroll();
            _surfaces.ClearInformation("ra-leaderboard");
            _dmd.ClearOwner("RA LEADERBOARD");
            _suppressUnlocksUntilUtc = DateTime.UtcNow.AddMilliseconds(_config.RetroAchievementsSpeedrunResultDurationMs);

            var diff = BuildSpeedrunDiff(displayTime, displayRef);
            var isRecord = !string.IsNullOrWhiteSpace(diff) && diff.StartsWith("−", StringComparison.Ordinal);
            if (_config.RetroAchievementsMarqueeEnabled &&
                DateTime.UtcNow - _lastSpeedrunResultCardAtUtc >= TimeSpan.FromSeconds(4))
            {
                _lastSpeedrunResultCardAtUtc = DateTime.UtcNow;
                _surfaces.ShowLeaderboardResult(displayTime, displayRank, diff, isRecord, _config.RetroAchievementsSpeedrunResultDurationMs, badgePath);
            }

            if (_config.RetroAchievementsDmdEnabled && _config.RetroAchievementsNotificationsEnabled)
            {
                await _dmd.ShowNotificationAsync("ra-leaderboard", "SPEEDRUN", string.IsNullOrWhiteSpace(displayRank) ? displayTime : $"{displayRank} {displayTime}", badgePath, _config.RetroAchievementsSpeedrunResultDurationMs, 100, cancellationToken);
            }

            ScheduleSpeedrunModeExit(_config.RetroAchievementsSpeedrunResultDurationMs);
            return;
        }

        if (IsTerminal(state))
        {
            _activeSpeedrunLeaderboardIds.Remove(leaderboardId);
            if (_activeSpeedrunLeaderboardIds.Count == 0)
            {
                ClearLeaderboardPresentation();
            }
            return;
        }

        if (_activeLeaderboardIsSpeedrun)
        {
            if (rank is > 0) _lastLeaderboardRank = rank;
            if (!string.IsNullOrWhiteSpace(referenceValue)) _lastLeaderboardReferenceValue = referenceValue;
            if (_activeLeaderboardId == leaderboardId && BestPositiveRaceTimeSeconds(references) is { } recordSeconds)
                _activeSpeedrunRecordSeconds = recordSeconds;
            if (_activeLeaderboardId == leaderboardId && IsPositiveRaceTime(personalBestValue))
                _activeSpeedrunUserRecordSeconds = ParseRaceTimeSeconds(personalBestValue);
        }
    }

    private void ScheduleSpeedrunModeExit(int durationMs)
    {
        _speedrunRecapCancellation?.Cancel();
        _speedrunRecapCancellation?.Dispose();
        var cts = _speedrunRecapCancellation = new CancellationTokenSource();
        _ = Task.Delay(Math.Max(0, durationMs), cts.Token).ContinueWith(task =>
        {
            if (!task.IsCompletedSuccessfully) return;
            _activeSpeedrunLeaderboardIds.Clear();
            ClearLeaderboardPresentation();
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private bool RestartActiveSpeedrunAttempt(double seconds, CancellationToken cancellationToken)
    {
        if (!_activeLeaderboardIsSpeedrun || _activeLeaderboardId is not > 0)
            return false;

        _speedrunRecapCancellation?.Cancel();
        _speedrunRecapCancellation?.Dispose();
        _speedrunRecapCancellation = null;
        _lastLeaderboardRank = null;
        StartLeaderboardScroll(
            _activeLeaderboardId.Value,
            _activeLeaderboardTitle,
            string.IsNullOrWhiteSpace(_activeLeaderboardUser) ? _raUsername : _activeLeaderboardUser,
            _activeLeaderboardBadgePath,
            cancellationToken,
            newAttempt: true,
            currentSeconds: seconds);
        return true;
    }

    private void ClearLeaderboardPresentation()
    {
        _speedrunRecapCancellation?.Cancel();
        _speedrunRecapCancellation?.Dispose();
        _speedrunRecapCancellation = null;
        StopLeaderboardScroll();
        _surfaces.ClearInformation("ra-leaderboard");
        _surfaces.ClearInformation("ra-leaderboard-result");
        _dmd.ClearOwner("RA LEADERBOARD");
        foreach (var key in _activeRaBadges.Keys.Where(key => key.StartsWith("leaderboard:", StringComparison.OrdinalIgnoreCase)).ToArray())
            _activeRaBadges.Remove(key);
        RefreshStatusBadges();
        _activeSpeedrunLeaderboardIds.Clear();
        _speedrunResultAtUtc.Clear();
        _lastSpeedrunResultCardAtUtc = DateTime.MinValue;
        lock (_leaderboardScrollLock) _leaderboardReferences.Clear();
        _activeLeaderboardId = null;
        _activeLeaderboardIsSpeedrun = false;
        _activeLeaderboardIsLevelScoped = false;
        _activeLeaderboardValue = string.Empty;
        _activeLeaderboardTitle = "SPEEDRUN";
        _activeLeaderboardUser = string.Empty;
        _activeLeaderboardBadgePath = null;
        _activeLeaderboardClockBaseSeconds = 0;
        _activeLeaderboardClockSyncedAt = DateTime.MinValue;
        _activeLeaderboardClockExternallySynced = false;
        _activeLeaderboardHasTimerSample = false;
        _activeLeaderboardClockPaused = false;
        _activeSpeedrunRecordSeconds = null;
        _activeSpeedrunUserRecordSeconds = null;
        _lastLeaderboardRank = null;
        _lastLeaderboardReferenceValue = string.Empty;
    }

    private void UpdateAchievementCatalog(JsonElement payload)
    {
        if (!Try(payload, "AchievementsById", out var catalog) || catalog.ValueKind != JsonValueKind.Object) return;
        _achievementBadgeCache.Clear();
        foreach (var property in catalog.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var id = Integer(property.Value, "Id", "id");
            if (id == null && int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) id = parsed;
            if (id == null) continue;
            _achievementBadgeCache[id.Value] = LocalMedia(Text(property.Value, "BadgeUrl", "badgeUrl", "BadgePath", "badgePath"));
        }
    }

    private void RefreshBadgeTray()
    {
        if (!_config.RetroAchievementsEnabled || !_config.RetroAchievementsMarqueeEnabled || !_config.RetroAchievementsBadgeTrayEnabled) return;

        // Debounce: catalog.updated + session.updated fire within milliseconds at game start.
        // Collapsing them into a single update avoids two full badge-tray rebuilds.
        _badgeTrayDebounce?.Cancel();
        _badgeTrayDebounce?.Dispose();
        var cts = _badgeTrayDebounce = new CancellationTokenSource();

        _ = Task.Delay(250, cts.Token).ContinueWith(_ =>
        {
            var badges = _achievementBadgeCache
                .Where(kvp => kvp.Value != null)
                .Select(kvp => (Id: kvp.Key, Path: kvp.Value!, Unlocked: _unlockedAchievementIds.Contains(kvp.Key)))
                .OrderByDescending(t => t.Unlocked)
                .ThenBy(t => t.Id)
                .ToList();
            _surfaces.UpdateBadgeTray(badges);
        }, cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
    }

    // Tracks when the current scroll started (wall-clock from leaderboard log event).
    private DateTime _scrollStartedAt = DateTime.MinValue;

    private void StartLeaderboardScroll(int leaderboardId, string title, string user, string? badgePath, CancellationToken cancellationToken, bool newAttempt = false, double? currentSeconds = null)
    {
        var shouldStart = true;
        lock (_leaderboardScrollLock)
        {
            // Only skip restart when the same leaderboard is already scrolling AND this is not a new attempt
            if (!newAttempt && _activeLeaderboardId == leaderboardId && _leaderboardScrollCancellation != null)
                shouldStart = false;
            _activeLeaderboardId = leaderboardId;
            _activeLeaderboardTitle = string.IsNullOrWhiteSpace(title) ? "SPEEDRUN" : title;
            _activeLeaderboardUser = user;
            _activeLeaderboardBadgePath = badgePath;
            if (newAttempt)
            {
                _activeLeaderboardClockBaseSeconds = Math.Max(0, currentSeconds ?? 0);
                _activeLeaderboardClockSyncedAt = DateTime.UtcNow;
                _activeLeaderboardClockExternallySynced = currentSeconds != null;
                _activeLeaderboardHasTimerSample = currentSeconds != null;
                _activeLeaderboardClockPaused = false;
            }
            else if (currentSeconds != null)
                SyncActiveLeaderboardClockCore(currentSeconds.Value, DateTime.UtcNow, externallySynced: true);
            else if (_activeLeaderboardClockSyncedAt == DateTime.MinValue)
                SyncActiveLeaderboardClockCore(0, DateTime.UtcNow, externallySynced: false);
            if (shouldStart)
                _activeLeaderboardValue = string.Empty;
        }

        if (!shouldStart) return;
        StopLeaderboardScroll();
        var scrollCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _leaderboardScrollCancellation = scrollCancellation;
        var absoluteStart = DateTime.UtcNow;
        _scrollStartedAt  = absoluteStart;

        _ = Task.Run(async () =>
        {
            var dmdLastValue = string.Empty; // DMD only re-rendered when the formatted value changes

            while (!scrollCancellation.IsCancellationRequested)
            {
                string displayTitle;
                string displayUser;
                string? displayBadge;
                List<LeaderboardReference> references;
                double elapsed;
                double? recordSeconds;
                double? userRecordSeconds;
                bool displayIsSpeedrun;
                int? displayLeaderboardId;
                lock (_leaderboardScrollLock)
                {
                    var activeId = _activeLeaderboardId ?? leaderboardId;
                    displayTitle = _activeLeaderboardTitle;
                    displayUser = _activeLeaderboardUser;
                    displayBadge = _activeLeaderboardBadgePath;
                    references = GetLeaderboardReferencesCore(activeId, leaderboardId);
                    elapsed = CurrentActiveLeaderboardSecondsCore(DateTime.UtcNow);
                    recordSeconds = _activeSpeedrunRecordSeconds ?? BestPositiveRaceTimeSeconds(references);
                    userRecordSeconds = _activeSpeedrunUserRecordSeconds;
                    displayIsSpeedrun = _activeLeaderboardIsSpeedrun;
                    displayLeaderboardId = activeId > 0 ? activeId : null;
                }

                var value   = FormatRaceTime(elapsed);
                var comparison = SelectSpeedrunComparison(elapsed, references, displayUser);
                var detail  = BuildLeaderboardDetail(value, null, comparison.Reference);

                _surfaceOwners.Add("ra-leaderboard");
                _dmdOwners.Add("RA LEADERBOARD");

                // Surface: persistent overlay — only Text properties updated, no WPF object creation.
                if (_config.RetroAchievementsMarqueeEnabled)
                    _surfaces.UpdateSpeedrunDisplay(displayIsSpeedrun ? "SPEEDRUN" : displayTitle, detail, displayBadge, elapsed, recordSeconds, userRecordSeconds, comparison.PlayerRank, displayLeaderboardId, displayTitle);

                // DMD: only re-render when the centisecond value actually changed (avoids GDI+ storm).
                if (_config.RetroAchievementsDmdEnabled && value != dmdLastValue)
                {
                    dmdLastValue = value;
                    _ = _dmd.SetPersistentTextAsync("RA LEADERBOARD", $"SPEEDRUN\n{detail}", 80, scrollCancellation.Token, "active-ra", "leaderboards", 900);
                }

                await Task.Delay(33, scrollCancellation.Token);
            }
        }, scrollCancellation.Token);
    }

    private List<LeaderboardReference> GetLeaderboardReferencesCore(params int[] leaderboardIds)
    {
        foreach (var id in leaderboardIds.Where(id => id > 0).Distinct())
        {
            if (_leaderboardReferences.TryGetValue(id, out var references) && references.Count > 0)
            {
                return references.ToList();
            }
        }
        return new List<LeaderboardReference>();
    }

    private static double? ParseRaceTimeSeconds(string formatted)
    {
        var match = System.Text.RegularExpressions.Regex.Match(formatted ?? string.Empty, @"(\d+):(\d+)\.(\d+)");
        if (!match.Success) return null;
        var seconds = int.Parse(match.Groups[1].Value) * 60
            + int.Parse(match.Groups[2].Value)
            + int.Parse(match.Groups[3].Value) / Math.Pow(10, match.Groups[3].Value.Length);
        return seconds > 0 ? seconds : null;
    }

    private static double? BestPositiveRaceTimeSeconds(IEnumerable<LeaderboardReference> references)
        => references
            .Select(reference => ParseRaceTimeSeconds(reference.FormattedScore))
            .Where(seconds => seconds is > 0)
            .DefaultIfEmpty(null)
            .Min();

    private static bool IsPositiveRaceTime(string value)
        => TryParseRaceTime(value, out var seconds) && seconds > 0;

    private static bool IsLevelScopedSpeedrun(string title, string description)
    {
        var text = $"{title} {description}";
        return text.Contains("act", StringComparison.OrdinalIgnoreCase)
            || text.Contains("level", StringComparison.OrdinalIgnoreCase)
            || text.Contains("stage", StringComparison.OrdinalIgnoreCase)
            || text.Contains("zone", StringComparison.OrdinalIgnoreCase)
            || text.Contains("course", StringComparison.OrdinalIgnoreCase)
            || text.Contains("lap", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mission", StringComparison.OrdinalIgnoreCase);
    }

    private void StopLeaderboardScroll()
    {
        var cts = _leaderboardScrollCancellation;
        _leaderboardScrollCancellation = null;
        if (cts == null) return;
        try { cts.Cancel(); }
        catch { }
        cts.Dispose();
    }

    private void SyncActiveLeaderboardClock(double seconds)
    {
        lock (_leaderboardScrollLock)
        {
            if (!_activeLeaderboardIsSpeedrun || _activeLeaderboardId == null) return;
            SyncActiveLeaderboardClockCore(seconds, DateTime.UtcNow, externallySynced: true);
        }
    }

    private void SyncActiveLeaderboardClockCore(double seconds, DateTime now, bool externallySynced)
    {
        _activeLeaderboardClockBaseSeconds = Math.Max(0, seconds);
        _activeLeaderboardClockSyncedAt = now;
        _activeLeaderboardClockExternallySynced = externallySynced;
        _activeLeaderboardHasTimerSample = externallySynced;
    }

    private double CurrentActiveLeaderboardSecondsCore(DateTime now)
    {
        if (_activeLeaderboardClockSyncedAt == DateTime.MinValue) return 0;
        if (_activeLeaderboardClockPaused) return Math.Max(0, _activeLeaderboardClockBaseSeconds);
        var elapsedSinceSync = Math.Max(0, (now - _activeLeaderboardClockSyncedAt).TotalSeconds);
        if (!_activeLeaderboardHasTimerSample)
        {
            // Armed but no game-clock sample yet: hold 0 through the title card, then
            // free-run from 0 so the overlay keeps moving even without a readable timer.
            return elapsedSinceSync <= UnsampledFreeRunGraceSeconds
                ? 0
                : Math.Max(0, elapsedSinceSync - UnsampledFreeRunGraceSeconds);
        }
        var smoothDelta = _activeLeaderboardClockExternallySynced
            ? Math.Min(elapsedSinceSync, ExternalClockGraceSeconds)
            : elapsedSinceSync;
        return Math.Max(0, _activeLeaderboardClockBaseSeconds + smoothDelta);
    }

    private void UpdateLeaderboardCatalog(JsonElement payload)
    {
        _leaderboards.Clear();
        _leaderboardCatalogGameId = Integer(payload, "GameId", "gameId");
        if (!Try(payload, "LeaderboardsById", out var catalog) || catalog.ValueKind != JsonValueKind.Object) return;
        foreach (var property in catalog.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var id = Integer(property.Value, "Id", "id");
            if (id == null && int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) id = parsed;
            if (id == null) continue;
            _leaderboards[id.Value] = new LeaderboardCatalogEntry(
                Text(property.Value, "Title", "title"),
                Text(property.Value, "Description", "description"),
                Text(property.Value, "TopUser", "topUser"),
                Text(property.Value, "TopFormattedScore", "topFormattedScore"),
                LocalMedia(Text(property.Value, "BadgeUrl", "badgeUrl", "BadgePath", "badgePath")),
                Text(property.Value, "Format", "format"));
        }
    }

    private static List<LeaderboardReference> ReadLeaderboardReferences(JsonElement payload)
    {
        var result = new List<LeaderboardReference>();
        if (!Try(payload, "ReferenceEntries", out var entries) && !Try(payload, "referenceEntries", out entries)) return result;
        if (entries.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in entries.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var rank = Integer(item, "Rank", "rank");
            var user = Text(item, "User", "user");
            var formatted = Text(item, "FormattedScore", "formattedScore", "ReferenceFormattedScore", "referenceFormattedScore");
            if (rank is > 0 && user.Length > 0) result.Add(new LeaderboardReference(rank, user, formatted));
        }
        return result;
    }

    private static string BuildLeaderboardDetail(string value, int? rank, string reference)
    {
        var left = string.IsNullOrWhiteSpace(value) ? "TIME" : value;
        var right = !string.IsNullOrWhiteSpace(reference)
            ? reference
            : rank is > 0 ? FormatRank(rank.Value) : string.Empty;
        return string.IsNullOrWhiteSpace(right) ? left : $"{left}  {right}";
    }

    private static string FormatReference(LeaderboardReference reference)
        => reference.Rank is > 0 && reference.User.Length > 0
            ? $"{FormatRank(reference.Rank.Value)} {reference.User}" + (reference.FormattedScore.Length > 0 ? $" {NormalizeSpeedrunTime(reference.FormattedScore)}" : string.Empty)
            : string.Empty;

    private static string FormatReferenceCompact(LeaderboardReference reference)
        => reference.Rank is > 0 && reference.User.Length > 0
            ? $"{FormatRank(reference.Rank.Value)} {reference.User}" + (reference.FormattedScore.Length > 0 ? $" {NormalizeSpeedrunTime(reference.FormattedScore)}" : string.Empty)
            : string.Empty;

    // D4 = minimum 4 digits, no upper clamp — supports leaderboards with tens of thousands of entries.
    private static string FormatRank(int rank) => "#" + Math.Max(0, rank).ToString("D4", CultureInfo.InvariantCulture);

    private static SpeedrunComparison SelectSpeedrunComparison(double elapsedSeconds, IReadOnlyList<LeaderboardReference> references, string user)
    {
        var player = string.IsNullOrWhiteSpace(user) ? "PLAYER" : user.Trim();

        var valid = references
            .Select(reference => new { Reference = reference, Seconds = TryParseRaceTime(reference.FormattedScore, out var seconds) ? seconds : (double?)null })
            .Where(item => item.Seconds is > 0)
            .OrderBy(item => item.Seconds)
            .ThenBy(item => item.Reference.Rank ?? int.MaxValue)
            .Select(item => item.Reference)
            .ToList();

        // No scoreboard yet → show the player at rank 1
        if (valid.Count == 0)
            return new SpeedrunComparison($"{FormatRank(1)} {player}", FormatRank(1));

        // Continuously cycle through the whole scoreboard (airport split-flap): one
        // entry every 1/rate second of the running clock, looping — the users always
        // scroll, independent of how far the player is from the record time.
        var elapsed = Math.Max(0, elapsedSeconds);
        const double ToleranceSeconds = 0.12d;
        var crossed = 0;
        LeaderboardReference? currentReference = null;
        foreach (var reference in valid)
        {
            if (!TryParseRaceTime(reference.FormattedScore, out var referenceSeconds)) continue;
            if (referenceSeconds > elapsed + ToleranceSeconds) break;
            crossed++;
            currentReference = reference;
        }

        var playerRank = FormatRank(crossed + 1);
        var referenceText = currentReference != null
            ? FormatReferenceCompact(currentReference)
            : string.Empty;
        return new SpeedrunComparison(referenceText, playerRank);
    }

    private static string BuildSpeedrunDiff(string submitted, string reference)
    {
        if (string.IsNullOrWhiteSpace(submitted) || string.IsNullOrWhiteSpace(reference)) return string.Empty;
        if (!TryParseRaceTime(submitted, out var subSecs) || !TryParseRaceTime(reference, out var refSecs)) return string.Empty;
        var diff = subSecs - refSecs;
        // Use Unicode minus for negative (record) to distinguish from the "+" prefix
        var sign = diff < 0 ? "−" : "+";
        return $"{sign}{FormatRaceTime(Math.Abs(diff))}";
    }

    private static string NormalizeSpeedrunTime(string value)
    {
        if (!TryParseRaceTime(value, out var totalSeconds)) return string.IsNullOrWhiteSpace(value) ? "00:00.00" : value.Trim();
        return FormatRaceTime(totalSeconds);
    }

    private static string FormatRaceTime(double totalSeconds)
    {
        var roundedCentiseconds = (long)Math.Round(Math.Max(0, totalSeconds) * 100d, MidpointRounding.AwayFromZero);
        var minutes = roundedCentiseconds / 6000;
        var seconds = (roundedCentiseconds / 100) % 60;
        var centiseconds = roundedCentiseconds % 100;
        return $"{minutes:00}:{seconds:00}.{centiseconds:00}";
    }

    private static bool TryParseRaceTime(string value, out double totalSeconds)
    {
        totalSeconds = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 1)
        {
            return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out totalSeconds) ||
                   double.TryParse(parts[0], NumberStyles.Float, CultureInfo.CurrentCulture, out totalSeconds);
        }
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) &&
            (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
             double.TryParse(parts[1], NumberStyles.Float, CultureInfo.CurrentCulture, out seconds)))
        {
            totalSeconds = minutes * 60d + seconds;
            return true;
        }
        return false;
    }

    private static string ResolveLeaderboardType(string title, string fallbackTitle, string value, string format)
    {
        // RetroAchievements marks time-based leaderboards with format="TIME" — the
        // authoritative speedrun signal. Fall back to title/value heuristics.
        if (format.Equals("TIME", StringComparison.OrdinalIgnoreCase)) return "SPEEDRUN";
        var text = $"{title} {fallbackTitle}";
        if (text.Contains("speedrun", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(':', StringComparison.OrdinalIgnoreCase))
            return "SPEEDRUN";
        return "LEADERBOARD";
    }

    private async Task NotifyAsync(string owner, string title, string detail, string? badge, int duration, CancellationToken cancellationToken)
    {
        if (!_config.RetroAchievementsNotificationsEnabled) return;
        if (_config.RetroAchievementsMarqueeEnabled) _surfaces.SetInformation(owner + ":" + Guid.NewGuid(), title, detail, badge, false, duration);
        if (_config.RetroAchievementsDmdEnabled) await _dmd.ShowNotificationAsync(owner, title, detail, badge, duration, 100, cancellationToken);
    }

    private bool Remember(string key)
    {
        lock (_dedupeLock)
        {
            if (!_dedupe.Add(key)) return false;
            _dedupeOrder.Enqueue(key);
            while (_dedupeOrder.Count > 256) _dedupe.Remove(_dedupeOrder.Dequeue());
            return true;
        }
    }

    // "submitting" = RetroArch's "Submitting X for leaderboard N" (the record is sent):
    // the run is over, so it is terminal. "canceled" (US spelling) added alongside
    // "cancelled" since RetroArch logs the single-l form.
    private static bool IsTerminal(string value) => value.Equals("ended", StringComparison.OrdinalIgnoreCase) || value.Equals("completed", StringComparison.OrdinalIgnoreCase) || value.Equals("cancelled", StringComparison.OrdinalIgnoreCase) || value.Equals("canceled", StringComparison.OrdinalIgnoreCase) || value.Equals("submitted", StringComparison.OrdinalIgnoreCase) || value.Equals("submitting", StringComparison.OrdinalIgnoreCase) || value.Equals("failed", StringComparison.OrdinalIgnoreCase);
    private static string ShortTitle(string value, string fallback)
    {
        var title = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return title.Length <= 14 ? title.ToUpperInvariant() : title[..14].ToUpperInvariant();
    }
    private static string EffectTimerTitle(JsonElement payload, string sourceKey, string role)
    {
        var hint = Text(payload, "EffectName", "effectName", "Name", "name", "Label", "label", "Description", "description");
        if (hint.Length == 0 && !role.Equals("powerup", StringComparison.OrdinalIgnoreCase) &&
            !role.Equals("unknown", StringComparison.OrdinalIgnoreCase)) hint = role;
        if (hint.Length == 0) hint = sourceKey;

        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "timer", "time", "effect", "powerup", "countdown", "remaining", "duration", "unknown"
        };
        var keyword = new string(hint.Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(word => !ignored.Contains(word) && word.Any(char.IsLetter));
        if (string.IsNullOrWhiteSpace(keyword)) return "EFFECT TIME";
        keyword = keyword.Length > 8 ? keyword[..8] : keyword;
        return keyword.ToUpperInvariant() + " TIME";
    }
    private static string? LocalMedia(string value) => value.Length > 0 && Path.IsPathRooted(value) && File.Exists(value) ? value : null;
    private static string FormatTimer(long value, string unit)
    {
        var seconds = unit.Equals("milliseconds", StringComparison.OrdinalIgnoreCase) || unit.Equals("ms", StringComparison.OrdinalIgnoreCase) ? value / 1000d : value;
        if (unit.Contains("second", StringComparison.OrdinalIgnoreCase) || unit.Equals("s", StringComparison.OrdinalIgnoreCase) || unit.Equals("ms", StringComparison.OrdinalIgnoreCase))
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }
        // "bcd" is an encoding (binary-coded decimal), not a display unit: never show
        // it as a suffix ("0 bcd"). Treat it — and unknown — as a plain number.
        if (unit.Length == 0 || unit.Equals("unknown", StringComparison.OrdinalIgnoreCase) || unit.Equals("bcd", StringComparison.OrdinalIgnoreCase))
            return value.ToString("N0", CultureInfo.CurrentCulture);
        return value.ToString("N0", CultureInfo.CurrentCulture) + " " + unit;
    }

    private static JsonElement Object(JsonElement source, params string[] names)
    {
        foreach (var name in names) if (Try(source, name, out var value) && value.ValueKind == JsonValueKind.Object) return value;
        return default;
    }
    private static string Text(JsonElement source, params string[] names)
    {
        foreach (var name in names) if (Try(source, name, out var value)) return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
        return string.Empty;
    }
    private static int? Integer(JsonElement source, params string[] names) => Long(source, names) is { } value ? (int?)value : null;
    private static long? Long(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!Try(source, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number)) return number;
        }
        return null;
    }
    private static double? Number(JsonElement source, params string[] names) => Decimal(source, names);
    private static bool? Boolean(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!Try(source, name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var result)) return result;
        }
        return null;
    }
    private static double? Decimal(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!Try(source, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)) return number;
        }
        return null;
    }
    private static int ArrayLength(JsonElement source, params string[] names)
    {
        foreach (var name in names) if (Try(source, name, out var value) && value.ValueKind == JsonValueKind.Array) return value.GetArrayLength();
        return 0;
    }
    private static IReadOnlyList<int> IntArray(JsonElement source, params string[] names)
    {
        foreach (var name in names)
        {
            if (!Try(source, name, out var value) || value.ValueKind != JsonValueKind.Array) continue;
            var result = new List<int>(value.GetArrayLength());
            foreach (var elem in value.EnumerateArray())
                if (elem.TryGetInt32(out var i)) result.Add(i);
            return result;
        }
        return Array.Empty<int>();
    }
    private static bool Try(JsonElement source, string name, out JsonElement value)
    {
        value = default;
        if (source.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in source.EnumerateObject()) if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { value = property.Value; return true; }
        return false;
    }

    private sealed record LeaderboardCatalogEntry(string Title, string Description, string TopUser, string TopFormattedScore, string? BadgePath, string Format);
    private sealed record LeaderboardReference(int? Rank, string User, string FormattedScore);
    private sealed record SpeedrunComparison(string Reference, string PlayerRank);
}
