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
            m_Settings.turnSpeed = 540f;
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
            Assert.AreEqual(54000, baked.TurnCdegPerSecond, "540 degrees a second");
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

        [Test]
        public void TheOwnBodysAnimation_ReadsTheGhostsSpeed_NotAMeasurement()
        {
            // the NGO game's own walk animated at the agent's velocity; the DOTS view of the own ghost at its predicted speed
            var body = new GameObject("own body");
            try
            {
                var motor = body.AddComponent<KryticalNova.Player.PlayerAnimancerMotor>();
                PlayerWorldsSetup.DriveAnimationFromGhost(body);
                Assert.IsNotNull(motor.ExternalSpeed);
                Assert.AreEqual(0f, motor.ExternalSpeed(), "no state yet: standing");
                var state = body.AddComponent<KryticalNova.Player.Entities.Presentation.PlayerPresentationState>();
                state.State.Speed = 5f;
                Assert.AreEqual(5f, motor.ExternalSpeed());
                state.State.Speed = 0f;
                Assert.AreEqual(0f, motor.ExternalSpeed(), "the stop is seen on the same frame");
            }
            finally { Object.DestroyImmediate(body); }
        }

        [Test]
        public void AnotherPlayersView_AlsoReadsItsGhostsSpeed_SoARunnerRuns()
        {
            // owner's hand test 7 Nov 2026, x3: another player's view chose walk or run from its run selection, which only the owner gets:
            // a runner slid in the walk clip. The factory's every-body hook now drives a remote view from its ghost's walk speed too.
            var world = new Unity.Entities.World("x3 client");
            var body = new GameObject("remote view");
            try
            {
                var motor = body.AddComponent<KryticalNova.Player.PlayerAnimancerMotor>();
                var factory = PlayerWorldsSetup.InstallClient(world, () => null, loadGhostScene: false);
                factory.BodyMade(body, new KryticalNova.Player.Entities.Presentation.PlayerBodyRequest { IsLocal = false });
                Assert.IsNotNull(motor.ExternalSpeed, "a remote view animates at its ghost's speed");
                var state = body.AddComponent<KryticalNova.Player.Entities.Presentation.PlayerPresentationState>();
                state.State.Speed = 28f;
                Assert.AreEqual(28f, motor.ExternalSpeed(), "a 28 m/s run reads as a run (the run clip from 2.5 m/s)");
                Assert.IsTrue(KryticalNova.Player.PlayerAnimancerMotor.UsesRunClip(false, motor.ExternalSpeed(), false, 2.5f, 0.5f, 0f, 0.2f));
            }
            finally
            {
                Object.DestroyImmediate(body);
                if (world.IsCreated) world.Dispose();
            }
        }
    }
}
