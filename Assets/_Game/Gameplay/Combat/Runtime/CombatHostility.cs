using KryticalNova.Factions;
using KryticalNova.Progression;
using KryticalNova.World;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Who may attack whom. Anything that is not a player and can take damage (dummies, monsters) is hostile.
    /// Between two players the factions decide (<see cref="FactionRules"/>): players of different factions are
    /// hostile while cross-faction PvP is on, players of the same faction are friendly, and two players in a duel
    /// (<see cref="PlayerDuel"/>) are hostile to each other. On neutral ground (a <see cref="TradeZone"/> marked so)
    /// the factions are at peace and only a duel is a fight; it is enough that one of the two stands on it, so
    /// nobody is struck from outside. The zone is a scene object, the same on every machine, so every peer agrees. On a
    /// map tagged always-on PvP (<see cref="ZoneRules.AlwaysOnPvp"/>, The Scar) other factions are hostile even with
    /// cross-faction PvP off.
    /// </summary>
    public static class CombatHostility
    {
        public static bool IsPlayer(Transform t) => t != null && t.GetComponentInParent<PlayerResources>() != null;

        // The player's body asks this through HostilityCheck (Player), which sits below Combat.
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void FillHostilityCheck() => KryticalNova.Player.HostilityCheck.Rule = IsHostile;

        public static bool IsHostile(Transform attacker, IDamageable target)
        {
            if (target == null || target.Transform == null || target.Transform == attacker) return false;
            if (!(IsPlayer(attacker) && IsPlayer(target.Transform))) return true;
            bool neutralGround = TradeZone.CrossFactionAt(attacker.position) || TradeZone.CrossFactionAt(target.Transform.position);
            IFactionMember a = FactionRules.MemberOf(attacker), b = FactionRules.MemberOf(target.Transform);
            // The Scar: the factions are at war there whatever cross-faction PvP is set to.
            if (!neutralGround && ZoneRules.AlwaysOnPvp && a != null && b != null && !ReferenceEquals(a, b)
                && a.Faction != FactionId.None && b.Faction != FactionId.None && a.Faction != b.Faction)
                return true;
            return FactionRules.AreHostile(a, b, neutralGround);
        }
    }
}
