User Guide for Marquee Manager in RetroBat

Description :
This document provides instructions for using Marquee Manager, a tool designed to enhance user experience with RetroBat. Marquee Manager enables custom marquee display, automatic scraping of data, and displaying RetroAchievements.

Features :

Marquee Display : Shows a custom banner for each game.
Automatic Scraping : Automatically retrieves marquees via the ScreenScraper API.
RetroAchievements Display : Displays achievements from the RetroAchievements API.
Installation :
Copy the files from the /dist folder into the /plugins/MarqueeManager/ of RetroBat. Run install.bat, then choose one of the following .bat files based on your needs:

StartRetrobatMarquees : Default marquee launcher.
StartRetrobatMarqueesAS : Marquee launcher with auto scraping on the ScreenScraper API. Set MarqueeAutoScraping = true in config.ini.
StartRetrobatMarqueesRA : Marquee launcher with RetroAchievements on the RA API. Set MarqueeRetroAchievements = true in config.ini.
StartRetrobatMarqueesASRA : Marquee launcher with both auto scraping and RetroAchievements. Activate both options in config.ini.
Customization :
You can add your own marquees in the /RetroBat/plugins/MarqueeManager/images/ folder. Use the format {system_name}-{game_name}.ext. For example, for Mario on NES, use nes-mario.jpg. (game_name = rom name without ext, system_name = system folder)

Auto-Start :
Create a Windows shortcut to automatically launch one of the .bat files. To make it less intrusive at startup, set the shortcut to minimized (right-click > Properties). Place the shortcut in the Windows startup folder (shell:startup).

Notes :
Don't forget to enter your RetroAchievements and ScreenScraper credentials in RetroBat to use these features.

##########################################################################################################################

Notice d'utilisation de Marquee Manager pour RetroBat

Ce document fournit des instructions pour utiliser Marquee Manager, un outil conçu pour améliorer l'expérience utilisateur avec RetroBat. Marquee Manager permet l'affichage de marquees (bannières), le scraping automatique de données, et l'affichage des succès de RetroAchievements.

Fonctionnalités :

Affichage du Marquee : Affiche une bannière personnalisée pour chaque jeu.
Scraping Automatique : Récupère automatiquement des marquees via l'API de ScreenScraper.
Affichage des RetroAchievements : Montre les succès obtenus sur l'API de RetroAchievements.
Installation :
Copiez les fichiers du dossier /dist dans /plugins/MarqueeManager/ de RetroBat. Exécutez install.bat, puis choisissez l'un des trois fichiers .bat suivants selon vos besoins :

StartRetrobatMarquees : Lanceur de marquee par défaut.
StartRetrobatMarqueesAS : Lanceur de marquee avec scraping automatique via l'API de ScreenScraper. Activez MarqueeAutoScraping = true dans config.ini.
StartRetrobatMarqueesRA : Lanceur de marquee avec RetroAchievements via l'API de RA. Activez MarqueeRetroAchievements = true dans config.ini.
StartRetrobatMarqueesASRA : Lanceur de marquee avec scraping automatique et RetroAchievements. Activez les deux options dans config.ini.
Personnalisation :
Vous pouvez ajouter vos propres marquees dans le dossier /RetroBat/plugins/MarqueeManager/images/. Utilisez le format {system_name}-{game_name}.ext. Par exemple, pour Mario sur NES, utilisez nes-mario.jpg. (game_name = nom de la rom sans l'extension, system_name = dossier du système)

Démarrage Automatique :
Créez un raccourci Windows pour lancer automatiquement un des fichiers .bat. Pour le rendre plus discret au démarrage, mettez le raccourci en fenêtre réduite (clic droit > Propriétés). Placez le raccourci dans le dossier de démarrage de Windows (shell:startup).

Remarques :
N'oubliez pas d'entrer vos identifiants pour RetroAchievements et ScreenScraper dans RetroBat pour bénéficier de ces fonctionnalités.






