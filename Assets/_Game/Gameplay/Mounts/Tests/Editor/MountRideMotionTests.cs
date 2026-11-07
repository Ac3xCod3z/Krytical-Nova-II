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
        public void GettingOn_StartsOnTheGround_EndsOnTheSeat_ClearingItOnTheWay([Values(false, true)] bool running)
        {
            MountTransition on = settings.Transition(true, running);
            Assert.AreEqual(0f, LiftAt(on, 0f), 0.001f);
            Assert.AreEqual(1f, LiftAt(on, on.seconds), 0.001f);
            float highest = 0f;
            for (float s = 0f; s <= on.seconds; s += 1f / 30f) highest = Mathf.Max(highest, LiftAt(on, s));
            Assert.Greater(highest, 1f, "the hop clears the seat before dropping into it");
        }

        [Test]
        public void GettingOff_StartsOnTheSeat_EndsOnTheGround_AndOnThePlayer([Values(false, true)] bool running)
        {
            MountTransition off = settings.Transition(false, running);
            Assert.AreEqual(1f, LiftAt(off, 0f), 0.001f);
            Assert.AreEqual(0f, LiftAt(off, off.freeSeconds), 0.001f, "on the ground by the time the rider is free");
            Assert.AreEqual(0f, MountRideMotion.Ahead(off.forward, off.freeSeconds, off.seconds), 0.001f,
                "back on the player's real position before the rider is free (no snap)");
        }

        [Test]
        public void RunningDismount_LaunchesAhead_StandingFlipDoesNot()
        {
            MountTransition run = settings.getOffRunning;
            float ahead = 0f;
            for (float s = 0f; s <= run.seconds; s += 1f / 30f) ahead = Mathf.Max(ahead, MountRideMotion.Ahead(run.forward, s, run.seconds));
            Assert.Greater(ahead, 0.5f, "the launch arc carries the rider ahead of the player");
            Assert.AreEqual(0f, MountRideMotion.Ahead(settings.getOff.forward, 0.5f, settings.getOff.seconds), 0.001f);
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
        public void TheMount_IsGoneByTheTimeTheRiderIsFree([Values(false, true)] bool running)
        {
            MountTransition off = settings.Transition(false, running);
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
