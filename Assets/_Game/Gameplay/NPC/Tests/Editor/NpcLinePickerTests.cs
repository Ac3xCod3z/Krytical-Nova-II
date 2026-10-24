using System;
using System.Collections.Generic;
using KryticalNova.NPC;
using NUnit.Framework;

namespace KryticalNova.NPC.Tests
{
    /// <summary>A vendor says a different line each visit: never the same twice in a row, and every line comes up.</summary>
    public sealed class NpcLinePickerTests
    {
        [Test]
        public void NoLines_GivesNone() => Assert.AreEqual(-1, NpcLinePicker.Next(0, -1, n => 0));

        [Test]
        public void OneLine_IsAlwaysIt() => Assert.AreEqual(0, NpcLinePicker.Next(1, 0, n => 0));

        [Test]
        public void NeverRepeats_AndReachesEveryLine()
        {
            var random = new Random(7);
            Func<int, int> roll = max => random.Next(max);
            var seen = new HashSet<int>();
            int last = -1;
            for (int i = 0; i < 500; i++)
            {
                int next = NpcLinePicker.Next(5, last, roll);
                Assert.That(next, Is.InRange(0, 4));
                Assert.AreNotEqual(last, next, "the same line twice in a row");
                seen.Add(next);
                last = next;
            }
            Assert.AreEqual(5, seen.Count, "every line should come up");
        }

        [Test]
        public void StepsOverTheLastLine()
        {
            // Rolling the lowest or highest of the other lines lands either side of the last one.
            Assert.AreEqual(0, NpcLinePicker.Next(3, 1, n => 0));
            Assert.AreEqual(2, NpcLinePicker.Next(3, 1, n => 1));
        }
    }
}
