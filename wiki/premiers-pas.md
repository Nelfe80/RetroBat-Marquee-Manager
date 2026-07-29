# Premiers pas

Installer MarqueeManager tient en un **installateur** : on télécharge, on lance, on active.

## Avant de commencer

- une installation **RetroBat** fonctionnelle ;
- le plugin **[APIExpose](https://github.com/Nelfe80/RetroBat-APIExpose/releases/latest/download/APIExpose-Cabinet-Setup.exe)** installé et fonctionnel — c'est lui qui fournit médias et données à MarqueeManager ;
- le **[runtime .NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0)** ;
- au moins un écran secondaire (marquee, topper…) ou un DMD, physique ou virtuel.

## Installation

1. Téléchargez **[`MarqueeManager-Setup.exe`](https://github.com/Nelfe80/RetroBat-Marquee-Manager/releases/latest/download/MarqueeManager-Setup.exe)** depuis la page des releases.
2. Lancez l'installateur : il installe le plugin dans `RetroBat\plugins\` et enregistre le hook de démarrage EmulationStation — vous obtenez :

    ```text
    RetroBat\plugins\MarqueeManager\
    ```

3. Relancez RetroBat : MarqueeManager démarre automatiquement avec EmulationStation.

!!! note "Que fait le hook ?"
    L'installateur enregistre un script de démarrage côté EmulationStation, sans toucher au reste de RetroBat. La désinstallation le retire tout aussi proprement.

## Premier réglage : vos écrans

Lancez `MarqueeManagerSetup.exe` : au premier démarrage, un **assistant en trois étapes** détecte vos écrans, propose un type pour chacun et pose une configuration fonctionnelle — moins de trois minutes jusqu'au premier marquee. Tout reste retouchable ensuite dans [Mon setup](mon-setup.md).

## Vérifier que ça fonctionne

Naviguez dans EmulationStation : le marquee doit suivre le système puis le jeu sélectionné. Lancez un jeu : le média du jeu s'affiche, et en fin de partie la surface revient à la sélection.

!!! tip "Mise à jour"
    Relancez simplement le nouvel installateur : il met à jour le plugin en place. Votre `config.ini` mérite une copie de sauvegarde avant — la migration de configuration est automatique mais votre fichier personnalisé reste votre référence.
