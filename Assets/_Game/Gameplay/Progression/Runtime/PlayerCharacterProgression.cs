using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Per-player session state and validated XP/point transactions. Never modifies shared data assets.</summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    public sealed class PlayerCharacterProgression : MonoBehaviour
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
        private readonly int[] attributes = new int[AttributeCount];
        public event Action Changed;
        public event Action<int> LevelGained;
        public int CurrentLevel => currentLevel;
        public int Experience => experience;
        public int StatPoints => statPoints;
        public int SkillPoints => skillPoints;
        public int MaxLevel => progressionData != null ? progressionData.MaxLevel : FirstLevel;
        public bool IsAtLevelCap => currentLevel >= MaxLevel;
        public int ExperienceRequired => progressionData != null && progressionData.TryGetStep(currentLevel, out var step) ? step.experienceRequired : 0;
        public int AllocationCost => statData != null ? statData.allocationCost : 1;

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

        /// <summary>Adds positive XP, carrying overflow across levels and granting each transition's rewards once.</summary>
        public void GrantExperience(int amount)
        {
            if (!isActiveAndEnabled || progressionData == null || amount <= 0 || IsAtLevelCap) return;
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
            Changed?.Invoke();
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

        /// <summary>Spends the configured stat-point cost for multiple points and increases the attribute atomically.</summary>
        public int TryAllocateMultiple(CharacterAttribute attribute, int maxCount)
        {
            if (maxCount <= 0 || !isActiveAndEnabled || statData == null) return 0;
            int index = (int)attribute;
            if (index < 0 || index >= attributes.Length) return 0;

            int cost = statData.allocationCost;
            int allocated = 0;
            while (allocated < maxCount && statPoints >= cost && attributes[index] < statData.maximumAttributeValue)
            {
                statPoints -= cost;
                attributes[index]++;
                allocated++;
            }
            if (allocated > 0)
            {
                Changed?.Invoke();
            }
            return allocated;
        }

        /// <summary>Atomically spends skill points if the requested positive cost is affordable.</summary>
        public bool TrySpendSkillPoints(int cost)
        {
            if (!isActiveAndEnabled || cost <= 0 || skillPoints < cost) return false;
            skillPoints -= cost;
            Changed?.Invoke();
            return true;
        }

        #region Development / GM tools
        /// <summary>GM: jumps to a level. Raising grants each skipped level's rewards; lowering rebuilds the character at that level (attributes reset, points re-granted).</summary>
        public void SetLevel(int level)
        {
            if (!isActiveAndEnabled || progressionData == null) return;
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
            Changed?.Invoke();
        }

        /// <summary>GM: sets XP within the current level (clamped below the level-up threshold).</summary>
        public void SetExperience(int amount)
        {
            if (!isActiveAndEnabled) return;
            int required = ExperienceRequired;
            experience = Mathf.Clamp(amount, 0, required > 0 ? required - 1 : 0);
            Changed?.Invoke();
        }

        /// <summary>GM: adds (or with a negative value removes) unspent stat points.</summary>
        public void AddStatPoints(int amount)
        {
            if (!isActiveAndEnabled) return;
            statPoints = (int)Math.Max(0L, Math.Min((long)statPoints + amount, int.MaxValue));
            Changed?.Invoke();
        }

        /// <summary>GM: adds (or with a negative value removes) unspent skill points.</summary>
        public void AddSkillPoints(int amount)
        {
            if (!isActiveAndEnabled) return;
            skillPoints = (int)Math.Max(0L, Math.Min((long)skillPoints + amount, int.MaxValue));
            Changed?.Invoke();
        }

        /// <summary>GM: sets an attribute directly without spending points.</summary>
        public void SetAttribute(CharacterAttribute attribute, int value)
        {
            int index = (int)attribute;
            if (!isActiveAndEnabled || statData == null || index < 0 || index >= attributes.Length) return;
            attributes[index] = Mathf.Clamp(value, 0, statData.maximumAttributeValue);
            Changed?.Invoke();
        }

        /// <summary>GM: refunds every allocated attribute point back to the stat point pool.</summary>
        public void ResetAllocations()
        {
            if (!isActiveAndEnabled || statData == null) return;
            int cost = Mathf.Max(1, statData.allocationCost);
            for (int index = 0; index < attributes.Length; index++)
            {
                int starting = statData.GetStartingValue((CharacterAttribute)index);
                int spent = Mathf.Max(0, attributes[index] - starting);
                statPoints = SaturatingAdd(statPoints, spent * cost);
                attributes[index] = starting;
            }
            Changed?.Invoke();
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

        private static int SaturatingAdd(int current, int amount)
        {
            return (int)Math.Min((long)current + Math.Max(0, amount), int.MaxValue);
        }
    }
}
