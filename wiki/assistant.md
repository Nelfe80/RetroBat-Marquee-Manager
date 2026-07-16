# L'assistant de configuration

`MarqueeManagerSetup.exe`, à la racine du plugin, est l'outil visuel qui configure vos écrans sans éditer `config.ini` à la main. Il détecte vos écrans, vous aide à décider lequel sert de marquee, de topper ou d'instruction card, règle le DMD physique et prépare même le tactile — puis écrit proprement la configuration (avec sauvegarde `.bak`, sans toucher aux commentaires du fichier).

Il propose sept vues dans la barre latérale, avec bascule thème sombre/clair et langue FR/EN en haut du rail.

!!! note "Français ou anglais"
    L'assistant s'affiche dans la langue de RetroBat (réglage EmulationStation), sinon celle de Windows — et se bascule à tout moment avec le bouton FR/EN du rail (choix mémorisé). Pour forcer : `MarqueeManagerSetup.exe --lang fr` ou `--lang en`.

## Accueil

![Onglet Accueil](assets/setup/setup-home.png)

L'état de l'installation en un coup d'œil : runtime démarré ou non (avec bouton Démarrer/Arrêter), réponse d'APIExpose, présence de `config.ini`, résumé des surfaces configurées et raccourcis vers les autres vues.

## Écrans

![Onglet Écrans](assets/setup/setup-screens.png)

L'onglet de départ : tous les écrans Windows détectés, avec leur numéro, leur résolution, leur ratio et la détection tactile.

- **Identifier les écrans** affiche un grand « ÉCRAN n » sur chaque écran physique pendant quelques secondes — fini de deviner quel numéro Windows correspond à quel écran de la borne.
- **Afficher la mire** remplit un écran d'une mire de réglage (grille, bordure, croix centrale). Cliquez dessus pour la fermer.
- Le **rapport de détection** en bas résume tout : écrans et suggestions (« ratio 5.33, suggéré marquee »), pile DMD, ports série, état de MarqueeManager et d'APIExpose.

!!! note "Les numéros affichés sont ceux de config.ini"
    L'assistant énumère les écrans exactement comme le runtime : le numéro affiché est celui à retrouver dans `MarqueeScreen`, `TopperScreen`, etc.

## Surfaces

![Onglet Surfaces](assets/setup/setup-surfaces.png)

Le cœur de l'outil, désormais **dynamique** : créez autant de surfaces que nécessaire (votre configuration `[Screens]` existante est convertie automatiquement au premier lancement, à comportement identique). Pour chaque surface :

- **la catégorie** : marquee, topper, instruction card, DMD virtuel, LCD ou libre ;
- **l'écran** qui l'affiche et **la zone** (largeur × hauteur — la position x,y se règle visuellement dans « Écrans → Composer cet écran ») ;
- **les flux** affichés (une surface peut en recevoir plusieurs) ;
- **les composants** empilés dessus, **placés graphiquement** (« Placer les composants » : glisser, redimensionner, guides magnétiques) : média du flux, logo, fanart, vidéo du jeu, gradient, texte, hiscores, score/timer live, RetroAchievements, rendu lumineux, lampes, cartes d'instructions, et même une page web embarquée (stream Twitch/YouTube en direct). Des **templates de surface** prêts à l'emploi (marquee composé fanart+gradient+logo, cartes fixe+variable, vitrine vidéo, stream web…) créent la pile en un clic.

![Écran vertical partagé](assets/ecran-partage.svg)

Un écran qui héberge plusieurs surfaces (écran vertical typique) se compose visuellement : glissez et redimensionnez les rectangles, avec guides magnétiques et dimensions affichées.

## DMD physique

![Onglet DMD physique](assets/setup/setup-dmd.png)

Réglage de la section `[DMD]` : modèle (ZeDMD, ZeDMD HD, Pin2DMD, PinDMD v3…), résolution, port série, luminosité, taille des paquets USB. L'onglet vérifie aussi que la pile DMD est en place (DLL DmdDevice/ZeDMD, `dmdext.exe`) et liste les ports série détectés.

**Afficher une mire sur le DMD** envoie le motif de test de dmdext au panneau — panneau allumé requis ; l'assistant arrête MarqueeManager le temps du test.

!!! warning "DMD virtuel ≠ DMD physique"
    `DmdScreen=-1` dans l'onglet Surfaces ne coupe que la fenêtre DMD à l'écran. Le vrai panneau se règle ici.

## IC card tactile

![Onglet IC card tactile](assets/setup/setup-touch.png)

Si votre écran instruction card est tactile (ou même à la souris), cet onglet le rend interactif. Quatre modes :

- **Simple** : un tap n'importe où passe à la carte suivante du jeu (how-to-play → moves → …).
- **Centre → IC2** : un appui au centre affiche la carte secondaire (les coups spéciaux, par exemple), puis revient automatiquement à la carte principale après le délai choisi.
- **Dual player** : pour un écran partagé entre deux joueurs — la moitié gauche affiche la carte du joueur 1, la moitié droite celle du joueur 2, avec une zone commune optionnelle au centre :

![Zones tactiles dual player](assets/zones-tactiles.svg)
- **Zones libres** : dessinez vos propres zones directement sur l'aperçu (cliquer-glisser) et choisissez l'action de chacune : carte suivante, carte précise, carte joueur, retour à la carte par défaut.

Le réglage est enregistré dans `state\surfaces.profile.json` et lu par MarqueeManager au démarrage. La souris déclenche les mêmes actions que le tactile — pratique pour tester sans écran tactile.

!!! note "Nommage des cartes (médias APIExpose)"
    Dans `artwork\ic` d'un jeu : `ic.png` pour une carte unique, ou `ic-1.png`, `ic-2.png`… pour plusieurs cartes. Les suffixes `-left`/`-right` (ex. mercs : `ic-1-left.png` … `ic-5-right.png`) sont les **deux porte-cartes du panel** : côté joueur 1 et côté joueur 2. La navigation passe de carte en carte (ic-1 → ic-2…), et le mode dual player affiche le côté du joueur qui a tapé ; `ic2` dans une action désigne bien la carte n°2, quel que soit le nombre de fichiers.

## Mes composants

![Onglet Mes composants](assets/setup/setup-games.png)

La bibliothèque de compositions : templates automatiques, priorités des sources par système (vos images, le scrapé, le généré… dans l'ordre que vous voulez), dossier de médias personnels résolu par alias, compositions originales par jeu ou système, scrap de médias en ligne, effets lumière, lampes et profils. Une page dédiée détaille tout : [Mes composants](mes-jeux.md).

## Options

![Onglet Options](assets/setup/setup-options.png)

Tout le reste, présenté en réglages simples :

- **Connexion** : adresse d'APIExpose avec bouton de test.
- **Rendu lumineux** : le Lighting Engine du marquee — qualité/performance, cadrage, reflet de vitre, sons des tubes.
- **Layouts MAME** : lecture des fichiers `.lay` pour marquee, topper, iccard et DMD.
- **RetroAchievements** : activation par surface, badges, plein écran d'unlock.
- **Score et timer live** : les overlays temps réel sur le marquee et le DMD.

Les réglages fins (durées, seuils…) restent accessibles dans `config.ini`, dont chaque option est commentée — l'assistant n'écrase jamais ces commentaires.
