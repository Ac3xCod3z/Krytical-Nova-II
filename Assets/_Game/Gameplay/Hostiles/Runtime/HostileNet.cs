using System;
using System.Collections.Generic;
using KryticalNova.Core;
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
    public sealed class HostileNet : NetworkBehaviour, IHitChanceModifier
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
        [SerializeField] private HostileSettings settings;
        [SerializeField] private LevelGapSettings levelGap;
        [SerializeField] private Animator animator;

        private readonly NetworkVariable<int> netLevel = new NetworkVariable<int>(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<HostileAnimState> netAnim =
            new NetworkVariable<HostileAnimState>(writePerm: NetworkVariableWritePermission.Server);
        // The NetworkObjectId of the player who holds the kill, or 0. Other players see a mark on the nameplate.
        private readonly NetworkVariable<ulong> netTag = new NetworkVariable<ulong>(writePerm: NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private IHostileBrain brain;
        private HostileStats stats;
        private int level;
        private bool setUp;
        private bool asleep;
        private float nextAnimSend;
        private Coroutine leaving;
        private PlayerCharacterProgression tagHolder;
        private float tagRefreshed;

        /// <summary>Every Hostile that is in the world on this peer.</summary>
        public static IReadOnlyList<HostileNet> Active => active;
        private static readonly List<HostileNet> active = new List<HostileNet>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active.Clear();
            AnyKilled = null;
        }

        /// <summary>Raised on every peer when an attack starts: (attack index from 0, seconds until it lands, heavy).</summary>
        public event Action<int, float, bool> AttackStarted;
        /// <summary>Raised where the game is decided for every Hostile that is killed, with the blow that did it.</summary>
        public static event Action<HostileNet, KillingBlow> AnyKilled;
        /// <summary>Raised on every peer when the player who holds the kill changes.</summary>
        public event Action TagChanged;
        /// <summary>Raised where the game is decided when this Hostile is killed.</summary>
        public event Action<HostileNet> Killed;
        /// <summary>Raised on every peer when the level is known or changes (a GM spawn at another level).</summary>
        public event Action LevelChanged;

        public HostileDefinition Definition => definition;
        public HostileStatCurve Curve => curve;
        public CombatEntity Entity => entity;
        public IHostileBrain Brain => brain;
        public HostileStats Stats => stats;
        public int Level => level > 0 ? level : definition != null ? definition.level : 1;
        public HostileSettings Settings => settings;
        public LevelGapSettings LevelGap => levelGap;
        /// <summary>True for a Hostile a GM command put in the world, so the same tools can clear it again.</summary>
        public bool GmSpawned { get; set; }
        /// <summary>The Hostile Zone it lives in, or null for one spawned outside any zone.</summary>
        public HostileZone Zone { get; set; }
        /// <summary>Where it appeared this life: where it goes back to when it gives up a chase.</summary>
        public Vector3 Home { get; private set; }
        /// <summary>True while its zone sleeps: no brain, no animation, until a player comes near again.</summary>
        public bool Asleep => asleep;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            entity = GetComponent<CombatEntity>();
            brain = GetComponent<IHostileBrain>();
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void OnEnable()
        {
            active.Add(this);
            entity.Killed += OnKilled;
            entity.Damaged += OnDamaged;
            // Taken from the pool with no session running: a new life starts here. On the network it starts
            // in OnNetworkSpawn, which follows.
            if (setUp && Offline) Revive();
        }

        private void OnDisable()
        {
            active.Remove(this);
            entity.Killed -= OnKilled;
            entity.Damaged -= OnDamaged;
            tagHolder = null;
            if (leaving != null) { StopCoroutine(leaving); leaving = null; }
        }

        private static bool Offline
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager == null || !manager.IsListening;
            }
        }

        private void Start()
        {
            // A Hostile that is not on the network (offline play, an edit-mode test scene) decides for itself.
            if (!IsSpawned && Offline) WakeWithAuthority();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netLevel.OnValueChanged += OnNetLevelChanged;
            netAnim.OnValueChanged += OnNetAnimChanged;
            netTag.OnValueChanged += OnNetTagChanged;
            if (IsServer)
            {
                netAnim.Value = default; // a body from the pool was last seen dead
                netTag.Value = 0;
                if (setUp) Revive();
                else WakeWithAuthority();
                netLevel.Value = Level;
                if (SpatialInterestManager.Instance != null) SpatialInterestManager.Instance.RegisterEntity(NetworkObject);
            }
            else
            {
                // The clips carry the events that make the brain strike. A client has no brain to hear them.
                if (animator != null)
                {
                    animator.fireEvents = false;
                    Stand();
                }
                level = netLevel.Value;
                ApplyIdentity();
                Play(netAnim.Value, default, true);
            }
        }

        public override void OnNetworkDespawn()
        {
            netLevel.OnValueChanged -= OnNetLevelChanged;
            netAnim.OnValueChanged -= OnNetAnimChanged;
            netTag.OnValueChanged -= OnNetTagChanged;
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
            Home = transform.position;
            ApplyNumbers();
            if (brain != null)
            {
                brain.SetAwake(!asleep);
                brain.SetHome(Home, settings != null ? settings.wanderRadius : 8);
            }
        }

        /// <summary>A new life for a body taken from the pool: full health, fresh numbers, brain on, standing.</summary>
        private void Revive()
        {
            Home = transform.position;
            asleep = false;
            SetTag(null);
            Stand();
            ApplyNumbers();
            if (brain != null)
            {
                brain.SetAwake(true);
                brain.SetHome(Home, settings != null ? settings.wanderRadius : 8);
            }
        }

        private void Stand()
        {
            if (animator == null) return;
            animator.enabled = true;
            animator.Rebind();
            animator.Update(0f);
        }

        /// <summary>
        /// Server or offline: puts a living Hostile to sleep or wakes it. Asleep it has no brain and does not
        /// animate; it stays where it is and can still be hit, which wakes nothing by itself.
        /// </summary>
        public void SetAsleep(bool value)
        {
            if (!HasAuthority || asleep == value) return;
            asleep = value;
            if (!setUp || !entity.IsAlive) return;
            if (brain != null) brain.SetAwake(!value);
            if (animator != null) animator.enabled = !value;
        }

        // ------------------------------------------------------------------ death

        private void OnKilled(KillingBlow blow)
        {
            // Raised where the game is decided only. The body lies, burns away (HostileCorpse, on every screen)
            // and then leaves the world for the pool.
            if (leaving != null) StopCoroutine(leaving);
            leaving = StartCoroutine(LeaveAfterCorpse());
            Killed?.Invoke(this);
            AnyKilled?.Invoke(this, blow);
            SetTag(null);
        }

        private System.Collections.IEnumerator LeaveAfterCorpse()
        {
            yield return new WaitForSeconds(settings != null ? settings.CorpseLifetime : 4f);
            leaving = null;
            if (!entity.IsAlive) HostileSpawner.Remove(this);
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

        // ------------------------------------------------------------------ who holds the kill

        /// <summary>
        /// Where the game is decided: the player whose kill this is. The first player to damage a Hostile holds
        /// it, with their Squad; the hold ends when they have dealt it no damage for the settings' time, when
        /// they die, or when the Hostile dies or gives up the chase. Null when nobody holds it.
        /// </summary>
        public PlayerCharacterProgression TagHolder
        {
            get
            {
                if (tagHolder != null && TagLapsed) SetTag(null);
                return tagHolder;
            }
        }

        /// <summary>On every peer: true when a player holds the kill and it is not the one given (nor their Squad).</summary>
        public bool IsTaggedByAnother(NetworkObject player)
        {
            ulong holder = IsSpawned ? netTag.Value : tagHolder != null && tagHolder.NetworkObject != null ? tagHolder.NetworkObjectId : 0;
            if (holder == 0 || player == null || holder == player.NetworkObjectId) return false;
            var mine = player.GetComponent<KryticalNova.Social.PlayerParty>();
            if (mine == null || !mine.InParty || !IsSpawned) return true;
            return !(NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(holder, out NetworkObject other)
                     && other.TryGetComponent(out KryticalNova.Social.PlayerParty theirs) && theirs.PartyId == mine.PartyId);
        }

        private bool TagLapsed
        {
            get
            {
                if (tagHolder == null) return true;
                float timeout = levelGap != null ? levelGap.tagTimeoutSeconds : 10f;
                if (Time.time - tagRefreshed > timeout) return true;
                return !tagHolder.TryGetComponent(out CombatEntity holder) || !holder.IsAlive;
            }
        }

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            // Raised on every peer that applies damage; only the deciding one keeps the tag. Being chased or
            // missed is not a hit.
            if (!HasAuthority || attacker == null || result.IsEvaded || result.DamageDealt <= 0f) return;
            var player = attacker.GetComponent<PlayerCharacterProgression>();
            if (player == null) return;
            if (tagHolder == null || TagLapsed) SetTag(player);
            else if (SameCredit(tagHolder, player)) tagRefreshed = Time.time;
        }

        private static bool SameCredit(PlayerCharacterProgression holder, PlayerCharacterProgression other)
        {
            if (holder == other) return true;
            return holder.TryGetComponent(out KryticalNova.Social.PlayerParty a) && other.TryGetComponent(out KryticalNova.Social.PlayerParty b)
                   && a.InParty && a.PartyId == b.PartyId;
        }

        private void SetTag(PlayerCharacterProgression player)
        {
            tagHolder = player;
            tagRefreshed = Time.time;
            ulong id = player != null && player.IsSpawned ? player.NetworkObjectId : 0;
            if (IsSpawned && IsServer && netTag.Value != id) netTag.Value = id;
            else if (!IsSpawned) TagChanged?.Invoke();
        }

        /// <summary>Drops the hold on the kill: the Hostile gave up the chase and was healed.</summary>
        public void ReleaseTag()
        {
            if (HasAuthority && tagHolder != null) SetTag(null);
        }

        private void OnNetTagChanged(ulong previous, ulong current) => TagChanged?.Invoke();

        // ------------------------------------------------------------------ level gap

        /// <summary>How much harder this Hostile is to hit for an attacker of another level, in percent points.</summary>
        public float HitChanceModifierFor(CharacterCombatStats attacker)
        {
            if (levelGap == null || attacker == null) return 0f;
            var player = attacker.GetComponent<PlayerCharacterProgression>();
            return player == null ? 0f : LevelGapRules.HitChanceModifier(levelGap, LevelGapRules.Gap(Level, player.CurrentLevel));
        }

        // ------------------------------------------------------------------ animation: what lasts

        private void Update()
        {
            if (!setUp || asleep || !IsSpawned || !IsServer || animator == null || Time.time < nextAnimSend) return;
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
            else ShowAttack(attackIndex, windup, heavy);
        }

        /// <summary>
        /// One of its attacks reached a player and was rolled. Server or offline; every client shows the blow,
        /// and the player it was aimed at sees the number.
        /// </summary>
        public void ReportStrike(CombatEntity target, in DamageResult result, int attackIndex)
        {
            if (!HasAuthority || target == null) return;
            bool heavy = definition != null && attackIndex >= 0 && attackIndex < definition.attacks.Length && definition.attacks[attackIndex].heavy;
            byte flags = (byte)((result.IsCritical ? StrikeCritical : 0) | (result.IsEvaded ? StrikeEvaded : 0)
                                | (result.IsReflected ? StrikeReflected : 0) | (heavy ? StrikeHeavy : 0));
            NetworkObject targetObject = target.NetworkObject;
            if (IsSpawned && targetObject != null && targetObject.IsSpawned) StrikeClientRpc(targetObject.NetworkObjectId, flags, result.DamageDealt);
            else if (!IsSpawned) ShowStrike(target.transform, flags, result.DamageDealt);
        }

        private const byte StrikeCritical = 1, StrikeEvaded = 2, StrikeReflected = 4, StrikeHeavy = 8;
        private const float HitHeight = 1.1f;

        [ClientRpc]
        private void StrikeClientRpc(ulong targetObjectId, byte flags, float damage)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetObjectId, out NetworkObject target))
                ShowStrike(target.transform, flags, damage);
        }

        /// <summary>The blow as this screen shows it: the comic hit of the game feel, and the number for the player it hit.</summary>
        private void ShowStrike(Transform target, byte flags, float damage)
        {
            bool evaded = (flags & StrikeEvaded) != 0, critical = (flags & StrikeCritical) != 0, reflected = (flags & StrikeReflected) != 0;
            var targetObject = target.GetComponent<NetworkObject>();
            bool targetIsLocal = targetObject == null || !targetObject.IsSpawned || targetObject.IsLocalPlayer;
            Vector3 toAttacker = transform.position - target.position;
            toAttacker.y = 0f;
            toAttacker = toAttacker.sqrMagnitude > 0.0001f ? toAttacker.normalized : -target.forward;
            Vector3 point = target.position + Vector3.up * (HitHeight - 1f) + toAttacker * 0.3f;

            if (targetIsLocal)
                ClientPresentationRegistry.Feedback?.ShowCombatFeedback(target.position, new DamageResult
                {
                    DamageDealt = damage, IsCritical = critical, IsEvaded = evaded, IsReflected = reflected,
                    ReflectedDamage = reflected ? damage : 0f
                });
            if (evaded) return;
            ClientPresentationRegistry.Feel?.Hit(new HitFeel
            {
                Point = point,
                TowardAttacker = toAttacker,
                Attacker = transform,
                Target = target,
                ComboStep = (flags & StrikeHeavy) != 0 ? 3 : 1,
                IsCritical = critical,
                IsReflected = reflected,
                AttackerIsLocalPlayer = false,
                TargetIsLocalPlayer = targetIsLocal,
            });
        }

        /// <summary>The start of an attack as this screen shows it. A heavy one is announced before it lands.</summary>
        private void ShowAttack(int attackIndex, float windupSeconds, bool heavy)
        {
            if (heavy)
            {
                float height = definition != null ? definition.bodySize.y * definition.scale : 1.8f;
                ClientPresentationRegistry.Feedback?.ShowFloatingText(transform.position + Vector3.up * height, "!", "crit");
                ClientPresentationRegistry.Feel?.Swing(transform, 3, false, windupSeconds);
            }
            AttackStarted?.Invoke(attackIndex, windupSeconds, heavy);
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
            ShowAttack(attackIndex, windupSeconds, heavy);
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
