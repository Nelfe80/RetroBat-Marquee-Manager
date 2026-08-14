# Mon setup

**Mon setup** est le plan de votre installation : chaque écran détecté y apparaît là où il est physiquement (glissez-les pour refléter votre borne, votre meuble ou votre bureau). De là, tout se configure en descendant du général au détail : **le plan → un écran → une surface → sa création graphique**.

![Vue Mon setup](assets/setup/setup-monsetup.png)

## Un type d'écran = tout est configuré

Cliquez sur un écran, choisissez son **type**, appliquez : surfaces, composants et flux par défaut sont posés — l'écran est fonctionnel immédiatement.

| Type | Ce qui est posé |
|---|---|
| **Marquee** | Surface plein écran : média du jeu, rendu lumineux (tubes néon), lampes, hiscores, score/timer live, RetroAchievements |
| **Topper** | Surface topper plein écran |
| **Instruction card** | La carte d'instructions du jeu (tactile pris en charge si l'écran l'est) |
| **DMD virtuel** | Une fenêtre DMD plein écran |
| **Vertical mixte** | Marquee en bandeau haut + instruction card en bas, **RetroBat/le jeu reste visible au centre** |
| **Écran de jeu** | Rien : RetroBat est chez lui |
| **Libre** | Une surface vide à créer soi-même |

L'assistant pré-suggère le type d'après la forme de l'écran (un bandeau 5:1 est probablement un marquee). L'expert retouche ensuite ce qu'il veut : « Diviser / positionner les surfaces » ouvre l'éditeur visuel de zones (glisser, redimensionner, guides magnétiques), y compris sur l'écran principal.

## Les états d'affichage

Chaque **surface** et chaque composant appartient à un état : **Navigation ES**, **En jeu**, ou les deux (défaut). Une surface « En jeu seulement » disparaît complètement pendant la navigation — par exemple, rien au-dessus d'ES sur l'écran RetroBat tant qu'on navigue. Le sélecteur d'état en haut du plan montre ce que chaque écran affichera dans chaque situation ; l'état d'une surface se règle dans « Éditer les surfaces » (Visible en : …).

!!! tip "Navigation dans le plan"
    Un **premier clic** sur un écran le sélectionne et affiche ses détails dessous ; un **deuxième clic** ouvre l'éditeur de surfaces. Le **DMD physique** apparaît comme un écran du plan (déplaçable, liseré rouge) — son deuxième clic ouvre ses réglages.

## La création graphique d'une surface

Le bouton « **Configurer** » d'une surface ouvre l'interface de création graphique, logique Photoshop :

- **à gauche, les éléments** par groupes : médias (fanart, logo 50 %, vidéo du jeu…), infos du jeu (titre, année/éditeur), live (hiscores, score, timer), RetroAchievements, décoration (gradient de lisibilité, texte, web embarqué, tubes néon) — et des **composites** posés d'un clic : *Marquee* (fanart+gradient+logo), *Score complet*, *Live media*, *Chat Twitch* ;
- **au centre, le canvas** à l'échelle réelle de la surface : glisser, poignée de redimensionnement, guides magnétiques, Suppr, Ctrl+D (dupliquer), Ctrl+Z/Y (annuler/rétablir) — avec les vrais médias d'un jeu d'exemple ;
- **à droite, les calques** (œil pour masquer, cadenas pour verrouiller, ↑↓ pour l'ordre) et l'**inspecteur** : disposition (x, y, largeur, hauteur en fractions — la création survit à tout changement de résolution), contenu (état de visibilité, gabarits `{name}` `{year}`…), style.

Les onglets **Navigation ES / En jeu** en haut filtrent l'édition par état. L'**œil** d'un calque veut dire « affiché dans CET état » : 👁 il l'est, ◌ il est absent de cet état mais présent dans l'autre, — il est éteint partout.

Quatre calques sont **épinglés** et ne se suppriment pas : en haut **Événements animés**, **Lampes** et **Lumière**, tout au fond l'**Image du jeu**. Ils couvrent toute la surface par définition et ne se déplacent donc pas. Éteindre l'un d'eux ne fait pas que le cacher : son moteur **n'est pas construit** et ne consomme plus rien.

Les overlays **score live, timer live et RetroAchievements** sont réservés à l'état **En jeu** : il n'y a ni score ni session à rapporter pendant la navigation. Leur œil ne fait que les allumer ou les éteindre.

!!! tip "Un fanart bien posé"
    Le préréglage Fanart couvre tout le cadre ; le gradient de lisibilité s'insère au-dessus, le logo centré occupe 50 % de la largeur — la recette des marquees générés, éditable.

!!! note "Vidéo live"
    Le composant vidéo peut suivre une chaîne **stream Twitch en direct > YouTube > vidéo locale** : s'il existe un live sur le jeu affiché, il prend la place de la vidéo. Identifiants dans Options → Sources en ligne ; sans clé, la vidéo locale s'affiche simplement.

### Les calques texte

**Texte méta** affiche les données du jeu sélectionné, par gabarit : `{name}`, `{year}`, `{developer}`, `{publisher}`, `{system}`, mais aussi tout ce que porte la fiche — `{desc}`, `{genre}`, `{players}`, `{rating}`.

Le **corps** est sur *auto* par défaut : le texte s'ajuste tout seul à sa zone. C'est ce qui permet au même calque de porter un nom de jeu ou une description de 1 500 caractères sans qu'on ait à le lui dire. Réglé à la main, le corps est une **fraction de la hauteur de la surface** — il garde donc sa taille quand vous redimensionnez la zone.

Les autres réglages (groupe *Style*) : **alignement** gauche / centré / droite / justifié, **position verticale** haut / milieu / bas, et **graisse** grasse ou normale — une description en gras sur 1 500 caractères est un mur.

### Le panneau de contrôle

Le composant **Panneau de contrôle** (palette *Live*) dessine le panneau de votre borne, avec ce que chaque bouton fait dans le jeu **sélectionné** — être sur sa fiche dans ES suffit, rien n'a besoin d'être lancé.

Et surtout : **appuyez sur un bouton de la borne, il s'allume sur le panneau**. C'est une vérification de câblage complète, qui fonctionne **sans LedManager installé**. Si c'est le bouton voisin qui s'allume, le câblage n'est pas celui que votre borne déclare.

Ses options (inspecteur) :

- **Panneau** (groupe *Contenu*) — Joueur 1 à 4. Un composant = un panneau : une borne à deux côtés en pose deux, chacun réglé sur le sien.
- **Aspect** (groupe *Style*) — *Vue de dessus* et *Vue de face 3D* affichent le **dessin réel** d'APIExpose, celui-là même qu'il écrit pour les thèmes EmulationStation ; *Simple* dessine des formes. Sans dessin disponible pour ce jeu, l'aspect retombe sur les formes plutôt que sur un cadre vide.
- **Fond** — aucun, noir, blanc, rouge, jaune ou bleu, avec son **opacité** et sa **marge** au curseur. Le dessin est sur fond transparent : au-dessus d'un fanart chargé, un voile rend les boutons lisibles.

Les boutons que le jeu **n'utilise pas** restent visibles, en transparence : le panneau dit la vérité sur la borne, pas sur le jeu. Ils s'allument quand même à l'appui, en blanc — n'ayant pas de couleur à eux pour répondre.

!!! note "L'allumage"
    Un appui allume une **lampe colorée**, comme celles du moteur de lumière : la couleur du bouton, un halo doux, aucun contour. Elle reste allumée un minimum de temps même sur un appui bref, puis s'éteint en fondu — sinon une rafale de boutons se lirait comme un scintillement.

### Les cartes d'instructions

Beaucoup de bornes affichent la **carte d'instructions** du jeu : les coups spéciaux, les objets, les points bonus. Deux calques la portent, et ils sont séparés à dessein — sur une borne, l'écran tactile et l'écran qui montre la carte sont rarement le même.

**Carte d'instructions** (palette *Cartes d'instructions*) affiche les fiches du jeu sélectionné. Ses options :

- **Joueur** — pour qui cette carte s'affiche. *Tous les joueurs* pour une carte partagée.
- **Rôle affiché** — le rôle est le **dossier** de la fiche dans le média du jeu : un personnage (`cody`), un thème (`items-and-weaponry`), un stage. Laissé vide, le calque parcourt **toutes** les fiches du jeu.
- **Suivre le personnage annoncé par le jeu** — quand le jeu sait dire qui a été choisi, la carte bascule sur ce personnage et tourne dans **ses** fiches uniquement.

**Zone tactile** est le calque qu'on presse. Son rectangle **est** la zone : ce que vous dessinez est ce que le doigt peut toucher. Ses options :

- **Au toucher** — fiche suivante, fiche précédente, revenir à la première, afficher une fiche ou un rôle précis, rôle suivant/précédent, ou suivre/ne plus suivre le jeu.
- **Texte affiché** et **Encadrer la zone** — rien n'est dessiné par défaut : un écran tactile qui marche n'a pas besoin d'être marqué. Ces deux réglages servent aux bornes dont les joueurs ignorent que la zone existe.
- **Retour auto (ms)** — revenir à la fiche de départ après un délai.

!!! note "Le canal, pour relier les deux"
    Une zone pilote la carte qui porte le **même canal**. Le canal se déduit de ce que le calque dit déjà — son joueur, sinon son rôle — donc une borne ordinaire n'a rien à nommer : une carte réglée sur *Joueur 2* et une zone réglée sur *Joueur 2* se répondent. Le champ **Canal** ne sert qu'aux montages libres : un bandeau tactile sur le marquee qui fait défiler des astuces sur le topper, par exemple.

!!! tip "D'où viennent les fiches"
    Du dossier média du jeu, dans `artwork\ic` : `ic.png` à la racine, ou `artwork\ic\<rôle>\ic-1.png` pour une fiche qui appartient à un personnage ou à un thème. Le nom du dossier **est** le rôle.

### Le tableau de scores

Le composant **Hiscores** affiche le classement du jeu courant. Ses options (inspecteur, groupe *Contenu*) :

- **Source** — *Classement local* (les scores captés sur la borne), *NelfePlay (en ligne)* (le **Top 100 mondial** certifié du jeu), ou *Les deux* : les deux tableaux s'affichent **l'un après l'autre** (le mondial d'abord), en n'affichant que celui qui existe.
- **Mon meilleur rang** — sous chaque tableau, une ligne rappelle **votre** position : en local, votre meilleure ligne pour le jeu ; en mondial, votre rang certifié (ou une invitation à vous identifier sur NelfePlay). Le libellé est personnalisable (`{rank} {of} {score} {pseudo}`) et localisé.
- **Lignes par page** — un nombre libre, ou **Dynamique** : le nombre s'ajuste à la place disponible (une marquee large et courte en montre peu, une zone haute davantage). Au-delà de N lignes, le tableau défile page par page.
- **Position** — le tableau se cale en **haut / milieu / bas** de sa zone.
- **Couleur rang/score** — or par défaut, une couleur fixe, ou **Auto** : une couleur vive extraite du logo/marquee du jeu.
- **Titre** — `{name}` (ou simplement `gamename`) = nom du jeu ; le titre est **découplé** de la liste (un titre long ne rapetisse plus le score).
- Un discret **filigrane** *local / mondial* en bas indique le tableau affiché à l'instant t.

## Mires, identification, DMD, tactile

- **Identifier les écrans** affiche un grand numéro sur chaque écran physique.
- **Afficher la mire** remplit l'écran sélectionné d'une grille de réglage.
- **DMD physique…** ouvre le réglage du panneau réel (ZeDMD, Pin2DMD… voir [DMD et ZeDMD](dmd.md)).
- **Tactile (IC card)…** apparaît sur les écrans tactiles : modes simple (un tap = carte suivante), centre→IC2, dual player (moitié gauche joueur 1, moitié droite joueur 2), zones libres dessinées à la souris. La souris déclenche les mêmes actions — pratique pour tester.

!!! note "Nommage des cartes (médias APIExpose)"
    Dans `artwork\ic` d'un jeu : `ic.png` pour une carte unique, ou `ic-1.png`, `ic-2.png`… pour plusieurs cartes. Les suffixes `-left`/`-right` (ex. mercs : `ic-1-left.png` … `ic-5-right.png`) sont les **deux porte-cartes du panel** : côté joueur 1 et côté joueur 2. La navigation passe de carte en carte, et le mode dual player affiche le côté du joueur qui a tapé.

## Sous le capot

Le plan et les surfaces vivent dans `state\surfaces.json` (les positions physiques serviront aux futures animations traversantes). Une configuration `[Screens]` héritée est convertie automatiquement au premier lancement, à comportement identique ; un écran débranché reste dans le plan, grisé, et retrouve ses réglages au rebranchement. Si APIExpose redémarre, chaque flux se reconnecte tout seul après cinq secondes.
