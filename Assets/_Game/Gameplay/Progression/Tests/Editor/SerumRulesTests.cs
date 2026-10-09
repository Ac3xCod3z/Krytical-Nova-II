using System.Collections.Generic;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Serums: a kind is taken up to the cap, what it gives stays, and the cap is the settings' own.</summary>
    public sealed class SerumRulesTests
    {
        private SerumSettings settings;

        [SetUp]
        public void Create() => settings = ScriptableObject.CreateInstance<SerumSettings>();

        [TearDown]
        public void Destroy() => Object.DestroyImmediate(settings);

        private int Kind(ushort itemId) => SerumRules.KindOf(settings, itemId);

        private static float Sum(List<StatModifier> modifiers, StatType stat)
        {
            float total = 0f;
            foreach (StatModifier modifier in modifiers)
            {
                if (modifier.Stat != stat) continue;
                Assert.AreEqual(ModifierType.Flat, modifier.ModType);
                total += modifier.Value;
            }
            return total;
        }

        [Test]
        public void TheSixKindsAreTwelveSkyTwosAtItsValues()
        {
            Assert.AreEqual(6, SerumRules.KindCount(settings));
            Assert.AreEqual(100, settings.Cap);
            var one = new List<StatModifier>();
            var state = new SerumState();
            for (int i = 0; i < 6; i++) state = state.With(i, 1);
            SerumRules.AddModifiers(settings, state, this, one);
            Assert.AreEqual(3f, Sum(one, StatType.AttackPower));
            Assert.AreEqual(20f, Sum(one, StatType.MaxHP));
            Assert.AreEqual(2f, Sum(one, StatType.HitRate));
            Assert.AreEqual(2f, Sum(one, StatType.EvasionRate));
            Assert.AreEqual(25f, Sum(one, StatType.MaxChi));
            Assert.AreEqual(10f, Sum(one, StatType.ElementalAttack));
            Assert.AreEqual(10f, Sum(one, StatType.ElementalDefense));
        }

        [Test]
        public void AnItemThatIsNoSerumIsNoKind()
        {
            Assert.AreEqual(0, Kind(SerumIds.Strength));
            Assert.AreEqual(5, Kind(SerumIds.ElementalWard));
            Assert.AreEqual(-1, Kind(9001));
            Assert.AreEqual(-1, Kind(0));
            Assert.IsNotNull(SerumRules.UseBlockedReason(settings, -1, default));
            Assert.IsNotNull(SerumRules.UseBlockedReason(null, 0, default));
        }

        [Test]
        public void EachOneTakenAddsItsGain_AndTheSheetSaysSo()
        {
            int strength = Kind(SerumIds.Strength);
            var state = new SerumState();
            for (int i = 0; i < 37; i++)
            {
                Assert.IsNull(SerumRules.UseBlockedReason(settings, strength, state));
                state = SerumRules.Take(settings, strength, state);
            }
            Assert.AreEqual(37, state.Taken(strength));
            Assert.AreEqual(0, state.Taken(Kind(SerumIds.Vitality)), "one kind does not count toward another");

            var modifiers = new List<StatModifier>();
            SerumRules.AddModifiers(settings, state, this, modifiers);
            Assert.AreEqual(1, modifiers.Count);
            Assert.AreEqual(111f, Sum(modifiers, StatType.AttackPower));
            Assert.AreSame(this, modifiers[0].Source);
            Assert.AreEqual("Strength Serum (37/100) +111 Attack Power", SerumRules.SheetLine(settings, strength, state));

            int agility = Kind(SerumIds.Agility);
            state = state.With(agility, 2);
            Assert.AreEqual("Agility Serum (2/100) +4 Hit Rate, +4 Evasion", SerumRules.SheetLine(settings, agility, state));
        }

        [Test]
        public void AtTheCapOneMoreIsRefused_AndChangesNothing()
        {
            int vitality = Kind(SerumIds.Vitality);
            SerumState state = new SerumState().With(vitality, 99);
            Assert.IsNull(SerumRules.UseBlockedReason(settings, vitality, state));
            state = SerumRules.Take(settings, vitality, state);
            Assert.AreEqual(100, state.Taken(vitality));

            string refusal = SerumRules.UseBlockedReason(settings, vitality, state);
            Assert.IsNotNull(refusal);
            StringAssert.Contains("100", refusal);
            Assert.AreEqual(state, SerumRules.Take(settings, vitality, state), "nothing is taken at the cap");
            Assert.IsNull(SerumRules.UseBlockedReason(settings, Kind(SerumIds.Spirit), state), "the cap is per kind");
        }

        [Test]
        public void TheExpansionRaisesTheCap_OnlyWhenItIsSwitchedOn()
        {
            int strength = Kind(SerumIds.Strength);
            SerumState full = new SerumState().With(strength, 100);
            Assert.IsFalse(settings.expansionEnabled, "off as shipped");
            Assert.IsNotNull(SerumRules.UseBlockedReason(settings, strength, full));

            settings.expansionEnabled = true;
            Assert.AreEqual(200, settings.Cap);
            Assert.IsNull(SerumRules.UseBlockedReason(settings, strength, full));
            SerumState more = SerumRules.Take(settings, strength, full);
            Assert.AreEqual(101, more.Taken(strength));
            Assert.AreEqual(200, SerumRules.Clamp(settings, 250));
        }

        [Test]
        public void ACountAboveTheCapInForceGivesOnlyTheCapsWorth()
        {
            // Should the cap ever be lowered, a character keeps the count but not more than the cap gives.
            int spirit = Kind(SerumIds.Spirit);
            SerumState state = new SerumState().With(spirit, 150);
            var modifiers = new List<StatModifier>();
            SerumRules.AddModifiers(settings, state, this, modifiers);
            Assert.AreEqual(100 * 25f, Sum(modifiers, StatType.MaxChi));
            Assert.AreEqual(100, SerumRules.Clamp(settings, 150));
            Assert.AreEqual(0, SerumRules.Clamp(settings, -4));
        }

        [Test]
        public void TheRecordHoldsEveryKindApart()
        {
            var state = new SerumState();
            for (int i = 0; i < SerumSettings.MaxKinds; i++) state = state.With(i, i + 1);
            for (int i = 0; i < SerumSettings.MaxKinds; i++) Assert.AreEqual(i + 1, state.Taken(i));
            Assert.AreEqual(0, state.Taken(SerumSettings.MaxKinds));
            Assert.IsTrue(state.Any);
            Assert.IsFalse(new SerumState().Any);
            Assert.AreEqual(state, state.With(3, 4));
            Assert.AreNotEqual(state, state.With(3, 5));
        }
    }
}
