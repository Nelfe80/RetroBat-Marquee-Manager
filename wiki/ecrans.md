# Écrans et surfaces

![Les surfaces sur la borne](assets/surfaces-borne.svg)

MarqueeManager peut animer **cinq surfaces**, chacune étant une fenêtre WPF placée sur l'écran Windows de votre choix :

| Surface | Usage typique | Flux APIExpose |
|---|---|---|
| `marquee` | Le bandeau lumineux au-dessus de la borne | `/ws/marquee` |
| `topper` | L'écran tout en haut du fronton | `/ws/topper` |
| `iccard` | La carte d'instructions du jeu | `/ws/instruction-card` |
| `dmd` (virtuel) | Un DMD affiché sur un écran classique | `/ws/marquee` (média DMD) |
| `lcd` | Écran d'informations : scores, défis, leaderboards | `/ws/score`, `/ws/timer`, `/ws/retroachievements` |

## Assigner les écrans

!!! tip "Le plus simple : l'assistant"
    [`MarqueeManagerSetup.exe`](assistant.md) fait tout cela visuellement : identification des écrans, affectation des surfaces, test des zones, sans éditer le fichier.

Tout se passe dans `config.ini`, section `[Screens]` : chaque surface reçoit l'**indice de l'écran Windows** qui doit l'afficher.

- `-1` désactive une surface ;
- plusieurs indices séparés par des virgules dupliquent la surface sur plusieurs écrans.

!!! tip "Trouver l'indice d'un écran"
    Le bouton « Identifier les écrans » de l'assistant affiche le bon numéro sur chaque écran. (Dans Windows, Paramètres → Affichage → « Identifier », l'indice MarqueeManager commence généralement à 0 — si le résultat n'est pas celui attendu, essayez le numéro Windows moins un.)

## Ce qui s'affiche, couche par couche

Le média fourni par APIExpose (logo, marquee du jeu, vidéo) reste la **couche de fond**. Par-dessus, MarqueeManager compose des couches natives :

- les vues `.lay` MAME avec leurs lampes pilotées par le flux `/ws/arcade` ;
- les informations persistantes (score RA, mode de jeu) ;
- les notifications temporaires (succès débloqué, défi, résultat de leaderboard).

Sur le **LCD**, les cartes actives se répartissent dans une grille horizontale à colonnes égales ; en mode speedrun, la carte leaderboard devient un bandeau bas pleine largeur pour rester lisible.

## Reconnexions

Si APIExpose redémarre, chaque flux se reconnecte automatiquement après cinq secondes — aucune intervention nécessaire.
