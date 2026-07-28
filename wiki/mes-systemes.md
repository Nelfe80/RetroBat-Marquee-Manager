# Mes systèmes

**Mes systèmes** décide, système par système, **quelle source s'affiche** quand un système est sélectionné dans ES. On ne réordonne plus une liste : chaque source est une **carte**, du plus général au plus précis, et on **clique la carte** qu'on veut utiliser.

![Vue Mes systèmes](assets/setup/setup-systems.png)

## Système & surface

Choisissez le **système** (seuls ceux avec des jeux installés apparaissent ; mame, fbneo… gardent leurs créations propres) et la **surface** sur la même ligne. Les surfaces **suspendues** (dont l'écran est exclu de MarqueeManager) sont masquées par défaut — une case « Afficher les surfaces suspendues » les réaffiche.

## Les cartes de résolution

Sous les sélecteurs, une carte par source, **du plus général au plus précis** :

- **Gabarit général — tous les systèmes** : la mise en page générique (voir plus bas), rendue avec les médias du système courant.
- **Ma création pour ce système** : votre composition dédiée, avec **Composer / Modifier** et **Supprimer**.
- **Mon dossier médias** : un fichier brut que vous déposez (voir plus bas).
- **Marquee scrapé** puis **Logo mis en page** : les sources automatiques.

Chaque carte a son **propre aperçu** (grisé « aucun média pour cette source » quand elle est vide, donc non sélectionnable). **Cliquer une carte** l'active pour ce système + cette surface ; la **coche verte ✓** marque celle qui s'affiche réellement. L'ordre de priorité par défaut est fixe (création > dossier > gabarit > scrapé > logo) ; cliquer une carte force celle-ci et désactive celles au-dessus.

Le **fanart du système** utilisé par les compositions vient du thème ES actif (carbon en fournit pour presque tous les systèmes).

## Le gabarit général

Le **gabarit** est une mise en page générique (fanart + gradient + logo, par exemple) composée **une fois** et appliquée à **tous les systèmes** : chaque calque est repéré par son type (fanart, logo…) et résout, au rendu, le média du système courant. **Modifier le gabarit général** ouvre le même compositeur que « Composer », avec des calques génériques.

**Pré-générer pour tous les systèmes** rend le gabarit pour chaque système d'un coup (navigation ES instantanée) ; sinon le rendu se fait à la première visite du système.

## Mon dossier médias

Déposez un fichier (PNG/JPG) pour un système : le bouton **Ouvrir le dossier** de la carte crée et ouvre l'emplacement exact, même si rien n'est encore sélectionné. Au niveau **système**, le fichier prend le **nom du système tel qu'affiché dans ES** — par exemple `media\marquees\user\systems\mame.png` (et non `arcade.png`). Dès qu'un fichier est présent, la carte « Mon dossier médias » devient sélectionnable et passe devant le gabarit et le scrapé.
