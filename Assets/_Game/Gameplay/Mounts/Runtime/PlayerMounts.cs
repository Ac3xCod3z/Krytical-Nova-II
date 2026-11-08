using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// A character's mounts (owner, 25 Oct 2026): up to four, one per box of the Mount window, each added by using its
    /// item from the Pack. One is selected; the Ride key puts its owner on it and takes them off. The selected mount
    /// earns EXP from its owner's Hostile kills (ridden or not) and grows through three stages, 0, 50 and 100 %, as a
    /// pet grows through its five; it gets hungry only while ridden and eats Mount Feed. It gives two stats of its own
    /// while selected and fed, and its riding speed grows with it.
    /// The server decides everything and saves the mounts with the character. Every machine knows the selected mount,
    /// its growth, whether it is fed and where its rider is (<see cref="MountNetState"/>): enough for the stats, the
    /// speed and the look (<see cref="MountRider"/>). The owner alone also gets all four boxes (<see cref="MountBook"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMounts : NetworkBehaviour, IItemUseHandler, ISaveParticipant, IBodyMounts
    {
        // Mounts or dismounts from far too often are ignored: one press every this many seconds is plenty.
        private const float RidePressSeconds = 0.4f;
        // A getting-on or getting-off that has no clip still takes this long.
        private const float MinPhaseSeconds = 0.3f;
        // On a getting-on or getting-off the rider stays put: walking and running go at this (any speed above 0 is a ride).
        private const float StandStillSpeed = 0.01f;

        [SerializeField] private MountCatalog catalog;

        private readonly NetworkVariable<MountNetState> netState = new NetworkVariable<MountNetState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<MountBook> netBook = new NetworkVariable<MountBook>(default,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private CharacterCombatStats combatStats;
        private PlayerMovementSkills movement;
        private PlayerAnimancerMotor motor;
        private CombatEntity entity;
        private PlayerChat chat;

        private readonly object statSource = new object();
        private readonly List<StatModifier> modifiers = new List<StatModifier>(2);

        // Where the game is decided: the working copy.
        private MountBook book;
        private int selected;
        private RidePhase phase;
        private bool running; // the getting on or off in play is the running kind
        private bool endsRunning = true; // a running getting-off comes up running (path left) or ends standing
        private float phaseEnds;
        private double hungerCarry;
        private float nextPublish;
        private bool warnedHungry;
        private float nextRidePress;

        // Every machine: what the stats, speed and look follow.
        private MountNetState shown;

        /// <summary>Raised on every machine when the selected mount, its growth, its hunger state or the rider's phase changes.</summary>
        public event Action Changed;
        /// <summary>Raised on every machine at the moment the selected mount evolves: (mount, the stage it reached).</summary>
        public event Action<MountDefinition, int> Evolved;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerMounts Local => LocalPlayer.Get<PlayerMounts>();

        public MountCatalog Catalog => catalog;
        public MountSettings Settings => catalog != null ? catalog.Settings : null;
        private MountTuning Tuning => catalog != null ? catalog.Tuning : MountTuning.Default;
        // A client view of the entity netcode only shows what the server decided (M8 8.4.8): NGO's !IsSpawned let it ride and drain itself.
        private bool HasAuthority => (!IsSpawned || IsServer) && !EntityView;
        private bool EntityView => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ClientView };
        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private IBodyRequests Requests => BodyRoleTag.RequestsOf(this) is IBodyRequests requests ? requests : null;
        // A DOTS server body has no motor: its ghost says when it is in a jump's flight (NGO's motor state Jumping).
        private bool ServerBodyAirborne => EntityServer && BodyRoleTag.Of(this).Mover is { Airborne: true };
        private MountBook viewBook;   // the own view of the entity netcode: the server's four boxes (NGO's netBook)
        private bool bookDelivered;   // a server body of the entity netcode: its player has the latest boxes

        /// <summary>The selected mount, or null.</summary>
        public MountDefinition Selected => catalog != null ? catalog.Get(shown.selected) : null;
        public int SelectedId => shown.selected;
        public RidePhase Phase => shown.phase;
        /// <summary>The getting on or off in play is the running kind: the Ride key was pressed while running.</summary>
        public bool IsRunningTransition => shown.running;
        /// <summary>A running getting-off ends running (the clicked spot is still far) rather than standing.</summary>
        public bool DismountEndsRunning => shown.endsRunning;
        /// <summary>On the mount, getting on or getting off.</summary>
        public bool IsMounted => shown.phase != RidePhase.OnFoot;
        public int GrowthPercent => shown.selected != 0 ? shown.growth : 0;
        public bool IsFed => shown.selected != 0 && shown.fed;
        public int Stage => MountRules.Stage(GrowthPercent);

        /// <summary>All four boxes. Known to the owner and where the game is decided.</summary>
        public MountBook Book => HasAuthority ? book : EntityView ? viewBook : netBook.Value;
        public bool Owns(int box) => Book.Owns(box);
        public MountState StateOf(int box) => Book.Get(box);
        public int GrowthOf(int box) => MountRules.GrowthPercent(StateOf(box), Tuning);
        public float GrowthExactOf(int box) => MountRules.GrowthExact(StateOf(box), Tuning);

        private void Awake()
        {
            combatStats = GetComponent<CharacterCombatStats>();
            movement = GetComponent<PlayerMovementSkills>();
            motor = GetComponent<PlayerAnimancerMotor>();
            entity = GetComponent<CombatEntity>();
            chat = GetComponent<PlayerChat>();
        }

        private void OnEnable()
        {
            HostileKillRewards.Rewarded += OnHostileKill;
            if (entity != null) entity.Died += OnDied;
            if (!IsSpawned) Publish();
        }

        private void OnDisable()
        {
            HostileKillRewards.Rewarded -= OnHostileKill;
            if (entity != null) entity.Died -= OnDied;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            netBook.OnValueChanged += OnBookChanged;
            if (IsServer) Publish();
            else Show(netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            netBook.OnValueChanged -= OnBookChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(MountNetState previous, MountNetState current)
        {
            if (!IsServer) Show(current);
        }

        private void OnBookChanged(MountBook previous, MountBook current)
        {
            if (!IsServer) Changed?.Invoke();
        }

        // ------------------------------------------------------------------ requests (owner)

        /// <summary>The owner's machine: makes an owned mount the selected one (from the Mount window).</summary>
        public void RequestSelect(int box)
        {
            if (Requests is { } requests) { requests.MountOp(MountOpKind.Select, box); return; }   // the entity netcode's own view (M8 8.4.8)
            if (EntityView) return;
            if (!IsSpawned) ServerSelect(box);
            else if (IsServer) ServerSelect(box);
            else SelectRpc((byte)box);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SelectRpc(byte box)
        {
            if (!RpcBudget.Allow(this, CallKind.Cosmetic, nameof(SelectRpc))) return;
            ServerSelect(box);
        }

        /// <summary>The owner's machine: the Ride key. Gets on the selected mount, or off it.</summary>
        public void RequestRide()
        {
            if (Requests is { } requests) { requests.MountOp(MountOpKind.Ride, 0); return; }   // the entity netcode's own view (M8 8.4.8)
            if (EntityView) return;
            if (!IsSpawned || IsServer) ServerToggleRide();
            else RideRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RideRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RideRpc))) return;
            ServerToggleRide();
        }

        /// <summary>Why the Ride key would do nothing now (shown to the player), or null.</summary>
        public string RideBlockedReason()
        {
            if (IsMounted) return null;
            if (catalog == null) return "Mounts are not set up.";
            MountDefinition mount = Selected;
            if (mount == null) return Book.owned == 0 ? "You have no mount. Use a mount item from your Pack to add one."
                : "Select a mount in the Mount window first.";
            if (!mount.IsBuilt) return $"The {mount.displayName} cannot be ridden yet.";
            if (entity != null && !entity.IsAlive) return "You cannot ride while defeated.";
            if (motor != null && motor.CurrentState != CharacterAnimState.Locomotion) return "Finish what you are doing first.";
            if (ServerBodyAirborne) return "Finish what you are doing first.";
            return null;
        }

        // ------------------------------------------------------------------ the server's side

        public void ServerSelect(int box)
        {
            if (!HasAuthority || catalog == null || !book.Owns(box) || box == selected) return;
            if (phase != RidePhase.OnFoot) { Notify("Get off your mount before choosing another.", toast: true); return; }
            selected = box;
            warnedHungry = Settings != null && book.Get(box).HungerPercent <= Settings.hungerWarningPercent;
            Publish();
        }

        public void ServerToggleRide()
        {
            if (!HasAuthority || Time.unscaledTime < nextRidePress) return;
            nextRidePress = Time.unscaledTime + RidePressSeconds;
            if (phase == RidePhase.Riding || phase == RidePhase.GettingOn) { BeginPhase(RidePhase.GettingOff); return; }
            if (phase != RidePhase.OnFoot) return;
            string reason = RideBlockedReason();
            if (reason != null) { Notify(reason, toast: true); return; }
            BeginPhase(RidePhase.GettingOn);
        }

        /// <summary>Server only: off the mount at once, with no clip (death, a jump, a vault, a wall run).</summary>
        public void ServerDismountNow()
        {
            if (!HasAuthority || phase == RidePhase.OnFoot) return;
            phase = RidePhase.OnFoot;
            running = false;
            PublishBook();
            Publish();
        }

        private void BeginPhase(RidePhase next)
        {
            MountDefinition mount = catalog != null ? catalog.Get(selected) : null;
            // Running (the Run selection and moving) gets the running kind: no stop (owner, 6 Nov 2026). Walking stops
            // and gets the standing kind, as standing still does.
            running = MountRules.IsRunningTransition(movement != null && movement.IsRunning, motor != null && motor.IsTravelling);
            AnimationClip clip = mount == null ? null : next == RidePhase.GettingOn ? mount.mountUp : next == RidePhase.GettingOff ? mount.dismount : null;
            int stage = MountRules.Stage(MountRules.GrowthPercent(book.Get(selected), Tuning));
            phase = next;
            // A summon-hop mount is timed by Mount Settings: the rider is free before the clip ends (its settle, landing
            // or roll plays on). Older mounts take their clip's length.
            MountSettings settings = Settings;
            endsRunning = true;
            if (next == RidePhase.GettingOff && running && settings != null)
            {
                // Owner, 7 Nov 2026: up running when the clicked spot is still 4 m or more away as the roll ends. The
                // rider goes at on-foot speed through the dismount (Show sets it), so that is the speed to reckon with.
                float footSpeed = 0f;
                if (movement != null) { movement.SetRideSpeeds(0f, 0f); footSpeed = movement.CurrentSpeed; }
                MountTransition roll = TransitionInPlay(false, true, true);
                endsRunning = MountRules.DismountEndsRunning(PathLeft(), footSpeed, roll.rollStartsAt, roll.rollEndsAt,
                    settings.rollSpeedScale, settings.rollIntoRunMinDistance);
            }
            // Review, 7 Nov 2026: a running getting-off is timed by its own clip (run or stand ending), not the standing one.
            if (mount != null)
            {
                AnimationClip own = null;
                if (next == RidePhase.GettingOn && running) own = mount.RiderClip(stage, l => l.mountUpRunning, mount.mountUpRunning);
                if (next == RidePhase.GettingOff && running)
                {
                    if (!endsRunning) own = mount.RiderClip(stage, l => l.dismountRunningToIdle, mount.dismountRunningToIdle);
                    if (own == null) own = mount.RiderClip(stage, l => l.dismountRunning, mount.dismountRunning);
                }
                if (next == RidePhase.GettingOn && !running) own = mount.RiderClip(stage, l => l.mountUp, mount.mountUp);
                if (next == RidePhase.GettingOff && !running) own = mount.RiderClip(stage, l => l.dismount, mount.dismount);
                if (own != null) clip = own;
            }
            if (next == RidePhase.GettingOff) PublishBook(); // what riding cost in hunger, before the ride ends
            Publish();
            float seconds = clip != null ? clip.length : 0f;
            if (mount != null && mount.hopMountUp && settings != null)
            {
                // Free the same time before the clip's end as the transition says (the settle, landing or roll plays on
                // after, MountRider). For the new clips that is exactly freeSeconds; an older, longer clip (the standing
                // dismount, left as it was) is not cut short.
                MountTransition t = TransitionInPlay(next == RidePhase.GettingOn, running, endsRunning);
                seconds = clip != null && !t.leap ? Mathf.Max(t.freeSeconds, clip.length - (t.seconds - t.freeSeconds)) : t.freeSeconds;
            }
            phaseEnds = Time.time + Mathf.Max(MinPhaseSeconds, seconds);
        }

        /// <summary>Metres of path left to the clicked spot (the server's own copy walks the player), 0 with no path.</summary>
        /// <summary>
        /// The getting on or off as it plays: the settings' transition, or for a getting-off the leap built from the
        /// game's own jump (MountSettings.leapWithJump). Every machine and every user of the timing asks here.
        /// </summary>
        public MountTransition TransitionInPlay(bool gettingOn, bool running, bool endsRunning)
        {
            MountSettings settings = Settings;
            if (settings == null) return null;
            MountTransition t = settings.Transition(gettingOn, running, endsRunning);
            if (gettingOn || !settings.leapWithJump || t == null) return t;
            if (jumpMotor == null) jumpMotor = GetComponent<KryticalNova.Player.PlayerJumpMotor>();
            if (jumpMotor == null || !jumpMotor.TryGetWarpTimes(out float takeoff, out float apex, out float land)) return t;
            // The feet leave the saddle a moment into the jump's rise (measured: MountSettings.leapFeetLeaveAfter).
            return t.WithLeap(Mathf.Min(takeoff + settings.leapFeetLeaveAfter, apex), apex, land, settings.leapStandRecoverSeconds);
        }

        private KryticalNova.Player.PlayerJumpMotor jumpMotor;

        private float PathLeft()
        {
            NavMeshAgent agent = GetComponent<NavMeshAgent>();
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || !agent.hasPath) return 0f;
            Vector3 straight = agent.destination - agent.nextPosition;
            straight.y = 0f;
            return Mathf.Max(agent.remainingDistance, straight.magnitude); // remainingDistance reads 0 just after SetPath
        }

        private void OnDied()
        {
            if (HasAuthority) ServerDismountNow();
        }

        // A running getting-off's travel on every machine: the speed profile of MountRules.DismountSpeedShare.
        private float dismountStartedAt, dismountRunSpeed, dismountScale, dismountSpeedSet = -1f;

        private void DriveDismountSpeed()
        {
            MountSettings settings = Settings;
            if (movement == null || settings == null || shown.phase != RidePhase.GettingOff || !shown.running || dismountRunSpeed <= 0f) return;
            MountTransition roll = TransitionInPlay(false, true, shown.endsRunning);
            float share = MountRules.DismountSpeedShare(Time.time - dismountStartedAt, shown.endsRunning, roll.rollStartsAt,
                roll.rollEndsAt, settings.rollSpeedScale, settings.rollSlideSeconds);
            float speed = Mathf.Max(StandStillSpeed, dismountRunSpeed * dismountScale * share);
            if (Mathf.Abs(speed - dismountSpeedSet) < 0.05f) return; // only real changes reach the movement listeners
            dismountSpeedSet = speed;
            movement.SetRideSpeeds(speed, speed);
        }

        private void Update()
        {
            DriveDismountSpeed();
            if (!HasAuthority || catalog == null) return;
            if (EntityServer && !bookDelivered) SendBook();

            if ((phase == RidePhase.GettingOn || phase == RidePhase.GettingOff) && Time.time >= phaseEnds)
            {
                phase = phase == RidePhase.GettingOn ? RidePhase.Riding : RidePhase.OnFoot;
                running = false;
                Publish();
            }
            // A jump, a vault or a wall run takes the rider off: the mount has no clips for them.
            if (phase == RidePhase.Riding && ((motor != null && motor.CurrentState == CharacterAnimState.Jumping) || ServerBodyAirborne)) ServerDismountNow();

            if (phase != RidePhase.Riding || selected == 0) return;
            MountState before = book.Get(selected);
            MountState after = MountRules.Drain(before, Time.deltaTime, Tuning, ref hungerCarry);
            if (after.Equals(before)) return;
            book.Set(selected, true, after);
            MountDefinition mount = catalog.Get(selected);
            string name = mount != null ? mount.displayName : "mount";
            if (before.IsFed && !after.IsFed)
            {
                PublishBook();
                Publish();
                Notify($"Your {name} is starving. Its stats are off, it rides at its slowest and it earns no EXP until it eats Mount Feed.", toast: true);
                return;
            }
            MountSettings settings = Settings;
            if (!warnedHungry && settings != null && after.HungerPercent <= settings.hungerWarningPercent)
            {
                warnedHungry = true;
                Notify($"Your {name} is getting hungry ({after.HungerPercent:0}%). Feed it Mount Feed.", toast: true);
            }
            if (Time.time >= nextPublish) PublishBook();
        }

        // ------------------------------------------------------------------ EXP

        private void OnHostileKill(HostileKill kill)
        {
            if (!HasAuthority || selected == 0 || kill.Credited == null || kill.Credited.gameObject != gameObject) return;
            AddXpUnits(Tuning.KillXp);
        }

        /// <summary>Server only (GM): adds EXP to the selected mount as the design counts it.</summary>
        public void ServerAddXp(float xp)
        {
            if (!HasAuthority || selected == 0 || xp <= 0f) return;
            AddXpUnits((uint)Mathf.Min(uint.MaxValue, Mathf.Round(xp * MountState.XpUnitsPerXp)));
        }

        private void AddXpUnits(uint units)
        {
            MountState before = book.Get(selected);
            MountState after = MountRules.AddXp(before, units, Tuning);
            if (after.Equals(before)) return;
            AfterGrowthChanged(before, after);
        }

        /// <summary>Server only (GM): puts the selected mount at a growth percent, evolving (or devolving) it on the spot.</summary>
        public void ServerSetGrowth(float percent)
        {
            if (!HasAuthority || selected == 0) return;
            MountState before = book.Get(selected);
            AfterGrowthChanged(before, MountRules.WithGrowth(before, percent, Tuning));
        }

        private void AfterGrowthChanged(MountState before, MountState after)
        {
            book.Set(selected, true, after);
            int stageBefore = MountRules.Stage(MountRules.GrowthPercent(before, Tuning));
            int growth = MountRules.GrowthPercent(after, Tuning);
            int stage = MountRules.Stage(growth);
            if (growth != MountRules.GrowthPercent(before, Tuning) || stage != stageBefore)
            {
                PublishBook();
                Publish();
            }
            else if (Time.time >= nextPublish) PublishBook();
            if (stage <= stageBefore) return;
            MountDefinition mount = catalog.Get(selected);
            if (mount == null) return;
            Notify($"Your {mount.displayName} evolved: {mount.StageName(stage)} ({growth}% growth).", toast: false);
            if (IsSpawned) EvolvedRpc((byte)stage);
            else if (EntityServer) MountOwnerLink.Current?.Evolved(this, stage);   // every machine (M8 8.4.8)
            else PresentEvolved(stage);
        }

        [Rpc(SendTo.Everyone)]
        private void EvolvedRpc(byte stage) => PresentEvolved(stage);

        private void PresentEvolved(int stage)
        {
            MountDefinition mount = Selected;
            if (mount == null) return;
            bool mine = KryticalNova.Core.BodyRoleTag.IsMine(this, !IsSpawned || IsOwner);
            ClientPresentationRegistry.Feel?.PetEvolved(transform, mount.Stage(stage).glow, mine);
            Evolved?.Invoke(mount, stage);
        }

        // ------------------------------------------------------------------ hunger

        /// <summary>Why Mount Feed would be wasted now, or null.</summary>
        public string FeedBlockedReason()
        {
            if (selected == 0 || catalog == null) return "Select a mount in the Mount window first.";
            MountDefinition mount = catalog.Get(selected);
            return book.Get(selected).HungerDeficit == 0 ? $"Your {(mount != null ? mount.displayName : "mount")} is not hungry." : null;
        }

        /// <summary>Server only: the selected mount's hunger back to 100 %.</summary>
        public void ServerFeed()
        {
            if (!HasAuthority || selected == 0) return;
            book.Set(selected, true, MountRules.Feed(book.Get(selected)));
            hungerCarry = 0.0;
            warnedHungry = false;
            PublishBook();
            Publish();
            MountDefinition mount = catalog != null ? catalog.Get(selected) : null;
            Notify($"Your {(mount != null ? mount.displayName : "mount")} has eaten its fill.", toast: false);
        }

        /// <summary>Server only (GM): sets the selected mount's hunger, 0–100.</summary>
        public void ServerSetHunger(float percent)
        {
            if (!HasAuthority || selected == 0) return;
            book.Set(selected, true, MountRules.WithHunger(book.Get(selected), percent));
            hungerCarry = 0.0;
            warnedHungry = Settings != null && book.Get(selected).HungerPercent <= Settings.hungerWarningPercent;
            PublishBook();
            Publish();
        }

        // ------------------------------------------------------------------ registering (IItemUseHandler)

        public ItemUseEffect Effect => ItemUseEffect.RegisterMount;

        public string UseBlockedReason(UsedItem item)
        {
            MountDefinition mount = catalog != null ? catalog.ByItem(item.Id) : null;
            if (mount == null) return "That item is not a mount.";
            if (!mount.IsBuilt) return $"The {mount.displayName} cannot be had yet.";
            return book.Owns(mount.mountId) ? $"Your Mount window already holds the {mount.displayName}." : null;
        }

        public void ServerUse(UsedItem item)
        {
            MountDefinition mount = catalog != null ? catalog.ByItem(item.Id) : null;
            if (mount != null) ServerRegister(mount);
        }

        /// <summary>Server only: adds a mount to the Mount window, fresh (no EXP, fed). Selected if nothing else is.</summary>
        public void ServerRegister(MountDefinition mount)
        {
            if (!HasAuthority || mount == null || book.Owns(mount.mountId)) return;
            book.Set(mount.mountId, true, default);
            if (selected == 0) selected = mount.mountId;
            PublishBook();
            Publish();
            // The server cannot know this player's keys: the words name the window and the key, not the letters.
            Notify($"The {mount.displayName} is in your Mount window. Select it there and press the Ride key to ride.", toast: true);
        }

        /// <summary>Server only (GM): takes a mount out of the Mount window again.</summary>
        public void ServerForget(int box)
        {
            if (!HasAuthority || !book.Owns(box)) return;
            if (box == selected)
            {
                ServerDismountNow();
                selected = 0;
            }
            book.Set(box, false, default);
            PublishBook();
            Publish();
        }

        // ------------------------------------------------------------------ publishing

        private void PublishBook()
        {
            nextPublish = Time.time + (Settings != null ? Settings.hungerWriteSeconds : 10f);
            if (IsSpawned && IsServer) netBook.Value = book;
            else if (EntityServer) SendBook();   // its own player's Mount window (M8 8.4.8)
            Changed?.Invoke();
        }

        // A server body of the entity netcode: the boxes to its own player; until its connection is there, again every frame (Update).
        private void SendBook()
        {
            bookDelivered = MountOwnerLink.Current?.Book(this, book.owned, book.xp1, book.xp2, book.xp3, book.xp4,
                book.hunger1, book.hunger2, book.hunger3, book.hunger4) == true;
        }

        /// <summary>Where the game is decided: tells every machine what the selected mount now gives and looks like.</summary>
        private void Publish()
        {
            if (selected != 0 && !book.Owns(selected)) selected = 0;
            MountState state = book.Get(selected);
            var next = new MountNetState
            {
                selected = (byte)selected,
                phase = selected != 0 ? phase : RidePhase.OnFoot,
                growth = (byte)(selected != 0 ? MountRules.GrowthPercent(state, Tuning) : 0),
                fed = selected != 0 && state.IsFed,
                running = selected != 0 && running && (phase == RidePhase.GettingOn || phase == RidePhase.GettingOff),
                endsRunning = endsRunning,
            };
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
        }

        /// <summary>Every machine: the stats and the riding speed that follow from the selected mount.</summary>
        private void Show(MountNetState next)
        {
            bool dismountStarts = next.phase == RidePhase.GettingOff && shown.phase != RidePhase.GettingOff;
            shown = next;
            MountDefinition mount = Selected;

            modifiers.Clear();
            if (mount != null)
            {
                float first = MountRules.StatBonus(mount.firstMax, next.growth, next.fed);
                float second = MountRules.StatBonus(mount.secondMax, next.growth, next.fed);
                if (first > 0f) modifiers.Add(new StatModifier(mount.firstStat, ModifierType.Flat, first, statSource));
                if (second > 0f) modifiers.Add(new StatModifier(mount.secondStat, ModifierType.Flat, second, statSource));
            }
            if (combatStats != null) combatStats.SetSourceModifiers(statSource, modifiers);

            if (movement != null)
            {
                MountSettings settings = Settings;
                if (next.phase == RidePhase.Riding && settings != null)
                    movement.SetRideSpeeds(MountRules.Speed(settings.walkSpeed.x, settings.walkSpeed.y, next.growth, next.fed),
                        MountRules.Speed(settings.runSpeed.x, settings.runSpeed.y, next.growth, next.fed));
                else if ((next.phase == RidePhase.GettingOn || next.phase == RidePhase.GettingOff) && !next.running)
                    movement.SetRideSpeeds(StandStillSpeed, StandStillSpeed);
                // A running getting on or off keeps the on-foot speeds: the rider never stops (owner, 6 Nov 2026).
                else
                {
                    movement.SetRideSpeeds(0f, 0f);
                    // A running getting-off (owner, 7 Nov 2026: "fluid"; retest 2: a shorter roll, then the run or a
                    // slight slide to a stop). Planned once as it starts, on every machine (each walks its own copy of
                    // the path), and driven each frame by DriveDismountSpeed.
                    if (dismountStarts && next.running && settings != null)
                    {
                        MountTransition roll = TransitionInPlay(false, true, next.endsRunning);
                        dismountStartedAt = Time.time;
                        dismountRunSpeed = movement.CurrentSpeed;
                        dismountScale = next.endsRunning ? 1f : MountRules.DismountStandingScale(PathLeft(), dismountRunSpeed,
                            roll.rollStartsAt, roll.rollEndsAt, settings.rollSpeedScale, settings.rollSlideSeconds);
                        dismountSpeedSet = -1f;
                        DriveDismountSpeed();
                    }
                }
            }
            Changed?.Invoke();
        }

        /// <summary>What a stat line of a mount reads at a growth: "+30 Vitality".</summary>
        public static string StatLine(StatType stat, float maxValue, int growth, bool fed) =>
            StatDisplay.FormatModifier(stat, ModifierType.Flat, MountRules.StatBonus(maxValue, growth, fed));

        // ------------------------------------------------------------------ saving (ISaveParticipant)

        public string SaveKey => "mounts";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            var save = new MountSave { selected = selected };
            var entries = new List<MountSaveEntry>(MountRules.Slots);
            for (int box = 1; box <= MountRules.Slots; box++)
            {
                if (!book.Owns(box)) continue;
                MountState state = book.Get(box);
                entries.Add(new MountSaveEntry { id = box, xp = state.XpUnits, hunger = state.HungerDeficit });
            }
            save.mounts = entries.ToArray();
            return JsonUtility.ToJson(save);
        }

        public void RestoreSave(string json)
        {
            book = default;
            selected = 0;
            phase = RidePhase.OnFoot; // nobody comes back into the world in the saddle
            if (!string.IsNullOrEmpty(json))
            {
                MountSave save = JsonUtility.FromJson<MountSave>(json);
                if (save != null && save.mounts != null)
                    foreach (MountSaveEntry entry in save.mounts)
                        if (entry != null) book.Set(entry.id, true, new MountState(entry.xp, entry.hunger));
                if (save != null && book.Owns(save.selected)) selected = save.selected;
            }
            warnedHungry = selected != 0 && Settings != null && book.Get(selected).HungerPercent <= Settings.hungerWarningPercent;
            PublishBook();
            Publish();
        }

        // ------------------------------------------------------------------ telling the owner

        private void Notify(string text, bool toast)
        {
            if (chat != null) chat.ServerSystem(text);
            if (!toast) return;
            if (EntityServer) { OwnerMessages.TryTell(this, text); return; }   // its player's message line (M8 8.4.8)
            if (IsSpawned) ToastRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void ToastRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ the entity netcode (IBodyMounts, M8 8.4.8)

        public byte ShownSelected => shown.selected;
        public byte ShownPhase => (byte)shown.phase;
        public byte ShownGrowth => shown.growth;
        public bool ShownFed => shown.fed;

        /// <summary>Any view of the entity netcode: the server's mount for this player (NGO's netState change).</summary>
        public void MirrorShown(byte selectedBox, byte ridePhase, byte growth, bool fed)
        {
            if (!EntityView) return;
            var next = new MountNetState { selected = selectedBox, phase = (RidePhase)ridePhase, growth = growth, fed = fed };
            if (next.Equals(shown)) return;
            Show(next);
        }

        /// <summary>The own view of the entity netcode: the server's four boxes (NGO's netBook change).</summary>
        public void MirrorBook(byte owned, uint xp1, uint xp2, uint xp3, uint xp4, uint hunger1, uint hunger2, uint hunger3, uint hunger4)
        {
            if (!EntityView) return;
            var next = new MountBook { owned = owned, xp1 = xp1, xp2 = xp2, xp3 = xp3, xp4 = xp4, hunger1 = hunger1, hunger2 = hunger2, hunger3 = hunger3, hunger4 = hunger4 };
            if (next.Equals(viewBook)) return;
            viewBook = next;
            Changed?.Invoke();
        }

        /// <summary>A server body of the entity netcode runs its owner's request (NGO's SelectRpc / RideRpc bodies).</summary>
        public void RunMountOp(MountOpKind op, int box)
        {
            if (!EntityServer) return;
            if (op == MountOpKind.Select) ServerSelect(box);
            else if (op == MountOpKind.Ride) ServerToggleRide();
        }

        /// <summary>Any view of the entity netcode: the selected mount evolved (NGO's EvolvedRpc).</summary>
        public void PlayEvolved(int stage)
        {
            if (EntityView) PresentEvolved(stage);
        }
    }
}
