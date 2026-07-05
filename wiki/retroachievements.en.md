# RetroAchievements

MarqueeManager displays your RetroAchievements session in real time: hardcore/softcore mode, score, unlocked achievements, active challenges and leaderboards — on the DMD, the LCD and the marquee.

!!! note "APIExpose owns your account"
    MarqueeManager never contacts the RetroAchievements API: credentials, network calls and RA media belong to APIExpose. Here, we only display.

## Enabling

RetroAchievements is **disabled by default**. In `config.ini`:

```ini
[RetroAchievements]
Enabled=true
NotificationsEnabled=true
```

`Enabled=true` is required to receive the session snapshot; `NotificationsEnabled` is the master switch for temporary notifications (unlocks, warnings, leaderboard results).

## Choosing what displays

Each family can be enabled separately:

| Key | Displays |
|---|---|
| `ModeEnabled` | Hardcore/softcore mode |
| `ScoreEnabled` | The persistent RA score |
| `UnlockEnabled` | Unlocked achievements |
| `WarningEnabled` | Session warnings |
| `ChallengeEnabled` | Active challenges |
| `LeaderboardEnabled` | Leaderboards and speedruns |

## Tuning durations

Durations are in milliseconds:

```ini
UnlockDurationMs=6000
ScoreDurationMs=4000
WarningDurationMs=4000
LeaderboardDurationMs=5000
```

An unlocked achievement stays 6 seconds by default and cannot be repainted by a score or timer during that time. The RA score displays in **blue in hardcore**, grey in softcore.

## Speedruns and leaderboards

While a speedrun leaderboard is active:

- the displayed time synchronizes with APIExpose timers (the local clock only smooths between ticks);
- the leaderboard card becomes a full-width bottom banner on the LCD;
- rank/nickname scrolling is capped by `SpeedrunUsersPerSecond=4` (one change every 250 ms);
- when the game has active leaderboards, the persistent `RA SCORE` card steps aside in favor of the leaderboard display.
