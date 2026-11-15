using System;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// Perf night chunk 3: <see cref="MeleeResolveServerSystem.LastVerdict"/> keeps the last due swing's numbers and writes the text only
    /// when read (KnCadenceProbe). The text is exactly the old per-swing line; recording a swing allocates nothing.
    /// </summary>
    public sealed class MeleeVerdictTextTests
    {
        // The old line, verbatim, on the same typed values the system had (byte step, int3 positions, int counts, bools).
        static string OldLine(byte step, int3 me, int3 them, int ticksBack, int samples, bool resolved, bool targetHit, int reachMm) =>
            $"step {step}: me {me}, target now {them}, seen {ticksBack} ticks back, target ring " +
            $"{samples} samples, resolved {resolved}, target hit {targetHit}, reach {reachMm} mm";

        [TestCase((byte)1, 0, 0, 0, 3, 12, true, true, 2500)]
        [TestCase((byte)3, -12345, 678, 90123, 0, 0, false, false, 0)]
        [TestCase((byte)2, int.MaxValue, int.MinValue, 7, -1, 31, true, false, 3100)]
        public void TheVerdictText_IsTheOldLine(byte step, int x, int y, int z, int ticksBack, int samples, bool resolved, bool hit, int reach)
        {
            var me = new int3(x, y, z); var them = new int3(z, x, y);
            var v = new MeleeResolveServerSystem.Verdict
            {
                Step = step, Me = me, Them = them, TicksBack = ticksBack, RingSamples = samples, Resolved = resolved, TargetHit = hit, ReachMm = reach,
            };
            Assert.AreEqual(OldLine(step, me, them, ticksBack, samples, resolved, hit, reach), v.ToString());
        }

        [Test]
        public void RecordingSwings_AllocatesNothing_TheOldLineDid()
        {
            // GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono (proved by the first run): the Test Framework's own
            // GC.Alloc recorder is the instrument; the heap growth printed beside it is only an estimate.
            const int swings = 1000;
            var v = default(MeleeResolveServerSystem.Verdict);
            string last = null;
            for (int i = 0; i < 10; i++) last = OldLine((byte)(i % 3 + 1), new int3(i), new int3(-i), i, i, true, false, 2500);   // warm up
            TestDelegate oldWay = () => { for (int i = 0; i < swings; i++) last = OldLine((byte)(i % 3 + 1), new int3(i), new int3(-i), i, i, true, false, 2500); };
            TestDelegate newWay = () =>
            {
                for (int i = 0; i < swings; i++)
                    v = new MeleeResolveServerSystem.Verdict { Step = (byte)(i % 3 + 1), Me = new int3(i), Them = new int3(-i), TicksBack = i, RingSamples = i, Resolved = true, ReachMm = 2500 };
            };
            long before = GC.GetTotalMemory(false); oldWay(); long oldBytes = GC.GetTotalMemory(false) - before;
            before = GC.GetTotalMemory(false); newWay(); long newBytes = GC.GetTotalMemory(false) - before;
            string line = $"[perf chunk 3] melee verdict, {swings} due swings: old ~{oldBytes} bytes of heap, new ~{newBytes} bytes";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            Assert.That(oldWay, Is.AllocatingGCMemory(), "the old per-swing line made garbage");
            Assert.That(newWay, Is.Not.AllocatingGCMemory(), "recording a swing makes none");
            Assert.IsNotNull(last); Assert.AreEqual(swings - 1, v.TicksBack);
        }
    }
}
