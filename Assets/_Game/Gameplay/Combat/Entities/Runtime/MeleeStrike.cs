using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// One landed strike of the basic attack on the server body, exactly as NGO's ExecuteAuthoritativeStrike applies it
    /// (PlayerMeleeCombat:749-772) once the swing is decided: a friend is never struck (CombatHostility), the step's multiplier
    /// (1.0 / 1.15 / 1.35) and label, the shared ResolveHit, and the next-hit buffs (Redline) spent unless it was evaded. The order and
    /// the reach are the entity side's (MeleeRules); this never decides them.
    /// </summary>
    public static class MeleeStrike
    {
        /// <summary>Applies strike <paramref name="step"/> of <paramref name="attacker"/> on <paramref name="target"/>; false when nothing landed.</summary>
        public static bool Apply(GameObject attacker, IDamageable target, int step, float stepMultiplier)
        {
            if (attacker == null || target == null || !target.IsAlive) return false;
            if (!attacker.TryGetComponent(out PlayerMeleeCombat melee)) return false;
            if (!CombatHostility.IsHostile(attacker.transform, target)) return false;
            string label = step == 3 ? "Combo Finisher" : $"Strike {step}";
            if (!melee.ResolveHit(target, stepMultiplier, step, label, out DamageResult result)) return false;
            if (!result.IsEvaded && attacker.TryGetComponent(out CharacterBuffManager buffs)) buffs.ConsumeNextHitBuffs();
            return true;
        }
    }
}
