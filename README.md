# RetroBat MarqueeManager

**MarqueeManager** est un moteur d'orchestration multi-écrans et d'affichage dynamique conçu pour fonctionner en parfaite symbiose avec **APIExpose** pour RetroBat. Il pilote en temps réel plusieurs écrans physiques (marquee/chapiteau principal, topper, DMD virtuel ou physique, IC card/move card, LCD secondaire) via un rendu WPF natif ultra-performant.

Entièrement piloté par les flux WebSocket d'**APIExpose**, MarqueeManager réagit instantanément aux sélections du frontend, aux lancements de jeux, et aux signaux internes des émulateurs (comme les sorties MAME) pour offrir une immersion arcade ultime.

---

## ⚠️ Licences et Protection (IMPORTANT)

Ce projet et ses fichiers associés sont protégés par le modèle de licence APIExpose :
1.  **Logiciel / Code Source** : Distribué sous licence **personnelle et non-commerciale** (voir `LICENSE.md` et `PERSONAL-LICENSE.md`). L'utilisation commerciale, l'intégration payante ou la revente matérielle/logicielle sans accord de licence commerciale écrit préalable est strictement interdite (voir `COMMERCIAL-LICENSE.md`).
2.  **Pack de Données d'Affichage et Médias** : Les configurations et les éléments graphiques associés sont protégés par la licence **`DATA-LICENSE.md`**.

---

## 🏗️ Architecture et Fonctionnement

```
APIExpose (ws://127.0.0.1:12345)
  │
  ├── /ws/marquee       → marquee.snapshot / marquee.snapshot.updated
  ├── /ws/topper        → topper.snapshot
  ├── /ws/instruction-card → instruction-card.snapshot
  ├── /ws/frontend      → ui.game.selected / ui.game.started / ui.game.ended
  ├── /ws/arcade        → mame.output.changed
  ├── /ws/hiscore       → hiscore.updated
  └── /ws/ingame        → ingame.mame.session.started
          │
          ▼
   MarqueeManager.exe (WebSocketListenerService)
          │
          ├── Rendu MPV (WPF) ──► Fenêtres cibles (marquee, topper, lcd, iccard, dmd)
          ├── DmdService      ──► DMD Physique (via dmdext)
          └── RetroAchievementsService ──► Overlays de succès en jeu
```

*   **Zéro couplage externe** : MarqueeManager ne consomme aucune autre source d'événements qu'APIExpose via WebSocket. Aucun script d'événement frontend bloquant n'est requis.
*   **Rendu Multi-Fenêtres** : Chaque cible d'affichage est une instance indépendante sans bordure (`MarqueeWindow`), positionnée nativement au pixel près via l'API Win32 pour contourner les problèmes de mise à l'échelle DPI de Windows.

---

## 🖥️ Cibles d'Affichage Supportées

| Cible | Clé config | Description |
|-------|-----------|-------------|
| **marquee** | `MarqueeScreen` | Chapiteau principal (images statiques, vidéos bouclées, layouts MAME `.lay` dynamiques) |
| **topper** | `TopperScreen` | Écran supérieur complémentaire (topper) |
| **dmd** | `DmdScreen` | Afficheur DMD virtuel WPF (en parallèle ou non avec un DMD physique) |
| **iccard** | `IcCardScreen` | Instruction Card / Move Card interactive |
| **lcd** | `LcdScreen` | Écran d'ambiance secondaire (fanarts, illustrations) |

Chaque fenêtre gère un empilement dynamique de 6 couches graphiques dans un Grid WPF : image de fond, vidéo avec accélération matérielle, layout MAME (.lay), logo composé/fanart, overlays RetroAchievements et texte OSD temporaire.

---

## 📁 Structure du Dépôt

*   `MarqueeManager.exe` : L'exécutable principal précompilé (Windows x64).
*   `config.ini.template` : Modèle de configuration pour déclarer vos écrans, répertoires RetroBat et options matérielles (à copier vers `config.ini`).
*   `systems.scrap` : Fichier de correspondance des consoles pour le scraper d'assets.
*   `dof/` : Répertoire contenant les fichiers de configuration de rétroaction physiques et layouts MAME (ex: `dof/mame/{romName}/default.lay`).
*   `medias/` : Assets graphiques et vidéos par défaut (écrans de chargement, game over, icônes).
*   `tools/` : Dépendances et utilitaires tiers (`dmdext` pour DMD physique et bibliothèques d'intégration matérielle comme `zedmd`).
*   `src/` : Code source C# complet de l'application principale (.NET 8 WPF single-file) et de son utilitaire de configuration Launcher (.NET 4.8 WinForms).

---

## 🔧 Compilation des Sources

Si vous souhaitez compiler l'exécutable vous-même :

1.  Installez le **SDK .NET 8.0** ou supérieur.
2.  Ouvrez un terminal ou PowerShell à la racine du dépôt.
3.  Compilez la solution complète :
    ```powershell
    dotnet publish src\RetroBatMarqueeManager\RetroBatMarqueeManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:IncludeNativeLibrariesForSelfExtract=true -o "./publish"
    ```
4.  Le fichier exécutable compilé se situera dans le dossier `./publish` sous le nom `RetroBatMarqueeManager.App.exe` (à renommer en `MarqueeManager.exe` à la racine pour utilisation).
