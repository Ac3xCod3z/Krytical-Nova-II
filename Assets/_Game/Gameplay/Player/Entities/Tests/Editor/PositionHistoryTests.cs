using NUnit.Framework;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The lag compensation ring (M4.3, plan acceptance list): a target that moved away after the attacker saw it is still hit within the
    /// window; a claim older than the window is refused, not trusted; a tick the target was not there cannot be hit; and in the server and
    /// client worlds at 0 and 100 ms the server's ring holds the last 16 ticks of the ghost's own place.
    /// </summary>
    public class PositionHistoryTests
    {
        static NetworkTick T(uint t) => new NetworkTick(t);

        // a target walking away along x, 0.5 m a tick, from x = 1 m, ticks 100..119
        static PositionHistory Walking()
        {
            var ring = new PositionHistory();
            for (uint t = 100; t < 120; t++) PositionHistoryRules.Record(ref ring, T(t), new int3(1000 + 500 * (int)(t - 100), 0, 0));
            return ring;
        }

        [Test]
        public void TheRing_KeepsTheLast16Ticks_AndOneSamplePerTick()
        {
            PositionHistory ring = Walking();
            Assert.AreEqual(16, ring.Samples.Length);
            Assert.AreEqual(104u, ring.Samples[0].Tick, "the oldest four went");
            Assert.AreEqual(119u, ring.Samples[15].Tick);
            PositionHistoryRules.Record(ref ring, T(119), new int3(42, 0, 0));
            Assert.AreEqual(16, ring.Samples.Length, "the same tick again replaces it");
            Assert.IsTrue(PositionHistoryRules.TryFeetAt(ring, T(119), out int3 feet));
            Assert.AreEqual(42, feet.x);
            Assert.IsTrue(PositionHistoryRules.TryFeetAt(ring, T(106), out feet));
            Assert.AreEqual(4000, feet.x);
            Assert.IsFalse(PositionHistoryRules.TryFeetAt(ring, T(103), out _), "gone from the ring");
        }

        [Test]
        public void AHit_IsTestedWhereTheAttackerSawTheTarget_WithinTheWindowOnly()
        {
            PositionHistory ring = Walking();
            var now = T(119);
            var attacker = new int3(0, 0, 0);
            const int reach = 1600;   // NGO's melee reach
            // seen at 106: the target stood at 4 m... out of reach; at 104 it stood at 3 m; reach 1.6 + radius 0.5 = 2.1 m
            Assert.IsFalse(PositionHistoryRules.HitTestAtTick(ring, HitProxy.Player, now, T(106), attacker, reach, out bool refused));
            Assert.IsFalse(refused);
            var near = new int3(2000, 0, 0);
            Assert.IsTrue(PositionHistoryRules.HitTestAtTick(ring, HitProxy.Player, now, T(104), near, reach, out refused),
                "the target moved away after the attacker saw it: still hit (it stood at 3 m, the attacker at 2 m)");
            Assert.IsFalse(PositionHistoryRules.HitTestAtTick(ring, HitProxy.Player, now, now, near, reach, out refused), "where it is now: 10.5 m, a miss");

            Assert.IsFalse(PositionHistoryRules.HitTestAtTick(ring, HitProxy.Player, now, T(103), near, reach, out refused));
            Assert.IsTrue(refused, "a claim 16 ticks old is older than the ring: refused, not trusted");
            Assert.AreEqual(now, PositionHistoryRules.TestTick(now, T(130), out refused), "a claim from the future is now");
            Assert.IsFalse(refused);
            Assert.AreEqual(now, PositionHistoryRules.TestTick(now, NetworkTick.Invalid, out refused), "no claim is now");

            var fresh = new PositionHistory();
            PositionHistoryRules.Record(ref fresh, T(118), new int3(500, 0, 0));
            Assert.IsFalse(PositionHistoryRules.HitTestAtTick(fresh, HitProxy.Player, now, T(110), attacker, reach, out refused),
                "a target that was not there on the tick cannot be hit");
            Assert.IsFalse(refused);
        }

        [Test]
        public void TheCapsule_IsUprightOnItsFeet()
        {
            HitProxy p = HitProxy.Player;
            Assert.IsTrue(PositionHistoryRules.WithinReach(int3.zero, new int3(2100, 0, 0), p, 1600), "reach + radius, on the edge");
            Assert.IsFalse(PositionHistoryRules.WithinReach(int3.zero, new int3(2101, 0, 0), p, 1600));
            Assert.IsTrue(PositionHistoryRules.WithinReach(int3.zero, new int3(1400, 0, 1400), p, 1600), "on the ground plane (1.98 m)");
            Assert.IsTrue(PositionHistoryRules.WithinReach(new int3(0, 3500, 0), int3.zero, p, 1600), "above its head, within the reach");
            Assert.IsFalse(PositionHistoryRules.WithinReach(new int3(0, 3700, 0), int3.zero, p, 1600), "too far above");
            Assert.IsFalse(PositionHistoryRules.WithinReach(new int3(0, -3700, 0), int3.zero, p, 1600), "too far below");
        }

        [TestCase(0)]
        [TestCase(100)]
        public void OnTheServer_TheRingHoldsTheGhostsOwnPlace_TickByTick(int delayMs)
        {
            using var rig = PlayerRig.Create(1, delayMs);
            rig.IssueOrder(0, new int3(6000, 0, 0));
            for (int i = 0; i < 40; i++)
            {
                rig.Tick();
                PositionHistory ring = rig.Get<PositionHistory>(rig.Server, rig.ServerGhosts[0]);
                Assert.Greater(ring.Samples.Length, 0, "written every tick");
                PositionSample last = ring.Samples[ring.Samples.Length - 1];
                Assert.AreEqual(rig.ServerTick, last.Tick, "this tick's sample");
                Assert.AreEqual(rig.ServerPose().Mm, last.FeetMm, "the server's own place on it");
            }
            PositionHistory full = rig.Get<PositionHistory>(rig.Server, rig.ServerGhosts[0]);
            Assert.AreEqual(PositionHistoryRules.Ticks, full.Samples.Length, "16 ticks kept");
            Assert.AreEqual(PositionHistoryRules.Ticks - 1, (int)(full.Samples[15].Tick - full.Samples[0].Tick), "one sample a tick, none skipped");
            Assert.AreNotEqual(full.Samples[0].FeetMm, full.Samples[15].FeetMm, "it walked meanwhile");
        }
    }
}
