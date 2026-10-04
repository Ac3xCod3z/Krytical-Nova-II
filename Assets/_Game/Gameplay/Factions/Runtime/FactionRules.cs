using UnityEngine;

namespace KryticalNova.Factions
{
    /// <summary>
    /// Who is an enemy of whom. While every faction shares the starter town the switch is off and all players are
    /// friendly; turning it on (<see cref="FactionCatalog.crossFactionPvp"/>) makes different factions hostile.
    /// </summary>
    public static class FactionRules
    {
        /// <summary>Pushed from the faction catalog when a player loads. Off by default.</summary>
        public static bool CrossFactionPvp { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => CrossFactionPvp = false;

        public static bool AreHostile(FactionId a, FactionId b) =>
            CrossFactionPvp && a != FactionId.None && b != FactionId.None && a != b;

        public static FactionMask MaskOf(FactionId id) => id == FactionId.None ? FactionMask.None : (FactionMask)(1 << ((int)id - 1));

        public static bool Includes(FactionMask mask, FactionId id) => (mask & MaskOf(id)) != 0;

        public static FactionId Of(Transform t)
        {
            if (t == null) return FactionId.None;
            var member = t.GetComponentInParent<IFactionMember>();
            return member != null ? member.Faction : FactionId.None;
        }
    }
}
