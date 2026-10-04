using UnityEngine;

namespace KryticalNova.Factions
{
    /// <summary>
    /// Who is an enemy of whom. Players of different factions are hostile while cross-faction PvP is on (the faction
    /// catalog's default, which a GM can switch at runtime); players of the same faction are friendly. A GM can also
    /// pin one player to friendly or hostile (<see cref="PvpStance"/>), which wins over the faction rule.
    /// </summary>
    public static class FactionRules
    {
        private static bool configured;

        /// <summary>
        /// The live switch. The server (or the offline game) owns it and replicates it; change it through
        /// PlayerIdentity.SetCrossFactionPvp so every peer agrees.
        /// </summary>
        public static bool CrossFactionPvp { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CrossFactionPvp = false;
            configured = false;
        }

        /// <summary>
        /// Takes the catalog's default once per session. Later calls change nothing, so a switch made by a GM is not
        /// undone when another player loads.
        /// </summary>
        public static void Configure(bool crossFactionPvpDefault)
        {
            if (configured) return;
            configured = true;
            CrossFactionPvp = crossFactionPvpDefault;
        }

        /// <summary>The faction rule alone.</summary>
        public static bool AreHostile(FactionId a, FactionId b) =>
            CrossFactionPvp && a != FactionId.None && b != FactionId.None && a != b;

        /// <summary>The full rule between two members: a friendly pin wins, then a hostile pin, then the factions.</summary>
        public static bool AreHostile(IFactionMember a, IFactionMember b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            if (a.Stance == PvpStance.Friendly || b.Stance == PvpStance.Friendly) return false;
            if (a.Stance == PvpStance.Hostile || b.Stance == PvpStance.Hostile) return true;
            return AreHostile(a.Faction, b.Faction);
        }

        public static FactionMask MaskOf(FactionId id) => id == FactionId.None ? FactionMask.None : (FactionMask)(1 << ((int)id - 1));

        public static bool Includes(FactionMask mask, FactionId id) => (mask & MaskOf(id)) != 0;

        public static IFactionMember MemberOf(Transform t) => t != null ? t.GetComponentInParent<IFactionMember>() : null;

        public static FactionId Of(Transform t)
        {
            var member = MemberOf(t);
            return member != null ? member.Faction : FactionId.None;
        }
    }
}
