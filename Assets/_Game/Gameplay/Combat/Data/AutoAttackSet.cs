using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// One weapon class's auto-attack (owner, 4 Nov 2026): the three strikes of its combo, each with its clip, how fast it plays,
    /// when its hit window opens and closes and when its slash shows, the pause after the third, and how far it reaches. Every class
    /// has its own set so its own animations can be slotted in, and a ranged class can strike from further off by its range alone;
    /// until then all nine hold the Monoblade clips and numbers the game shipped with (every class auto-attacks in melee today).
    /// <see cref="PlayerMeleeCombat"/> uses the set of the class in hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Combat/Auto-Attack Set")]
    public sealed class AutoAttackSet : ScriptableObject
    {
        [Serializable]
        public struct Strike
        {
            public AnimationClip clip;
            [Tooltip("How fast the clip plays: the strike lasts clip length / speed.")]
            [Range(1.0f, 2.5f)] public float speed;
            [Tooltip("The hit window, as fractions of the clip. The hit lands by the window's end.")]
            [Range(0f, 1f)] public float swingStart;
            [Range(0f, 1f)] public float swingEnd;
            [Tooltip("When the slash VFX fires, as a fraction of the clip.")]
            [Range(0f, 1f)] public float slashVfxTime;
        }

        [Tooltip("The class this set belongs to.")]
        public WeaponClassId weaponClass;
        public Strike strike1;
        public Strike strike2;
        public Strike strike3;
        [Tooltip("Seconds after the third strike before the combo starts again.")]
        [Min(0f)] public float postComboDelay = 0.25f;
        [Tooltip("How close the target must be for a strike to start, in metres (the walk in stops here).")]
        [Min(0.5f)] public float attackRange = 1.6f;
        [Tooltip("The server's check when a hit is applied, centre to centre, in metres: keep it above the attack range.")]
        [Min(0.5f)] public float serverCheckRange = 2.7f;

        /// <summary>Strike <paramref name="step"/> (1-3).</summary>
        public Strike StrikeOf(int step) => step == 1 ? strike1 : step == 2 ? strike2 : strike3;

        /// <summary>True when all three strikes have a clip: an incomplete set is not used.</summary>
        public bool IsComplete => strike1.clip != null && strike2.clip != null && strike3.clip != null;
    }
}
