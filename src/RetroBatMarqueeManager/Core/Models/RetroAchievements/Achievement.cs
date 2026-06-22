using System.Text.Json.Serialization;
using RetroBatMarqueeManager.Infrastructure.Api;

namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: Individual achievement data
    /// FR: Données d'un succès individuel
    /// </summary>
    public class Achievement
    {
        /// <summary>
        /// EN: Achievement ID
        /// FR: ID du succès
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// EN: Achievement title
        /// FR: Titre du succès
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// EN: Achievement description
        /// FR: Description du succès
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// EN: Points awarded
        /// FR: Points attribués
        /// </summary>
        public int Points { get; set; }

        /// <summary>
        /// EN: True ratio (weighted points)
        /// FR: Ratio réel (points pondérés)
        /// </summary>
        public int TrueRatio { get; set; }

        /// <summary>
        /// EN: Badge filename (without extension)
        /// FR: Nom fichier badge (sans extension)
        /// </summary>
        public string BadgeName { get; set; } = string.Empty;

        /// <summary>
        /// EN: Badge locked filename (for unearned achievements)
        /// FR: Nom fichier badge verrouillé (pour succès non obtenus)
        /// </summary>
        public string BadgeLockedName { get; set; } = string.Empty;

        /// <summary>
        /// EN: Display order in achievement list
        /// FR: Ordre d'affichage dans la liste
        /// </summary>
        public int DisplayOrder { get; set; }

        /// <summary>
        /// EN: Number of users who earned this achievement
        /// FR: Nombre d'utilisateurs ayant obtenu ce succès
        /// </summary>
        public int NumAwarded { get; set; }

        /// <summary>
        /// EN: Number of users who earned this in hardcore mode
        /// FR: Nombre d'utilisateurs l'ayant obtenu en mode hardcore
        /// </summary>
        public int NumAwardedHardcore { get; set; }

        /// <summary>
        /// EN: Whether current user unlocked this achievement
        /// FR: Si l'utilisateur actuel a débloqué ce succès
        /// </summary>
        public bool Unlocked { get; set; }

        /// <summary>
        /// EN: Date earned (normal mode)
        /// FR: Date d'obtention (mode normal)
        /// </summary>
        [JsonConverter(typeof(FlexibleDateTimeConverter))]
        public DateTime? DateEarned { get; set; }

        /// <summary>
        /// EN: Date earned in hardcore mode
        /// FR: Date d'obtention en mode hardcore
        /// </summary>
        [JsonConverter(typeof(FlexibleDateTimeConverter))]
        public DateTime? DateEarnedHardcore { get; set; }

        /// <summary>
        /// EN: Achievement type (e.g., "progression", "win_condition")
        /// FR: Type de succès (ex: "progression", "win_condition")
        /// </summary>
        public string Type { get; set; } = string.Empty;
    }
}
