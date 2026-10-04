using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The edits the character creator makes, checked against the baked data the game ships.</summary>
    public sealed class RecipeEditTests
    {
        private const string DatabasePath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";
        private const byte Civilian = 1, Robot = 3;
        private const byte HairChannel = 1;

        private CharacterCreationDatabase database;

        [OneTimeSetUp]
        public void LoadAssets()
        {
            database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabasePath);
            Assert.IsNotNull(database, "Run Krytical Nova/Characters/Bake Everything first.");
        }

        private static readonly BodyType[] BodyTypes = { BodyType.Masculine, BodyType.Feminine };

        private LineageDefinition.RegionOptions Region(LineageDefinition.Setup setup, BodyRegion region)
        {
            Assert.IsTrue(setup.TryGetRegion(region, out var options), region.ToString());
            return options;
        }

        [Test]
        public void DefaultLookIsValidForEveryLineageAndBodyType()
        {
            foreach (var lineage in database.Lineages)
                foreach (var bodyType in BodyTypes)
                {
                    var recipe = RecipeEdit.Default(lineage, bodyType);
                    Assert.AreEqual(lineage.id, recipe.lineage);
                    Assert.AreEqual(lineage.faction, recipe.faction);
                    Assert.AreEqual(bodyType, recipe.bodyType);
                    Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), $"{lineage.displayName} {bodyType}: {reason}");
                    Assert.AreEqual(0, RecipeEdit.MatchingPreset(lineage.SetupFor(bodyType), recipe), lineage.displayName);
                }
        }

        [Test]
        public void SteppingARegionWrapsRoundAndStaysValid()
        {
            var lineage = database.GetLineage(Civilian);
            var setup = lineage.SetupFor(BodyType.Masculine);
            var nose = Region(setup, BodyRegion.Nose);
            var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);
            int start = RecipeEdit.IndexOf(recipe, database.Parts, nose);

            for (int i = 0; i < nose.partIds.Length; i++)
            {
                RecipeEdit.Step(ref recipe, database.Parts, setup, nose, 1);
                Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), reason);
            }
            Assert.AreEqual(start, RecipeEdit.IndexOf(recipe, database.Parts, nose), "a full lap returns to the start");

            RecipeEdit.Select(ref recipe, database.Parts, setup, nose, 0);
            Assert.AreEqual(nose.partIds.Length - 1, RecipeEdit.Step(ref recipe, database.Parts, setup, nose, -1), "back from the first goes to the last");
        }

        [Test]
        public void AnOptionalRegionHasAnEmptyStop()
        {
            var lineage = database.GetLineage(Civilian);
            var setup = lineage.SetupFor(BodyType.Masculine);
            var hair = Region(setup, BodyRegion.Hair);
            Assert.IsTrue(hair.optional);
            var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);

            RecipeEdit.Select(ref recipe, database.Parts, setup, hair, 0);
            Assert.AreEqual(-1, RecipeEdit.Step(ref recipe, database.Parts, setup, hair, -1), "before the first style comes bald");
            Assert.AreEqual(0, RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.Hair));
            Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), reason);
            Assert.AreEqual(0, RecipeEdit.Step(ref recipe, database.Parts, setup, hair, 1));
        }

        [Test]
        public void ARequiredRegionCannotBeEmptied()
        {
            var lineage = database.GetLineage(Civilian);
            var setup = lineage.SetupFor(BodyType.Masculine);
            var nose = Region(setup, BodyRegion.Nose);
            var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);
            ushort before = RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.Nose);
            RecipeEdit.Select(ref recipe, database.Parts, setup, nose, -1);
            Assert.AreEqual(before, RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.Nose));
        }

        [Test]
        public void LeftAndRightChangeTogether()
        {
            var lineage = database.GetLineage(Civilian);
            var setup = lineage.SetupFor(BodyType.Masculine);
            var ears = Region(setup, BodyRegion.EarLeft);
            var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);

            for (int index = 0; index < ears.partIds.Length; index++)
            {
                RecipeEdit.Select(ref recipe, database.Parts, setup, ears, index);
                Assert.IsTrue(database.Parts.TryGet(RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.EarLeft), out var left));
                Assert.IsTrue(database.Parts.TryGet(RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.EarRight), out var right));
                Assert.AreEqual(left.pack, right.pack, $"ear {index}");
                Assert.AreEqual(left.variant, right.variant, $"ear {index}");
            }

            var brows = Region(setup, BodyRegion.EyebrowLeft);
            RecipeEdit.Select(ref recipe, database.Parts, setup, brows, -1);
            Assert.AreEqual(0, RecipeEdit.GetPart(recipe, database.Parts, BodyRegion.EyebrowRight), "no left brow, no right brow");
            Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), reason);
        }

        [Test]
        public void OnlyRegionsWithARealChoiceGetAControl()
        {
            var human = database.GetLineage(Civilian).SetupFor(BodyType.Masculine);
            Assert.IsTrue(RecipeEdit.HasChoice(Region(human, BodyRegion.Hair)));
            Assert.IsTrue(RecipeEdit.HasChoice(Region(human, BodyRegion.EarLeft)));
            Assert.IsFalse(RecipeEdit.HasChoice(Region(human, BodyRegion.EarRight)), "the right ear follows the left");
            Assert.IsFalse(RecipeEdit.HasChoice(Region(human, BodyRegion.Tongue)), "one tongue is no choice");

            var robot = database.GetLineage(Robot).SetupFor(BodyType.Feminine);
            Assert.IsTrue(RecipeEdit.HasChoice(Region(robot, BodyRegion.Head)));
        }

        [Test]
        public void PickingAColourReplacesTheEarlierPick()
        {
            var lineage = database.GetLineage(Civilian);
            var recipe = RecipeEdit.Default(lineage, BodyType.Feminine);
            Assert.IsTrue(database.TryGetChannel(HairChannel, out var hair));

            RecipeEdit.SetColor(ref recipe, HairChannel, hair.swatches[2]);
            int count = recipe.ColorCount;
            RecipeEdit.SetColor(ref recipe, HairChannel, hair.swatches[5]);
            Assert.AreEqual(count, recipe.ColorCount, "one colour per channel");
            Assert.AreEqual(5, RecipeEdit.SwatchIndex(recipe, hair));
            Assert.IsTrue(RecipeEdit.TryGetColor(recipe, HairChannel, out Color32 picked));
            Assert.AreEqual(hair.swatches[5].r, picked.r);
            Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), reason);
        }

        [Test]
        public void APresetKeepsTheBodyShape()
        {
            var lineage = database.GetLineage(Civilian);
            var recipe = RecipeEdit.Default(lineage, BodyType.Masculine);
            recipe.build = 40;
            recipe.muscle = -25;
            var next = RecipeEdit.FromPreset(lineage, BodyType.Masculine, 2, recipe);
            Assert.AreEqual(40, next.build);
            Assert.AreEqual(-25, next.muscle);
            Assert.AreEqual(2, RecipeEdit.MatchingPreset(lineage.SetupFor(BodyType.Masculine), next));
        }

        [Test]
        public void RandomLooksAreAlwaysValid()
        {
            var random = new System.Random(20261004);
            foreach (var lineage in database.Lineages)
                foreach (var bodyType in BodyTypes)
                    for (int i = 0; i < 40; i++)
                    {
                        var recipe = RecipeEdit.Randomize(lineage, bodyType, database, random);
                        Assert.AreEqual(bodyType, recipe.bodyType);
                        Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out string reason), $"{lineage.displayName} {bodyType} #{i}: {reason}");
                    }
        }
    }
}
