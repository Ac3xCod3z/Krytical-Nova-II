using System.Collections.Generic;
using KryticalNova.Crafting;
using KryticalNova.Economy;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    public sealed class CraftRulesTests
    {
        private const ushort Alumite = 9100, Iridion = 9101, Cyan = 9310, Magenta = 9311, Yellow = 9312, Black = 9313, Chroma = 9300;
        private readonly Dictionary<ushort, int> held = new Dictionary<ushort, int>();

        [SetUp]
        public void SetUp() => held.Clear();

        private int Have(ushort itemId) => held.TryGetValue(itemId, out int count) ? count : 0;

        private static CraftRecipe AlloyUp => new CraftRecipe
        {
            id = 1, resultItemId = Iridion, resultCount = 1, ingredients = new[] { new CraftIngredient(Alumite, 4) }
        };

        private static CraftRecipe ChromaCell => new CraftRecipe
        {
            id = 10, resultItemId = Chroma, resultCount = 1,
            ingredients = new[] { new CraftIngredient(Cyan, 1), new CraftIngredient(Magenta, 1), new CraftIngredient(Yellow, 1), new CraftIngredient(Black, 1) }
        };

        [Test]
        public void FourOfOneGrade_MakeOneOfTheNext()
        {
            held[Alumite] = 4;
            Assert.IsNull(CraftRules.BlockedReason(AlloyUp, 1, 1, 0, Have));
            Assert.IsNotNull(CraftRules.BlockedReason(AlloyUp, 2, 1, 0, Have), "eight are needed for two");
            held[Alumite] = 11;
            Assert.AreEqual(2, CraftRules.MaxCrafts(AlloyUp, 0, Have, 99));
        }

        [Test]
        public void AChromaCell_NeedsOneOfEachColour()
        {
            held[Cyan] = 3; held[Magenta] = 3; held[Yellow] = 3;
            StringAssert.Contains("You need 1", CraftRules.BlockedReason(ChromaCell, 1, 1, 0, Have));
            Assert.AreEqual(0, CraftRules.MaxCrafts(ChromaCell, 0, Have, 99));
            held[Black] = 2;
            Assert.IsNull(CraftRules.BlockedReason(ChromaCell, 2, 1, 0, Have));
            Assert.AreEqual(2, CraftRules.MaxCrafts(ChromaCell, 0, Have, 99), "the scarcest colour decides");
        }

        [Test]
        public void ARecipeMayAskForALevelAndCreds()
        {
            CraftRecipe recipe = AlloyUp;
            recipe.requiredLevel = 80;
            recipe.creds = 1_000;
            held[Alumite] = 40;
            Assert.AreEqual("Requires level 80.", CraftRules.BlockedReason(recipe, 1, 79, 5_000, Have));
            Assert.IsNull(CraftRules.BlockedReason(recipe, 5, 80, 5_000, Have));
            Assert.AreEqual(CraftRules.ReasonNoCreds, CraftRules.BlockedReason(recipe, 6, 80, 5_000, Have));
            Assert.AreEqual(5, CraftRules.MaxCrafts(recipe, 5_000, Have, 99), "the Creds run out before the Alumite");
            Assert.AreEqual(3, CraftRules.MaxCrafts(recipe, 5_000, Have, 3), "never more than one request may make");
        }

        [Test]
        public void TheSameMaterialListedTwice_IsNeededTwice()
        {
            var recipe = new CraftRecipe
            {
                id = 2, resultItemId = Iridion, resultCount = 1,
                ingredients = new[] { new CraftIngredient(Alumite, 2), new CraftIngredient(Alumite, 2) }
            };
            held[Alumite] = 3;
            Assert.IsNotNull(CraftRules.BlockedReason(recipe, 1, 1, 0, Have));
            held[Alumite] = 4;
            Assert.IsNull(CraftRules.BlockedReason(recipe, 1, 1, 0, Have));
            Assert.AreEqual(1, CraftRules.MaxCrafts(recipe, 0, Have, 99));
        }

        [Test]
        public void TheCosts_AreTheIngredientsTimesTheCrafts()
        {
            var costs = new List<ItemCost>();
            CraftRules.Costs(ChromaCell, 3, costs);
            Assert.AreEqual(4, costs.Count);
            foreach (ItemCost cost in costs) Assert.AreEqual(3, cost.Count);
            Assert.AreEqual(Cyan, costs[0].ItemId);
        }

        [Test]
        public void ARecipeWithNothingInIt_IsNotARecipe()
        {
            Assert.AreEqual(CraftRules.ReasonNoRecipe, CraftRules.BlockedReason(default, 1, 99, 0, Have));
            Assert.AreEqual(CraftRules.ReasonNoRecipe, CraftRules.BlockedReason(AlloyUp, 0, 99, 0, Have));
            Assert.AreEqual(0, CraftRules.MaxCrafts(default, 0, Have, 99));
        }
    }
}
