using System.Collections.Generic;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>Checks the baked character data and gear dictionary (the assets the game actually ships).</summary>
    public sealed class CharacterDataTests
    {
        private const string DatabasePath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";
        private const string DictionaryPath = "Assets/_Game/Gameplay/Items/Data/Assets/GearDictionary.asset";
        private const byte Civilian = 1, Robot = 3, Skeleton = 4;

        private CharacterCreationDatabase database;
        private GearDictionary dictionary;

        [OneTimeSetUp]
        public void LoadAssets()
        {
            database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabasePath);
            dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(DictionaryPath);
            Assert.IsNotNull(database, "Run Krytical Nova/Characters/Bake Everything first.");
            Assert.IsNotNull(dictionary, "Run Krytical Nova/Characters/Bake Everything first.");
        }

        private CharacterRecipe Preset(byte lineage, BodyType bodyType) =>
            database.GetLineage(lineage).SetupFor(bodyType).presets[0].recipe.Clone();

        [Test]
        public void SixLineagesTwoPerFaction()
        {
            Assert.AreEqual(6, database.Lineages.Count);
            foreach (var faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                Assert.AreEqual(2, database.Lineages.Count(l => l.faction == faction), faction.ToString());
        }

        [Test]
        public void EveryLineageStartsInItsOwnPacksVariant01()
        {
            foreach (var lineage in database.Lineages)
            {
                Assert.AreEqual(12, lineage.basicOutfit.Length, lineage.displayName);
                foreach (ushort id in lineage.basicOutfit)
                {
                    Assert.IsTrue(database.Parts.TryGet(id, out var part), lineage.displayName);
                    Assert.AreEqual(lineage.ownPack, part.pack, lineage.displayName);
                    Assert.AreEqual(1, part.variant, lineage.displayName);
                    Assert.IsTrue(EquipSlots.IsBodyRegion(part.region), lineage.displayName);
                }
            }
        }

        [Test]
        public void EveryLineageHasABareBodyThatIsNotItsBasicOutfit()
        {
            foreach (var lineage in database.Lineages)
            {
                Assert.AreEqual(12, lineage.bareBody.Length, lineage.displayName);
                var regions = new HashSet<BodyRegion>();
                foreach (ushort id in lineage.bareBody)
                {
                    Assert.IsTrue(database.Parts.TryGet(id, out var part), lineage.displayName);
                    Assert.IsTrue(EquipSlots.IsBodyRegion(part.region), lineage.displayName);
                    Assert.IsTrue(regions.Add(part.region), $"{lineage.displayName}: two bare parts for {part.region}");
                    CollectionAssert.DoesNotContain(lineage.basicOutfit, id, $"{lineage.displayName}: taking level-one gear off must change the look");
                }
                Assert.AreSame(lineage.bareBody, lineage.BodyParts(false));
                Assert.AreSame(lineage.basicOutfit, lineage.BodyParts(true));
            }
        }

        [Test]
        public void EveryReadyMadeFaceIsAValidRecipe()
        {
            foreach (var lineage in database.Lineages)
                foreach (var bodyType in new[] { BodyType.Masculine, BodyType.Feminine })
                {
                    var setup = lineage.SetupFor(bodyType);
                    Assert.Greater(setup.presets.Count, 0, lineage.displayName);
                    foreach (var preset in setup.presets)
                    {
                        Assert.IsTrue(CharacterRecipeValidator.Validate(preset.recipe, database, out string reason),
                            $"{lineage.displayName} {bodyType} {preset.name}: {reason}");
                        Assert.AreEqual(bodyType, preset.recipe.bodyType);
                    }
                }
        }

        [Test]
        public void FacialHairIsOfferedToMasculineBodiesOnly()
        {
            var civilian = database.GetLineage(Civilian);
            Assert.IsTrue(civilian.masculine.TryGetRegion(BodyRegion.FacialHair, out _));
            Assert.IsFalse(civilian.feminine.TryGetRegion(BodyRegion.FacialHair, out _));
        }

        [Test]
        public void NoSetupOffersHelmetsMasksOrBodyParts()
        {
            foreach (var lineage in database.Lineages)
                foreach (var setup in new[] { lineage.masculine, lineage.feminine })
                    foreach (var options in setup.regions)
                        Assert.IsTrue(RecipeResolver.IsChoosable(options.region), $"{lineage.displayName}: {options.region}");
        }

        [Test]
        public void BodyPartsCannotBeChosenInARecipe()
        {
            var lineage = database.GetLineage(Civilian);
            var recipe = Preset(Civilian, BodyType.Masculine);
            recipe.parts = recipe.parts.Append(lineage.basicOutfit[0]).ToArray();
            Assert.IsFalse(CharacterRecipeValidator.Validate(recipe, database, out string reason));
            Assert.AreEqual(CharacterRecipeValidator.ReasonForbiddenRegion, reason);
        }

        [Test]
        public void LineageMustBelongToTheClaimedFaction()
        {
            var recipe = Preset(Civilian, BodyType.Masculine);
            recipe.faction = FactionId.Null;
            Assert.IsFalse(CharacterRecipeValidator.Validate(recipe, database, out string reason));
            Assert.AreEqual(CharacterRecipeValidator.ReasonWrongFaction, reason);
        }

        [Test]
        public void HeadOfAnotherLineageIsRejected()
        {
            var recipe = Preset(Civilian, BodyType.Masculine);
            Assert.IsTrue(database.GetLineage(Robot).masculine.TryGetRegion(BodyRegion.Head, out var robotHeads));
            for (int i = 0; i < recipe.parts.Length; i++)
                if (database.Parts.TryGet(recipe.parts[i], out var part) && part.region == BodyRegion.Head)
                    recipe.parts[i] = robotHeads.partIds[0];
            Assert.IsFalse(CharacterRecipeValidator.Validate(recipe, database, out string reason));
            Assert.AreEqual(CharacterRecipeValidator.ReasonPartNotAllowed, reason);
        }

        [Test]
        public void ResolverGivesHeadFromTheRecipeAndBodyFromTheLineage()
        {
            var lineage = database.GetLineage(Civilian);
            var recipe = Preset(Civilian, BodyType.Feminine);
            var resolved = new ResolvedAppearance();
            Assert.IsTrue(RecipeResolver.Resolve(recipe, database, resolved));
            Assert.AreEqual(0, resolved.missing);
            Assert.AreEqual(recipe.PartCount, resolved.fixedParts.Count);
            Assert.AreEqual(lineage.bareBody.Length, resolved.defaultParts.Count);
            Assert.IsTrue(resolved.defaultParts.All(p => EquipSlots.IsBodyRegion(p.region)));
            var bareTorso = resolved.defaultParts.First(p => p.region == BodyRegion.Torso).mesh;
            // A menu doll that knows no gear is resolved dressed: the basic outfit.
            Assert.IsTrue(RecipeResolver.Resolve(recipe, database, resolved, dressed: true));
            Assert.AreEqual(lineage.basicOutfit.Length, resolved.defaultParts.Count);
            Assert.AreNotSame(bareTorso, resolved.defaultParts.First(p => p.region == BodyRegion.Torso).mesh, "with no gear on, the body is bare");
            Assert.IsTrue(RecipeResolver.Resolve(recipe, database, resolved));
            Assert.IsTrue(resolved.wrap.IsValid, "a feminine human carries the chest wrap");
            Assert.AreEqual(100f, resolved.shape.masculineFeminine);
        }

        [Test]
        public void MasculineBodiesAndRobotsHaveNoWrap()
        {
            var resolved = new ResolvedAppearance();
            Assert.IsTrue(RecipeResolver.Resolve(Preset(Civilian, BodyType.Masculine), database, resolved));
            Assert.IsFalse(resolved.wrap.IsValid);
            Assert.IsTrue(RecipeResolver.Resolve(Preset(Robot, BodyType.Feminine), database, resolved));
            Assert.IsFalse(resolved.wrap.IsValid);
        }

        [Test]
        public void GearBelongsToItsFactionAndBody()
        {
            var soldierArmor = dictionary.Entries.First(e => e.pack == "SCFI_SOLD" && e.slot == EquipSlot.Armor);
            Assert.IsTrue(dictionary.CanUse(soldierArmor.itemId, FactionId.Hegemony, BodyFit.Humanoid, out _));
            Assert.IsFalse(dictionary.CanUse(soldierArmor.itemId, FactionId.Null, BodyFit.Robot, out string reason));
            Assert.AreEqual(GearDictionary.ReasonWrongFaction, reason);

            var robotArmor = dictionary.Entries.First(e => e.pack == "SCFI_ROBO" && e.slot == EquipSlot.Armor);
            Assert.IsTrue(dictionary.CanUse(robotArmor.itemId, FactionId.Null, BodyFit.Robot, out _));
            Assert.IsFalse(dictionary.CanUse(robotArmor.itemId, FactionId.Null, BodyFit.Skeleton, out reason));
            Assert.AreEqual(GearDictionary.ReasonWrongBody, reason);
        }

        [Test]
        public void ItemsOutsideTheDictionaryAreOpenToEveryone()
        {
            Assert.IsTrue(dictionary.CanUse(1001, FactionId.Null, BodyFit.Robot, out _)); // the katana
        }

        [Test]
        public void OnlySkeletonArmorCarriesAHelm()
        {
            foreach (var entry in dictionary.Entries)
            {
                bool hasHeadgear = entry.partIds.Any(id => database.Parts.TryGet(id, out var part)
                    && (part.region == BodyRegion.AttachmentHead || part.region == BodyRegion.AttachmentFace));
                if (entry.pack != "FANT_SKTN") Assert.IsFalse(hasHeadgear, entry.name);
            }
            // A skeleton armor that has a helm wears the helm of its own variant.
            var skeletonArmor = dictionary.Entries.First(e => e.pack == "FANT_SKTN" && e.slot == EquipSlot.Armor
                && e.partIds.Any(id => database.Parts.TryGet(id, out var part) && part.region == BodyRegion.AttachmentHead));
            Assert.IsTrue(skeletonArmor.partIds.Any(id => database.Parts.TryGet(id, out var part)
                && part.region == BodyRegion.AttachmentHead && part.variant == skeletonArmor.variant));
        }

        [Test]
        public void EveryPackHasGearInEveryRarity()
        {
            // Legendary is reserved: nothing of that rarity is in the game yet.
            foreach (var pack in dictionary.Entries.Select(e => e.pack).Where(p => !string.IsNullOrEmpty(p)).Distinct())
                for (var rarity = ItemRarity.Common; rarity <= ItemRarity.Epic; rarity++)
                    Assert.IsTrue(dictionary.Entries.Any(e => e.pack == pack && e.rarity == rarity), $"{pack} has no {rarity} gear");
        }

        [Test]
        public void CommonOutfitsAreTheBasicOutfit()
        {
            foreach (var lineage in database.Lineages)
            {
                foreach (var entry in dictionary.Entries.Where(e => e.pack == lineage.ownPack && e.rarity == ItemRarity.Common
                                                                    && (e.slot == EquipSlot.Armor || e.slot == EquipSlot.Gloves || e.slot == EquipSlot.Boots)))
                    Assert.AreEqual(1, entry.variant, $"{entry.name} (level {entry.itemLevel}) should look like {lineage.displayName}'s basic outfit");
            }
        }

        [Test]
        public void ADisciplineMustBelongToTheCharactersFaction()
        {
            var recipe = Preset(Civilian, BodyType.Masculine); // Hegemony
            recipe.weaponClass = WeaponClassId.Carbine;
            Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out _));
            recipe.weaponClass = WeaponClassId.None; // saved before classes existed
            Assert.IsTrue(CharacterRecipeValidator.Validate(recipe, database, out _));
            recipe.weaponClass = WeaponClassId.Warbow; // Covenant
            Assert.IsFalse(CharacterRecipeValidator.Validate(recipe, database, out string reason));
            Assert.AreEqual(WeaponClassRules.ReasonWrongDiscipline, reason);
        }

        [Test]
        public void TheDisciplineTravelsWithTheCharacter()
        {
            var recipe = Preset(Robot, BodyType.Masculine); // Null
            recipe.weaponClass = WeaponClassId.ArcCaster;
            var state = CharacterNetState.From("Unit", recipe);
            Assert.AreEqual(WeaponClassId.ArcCaster, state.weaponClass);
            Assert.AreEqual(WeaponClassId.ArcCaster, state.ToRecipe().weaponClass);

            var changed = recipe.Clone();
            changed.weaponClass = WeaponClassId.Harvester;
            Assert.IsFalse(recipe.Equals(changed), "Two characters that differ only by Discipline are different");
            Assert.IsFalse(state.Equals(CharacterNetState.From("Unit", changed)));
        }
    }
}
