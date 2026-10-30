using KryticalNova.Player.Entities;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>The ghost's walk numbers come from the game's movement data (<see cref="PlayerMoveFromSkills"/>), not from code.</summary>
    public class PlayerMoveFromSkillsTests
    {
        MovementSettings m_Settings;

        [SetUp]
        public void SetUp()
        {
            m_Settings = ScriptableObject.CreateInstance<MovementSettings>();
            m_Settings.acceleration = 30f;
            m_Settings.accelerationPerSpeed = 6f;
            m_Settings.arrivalSlowdownTime = 0.4f;
            m_Settings.arrivalMinRadius = 2f;
            m_Settings.arrivalMaxRadius = 7.5f;
            m_Settings.arrivalMinSpeedFactor = 0.6f;
            m_Settings.stoppingDistance = 0.25f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(m_Settings);

        [Test]
        public void Bake_TurnsTheSettingsAssetIntoTheGhostsIntegers()
        {
            PlayerMoveSettings baked = PlayerMoveFromSkills.Bake(m_Settings);
            Assert.AreEqual(30000, baked.AccelMmps2);
            Assert.AreEqual(6, baked.AccelPerSpeed);
            Assert.AreEqual(400, baked.ArrivalSlowdownMs);
            Assert.AreEqual(2000, baked.ArrivalMinRadiusMm);
            Assert.AreEqual(7500, baked.ArrivalMaxRadiusMm);
            Assert.AreEqual(600, baked.ArrivalMinPermille);
            Assert.AreEqual(250, baked.StopMm);
            Assert.AreEqual(0, baked.MaxClickRangeMm, "no setting for the click range yet: no limit");
        }

        [Test]
        public void Bake_NeverGivesAMaximumRadiusBelowTheMinimum()
        {
            m_Settings.arrivalMaxRadius = 1f;
            Assert.AreEqual(2000, PlayerMoveFromSkills.Bake(m_Settings).ArrivalMaxRadiusMm);
        }

        [Test]
        public void Stats_AreMillimetresASecond_AndRunIsNeverBelowWalk()
        {
            Assert.AreEqual(new MoveStats { WalkMmps = 5000, RunMmps = 9250 }, PlayerMoveFromSkills.Stats(5f, 9.25f));
            Assert.AreEqual(new MoveStats { WalkMmps = 5000, RunMmps = 5000 }, PlayerMoveFromSkills.Stats(5f, 0f), "no Run rank: running walks");
        }

        [Test]
        public void StatsOf_ABodyWithoutMovementSkills_IsNothing_SoTheGhostKeepsItsOwn()
        {
            Assert.IsNull(PlayerMoveFromSkills.StatsOf(null));
            var bare = new GameObject("bare body");
            try { Assert.IsNull(PlayerMoveFromSkills.StatsOf(bare)); }
            finally { Object.DestroyImmediate(bare); }
        }

        [Test]
        public void SettingsOf_NoPrefabYet_IsNothing_WithoutAnError()
        {
            Assert.IsNull(PlayerMoveFromSkills.SettingsOf(null));
        }
    }
}
