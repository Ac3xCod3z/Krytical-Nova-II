using System;
using System.Reflection;
using NUnit.Framework;

namespace KryticalNova.Autopilot.Tests
{
    /// <summary>
    /// Perf round 3, R3-8: the entity server sends an owner its Autopilot choices only when <see cref="AutopilotPreferences.SameAs"/>
    /// says they changed, instead of comparing JSON built every tick. Same answer: SameAs is false exactly when the JSON differs, for
    /// every field the save has (a field added later and missing from SameAs fails here).
    /// </summary>
    public class AutopilotPreferencesCompareTests
    {
        static object Changed(object value)
        {
            switch (value)
            {
                case bool b: return !b;
                case int i: return i + 7;
                case float f: return f + 3.5f;
                case Enum e:
                    Array all = Enum.GetValues(e.GetType());
                    return all.GetValue((Array.IndexOf(all, e) + 1) % all.Length);
                default: throw new NotSupportedException("AutopilotPreferences got a field of type " + value?.GetType() + ": teach this test and SameAs about it");
            }
        }

        [Test]
        public void ACopy_IsTheSame_AndHasTheSameJson()
        {
            var live = new AutopilotPreferences { huntRadius = 40f, attackSlot = 3, takeCommon = true };
            AutopilotPreferences copy = live.Copy();
            Assert.AreNotSame(live, copy);
            Assert.IsTrue(live.SameAs(copy));
            Assert.AreEqual(live.ToJson(), copy.ToJson());
            Assert.IsFalse(live.SameAs(null));
        }

        [Test]
        public void ChangingAnyField_IsSeen_ExactlyWhenTheJsonChanges()
        {
            FieldInfo[] fields = typeof(AutopilotPreferences).GetFields(BindingFlags.Public | BindingFlags.Instance);
            Assert.Greater(fields.Length, 20, "the choices' fields");
            foreach (FieldInfo field in fields)
            {
                var sent = new AutopilotPreferences();
                AutopilotPreferences live = sent.Copy();
                field.SetValue(live, Changed(field.GetValue(live)));
                Assert.AreNotEqual(sent.ToJson(), live.ToJson(), field.Name + " is in the JSON");
                Assert.IsFalse(live.SameAs(sent), field.Name + " is missing from AutopilotPreferences.SameAs: its change would never reach the owner");
            }
        }
    }
}
