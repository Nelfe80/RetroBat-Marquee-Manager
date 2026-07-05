MarqueeManager tools / Outils MarqueeManager
============================================

EN
--
This directory contains local runtime/helper tools for optional DMD and ZeDMD
support. Its contents are intentionally not tracked by Git, except for this
README.

Expected local layout when DMD features are used:

- dmd/
- zedmd/

Each runtime folder has its own README.txt with the concrete restore steps for
that component.

Installation:

1. Restore the dmd and zedmd folders from the local release package or your
   hardware/vendor bundle.
2. Place them under MarqueeManager/tools with the names above.
3. Keep native DLLs, dmdext binaries, hardware libraries, and local logs in this
   folder; Git will ignore them.
4. If DMD support is disabled, this folder can contain only this README.

FR
--
Ce dossier contient les outils locaux necessaires au support optionnel DMD et
ZeDMD. Son contenu n'est volontairement pas suivi par Git, sauf ce README.

Structure locale attendue lorsque les fonctions DMD sont utilisees :

- dmd/
- zedmd/

Chaque dossier runtime a son propre README.txt avec les etapes de restauration
concretes du composant.

Installation :

1. Restaurer les dossiers dmd et zedmd depuis le package de release local ou le
   bundle materiel/fournisseur.
2. Les placer dans MarqueeManager/tools avec les noms indiques ci-dessus.
3. Garder dans ce dossier les DLL natives, binaires dmdext, bibliotheques
   materiel et logs locaux ; Git les ignorera.
4. Si le support DMD est desactive, ce dossier peut ne contenir que ce README.
