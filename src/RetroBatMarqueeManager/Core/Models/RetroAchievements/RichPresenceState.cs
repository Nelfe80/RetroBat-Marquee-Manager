namespace RetroBatMarqueeManager.Core.Models.RetroAchievements
{
    /// <summary>
    /// EN: Model for structured Rich Presence data
    /// FR: Modèle pour les données de Rich Presence structurées
    /// </summary>
    public class RichPresenceState
    {
        /// <summary>
        /// EN: Narrative portion of the Rich Presence (Stage, Location, Area)
        /// FR: Portion narrative de la Rich Presence (Niveau, Lieu, Zone)
        /// </summary>
        public string Narrative { get; set; } = string.Empty;

        /// <summary>
        /// EN: Dictionary of high-frequency stats (Lives, Score, Weapon, etc.)
        /// FR: Dictionnaire des statistiques à haute fréquence (Vies, Score, Arme, etc.)
        /// </summary>
        public Dictionary<string, string> Stats { get; set; } = new();

        /// <summary>
        /// EN: Original full string
        /// FR: Chaîne complète originale
        /// </summary>
        public string RawText { get; set; } = string.Empty;

        /// <summary>
        /// EN: Check if the narrative has changed compared to another state
        /// FR: Vérifier si la narration a changé par rapport à un autre état
        /// </summary>
        public bool IsNarrativeChanged(RichPresenceState? other)
        {
            if (other == null) return true;
            return Narrative != other.Narrative;
        }

        /// <summary>
        /// EN: Check if any of the high-frequency stats have changed
        /// FR: Vérifier si l'une des statistiques à haute fréquence a changé
        /// </summary>
        public bool IsStatsChanged(RichPresenceState? other)
        {
            if (other == null) return Stats.Count > 0;
            if (Stats.Count != other.Stats.Count) return true;

            foreach (var kvp in Stats)
            {
                if (!other.Stats.TryGetValue(kvp.Key, out var otherVal) || kvp.Value != otherVal)
                    return true;
            }

            return false;
        }
    }
}
