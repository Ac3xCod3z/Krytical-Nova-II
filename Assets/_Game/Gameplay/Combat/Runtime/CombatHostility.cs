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
            // A stand-in (the Mount Companion's body, day list item 19) is judged as the player it stands for: an enemy may hit it, a
            // friend or its own owner never.
            Transform victim = target.Transform;
            if (victim.TryGetComponent(out KryticalNova.Core.IStandIn standIn) && standIn.StandsFor != null) victim = standIn.StandsFor;
            if (victim == attacker) return false;
            if (!(IsPlayer(attacker) && IsPlayer(victim))) return true;
            bool neutralGround = TradeZone.CrossFactionAt(attacker.position) || TradeZone.CrossFactionAt(victim.position);
            IFactionMember a = FactionRules.MemberOf(attacker), b = FactionRules.MemberOf(victim);
            bool sameMember = a != null && ReferenceEquals(a, b);
            bool duelling = a != null && b != null && !sameMember && (a.IsDuelling(b) || b.IsDuelling(a));
            // The one rule (M4): the entity netcode asks the same HostilityRules with what its ghosts carry. The Scar: the factions are at
            // war there whatever cross-faction PvP is set to.
            return HostilityRules.IsHostile(a != null ? a.Faction : FactionId.None, b != null ? b.Faction : FactionId.None, true, sameMember,
                duelling, neutralGround, ZoneRules.AlwaysOnPvp, FactionRules.CrossFactionPvp);
        }
    }
}
