using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Designer-owned HP/Chi formulas and recovery rules. Current values remain per-player state.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Character Resources")]
    public sealed class CharacterResourceData : ScriptableObject
    {
        private const float DefaultBaseHealth = 100f;
        private const float DefaultHealthPerVitality = 10f;
        private const float DefaultBaseChi = 50f;
        private const float DefaultChiPerSpirit = 5f;
        private const float DefaultChiRecovery = 5f;
        private const float DefaultRecoveryDelay = 2f;
        [Header("Maximums: base + total attribute × bonus")]
        [Min(1f)] public float baseHealth = DefaultBaseHealth;
        [Min(0f)] public float healthPerVitality = DefaultHealthPerVitality;
        [Min(0f)] public float baseChi = DefaultBaseChi;
        [Min(0f)] public float chiPerSpirit = DefaultChiPerSpirit;
        [Tooltip("Add the capacity increase to current HP/Chi when its maximum rises.")]
        public bool fillGainedCapacity = true;
        [Header("Recovery (zero disables regeneration)")]
        [Min(0f)] public float healthRegenerationPerSecond;
        [Min(0f)] public float chiRegenerationPerSecond = DefaultChiRecovery;
        [Min(0f)] public float healthRegenerationDelay = DefaultRecoveryDelay;
        [Min(0f)] public float chiRegenerationDelay = DefaultRecoveryDelay;

        private void OnValidate()
        {
            baseHealth = Mathf.Max(1f, baseHealth);
            healthPerVitality = Mathf.Max(0f, healthPerVitality);
            baseChi = Mathf.Max(0f, baseChi);
            chiPerSpirit = Mathf.Max(0f, chiPerSpirit);
            healthRegenerationPerSecond = Mathf.Max(0f, healthRegenerationPerSecond);
            chiRegenerationPerSecond = Mathf.Max(0f, chiRegenerationPerSecond);
            healthRegenerationDelay = Mathf.Max(0f, healthRegenerationDelay);
            chiRegenerationDelay = Mathf.Max(0f, chiRegenerationDelay);
        }
    }
}
