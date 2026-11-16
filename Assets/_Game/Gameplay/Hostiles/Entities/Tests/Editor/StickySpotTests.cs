using System.Collections.Generic;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Sticky spots (owner pick, 15 Nov 2026; his clip: three or four big armoured Hostiles swapping sides every 0.25-0.5 s and standing
    /// in him). Round a player they hold their own spot facing him, step in when they get the turn to strike, step back to the same spot,
    /// and never orbit; a join or a leave moves the others least; no body overlaps another or the player.
    /// </summary>
    public sealed class StickySpotTests
    {
        const byte Alive = SensedFlags.Alive;
        const float Walk = 4f;   // m/s, a Hostile's run in the test world

        static HostileBrainConfig Config()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<HostileBrainSettings>();
            HostileBrainConfig c = HostileBrainConfig.From(settings, HostileTestWorld.TickRate);
            UnityEngine.Object.DestroyImmediate(settings);
            return c;
        }

        static Entity Make(HostileTestWorld w, float bearingDeg, float distance, int radiusMm, byte tier, uint id)
        {
            float a = math.radians(bearingDeg);
            Entity h = w.Hostile(new float3(math.sin(a) * distance, 0f, math.cos(a) * distance), HostileBehaviourKind.FightsBack, id);
            HostileLife life = w.Em.GetComponentData<HostileLife>(h);
            life.RadiusMm = radiusMm;
            life.Tier = tier;
            w.Em.SetComponentData(h, life);
            w.Hit(h, 100, 10f);
            return h;
        }

        static float3 At(HostileTestWorld w, Entity h) => w.Em.GetComponentData<LocalTransform>(h).Position;
        static float Radius(HostileTestWorld w, Entity h) => w.Em.GetComponentData<HostileLife>(h).RadiusMm * 0.001f;
        static HostileSlot Slot(HostileTestWorld w, Entity h) => w.Em.GetComponentData<HostileSlot>(h);
        static float Off(float a, float b) => math.abs(HostileRingRules.Wrap(a - b));

        static void NoBodyOverlaps(HostileTestWorld w, List<Entity> pack, in HostileBrainConfig c, string when)
        {
            for (int i = 0; i < pack.Count; i++)
            {
                Assert.GreaterOrEqual(math.length(At(w, pack[i]).xz) + 1e-3f, c.PlayerRadius + Radius(w, pack[i]), when + ": Hostile " + i + " stands in the player");
                for (int j = i + 1; j < pack.Count; j++)
                    Assert.GreaterOrEqual(math.distance(At(w, pack[i]).xz, At(w, pack[j]).xz) + 1e-3f, Radius(w, pack[i]) + Radius(w, pack[j]),
                        when + ": Hostiles " + i + " and " + j + " overlap");
            }
        }

        /// <summary>
        /// Two to six of mixed sizes coming in from all round: normal bodies, a Colossus (0.78 m, a Bruiser: its arc counts double), a
        /// War Frame (0.6 m); five fit round the player (90 % of the circle), a sixth waits.
        /// </summary>
        static List<Entity> Pack(HostileTestWorld w, int count, uint seed)
        {
            var rng = new Random(seed);
            int[] radii = { 450, 780, 600, 450, 450, 600 };
            var pack = new List<Entity>();
            for (int i = 0; i < count; i++)
                pack.Add(Make(w, rng.NextFloat(-180f, 180f), rng.NextFloat(3f, 6f), radii[i], (byte)(radii[i] == 780 ? HostileTier.Bruiser : 0), (uint)(i + 1)));
            return pack;
        }

        [Test]
        public void TwoToSixOfMixedSizes_HoldTheirSpots_NeverOrbit_NeverOverlap()
        {
            HostileBrainConfig c = Config();
            for (int count = 2; count <= 6; count++)
            for (uint seed = 1; seed <= 4; seed++)
            {
                using var w = new HostileTestWorld { MoveSpeed = Walk };
                w.SetPlayers((float3.zero, Alive));
                List<Entity> pack = Pack(w, count, seed * 31u + (uint)count);
                w.Step(150);   // they come in and take their spots
                var ring = new List<Entity>();
                foreach (Entity h in pack) if (Slot(w, h).Placed != 0) ring.Add(h);
                // four or five fit round the player (90 % of the circle), depending on who comes in first; the rest wait
                Assert.GreaterOrEqual(ring.Count, math.min(count, 4), $"{count} Hostiles, seed {seed}: those that fit have spots");
                if (count <= 4) Assert.AreEqual(count, ring.Count, $"{count} Hostiles, seed {seed}: all fit");
                var waiters = new List<Entity>();
                foreach (Entity h in pack) if (Slot(w, h).Placed == 0) waiters.Add(h);
                pack = ring;
                count = ring.Count;
                var spot = new float[count];
                for (int i = 0; i < count; i++) spot[i] = Slot(w, pack[i]).Angle;
                int strikes = w.Strikes;
                var bearingAtStart = new float[count];
                for (int i = 0; i < count; i++) bearingAtStart[i] = HostileRingRules.Bearing(float3.zero, At(w, pack[i]));
                for (int t = 0; t < 900; t++)   // 30 s of fighting
                {
                    w.Step();
                    string when = $"{count} Hostiles, seed {seed}, tick {w.Tick}";
                    for (int i = 0; i < count; i++)
                    {
                        HostileSlot s = Slot(w, pack[i]);
                        Assert.AreEqual(1, s.Placed, when + ": Hostile " + i + " keeps its spot");
                        Assert.Less(Off(s.Angle, spot[i]), 1e-4f, when + ": Hostile " + i + "'s spot never moves");
                        Assert.Less(Off(HostileRingRules.Bearing(float3.zero, At(w, pack[i])), bearingAtStart[i]), math.radians(10f),
                            when + ": Hostile " + i + " never walks round the player (no orbit, no side swaps)");
                    }
                    NoBodyOverlaps(w, pack, c, when);
                    foreach (Entity h in waiters) Assert.AreEqual(0, Slot(w, h).Placed, when + ": no spot is taken from one that holds it");
                }
                count = pack.Count + waiters.Count;
                Assert.Greater(w.Strikes - strikes, 20, $"{count} Hostiles, seed {seed}: and they do fight");
            }
        }

        [Test]
        public void ASpotIsKept_ThroughTheStrike_InToStrike_BackToTheSameSpot()
        {
            HostileBrainConfig c = Config();
            using var w = new HostileTestWorld { MoveSpeed = Walk };
            w.SetPlayers((float3.zero, Alive));
            // five round the player, so two always wait their turn a step out (the attack budget lets three strike at once)
            var pack = new List<Entity> { Make(w, 0f, 4f, 780, (byte)HostileTier.Bruiser, 1), Make(w, 100f, 4f, 450, 0, 2), Make(w, 150f, 4f, 450, 0, 3),
                Make(w, -150f, 4f, 450, 0, 4), Make(w, -100f, 4f, 450, 0, 5) };
            Entity big = pack[0];
            float stand = HostileRingRules.Stand(c.PlayerRadius, 0.78f, c.SlotStandGap);
            w.Step(150);
            float angle = Slot(w, big).Angle;
            int swings = 0, waitedOut = 0;
            bool wasBusy = false;
            float nearest = float.MaxValue;
            byte last = w.Brain(big).Phase;
            for (int t = 0; t < 900; t++)
            {
                w.Step();
                HostileBrain b = w.Brain(big);
                float dist = math.length(At(w, big).xz);
                bool busy = b.Phase == HostilePhase.Tell || b.Phase == HostilePhase.Windup || b.Phase == HostilePhase.Recover;
                if (busy) nearest = math.min(nearest, dist);
                if (b.Phase == HostilePhase.Tell && last != HostilePhase.Tell)
                {
                    swings++;
                    Assert.LessOrEqual(dist, stand + c.SlotStrikeArrive + 1e-3f, "it stepped in to its strike spot before the swing");
                }
                HostileSlot s = Slot(w, big);
                if (!busy && !wasBusy && s.Chosen == 0 && s.Walking == 0)   // (the tick a recover ends, its walk back starts on the next)
                {
                    // waiting its turn (and after each swing): back on the same spot, a step out
                    waitedOut++;
                    Assert.AreEqual(stand + c.SlotHoldBack, dist, c.SlotHoldDeadBand + 1e-3f, "where it waits, a step out from where it strikes");
                }
                Assert.Less(Off(Slot(w, big).Angle, angle), 1e-4f, "its spot is kept through the strike");
                last = w.Brain(big).Phase;
                wasBusy = busy;
                NoBodyOverlaps(w, pack, c, "tick " + w.Tick);
            }
            Assert.Greater(swings, 3, "the big one gets its turns");
            Assert.Greater(waitedOut, 30, "and waits between them on its own spot");
            Assert.GreaterOrEqual(nearest + 1e-3f, c.PlayerRadius + 0.78f, "and never swings from inside the player");
        }

        [Test]
        public void ALeave_MovesNobody_AJoin_TakesTheFreeSpotNearestIt()
        {
            using var w = new HostileTestWorld { MoveSpeed = Walk };
            w.SetPlayers((float3.zero, Alive));
            var pack = new List<Entity> { Make(w, 0f, 3f, 780, (byte)HostileTier.Bruiser, 1), Make(w, 90f, 3f, 450, 0, 2),
                Make(w, 180f, 3f, 450, 0, 3), Make(w, -90f, 3f, 450, 0, 4) };
            w.Step(150);
            var before = new float[pack.Count];
            for (int i = 0; i < pack.Count; i++) before[i] = Slot(w, pack[i]).Angle;

            w.Em.DestroyEntity(pack[3]);   // one leaves the fight
            pack.RemoveAt(3);
            w.Step(60);
            for (int i = 0; i < pack.Count; i++) Assert.Less(Off(Slot(w, pack[i]).Angle, before[i]), 1e-4f, "a leave moves nobody (" + i + ")");

            Entity late = Make(w, -100f, 5f, 450, 0, 9);   // a newcomer from where the one that left stood
            w.Step(90);
            Assert.AreEqual(1, Slot(w, late).Placed);
            Assert.Less(Off(Slot(w, late).Angle, math.radians(-100f)), 1e-3f, "it takes the free spot right where it comes in");
            for (int i = 0; i < pack.Count; i++) Assert.Less(Off(Slot(w, pack[i]).Angle, before[i]), 1e-4f, "a join with room moves nobody (" + i + ")");
        }

        [Test]
        public void AJoinWithNoGapWideEnough_PushesItsTwoNeighboursEquallyBothWays()
        {
            const float arc = 0.8f;
            var angles = new NativeArray<float>(4, Allocator.Temp);
            var arcs = new NativeArray<float>(4, Allocator.Temp);
            angles[0] = -1.2f; angles[1] = 0.2f; angles[2] = 2.5f;   // a 1.4 rad gap between 0 and 1: too small for a 0.8 arc beside two 0.8s
            for (int i = 0; i < 4; i++) arcs[i] = arc;
            Assert.IsTrue(HostileRingRules.FreeSpot(angles, arcs, 3, arc, 1.1f, out float free), "room between the second and the third");
            Assert.AreEqual(1.1f, free, 1e-4f, "its own bearing is free: it stands where it came in");
            Assert.IsTrue(HostileRingRules.FreeSpot(angles, arcs, 2, arc, -0.5f, out float edge), "between two too close for it: the nearest edge with room");
            Assert.AreEqual(-2.0f, edge, 1e-3f, "just past the one at -1.2 (the gap between the two is too small)");

            // no gap wide enough where it comes in: it takes its bearing and the two either side move apart by half the overlap each
            var two = new NativeArray<float>(3, Allocator.Temp);
            var twoArcs = new NativeArray<float>(3, Allocator.Temp);
            two[0] = -0.7f; two[1] = 0.7f; two[2] = 0f;   // the newcomer at 0, both neighbours 0.7 away, each pair needs 0.8
            for (int i = 0; i < 3; i++) twoArcs[i] = arc;
            HostileRingRules.Spread(two, twoArcs, 3);
            Assert.AreEqual(-0.8f, two[0], 1e-3f, "the left one moves 0.1 left");
            Assert.AreEqual(0.8f, two[1], 1e-3f, "the right one moves 0.1 right");
            Assert.AreEqual(0f, two[2], 1e-3f, "the newcomer stays where it came in");

            // nothing overlaps: nothing moves at all (the ring never turns by itself)
            var apart = new NativeArray<float>(3, Allocator.Temp);
            apart[0] = -2f; apart[1] = 0.1f; apart[2] = 2.2f;
            HostileRingRules.Spread(apart, twoArcs, 3);
            Assert.AreEqual(-2f, apart[0], 0f); Assert.AreEqual(0.1f, apart[1], 0f); Assert.AreEqual(2.2f, apart[2], 0f);
            angles.Dispose(); arcs.Dispose(); two.Dispose(); twoArcs.Dispose(); apart.Dispose();
        }

        [Test]
        public void WaitersHoldTheWaitRing_UntilASpotFrees()
        {
            HostileBrainConfig c = Config();
            using var w = new HostileTestWorld { MoveSpeed = Walk };
            w.SetPlayers((float3.zero, Alive));
            var pack = new List<Entity>();
            for (int i = 0; i < 10; i++) pack.Add(Make(w, 36f * i, 4.5f, 450, 0, (uint)(i + 1)));
            w.Step(150);
            var waiting = new List<Entity>();
            Entity seated = Entity.Null;
            foreach (Entity h in pack)
            {
                if (Slot(w, h).Placed == 0) waiting.Add(h);
                else seated = h;
            }
            Assert.AreEqual(3, waiting.Count, "seven 0.45 m bodies fit round a player, three wait");
            foreach (Entity h in waiting)
            {
                float d = math.length(At(w, h).xz);
                Assert.That(d, Is.InRange(c.WaitRingMin - c.SlotHoldDeadBand, c.WaitRingMax + c.SlotHoldDeadBand), "a waiter holds the wait ring");
            }
            w.Step(300);
            foreach (Entity h in waiting) Assert.AreEqual(0, Slot(w, h).Placed, "no spot is taken from one that holds it");
            w.Em.DestroyEntity(seated);
            w.Step(5);
            int joined = 0;
            foreach (Entity h in waiting) joined += Slot(w, h).Placed;
            Assert.AreEqual(1, joined, "one spot freed: the longest waiting takes it");
        }
    }
}
