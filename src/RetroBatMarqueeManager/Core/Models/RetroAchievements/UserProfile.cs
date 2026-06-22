using System.Text.Json.Serialization;
using RetroBatMarqueeManager.Infrastructure.Api;

namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: RetroAchievements user profile information
    /// FR: Informations de profil utilisateur RetroAchievements
    /// </summary>
    public class UserProfile
    {
        /// <summary>
        /// EN: Username
        /// FR: Nom d'utilisateur
        /// </summary>
        public string User { get; set; } = string.Empty;

        /// <summary>
        /// EN: User profile picture path (relative)
        /// FR: Chemin image de profil (relative)
        /// </summary>
        public string UserPic { get; set; } = string.Empty;

        /// <summary>
        /// EN: Total achievement points earned
        /// FR: Total des points de succès gagnés
        /// </summary>
        public int TotalPoints { get; set; }

        /// <summary>
        /// EN: Total ranked achievements
        /// FR: Total des succès classés
        /// </summary>
        public int TotalRanked { get; set; }

        /// <summary>
        /// EN: User status (e.g., "Online", "Offline")
        /// FR: Statut utilisateur (ex: "En ligne", "Hors ligne")
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// EN: Member since date
        /// FR: Membre depuis
        /// </summary>
        [JsonConverter(typeof(FlexibleDateTimeNonNullableConverter))]
        public DateTime MemberSince { get; set; }
    }
}
