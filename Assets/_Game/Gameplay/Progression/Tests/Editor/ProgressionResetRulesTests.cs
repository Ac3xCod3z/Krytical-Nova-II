using System.Collections.Generic;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The sums of the Respec Chip and the Memory Wipe (wave 3b, owner 12 Nov 2026): the hand-back equals what the spend
    /// paid, by the spend's own cost function, and an item never hands back more than the level earned.
    /// </summary>
    public sealed class ProgressionResetRulesTests
    {
        private static CharacterProgressionData Data(int start, int perLevel, int levels)
        {
            var data = ScriptableObject.CreateInstance<CharacterProgressionData>();
            data.startingStatPoints = start;
            data.startingSkillPoints = start;
            data.levels = new CharacterLevelStep[levels];
            for (int i = 0; i < levels; i++)
                data.levels[i] = new CharacterLevelStep { experienceRequired = 10, statPointReward = perLevel, skillPointReward = perLevel };
            return data;
        }

        private static SkillDefinitionSO Skill(int maxRank, int toLearn, int toMax)
        {
            var skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            skill.maxRank = maxRank;
            skill.skillPointsToLearn = toLearn;
            skill.totalSkillPointsToMax = toMax;
            return skill;
        }

        [Test]
        public void EarnedPoints_AreTheStartPlusEveryLevelReached()
        {
            CharacterProgressionData data = Data(5, 3, 10);
            Assert.AreEqual(5, ProgressionResetRules.EarnedStatPoints(data, 1));
            Assert.AreEqual(5 + 3 * 4, ProgressionResetRules.EarnedStatPoints(data, 5));
            Assert.AreEqual(5 + 3 * 10, ProgressionResetRules.EarnedSkillPoints(data, 11));
            Assert.AreEqual(5 + 3 * 10, ProgressionResetRules.EarnedSkillPoints(data, 50), "past the cap nothing more is earned");
            Object.DestroyImmediate(data);
        }

        [Test]
        public void StatPointsPlaced_IsWhatTheAllocationsPaid()
        {
            int[] start = { 10, 10, 10, 10 };
            foreach (int cost in new[] { 1, 2 })
            {
                int[] attributes = { 13, 10, 10, 12 }; // 3 Str, 2 Spi
                Assert.AreEqual(5 * cost, ProgressionResetRules.StatPointsPlaced(attributes, i => start[i], cost));
            }
            Assert.AreEqual(0, ProgressionResetRules.StatPointsPlaced(new[] { 9, 10, 10, 10 }, i => start[i], 1), "below start pays nothing");
        }

        [Test]
        public void CombatRanks_HandBackExactlyWhatTheSpendPaid()
        {
            // odd numbers, so the max(1, ...) floor makes the sum differ from totalSkillPointsToMax
            foreach (SkillDefinitionSO skill in new[] { Skill(20, 1, 20), Skill(7, 3, 4), Skill(12, 0, 100), Skill(1, 2, 2) })
            {
                for (int target = 0; target <= skill.maxRank; target++)
                {
                    long paid = 0;
                    for (int rank = 0; rank < target; rank++) paid += skill.GetSkillPointCostForRank(rank); // the spend loop (PCS TryUpgradeMultiple)
                    Assert.AreEqual(paid, ProgressionResetRules.CombatRankCost(target, skill.GetSkillPointCostForRank), $"max {skill.maxRank} rank {target}");
                }
                Object.DestroyImmediate(skill);
            }
        }

        [Test]
        public void MovementRanks_HandBackEachRanksCost()
        {
            int[] costs = { 0, 1, 2, 4 };
            Assert.AreEqual(0, ProgressionResetRules.MovementRankCost(0, k => costs[k]));
            Assert.AreEqual(1 + 2 + 4, ProgressionResetRules.MovementRankCost(3, k => costs[k]));
        }

        [Test]
        public void SpendWipeSpend_EndsWhereItStarted()
        {
            SkillDefinitionSO skill = Skill(20, 1, 20);
            int free = 40;
            int before = free;
            for (int rank = 0; rank < 9; rank++) free -= skill.GetSkillPointCostForRank(rank);
            free += ProgressionResetRules.Capped(ProgressionResetRules.CombatRankCost(9, skill.GetSkillPointCostForRank), free, before);
            Assert.AreEqual(before, free, "the wipe gives back exactly the points spent");
            for (int rank = 0; rank < 9; rank++) free -= skill.GetSkillPointCostForRank(rank);
            Assert.GreaterOrEqual(free, 0, "spending to the same rank again works");
            Object.DestroyImmediate(skill);
        }

        [Test]
        public void Cap_NeverHandsBackMoreThanTheLevelEarned()
        {
            var random = new System.Random(3);
            for (int run = 0; run < 2000; run++)
            {
                int earned = random.Next(0, 500);
                int bought = random.Next(0, earned + 1);
                int given = random.Next(0, 300);
                int free = earned - bought + (random.Next(4) == 0 ? random.Next(0, 50) : 0); // sometimes a GM added points
                int handedBack = ProgressionResetRules.Capped(bought + given, free, earned);
                Assert.LessOrEqual(free + handedBack, System.Math.Max(free, earned));
                if (free <= earned - bought) Assert.GreaterOrEqual(handedBack, bought, "a real player always gets back what they bought");
            }
        }

        [Test]
        public void GivenRanks_OnAFullyLeveledTestCharacter_HandBackNothing()
        {
            // TestCharacterKit / gm.skills.max: ranks set, no point spent, so free == earned
            Assert.AreEqual(0, ProgressionResetRules.Capped(500, 120, 120));
            Assert.AreEqual(ProgressionResetRules.NoCap, int.MaxValue);
            Assert.AreEqual(500, ProgressionResetRules.Capped(500, 120, ProgressionResetRules.NoCap), "the GM reset is not capped");
        }

        [Test]
        public void ATrimmedHandBack_WritesItsLine_AndAFullOneDoesNot()
        {
            Assert.AreEqual("[Respec] Tester: handed back 4 of 10 (the rest was given, not spent)",
                ProgressionResetRules.TrimLine("Respec", "Tester", 4, 10));
            Assert.IsNull(ProgressionResetRules.TrimLine("Respec", "Tester", 10, 10));
            Assert.IsNull(ProgressionResetRules.TrimLine("Respec", "Tester", 0, 10), "nothing handed back is a refusal, not a trim");
        }

        [Test]
        public void SkillsSharingARank_HaveTheSameCosts()
        {
            var byKey = new Dictionary<string, SkillDefinitionSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinitionSO", new[] { "Assets/_Game" }))
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill == null) continue;
                if (!byKey.TryGetValue(skill.RankKey, out SkillDefinitionSO first)) { byKey[skill.RankKey] = skill; continue; }
                Assert.AreEqual(first.maxRank, skill.maxRank, $"{skill.name} and {first.name} share rank '{skill.RankKey}'");
                for (int rank = 0; rank < skill.maxRank; rank++)
                    Assert.AreEqual(first.GetSkillPointCostForRank(rank), skill.GetSkillPointCostForRank(rank),
                        $"{skill.name} and {first.name} share rank '{skill.RankKey}' but cost differently at rank {rank}");
            }
            Assert.Greater(byKey.Count, 0);
        }
    }
}
