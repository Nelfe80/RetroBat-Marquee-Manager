# Mes jeux

**Mes jeux** est la fiche complète d'un jeu : sa composition de marquee, ses médias en ligne, ses **effets lumière** pilotés par les signaux du jeu, ses lampes et son profil d'éclairage. La recherche accepte le nom du jeu, le nom de la rom ou n'importe quel alias.

![Vue Mes jeux](assets/setup/setup-games.png)

## Compositeur

Des calques depuis les médias du jeu, sur un canvas calé sur la résolution réelle du marquee. Un logo inséré occupe **50 % de la largeur** par défaut ; « Gabarit auto » pose fanart + gradient + logo en un clic. La composition enregistrée prend le dessus sur toutes les autres sources ([Mes systèmes](mes-systemes.md)).

## Récupérer des médias en ligne

Arcade Database (sans clé), SteamGridDB, TheGamesDB — clés dans Options → Sources en ligne. Un clic sur un résultat le télécharge et l'ajoute en calque.

??? note "Et ScreenScraper ?"
    La source ScreenScraper n'apparaît que si les identifiants **développeur** sont disponibles (ils ne sont jamais distribués dans le code) ; votre compte **utilisateur** ScreenScraper est repris automatiquement d'EmulationStation, ou saisi dans Options. Au quotidien, APIExpose scrape déjà ScreenScraper localement — cette source directe est un complément à la demande, décochée par défaut.

## Effets lumière : « Quand [signal] alors [effet] »

Les jeux équipés d'une définition `.MEM` émettent des **signaux sémantiques** (HIT, LOSE_LIFE, BOSS_DEFEATED…). Chaque signal se lit comme une phrase : *Quand HIT alors flash rouge*. Les réglages s'appliquent au jeu, à tout son système ou à tout son **genre** (un HIT dans un shmup n'est pas un HIT dans un beat'em up) ; un badge indique toujours d'où vient la règle qui gagne.

### Mes effets — le compositeur d'effets

**Mes effets…** ouvre la bibliothèque : un effet est une **pile d'actions ordonnancées**, chacune avec son type (voile coloré, flash, secousse, strobe, nuée de sprites, **votre média webm/gif**), ses paramètres, son **départ** (ms) et sa durée. Deux actions à départ 0 jouent ensemble (« voile rouge + secousse + explosions ») ; des départs échelonnés font une séquence (« flash rouge PUIS nuée de sprites »). La préview rejoue la séquence complète ; la bibliothèque est un simple fichier (`media\effects\library.json`), exportable.

Déposez vos animations (webm transparent, gif, apng) dans `media\effects\user\` : une action « Mon média » les joue en **surimpression** du marquee composé, ou en **plein écran** temporaire. Les tubes néon du Lighting Engine restent vivants derrière.

### Allouer et débrayer

Sur chaque signal du jeu : **Effet simple** (flash, sprite… comme avant) ou **Un de mes effets** (la séquence nommée). Et pour chaque jeu, une politique à trois positions :

- **Hériter** — défauts genre/système + vos réglages (comportement normal) ;
- **Uniquement mes effets** — tous les défauts coupés, seuls les signaux que vous avez alloués réagissent ;
- **Tout désactiver** — aucun effet MEM sur ce jeu.

### Moniteur live

Lancez le jeu et jouez : les signaux qui tirent s'affichent en direct ; cliquez-en un pour régler son effet. C'est le moyen le plus simple de découvrir ce qu'un jeu sait émettre.

## Scène, lampes et éclairage

- **Scène & lampes** : l'éditeur rbmarquee (lampes des layouts MAME pilotées par `/ws/arcade`).
- **Profil d'éclairage** : ampoules et meuble du Lighting Engine, par jeu.
