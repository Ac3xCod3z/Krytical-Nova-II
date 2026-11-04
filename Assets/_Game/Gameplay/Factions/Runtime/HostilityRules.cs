namespace KryticalNova.Factions
{
    /// <summary>
    /// Who may attack whom, as plain values (M4, ghosts-design 469): the one rule <c>CombatHostility</c> uses on the old netcode and the
    /// entity netcode's server and clients use with what their ghosts carry (factions, a duel, the map's PvP switches, neutral ground).
    /// </summary>
    public static class HostilityRules
    {
        /// <param name="bothPlayers">Both ends are players; anything else that can take damage (dummies, monsters) is always hostile.</param>
        /// <param name="sameMember">Both ends are one member (never hostile to itself).</param>
        /// <param name="duelling">Either one says it duels the other (<see cref="IFactionMember.IsDuelling"/>).</param>
        /// <param name="neutralGround">Either one stands on neutral ground (a cross-faction Trade Zone): only a duel is a fight there.</param>
        /// <param name="alwaysOnPvp">The map is always-on PvP (The Scar): other factions are hostile whatever the switch says.</param>
        /// <param name="crossFactionPvp">The switch (<see cref="FactionRules.CrossFactionPvp"/>, a GM's gm.pvp).</param>
        public static bool IsHostile(FactionId a, FactionId b, bool bothPlayers, bool sameMember, bool duelling, bool neutralGround,
            bool alwaysOnPvp, bool crossFactionPvp)
        {
            if (!bothPlayers) return true;
            if (sameMember) return false;
            bool otherFactions = a != FactionId.None && b != FactionId.None && a != b;
            if (!neutralGround && alwaysOnPvp && otherFactions) return true;   // The Scar
            if (duelling) return true;
            if (neutralGround) return false;
            return crossFactionPvp && otherFactions;
        }
    }
}
