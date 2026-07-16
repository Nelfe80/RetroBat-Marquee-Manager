# Dépannage

## Rien ne s'affiche sur le marquee

1. **APIExpose tourne ?** MarqueeManager n'affiche que ce qu'APIExpose lui envoie. Vérifiez que le plugin APIExpose est démarré.
2. **Le bon écran ?** Ouvrez [Mon setup](mon-setup.md) dans l'assistant : « Identifier les écrans » affiche le numéro de chacun, et le plan montre quelle surface vit sur quel écran.
3. **Le runtime .NET 8 Desktop** est-il installé ? Sans lui, l'exécutable ne démarre pas.

## Le DMD est flou

Vos médias DMD sont probablement générés en 256×64 pour un panneau 128×32. Réglez le profil de génération côté APIExpose et purgez les anciens fichiers — voir [DMD — un rendu net](dmd.md#un-rendu-net-en-12832).

## Le ZeDMD n'est pas détecté

- Indiquez le port explicitement : `ZeDmdPort=COMx` dans `[DMD]` (Gestionnaire de périphériques → Ports COM).
- Vérifiez qu'aucune autre application DMD (dmdext lancé à la main, jeu de flipper) ne tient déjà le panneau.

## Le DMD ne revient pas après un flipper

Le mode « contrôle externe » se termine au `ui.game.ended`. Si un pinball s'est terminé brutalement, revenez à la sélection de jeu dans EmulationStation — MarqueeManager y reprend la main. Vérifiez aussi que le système figure bien dans `ActiveSystemsDMD`.

## Ma configuration a changé après une mise à jour

La première migration V1→V2 sauvegarde votre ancien fichier dans `config.ini.v1.bak`, puis migre écrans, DMD, DOF et l'activation RA. Les anciennes clés historiques (scraping, MPV, ImageMagick, génération vidéo…) ne sont volontairement pas reprises : ces responsabilités appartiennent désormais à APIExpose.

## Où sont les logs ?

Dans le dossier `.log\` du plugin. En cas de souci DMD, `DmdDevice.log` (à la racine) contient le dialogue avec le panneau. Joignez ces fichiers à toute demande d'aide.
