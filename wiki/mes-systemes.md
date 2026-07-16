# Mes systèmes

**Mes systèmes** décide, système par système, **quoi afficher et dans quel ordre** : vos médias, le scrapé, le généré — et le template automatique qui fabrique une composition pour chaque jeu.

![Vue Mes systèmes](assets/setup/setup-systems.png)

## Priorités par système

Pour chaque **catégorie** (marquee, topper, DMD) puis chaque système, une chaîne ordonnée de sources : le runtime affiche la **première disponible**. Sources : composition manuelle, mon dossier, template, marquee scrapé, screen-marquee, généré APIExpose, logo, fanart… (et pour le DMD : vos GIF animés, les `dmd*.gif` du pack, `dmd.png`).

Exemple typique pour arcade : *composition > mon dossier > marquee scrapé > généré* — vos images d'abord, le scrapé ensuite, l'auto-généré en dernier recours. Et si un jeu ne vous plaît pas, sa composition manuelle (fiche du jeu) le surclasse individuellement.

**Tester la chaîne** affiche pour quelques jeux du système la source qui gagne — la chaîne n'est jamais une boîte noire.

## Mon dossier

Déposez vos propres **images ou vidéos** dans `media\marquees\user\<système>\` (idem `media\toppers\user\`, `media\dmd\user\` — vos GIF animés DMD y sont cyclés). Les noms de fichiers sont **résolus par alias** via l'index gamelist d'APIExpose : `Metal Slug (World).png`, `metalslug.png` ou le nom de set exact désignent tous le même jeu. Le glisser-déposer directement sur la carte (ou sur la fiche d'un jeu) copie et renomme automatiquement.

## Templates de composition

Quatre gabarits automatiques reprennent la recette d'APIExpose (fanart en fond, gradient noir ou blanc selon la luminance, logo) : trois horizontaux aux proportions des marquees générés — **1920×360, 1280×400, 920×360** — et un vertical **1080×1920**. Affectez un template à un système dans les priorités : chaque jeu reçoit sa composition, rendue en tâche de fond au premier affichage puis mise en cache.

!!! tip "Navigation ES instantanée"
    **Pré-générer ce système** (ou tous) rend d'avance toutes les compositions templatées : plus aucune attente à la sélection d'un jeu. En ligne de commande : `MarqueeManager.exe --render-templates arcade` (ou `all`).

## Compositions par système

La sélection d'un **système** dans ES bascule automatiquement les composants média sur les médias du système (logo et fanart du thème) — la même composition sert au jeu et au système. Pour un rendu système sur mesure, une composition manuelle s'enregistre dans `media\marquees\systems\<système>.png`.
