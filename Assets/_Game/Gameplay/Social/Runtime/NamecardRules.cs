using KryticalNova.Factions;

namespace KryticalNova.Social
{
    /// <summary>
    /// Who sees whose namecard (the name, level, Crew and Standing shown over a player's head). Free of the scene,
    /// so it can be tested: the caller says where the other player stands.
    /// </summary>
    public static class NamecardRules
    {
        /// <param name="isSelf">The namecard is the viewer's own.</param>
        /// <param name="showOwn">The viewer's setting for their own namecard.</param>
        /// <param name="viewer">The faction of the player looking.</param>
        /// <param name="subject">The faction of the player whose namecard it is.</param>
        /// <param name="subjectOnNeutralGround">That player stands where factions may deal with each other.</param>
        public static bool Visible(bool isSelf, bool showOwn, FactionId viewer, FactionId subject, bool subjectOnNeutralGround)
        {
            if (isSelf) return showOwn;
            if (viewer == subject) return true;   // your own faction, always
            return subjectOnNeutralGround;         // another faction, only on neutral ground
        }
    }
}
