namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: User progress for a specific game
    /// FR: Progression utilisateur pour un jeu spécifique
    /// </summary>
    public class UserProgress
    {
        /// <summary>
        /// EN: Game ID
        /// FR: ID du jeu
        /// </summary>
        public int GameID { get; set; }

        /// <summary>
        /// EN: Number of achievements earned by user
        /// FR: Nombre de succès obtenus par l'utilisateur
        /// </summary>
        public int NumAwardedToUser { get; set; }

        /// <summary>
        /// EN: Number of achievements earned in hardcore mode
        /// FR: Nombre de succès obtenus en mode hardcore
        /// </summary>
        public int NumAwardedToUserHardcore { get; set; }

        /// <summary>
        /// EN: User completion percentage (e.g., "75%")
        /// FR: Pourcentage de complétion (ex: "75%")
        /// </summary>
        public string UserCompletion { get; set; } = "0%";

        /// <summary>
        /// EN: User hardcore completion percentage
        /// FR: Pourcentage de complétion hardcore
        /// </summary>
        public string UserCompletionHardcore { get; set; } = "0%";

        /// <summary>
        /// EN: List of all achievements for this game
        /// FR: Liste de tous les succès pour ce jeu
        /// </summary>
        public Dictionary<string, Achievement> Achievements { get; set; } = new();

        /// <summary>
        /// EN: List of all leaderboards for this game
        /// FR: Liste de tous les leaderboards pour ce jeu
        /// </summary>
        public List<Leaderboard> Leaderboards { get; set; } = new();

        /// <summary>
        /// EN: Game information
        /// FR: Informations du jeu
        /// </summary>
        public GameInfo? GameInfo { get; set; }
    }
}
