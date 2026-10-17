using KryticalNova.Player;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Wallstride: when it opens, and which surfaces a player can run up (canRunUpWall) or why not.</summary>
    public sealed class WallRunRulesTests
    {
        private WallstrideSettings settings;

        [SetUp]
        public void SetUp() => settings = ScriptableObject.CreateInstance<WallstrideSettings>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(settings);

        // A good climb: an upright wall met head-on, 5 m to a walkable top with room, Run selected, Flux to pay.
        private static ClimbQuery Good() => new ClimbQuery
        {
            Unlocked = true, Stride = true, Eligible = true, Kind = ClimbableKind.Wall,
            Normal = Vector3.back, Travel = Vector3.forward, Height = 5f, HasTop = true, RoomAtTop = true, CanPay = true,
        };

        [Test]
        public void ItOpensAtLevel60_WithRunAndJumpAtRank20()
        {
            Assert.IsTrue(WallRunRules.IsOpen(60, 20, 20, settings));
            Assert.IsFalse(WallRunRules.IsOpen(59, 20, 20, settings));
            Assert.IsFalse(WallRunRules.IsOpen(60, 19, 20, settings));
            Assert.IsFalse(WallRunRules.IsOpen(60, 20, 19, settings));
        }

        [Test]
        public void AnUprightWallMetHeadOn_CanBeRunUp()
        {
            Assert.IsTrue(WallRunRules.CanRunUpWall(Good(), settings));
        }

        [Test]
        public void EachMissingThing_IsTheReason()
        {
            var q = Good(); q.Unlocked = false; Assert.AreEqual(ClimbVerdict.Locked, WallRunRules.Evaluate(q, settings));
            q = Good(); q.Stride = false; Assert.AreEqual(ClimbVerdict.NoStride, WallRunRules.Evaluate(q, settings));
            q = Good(); q.Eligible = false; Assert.AreEqual(ClimbVerdict.NotClimbable, WallRunRules.Evaluate(q, settings));
            q = Good(); q.Kind = ClimbableKind.Never; Assert.AreEqual(ClimbVerdict.NotClimbable, WallRunRules.Evaluate(q, settings));
            q = Good(); q.Height = 1.5f; Assert.AreEqual(ClimbVerdict.TooLow, WallRunRules.Evaluate(q, settings), "vaulted or walked");
            q = Good(); q.Height = 30f; Assert.AreEqual(ClimbVerdict.TooHigh, WallRunRules.Evaluate(q, settings));
            q = Good(); q.HasTop = false; Assert.AreEqual(ClimbVerdict.NoTop, WallRunRules.Evaluate(q, settings));
            q = Good(); q.RoomAtTop = false; Assert.AreEqual(ClimbVerdict.NoRoom, WallRunRules.Evaluate(q, settings));
            q = Good(); q.CanPay = false; Assert.AreEqual(ClimbVerdict.NoFlux, WallRunRules.Evaluate(q, settings));
            foreach (ClimbVerdict verdict in System.Enum.GetValues(typeof(ClimbVerdict)))
                if (verdict != ClimbVerdict.CanRunUpWall) Assert.IsNotEmpty(WallRunRules.Reason(verdict, settings), verdict.ToString());
        }

        [Test]
        public void ASlopeIsWalked_AndAnOverhangIsTooMuch()
        {
            var q = Good();
            q.Normal = new Vector3(0f, Mathf.Sin(40f * Mathf.Deg2Rad), -Mathf.Cos(40f * Mathf.Deg2Rad)); // leans back 40°: a slope of 50°
            Assert.AreEqual(ClimbVerdict.TooFlat, WallRunRules.Evaluate(q, settings));
            q.Normal = new Vector3(0f, -Mathf.Sin(10f * Mathf.Deg2Rad), -Mathf.Cos(10f * Mathf.Deg2Rad)); // leans out 10°
            Assert.AreEqual(ClimbVerdict.CanRunUpWall, WallRunRules.Evaluate(q, settings));
            q.Normal = new Vector3(0f, -Mathf.Sin(30f * Mathf.Deg2Rad), -Mathf.Cos(30f * Mathf.Deg2Rad)); // leans out 30°
            Assert.AreEqual(ClimbVerdict.Overhang, WallRunRules.Evaluate(q, settings));
        }

        [Test]
        public void MetAlongItsFace_ItIsNotRunUp()
        {
            var q = Good();
            q.Travel = Quaternion.AngleAxis(40f, Vector3.up) * Vector3.forward;
            Assert.AreEqual(ClimbVerdict.CanRunUpWall, WallRunRules.Evaluate(q, settings));
            q.Travel = Quaternion.AngleAxis(60f, Vector3.up) * Vector3.forward;
            Assert.AreEqual(ClimbVerdict.Sideways, WallRunRules.Evaluate(q, settings));
        }

        [Test]
        public void AJumpCatchesASurfaceItMeetsAtASlant_ARunDoesNot()
        {
            var q = Good();
            float between = (settings.maxApproachAngle + settings.maxJumpApproachAngle) * 0.5f;
            q.Travel = Quaternion.AngleAxis(between, Vector3.up) * Vector3.forward;
            Assert.AreEqual(ClimbVerdict.Sideways, WallRunRules.Evaluate(q, settings));
            q.FromJump = true;
            Assert.AreEqual(ClimbVerdict.CanRunUpWall, WallRunRules.Evaluate(q, settings));
            q.Travel = Quaternion.AngleAxis(settings.maxJumpApproachAngle + 5f, Vector3.up) * Vector3.forward;
            Assert.AreEqual(ClimbVerdict.Sideways, WallRunRules.Evaluate(q, settings));
        }

        [Test]
        public void ATreeHasNoSlope_OnlyAPlaceToStand()
        {
            var q = Good();
            q.Kind = ClimbableKind.Tree;
            q.Normal = Vector3.back;
            q.Height = 1.5f; // a low perch is still a perch
            Assert.AreEqual(ClimbVerdict.CanRunUpWall, WallRunRules.Evaluate(q, settings));
        }

        [Test]
        public void TheClimbIsAsFastAsTheRun_WithinItsLimits()
        {
            Assert.AreEqual(settings.minClimbSpeed, WallRunRules.ClimbSpeed(1f, settings), 1e-4f);
            // A run speed whose share lands between the limits, whatever they are set to.
            float between = (settings.minClimbSpeed + settings.maxClimbSpeed) * 0.5f / settings.climbSpeedShare;
            Assert.AreEqual(between * settings.climbSpeedShare, WallRunRules.ClimbSpeed(between, settings), 1e-4f);
            Assert.AreEqual(settings.maxClimbSpeed, WallRunRules.ClimbSpeed(100f, settings), 1e-4f);
        }
    }
}
