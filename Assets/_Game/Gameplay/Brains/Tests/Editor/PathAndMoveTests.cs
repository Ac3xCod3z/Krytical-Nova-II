using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Brains.Tests
{
    /// <summary>M5.4a (implementation plan s4): the path service's budget and turns, and the move system (arrive, separation, turn rate).</summary>
    public sealed class PathServiceTests
    {
        static Entity Queued(BrainTestWorld w, float3 at, uint id, byte priority, uint requestTick)
        {
            Entity e = w.Walker(at, id, at, 0f, BrainMoverFlags.Stop);   // stopped: only the service touches the path
            w.Em.SetComponentData(e, new BrainPath { To = at + new float3(5f, 0f, 0f), RequestTick = requestTick, Status = BrainPathStatus.Queued, Priority = priority });
            return e;
        }

        static int Ready(BrainTestWorld w, Entity[] brains)
        {
            int n = 0;
            foreach (Entity b in brains) if (w.Em.GetComponentData<BrainPath>(b).Status == BrainPathStatus.Ready) n++;
            return n;
        }

        [Test]
        public void Budget_NeverExceeded()
        {
            using var w = new BrainTestWorld(movement: true);
            var brains = new Entity[100];
            for (int i = 0; i < brains.Length; i++) brains[i] = Queued(w, new float3(i % 10 * 3f, 0f, i / 10 * 3f), (uint)i, 0, 1);
            int before = 0;
            for (int tick = 1; tick <= 13; tick++)
            {
                w.Step();
                int ready = Ready(w, brains);
                Assert.LessOrEqual(ready - before, w.Config.PathSearchesPerTick, $"tick {tick}");
                Assert.AreEqual(math.min(100, tick * 8), ready, $"tick {tick}");
                before = ready;
            }
            Assert.AreEqual(100, Ready(w, brains), "all served by tick 13");
            Assert.AreEqual(1, w.Em.GetBuffer<BrainWaypoint>(brains[0]).Length, "the straight line: one waypoint");
        }

        [Test]
        public void EngagedFirst()
        {
            using var w = new BrainTestWorld(movement: true);
            var patrol = new Entity[20];
            for (int i = 0; i < patrol.Length; i++) patrol[i] = Queued(w, new float3(i * 3f, 0f, 0f), (uint)i, 0, 1);
            var fighting = new Entity[3];
            for (int i = 0; i < fighting.Length; i++) fighting[i] = Queued(w, new float3(i * 3f, 0f, 9f), (uint)(100 + i), 1, 1);
            w.Step();
            Assert.AreEqual(3, Ready(w, fighting), "every fighting brain in the first tick");
            Assert.AreEqual(5, Ready(w, patrol), "the rest of the budget goes to the oldest others");
        }

        [Test]
        public void Failure_MarksThePathFailed_AndTheBrainWaits()
        {
            using var w = new BrainTestWorld(movement: true);
            Entity wall = w.Em.CreateEntity();
            w.Em.AddBuffer<PathTestWall>(wall).Add(new PathTestWall { A = new float2(3f, -10f), B = new float2(3f, 10f) });
            Entity b = w.Walker(float3.zero, 1, new float3(8f, 0f, 0f), 4f);
            w.Step();
            BrainPath asked = w.Em.GetComponentData<BrainPath>(b);
            Assert.AreEqual(BrainPathStatus.Queued, asked.Status, "blocked: it asks for a path");
            w.Step();
            Assert.AreEqual(BrainPathStatus.Failed, w.Em.GetComponentData<BrainPath>(b).Status);
            w.Step(30);
            Assert.AreEqual(BrainPathStatus.Failed, w.Em.GetComponentData<BrainPath>(b).Status, "the same destination is not asked again");
            Assert.AreEqual(asked.RequestTick, w.Em.GetComponentData<BrainPath>(b).RequestTick);
            Assert.Less(math.length(w.Position(b)), 0.01f, "it waits where it is");
        }
    }

    public sealed class MoveTests
    {
        [Test]
        public void ArrivesWithin0_2m_AndStays()
        {
            using var w = new BrainTestWorld(movement: true);
            var dest = new float3(20f, 0f, 0f);
            Entity b = w.Walker(float3.zero, 1, dest, 4f);
            w.Step(5 * BrainTestWorld.TickRate + 10);
            float3 there = w.Position(b);
            Assert.LessOrEqual(math.distance(there.xz, dest.xz), PathRules.ArriveRadius + 1e-4f);
            w.Step(BrainTestWorld.TickRate);
            Assert.Less(math.distance(w.Position(b), there), 1e-4f, "no jitter once there");
        }

        [Test]
        public void Separation_NoOverlapBelow0_9m()
        {
            using var w = new BrainTestWorld(movement: true);
            var brains = new Entity[20];
            for (int i = 0; i < brains.Length; i++)
            {
                float a = i * 2f * math.PI / brains.Length;
                brains[i] = w.Walker(new float3(math.sin(a), 0f, math.cos(a)) * 10f, (uint)i, float3.zero, 4f);
            }
            float closest = float.MaxValue;
            for (int tick = 0; tick < 300; tick++)
            {
                w.Step();
                for (int i = 0; i < brains.Length; i++)
                for (int j = i + 1; j < brains.Length; j++)
                    closest = math.min(closest, math.distance(w.Position(brains[i]).xz, w.Position(brains[j]).xz));
            }
            Assert.GreaterOrEqual(closest, 0.9f);
        }

        [Test]
        public void TurnRate_540dps()
        {
            using var w = new BrainTestWorld(movement: true);
            Entity b = w.Walker(float3.zero, 1, float3.zero, 0f, BrainMoverFlags.Stop | BrainMoverFlags.Face);
            BrainMover m = w.Em.GetComponentData<BrainMover>(b);
            m.Yaw = math.PI - 0.001f;   // face behind, turning the positive way
            w.Em.SetComponentData(b, m);
            w.Em.SetComponentData(b, new BrainClock { StableId = 1, EngagedUntilTick = 1000 });   // fighting: 540 deg/s
            w.Step(5);
            float yaw = PathRules.YawOf(w.Em.GetComponentData<LocalTransform>(b).Rotation);
            Assert.AreEqual(90f, math.degrees(yaw), 0.5f, "540 deg/s at 30 Hz: 18 deg a tick");
            w.Step(5);
            yaw = PathRules.YawOf(w.Em.GetComponentData<LocalTransform>(b).Rotation);
            Assert.AreEqual(180f, math.degrees(math.abs(yaw)), 0.5f);
        }
    }
}
