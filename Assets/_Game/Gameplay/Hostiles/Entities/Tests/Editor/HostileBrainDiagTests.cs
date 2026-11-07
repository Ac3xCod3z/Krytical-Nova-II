using System.Collections.Generic;
using KryticalNova.Brains;
using NUnit.Framework;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M5.7: the [KnBrain] timing lines (off by default): a notice when a fight starts, a line per swing start, nothing otherwise.</summary>
    public sealed class HostileBrainDiagTests
    {
        [TearDown]
        public void Reset()
        {
            HostileBrainDiag.On = null;
            HostileBrainDiag.Sink = UnityEngine.Debug.Log;
        }

        [Test]
        public void TheLines_AreANotice_AndASwingStart_AndNothingWhileNothingHappens()
        {
            var lines = new List<string>();
            HostileBrainDiag.Lines(100, 7, HostileMode.Idle, HostileMode.Idle, 0, 0, 0, 0, lines);
            Assert.AreEqual(0, lines.Count, "idle: nothing");
            HostileBrainDiag.Lines(101, 7, HostileMode.Idle, HostileMode.Engage, 0, 0, 0, 42, lines);
            Assert.AreEqual(1, lines.Count);
            StringAssert.StartsWith("[KnBrain] tick 101 hostile #7 notice:", lines[0]);
            StringAssert.Contains("target ghost 42", lines[0]);
            lines.Clear();
            HostileBrainDiag.Lines(110, 7, HostileMode.Engage, HostileMode.Engage, 0, 110, 2, 42, lines);
            Assert.AreEqual(1, lines.Count, "a swing start; no second notice while in the fight");
            StringAssert.StartsWith("[KnBrain] tick 110 hostile #7 swing start: ability 2 at tick 110", lines[0]);
            lines.Clear();
            HostileBrainDiag.Lines(111, 7, HostileMode.Engage, HostileMode.Engage, 110, 110, 2, 42, lines);
            Assert.AreEqual(0, lines.Count, "the same swing: no new line");
            Assert.AreEqual("[KnBrain] tick 122 hostile #7 resolve: hit for 12", HostileBrainDiag.Resolve(122, 7, "hit for 12"));
        }

        [Test]
        public void OffByDefault_NothingIsWritten()
        {
            var written = new List<string>();
            HostileBrainDiag.Sink = written.Add;
            HostileBrainDiag.On = false;
            HostileBrainDiag.Write("x");
            Assert.AreEqual(0, written.Count);
            HostileBrainDiag.On = true;
            HostileBrainDiag.Write("y");
            CollectionAssert.AreEqual(new[] { "y" }, written);
        }
    }
}
