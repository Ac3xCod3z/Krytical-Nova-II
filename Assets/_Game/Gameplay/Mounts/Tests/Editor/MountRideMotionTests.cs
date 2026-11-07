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
        public void AHigherSeat_LowersTheRiderOnlyWhileTheFeetAreOnTheGround()
        {
            // Getting on: on the ground until the feet leave, on the seat from arrival.
            Assert.AreEqual(1f, MountRideMotion.OnGround(true, 0f, 0.2f, 0.77f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(true, 0.2f, 0.2f, 0.77f), 0.001f);
            Assert.AreEqual(0f, MountRideMotion.OnGround(true, 0.77f, 0.2f, 0.77f), 0.001f);
            // Getting off: in the saddle until the take-off, on the ground from touchdown, and after it.
            Assert.AreEqual(0f, MountRideMotion.OnGround(false, 0.3f, 0.4f, 1.02f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(false, 1.02f, 0.4f, 1.02f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(false, 1.6f, 0.4f, 1.02f), 0.001f);
            // No times: the seat throughout.
            Assert.AreEqual(0f, MountRideMotion.OnGround(false, 1.6f, 0f, 0f), 0.001f);
            // The Circuit Strider's 50 % seat (0.48 m) against the Brambleback's the clips were keyed on (0.34 m).
            Assert.AreEqual(0.14f, MountRideMotion.GroundDrop(0.48f, 0.34f), 0.001f);
            // Keyed on this seat (or no keyed seat given): no drop.
            Assert.AreEqual(0f, MountRideMotion.GroundDrop(0.5f, 0f), 0.001f);
        }

        // Where the soles end, metres above the ground, after a getting-off on a built stage: the rider's origin is on the
        // seat less the ground drop (all of it once the feet are down), and the clip puts the soles the keyed seat height
        // below its origin (the stage's own seat when it was keyed on it).
        private float LandingSoles(MountDefinition mount, int stage)
        {
            MountStageLook look = mount.Stage(stage);
            MountTransition off = settings.Transition(false, false);
            float keyed = look.clipSeatHeight > 0f ? look.clipSeatHeight : look.seat.y;
            float drop = MountRideMotion.GroundDrop(look.seat.y, look.clipSeatHeight);
            float origin = look.seat.y - drop * MountRideMotion.OnGround(false, off.arrivesAt, off.leavesAt, off.arrivesAt);
            return origin - keyed;
        }

        private static MountDefinition Def(string key) => UnityEditor.AssetDatabase.LoadAssetAtPath<MountDefinition>(
            $"Assets/_Game/Gameplay/Mounts/Data/Assets/MountDef_{key}.asset");

        [Test]
        public void TheBrambleback_HasNoGroundDrop([Values(0, 1, 2)] int stage)
        {
            MountDefinition bramble = Def("Brambleback");
            Assume.That(bramble != null && bramble.IsBuilt, "the Brambleback is built (Items > 11. Build Mounts)");
            MountStageLook look = bramble.Stage(stage);
            Assert.AreEqual(0f, MountRideMotion.GroundDrop(look.seat.y, look.clipSeatHeight), 0.0001f);
        }

        [Test]
        public void TheCircuitStrider_LandsWithItsSolesOnTheGround([Values(0, 1, 2)] int stage)
        {
            MountDefinition strider = Def("CircuitStrider");
            Assume.That(strider != null && strider.IsBuilt, "the Circuit Strider is built (Items > 11. Build Mounts)");
            Assert.That(strider.hopMountUp, "the Strider rides the shared summon hop");
            Assert.Greater(strider.Stage(stage).clipSeatHeight, 0f, "its stages say which seat the shared clips were keyed on");
            Assert.AreEqual(0f, LandingSoles(strider, stage), 0.01f, "soles within 1 cm of the ground");
        }

        [Test]
        public void EveryHopTransition_SaysWhenTheFeetLeaveAndArrive([Values(false, true)] bool on, [Values(false, true)] bool running)
        {
            foreach (bool endsRunning in new[] { false, true })
            {
                MountTransition t = settings.Transition(on, running, endsRunning);
                Assert.Greater(t.arrivesAt, t.leavesAt, "the feet must leave before they arrive");
                Assert.LessOrEqual(t.arrivesAt, t.seconds, "they arrive within the clip");
                // Getting off, the feet are down before the rider is free, so the tail plays on the ground.
                if (!on) Assert.LessOrEqual(t.arrivesAt, t.freeSeconds);
            }
        }

        [Test]
        public void RunningDismount_NeverStallsBeforeTheRollEnds()
        {
            // Ending running: the run speed the whole way.
            Assert.AreEqual(28f, MountRules.DismountTravelSpeed(true, 60f, 28f, 1.76f), 0.001f);
            // Ending standing, 20 m left: 20 / 1.76 = 11.36 m/s, so the body reaches the spot as the roll ends.
            Assert.AreEqual(20f / 1.76f, MountRules.DismountTravelSpeed(false, 20f, 28f, 1.76f), 0.001f);
            // Never faster than the run, even with a long path.
            Assert.AreEqual(28f, MountRules.DismountTravelSpeed(false, 100f, 28f, 1.76f), 0.001f);
            // No path left: no travel.
            Assert.AreEqual(0f, MountRules.DismountTravelSpeed(false, 0f, 28f, 1.76f), 0.001f);
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
