using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The four ways on and off a mount (owner, 6 Nov 2026): a standing summon hop, a running hop, a standing front flip
    /// and a running launch flip into a roll. The rider starts and ends where they should, the mount is solid as the rider
    /// lands on it and gone as the rider lands off it, and only running picks the running kind.
    /// </summary>
    public sealed class MountRideMotionTests
    {
        private MountSettings settings;

        [SetUp] public void Make() => settings = ScriptableObject.CreateInstance<MountSettings>();
        [TearDown] public void Drop() => Object.DestroyImmediate(settings);

        private static float LiftAt(MountTransition t, float seconds) => MountRideMotion.Lift(t.lift, seconds, t.seconds);

        [Test]
        public void TheClipCarriesTheHeight_TheCodeKeepsTheRiderOnTheSeat([Values(true, false)] bool on, [Values(false, true)] bool running)
        {
            // (both running getting-off endings are covered by RunningDismount_IsAllInTheClip_NoDrawnArc)
            // The Brambleback's seat is only 0.2-0.5 m above the rider's feet, so the hops and flips are keyed per stage
            // from the ground to the seat; the code's lift stays on the seat the whole way.
            MountTransition t = settings.Transition(on, running);
            for (float s = 0f; s <= t.seconds; s += 1f / 30f) Assert.AreEqual(1f, LiftAt(t, s), 0.001f);
        }

        [Test]
        public void GettingOff_IsBackOnThePlayer_BeforeTheRiderIsFree([Values(false, true)] bool running)
        {
            MountTransition off = settings.Transition(false, running);
            Assert.AreEqual(0f, MountRideMotion.Ahead(off.forward, off.freeSeconds, off.seconds), 0.001f,
                "back on the player's real position before the rider is free (no snap)");
        }

        [Test]
        public void RunningDismount_IsAllInTheClip_NoDrawnArc([Values(true, false)] bool endsRunning)
        {
            // Owner-approved 7 Nov 2026: take-off, flip, roll and ending are keyed; the player is never drawn ahead.
            MountTransition t = settings.Transition(false, true, endsRunning);
            for (float s = 0f; s <= t.seconds; s += 1f / 30f)
                Assert.AreEqual(0f, MountRideMotion.Ahead(t.forward, s, t.seconds), 0.001f);
        }

        [Test]
        public void RunningDismount_TwoEndings_TheIdleOneIsLonger()
        {
            Assert.AreSame(settings.getOffRunning, settings.Transition(false, true, true));
            Assert.AreSame(settings.getOffRunningToIdle, settings.Transition(false, true, false));
            Assert.Greater(settings.getOffRunningToIdle.seconds, settings.getOffRunning.seconds);
            Assert.Less(settings.getOffRunning.rollEndsAt, settings.getOffRunning.freeSeconds, "the roll ends before the rider is free");
            Assert.AreEqual(4f, settings.rollIntoRunMinDistance, 0.001f, "owner, 7 Nov 2026: 4 m");
        }

        [Test]
        public void RollIntoRun_OnlyWhenTheClickedSpotIsStillFarAsTheRollEnds()
        {
            // 10 m left at the press, 3 m/s on foot, roll ends at 1.77 s: 4.69 m left then, so up running.
            Assert.IsTrue(MountRules.DismountEndsRunning(10f, 3f, 1.77f, 4f));
            // 8 m left: 2.69 m left as the roll ends, so the roll ends standing.
            Assert.IsFalse(MountRules.DismountEndsRunning(8f, 3f, 1.77f, 4f));
            // No path (or the spot already reached): standing.
            Assert.IsFalse(MountRules.DismountEndsRunning(0f, 3f, 1.77f, 4f));
            // Exactly the threshold counts as running.
            Assert.IsTrue(MountRules.DismountEndsRunning(4f, 0f, 1.77f, 4f));
        }

        [Test]
        public void TheMount_IsSolidAsTheRiderLandsOnIt([Values(false, true)] bool running)
        {
            MountTransition on = settings.Transition(true, running);
            Assert.AreEqual(on.stirrupsAt * on.seconds, on.effectDelay + on.effectSeconds, 0.03f,
                "the mount is solid as the rider lands on the seat (owner pick)");
            Assert.LessOrEqual(on.freeSeconds, on.seconds);
        }

        [Test]
        public void TheMount_IsGoneByTheTimeTheRiderIsFree([Values(false, true)] bool running, [Values(true, false)] bool endsRunning)
        {
            MountTransition off = settings.Transition(false, running, endsRunning);
            Assert.LessOrEqual(off.effectDelay + off.effectSeconds, off.freeSeconds);
        }

        [Test]
        public void OnlyRunning_PicksTheRunningKind()
        {
            Assert.IsTrue(MountRules.IsRunningTransition(true, true));
            Assert.IsFalse(MountRules.IsRunningTransition(true, false), "Run selected but standing still");
            Assert.IsFalse(MountRules.IsRunningTransition(false, true), "walking: the rider stops");
            Assert.IsFalse(MountRules.IsRunningTransition(false, false));
        }

        [Test]
        public void Curves_ClampOutsideTheClip_AndDefaultWithNoCurve()
        {
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.AreEqual(0f, MountRideMotion.Lift(curve, -1f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(curve, 5f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(null, 0.3f, 1f), 0.001f);
            Assert.AreEqual(0f, MountRideMotion.Ahead(null, 0.3f, 1f), 0.001f);
        }
    }
}
