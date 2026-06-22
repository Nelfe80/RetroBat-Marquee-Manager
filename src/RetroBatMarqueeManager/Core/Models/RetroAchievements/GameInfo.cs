namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: RetroAchievements game information
    /// FR: Informations de jeu RetroAchievements
    /// </summary>
    public class GameInfo
    {
        /// <summary>
        /// EN: Game ID on RetroAchievements
        /// FR: ID du jeu sur RetroAchievements
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// EN: Game title
        /// FR: Titre du jeu
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// EN: Console/System ID
        /// FR: ID Console/Système
        /// </summary>
        public int ConsoleID { get; set; }

        /// <summary>
        /// EN: Console name (e.g., "Mega Drive", "NES")
        /// FR: Nom de la console (ex: "Mega Drive", "NES")
        /// </summary>
        public string ConsoleName { get; set; } = string.Empty;

        /// <summary>
        /// EN: Game icon path (relative)
        /// FR: Chemin icône du jeu (relative)
        /// </summary>
        public string GameIcon { get; set; } = string.Empty;

        /// <summary>
        /// EN: Image icon path
        /// FR: Chemin icône image
        /// </summary>
        public string ImageIcon { get; set; } = string.Empty;

        /// <summary>
        /// EN: Title screen image path
        /// FR: Chemin image écran titre
        /// </summary>
        public string ImageTitle { get; set; } = string.Empty;

        /// <summary>
        /// EN: In-game screenshot path
        /// FR: Chemin capture d'écran en jeu
        /// </summary>
        public string ImageIngame { get; set; } = string.Empty;

        /// <summary>
        /// EN: Box art image path
        /// FR: Chemin image jaquette
        /// </summary>
        public string ImageBoxArt { get; set; } = string.Empty;

        /// <summary>
        /// EN: Publisher name
        /// FR: Nom de l'éditeur
        /// </summary>
        public string Publisher { get; set; } = string.Empty;

        /// <summary>
        /// EN: Developer name
        /// FR: Nom du développeur
        /// </summary>
        public string Developer { get; set; } = string.Empty;

        /// <summary>
        /// EN: Genre
        /// FR: Genre
        /// </summary>
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// EN: Release date
        /// FR: Date de sortie
        /// </summary>
        public string Released { get; set; } = string.Empty;
    }
}
