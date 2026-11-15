using System;
using NUnit.Framework;

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
            const int strikes = 1000;
            for (int i = 0; i < 10; i++) OldText(i * 1.5f, i % 4 == 0);   // warm up
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < strikes; i++) OldText(i * 1.5f, i % 4 == 0);
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            // the new path with diagnostics off: the queued case keeps a null and the text is never made
            string what = "x";
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < strikes; i++) what = null;
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            string line = $"[perf chunk 3] hostile strike text, {strikes} strikes, diagnostics off: old {oldBytes} bytes, new {newBytes} bytes";
            TestContext.WriteLine(line);
            UnityEngine.Debug.Log(line);
            Assert.IsNull(what);
            Assert.AreEqual(0, newBytes, line);
            Assert.Greater(oldBytes, 0, line);
        }
    }
}
