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
        private PlayerGmState gmState;
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
            gmState = GetComponent<PlayerGmState>();
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
            // another player's view (M4.5h): a mirror from the start, its HP comes from the ghost (CombatEntity.MirrorHealth)
            if (RemoteView) chiMirrored = true;
        }

        private bool RemoteView => BodyRoleTag.IsRemoteClientView(this);

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

        // HP also falls when the most HP falls (a pet put away, gear taken off): that is not a hit, so the hurt
        // effect is not raised here but by ApplyDamage, the one path real damage takes.
        private void OnNetHealthChanged(float previous, float current)
        {
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
            if (IsMirror) return; // Only server handles passive regeneration and capacity calculations

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
            if (IsMirror) return;

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
            && amount >= 0f && (chi + ComparisonTolerance >= amount || HasInfiniteChi);

        /// <summary>True where this player's HP and Chi are decided (the server, or offline). Clients only mirror them.</summary>
        public bool HasAuthority => (!IsSpawned || IsServer) && !RemoteView;

        // An NGO client, an entity-netcode client's own view body once its server's Flux has come (MirrorChi), or another player's view.
        private bool IsMirror => (IsSpawned && !IsServer) || chiMirrored || RemoteView;
        private bool chiMirrored;

        /// <summary>
        /// An entity-netcode client's own view body: the Flux and most Flux its server sent (the player ghost's owner-only Flux),
        /// so the owner's own checks refuse what the server would, in the NGO client's words. From then on this body neither
        /// recalculates its maximums nor recovers on its own: the server's numbers stand. Not for an NGO body (network variables).
        /// </summary>
        public void MirrorChi(float current, float max)
        {
            if (IsSpawned) return;
            chiMirrored = true;   // first: the server's maximum stands, this body's own stats are not read
            EnsureInitialized();
            max = Mathf.Max(0f, max);
            current = Mathf.Clamp(current, 0f, max);
            if (Mathf.Approximately(current, chi) && Mathf.Approximately(max, maxChi)) return;
            chi = current;
            maxChi = max;
            Changed?.Invoke();
        }

        // GM toggles are per player (see PlayerGmState); characters without one are never affected.
        private bool HasInfiniteChi => gmState != null && gmState.InfiniteChi;

        /// <summary>Consumes Chi once for an accepted skill command and delays passive recovery.</summary>
        public bool TrySpendChi(float amount)
        {
            if (HasInfiniteChi && IsAlive) return true;
            if (chiMirrored) return CanSpendChi(amount);   // a mirror only answers; the server spends
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
            if (chiMirrored) return;   // a mirror never fills itself: the server does, and the Flux comes back (item 5)
            if (!initialized || !float.IsFinite(amount) || amount <= 0f) return;
            chi = Mathf.Min(maxChi, chi + amount);
            SyncNetworkState();
            Changed?.Invoke();
        }

        /// <summary>Applies positive damage and signals depletion on the transition to zero HP.</summary>
        /// <param name="ignoreGodMode">GM kill: the damage lands even on a god-mode player.</param>
        public void ApplyDamage(float amount, bool ignoreGodMode = false)
        {
            if (!isActiveAndEnabled || !IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            if (RemoteView) return;   // another player's view: the server's hits come back through the ghost (M4.5h)
            if (!ignoreGodMode && gmState != null && gmState.GodMode) return;
            float before = health;
            health = Mathf.Max(0f, health - amount);
            healthRecoveryTime = Time.time + resourceData.healthRegenerationDelay;
            SyncNetworkState();
            Changed?.Invoke();
            float lost = before - health;
            if (lost > 0.01f)
            {
                float fraction = lost / Mathf.Max(1f, maxHealth);
                if (IsSpawned) DamagedRpc(fraction);
                else ClientPresentationRegistry.Feel?.LocalPlayerDamaged(fraction);
            }
            if (health <= 0f) Depleted?.Invoke();
        }

        /// <summary>The hurt effect on the player's own screen, for a real hit only.</summary>
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void DamagedRpc(float fraction01) => ClientPresentationRegistry.Feel?.LocalPlayerDamaged(fraction01);

        /// <summary>
        /// An entity-netcode client's own view body: the HP and Max HP its server sent (with the owner-only Flux), so the HUD shows a heal
        /// the server made (a stim, Serenity's ticks; owner, 2 Nov, item 5). Like <see cref="MirrorChi"/>: the server's numbers stand.
        /// </summary>
        public void MirrorHealth(float current, float max)
        {
            if (IsSpawned) return;
            chiMirrored = true;
            EnsureInitialized();
            max = Mathf.Max(1f, max);
            current = Mathf.Clamp(current, 0f, max);
            if (Mathf.Approximately(current, health) && Mathf.Approximately(max, maxHealth)) return;
            health = current;
            maxHealth = max;
            Changed?.Invoke();
        }

        /// <summary>Heals a living player without exceeding maximum HP.</summary>
        public void Heal(float amount)
        {
            if (chiMirrored) return;   // a mirror never heals itself: the server heals its body and the HP comes back (item 5)
            if (!IsAlive || !float.IsFinite(amount) || amount <= 0f) return;
            health = Mathf.Min(maxHealth, health + amount);
            SyncNetworkState();
            Changed?.Invoke();
        }

        /// <summary>Restores both resources; usable by future respawn systems and explicit playtest controls.</summary>
        public void RestoreAll()
        {
            if (!initialized || RemoteView) return;
            health = maxHealth;
            chi = maxChi;
            SyncNetworkState();
            Changed?.Invoke();
        }
    }
}

