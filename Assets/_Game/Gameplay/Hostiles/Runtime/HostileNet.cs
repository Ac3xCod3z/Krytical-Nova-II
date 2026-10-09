using System;
using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// A Hostile on the network. Where the game is decided (server, host or offline) it gives the Hostile its
    /// numbers, wakes its brain and reports what the brain does; on a client the brain stays off and this plays
    /// back what the server reports on the same Animator. Health and death are the CombatEntity's, position is
    /// the NetworkTransform's.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatEntity))]
    public sealed class HostileNet : NetworkBehaviour
    {
        // The parameters of the Animator Controller every Hostile is built with.
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int IdleParam = Animator.StringToHash("Idle Active");
        private static readonly int CombatParam = Animator.StringToHash("Combat State Active");
        private static readonly int StunnedParam = Animator.StringToHash("Stunned Active");
        private static readonly int AttackIndexParam = Animator.StringToHash("Attack Index");
        private static readonly int AttackParam = Animator.StringToHash("Attack");
        private static readonly int HitIndexParam = Animator.StringToHash("Hit Index");
        private static readonly int HitParam = Animator.StringToHash("Hit");
        private static readonly int DeathIndexParam = Animator.StringToHash("Death Index");
        private static readonly int DeadParam = Animator.StringToHash("Dead");

        private const float AnimSendInterval = 0.1f;
        private const float IdleSpeed = 0.05f;

        [SerializeField] private HostileDefinition definition;
        [SerializeField] private HostileStatCurve curve;
        [SerializeField] private Animator animator;

        private readonly NetworkVariable<int> netLevel = new NetworkVariable<int>(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<HostileAnimState> netAnim =
            new NetworkVariable<HostileAnimState>(writePerm: NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private IHostileBrain brain;
        private HostileStats stats;
        private int level;
        private bool setUp;
        private float nextAnimSend;

        /// <summary>Every Hostile that is in the world on this peer.</summary>
        public static IReadOnlyList<HostileNet> Active => active;
        private static readonly List<HostileNet> active = new List<HostileNet>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active.Clear();

        /// <summary>Raised on every peer when an attack starts: (attack index from 0, seconds until it lands, heavy).</summary>
        public event Action<int, float, bool> AttackStarted;
        /// <summary>Raised on every peer when the level is known or changes (a GM spawn at another level).</summary>
        public event Action LevelChanged;

        public HostileDefinition Definition => definition;
        public HostileStatCurve Curve => curve;
        public CombatEntity Entity => entity;
        public IHostileBrain Brain => brain;
        public HostileStats Stats => stats;
        public int Level => level > 0 ? level : definition != null ? definition.level : 1;
        /// <summary>True for a Hostile a GM command put in the world, so the same tools can clear it again.</summary>
        public bool GmSpawned { get; set; }

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            entity = GetComponent<CombatEntity>();
            brain = GetComponent<IHostileBrain>();
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void OnEnable() => active.Add(this);
        private void OnDisable() => active.Remove(this);

        private void Start()
        {
            // A Hostile that is not on the network (offline play, an edit-mode test scene) decides for itself.
            NetworkManager manager = NetworkManager.Singleton;
            if (!IsSpawned && (manager == null || !manager.IsListening)) WakeWithAuthority();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netLevel.OnValueChanged += OnNetLevelChanged;
            netAnim.OnValueChanged += OnNetAnimChanged;
            if (IsServer)
            {
                WakeWithAuthority();
                netLevel.Value = Level;
                if (SpatialInterestManager.Instance != null) SpatialInterestManager.Instance.RegisterEntity(NetworkObject);
            }
            else
            {
                // The clips carry the events that make the brain strike. A client has no brain to hear them.
                if (animator != null) animator.fireEvents = false;
                level = netLevel.Value;
                ApplyIdentity();
                Play(netAnim.Value, default, true);
            }
        }

        public override void OnNetworkDespawn()
        {
            netLevel.OnValueChanged -= OnNetLevelChanged;
            netAnim.OnValueChanged -= OnNetAnimChanged;
            if (IsServer && SpatialInterestManager.Instance != null) SpatialInterestManager.Instance.UnregisterEntity(NetworkObject);
            base.OnNetworkDespawn();
        }

        /// <summary>
        /// Server or offline: sets the level this Hostile fights at (0 = its species' own). Call before it is
        /// spawned on the network, or at any time after: the numbers, the health and the name follow.
        /// </summary>
        public void SetLevel(int newLevel)
        {
            if (!HasAuthority) return;
            level = newLevel > 0 ? newLevel : 0;
            if (!setUp) return;
            ApplyNumbers();
            if (IsSpawned) netLevel.Value = Level;
            LevelChanged?.Invoke();
        }

        private void WakeWithAuthority()
        {
            if (setUp) return;
            setUp = true;
            ApplyNumbers();
            if (brain != null) brain.SetAwake(true);
        }

        private void ApplyNumbers()
        {
            if (definition == null || curve == null)
            {
                Debug.LogWarning($"[Hostiles] {name} has no definition or stat curve: it keeps its prefab values.", this);
                return;
            }
            stats = definition.StatsFrom(curve, Level);
            entity.InitializeEntity();
            stats.ApplyTo(entity.CombatStats);
            entity.SetMaxHealthOverride(stats.MaxHealth);
            ApplyIdentity();
            if (brain != null) brain.Configure(definition, stats);
        }

        private void ApplyIdentity()
        {
            if (definition != null) entity.SetEntityName(definition.displayName);
        }

        private void OnNetLevelChanged(int previous, int current)
        {
            if (IsServer) return;
            level = current;
            LevelChanged?.Invoke();
        }

        // ------------------------------------------------------------------ animation: what lasts

        private void Update()
        {
            if (!setUp || !IsSpawned || !IsServer || animator == null || Time.time < nextAnimSend) return;
            nextAnimSend = Time.time + AnimSendInterval;

            HostileAnimState state = netAnim.Value;
            state.Speed = animator.GetFloat(SpeedParam);
            state.InCombat = animator.GetBool(CombatParam);
            state.Stunned = animator.GetBool(StunnedParam);
            bool dead = !entity.IsAlive;
            if (dead && !state.Dead) state.DeathIndex = (byte)Mathf.Max(1, animator.GetInteger(DeathIndexParam));
            state.Dead = dead;
            if (!state.Equals(netAnim.Value)) netAnim.Value = state;
        }

        private void OnNetAnimChanged(HostileAnimState previous, HostileAnimState current)
        {
            if (!IsServer) Play(current, previous, false);
        }

        private void Play(HostileAnimState state, HostileAnimState previous, bool joining)
        {
            if (animator == null) return;
            animator.SetFloat(SpeedParam, state.Speed);
            animator.SetBool(IdleParam, state.Speed < IdleSpeed);
            animator.SetBool(CombatParam, state.InCombat);
            animator.SetBool(StunnedParam, state.Stunned);
            if (state.Dead && (joining || !previous.Dead))
            {
                animator.SetInteger(DeathIndexParam, Mathf.Max(1, (int)state.DeathIndex));
                animator.SetTrigger(DeadParam);
            }
            else if (!state.Dead && previous.Dead)
            {
                // Back from the pool: stand up without playing anything.
                animator.Rebind();
                animator.Update(0f);
            }
        }

        // ------------------------------------------------------------------ animation: moments

        /// <summary>The brain started an attack. Server or offline; every client plays it and may warn of it.</summary>
        public void ReportAttack(int attackIndex)
        {
            if (!HasAuthority || definition == null) return;
            bool known = attackIndex >= 0 && attackIndex < definition.attacks.Length;
            float windup = known ? definition.attacks[attackIndex].windupSeconds : 0f;
            bool heavy = known && definition.attacks[attackIndex].heavy;
            if (IsSpawned) AttackClientRpc((byte)Mathf.Clamp(attackIndex, 0, 255), windup, heavy);
            else AttackStarted?.Invoke(attackIndex, windup, heavy);
        }

        /// <summary>The Hostile flinched from a hit. Server or offline; every client plays it.</summary>
        public void ReportHit(int hitIndex)
        {
            if (HasAuthority && IsSpawned) HitClientRpc((byte)Mathf.Clamp(hitIndex, 0, 255));
        }

        [ClientRpc]
        private void AttackClientRpc(byte attackIndex, float windupSeconds, bool heavy)
        {
            if (!IsServer && animator != null)
            {
                animator.SetInteger(AttackIndexParam, attackIndex + 1);
                animator.SetTrigger(AttackParam);
            }
            AttackStarted?.Invoke(attackIndex, windupSeconds, heavy);
        }

        [ClientRpc]
        private void HitClientRpc(byte hitIndex)
        {
            if (IsServer || animator == null) return;
            animator.SetInteger(HitIndexParam, hitIndex);
            animator.SetTrigger(HitParam);
        }
    }
}
