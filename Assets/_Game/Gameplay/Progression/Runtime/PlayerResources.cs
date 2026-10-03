using System;
using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Per-player HP/Chi, stat-derived capacity, regeneration, and atomic skill spending.</summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression))]
    public sealed class PlayerResources : NetworkBehaviour
    {
        private const int InitializationOrder = -150;
        private const float ComparisonTolerance = 0.0001f;
        [SerializeField] private CharacterResourceData resourceData;
        private PlayerCharacterProgression progression;
        private CharacterCombatStats combatStats;
        private float health;
        private float chi;
        private float maxHealth;
        private float maxChi;
        private float healthRecoveryTime;
        private float chiRecoveryTime;
        private bool initialized;

        private readonly NetworkVariable<float> netHealth = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netChi = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netMaxHealth = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netMaxChi = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);

        public event Action Changed;
        public event Action Depleted;
        public float Health => health;
        public float Chi => chi;
        public float MaxHealth => maxHealth;
        public float MaxChi => maxChi;
        public bool IsAlive
        {
            get
            {
                if (!initialized && Application.isEditor && !Application.isPlaying) EnsureInitialized();
                return initialized && health > 0f;
            }
        }
        public CharacterResourceData Data => resourceData;

        public void EnsureInitialized()
        {
            if (initialized) return;
            progression = GetComponent<PlayerCharacterProgression>();
            combatStats = GetComponent<CharacterCombatStats>();
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

        private void Awake()
        {
            EnsureInitialized();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                netHealth.Value = health;
                netChi.Value = chi;
                netMaxHealth.Value = maxHealth;
                netMaxChi.Value = maxChi;
            }
            else
            {
                health = netHealth.Value;
                chi = netChi.Value;
                maxHealth = netMaxHealth.Value;
                maxChi = netMaxChi.Value;
                Changed?.Invoke();
            }

            netHealth.OnValueChanged += OnNetHealthChanged;
            netChi.OnValueChanged += OnNetChiChanged;
            netMaxHealth.OnValueChanged += OnNetMaxHealthChanged;
            netMaxChi.OnValueChanged += OnNetMaxChiChanged;
        }

        public override void OnNetworkDespawn()
        {
            netHealth.OnValueChanged -= OnNetHealthChanged;
            netChi.OnValueChanged -= OnNetChiChanged;
            netMaxHealth.OnValueChanged -= OnNetMaxHealthChanged;
            netMaxChi.OnValueChanged -= OnNetMaxChiChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetHealthChanged(float previous, float current)
        {
            if (IsOwner && current < previous - 0.01f)
                ClientPresentationRegistry.Feel?.LocalPlayerDamaged((previous - current) / Mathf.Max(1f, netMaxHealth.Value));
            if (IsServer) return;
            health = current;
            Changed?.Invoke();
            if (previous > 0f && current <= 0f) Depleted?.Invoke();
        }

        private void OnNetChiChanged(float previous, float current)
        {
            if (IsServer) return;
            chi = current;
            Changed?.Invoke();
        }

        private void OnNetMaxHealthChanged(float previous, float current)
        {
            if (IsServer) return;
            maxHealth = current;
            Changed?.Invoke();
        }

        private void OnNetMaxChiChanged(float previous, float current)
        {
            if (IsServer) return;
            maxChi = current;
            Changed?.Invoke();
        }

        private void OnEnable()
        {
            if (progression == null) progression = GetComponent<PlayerCharacterProgression>();
            if (combatStats == null) combatStats = GetComponent<CharacterCombatStats>();

            if (progression != null)
            {
                progression.Changed -= RecalculateMaximums;
                progression.Changed += RecalculateMaximums;
            }
            if (combatStats != null)
            {
                combatStats.OnStatsRecalculated -= RecalculateMaximums;
                combatStats.OnStatsRecalculated += RecalculateMaximums;
            }
        }

        private void OnDisable()
        {
            if (progression != null) progression.Changed -= RecalculateMaximums;
            if (combatStats != null) combatStats.OnStatsRecalculated -= RecalculateMaximums;
        }

        private void Update()
        {
            if (!initialized || resourceData == null) return;
            if (IsSpawned && !IsServer) return; // Only server handles passive regeneration and capacity calculations

            // Also reflects live Inspector balance edits without changing the shared asset.
            RecalculateMaximums();
            if (!IsAlive) return;
            float previousHealth = health;
            if (Time.time >= healthRecoveryTime)
                health = Mathf.Min(maxHealth, health + resourceData.healthRegenerationPerSecond * Time.deltaTime);
            // In Twelve Sky 2, Chi does not passively regenerate. It is restored via Heavenly Serenity skill, items, or explicit commands.
            if (!Mathf.Approximately(previousHealth, health))
            {
                SyncNetworkState();
                Changed?.Invoke();
            }
        }

        private void SyncNetworkState()
        {
            if (IsSpawned && IsServer)
            {
                netHealth.Value = health;
                netChi.Value = chi;
                netMaxHealth.Value = maxHealth;
                netMaxChi.Value = maxChi;
            }
        }

        private void RecalculateMaximums()
        {
            if (IsSpawned && !IsServer) return;

            float nextHealth;
            float nextChi;

            if (combatStats == null)
                combatStats = GetComponent<CharacterCombatStats>();

            if (combatStats == null)
            {
                // CharacterCombatStats (tuned by CombatStatTuningData) is the single owner of Max HP/Chi formulas.
                Debug.LogError($"{nameof(PlayerResources)} on '{name}' requires a {nameof(CharacterCombatStats)} to derive Max HP/Chi.", this);
                return;
            }

            nextHealth = Mathf.Max(1f, combatStats.GetStatValue(StatType.MaxHP));
            nextChi = Mathf.Max(0f, combatStats.GetStatValue(StatType.MaxChi));

            if (Mathf.Approximately(nextHealth, maxHealth) && Mathf.Approximately(nextChi, maxChi)) return;
            if (initialized && resourceData != null && resourceData.fillGainedCapacity)
            {
                // Allocating stats must not revive a defeated character.
                if (health > 0f) health += Mathf.Max(0f, nextHealth - maxHealth);
                chi += Mathf.Max(0f, nextChi - maxChi);
            }
            maxHealth = nextHealth;
            maxChi = nextChi;
            health = Mathf.Clamp(health, 0f, maxHealth);
            chi = Mathf.Clamp(chi, 0f, maxChi);
            SyncNetworkState();
            if (initialized) Changed?.Invoke();
        }

        /// <summary>Checks affordability without consuming Chi. Selection and hotbar assignment are free.</summary>
        public bool CanSpendChi(float amount) => isActiveAndEnabled && IsAlive && float.IsFinite(amount)
            && amount >= 0f && (chi + ComparisonTolerance >= amount || (GmState.InfiniteChi && IsGmTarget));

        /// <summary>True where this player's HP and Chi are decided (the server, or offline). Clients only mirror them.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>GM toggles only apply to the local (owning) player, never to other players simulated on a host.</summary>
        private bool IsGmTarget => !IsSpawned || IsOwner;

        /// <summary>Consumes Chi once for an accepted skill command and delays passive recovery.</summary>
        public bool TrySpendChi(float amount)
        {
            if (GmState.InfiniteChi && IsGmTarget && IsAlive) return true;
            if (!CanSpendChi(amount)) return false;
            if (amount <= 0f) return true;
            chi = Mathf.Max(0f, chi - amount);
            chiRecoveryTime = Time.time + resourceData.chiRegenerationDelay;
            SyncNetworkState();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Restores a positive amount of Chi, clamped to the current capacity.</summary>
        public void RestoreChi(float amount)
        {
            if (!initialized || !float.IsFinite(amount) || amount <= 0f) return;
            chi = Mathf.Min(maxChi, chi + amount);
            SyncNetworkState();
            Changed?.Invoke();
        }

        /// <summary>Applies positive damage and signals depletion on the transition to zero HP.</summary>
        public void ApplyDamage(float amount)
        {
            if (!isActiveAndEnabled || !IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            if (GmState.GodMode && IsGmTarget) return;
            health = Mathf.Max(0f, health - amount);
            healthRecoveryTime = Time.time + resourceData.healthRegenerationDelay;
            SyncNetworkState();
            Changed?.Invoke();
            if (health <= 0f) Depleted?.Invoke();
        }

        /// <summary>Heals a living player without exceeding maximum HP.</summary>
        public void Heal(float amount)
        {
            if (!IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            health = Mathf.Min(maxHealth, health + amount);
            SyncNetworkState();
            Changed?.Invoke();
        }

        /// <summary>Restores both resources; usable by future respawn systems and explicit playtest controls.</summary>
        public void RestoreAll()
        {
            if (!initialized) return;
            health = maxHealth;
            chi = maxChi;
            SyncNetworkState();
            Changed?.Invoke();
        }
    }
}

