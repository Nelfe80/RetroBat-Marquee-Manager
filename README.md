# RetroBat MarqueeManager

MarqueeManager affiche sur les surfaces physiques les données et médias déjà résolus par APIExpose. Il ne scrape rien, ne contacte aucune API RetroAchievements et ne génère aucun média avec ImageMagick ou FFmpeg.

## Flux consommés

| Flux APIExpose | Utilisation |
|---|---|
| `/ws/marquee` | Marquee principal et média du DMD physique/virtuel. |
| `/ws/topper` | Média topper. |
| `/ws/instruction-card` | Première carte d'instructions locale disponible. |
| `/ws/frontend` | Démarrage/fin de jeu et chargement/nettoyage des layouts MAME. |
| `/ws/arcade` | Signaux MAME pour les lampes `.lay`. |
| `/ws/retroachievements` | Session, rich presence, unlocks, challenges et leaderboards. |
| `/ws/score` | Score temps réel normalisé, toutes sources. |
| `/ws/timer` | Timer temps réel normalisé, toutes sources. |
| `/ws/hiscore` | Notification de high score. |

Les reconnexions ont lieu après cinq secondes. Pour RA, APIExpose renvoie d'abord
le catalogue statique retenu, puis le snapshot léger de session. Score et timer
conservent leur propre état courant.

## Rendu

- Cinq surfaces WPF possibles : `marquee`, `topper`, `iccard`, `dmd` virtuel et `lcd`.
- Le média APIExpose reste la couche de fond ; `.lay`, informations persistantes et notifications sont des couches WPF natives.
- Le DMD physique utilise les DLL privées de `tools/dmd` et `tools/zedmd`.
- `dmdext` est utilisé uniquement pour transmettre des médias vidéo compatibles. MarqueeManager arrête seulement les processus qu'il a lui-même lancés.
- Priorité DMD : notification, challenge/leaderboard, timer/score, état RA, `.lay`, média de base.

## Configuration

La configuration V2 est décrite dans [docs/CONFIGURATION.md](docs/CONFIGURATION.md). Une ancienne configuration est sauvegardée dans `config.ini.v1.bak`, puis migrée une seule fois.

RetroAchievements est désactivé par défaut. APIExpose reste propriétaire des identifiants, appels réseau et médias RA.
Les familles RA peuvent être activées séparément et leurs durées DMD sont
réglables en millisecondes ; un unlock dure six secondes par défaut.

## Développement et release

```powershell
dotnet build src\RetroBatMarqueeManager\RetroBatMarqueeManager.csproj -c Debug --no-restore
dotnet run --project tests\MarqueeManager.Tests\MarqueeManager.Tests.csproj -c Debug --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools\release-framework-dependent.ps1 -CopyRoot
```

La release est `win-x64`, single-file et framework-dependent : le runtime .NET 8 Desktop doit être installé. Les DLL natives privées restent dans le dossier du plugin. Le workflow standard remplace aussi l'exécutable racine via `-CopyRoot`, car c'est celui lancé par le hook EmulationStation.

Voir aussi [l’architecture](docs/ARCHITECTURE.md), le [contrat WebSocket](docs/WEBSOCKET_CONTRACTS.md) et les [règles de publication](docs/DEVOPS.md).
