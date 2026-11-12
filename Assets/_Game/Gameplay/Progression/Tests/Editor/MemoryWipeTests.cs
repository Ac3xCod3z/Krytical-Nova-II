using KryticalNova.Core;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Memory Wipe on a player of its own (offline, so it decides): ranks bought with skill points come back as exactly those
    /// points, ranks a GM gave come back as nothing, Skill Chip unlocks stay, and a wiped skill casts nothing (Autobuff cells
    /// and the Autopilot cast through the same check).
    /// </summary>
    public sealed class MemoryWipeTests
    {
        private GameObject go;
        private CharacterProgressionData data;
        private CharacterStatData stats;
        private SkillDefinitionSO strike, ward, chipSkill;
        private PlayerCharacterProgression progression;
        private PlayerCombatSkills skills;
        private PlayerMemoryWipe wipe;
        private static readonly UsedItem Wipe = new UsedItem(9038, "Memory Wipe");

        private static SkillDefinitionSO Skill(string id, int maxRank, int toLearn, int toMax, bool chip = false)
        {
            var skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            skill.skillId = id;
            skill.displayName = id;
            skill.maxRank = maxRank;
            skill.skillPointsToLearn = toLearn;
            skill.totalSkillPointsToMax = toMax;
            skill.requiresSkillBook = chip;
            return skill;
        }

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<CharacterProgressionData>();
            data.startingStatPoints = 5;
            data.startingSkillPoints = 1;
            data.levels = new CharacterLevelStep[30];
            for (int i = 0; i < data.levels.Length; i++)
                data.levels[i] = new CharacterLevelStep { experienceRequired = 10, statPointReward = 5, skillPointReward = 4 };
            stats = ScriptableObject.CreateInstance<CharacterStatData>();
            strike = Skill("WipeStrike", 20, 1, 20);
            ward = Skill("WipeWard", 7, 3, 4);
            chipSkill = Skill("WipeChip", 12, 2, 30, chip: true);

            go = new GameObject("WipeTester");
            go.SetActive(false);
            progression = go.AddComponent<PlayerCharacterProgression>();
            var serialized = new SerializedObject(progression);
            serialized.FindProperty("progressionData").objectReferenceValue = data;
            serialized.FindProperty("statData").objectReferenceValue = stats;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            skills = go.AddComponent<PlayerCombatSkills>();
            wipe = go.AddComponent<PlayerMemoryWipe>();
            go.SetActive(true);
            skills.SetAvailableSkills(new[] { strike, ward, chipSkill });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            foreach (Object o in new Object[] { data, stats, strike, ward, chipSkill }) Object.DestroyImmediate(o);
        }

        [Test]
        public void AFreshCharacter_IsRefused_AndNothingChanges()
        {
            Assert.AreEqual(ProgressionResetRules.ReasonNoSkillsLearned, wipe.UseBlockedReason(Wipe));
            Assert.AreEqual(1, progression.SkillPoints);
        }

        [Test]
        public void BoughtRanks_ComeBackAsExactlyThePointsSpent_AndCanBeBoughtAgain()
        {
            progression.SetLevel(31);
            skills.UnlockDropSkill(chipSkill.skillId);
            int before = progression.SkillPoints;
            Assert.AreEqual(9, skills.TryUpgradeMultiple(strike, 9));
            Assert.AreEqual(3, skills.TryUpgradeMultiple(ward, 3));
            Assert.AreEqual(2, skills.TryUpgradeMultiple(chipSkill, 2));
            Assert.Less(progression.SkillPoints, before);

            Assert.IsNull(wipe.UseBlockedReason(Wipe));
            wipe.ServerUse(Wipe);
            Assert.AreEqual(before, progression.SkillPoints, "free skill points equal those before spending");
            Assert.AreEqual(0, skills.GetRank(strike));
            Assert.AreEqual(0, skills.GetRank(ward));
            Assert.IsFalse(skills.IsLocked(chipSkill), "the Skill Chip unlock stays");

            Assert.AreEqual(9, skills.TryUpgradeMultiple(strike, 9), "the same ranks can be bought again");
            Assert.AreEqual(3, skills.TryUpgradeMultiple(ward, 3));
            Assert.AreEqual(2, skills.TryUpgradeMultiple(chipSkill, 2));
        }

        [Test]
        public void GivenRanks_OnAFullyLeveledTestCharacter_AreRefused_AndKept()
        {
            progression.SetLevel(31);
            skills.GmSetAllRanksToMax(); // TestCharacterKit / gm.skills.max: no point spent
            int free = progression.SkillPoints;
            Assert.AreEqual(ProgressionResetRules.ReasonOnlyGiven, wipe.UseBlockedReason(Wipe));
            Assert.AreEqual(free, progression.SkillPoints);
            Assert.AreEqual(strike.maxRank, skills.GetRank(strike));
        }

        [Test]
        public void BoughtAndGiven_HandsBackOnlyWhatWasBought()
        {
            progression.SetLevel(5); // earned 1 + 4 x 4 = 17
            int earned = progression.SkillPointsEarnedByLevel;
            skills.TryUpgradeMultiple(ward, 2);
            skills.GmSetAllRanksToMax();
            wipe.ServerUse(Wipe);
            Assert.AreEqual(earned, progression.SkillPoints, "free never passes what the level earned");
        }

        [Test]
        public void AWipedSkill_CastsNothing()
        {
            progression.SetLevel(10);
            skills.TryUpgradeMultiple(ward, 1);
            wipe.ServerUse(Wipe);
            Assert.IsFalse(skills.CanCast(ward, null, out string reason));
            StringAssert.StartsWith("Learn ", reason, "refused as unlearned, before any Flux is read");
            Assert.AreEqual(0, skills.TryCastWardsAtOnce(new[] { ward }, out _), "an Autobuff cell naming it casts nothing");
        }

        [Test]
        public void SpentOnRanks_CountsASharedRankOnce()
        {
            progression.SetLevel(10);
            skills.TryUpgradeMultiple(strike, 4);
            long paid = 0;
            for (int r = 0; r < 4; r++) paid += strike.GetSkillPointCostForRank(r);
            skills.SetAvailableSkills(new[] { strike, strike, ward });
            Assert.AreEqual(paid, skills.SpentOnRanks());
        }
    }
}
