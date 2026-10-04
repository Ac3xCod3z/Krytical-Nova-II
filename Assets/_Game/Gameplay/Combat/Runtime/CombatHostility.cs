using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Who may attack whom. Anything that is not a player and can take damage (dummies, monsters) is hostile.
    /// Between two players the factions decide (<see cref="FactionRules.AreHostile"/>): while cross-faction PvP is
    /// switched off, which it is until factions stop sharing the starter town, every player is friendly.
    /// </summary>
    public static class CombatHostility
    {
        public static bool IsPlayer(Transform t) => t != null && t.GetComponentInParent<PlayerResources>() != null;

        public static bool IsHostile(Transform attacker, IDamageable target)
        {
            if (target == null || target.Transform == null || target.Transform == attacker) return false;
            if (!(IsPlayer(attacker) && IsPlayer(target.Transform))) return true;
            return FactionRules.AreHostile(FactionRules.Of(attacker), FactionRules.Of(target.Transform));
        }
    }
}
