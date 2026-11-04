using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The melee rules (M4.4, NGO PlayerMeleeCombat with the prefab's numbers): strike lengths, the hit at the window's end, the combo order
    /// and cadence, the server's validation range, the arc, and who a swing lands on where the attacker saw them.
    /// </summary>
    public class MeleeRulesTests
    {
        static MeleeTiming T => MeleeTiming.Prefab(new int3(1200000, 1000000, 1500000));

        [Test]
        public void Strikes_LastTheirClipAtThePrefabsSpeed_AndLandAtTheWindowsEnd()
        {
            MeleeTiming t = T;
            Assert.AreEqual(850460, MeleeRules.StrikeMicros(t, 1), "1.2 s at 1.411");
            Assert.AreEqual(769230, MeleeRules.StrikeMicros(t, 2), "1.0 s at 1.30");
            Assert.AreEqual(1332149, MeleeRules.StrikeMicros(t, 3), "1.5 s at 1.126");
            Assert.AreEqual(552799, MeleeRules.HitAfterMicros(t, 1), "65 % of the strike");
            Assert.AreEqual(230769, MeleeRules.HitAfterMicros(t, 2), "30 %");
            Assert.AreEqual(932504, MeleeRules.HitAfterMicros(t, 3), "70 % (the prefab's 0.33-0.70)");
            Assert.AreEqual(1.15f, MeleeRules.StepMultiplier(t, 2), 1e-6);
            Assert.AreEqual(1.35f, MeleeRules.StepMultiplier(t, 3), 1e-6);
        }

        [Test]
        public void TheCombo_Goes123_NoSooner70PercentIn_AndRestartsAfterThePause()
        {
            MeleeTiming t = T;
            Assert.AreEqual(1, MeleeRules.NextStep(t, 0, 0), "a fresh attack starts at 1");
            Assert.AreEqual(0, MeleeRules.NextStep(t, 1, 595000), "too soon: under 70 % of step 1");
            Assert.AreEqual(2, MeleeRules.NextStep(t, 1, 595400));
            Assert.AreEqual(3, MeleeRules.NextStep(t, 2, 600000));
            Assert.AreEqual(0, MeleeRules.NextStep(t, 3, 1582148), "step 3, then the 0.25 s pause");
            Assert.AreEqual(1, MeleeRules.NextStep(t, 3, 1582149), "back to 1");
        }

        [Test]
        public void TheServer_ChecksTheValidationRange_AndTheArcIsInFrontWithinReach()
        {
            MeleeTiming t = T;
            HitProxy p = HitProxy.Player;
            Assert.IsTrue(MeleeRules.WithinValidation(t, int3.zero, new int3(2700, 0, 0)));
            Assert.IsFalse(MeleeRules.WithinValidation(t, int3.zero, new int3(2701, 0, 0)));
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, 1500), p), "straight ahead (yaw 0 = +z)");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, -1500), p), "behind");
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 0, new int3(1200, 0, 1200), p), "45 degrees off");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(1900, 0, 500), p), "76 degrees off: out of the 120 degree arc");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, 2200), p), "beyond reach + radius (2.1 m)");
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 9000, new int3(1500, 0, 0), p), "facing +x");
        }

        static PositionHistory Standing(int3 feet, uint from = 100, uint to = 120)
        {
            var ring = new PositionHistory();
            for (uint k = from; k < to; k++) PositionHistoryRules.Record(ref ring, new NetworkTick(k), feet);
            return ring;
        }

        [Test]
        public void ASwing_LandsOnTheTargetAndTheOthersInTheArc_WhereTheAttackerSawThem()
        {
            MeleeTiming t = T;
            HitProxy p = HitProxy.Player;
            var now = new NetworkTick(119);
            var target = new PositionHistory();
            for (uint k = 100; k < 120; k++) PositionHistoryRules.Record(ref target, new NetworkTick(k), new int3(0, 0, 1000 + 100 * (int)(k - 100)));
            using var others = new NativeArray<PositionHistory>(new[]
            {
                Standing(new int3(800, 0, 1200)),    // in front, beside the target: caught
                Standing(new int3(0, 0, -1200)),     // behind: missed
                Standing(new int3(0, 0, 1000), 117), // there only from 117: not seen at 110
            }, Allocator.Temp);
            var hit = new NativeList<int>(Allocator.Temp);
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 2400), others, p, out bool targetHit, ref hit));
            Assert.IsTrue(targetHit, "seen at 2.0 m, now at 2.4 m: within reach then, within validation now");
            CollectionAssert.AreEqual(new[] { 0 }, hit.AsArray().ToArray(), "only the one in the arc on the seen tick");

            hit.Clear();
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 3000), others, p, out targetHit, ref hit));
            Assert.IsFalse(targetHit, "it got away past the validation range before the hit was applied");

            hit.Clear();
            Assert.IsFalse(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(100), target, new int3(0, 0, 2400), others, p, out targetHit, ref hit));
            Assert.IsFalse(targetHit, "a claim older than the ring hits nobody");
            Assert.AreEqual(0, hit.Length);
            hit.Dispose();
        }
    }
}
