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
