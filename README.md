# RetroBat Marquee Manager

Moteur d'orchestration multi-écrans pour RetroBat. Pilote en temps réel plusieurs écrans physiques (marquee, topper, DMD, IC card, LCD) via un rendu WPF natif, entièrement piloté par les flux WebSocket d'**APIExpose**.

---

## Architecture

```
APIExpose (ws://127.0.0.1:12345)
  │
  ├── /ws/marquee       → marquee.snapshot / marquee.snapshot.updated
  ├── /ws/topper        → topper.snapshot
  ├── /ws/instruction-card → instruction-card.snapshot
  ├── /ws/frontend      → ui.game.selected / ui.game.started / ui.game.ended / ui.system.selected
  ├── /ws/arcade        → mame.output.changed / mame.session.started
  ├── /ws/hiscore       → hiscore.updated / hiscore.score.changed
  └── /ws/ingame        → ingame.mame.session.started / ingame.memory.changed
          │
          ▼
  WebSocketListenerService (BackgroundService)
          │
          ├── Media direct → MpvController → MarqueeWindow (WPF, thread STA dédié)
          └── Lifecycle    → MarqueeWorkflow → DmdService (DMD physique via dmdext)
                                             └── RetroAchievementsService (overlays RA)
```

**MarqueeManager ne consomme aucune source d'événements autre qu'APIExpose WS.**  
Il n'utilise ni `ESEvent.arg`, ni pipe IPC nommé.

---

## Écrans supportés

| Cible | Clé config | Description |
|-------|-----------|-------------|
| `marquee` | `MarqueeScreen` | Chapiteau principal (image, vidéo, layout MAME .lay) |
| `topper` | `TopperScreen` | Écran topper supérieur |
| `dmd` | `DmdScreen` | DMD virtuel WPF (en plus du DMD physique via dmdext) |
| `iccard` | `IcCardScreen` | Lecteur de carte / Move Card |
| `lcd` | `LcdScreen` | Écran LCD secondaire (fanart, illustrations) |

Chaque cible est une instance indépendante de `MarqueeWindow` (fenêtre WPF sans bordure, topmost, positionnée par `SetWindowPos` Win32 pour éviter les problèmes DPI).

---

## Couches de rendu (par fenêtre)

Chaque `MarqueeWindow` empile 6 couches dans un `Grid` WPF :

1. **Image de fond** — `BitmapImage` (PNG, JPG, GIF)
2. **Vidéo** — `MediaElement` WPF, lecture en boucle automatique
3. **Layout MAME** — `Canvas` dans un `Viewbox`, chargé depuis les fichiers `.lay`
4. **Logo composé** — `Image` avec `TransformGroup` (translate + scale) pour le mode composition fanart+logo
5. **Overlays slots** — `Canvas` libre pour les badges RetroAchievements et autres superpositions temporaires
6. **OSD texte** — `TextBlock` en bas, masqué automatiquement après un délai

---

## Flux WebSocket et routage

### `/ws/marquee` — Priorité d'affichage marquee
Événements : `marquee.snapshot`, `marquee.snapshot.updated`

Ordre de priorité pour la sélection du média :
1. `media.marquee` (marquee scrapée)
2. `media.generatedMarquee` (marquee générée par composition fanart+logo)
3. `media.screenMarquee` / `media.screenMarqueeSmall` (capture d'écran)
4. `media.logo` (wheel/logo seul)
5. `media.fanart` (fanart seul)

Routage simultané dans le même payload :
- `media.dmd` → cible `dmd` (animations GIF en priorité, sinon still PNG)
- `media.topper` → cible `topper`
- `media.fanart` → cible `lcd`

### `/ws/topper` — Écran topper
Événement : `topper.snapshot` → `media.topper` → cible `topper`

### `/ws/instruction-card` — IC Card
Événement : `instruction-card.snapshot` → premier `cards[0]` valide → cible `iccard`

### `/ws/frontend` — Cycle de vie des jeux
| Événement APIExpose | Action MarqueeManager |
|--------------------|-----------------------|
| `ui.game.selected` / `.raw` | Mise à jour état interne + affichage DMD physique du jeu |
| `ui.system.selected` / `.raw` | Mise à jour état interne + affichage DMD physique du système |
| `ui.game.started` / `.raw` | DMD loading screen, chargement layout MAME .lay, suivi état jeu |
| `ui.game.ended` / `.raw` | Nettoyage overlays RA, reset état, écran game-over si configuré |

Les payloads enrichis (`ui.game.started`) sont extraits depuis `payload.context.ui.selected` :
- `gamePath` → chemin ROM complet
- `gameName` → nom affichage ES
- `selectedSystem.name` → identifiant système

### `/ws/arcade` — Sorties MAME
| Événement | Action |
|-----------|--------|
| `mame.output.changed` | `signals[].key/value` → `MarqueeWindow.SetLampState()` pour animer les lampes du layout .lay |
| `mame.session.started` | `machineName` → déclenchement `game-start` pour chargement layout |

### `/ws/hiscore` — Scores
`hiscore.updated` et `hiscore.score.changed` → OSD texte sur les cibles `dmd`, `lcd`, `topper`

### `/ws/ingame` — In-game runtime
`ingame.mame.session.started` → déclenchement `game-start` si session MAME détectée sans événement frontend préalable

---

## Layouts MAME (`.lay`)

Au `game-start`, si un fichier `dof/mame/{romName}/default.lay` existe à la racine du plugin, il est parsé par `MameLayParser` et chargé dans `MarqueeWindow` via `LoadMameLayout()`.

Chaque fenêtre reçoit une vue dédiée :
- `Marquee_Only` → cible `marquee`
- `Topper_Only` → cible `topper`
- `DMD_Only` → cible `dmd`
- `ICCard_Only` → cible `iccard`

Les éléments nommés dans le `.lay` deviennent des lampes dynamiques pilotées par `mame.output.changed`.

---

## DMD physique

Le `DmdService` pilote un DMD physique via `dmdext` (processus externe). Il s'active si `DmdEnabled=True` dans `config.ini`. Le DMD virtuel WPF (cible `dmd`) et le DMD physique fonctionnent en parallèle et indépendamment.

Optimisation DMD physique :
- les frames DMD statiques sont mises en cache en memoire par chemin, taille, date de modification, resolution et mode couleur ;
- le premier rendu d'une image peut encore payer la conversion, puis les passages suivants sur la meme image sont quasi instantanes ;
- le nettoyage global `dmdext` est reserve aux arrets explicites, afin de ne pas ralentir chaque changement de selection quand le driver natif est utilise ;
- la calibration ZeDMD memorise une signature dans `config.ini` (`ZeDmdCalibrationSignature`) et saute la pre-calibration au demarrage quand modele, resolution, luminosite, paquet USB, refresh et port n'ont pas change.

---

## RetroAchievements

Le `RetroAchievementsService` (HostedService) se connecte à l'API RetroAchievements et émet des événements :
- `AchievementUnlocked` → badge overlay temporaire sur marquee et DMD
- `RichPresenceUpdated` → texte RP en rotation sur DMD
- `ChallengeUpdated` → challenges actifs en cycle sur DMD/marquee

Nécessite `RetroAchievementsEnabled=True` et les credentials RA dans `config.ini`.

---

## Configuration (`config.ini`)

```ini
[General]
RetroBatPath=E:\RetroBat
RomsPath=E:\RetroBat\roms
MarqueeImagePath=E:\RetroBat\plugins\RetroBatMarqueeManager\medias
MinimizeToTray=True
LogToFile=True

[ScreenMPV]
; Index écran Windows (0=principal, 1=premier secondaire, etc. ; -1=désactivé)
MarqueeScreen=2
TopperScreen=-1
DmdScreen=-1
IcCardScreen=-1
LcdScreen=-1

[APIExpose]
ApiExposeBaseUrl=ws://127.0.0.1:12345

[DMD]
DmdEnabled=False
DefaultDmdPath=medias\default_dmd.gif

[RetroAchievements]
RetroAchievementsEnabled=False
RetroAchievementsUsername=
RetroAchievementsApiKey=
```

---

## Démarrage et cycle de vie

1. **`Worker.ExecuteAsync`** :
   - Tue les processus `mpv` / `dmdext` orphelins
   - Initialise `MarqueeFileFinder` (lecture `es_settings.cfg`)
   - Démarre `MpvController.StartMpv()` → crée les fenêtres WPF sur thread STA
   - Affiche `DefaultImagePath` après 600ms (évite l'écran noir initial)
   - Démarre `MarqueeWorkflow.Start()` → initialise le DMD physique

2. **`WebSocketListenerService`** (BackgroundService) : se connecte en parallèle à tous les flux APIExpose avec reconnexion automatique (5s).

3. **`RetroAchievementsService`** (HostedService) : polling API RA toutes les 30s pendant un jeu.

4. **`RetroBatMonitorService`** (HostedService) : surveille l'arrêt de RetroBat et déclenche l'arrêt propre.

---

## Structure du projet

```
src/
├── RetroBatMarqueeManager/           # Service principal (.NET 8, win-x64)
│   ├── Program.cs                    # Point d'entrée, DI, tray icon
│   ├── Worker.cs                     # BackgroundService principal
│   ├── GlobalUsings.cs
│   ├── Application/
│   │   ├── Services/
│   │   │   ├── WebSocketListenerService.cs   # Consommateur unique des flux APIExpose
│   │   │   ├── ApiExposeEvent.cs             # Modèle d'événement APIExpose
│   │   │   ├── DmdService.cs                 # DMD physique (dmdext)
│   │   │   ├── ImageConversionService.cs     # ImageMagick (composition fanart+logo)
│   │   │   ├── MarqueeFileFinderService.cs   # Résolution de chemins médias locaux
│   │   │   ├── MameLayParser.cs              # Parser fichiers .lay MAME
│   │   │   ├── RetroAchievementsService.cs   # Client API RetroAchievements
│   │   │   ├── VideoMarqueeService.cs        # Génération vidéos marquee (ffmpeg)
│   │   │   ├── OverlayTemplateService.cs     # Templates overlays (overlays.json)
│   │   │   └── ...
│   │   ├── Workflows/
│   │   │   └── MarqueeWorkflow.cs            # Logique métier (DMD, RA, état jeu)
│   │   └── Imaging/
│   │       └── ImageCommandBuilder.cs
│   ├── Core/
│   │   ├── Interfaces/               # IConfigService, IDmdService, IMarqueeFileFinder...
│   │   └── Models/RetroAchievements/ # Modèles RA (Achievement, ChallengeState...)
│   └── Infrastructure/
│       ├── UI/
│       │   ├── MarqueeWindow.cs      # Fenêtre WPF (6 couches de rendu)
│       │   └── TrayIconService.cs    # Icône systray
│       ├── Processes/
│       │   └── MpvController.cs      # Gestionnaire fenêtres WPF multi-écrans
│       ├── Configuration/
│       │   ├── IniConfigService.cs   # Lecture config.ini
│       │   └── EsSettingsService.cs  # Lecture es_settings.cfg
│       ├── Native/
│       │   └── DmdDeviceWrapper.cs   # Wrapper DMD natif
│       ├── Input/
│       │   └── KeyboardInputService.cs # Raccourcis clavier (mode ajustement vidéo)
│       └── Installation/
│           ├── ScriptInstallerService.cs  # Auto-install scripts ES
│           └── AutoStartService.cs        # Démarrage automatique Windows
│
└── RetroBatMarqueeManager.Launcher/  # Configurateur UI (.NET Framework 4.8)
    ├── Program.cs
    └── Forms/
        ├── ConfigMenuForm.cs         # Interface de configuration
        └── OverlayDesignerForm.cs    # Designer d'overlays RetroAchievements
```

---

## Build et déploiement

```cmd
.\build.bat
```

Compile en mode `Release` pour `win-x64`, publie en **single-file** autonome.  
Le binaire `MarqueeManager.exe` est placé à la racine du dossier plugin.

**Emplacement de déploiement :** `RetroBat\plugins\MarqueeManager\`

Les logs sont écrits dans `logs\debug.log` (rotation automatique à 1,5 MB, 2 fichiers max).
