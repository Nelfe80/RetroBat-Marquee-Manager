# Mes composants

L'onglet **Mes composants** est la bibliothèque de compositions de MarqueeManagerSetup : des templates automatiques, des **priorités de sources par système**, votre **dossier de médias personnels**, et les compositions originales par jeu ou par système — avec toujours les effets lumière, les lampes et les profils d'éclairage.

![Onglet Mes composants](assets/setup/setup-games.png)

## Templates de composition

Quatre gabarits automatiques reprennent la recette d'APIExpose (fanart en fond, gradient noir ou blanc selon la luminance, logo) : trois horizontaux aux proportions des marquees générés — **1920×360, 1280×400, 920×360** — et un vertical **1080×1920**. Affectez un template à un système dans les priorités : chaque jeu reçoit sa composition, rendue en tâche de fond au premier affichage puis mise en cache.

!!! tip "Navigation ES instantanée"
    **Pré-générer ce système** (ou tous) rend d'avance toutes les compositions templatées : plus aucune attente à la sélection d'un jeu.

## Priorités par système

Pour chaque **catégorie** (marquee, topper, DMD) puis chaque système, une chaîne ordonnée de sources : le runtime affiche la **première disponible**. Sources : composition manuelle, mon dossier, template, marquee scrapé, screen-marquee, généré APIExpose, logo, fanart… (et pour le DMD : vos GIF animés, les dmd*.gif du pack, dmd.png).

Exemple typique pour arcade : *composition > mon dossier > marquee scrapé > généré* — vos images d'abord, le scrapé ensuite, l'auto-généré en dernier recours. Et si un jeu ne vous plaît pas, sa composition manuelle le surclasse individuellement.

### Mon dossier

Déposez vos propres **images ou vidéos** dans `media\marquees\user\<système>\` (idem `media\toppers\user\`, `media\dmd\user\` — vos GIF animés DMD y sont cyclés). Les noms de fichiers sont **résolus par alias** via l'index gamelist d'APIExpose : `Metal Slug (World).png`, `metalslug.png` ou le nom de set exact désignent tous le même jeu. Le glisser-déposer directement sur la carte (ou sur la fiche d'un jeu) copie et renomme automatiquement.

**Tester la chaîne** affiche pour quelques jeux du système la source qui gagne — la chaîne n'est jamais une boîte noire.

## Compositions originales

La recherche (nom de jeu, nom de rom ou alias) ouvre la fiche du jeu :

- **Compositeur** : calques depuis les médias du jeu, canvas calé sur la résolution réelle du marquee. Un logo inséré occupe **50 % de la largeur** par défaut ; le bouton « Gabarit auto » pose fanart + logo en un clic.
- **Récupérer des médias en ligne** : Arcade Database (sans clé), SteamGridDB, TheGamesDB — et ScreenScraper en secours (APIExpose le scrape déjà). Clés dans Options → Sources en ligne. Un clic sur un résultat le télécharge et l'ajoute en calque.
- **Effets lumière** (signaux .MEM), **scène & lampes** rbmarquee et **profil d'éclairage** : inchangés, voir les sections dédiées.

Les compositions par SYSTÈME (sélection d'un système dans ES) s'enregistrent dans `media\marquees\systems\<système>.png`.

## Vidéo live sur une surface

Le composant vidéo d'une surface peut suivre une chaîne **stream Twitch en direct > YouTube > vidéo locale** : s'il existe un live sur le jeu affiché, il prend la place de la vidéo. Identifiants Twitch/YouTube dans Options → Sources en ligne ; sans clé, la vidéo locale s'affiche simplement.
