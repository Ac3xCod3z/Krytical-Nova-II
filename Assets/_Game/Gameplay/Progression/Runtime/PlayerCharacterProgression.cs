using KryticalNova.Networking;
using System;
using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Per-player level, XP, points and attributes. Decided where the player is simulated (the server, or offline)
    /// and mirrored to every client; an owning client asks the server to spend its points. Never modifies shared data assets.
    /// </summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterProgression : NetworkBehaviour, ISaveParticipant, IItemUseHandler
    {
        private const int InitializationOrder = -200;
        private const int AttributeCount = 4;
        private const int FirstLevel = 1;
        [SerializeField] private CharacterProgressionData progressionData;
        [SerializeField] private CharacterStatData statData;
        private int currentLevel = FirstLevel;
        private int experience;
        private int statPoints;
        private int skillPoints;
        private int renown;
        private readonly int[] attributes = new int[AttributeCount];

        private readonly NetworkVariable<ProgressionState> netState = new NetworkVariable<ProgressionState>(
            writePerm: NetworkVariableWritePermission.Server);

        public event Action Changed;
        public event Action<int> LevelGained;
        public int CurrentLevel => currentLevel;
        public int Experience => experience;
        public int StatPoints => statPoints;
        public int SkillPoints => skillPoints;
        /// <summary>Renown held: spent on Tuning. Server-held like the rest of the progression.</summary>
        public int Renown => renown;
        public int MaxLevel => progressionData != null ? progressionData.MaxLevel : FirstLevel;
        public bool IsAtLevelCap => currentLevel >= MaxLevel;
        public int ExperienceRequired => progressionData != null && progressionData.TryGetStep(currentLevel, out var step) ? step.experienceRequired : 0;
        public int AllocationCost => statData != null ? statData.allocationCost : 1;

        /// <summary>True where this player's progression is decided (the server, or offline). Clients only mirror it.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            if (progressionData == null || statData == null)
            {
                Debug.LogError("PlayerCharacterProgression needs progression and stat data assets.", this);
                enabled = false;
                return;
            }
            statPoints = progressionData.startingStatPoints;
            skillPoints = progressionData.startingSkillPoints;
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = statData.GetStartingValue((CharacterAttribute)index);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (IsServer) netState.Value = Capture();
            else ApplyReplicated(netState.Value, announceLevels: false);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(ProgressionState previous, ProgressionState current)
        {
            if (!IsServer) ApplyReplicated(current, announceLevels: true);
        }

        /// <summary>A server body of the entity netcode puts its owner's points in (NGO's AllocateServerRpc body, after the caller's limit).</summary>
        public int RunAllocate(CharacterAttribute attribute, int count) =>
            BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody } ? TryAllocateMultiple(attribute, count) : 0;

        // Another player's body on a client of the entity netcode: only their level is told (their nameplate); the first is no level-up.
        private bool levelObserved;

        /// <summary>The entity netcode's view of another player: their level (NGO's network variable, read by everyone), level-ups after the first.</summary>
        public void MirrorObservedLevel(int level)
        {
            if (level < FirstLevel || (levelObserved && level == currentLevel)) return;
            ProgressionState state = Capture();
            state.level = level;
            bool first = !levelObserved;
            levelObserved = true;
            ApplyReplicated(state, announceLevels: !first);
        }

        /// <summary>Everything the owner's windows show, as NGO's network variable carries it (the entity netcode reads it on the server).</summary>
        public ProgressionState State => Capture();

        /// <summary>The entity netcode's owner view: the server's progression, with its level-ups (NGO's OnNetStateChanged on a client).</summary>
        public void MirrorState(ProgressionState state)
        {
            // Build 17 item 2.5: the first state after joining is the character as it is, no level-up (a level-37 character used to
            // play 36 level-ups at login); every later rise is one.
            if (state.level < FirstLevel) return;
            bool first = !ownStateSeen;
            ownStateSeen = true;
            ApplyReplicated(state, announceLevels: !first);
        }

        private bool ownStateSeen;

        private ProgressionState Capture()
        {
            var state = new ProgressionState
            {
                level = currentLevel,
                experience = experience,
                statPoints = statPoints,
                skillPoints = skillPoints,
                renown = renown,
            };
            for (int index = 0; index < attributes.Length; index++)
                state[(CharacterAttribute)index] = attributes[index];
            return state;
        }

        private void ApplyReplicated(ProgressionState state, bool announceLevels)
        {
            if (state.level < FirstLevel) return; // not written by the server yet

            int previousLevel = currentLevel;
            currentLevel = state.level;
            experience = state.experience;
            statPoints = state.statPoints;
            skillPoints = state.skillPoints;
            renown = state.renown;
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = state[(CharacterAttribute)index];

            if (announceLevels)
            {
                for (int level = previousLevel + 1; level <= currentLevel; level++)
                    Announce(level);
            }
            Changed?.Invoke();
        }

        /// <summary>A level was gained here, on whichever machine this is: listeners are told, and the game feel shows it.</summary>
        private void Announce(int level)
        {
            KryticalNova.Diagnostics.PerformanceLog.Mark("level.gained", null, level);
            LevelGained?.Invoke(level);
            ClientPresentationRegistry.Feel?.LevelUp(transform, KryticalNova.Core.BodyRoleTag.IsMine(this, !IsSpawned || IsOwner));
        }

        /// <summary>Publishes a change made here, where the progression is decided.</summary>
        private void Commit()
        {
            if (IsSpawned && IsServer) netState.Value = Capture();
            Changed?.Invoke();
        }

        /// <summary>Adds positive XP, carrying overflow across levels and granting each transition's rewards once. Authority only.</summary>
        public void GrantExperience(int amount)
        {
            if (!HasAuthority || !isActiveAndEnabled || progressionData == null || amount <= 0 || IsAtLevelCap) return;
            long remainingExperience = (long)experience + amount;
            while (progressionData.TryGetStep(currentLevel, out var step) && remainingExperience >= step.experienceRequired)
            {
                remainingExperience -= step.experienceRequired;
                currentLevel++;
                statPoints = SaturatingAdd(statPoints, step.statPointReward);
                skillPoints = SaturatingAdd(skillPoints, step.skillPointReward);
                Announce(currentLevel);
            }
            experience = IsAtLevelCap ? 0 : (int)Math.Min(remainingExperience, int.MaxValue);
            Commit();
        }

        /// <summary>Reads the player's current allocated attribute value.</summary>
        public int GetAttribute(CharacterAttribute attribute)
        {
            int index = (int)attribute;
            return index >= 0 && index < attributes.Length ? attributes[index] : 0;
        }

        /// <summary>Reports whether a point allocation is affordable and below the attribute cap.</summary>
        public bool CanAllocate(CharacterAttribute attribute)
        {
            int index = (int)attribute;
            return isActiveAndEnabled && statData != null && index >= 0 && index < attributes.Length
                && statPoints >= statData.allocationCost && attributes[index] < statData.maximumAttributeValue;
        }

        /// <summary>Spends the configured stat-point cost and increases one attribute; no refunds are implied.</summary>
        public bool TryAllocate(CharacterAttribute attribute)
        {
            return TryAllocateMultiple(attribute, 1) > 0;
        }

        /// <summary>
        /// Spends the configured stat-point cost for multiple points and increases the attribute atomically. On an
        /// owning client this asks the server and returns how many points it is expected to accept; the new values
        /// arrive through <see cref="Changed"/>.
        /// </summary>
        public int TryAllocateMultiple(CharacterAttribute attribute, int maxCount)
        {
            if (maxCount <= 0 || !isActiveAndEnabled || statData == null) return 0;
            int index = (int)attribute;
            if (index < 0 || index >= attributes.Length) return 0;

            int cost = statData.allocationCost;
            // the entity netcode's own view: the owner's prediction (NGO's expected) goes to the server's body (M8 8.3.1)
            if (BodyRoleTag.RequestsOf(this) is IBodyRequests requests)
            {
                int affordable = cost > 0 ? statPoints / cost : maxCount;
                int expected = Mathf.Min(maxCount, affordable, statData.maximumAttributeValue - attributes[index]);
                if (expected <= 0) return 0;
                requests.Allocate((byte)attribute, (short)Mathf.Min(expected, short.MaxValue));
                return expected;
            }
            if (!HasAuthority)
            {
                if (!IsOwner) return 0;
                int affordable = cost > 0 ? statPoints / cost : maxCount;
                int expected = Mathf.Min(maxCount, affordable, statData.maximumAttributeValue - attributes[index]);
                if (expected <= 0) return 0;
                AllocateServerRpc(attribute, expected);
                return expected;
            }

            int allocated = 0;
            while (allocated < maxCount && statPoints >= cost && attributes[index] < statData.maximumAttributeValue)
            {
                statPoints -= cost;
                attributes[index]++;
                allocated++;
            }
            if (allocated > 0)
            {
                Commit();
            }
            return allocated;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AllocateServerRpc(CharacterAttribute attribute, int count)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(AllocateServerRpc))) return;
            TryAllocateMultiple(attribute, count);
        }

        /// <summary>Atomically spends skill points if the requested positive cost is affordable. Authority only.</summary>
        public bool TrySpendSkillPoints(int cost)
        {
            if (!HasAuthority || !isActiveAndEnabled || cost <= 0 || skillPoints < cost) return false;
            skillPoints -= cost;
            Commit();
            return true;
        }

        /// <summary>Atomically spends Renown if the character holds that much. Authority only.</summary>
        public bool TrySpendRenown(int cost)
        {
            if (!HasAuthority || !isActiveAndEnabled || cost < 0 || renown < cost) return false;
            if (cost == 0) return true;
            renown -= cost;
            Commit();
            return true;
        }

        /// <summary>Adds Renown (or with a negative value takes it away, never below zero). Authority only.</summary>
        public void AddRenown(int amount)
        {
            if (!HasAuthority || !isActiveAndEnabled || amount == 0) return;
            renown = (int)Math.Max(0L, Math.Min((long)renown + amount, int.MaxValue));
            Commit();
        }

        #region Development / GM tools (authority only)
        /// <summary>GM: jumps to a level. Raising grants each skipped level's rewards; lowering rebuilds the character at that level (attributes reset, points re-granted).</summary>
        public void SetLevel(int level)
        {
            if (!HasAuthority || !isActiveAndEnabled || progressionData == null) return;
            level = Mathf.Clamp(level, FirstLevel, MaxLevel);
            if (level < currentLevel)
            {
                ResetCharacter();
            }
            while (currentLevel < level && progressionData.TryGetStep(currentLevel, out var step))
            {
                currentLevel++;
                statPoints = SaturatingAdd(statPoints, step.statPointReward);
                skillPoints = SaturatingAdd(skillPoints, step.skillPointReward);
                Announce(currentLevel);
            }
            experience = 0;
            Commit();
        }

        /// <summary>GM: sets XP within the current level (clamped below the level-up threshold).</summary>
        public void SetExperience(int amount)
        {
            if (!HasAuthority || !isActiveAndEnabled) return;
            int required = ExperienceRequired;
            experience = Mathf.Clamp(amount, 0, required > 0 ? required - 1 : 0);
            Commit();
        }

        /// <summary>GM: adds (or with a negative value removes) unspent stat points.</summary>
        public void AddStatPoints(int amount)
        {
            if (!HasAuthority || !isActiveAndEnabled) return;
            statPoints = (int)Math.Max(0L, Math.Min((long)statPoints + amount, int.MaxValue));
            Commit();
        }

        /// <summary>GM: adds (or with a negative value removes) unspent skill points.</summary>
        public void AddSkillPoints(int amount)
        {
            if (!HasAuthority || !isActiveAndEnabled) return;
            skillPoints = (int)Math.Max(0L, Math.Min((long)skillPoints + amount, int.MaxValue));
            Commit();
        }

        /// <summary>GM: sets an attribute directly without spending points.</summary>
        public void SetAttribute(CharacterAttribute attribute, int value)
        {
            int index = (int)attribute;
            if (!HasAuthority || !isActiveAndEnabled || statData == null || index < 0 || index >= attributes.Length) return;
            attributes[index] = Mathf.Clamp(value, 0, statData.maximumAttributeValue);
            Commit();
        }

        /// <summary>GM: refunds every allocated attribute point back to the stat point pool.</summary>
        public void ResetAllocations()
        {
            if (!HasAuthority || !isActiveAndEnabled || statData == null) return;
            ApplyStatRefund(StatPointsPlaced);
        }

        /// <summary>Stat points placed above the lineage's starting values (what <see cref="ResetAllocations"/> hands back).</summary>
        public int StatPointsPlaced => statData == null ? 0
            : ProgressionResetRules.StatPointsPlaced(attributes, index => statData.GetStartingValue((CharacterAttribute)index), statData.allocationCost);

        /// <summary>Stat points this character's level has earned (the start plus every level's reward).</summary>
        public int StatPointsEarnedByLevel => ProgressionResetRules.EarnedStatPoints(progressionData, currentLevel);

        /// <summary>Skill points this character's level has earned (the start plus every level's reward).</summary>
        public int SkillPointsEarnedByLevel => ProgressionResetRules.EarnedSkillPoints(progressionData, currentLevel);

        // ---------------- a Respec Chip is used (IItemUseHandler, server; wave 3b, owner 12 Nov 2026) ----------------

        public ItemUseEffect Effect => ItemUseEffect.Respec;

        /// <summary>The stat points a Respec Chip hands back now: what was placed, capped at what the level earned.</summary>
        public int RespecHandBack => ProgressionResetRules.Capped(StatPointsPlaced, statPoints, StatPointsEarnedByLevel);

        public string UseBlockedReason(UsedItem item)
        {
            if (statData == null || StatPointsPlaced <= 0) return ProgressionResetRules.ReasonNoStatsPlaced;
            return RespecHandBack <= 0 ? ProgressionResetRules.ReasonOnlyGiven : null;
        }

        public void ServerUse(UsedItem item)
        {
            if (!HasAuthority || !isActiveAndEnabled || statData == null) return;
            int placed = StatPointsPlaced, handBack = RespecHandBack;
            if (handBack <= 0)
            {
                Debug.LogError($"[Respec] {name}: the chip was used with nothing to hand back (check and use disagree).", this);
                return;
            }
            string trimmed = ProgressionResetRules.TrimLine("Respec", name, handBack, placed);
            if (trimmed != null) Debug.Log(trimmed, this);
            ApplyStatRefund(handBack);
        }

        // Attributes back to their starting values and handBack stat points added. Authority only.
        private void ApplyStatRefund(int handBack)
        {
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = statData.GetStartingValue((CharacterAttribute)index);
            statPoints = SaturatingAdd(statPoints, Mathf.Max(0, handBack));
            Commit();
        }

        private void ResetCharacter()
        {
            currentLevel = FirstLevel;
            experience = 0;
            statPoints = progressionData.startingStatPoints;
            skillPoints = progressionData.startingSkillPoints;
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = statData.GetStartingValue((CharacterAttribute)index);
        }
        #endregion

        #region Saving
        public string SaveKey => "progression";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(Capture());

        /// <summary>Puts a saved character back as it was: no level-up rewards are granted and nothing is announced.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json) || progressionData == null || statData == null) return;
            var state = JsonUtility.FromJson<ProgressionState>(json);
            if (state.level < FirstLevel) return;

            currentLevel = Mathf.Clamp(state.level, FirstLevel, MaxLevel);
            experience = IsAtLevelCap ? 0 : Mathf.Max(0, state.experience);
            statPoints = Mathf.Max(0, state.statPoints);
            skillPoints = Mathf.Max(0, state.skillPoints);
            renown = Mathf.Max(0, state.renown); // a character saved before Renown existed reads as 0
            for (int index = 0; index < attributes.Length; index++)
                attributes[index] = Mathf.Clamp(state[(CharacterAttribute)index], 0, statData.maximumAttributeValue);
            Commit();
        }
        #endregion

        private static int SaturatingAdd(int current, int amount)
        {
            return (int)Math.Min((long)current + Math.Max(0, amount), int.MaxValue);
        }
    }
}
