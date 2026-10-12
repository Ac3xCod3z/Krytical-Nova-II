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
    /// What is seen of the pet may be a Morph (<see cref="MorphRules"/>): the look of another pet, or of an earlier
    /// stage, from <see cref="EquipmentLooks"/>. The bonus, growth and hunger are always the real pet's.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory), typeof(PlayerEquipment))]
    public sealed class PlayerPet : NetworkBehaviour, IItemUseHandler, IPetPresenter
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
        // What the worn pet item held when its state was last read from it or written to it: anything else in the
        // slot is another pet, also when it is one of the same kind (the slot's item id does not change then).
        private ulong itemData;
        private PetDefinition shownPet; // every machine: the pet whose look is shown (a Morph, or the pet itself)
        private int shownStage;
        // The owner's machine only: a Morph being tried on at the Holoweaver, before it is paid for.
        private PetDefinition previewPet;
        private int previewStage;

        /// <summary>Raised on every machine when the pet, its growth or its hunger state changes.</summary>
        public event Action Changed;
        /// <summary>Raised on every machine at the moment a pet evolves: (pet, the stage it reached).</summary>
        public event Action<PetDefinition, int> Evolved;
        /// <summary>
        /// Raised where the game is decided when the pet that is out is at a stage as this player's own: it was put
        /// out at that stage, or it has just evolved into it. The Weave Library collects the Morph.
        /// </summary>
        public event Action<PetDefinition, int> StageReached;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerPet Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerPet>() : null;

        public PetCatalog Catalog => catalog;
        /// <summary>The pet that is out, or null.</summary>
        public PetDefinition Pet => pet;
        public int GrowthPercent => pet != null ? shown.growth : 0;
        public bool IsFed => pet != null && shown.fed;
        public int Stage => PetRules.Stage(GrowthPercent);
        /// <summary>The pet whose look is shown: the Morph over the pet that is out, or that pet itself. Null with no pet out.</summary>
        public PetDefinition ShownPet => pet != null ? shownPet : null;
        /// <summary>The growth stage whose look is shown.</summary>
        public int ShownStage => pet != null ? shownStage : 0;
        /// <summary>True while a Morph is shown in place of the pet's own look.</summary>
        public bool IsMorphed => pet != null && shownPet != null && (shownPet != pet || shownStage != Stage);
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
            inventory.Changed += OnInventoryChanged;
            equipment.LookChanged += OnLookChanged;
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
            inventory.Changed -= OnInventoryChanged;
            equipment.LookChanged -= OnLookChanged;
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

        // One pet swapped for another of its kind: the slot shows the same item, but it is not the same pet. Without
        // this the pet that came out would be given the growth and hunger of the one that went back into the Pack.
        private void OnInventoryChanged()
        {
            if (pet == null || !decides || !HasAuthority) return;
            ItemStack worn = inventory.GetEquipped(EquipSlot.Pet);
            if (worn.itemId == pet.itemId && worn.data != itemData) Adopt(worn.itemId);
        }

        // A Morph was put on or taken off: the same pet, another look.
        private void OnLookChanged(EquipSlot slot)
        {
            if (slot == EquipSlot.Pet) Show(shown);
        }

        /// <summary>
        /// The owner's machine: shows a Morph on the pet that is out without paying for it, to be judged at the
        /// Holoweaver. Nobody else sees it. <see cref="ClearPreview"/> puts back what the pet really shows.
        /// </summary>
        public void PreviewLook(PetDefinition morph, int stage)
        {
            previewPet = morph;
            previewStage = Mathf.Clamp(stage, 0, PetRules.LastStage);
            Show(shown);
        }

        public void ClearPreview()
        {
            if (previewPet == null) return;
            previewPet = null;
            Show(shown);
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
            ItemStack worn = pet != null ? inventory.GetEquipped(EquipSlot.Pet) : ItemStack.Empty;
            state = PetItemState.Of(worn);
            itemData = worn.data;
            hungerCarry = 0.0;
            warnedHungry = pet != null && Settings != null && state.HungerPercent <= Settings.hungerWarningPercent;
            nextWrite = Time.time + WriteSeconds;
            Publish();
            // Putting a pet out at a stage makes that stage's look this player's own (and no earlier one).
            if (pet != null) StageReached?.Invoke(pet, PetRules.Stage(PetRules.GrowthPercent(state, Tuning)));
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

            // What is seen: the pet itself, or the Morph over it. From here on only the look is the Morph's.
            PetDefinition seen = pet;
            int seenStage = Stage;
            if (pet != null)
            {
                if (previewPet != null) { seen = previewPet; seenStage = previewStage; }
                else
                {
                    EquipmentLooks looks = equipment.Looks;
                    if (looks.pet != 0 && catalog != null) MorphRules.Shown(pet, Stage, catalog.Get(looks.pet), looks.petStage, out seen, out seenStage);
                }
            }
            shownPet = seen;
            shownStage = seenStage;

            if (follower != null)
            {
                if (pet == null) follower.SetPetLook(null, null, 1f, default, default);
                else
                {
                    PetStageLook look = seen.Stage(seenStage);
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
                        glow.noRim = seenStage < Settings.rimFromStage;
                        // The halo at the top of its breath is the widest the pet is ever seen.
                        float reach = glow.strength > 0f && !glow.noRim
                            ? Mathf.Lerp(glow.rimWidth.x, glow.rimWidth.y, glow.strength) * (1f + Mathf.Lerp(glow.rimPulseWidth.x, glow.rimPulseWidth.y, glow.strength))
                            : 0f;
                        spacing = new PetFollowLook
                        {
                            spaced = true,
                            ownerHalfWidth = Settings.ownerHalfWidth,
                            gap = Settings.followGap + seen.followGapAdjust,
                            glowReach = reach,
                            gapGrowth = Settings.followGapGrowth,
                            cameraClearance = Settings.cameraClearance,
                            maxTrail = Settings.followMaxTrail,
                        };
                    }
                    follower.SetRestPose(seen.restPose);
                    follower.SetFollowOns(FollowOnsOf(seen));
                    ItemDefinition item = inventory.Catalog != null ? inventory.Catalog.Get(seen.itemId) : null;
                    follower.SetPetLook(item, look.model, look.scale, glow, spacing);
                }
                follower.SetLines(LinesOf(seen, seenStage));
                follower.SetAnimationCycle(pet != null && Settings != null ? Settings.animationCycleSeconds : 0f);
                if (pet != null && Settings != null) follower.SetGaitSpeeds(Settings.walkFromOwnerSpeed, Settings.runFromOwnerSpeed, Settings.runToWalkOwnerSpeed);
                else follower.SetGaitSpeeds(0f, 0f, 0f);
                if (seen != null) follower.SetMoveLoop(seen.moveLoop, seen.moveLoopWalkSpeed, seen.moveLoopRunSpeed);
                else follower.SetMoveLoop(null, 1f, 1f);
                follower.SetRestLoop(seen != null ? seen.restLoop : null);
                // A stage may stand on something of its own (the largest foxes on fire); otherwise on the pet's.
                GameObject underFeet = seen != null ? seen.Stage(seenStage).footEffect : null;
                if (underFeet == null && seen != null) underFeet = seen.footEffect;
                follower.SetStance(seen != null && !seen.bobs, underFeet, seen != null ? seen.footBones : null);
                follower.SetVanish(seen != null ? seen.vanishAnimation : null, seen != null ? seen.vanishDuring : default);
                PetFollower.WornEffect[] effects = EffectsOf(seen, seenStage);
                follower.SetEffects(effects, effects);
                follower.SetCharge(seen != null ? seen.chargeAnimation : null, seen != null ? seen.chargeBone : null);
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
                    effect.offset, effect.animation, effect.at, effect.during, effect.size, effect.alongBone, effect.endsWithAnimation, effect.facesAhead));
            }
            PetFollower.WornEffect[] list = effects.Count > 0 ? effects.ToArray() : null;
            effectLists[(pet, stage)] = list;
            return list;
        }

        /// <summary>What a pet says in its animations at a growth stage, as the follower takes it, or null.</summary>
        private static PetFollower.SpokenLine[] LinesOf(PetDefinition pet, int stage)
        {
            if (pet == null || pet.lines == null || pet.lines.Length == 0) return null;
            var spoken = new List<PetFollower.SpokenLine>(pet.lines.Length);
            foreach (PetLine line in pet.lines)
                if (line != null && stage >= line.stages.x && stage <= line.stages.y && !string.IsNullOrEmpty(line.text))
                    spoken.Add(new PetFollower.SpokenLine(line.animation, line.at, line.text));
            return spoken.Count > 0 ? spoken.ToArray() : null;
        }

        /// <summary>The animations of a pet that are followed by another, as the follower takes them, or null.</summary>
        private static PetAnimations.FollowOn[] FollowOnsOf(PetDefinition pet)
        {
            if (pet == null || pet.followOns == null || pet.followOns.Length == 0) return null;
            var list = new PetAnimations.FollowOn[pet.followOns.Length];
            for (int i = 0; i < list.Length; i++)
                list[i] = new PetAnimations.FollowOn(pet.followOns[i].animation, pet.followOns[i].then, pet.followOns[i].times);
            return list;
        }

        /// <summary>Puts the working state back into the worn pet item, so it travels with it and is saved with it.</summary>
        private void WriteToItem()
        {
            if (pet == null || inventory == null || !decides) return;
            nextWrite = Time.time + WriteSeconds;
            itemData = state.Pack(); // before the write: the inventory tells of it at once
            inventory.ServerSetEquippedData(EquipSlot.Pet, itemData);
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
            // Every stage it grew through in this player's hands is theirs (a GM may set growth past several at once).
            for (int reached = stageBefore + 1; reached <= stage; reached++) StageReached?.Invoke(pet, reached);

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
