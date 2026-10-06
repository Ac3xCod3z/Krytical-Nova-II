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
    public sealed class PlayerCharacterProgression : NetworkBehaviour, ISaveParticipant
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
                    LevelGained?.Invoke(level);
            }
            Changed?.Invoke();
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
                LevelGained?.Invoke(currentLevel);
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

        [ServerRpc]
        private void AllocateServerRpc(CharacterAttribute attribute, int count)
        {
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
                LevelGained?.Invoke(currentLevel);
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
            int cost = Mathf.Max(1, statData.allocationCost);
            for (int index = 0; index < attributes.Length; index++)
            {
                int starting = statData.GetStartingValue((CharacterAttribute)index);
                int spent = Mathf.Max(0, attributes[index] - starting);
                statPoints = SaturatingAdd(statPoints, spent * cost);
                attributes[index] = starting;
            }
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
