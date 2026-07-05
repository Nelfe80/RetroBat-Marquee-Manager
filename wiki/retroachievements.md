# RetroAchievements

MarqueeManager affiche votre session RetroAchievements en temps réel : mode hardcore/softcore, score, succès débloqués, défis actifs et leaderboards — sur le DMD, le LCD et le marquee.

!!! note "APIExpose garde la main sur votre compte"
    MarqueeManager ne contacte jamais l'API RetroAchievements : identifiants, appels réseau et médias RA appartiennent à APIExpose. Ici, on ne fait qu'afficher.

## Activer

RetroAchievements est **désactivé par défaut**. Dans `config.ini` :

```ini
[RetroAchievements]
Enabled=true
NotificationsEnabled=true
```

`Enabled=true` est nécessaire pour recevoir le snapshot de session ; `NotificationsEnabled` est l'interrupteur maître des notifications temporaires (unlocks, warnings, résultats de leaderboard).

## Choisir ce qui s'affiche

Chaque famille s'active séparément :

| Clé | Affiche |
|---|---|
| `ModeEnabled` | Le mode hardcore/softcore |
| `ScoreEnabled` | Le score RA persistant |
| `UnlockEnabled` | Les succès débloqués |
| `WarningEnabled` | Les avertissements de session |
| `ChallengeEnabled` | Les défis actifs |
| `LeaderboardEnabled` | Les leaderboards et speedruns |

## Régler les durées

Les durées sont en millisecondes :

```ini
UnlockDurationMs=6000
ScoreDurationMs=4000
WarningDurationMs=4000
LeaderboardDurationMs=5000
```

Un succès débloqué reste affiché 6 secondes par défaut et ne peut pas être remplacé par un score ou un timer pendant ce délai. Le score RA s'affiche en **bleu en hardcore**, en gris en softcore.

## Speedruns et leaderboards

Pendant un leaderboard speedrun actif :

- le temps affiché se synchronise sur les timers APIExpose (le chrono local ne fait que lisser entre deux ticks) ;
- la carte leaderboard devient un bandeau bas pleine largeur sur le LCD ;
- le défilement rank/pseudo est limité par `SpeedrunUsersPerSecond=4` (un changement toutes les 250 ms) ;
- quand le jeu a des leaderboards actifs, la carte persistante `RA SCORE` s'efface au profit de l'affichage leaderboard.
