using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    [Serializable]
    public struct CharacterLevelStep
    {
        [Tooltip("XP needed to advance FROM this level to the next.")]
        [Min(1)] public int experienceRequired;
        [Tooltip("Stat points awarded when the next level is reached.")]
        [Min(0)] public int statPointReward;
        [Tooltip("Skill points awarded when the next level is reached.")]
        [Min(0)] public int skillPointReward;
    }

    /// <summary>Designer-owned level transitions. The level cap is the number of rows plus one (111 rows: cap 112).</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Character Progression")]
    public sealed class CharacterProgressionData : ScriptableObject
    {
        private const int DefaultStartingStatPoints = 5;
        private const int DefaultStartingSkillPoints = 1;
        [Min(0)] public int startingStatPoints = DefaultStartingStatPoints;
        [Tooltip("One starting point lets a new character learn Run immediately.")]
        [Min(0)] public int startingSkillPoints = DefaultStartingSkillPoints;
        [Tooltip("Row 1 is level 1 to 2. Max level is the number of rows plus one.")]
        public CharacterLevelStep[] levels = Array.Empty<CharacterLevelStep>();
        public int MaxLevel => (levels?.Length ?? 0) + 1;

        /// <summary>Returns the transition from a one-based character level, if below the cap.</summary>
        public bool TryGetStep(int currentLevel, out CharacterLevelStep step)
        {
            step = default;
            if (levels == null || currentLevel < 1 || currentLevel > levels.Length)
                return false;
            step = levels[currentLevel - 1];
            step.experienceRequired = Mathf.Max(1, step.experienceRequired);
            step.statPointReward = Mathf.Max(0, step.statPointReward);
            step.skillPointReward = Mathf.Max(0, step.skillPointReward);
            return true;
        }

        private void OnValidate()
        {
            startingStatPoints = Mathf.Max(0, startingStatPoints);
            startingSkillPoints = Mathf.Max(0, startingSkillPoints);
            if (levels == null) return;
            for (int index = 0; index < levels.Length; index++)
            {
                levels[index].experienceRequired = Mathf.Max(1, levels[index].experienceRequired);
                levels[index].statPointReward = Mathf.Max(0, levels[index].statPointReward);
                levels[index].skillPointReward = Mathf.Max(0, levels[index].skillPointReward);
            }
        }
    }
}
