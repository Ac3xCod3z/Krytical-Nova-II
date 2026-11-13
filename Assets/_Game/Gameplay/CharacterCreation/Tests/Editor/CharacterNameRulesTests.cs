using System;
using KryticalNova.CharacterCreation;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class CharacterNameRulesTests
    {
        [Test]
        public void DigitsAreAllowedAfterTheFirstLetter()
        {
            Assert.IsTrue(CharacterNameRules.Validate("bj0rn", out string name, out string reason), reason);
            Assert.AreEqual("Bj0rn", name);
            Assert.IsTrue(CharacterNameRules.Validate("Ace007", out name, out reason), reason);
        }

        [Test]
        public void ValidNameIsCapitalised()
        {
            Assert.IsTrue(CharacterNameRules.Validate("  aSTRID ", out string name, out string reason), reason);
            Assert.AreEqual("Astrid", name);
        }

        [TestCase("", CharacterNameRules.ReasonEmpty)]
        [TestCase("Al", CharacterNameRules.ReasonTooShort)]
        [TestCase("Abcdefghijklmnopq", CharacterNameRules.ReasonTooLong)]
        [TestCase("Kex 7", CharacterNameRules.ReasonLettersOnly)]
        [TestCase("7kex", CharacterNameRules.ReasonLetterFirst)]
        [TestCase("Ace000", CharacterNameRules.ReasonRepeats)]
        [TestCase("Baaad", CharacterNameRules.ReasonRepeats)]
        [TestCase("ADMIN", CharacterNameRules.ReasonReserved)]
        [TestCase("Null", CharacterNameRules.ReasonReserved)]
        public void BadNamesAreRejectedWithTheirReason(string raw, string expected)
        {
            Assert.IsFalse(CharacterNameRules.Validate(raw, out _, out string reason));
            Assert.AreEqual(expected, reason);
        }

        // Owner, 13 Nov 2026: no name may pass for a GM (case, digits, spaces and symbols ignored).
        [TestCase("GMBob")]
        [TestCase("g.m.Bob")]
        [TestCase("BobGM")]
        [TestCase("bobgm")]
        [TestCase("G_M")]
        [TestCase("[GM]")]
        [TestCase("G1M")]
        [TestCase("G M Ace")]
        [TestCase("Ace9g9m")]
        [TestCase("Magma")] // contains "gm": refused, as the owner's rule is written
        [TestCase("Ogma")]
        public void GmNamesAreRefusedWithTheirOwnReason(string raw)
        {
            Assert.IsFalse(CharacterNameRules.Validate(raw, out _, out string reason));
            Assert.AreEqual(CharacterNameRules.ReasonNoGm, reason);
            Assert.AreEqual("Names can't contain GM.", CharacterNameRules.ReasonNoGm);
        }

        [TestCase("Gamma")]   // G and M apart, a letter between: allowed
        [TestCase("Mog")]     // M before G: allowed
        [TestCase("Gideon")]
        [TestCase("Emberg")]
        public void NamesWithGAndMApartAreAllowed(string raw)
        {
            Assert.IsTrue(CharacterNameRules.Validate(raw, out _, out string reason), reason);
        }

        [Test]
        public void LengthLimitsAreInclusive()
        {
            Assert.IsTrue(CharacterNameRules.Validate("Abc", out _, out _));
            Assert.IsTrue(CharacterNameRules.Validate("Abcdefghijklmnop", out _, out _)); // 16 letters
        }

        [Test]
        public void GeneratedNamesAlwaysPassTheRules()
        {
            var random = new Random(12345);
            for (byte lineage = 1; lineage <= 6; lineage++)
            {
                foreach (BodyType bodyType in Enum.GetValues(typeof(BodyType)))
                {
                    for (int i = 0; i < 100; i++)
                    {
                        string name = CharacterNameGenerator.Generate(lineage, bodyType, random);
                        Assert.IsTrue(CharacterNameRules.Validate(name, out string normalized, out string reason), $"{name}: {reason}");
                        Assert.AreEqual(name, normalized);
                    }
                }
            }
        }
    }
}
