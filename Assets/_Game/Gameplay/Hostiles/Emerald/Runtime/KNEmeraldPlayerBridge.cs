using EmeraldAI;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Hostiles.Emerald
{
    /// <summary>
    /// Makes a player something Emerald AI can see, chase and strike. Emerald only supplies the moment of the
    /// hit: the damage is rolled by the game's own combat rules (<see cref="HostileBrainLink.Strike"/>), and the
    /// health Emerald reads here is a copy of the player's CombatEntity, so it knows when its target is dead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KNEmeraldPlayerBridge : EmeraldPlayerBridge
    {
        /// <summary>The players' entry in Emerald's faction list ("Player").</summary>
        public const int PlayerFaction = 2;

        private CombatEntity entity;

        public override void Start()
        {
            base.Start();
            entity = GetComponent<CombatEntity>();
            if (entity == null) return;
            entity.HealthChanged += CopyHealth;
            CopyHealth();
        }

        private void OnDestroy()
        {
            if (entity != null) entity.HealthChanged -= CopyHealth;
        }

        private void CopyHealth()
        {
            StartHealth = Mathf.Max(1, Mathf.CeilToInt(entity.MaxHealth));
            Health = entity.IsAlive ? Mathf.Max(1, Mathf.CeilToInt(entity.CurrentHealth)) : 0;
        }

        /// <summary>Emerald's number is ignored; only the fact and the moment of the hit are used.</summary>
        public override void DamageCharacterController(int DamageAmount, Transform Target)
        {
            if (entity == null || Target == null) return;
            if (Target.TryGetComponent(out HostileBrainLink attacker)) attacker.Strike(entity);
            CopyHealth();
        }
    }
}
