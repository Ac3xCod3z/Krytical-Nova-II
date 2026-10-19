using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Progression
{
    /// <summary>The hit that took an entity to zero health, as known where combat is decided (server or offline).</summary>
    public struct KillingBlow
    {
        public DamageResult Result;
        /// <summary>Who struck it; null for a kill nobody dealt (a GM command).</summary>
        public CharacterCombatStats Attacker;
        /// <summary>Flat, normalised: the way the blow travelled, from the attacker to the victim.</summary>
        public Vector3 Direction;
        public bool IsCritical;

        /// <summary>A blow from straight ahead of <paramref name="victim"/>, for kills that have no attacker.</summary>
        public static KillingBlow FromTheFront(Transform victim) => new KillingBlow { Direction = Flat(-victim.forward, Vector3.back) };

        /// <summary>The blow's direction for an attacker standing at <paramref name="attacker"/>.</summary>
        public static Vector3 DirectionFrom(Transform attacker, Transform victim) =>
            attacker == null ? Flat(-victim.forward, Vector3.back) : Flat(victim.position - attacker.position, -victim.forward);

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude > 0.0001f) return v.normalized;
            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.back;
        }
    }
}
