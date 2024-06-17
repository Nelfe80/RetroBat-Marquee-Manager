# User Guide for Marquee Manager in RetroBat

## Description
This document provides instructions for using Marquee Manager, a tool designed to enhance user experience with RetroBat. Marquee Manager enables custom marquee display, automatic scraping of data, and displaying RetroAchievements.

## Features

- **Marquee Display**: Shows a custom banner for each game.
- **Automatic Scraping**: Automatically retrieves marquees via the ScreenScraper API.
- **RetroAchievements Display**: Displays achievements from the RetroAchievements API.

## Installation
1. Copy the files from the `/dist` folder into the `/plugins/MarqueeManager/` directory of RetroBat.
2. Run `install.bat`, then run `.bat` file :
    - `Start`: Default marquee launcher.

## Customization
You can add your own marquees in the `/RetroBat/plugins/MarqueeManager/images/` folder. Use the format `{system_name}-{game_name}.ext`. For example, for Mario on NES, use `nes-mario.jpg`. (`game_name` = ROM name without extension, `system_name` = system folder)

## Auto-Start
Create a Windows shortcut to automatically launch one of the `.bat` files. To make it less intrusive at startup, set the shortcut to minimized (right-click > Properties). Place the shortcut in the Windows startup folder (`shell:startup`).

## Notes
Don't forget to enter your RetroAchievements and ScreenScraper credentials in RetroBat to use these features.

## New Feature: DMD Screen Support

With the latest update, DMD screen support is now available. To enable this, you need to rename `config-ini` to `config` and adjust the path to `dmd.exe` in your configuration file. Make sure the path correctly points to the location of `dmd.exe` to utilize the new display capabilities.

---

# Notice d'utilisation de Marquee Manager pour RetroBat

## Description
Ce document fournit des instructions pour utiliser Marquee Manager, un outil conçu pour améliorer l'expérience utilisateur avec RetroBat. Marquee Manager permet l'affichage de marquees (bannières), le scraping automatique de données, et l'affichage des succès de RetroAchievements.

## Fonctionnalités

- **Affichage du Marquee**: Affiche une bannière personnalisée pour chaque jeu.
- **Scraping Automatique**: Récupère automatiquement des marquees via l'API de ScreenScraper.
- **Affichage des RetroAchievements**: Montre les succès obtenus sur l'API de RetroAchievements.

## Installation
1. Copiez les fichiers du dossier `/dist` dans `/plugins/MarqueeManager/` de RetroBat.
2. Exécutez `install.bat`, puis lancez le fichier `.bat` suivant :
    - `Start`: Lanceur de marquee par défaut.

## Personnalisation
Vous pouvez ajouter vos propres marquees dans le dossier `/RetroBat/plugins/MarqueeManager/images/`. Utilisez le format `{system_name}-{game_name}.ext`. Par exemple, pour Mario sur NES, utilisez `nes-mario.jpg`. (`game_name` = nom de la rom sans l'extension, `system_name` = dossier du système)

## Démarrage Automatique
Créez un raccourci Windows pour lancer automatiquement un des fichiers `.bat`. Pour le rendre plus discret au démarrage, mettez le raccourci en fenêtre réduite (clic droit > Propriétés). Placez le raccourci dans le dossier de démarrage de Windows (`shell:startup`).

## Remarques
N'oubliez pas d'entrer vos identifiants pour RetroAchievements et ScreenScraper dans RetroBat pour bénéficier de ces fonctionnalités.

## Nouvelle fonctionnalité : Support de l'écran DMD

Avec la dernière mise à jour, le support de l'écran DMD est désormais disponible. Pour l'activer, vous devez renommer `config-ini` en `config` et ajuster le chemin vers `dmd.exe` dans votre fichier de configuration. Assurez-vous que le chemin pointe correctement vers l'emplacement de `dmd.exe` pour utiliser les nouvelles capacités d'affichage.
