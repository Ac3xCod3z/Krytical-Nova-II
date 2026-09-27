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
            if (!CanAllocate(attribute)) return false;
            statPoints -= statData.allocationCost;
            attributes[(int)attribute]++;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Atomically spends skill points if the requested positive cost is affordable.</summary>
        public bool TrySpendSkillPoints(int cost)
        {
            if (!isActiveAndEnabled || cost <= 0 || skillPoints < cost) return false;
            skillPoints -= cost;
            Changed?.Invoke();
            return true;
        }

        private static int SaturatingAdd(int current, int amount)
        {
            return (int)Math.Min((long)current + Math.Max(0, amount), int.MaxValue);
        }
    }
}
