using System;
using System.Collections.Generic;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>What every machine knows of a player's pet: enough to derive its stat bonus and its look.</summary>
    public struct PetNetState : INetworkSerializable, IEquatable<PetNetState>
    {
        /// <summary>Whole percent of growth, 0–100.</summary>
        public byte growth;
        /// <summary>False at 0 % hunger: no bonus.</summary>
        public bool fed;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref growth);
            serializer.SerializeValue(ref fed);
        }

        public bool Equals(PetNetState other) => growth == other.growth && fed == other.fed;
        public override bool Equals(object obj) => obj is PetNetState other && Equals(other);
        public override int GetHashCode() => (growth << 1) | (fed ? 1 : 0);
    }

    /// <summary>
    /// The pet a player has out (the item in the Pet slot). The server feeds it XP for the owner's Hostile kills,
    /// lets it grow hungry while the owner plays, and keeps both in the pet item itself, so they go with the item.
    /// Growth and fed / starving are replicated to everyone; each machine works out the stat bonus and the stage's
    /// look from them, the way worn gear does. Only the pet that is out grows or gets hungry.
    /// Offline (the unspawned scene avatar) the local copy decides. While the owner is in a fight
    /// (<see cref="PlayerCombatState"/>) the pet is kept to its idle, walk and run on every machine.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerEquipment))]
    public sealed class PlayerPet : NetworkBehaviour, IItemUseHandler
    {
        private const string ReasonNoPet = "You have no pet out to feed.";
        private const string ReasonNotHungry = "Your pet is not hungry.";

        [SerializeField] private PetCatalog catalog;

        private readonly NetworkVariable<PetNetState> netState = new NetworkVariable<PetNetState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerInventory inventory;
        private PlayerEquipment equipment;
        private CharacterCombatStats combatStats;
        private PetFollower follower;
        private PlayerChat chat;
        private PlayerCombatState combat;

        // One stable source object so the pet's bonus can be replaced or removed on its own.
        private readonly object statSource = new object();
        private readonly List<StatModifier> modifiers = new List<StatModifier>(1);

        private PetDefinition pet;      // every machine: from the replicated loadout
        private PetNetState shown;      // every machine: what the bonus and the look are derived from
        // Where the game is decided: the working copy of the worn pet's own state.
        private PetItemState state;
        private double hungerCarry;
        private float nextWrite;
        private bool warnedHungry;
        private bool decides;           // this machine held the pet's state when it was adopted

        /// <summary>Raised on every machine when the pet, its growth or its hunger state changes.</summary>
        public event Action Changed;
        /// <summary>Raised on every machine at the moment a pet evolves: (pet, the stage it reached).</summary>
        public event Action<PetDefinition, int> Evolved;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerPet Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerPet>() : null;

        public PetCatalog Catalog => catalog;
        /// <summary>The pet that is out, or null.</summary>
        public PetDefinition Pet => pet;
        public int GrowthPercent => pet != null ? shown.growth : 0;
        public bool IsFed => pet != null && shown.fed;
        public int Stage => PetRules.Stage(GrowthPercent);
        /// <summary>What the pet adds to its stat right now.</summary>
        public float StatBonus => pet != null ? PetRules.StatBonus(pet.maxValue, shown.growth, shown.fed) : 0f;
        /// <summary>The pet's exact XP and hunger. Known to the owner and where the game is decided; empty elsewhere.</summary>
        public PetItemState State => pet == null ? default : HasAuthority ? state : PetItemState.Of(inventory.GetEquipped(EquipSlot.Pet));

        private bool HasAuthority => !IsSpawned || IsServer;
        private PetTuning Tuning => catalog != null ? catalog.Tuning : PetTuning.Default;
        private PetSettings Settings => catalog != null ? catalog.Settings : null;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            equipment = GetComponent<PlayerEquipment>();
            combatStats = GetComponent<CharacterCombatStats>();
            follower = GetComponent<PetFollower>();
            chat = GetComponent<PlayerChat>();
            combat = GetComponent<PlayerCombatState>();
        }

        private void OnEnable()
        {
            equipment.SlotChanged += OnSlotChanged;
            CombatEntity.AnyKilled += OnAnyKilled;
            if (combat != null)
            {
                combat.Changed += OnCombatChanged;
                OnCombatChanged(combat.InCombat);
            }
            // The offline avatar comes back after a session with whatever is worn now.
            if (!IsSpawned) Adopt(equipment.Get(EquipSlot.Pet));
        }

        private void OnDisable()
        {
            equipment.SlotChanged -= OnSlotChanged;
            CombatEntity.AnyKilled -= OnAnyKilled;
            if (combat != null) combat.Changed -= OnCombatChanged;
            if (!IsSpawned) WriteToItem();
        }

        // In a fight the pet keeps to its idle and its gait: fewer animations and effects to pay for when most is going on.
        private void OnCombatChanged(bool fighting)
        {
            if (follower != null) follower.SetCombatCalm(fighting);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (IsServer) Adopt(equipment.Get(EquipSlot.Pet));
            else Show(netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) WriteToItem();
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(PetNetState previous, PetNetState current)
        {
            if (!IsServer) Show(current);
        }

        private void OnSlotChanged(EquipSlot slot, ushort previous, ushort next)
        {
            if (slot == EquipSlot.Pet) Adopt(next);
        }

        // ------------------------------------------------------------------ the pet that is out

        /// <summary>A pet came out, was swapped or was put away. Its own state came with it in the item.</summary>
        private void Adopt(ushort itemId)
        {
            pet = catalog != null ? catalog.Get(itemId) : null;
            decides = HasAuthority;
            if (!decides)
            {
                Show(netState.Value);
                return;
            }
            state = pet != null ? PetItemState.Of(inventory.GetEquipped(EquipSlot.Pet)) : default;
            hungerCarry = 0.0;
            warnedHungry = pet != null && Settings != null && state.HungerPercent <= Settings.hungerWarningPercent;
            nextWrite = Time.time + WriteSeconds;
            Publish();
        }

        private float WriteSeconds => Settings != null ? Settings.hungerWriteSeconds : 10f;

        /// <summary>Where the game is decided: tells every machine what the pet now gives and looks like.</summary>
        private void Publish()
        {
            var next = pet != null
                ? new PetNetState { growth = (byte)PetRules.GrowthPercent(state, Tuning), fed = state.IsFed }
                : default;
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
        }

        /// <summary>Every machine: the bonus and the look that follow from growth and hunger.</summary>
        private void Show(PetNetState next)
        {
            shown = next;

            modifiers.Clear();
            float bonus = StatBonus;
            if (pet != null && bonus > 0f) modifiers.Add(new StatModifier(pet.stat, ModifierType.Flat, bonus, statSource));
            if (combatStats != null) combatStats.SetSourceModifiers(statSource, modifiers);

            if (follower != null)
            {
                if (pet == null) follower.ClearStageLook();
                else
                {
                    PetStageLook look = pet.Stage(Stage);
                    var glow = new PetGlowLook { color = look.glow, strength = shown.fed ? look.glowStrength : 0f }; // a starving pet loses its glow with its bonus
                    var spacing = default(PetFollowLook);
                    if (Settings != null)
                    {
                        glow.sparkles = Settings.auraPrefab;
                        glow.rimIntensity = Settings.rimIntensity;
                        glow.rimWidth = Settings.rimWidth;
                        glow.rimPulse = Settings.rimPulse;
                        glow.rimPulseSpeed = Settings.rimPulseSpeed;
                        glow.rimPulseWidth = Settings.rimPulseWidth;
                        glow.sparkleRate = Settings.sparkleRate;
                        glow.noRim = Stage < Settings.rimFromStage;
                        // The halo at the top of its breath is the widest the pet is ever seen.
                        float reach = glow.strength > 0f && !glow.noRim
                            ? Mathf.Lerp(glow.rimWidth.x, glow.rimWidth.y, glow.strength) * (1f + Mathf.Lerp(glow.rimPulseWidth.x, glow.rimPulseWidth.y, glow.strength))
                            : 0f;
                        spacing = new PetFollowLook
                        {
                            spaced = true,
                            ownerHalfWidth = Settings.ownerHalfWidth,
                            gap = Settings.followGap + pet.followGapAdjust,
                            glowReach = reach,
                            gapGrowth = Settings.followGapGrowth,
                            cameraClearance = Settings.cameraClearance,
                            maxTrail = Settings.followMaxTrail,
                        };
                    }
                    follower.SetStageLook(look.model, look.scale, glow, spacing);
                }
                follower.SetAnimationCycle(pet != null && Settings != null ? Settings.animationCycleSeconds : 0f);
                if (pet != null && Settings != null) follower.SetGaitSpeeds(Settings.walkFromOwnerSpeed, Settings.runFromOwnerSpeed, Settings.runToWalkOwnerSpeed);
                else follower.SetGaitSpeeds(0f, 0f, 0f);
                if (pet != null) follower.SetMoveLoop(pet.moveLoop, pet.moveLoopWalkSpeed, pet.moveLoopRunSpeed);
                else follower.SetMoveLoop(null, 1f, 1f);
                follower.SetRestLoop(pet != null ? pet.restLoop : null);
                follower.SetStance(pet != null && !pet.bobs, pet != null ? pet.footEffect : null, pet != null ? pet.footBones : null);
                PetFollower.WornEffect[] effects = EffectsOf(pet, Stage);
                follower.SetEffects(effects, effects);
                follower.SetCharge(pet != null ? pet.chargeAnimation : null, pet != null ? pet.chargeBone : null);
                if (Settings != null) follower.SetEmissionBreath(Settings.emissionBreath, Settings.emissionBreathsPerSecond);
            }
            Changed?.Invoke();
        }

        // The effects of each pet at each growth stage, made once: the follower rebuilds what a pet wears only when
        // it is handed a different list, and the same pet at the same stage is always handed the same one.
        private static readonly Dictionary<(PetDefinition, int), PetFollower.WornEffect[]> effectLists = new Dictionary<(PetDefinition, int), PetFollower.WornEffect[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ForgetEffectLists() => effectLists.Clear(); // definitions may have been edited between two Plays

        /// <summary>A pet's own effects at a growth stage as the follower takes them, or null.</summary>
        private static PetFollower.WornEffect[] EffectsOf(PetDefinition pet, int stage)
        {
            if (pet == null || pet.effects == null || pet.effects.Length == 0) return null;
            if (effectLists.TryGetValue((pet, stage), out PetFollower.WornEffect[] known)) return known;
            var effects = new List<PetFollower.WornEffect>(pet.effects.Length);
            foreach (PetEffect effect in pet.effects)
            {
                if (effect == null || stage < effect.stages.x || stage > effect.stages.y) continue;
                effects.Add(new PetFollower.WornEffect(effect.prefab, effect.bones, effect.stageColour, (PetFollower.EffectPlace)(int)effect.anchor,
                    effect.offset, effect.animation, effect.at, effect.during, effect.size));
            }
            PetFollower.WornEffect[] list = effects.Count > 0 ? effects.ToArray() : null;
            effectLists[(pet, stage)] = list;
            return list;
        }

        /// <summary>Puts the working state back into the worn pet item, so it travels with it and is saved with it.</summary>
        private void WriteToItem()
        {
            if (pet == null || inventory == null || !decides) return;
            nextWrite = Time.time + WriteSeconds;
            inventory.ServerSetEquippedData(EquipSlot.Pet, state.Pack());
        }

        // ------------------------------------------------------------------ hunger (server clock, while online)

        private void Update()
        {
            if (pet == null || !HasAuthority) return;

            PetItemState before = state;
            state = PetRules.Drain(state, Time.deltaTime, Tuning, ref hungerCarry);
            if (state.Equals(before)) return;

            if (before.IsFed && !state.IsFed)
            {
                WriteToItem();
                Publish();
                Notify($"Your {pet.displayName} is starving. Its bonus is off and it earns no XP until it eats a Pet Ration.", toast: true);
                return;
            }
            PetSettings settings = Settings;
            if (!warnedHungry && settings != null && state.HungerPercent <= settings.hungerWarningPercent)
            {
                warnedHungry = true;
                Notify($"Your {pet.displayName} is getting hungry ({state.HungerPercent:0}%). Feed it a Pet Ration.", toast: true);
            }
            if (Time.time >= nextWrite) WriteToItem();
        }

        // ------------------------------------------------------------------ XP

        private void OnAnyKilled(CombatEntity victim, KillingBlow blow)
        {
            if (pet == null || !HasAuthority || victim == null || blow.Attacker == null) return;
            if (blow.Attacker.gameObject != gameObject) return;
            // Hostiles only: another player is never worth pet XP here (wars pay through ServerAwardEventXp).
            if (victim.GetComponent<PlayerIdentity>() != null) return;
            // A Hostile's kill belongs to whoever landed the first hit, not the last: ServerCreditHostileKill.
            if (victim.GetComponent<KryticalNova.Hostiles.HostileNet>() != null) return;
            AddXpUnits(Tuning.KillXp);
        }

        /// <summary>Server only: this player holds the kill of a Hostile; the pet that is out gets its kill XP.</summary>
        public void ServerCreditHostileKill()
        {
            if (pet == null || !HasAuthority) return;
            // An XP booster for the pet (Companion, Tandem) raises what a kill is worth to it.
            float bonus = TryGetComponent(out KryticalNova.Progression.PlayerExperienceBoosts boosts) ? boosts.ServerPetBonus : 0f;
            AddXpUnits(KryticalNova.Progression.ExperienceBoostRules.Boosted(Tuning.KillXp, bonus));
        }

        /// <summary>
        /// Server only: the pet's share of a PvP event (a war). Players with no pet out get nothing. Nothing calls
        /// this yet: it is here for the events when they are built.
        /// </summary>
        public void ServerAwardEventXp(bool won)
        {
            if (pet == null || !HasAuthority) return;
            AddXpUnits(won ? Tuning.EventWinXp : Tuning.EventLossXp);
        }

        /// <summary>Server only (GM): adds pet XP as the design counts it.</summary>
        public void ServerAddXp(float xp)
        {
            if (pet == null || !HasAuthority || xp <= 0f) return;
            AddXpUnits((uint)Mathf.Min(uint.MaxValue, Mathf.Round(xp * PetItemState.XpUnitsPerXp)));
        }

        private void AddXpUnits(uint units)
        {
            PetItemState before = state;
            state = PetRules.AddXp(state, units, Tuning);
            if (state.Equals(before)) return;
            AfterGrowthChanged(before);
        }

        /// <summary>Server only (GM): puts the pet at a growth percent, evolving (or devolving) it on the spot.</summary>
        public void ServerSetGrowth(float percent)
        {
            if (pet == null || !HasAuthority) return;
            PetItemState before = state;
            state = PetRules.WithGrowth(state, percent, Tuning);
            AfterGrowthChanged(before);
        }

        private void AfterGrowthChanged(PetItemState before)
        {
            int stageBefore = PetRules.Stage(PetRules.GrowthPercent(before, Tuning));
            int growth = PetRules.GrowthPercent(state, Tuning);
            int stage = PetRules.Stage(growth);
            WriteToItem();
            if (growth != shown.growth || state.IsFed != shown.fed) Publish();
            if (stage <= stageBefore) return;

            string bonus = StatDisplay.FormatModifier(pet.stat, ModifierType.Flat, PetRules.StatBonus(pet.maxValue, growth, true));
            Notify($"Your {pet.displayName} evolved: {pet.StageName(stage)} ({growth}% growth, {bonus}).", toast: false);
            if (IsSpawned) EvolvedRpc((byte)stage);
            else PresentEvolved(stage);
        }

        [Rpc(SendTo.Everyone)]
        private void EvolvedRpc(byte stage) => PresentEvolved(stage);

        private void PresentEvolved(int stage)
        {
            if (pet == null) return;
            KryticalNova.Diagnostics.PerformanceLog.Mark("pet.evolved", null, stage);
            bool mine = !IsSpawned || IsOwner;
            Transform body = follower != null && follower.PetTransform != null ? follower.PetTransform : transform;
            ClientPresentationRegistry.Feel?.PetEvolved(body, pet.Stage(stage).glow, mine);
            Evolved?.Invoke(pet, stage);
        }

        // ------------------------------------------------------------------ animations

        /// <summary>The animations the pet that is out can play, by name. Empty for a pet whose model has none.</summary>
        public IReadOnlyList<string> AnimationNames => pet != null && follower != null ? follower.AnimationNames : Array.Empty<string>();

        /// <summary>
        /// Server only (GM): the pet plays one of its animations now, on every machine. False when it has no
        /// animation of that name.
        /// </summary>
        public bool ServerPlayAnimation(string animationName)
        {
            if (pet == null || !HasAuthority || follower == null) return false;
            IReadOnlyList<string> names = follower.AnimationNames;
            for (int i = 0; i < names.Count; i++)
            {
                if (!string.Equals(names[i], animationName, StringComparison.OrdinalIgnoreCase)) continue;
                int hash = Animator.StringToHash(names[i]);
                if (IsSpawned) PlayAnimationRpc(hash);
                else follower.PlayAnimation(hash);
                return true;
            }
            return false;
        }

        [Rpc(SendTo.Everyone)]
        private void PlayAnimationRpc(int nameHash)
        {
            if (follower != null) follower.PlayAnimation(nameHash);
        }

        // ------------------------------------------------------------------ feeding (IItemUseHandler)

        public ItemUseEffect Effect => ItemUseEffect.FeedPet;

        public string UseBlockedReason(ItemDefinition item)
        {
            if (pet == null) return ReasonNoPet;
            return state.HungerDeficit == 0 ? ReasonNotHungry : null;
        }

        public void ServerUse(ItemDefinition item) => ServerFeed();

        /// <summary>Server only: hunger back to 100 %.</summary>
        public void ServerFeed()
        {
            if (pet == null || !HasAuthority) return;
            state = PetRules.Feed(state);
            hungerCarry = 0.0;
            warnedHungry = false;
            WriteToItem();
            Publish();
            Notify($"Your {pet.displayName} has eaten its fill.", toast: false);
        }

        /// <summary>Server only (GM): sets hunger, 0–100.</summary>
        public void ServerSetHunger(float percent)
        {
            if (pet == null || !HasAuthority) return;
            state = PetRules.WithHunger(state, percent);
            hungerCarry = 0.0;
            warnedHungry = Settings != null && state.HungerPercent <= Settings.hungerWarningPercent;
            WriteToItem();
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
