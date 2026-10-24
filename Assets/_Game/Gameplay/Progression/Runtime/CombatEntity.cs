using System;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Progression
{
    /// <summary>Concrete combat participant (Player or NPC/Monster/Dummy) that can receive and process damage.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatEntity : NetworkBehaviour, IDamageable
    {
        [Header("Identity & Tuning (ScriptableObjects)")]
        [SerializeField] private string entityName = "Combat Dummy";
        [SerializeField] private CharacterClassData classData;
        [SerializeField] private CombatStatTuningData tuningData;

        [Header("Health Pool")]
        [SerializeField] private float overrideMaxHealth = 500f;
        [SerializeField] private bool useCombatStatsForMaxHealth = true;

        private CharacterCombatStats combatStats;
        private PlayerResources playerResources;
        private CharacterBuffManager buffManager;
        private float standaloneHealth;
        private float maxHealthOverride; // 0 = none (testing: GM dummy tools)
        private bool infiniteHealth;
        private bool initialized;

        private readonly NetworkVariable<float> netStandaloneHealth = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netMaxHealthOverride = new NetworkVariable<float>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> netInfiniteHealth = new NetworkVariable<bool>(
            writePerm: NetworkVariableWritePermission.Server);

        public event Action<DamageResult, CharacterCombatStats> Damaged;
        public event Action Died;
        /// <summary>
        /// Raised where combat is decided (server or offline) when this entity dies, with the blow that did it: who,
        /// from which side, and whether it was critical. <see cref="Died"/> is the bare notice every peer gets.
        /// </summary>
        public event Action<KillingBlow> Killed;
        /// <summary>
        /// Raised where combat is decided (server or offline) for every entity that is killed: (victim, blow).
        /// For systems that reward a kill without knowing every entity: pet XP today, drops and Contracts later.
        /// </summary>
        public static event Action<CombatEntity, KillingBlow> AnyKilled;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetKillListeners() => AnyKilled = null;
        public event Action HealthChanged;

        // The hit being applied right now, so the death it may cause knows what caused it.
        private KillingBlow pendingBlow;
        private bool hasPendingBlow;

        private bool HasAuthority => !IsSpawned || IsServer;

        public string EntityName => entityName;

        /// <summary>A player's character name, applied on every peer when its identity replicates.</summary>
        public void SetEntityName(string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) entityName = value;
        }
        public Transform Transform => transform;
        public CharacterCombatStats CombatStats => combatStats;

        public float MaxHealth
        {
            get
            {
                if (playerResources != null) return playerResources.MaxHealth;
                if (maxHealthOverride > 0f) return maxHealthOverride;
                if (useCombatStatsForMaxHealth && combatStats != null) return combatStats.GetStatValue(StatType.MaxHP);
                return overrideMaxHealth;
            }
        }

        /// <summary>Standalone entities only (dummies, NPCs): a lethal hit refills the pool instead of killing.</summary>
        public bool InfiniteHealth => infiniteHealth;

        public float CurrentHealth
        {
            get
            {
                if (!initialized) InitializeEntity();
                if (playerResources != null) return playerResources.Health;
                return standaloneHealth;
            }
        }

        public bool IsAlive => CurrentHealth > 0f;

        private void Awake()
        {
            InitializeEntity();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (playerResources == null)
            {
                if (IsServer)
                {
                    netStandaloneHealth.Value = standaloneHealth;
                    netMaxHealthOverride.Value = maxHealthOverride;
                    netInfiniteHealth.Value = infiniteHealth;
                }
                else
                {
                    maxHealthOverride = netMaxHealthOverride.Value;
                    infiniteHealth = netInfiniteHealth.Value;
                    standaloneHealth = netStandaloneHealth.Value;
                    HealthChanged?.Invoke();
                }

                netStandaloneHealth.OnValueChanged += OnNetStandaloneHealthChanged;
                netMaxHealthOverride.OnValueChanged += OnNetMaxHealthOverrideChanged;
                netInfiniteHealth.OnValueChanged += OnNetInfiniteHealthChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (playerResources == null)
            {
                netStandaloneHealth.OnValueChanged -= OnNetStandaloneHealthChanged;
                netMaxHealthOverride.OnValueChanged -= OnNetMaxHealthOverrideChanged;
                netInfiniteHealth.OnValueChanged -= OnNetInfiniteHealthChanged;
            }
            base.OnNetworkDespawn();
        }

        private void OnNetMaxHealthOverrideChanged(float previous, float current)
        {
            if (IsServer) return;
            maxHealthOverride = current;
            HealthChanged?.Invoke();
        }

        private void OnNetInfiniteHealthChanged(bool previous, bool current)
        {
            if (!IsServer) infiniteHealth = current;
        }

        private void OnNetStandaloneHealthChanged(float previous, float current)
        {
            if (IsServer) return;
            standaloneHealth = current;
            HealthChanged?.Invoke();
            if (previous > 0f && current <= 0f)
            {
                Died?.Invoke();
            }
        }

        public void InitializeEntity()
        {
            if (initialized) return;

            combatStats = GetComponent<CharacterCombatStats>();
            if (combatStats == null)
            {
                combatStats = gameObject.AddComponent<CharacterCombatStats>();
            }

            if (classData != null && combatStats.ClassData == null)
            {
                combatStats.ClassData = classData;
            }
            if (tuningData != null && combatStats.TuningData == null)
            {
                combatStats.TuningData = tuningData;
            }

            playerResources = GetComponent<PlayerResources>();
            if (playerResources != null) playerResources.EnsureInitialized();
            buffManager = GetComponent<CharacterBuffManager>();
            if (playerResources != null)
            {
                playerResources.Changed += OnResourcesChanged;
                playerResources.Depleted += OnResourcesDepleted;
            }
            else
            {
                standaloneHealth = MaxHealth;
                // A Strike Dummy (no player, no Hostile) never runs out (owner, 24 Oct 2026: permanently infinite).
                if (IsStrikeDummy) infiniteHealth = true;
            }

            initialized = true;
        }

        public override void OnDestroy()
        {
            if (playerResources != null)
            {
                playerResources.Changed -= OnResourcesChanged;
                playerResources.Depleted -= OnResourcesDepleted;
            }
            base.OnDestroy();
        }

        private void OnResourcesChanged() => HealthChanged?.Invoke();
        private void OnResourcesDepleted() => RaiseDied();

        private void RaiseDied()
        {
            Died?.Invoke();
            if (!HasAuthority) return;
            KillingBlow blow = hasPendingBlow ? pendingBlow : KillingBlow.FromTheFront(transform);
            Killed?.Invoke(blow);
            AnyKilled?.Invoke(this, blow);
        }

        /// <summary>
        /// Server (or offline) only: kills this entity outright with a blow of the given kind, for tools and effects
        /// that have no attack roll behind them. Ignores god mode.
        /// </summary>
        public void Kill(KillingBlow blow)
        {
            if (!HasAuthority || !IsAlive) return;
            pendingBlow = blow;
            hasPendingBlow = true;
            try
            {
                if (playerResources != null)
                {
                    playerResources.ApplyDamage(playerResources.Health + 1f, ignoreGodMode: true);
                }
                else
                {
                    standaloneHealth = 0f;
                    if (IsSpawned && IsServer) netStandaloneHealth.Value = 0f;
                    HealthChanged?.Invoke();
                    RaiseDied();
                }
            }
            finally
            {
                hasPendingBlow = false;
            }
        }

        public DamageResult TakeDamage(DamageResult damage, CharacterCombatStats attacker)
        {
            if (!IsAlive) return damage;

            if (damage.IsReflected)
            {
                // Reflected strike: attacker takes the reflected damage (which bypasses Heavenly Shield)
                if (attacker != null)
                {
                    var attackerEntity = attacker.GetComponent<IDamageable>();
                    if (attackerEntity != null)
                    {
                        var reflectResult = new DamageResult
                        {
                            DamageDealt = damage.ReflectedDamage,
                            RawPhysicalDamage = damage.ReflectedDamage,
                            MitigatedPhysicalDamage = damage.ReflectedDamage,
                            BypassesShield = true // Reflected damage bypasses target's Heavenly Shield
                        };
                        attackerEntity.TakeDamage(reflectResult, combatStats);
                    }
                }
                return damage;
            }

            if (damage.IsEvaded)
            {
                Damaged?.Invoke(damage, attacker);
                return damage;
            }

            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();

            // 1. Check if attack shattered defender's shield (Heavenly Destruction proc)
            if (damage.ShieldShattered && buffManager != null)
            {
                buffManager.ShatterShield();
            }

            // 2. Physical damage hits Heavenly Shield first; Elemental damage bypasses shield directly to life
            float unabsorbedPhysical = damage.MitigatedPhysicalDamage;
            if (!damage.BypassesShield && buffManager != null && buffManager.HasActiveShield && unabsorbedPhysical > 0f)
            {
                unabsorbedPhysical = buffManager.AbsorbDamage(unabsorbedPhysical);
            }

            float directElemental = damage.NetElementalDamage;
            float totalDamageToLife = unabsorbedPhysical + directElemental;

            // 3. Any unabsorbed damage reaching life interrupts channeled skills (Heavenly Serenity)
            if (totalDamageToLife > 0f && buffManager != null)
            {
                buffManager.InterruptChanneled();
            }

            if (totalDamageToLife > 0f)
            {
                pendingBlow = new KillingBlow
                {
                    Result = damage,
                    Attacker = attacker,
                    Direction = KillingBlow.DirectionFrom(attacker != null ? attacker.transform : null, transform),
                    IsCritical = damage.IsCritical,
                };
                hasPendingBlow = true;
                try
                {
                    if (playerResources != null)
                    {
                        playerResources.ApplyDamage(totalDamageToLife);
                    }
                    else
                    {
                        standaloneHealth = Mathf.Max(0f, standaloneHealth - totalDamageToLife);
                        if (infiniteHealth && standaloneHealth <= 0f) standaloneHealth = MaxHealth;
                        if (IsSpawned && IsServer)
                        {
                            netStandaloneHealth.Value = standaloneHealth;
                        }
                        HealthChanged?.Invoke();
                        if (standaloneHealth <= 0f)
                        {
                            RaiseDied();
                        }
                    }
                }
                finally
                {
                    hasPendingBlow = false;
                }
            }

            Damaged?.Invoke(damage, attacker);
            return damage;
        }

        public void ResetHealth()
        {
            if (playerResources != null)
            {
                playerResources.RestoreAll();
            }
            else
            {
                standaloneHealth = MaxHealth;
                if (IsSpawned && IsServer)
                {
                    netStandaloneHealth.Value = standaloneHealth;
                }
                HealthChanged?.Invoke();
            }
        }

        // ------------------------------------------------------------------ testing (standalone entities, server / offline)

        private bool CanEditStandalone => playerResources == null && (!IsSpawned || IsServer);

        /// <summary>Sets current health, clamped to 1..max. Returns false for players and on non-authoritative peers.</summary>
        public bool SetHealth(float value)
        {
            if (!initialized) InitializeEntity();
            if (!CanEditStandalone) return false;
            standaloneHealth = Mathf.Clamp(value, 1f, MaxHealth);
            if (IsSpawned) netStandaloneHealth.Value = standaloneHealth;
            HealthChanged?.Invoke();
            return true;
        }

        /// <summary>Replaces max health (0 returns to the class / stat value) and refills the pool.</summary>
        public bool SetMaxHealthOverride(float value)
        {
            if (!initialized) InitializeEntity();
            if (!CanEditStandalone) return false;
            maxHealthOverride = Mathf.Max(0f, value);
            if (IsSpawned) netMaxHealthOverride.Value = maxHealthOverride;
            ResetHealth();
            return true;
        }

        /// <summary>A training dummy: neither a player nor a Hostile. Its health is always infinite.</summary>
        public bool IsStrikeDummy => playerResources == null && !TryGetComponent(out IHostile _);

        public bool SetInfiniteHealth(bool value)
        {
            if (!initialized) InitializeEntity();
            if (!CanEditStandalone) return false;
            if (IsStrikeDummy) value = true;
            infiniteHealth = value;
            if (IsSpawned) netInfiniteHealth.Value = value;
            if (value && standaloneHealth <= 0f) ResetHealth(); // revive a dummy that was already down
            return true;
        }
    }
}
