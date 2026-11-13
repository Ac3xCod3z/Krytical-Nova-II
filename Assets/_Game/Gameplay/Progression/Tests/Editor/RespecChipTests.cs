using KryticalNova.Core;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The Respec Chip on a progression of its own (offline, so it decides): it hands back exactly what was placed, is refused with
    /// a clear line when nothing was placed or everything was given, and the GM reset stays NGO's uncapped one.
    /// </summary>
    public sealed class RespecChipTests
    {
        private GameObject go;
        private CharacterProgressionData data;
        private CharacterStatData stats;
        private PlayerCharacterProgression progression;
        private static readonly UsedItem Chip = new UsedItem(9037, "Respec Chip");

        [SetUp]
        public void SetUp()
        {
            data = ScriptableObject.CreateInstance<CharacterProgressionData>();
            data.startingStatPoints = 5;
            data.startingSkillPoints = 1;
            data.levels = new CharacterLevelStep[20];
            for (int i = 0; i < data.levels.Length; i++)
                data.levels[i] = new CharacterLevelStep { experienceRequired = 10, statPointReward = 5, skillPointReward = 2 };
            stats = ScriptableObject.CreateInstance<CharacterStatData>();
            stats.allocationCost = 1;
            go = new GameObject("RespecTester");
            go.SetActive(false);
            progression = go.AddComponent<PlayerCharacterProgression>();
            var serialized = new SerializedObject(progression);
            serialized.FindProperty("progressionData").objectReferenceValue = data;
            serialized.FindProperty("statData").objectReferenceValue = stats;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            go.SetActive(true);
            // An EditMode test runs no Awake: the starting points and attributes are set by calling it, as other tests here do.
            typeof(PlayerCharacterProgression).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(progression, null);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(data);
            Object.DestroyImmediate(stats);
        }

        [Test]
        public void AFreshCharacter_IsRefused_AndNothingChanges()
        {
            Assert.AreEqual(ProgressionResetRules.ReasonNoStatsPlaced, progression.UseBlockedReason(Chip));
            Assert.AreEqual(5, progression.StatPoints);
        }

        [Test]
        public void TheChip_HandsBackWhatWasPlaced_AndItCanBePlacedAgain()
        {
            progression.SetLevel(5);
            int before = progression.StatPoints;
            progression.TryAllocateMultiple(CharacterAttribute.Strength, 3);
            progression.TryAllocateMultiple(CharacterAttribute.Spirit, 2);
            Assert.AreEqual(before - 5, progression.StatPoints);
            Assert.IsNull(progression.UseBlockedReason(Chip));
            progression.ServerUse(Chip);
            Assert.AreEqual(before, progression.StatPoints, "every point placed is back");
            Assert.AreEqual(stats.GetStartingValue(CharacterAttribute.Strength), progression.GetAttribute(CharacterAttribute.Strength));
            Assert.AreEqual(3, progression.TryAllocateMultiple(CharacterAttribute.Strength, 3), "placed again");
        }

        [Test]
        public void GivenAttributes_HandBackNothing_ButTheGmResetStillDoes()
        {
            progression.SetAttribute(CharacterAttribute.Agility, stats.GetStartingValue(CharacterAttribute.Agility) + 40); // gm.attr
            Assert.AreEqual(ProgressionResetRules.ReasonOnlyGiven, progression.UseBlockedReason(Chip));
            progression.ResetAllocations();
            Assert.AreEqual(5 + 40, progression.StatPoints, "gm.resetstats is NGO's, uncapped");
        }

        [Test]
        public void BoughtAndGiven_HandsBackOnlyWhatWasBought()
        {
            progression.TryAllocateMultiple(CharacterAttribute.Vitality, 4);
            progression.SetAttribute(CharacterAttribute.Strength, stats.GetStartingValue(CharacterAttribute.Strength) + 10);
            progression.ServerUse(Chip);
            Assert.AreEqual(5, progression.StatPoints, "4 bought back, the 10 given are not turned into points");
        }

        [Test]
        public void ASaveFromBeforeTheChip_LoadsAndResets()
        {
            // a progression part as saved before wave 3b (no field was added): level 3, 7 points left, 3 Spirit placed
            int spirit = stats.GetStartingValue(CharacterAttribute.Spirit) + 3, start = stats.startingStrength;
            progression.RestoreSave("{\"level\":3,\"experience\":0,\"statPoints\":12,\"skillPoints\":5,\"strength\":" + start +
                                    ",\"vitality\":" + start + ",\"agility\":" + start + ",\"spirit\":" + spirit + "}");
            Assert.AreEqual(3, progression.CurrentLevel);
            progression.ServerUse(Chip);
            Assert.AreEqual(15, progression.StatPoints, "earned at level 3 = 5 + 2 x 5; 12 free + 3 placed");
        }
    }
}
