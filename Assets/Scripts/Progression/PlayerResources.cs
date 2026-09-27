using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Per-player HP/Chi, stat-derived capacity, regeneration, and atomic skill spending.</summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression))]
    public sealed class PlayerResources : MonoBehaviour
    {
        private const int InitializationOrder = -150;
        private const float ComparisonTolerance = 0.0001f;
        [SerializeField] private CharacterResourceData resourceData;
        private PlayerCharacterProgression progression;
        private float health;
        private float chi;
        private float maxHealth;
        private float maxChi;
        private float healthRecoveryTime;
        private float chiRecoveryTime;
        private bool initialized;
        public event Action Changed;
        public event Action Depleted;
        public float Health => health;
        public float Chi => chi;
        public float MaxHealth => maxHealth;
        public float MaxChi => maxChi;
        public bool IsAlive => initialized && health > 0f;
        public CharacterResourceData Data => resourceData;

        private void Awake()
        {
            progression = GetComponent<PlayerCharacterProgression>();
            if (resourceData == null)
            {
                Debug.LogError("PlayerResources requires CharacterResourceData.", this);
                enabled = false;
                return;
            }
            RecalculateMaximums();
            health = maxHealth;
            chi = maxChi;
            initialized = true;
        }

        private void OnEnable()
        {
            if (progression != null) progression.Changed += RecalculateMaximums;
        }

        private void OnDisable()
        {
            if (progression != null) progression.Changed -= RecalculateMaximums;
        }

        private void Update()
        {
            if (!initialized || resourceData == null) return;
            // Also reflects live Inspector balance edits without changing the shared asset.
            RecalculateMaximums();
            if (!IsAlive) return;
            float previousHealth = health;
            float previousChi = chi;
            if (Time.time >= healthRecoveryTime)
                health = Mathf.Min(maxHealth, health + resourceData.healthRegenerationPerSecond * Time.deltaTime);
            if (Time.time >= chiRecoveryTime)
                chi = Mathf.Min(maxChi, chi + resourceData.chiRegenerationPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(previousHealth, health) || !Mathf.Approximately(previousChi, chi)) Changed?.Invoke();
        }

        private void RecalculateMaximums()
        {
            if (progression == null || resourceData == null) return;
            float nextHealth = Mathf.Max(1f, resourceData.baseHealth
                + progression.GetAttribute(CharacterAttribute.Vitality) * resourceData.healthPerVitality);
            float nextChi = Mathf.Max(0f, resourceData.baseChi
                + progression.GetAttribute(CharacterAttribute.Spirit) * resourceData.chiPerSpirit);
            if (Mathf.Approximately(nextHealth, maxHealth) && Mathf.Approximately(nextChi, maxChi)) return;
            if (initialized && resourceData.fillGainedCapacity)
            {
                // Allocating stats must not revive a defeated character.
                if (health > 0f) health += Mathf.Max(0f, nextHealth - maxHealth);
                chi += Mathf.Max(0f, nextChi - maxChi);
            }
            maxHealth = nextHealth;
            maxChi = nextChi;
            health = Mathf.Clamp(health, 0f, maxHealth);
            chi = Mathf.Clamp(chi, 0f, maxChi);
            if (initialized) Changed?.Invoke();
        }

        /// <summary>Checks affordability without consuming Chi. Selection and hotbar assignment are free.</summary>
        public bool CanSpendChi(float amount) => isActiveAndEnabled && IsAlive && float.IsFinite(amount)
            && amount >= 0f && chi + ComparisonTolerance >= amount;

        /// <summary>Consumes Chi once for an accepted skill command and delays passive recovery.</summary>
        public bool TrySpendChi(float amount)
        {
            if (!CanSpendChi(amount)) return false;
            if (amount <= 0f) return true;
            chi = Mathf.Max(0f, chi - amount);
            chiRecoveryTime = Time.time + resourceData.chiRegenerationDelay;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Restores a positive amount of Chi, clamped to the current capacity.</summary>
        public void RestoreChi(float amount)
        {
            if (!initialized || !float.IsFinite(amount) || amount <= 0f) return;
            chi = Mathf.Min(maxChi, chi + amount);
            Changed?.Invoke();
        }

        /// <summary>Applies positive damage and signals depletion on the transition to zero HP.</summary>
        public void ApplyDamage(float amount)
        {
            if (!isActiveAndEnabled || !IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            health = Mathf.Max(0f, health - amount);
            healthRecoveryTime = Time.time + resourceData.healthRegenerationDelay;
            Changed?.Invoke();
            if (health <= 0f) Depleted?.Invoke();
        }

        /// <summary>Heals a living player without exceeding maximum HP.</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            health = Mathf.Min(maxHealth, health + amount);
            Changed?.Invoke();
        }

        /// <summary>Restores both resources; usable by future respawn systems and explicit playtest controls.</summary>
        public void RestoreAll()
        {
            if (!initialized) return;
            health = maxHealth;
            chi = maxChi;
            Changed?.Invoke();
        }
    }
}
