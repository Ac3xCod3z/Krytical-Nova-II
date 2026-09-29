using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Owns learned combat skills, rare drop book unlocks, cooldowns, and skill casting execution.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression), typeof(PlayerResources), typeof(CharacterBuffManager))]
    public sealed class PlayerCombatSkills : MonoBehaviour
    {
        [SerializeField] private SkillDefinitionSO[] availableSkills = Array.Empty<SkillDefinitionSO>();

        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private CharacterBuffManager buffManager;
        private CharacterCombatStats combatStats;

        private readonly Dictionary<string, int> learnedRanks = new Dictionary<string, int>();
        private readonly HashSet<string> unlockedDropSkills = new HashSet<string>();
        private readonly Dictionary<string, float> cooldownEndTimes = new Dictionary<string, float>();

        public event Action Changed;
        public event Action<SkillDefinitionSO> SkillCast;
        public event Action<string> SkillMessage;

        public IReadOnlyList<SkillDefinitionSO> AvailableSkills => availableSkills;

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (progression == null) progression = GetComponent<PlayerCharacterProgression>();
            if (resources == null) resources = GetComponent<PlayerResources>();
            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            if (combatStats == null) combatStats = GetComponent<CharacterCombatStats>();
        }

        public void SetAvailableSkills(SkillDefinitionSO[] skills)
        {
            availableSkills = skills;
            Changed?.Invoke();
        }

        public SkillDefinitionSO GetSkill(string skillId)
        {
            if (availableSkills == null) return null;
            for (int i = 0; i < availableSkills.Length; i++)
            {
                if (availableSkills[i] != null && availableSkills[i].skillId == skillId)
                    return availableSkills[i];
            }
            return null;
        }

        public int GetRank(SkillDefinitionSO skill)
        {
            if (skill == null) return 0;
            if (skill.requiresSkillBook && !unlockedDropSkills.Contains(skill.skillId))
                return 0;
            return learnedRanks.TryGetValue(skill.skillId, out int rank) ? rank : (skill.requiresSkillBook && unlockedDropSkills.Contains(skill.skillId) ? 1 : 0);
        }

        public bool IsLearned(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            return GetRank(skill) > 0;
        }

        public bool IsDropSkillUnlocked(string skillId) => unlockedDropSkills.Contains(skillId);

        public void UnlockDropSkill(string skillId)
        {
            if (!unlockedDropSkills.Contains(skillId))
            {
                unlockedDropSkills.Add(skillId);
                SkillMessage?.Invoke($"Acquired secret martial arts book: {skillId} unlocked!");
                Changed?.Invoke();
            }
        }

        public void LockDropSkill(string skillId)
        {
            if (unlockedDropSkills.Remove(skillId))
            {
                Changed?.Invoke();
            }
        }

        public void UnlockAllDropSkills()
        {
            if (availableSkills == null) return;
            foreach (var skill in availableSkills)
            {
                if (skill != null && skill.requiresSkillBook)
                    unlockedDropSkills.Add(skill.skillId);
            }
            SkillMessage?.Invoke("All Rare Drop Book Skills unlocked for testing!");
            Changed?.Invoke();
        }

        public void LockAllDropSkills()
        {
            unlockedDropSkills.Clear();
            SkillMessage?.Invoke("All Rare Drop Book Skills locked.");
            Changed?.Invoke();
        }

        public bool CanUpgrade(SkillDefinitionSO skill, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "Unknown skill"; return false; }
            if (skill.requiresSkillBook && !unlockedDropSkills.Contains(skill.skillId))
            {
                reason = $"Requires {skill.acquisitionNote}";
                return false;
            }

            int curRank = GetRank(skill);
            if (curRank >= skill.maxRank)
            {
                reason = "Maximum rank reached";
                return false;
            }

            if (progression == null) { reason = "Progression missing"; return false; }
            if (progression.CurrentLevel < skill.requiredCharacterLevel)
            {
                reason = $"Requires character level {skill.requiredCharacterLevel}";
                return false;
            }

            int cost = skill.GetSkillPointCostForRank(curRank);
            if (progression.SkillPoints < cost)
            {
                reason = $"Requires {cost} skill point(s)";
                return false;
            }

            return true;
        }

        public int TryUpgradeMultiple(SkillDefinitionSO skill, int maxCount)
        {
            if (skill == null || maxCount <= 0 || progression == null) return 0;

            int upgraded = 0;
            while (upgraded < maxCount)
            {
                if (!CanUpgrade(skill, out _)) break;
                int curRank = GetRank(skill);
                int cost = skill.GetSkillPointCostForRank(curRank);
                if (!progression.TrySpendSkillPoints(cost)) break;

                learnedRanks[skill.skillId] = curRank + 1;
                upgraded++;
            }

            if (upgraded > 0)
            {
                SkillMessage?.Invoke($"Upgraded {skill.displayName} to Rank {learnedRanks[skill.skillId]}");
                Changed?.Invoke();
            }
            return upgraded;
        }

        public bool IsOnCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            return cooldownEndTimes.TryGetValue(skill.skillId, out float endTime) && Time.time < endTime;
        }

        public float GetRemainingCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return 0f;
            if (cooldownEndTimes.TryGetValue(skill.skillId, out float endTime))
                return Mathf.Max(0f, endTime - Time.time);
            return 0f;
        }

        public bool CanCast(SkillDefinitionSO skill, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "No skill selected"; return false; }
            if (!IsLearned(skill))
            {
                reason = skill.requiresSkillBook
                    ? $"Requires {skill.acquisitionNote}"
                    : "Learn this skill from the trainer first.";
                return false;
            }
            if (resources == null || !resources.IsAlive)
            {
                reason = "Cannot cast while defeated.";
                return false;
            }
            if (IsOnCooldown(skill))
            {
                float rem = GetRemainingCooldown(skill);
                reason = $"{skill.displayName} on cooldown ({rem:0.0}s remaining).";
                return false;
            }

            int rank = GetRank(skill);
            float chiCost = skill.GetChiCost(rank);
            if (!resources.CanSpendChi(chiCost))
            {
                reason = $"Not enough Chi ({chiCost:0} Chi required, current: {resources.Chi:0}).";
                return false;
            }
            return true;
        }

        public bool TryCastSkill(SkillDefinitionSO skill, out string reason)
        {
            if (!CanCast(skill, out reason)) return false;

            int rank = GetRank(skill);
            float chiCost = skill.GetChiCost(rank);
            if (chiCost > 0f)
            {
                if (!resources.TrySpendChi(chiCost))
                {
                    reason = "Failed to spend Chi.";
                    return false;
                }
            }

            // Set cooldown
            cooldownEndTimes[skill.skillId] = Time.time + skill.cooldown;

            // Apply effect to CharacterBuffManager with rank
            if (buffManager != null)
            {
                buffManager.ApplyBuff(skill, rank);
            }

            SkillCast?.Invoke(skill);
            SkillMessage?.Invoke($"Activated {skill.displayName} (Rank {rank})!");
            Changed?.Invoke();
            return true;
        }
    }
}
