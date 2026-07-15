# Mes jeux

L'onglet **Mes jeux** de MarqueeManagerSetup est l'atelier par jeu : composez votre propre marquee, liez les signaux de jeu à des effets lumière, retouchez les lampes de la scène et épinglez un profil d'éclairage.

![Onglet Mes jeux](assets/setup/setup-games.png)

Choisissez d'abord un jeu : filtrez par système puis tapez quelques lettres dans la recherche (la bibliothèque média d'APIExpose est indexée automatiquement).

## Composer le marquee

Le compositeur assemble un marquee à partir des médias du jeu : fanart, logo (wheel), marquee scrapé, flyer, boîte, capture… Cliquez sur une vignette pour l'ajouter en calque, puis :

- **glisser** pour déplacer, **molette** pour la taille, **Maj+molette** pour la rotation ;
- réglages fins (opacité, miroir, ordre des calques) sous l'aperçu ;
- trois fonds : noir, dégradé sombre ou fanart flouté.

Le canevas est **calé sur la résolution réelle** de votre marquee (zone `MarqueeBounds` si définie, sinon l'écran marquee). À l'enregistrement :

- le PNG aplati part dans `media\marquees\<système>\<rom>.png` — il **remplace le marquee scrapé ou généré** pour ce jeu, en priorité absolue ;
- un fichier projet `.project.json` est posé à côté : rouvrez le jeu et la composition se réédite calque par calque.

« Supprimer ma composition » rend la main au marquee d'origine.

## Effets lumière (signaux du jeu)

Chaque jeu doté d'une définition `.MEM` expose ses **signaux sémantiques** (LOSE_LIFE, BOSS_DEFEATED, COIN_GAIN…). La carte les présente en phrases lisibles :

> **Quand** `HIT` — Player 1 Health decreased **alors** Flash coloré

Cliquez une ligne pour ouvrir l'éditeur : type d'effet (flash, impulsion, teinte, secousse, strobo, extinction, sprites), couleur, durée, sprite animé, anti-rafale… Le bouton **▶ Tester l'effet** rejoue l'effet dans une mini-bande marquee, sans lancer le jeu.

### Portée des réglages

Le sélecteur « Mes réglages s'appliquent à » choisit la couche d'écriture :

| Portée | Fichier |
|---|---|
| ce jeu uniquement | `overrides\effects\<système>\<rom>.json` |
| tout le système | `overrides\effects\<système>.json` |
| tous les jeux du genre | `overrides\effects\genres\<slug>.json` |

Le runtime résout dans l'ordre **jeu → système → genre → défauts genrés → défauts génériques**, et recharge les couches à chaque changement de jeu. « Désactiver ce signal » rend un signal muet sans toucher aux autres.

### Le genre pilote le style

Le genre scrapé du jeu (shmup, beat'em up, racing…) est normalisé par `resources\lighting\genres.map.xml` : un `HIT` dans un shmup fait exploser des bombes sur le marquee, le même `HIT` dans un beat'em up projette une gerbe d'impact. Les règles genrées vivent dans `resources\lighting\ingame.effects.xml` (attribut `genre=`) et restent éditables.

### Moniteur live

Démarrez l'écoute, lancez le jeu et jouez : les signaux qui tirent défilent en direct (nom, famille, heure). Cliquez-en un pour régler immédiatement son effet — c'est la façon la plus naturelle de découvrir ce qu'un jeu émet.

## Scène & lampes

Pour les jeux arcade, la scène lumineuse (`resources\rbmarquee\<rom>.xml`) place des **lampes pilotées par les outputs du jeu** (gyrophares d'APB, lampes de Chase H.Q.…). L'éditeur affiche le marquee en fond :

- glissez une lampe, redimensionnez à la molette ;
- couleur et **câblage output** (liste des outputs connus du jeu) sous l'aperçu ;
- mode attract : aucun, chenillard ou alterné.

L'enregistrement marque la scène `generated="false"` : elle est **curée**, le générateur automatique ne l'écrasera plus (une sauvegarde `.bak` est conservée).

## Profil d'éclairage

Par défaut le moteur lumineux choisit l'ampoule d'époque via sa grammaire (année, constructeur, éditeur…). Vous pouvez **épingler** pour un jeu précis une ampoule de la bibliothèque (39 profils : tubes fluo, incandescence, néon, LED…) et/ou un profil de borne. Le choix est stocké avec les effets du jeu et prime sur la grammaire.
