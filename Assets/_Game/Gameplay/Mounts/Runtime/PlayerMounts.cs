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
    public sealed class PlayerMounts : NetworkBehaviour, IItemUseHandler, ISaveParticipant
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
        private bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>The selected mount, or null.</summary>
        public MountDefinition Selected => catalog != null ? catalog.Get(shown.selected) : null;
        public int SelectedId => shown.selected;
        public RidePhase Phase => shown.phase;
        /// <summary>On the mount, getting on or getting off.</summary>
        public bool IsMounted => shown.phase != RidePhase.OnFoot;
        public int GrowthPercent => shown.selected != 0 ? shown.growth : 0;
        public bool IsFed => shown.selected != 0 && shown.fed;
        public int Stage => MountRules.Stage(GrowthPercent);

        /// <summary>All four boxes. Known to the owner and where the game is decided.</summary>
        public MountBook Book => HasAuthority ? book : netBook.Value;
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
            PublishBook();
            Publish();
        }

        private void BeginPhase(RidePhase next)
        {
            MountDefinition mount = catalog != null ? catalog.Get(selected) : null;
            AnimationClip clip = mount == null ? null : next == RidePhase.GettingOn ? mount.mountUp : next == RidePhase.GettingOff ? mount.dismount : null;
            phase = next;
            phaseEnds = Time.time + Mathf.Max(MinPhaseSeconds, clip != null ? clip.length : 0f);
            if (next == RidePhase.GettingOff) PublishBook(); // what riding cost in hunger, before the ride ends
            Publish();
        }

        private void OnDied()
        {
            if (HasAuthority) ServerDismountNow();
        }

        private void Update()
        {
            if (!HasAuthority || catalog == null) return;

            if ((phase == RidePhase.GettingOn || phase == RidePhase.GettingOff) && Time.time >= phaseEnds)
            {
                phase = phase == RidePhase.GettingOn ? RidePhase.Riding : RidePhase.OnFoot;
                Publish();
            }
            // A jump, a vault or a wall run takes the rider off: the mount has no clips for them.
            if (phase == RidePhase.Riding && motor != null && motor.CurrentState == CharacterAnimState.Jumping) ServerDismountNow();

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
            else PresentEvolved(stage);
        }

        [Rpc(SendTo.Everyone)]
        private void EvolvedRpc(byte stage) => PresentEvolved(stage);

        private void PresentEvolved(int stage)
        {
            MountDefinition mount = Selected;
            if (mount == null) return;
            bool mine = !IsSpawned || IsOwner;
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
            Changed?.Invoke();
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
            };
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
        }

        /// <summary>Every machine: the stats and the riding speed that follow from the selected mount.</summary>
        private void Show(MountNetState next)
        {
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
                else if (next.phase == RidePhase.GettingOn || next.phase == RidePhase.GettingOff)
                    movement.SetRideSpeeds(StandStillSpeed, StandStillSpeed);
                else movement.SetRideSpeeds(0f, 0f);
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
            if (IsSpawned) ToastRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void ToastRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
