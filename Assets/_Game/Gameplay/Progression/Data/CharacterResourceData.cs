using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Designer-owned HP/Chi recovery rules. Maximum HP/Chi formulas are owned exclusively by
    /// <see cref="CombatStatTuningData"/> via <see cref="CharacterCombatStats"/>. Current values remain per-player state.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Character Resources")]
    public sealed class CharacterResourceData : ScriptableObject
    {
        private const float DefaultChiRecovery = 0f;
        private const float DefaultRecoveryDelay = 2f;

        [Tooltip("Add the capacity increase to current HP/Chi when its maximum rises.")]
        public bool fillGainedCapacity = true;
        [Header("Recovery (zero disables regeneration)")]
        [Min(0f)] public float healthRegenerationPerSecond;
        [Tooltip("Chi regeneration per second (0 by default in Twelve Sky 2; Chi is recovered via Heavenly Serenity skill)")]
        [Min(0f)] public float chiRegenerationPerSecond = DefaultChiRecovery;
        [Min(0f)] public float healthRegenerationDelay = DefaultRecoveryDelay;
        [Min(0f)] public float chiRegenerationDelay = DefaultRecoveryDelay;

        private void OnValidate()
        {
            healthRegenerationPerSecond = Mathf.Max(0f, healthRegenerationPerSecond);
            chiRegenerationPerSecond = Mathf.Max(0f, chiRegenerationPerSecond);
            healthRegenerationDelay = Mathf.Max(0f, healthRegenerationDelay);
            chiRegenerationDelay = Mathf.Max(0f, chiRegenerationDelay);
        }
    }
}
