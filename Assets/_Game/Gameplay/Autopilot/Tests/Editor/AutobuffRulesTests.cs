using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Autopilot.Tests
{
    /// <summary>The Autobuff licence by the clock, its eight cells, and which buff a press casts next.</summary>
    public sealed class AutobuffRulesTests
    {
        private static readonly long Now = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc).Ticks;
        private static long Days(double days) => TimeSpan.FromDays(days).Ticks;

        private readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in made) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        private SkillDefinitionSO Skill(string id, float damage, SkillCategory category = SkillCategory.Support)
        {
            SkillDefinitionSO skill = ScriptableObject.CreateInstance<SkillDefinitionSO>();
            made.Add(skill);
            skill.skillId = id;
            skill.maxDamagePercent = damage;
            skill.category = category;
            return skill;
        }

        [Test]
        public void AnExpiredLicenceStartsAgainFromNow()
        {
            long expires = AutobuffRules.Extend(Now, Now - Days(10), 3);
            Assert.AreEqual(Now + Days(3), expires);
            Assert.IsTrue(AutobuffRules.IsActive(Now, expires));
            Assert.IsFalse(AutobuffRules.IsActive(Now, Now - Days(10)));
        }

        [Test]
        public void ARunningLicenceIsLengthenedFromItsEnd()
        {
            long running = Now + Days(2);
            Assert.AreEqual(running + Days(14), AutobuffRules.Extend(Now, running, 14));
        }

        [Test]
        public void APassPastTheCapIsRefused()
        {
            Assert.IsFalse(AutobuffRules.WouldExceed(Now, Now + Days(60), 30, 90));
            Assert.IsTrue(AutobuffRules.WouldExceed(Now, Now + Days(61), 30, 90));
            Assert.IsFalse(AutobuffRules.WouldExceed(Now, 0, 30, 90), "a fresh licence is always within it");
        }

        [Test]
        public void TheTimeLeftLineSaysWhatToDoWhenItHasRunOut()
        {
            StringAssert.Contains("Autoward Pass", AutobuffRules.TimeLeftLine(Now, Now - 1));
            StringAssert.StartsWith("Autoward: 2d", AutobuffRules.TimeLeftLine(Now, Now + Days(2.5)));
        }

        [Test]
        public void TheCellsAreAlwaysEightAndHoldASkillOnce()
        {
            string[] cells = AutobuffRules.Sanitise(new[] { "Aegis", " Redline ", null, "Aegis", new string('x', 80) });
            Assert.AreEqual(AutobuffRules.Slots, cells.Length);
            Assert.AreEqual("Aegis", cells[0]);
            Assert.AreEqual("Redline", cells[1]);
            Assert.AreEqual(string.Empty, cells[2]);
            Assert.AreEqual(string.Empty, cells[3], "the second Aegis is cleared");
            Assert.AreEqual(string.Empty, cells[4], "too long to be a skill id");
            Assert.AreEqual(AutobuffRules.Slots, AutobuffRules.Sanitise(null).Length);
            Assert.AreEqual(2, AutobuffRules.Count(cells));
        }

        [Test]
        public void OnlyBuffSkillsAreTaken()
        {
            Assert.IsTrue(AutobuffRules.Accepts(Skill("Aegis", 0f)));
            Assert.IsFalse(AutobuffRules.Accepts(Skill("Slash", 150f)), "an attack");
            Assert.IsFalse(AutobuffRules.Accepts(Skill("Odd", 0f, SkillCategory.Combat)), "a combat skill with no damage is still not a buff");
            Assert.IsFalse(AutobuffRules.Accepts(null));
        }

        [Test]
        public void TheNextBuffToCastSkipsThoseUpAndThoseThatCannotBeCast()
        {
            string[] cells = AutobuffRules.Sanitise(new[] { "Aegis", "Redline", "", "Guard" });
            var up = new HashSet<string> { "Aegis" };
            var castable = new HashSet<string> { "Aegis", "Guard" };
            Assert.AreEqual(3, AutobuffRules.NextToCast(cells, up.Contains, castable.Contains), "Aegis is up, Redline cannot be cast: Guard");
            up.Add("Guard");
            Assert.AreEqual(-1, AutobuffRules.NextToCast(cells, up.Contains, castable.Contains), "nothing left to cast now");
            castable.Add("Redline");
            Assert.AreEqual(1, AutobuffRules.NextToCast(cells, up.Contains, castable.Contains));
        }
    }
}
