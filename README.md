# RetroBat MarqueeManager

**MarqueeManager** est un futur module d'affichage dynamique conçu pour s'interfacer avec **APIExpose** pour RetroBat. Son but est de piloter un second écran (Marquee / DMD / Topper / Dalle LED ou LCD) placé sur votre borne d'arcade ou setup de jeu, afin d'afficher le logo, l'image de marquee, des GIFs animés ou des vidéos spécifiques au jeu actif.

> [!NOTE]
> Ce projet est actuellement **en cours de préparation / développement** et sera disponible prochainement.

---

## ⚠️ Licences et Protection (IMPORTANT)

Ce projet et ses fichiers associés sont protégés par le modèle de licence APIExpose :
1.  **Logiciel / Code Source** : Distribué sous licence **personnelle et non-commerciale** (voir `LICENSE.md` et `PERSONAL-LICENSE.md`). L'utilisation commerciale, l'intégration payante ou la revente matérielle/logicielle sans accord de licence commerciale écrit préalable est strictement interdite (voir `COMMERCIAL-LICENSE.md`).
2.  **Pack de Données d'Affichage** : Les configurations et les éléments graphiques associés seront protégés par la licence **`DATA-LICENSE.md`**.

---

## 🔮 Fonctionnalités Prévues

*   **Affichage Dynamique du Jeu Actif** : Changement automatique de l'affichage de marquee dès qu'un jeu est sélectionné ou lancé dans RetroBat/EmulationStation.
*   **Support Multi-Format** : Gestion des images statiques (`.png`, `.jpg`), des animations légères (`.gif` pour DMD) et des flux vidéo de démonstration.
*   **Communication WebSocket** : Abonnement en temps réel au flux WebSocket d'APIExpose pour une réactivité instantanée.
*   **Intégration d'Écrans Secondaires LCD/LED** : Prise en charge des configurations matérielles d'affichage les plus courantes.
