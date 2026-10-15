using KryticalNova.Items;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The Flux Flask's drinks, as its stack's data carries them: 0 is full.</summary>
    public sealed class FlaskRulesTests
    {
        private const int Charges = 3;

        [Test]
        public void ANewFlaskIsFull()
        {
            Assert.AreEqual(Charges, FlaskRules.Left(0UL, Charges));
            Assert.IsTrue(FlaskRules.HasDrink(0UL, Charges));
        }

        [Test]
        public void EachDrinkUsesOneUntilItIsEmpty()
        {
            ulong data = 0UL;
            for (int drink = 1; drink <= Charges; drink++)
            {
                data = FlaskRules.AfterDrink(data, Charges);
                Assert.AreEqual(Charges - drink, FlaskRules.Left(data, Charges));
            }
            Assert.IsFalse(FlaskRules.HasDrink(data, Charges));
            Assert.AreEqual(0, FlaskRules.Left(FlaskRules.AfterDrink(data, Charges), Charges), "an empty Flask stays empty");
        }

        [Test]
        public void ARefillCostsItsUsedDrinksAndEmptiesTheCount()
        {
            ulong data = FlaskRules.AfterDrink(FlaskRules.AfterDrink(0UL, Charges), Charges);
            Assert.AreEqual(120L, FlaskRules.RefillCost(data, Charges, 60));
            Assert.AreEqual(0L, FlaskRules.RefillCost(0UL, Charges, 60));
            Assert.AreEqual(Charges, FlaskRules.Left(FlaskRules.Refilled(data), Charges));
        }

        [Test]
        public void TheHigherBitsOfTheDataAreLeftAlone()
        {
            ulong data = 0xAB00UL;
            Assert.AreEqual(0xAB01UL, FlaskRules.AfterDrink(data, Charges));
            Assert.AreEqual(0xAB00UL, FlaskRules.Refilled(0xAB02UL));
        }

        [Test]
        public void ADrinkGivesItsShareOverItsSeconds()
        {
            Assert.AreEqual(45f, FlaskRules.FluxPerSecond(800f, 0.45f, 8f), 1e-3f);
        }
    }
}
