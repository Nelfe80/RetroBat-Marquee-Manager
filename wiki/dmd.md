# DMD et ZeDMD

MarqueeManager pilote un **DMD physique** (ZeDMD et compatibles) via les DLL privées des dossiers `tools\dmd` et `tools\zedmd`, et utilise `dmdext` uniquement pour transmettre les médias vidéo compatibles.

!!! tip "Fabriquer le DMD"
    Pour construire un DMD à panneaux LED (montage, câblage HUB75, alimentation, flashage ZeDMD), voir [Matériel](materiel.md#construire-un-dmd-zedmd-128-32).

## Activer le DMD

Dans `config.ini`, section `[DMD]` : activation, modèle, résolution et port. Pour un ZeDMD standard :

```ini
[DMD]
Enabled=true
Model=zedmd
Width=128
Height=32
ZeDmdPort=
OptimizeZeDmd=true
```

`ZeDmdPort=` vide laisse l'auto-détection faire son travail ; indiquer `COMx` accélère le démarrage si vous connaissez le port.

## L'optimisation ZeDMD

Quand `OptimizeZeDmd=true`, MarqueeManager prépare le panneau avant de l'ouvrir : lecture du firmware et de la taille, calibration USB/refresh/luminosité si nécessaire, sauvegarde uniquement si un réglage change. Réglages associés :

| Clé | Valeur neutre | Effet |
|---|---|---|
| `Brightness` | `-1` | Ne modifie pas la luminosité du firmware |
| `UsbPackageSize` | `0` | Auto : 512 en 128×32, 1024 en HD |
| `PanelMinRefreshRate` | `0` | Ne modifie pas la fréquence minimale |

## Un rendu net en 128×32

MarqueeManager rend toujours à la résolution `Width`×`Height`. Les médias déjà en 128×32 s'affichent pixel-perfect ; les autres sont redimensionnés en nearest-neighbor (sans flou, mais le natif reste meilleur).

!!! tip "Le meilleur réglage : générer directement au bon format"
    Côté APIExpose, demandez des DMD générés en 128×32 :

    ```text
    global.apiexpose.marquee_manager.dmd_autogen_profile=128x32
    ```

    Après changement de profil, supprimez les anciens `generated-dmd.png` / `generated-system-dmd.png` en 256×64 pour qu'APIExpose les régénère.

## La rotation d'affichage

Le DMD alterne les blocs selon une priorité claire : **notification > défi/leaderboard > timer/score > état RA > `.lay` MAME > média de base**. Un bloc persistant reste lisible au moins 3 secondes (`MinimumBlockDisplayMs=3000`) ; une notification est verrouillée pendant toute sa durée, puis les valeurs persistantes reviennent.

Jusqu'à deux badges de défis/leaderboards actifs se placent à droite du panneau.

## Pinballs : laisser la main

Les pinballs pilotent leur propre DMD. La liste `ActiveSystemsDMD` déclenche le mode « contrôle externe » :

```ini
ActiveSystemsDMD=fpinball,pinballfx,pinballfx2,pinballfx3,pinballfm,vpinball,zaccariapinball
```

Au lancement d'un jeu de ces systèmes, MarqueeManager libère le DMD (et n'arrête que les `dmdext` qu'il a lancés lui-même), puis reprend la main en fin de partie.

## `.lay` MAME

Les vues `.lay` ne libèrent jamais le DMD physique : la vue `DMD_Only` est rendue hors écran puis transmise au DMD comme n'importe quel média. Seule `ActiveSystemsDMD` donne le contrôle externe.
