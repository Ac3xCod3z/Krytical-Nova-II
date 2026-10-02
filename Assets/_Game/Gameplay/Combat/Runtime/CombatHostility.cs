using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Who may attack whom. There is no faction system yet, so every player is friendly to every other player;
    /// anything else that can take damage (dummies, monsters) is hostile. Replace the player rule with a faction
    /// comparison once factions exist.
    /// </summary>
    public static class CombatHostility
    {
        public static bool IsPlayer(Transform t) => t != null && t.GetComponentInParent<PlayerResources>() != null;

        public static bool IsHostile(Transform attacker, IDamageable target)
        {
            if (target == null || target.Transform == null || target.Transform == attacker) return false;
            return !(IsPlayer(attacker) && IsPlayer(target.Transform));
        }
    }
}
