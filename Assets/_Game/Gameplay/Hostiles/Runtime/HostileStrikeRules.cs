using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// Whether a Hostile's attack lands on a player and what it does, by the game's rules (M5 seam 2): the part of
    /// <c>HostileBrainLink.Strike</c> that does not depend on Emerald, so Emerald's brain and the entity netcode's brain roll the same
    /// hit. The caller applies the result (<see cref="CombatEntity.TakeDamage"/>) and reports it.
    /// </summary>
    public static class HostileStrikeRules
    {
        /// <summary>Metres beyond the attack range that still reach a player standing on a wall.</summary>
        public const float WallReachSlack = 0.5f;
        /// <summary>The height a Hostile looks from when it reaches for a player on a wall.</summary>
        public const float EyeHeight = 1.2f;
        private static readonly RaycastHit[] SightHits = new RaycastHit[16];

        /// <summary>
        /// Rolls attack <paramref name="attackIndex"/> of <paramref name="attacker"/> (whose body is <paramref name="attackerBody"/>) on
        /// <paramref name="target"/>. False when it cannot land: either side dead, the player above what the species reaches, or on a wall
        /// it cannot reach in a straight clear line; otherwise the rolled <paramref name="result"/>.
        /// </summary>
        public static bool TryRoll(CombatEntity attacker, HostileDefinition definition, int attackIndex, CombatEntity target, Transform attackerBody,
            out DamageResult result)
        {
            result = default;
            if (attacker == null || target == null || attackerBody == null || !target.IsAlive || !attacker.IsAlive) return false;
            // A player up a wall or in a tree, above what this species can reach: the swing meets nothing.
            if (definition != null && target.transform.position.y - attackerBody.position.y > definition.reachAbove) return false;
            // A player standing on a wall (walls as a second ground): only an attack that reaches there in a straight
            // line, height included, with nothing in the way, lands. Melee species wait below.
            if (OnAWall(target.transform) && !ReachesOnWall(attackerBody, target.transform, definition != null ? definition.attackRange : 2f)) return false;
            CharacterCombatStats mine = attacker.CombatStats;
            if (mine == null || target.CombatStats == null) return false;

            float multiplier = 1f;
            if (definition != null && attackIndex >= 0 && attackIndex < definition.attacks.Length)
                multiplier = Mathf.Max(0.1f, definition.attacks[attackIndex].damageMultiplier);

            float reflectChance = 0f;
            var defenderBuffs = target.GetComponent<CharacterBuffManager>();
            bool reflects = defenderBuffs != null && defenderBuffs.HasReflection(out reflectChance, out _);

            result = CombatCalculator.CalculateAttack(mine, target.CombatStats, multiplier, reflects, reflectChance);
            return true;
        }

        /// <summary>
        /// The same roll on a stand-in that is not a player (the Mount Companion in its TANK stance, day list item 19 phase 4): both alive, the
        /// reach above, the attack's multiplier, <c>CombatCalculator.CalculateAttack</c> against the stand-in's stats. A stand-in never stands
        /// on a wall and never reflects.
        /// </summary>
        public static bool TryRollOnStandIn(CombatEntity attacker, HostileDefinition definition, int attackIndex, IDamageable target, Transform attackerBody,
            out DamageResult result)
        {
            result = default;
            if (attacker == null || target == null || attackerBody == null || !target.IsAlive || !attacker.IsAlive || target.Transform == null) return false;
            if (definition != null && target.Transform.position.y - attackerBody.position.y > definition.reachAbove) return false;
            CharacterCombatStats mine = attacker.CombatStats;
            if (mine == null || target.CombatStats == null) return false;
            float multiplier = 1f;
            if (definition != null && attackIndex >= 0 && attackIndex < definition.attacks.Length)
                multiplier = Mathf.Max(0.1f, definition.attacks[attackIndex].damageMultiplier);
            result = CombatCalculator.CalculateAttack(mine, target.CombatStats, multiplier, false, 0f);
            return true;
        }

        /// <summary>A body whose up is not the world's stands on a wall.</summary>
        public static bool OnAWall(Transform body) => Vector3.Angle(body.up, Vector3.up) > 30f;

        /// <summary>
        /// From <paramref name="attackerBody"/>'s eyes to <paramref name="target"/>: within the attack range (plus the slack and the eye
        /// height) and nothing but the two bodies in the way.
        /// </summary>
        public static bool ReachesOnWall(Transform attackerBody, Transform target, float attackRange)
        {
            float reach = attackRange + WallReachSlack;
            Vector3 from = attackerBody.position + Vector3.up * EyeHeight;
            Vector3 to = target.position;
            Vector3 way = to - from;
            float distance = way.magnitude;
            if (distance > reach + EyeHeight) return false;
            if (distance < 1e-3f) return true;
            int count = Physics.RaycastNonAlloc(from, way / distance, SightHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Transform hit = SightHits[i].transform;
                if (hit.IsChildOf(attackerBody) || hit.IsChildOf(target)) continue;
                return false; // the wall's corner, a ledge or another body is in the way
            }
            return true;
        }
    }
}
