using System;
using NUnit.Framework;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Perf night chunk 3: the bridge made "hit for N" for every queued Hostile strike, but only the [KnBrain] diagnostics line
    /// (off by default) reads it. It is now made only when that line is written; the words are the same.
    /// </summary>
    public sealed class HostileHitTextTests
    {
        static string OldText(float dealt, bool evaded) => $"hit for {dealt:0.#}{(evaded ? " (evaded)" : string.Empty)}";

        [TestCase(0f, false), TestCase(12f, false), TestCase(12.34f, true), TestCase(0.05f, false), TestCase(99999.96f, true), TestCase(-3.5f, false)]
        public void TheHitText_IsTheOldText(float dealt, bool evaded) =>
            Assert.AreEqual(OldText(dealt, evaded), HostileStrikeBridge.HitText(dealt, evaded));

        [Test]
        public void WithDiagnosticsOff_AStrikeMakesNoText()
        {
            // GC.GetAllocatedBytesForCurrentThread reads 0 under Unity's Mono: the Test Framework's GC.Alloc recorder decides.
            const int strikes = 1000;
            for (int i = 0; i < 10; i++) OldText(i * 1.5f, i % 4 == 0);   // warm up
            string what = "x";
            System.Action oldWay = () => { for (int i = 0; i < strikes; i++) what = OldText(i * 1.5f, i % 4 == 0); };
            // the new path with diagnostics off: the queued case keeps a null and the text is never made
            System.Action newWay = () => { for (int i = 0; i < strikes; i++) what = HostileBrainDiag.Enabled ? HostileStrikeBridge.HitText(i * 1.5f, i % 4 == 0) : null; };
            HostileBrainDiag.On = false;   // as by default (no other test may leave it on here)
            long before = GC.GetTotalMemory(false); oldWay(); long oldBytes = GC.GetTotalMemory(false) - before;
            before = GC.GetTotalMemory(false); newWay(); long newBytes = GC.GetTotalMemory(false) - before;
            string line = $"[perf chunk 3] hostile strike text, {strikes} strikes, diagnostics off: old ~{oldBytes} bytes of heap, new ~{newBytes} bytes";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            Assert.That(oldWay, Is.AllocatingGCMemory(), "the old text per strike made garbage");
            Assert.That(newWay, Is.Not.AllocatingGCMemory(), "with diagnostics off no text is made");
            Assert.IsNull(what);
            HostileBrainDiag.On = null;
        }
    }
}
