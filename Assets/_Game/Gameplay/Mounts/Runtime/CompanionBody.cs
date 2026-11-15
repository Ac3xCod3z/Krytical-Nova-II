using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// The Mount Companion's hidden server body (day list item 19, phase 2): a trigger capsule where the follow put the companion, so the
    /// game's own area-skill test (PlayerSkillAttacks.CollectAreaTargets) finds it. It stands in for its owner (<see cref="IStandIn"/>):
    /// only the owner's enemies in PvP may hit it (owner pick 2 a), and in its TANK stance the Hostiles it taunted (phase 4, through
    /// <c>StandInTargets</c>); every hit goes to the owner's <see cref="PlayerMounts"/>, which holds
    /// its HP. Its defence is the owner's (it has no gear of its own). Server only: made and moved by the companion's server system, never
    /// on a view, no renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionBody : MonoBehaviour, IDamageable, IStandIn
    {
        PlayerMounts owner;
        CharacterCombatStats ownerStats;

        /// <summary>Server: makes a body for <paramref name="mounts"/>'s companion (hidden, a trigger capsule of the mount's size).</summary>
        public static CompanionBody Make(PlayerMounts mounts, float radius, float height)
        {
            var go = new GameObject("CompanionBody");
            go.hideFlags = HideFlags.DontSave;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.isTrigger = true;
            capsule.radius = Mathf.Max(0.2f, radius);
            capsule.height = Mathf.Max(capsule.radius * 2f, height);
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            var body = go.AddComponent<CompanionBody>();
            body.Link(mounts);
            return body;
        }

        public void Link(PlayerMounts mounts)
        {
            owner = mounts;
            ownerStats = mounts != null ? mounts.GetComponent<CharacterCombatStats>() : null;
        }

        public PlayerMounts Owner => owner;
        public Transform StandsFor => owner != null ? owner.transform : null;

        public string EntityName => owner != null && owner.Selected != null ? owner.Selected.displayName : "Mount";
        public bool IsAlive => owner != null && owner.CompanionOut;
        public float MaxHealth => owner != null ? owner.CompanionMaxHp : 1f;
        public float CurrentHealth => MaxHealth * (owner != null ? owner.CompanionHpPermille : 0) / CompanionRules.Permille;
        public Transform Transform => transform;
        public CharacterCombatStats CombatStats => ownerStats;

        /// <summary>The share of a blow it takes: below 1 while it acts in the TANK stance (phase 4: its defence buff), set by its server system.</summary>
        public float TakenScale { get; set; } = 1f;

        public DamageResult TakeDamage(DamageResult damage, CharacterCombatStats attacker)
        {
            if (!IsAlive || damage.IsEvaded || damage.IsReflected) return damage;
            owner.HurtCompanion((damage.DamageDealt + damage.NetElementalDamage) * Mathf.Clamp01(TakenScale));
            return damage;
        }
    }
}
