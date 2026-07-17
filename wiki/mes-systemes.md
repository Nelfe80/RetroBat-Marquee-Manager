# Mes systèmes

**Mes systèmes** décide, système par système, **quoi afficher et dans quel ordre** : vos créations graphiques, vos médias, le scrapé, le généré — et le template automatique qui fabrique une création pour chaque jeu.

![Vue Mes systèmes](assets/setup/setup-systems.png)

## Mon marquee

Le marquee affiché quand un **système** est sélectionné dans ES. Choisissez le système (seuls ceux avec des jeux installés apparaissent ; mame et fbneo gardent leurs créations propres) : l'aperçu du marquee actuel, le **sélecteur de surface** (chaque création est propre à une surface), « **Ouvrir l'interface de création graphique** » et la **suppression de la création de cette surface** apparaissent alors. Le **fanart du système** vient du thème ES actif (carbon en fournit pour presque tous les systèmes).

## Priorités par système

Pour chaque **catégorie** (marquee, topper, DMD) puis chaque système, une chaîne ordonnée de sources : le runtime affiche la **première disponible**. Sources : ma création graphique, mon dossier, template, marquee scrapé, screen-marquee, généré APIExpose, logo, fanart… (DMD : vos GIF animés, `dmd*.gif`, `dmd.png`).

Exemple pour arcade : *ma création > mon dossier > marquee scrapé > généré*. **Tester la chaîne** affiche, juste en dessous, la source qui gagne sur un échantillon (fonctionne aussi en Global).

## Mon dossier

Déposez ici vos médias (images PNG/JPG ou vidéos MP4) : un fichier par jeu, nommé comme la rom (« mslug.png »), le titre (« Metal Slug (World).png ») ou n'importe quel alias — le nom se résout automatiquement. Ils passent devant les autres sources dès que « Mon dossier » est dans la chaîne. Glisser-déposer directement sur la carte fonctionne.

## Pré-génération des templates

Un « Template » est une création automatique (fanart + gradient selon la luminance + logo) rendue pour **chaque jeu** du système, aux formats 1920×360, 1280×400, 920×360 ou vertical 1080×1920. Ajoutez « Template … » dans la chaîne pour l'utiliser ; le rendu se fait au premier affichage, ou d'avance avec **Pré-générer** pour une navigation ES instantanée (`MarqueeManager.exe --render-templates <système|all>`).
