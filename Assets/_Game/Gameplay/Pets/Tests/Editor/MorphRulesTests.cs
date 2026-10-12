using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Pets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>Morphweave: which look a pet shows, and when a Morph may go over the pet that is out.</summary>
    public sealed class MorphRulesTests
    {
        private const string CatalogPath = "Assets/_Game/Gameplay/Pets/Data/Assets/PetCatalog.asset";
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private PetDefinition Pet(ushort id, string name)
        {
            var pet = ScriptableObject.CreateInstance<PetDefinition>();
            pet.itemId = id;
            pet.displayName = name;
            made.Add(pet);
            return pet;
        }

        [Test]
        public void AMorphIsOnePetAtOneStage()
        {
            uint key = MorphRules.Key(8003, 4);
            Assert.AreNotEqual(0u, key);
            Assert.AreEqual(8003, MorphRules.PetOf(key));
            Assert.AreEqual(4, MorphRules.StageOf(key));
            Assert.AreNotEqual(key, MorphRules.Key(8003, 3), "each stage is a look of its own");
            Assert.AreNotEqual(key, MorphRules.Key(8004, 4));
            Assert.AreEqual(0u, MorphRules.Key(0, 2), "no pet");
            Assert.AreEqual(0u, MorphRules.Key(8003, PetRules.StageCount), "a pet has five stages");
            Assert.AreEqual(0u, MorphRules.Key(8003, -1));
        }

        [Test]
        public void AMorphGoesOverAnyPet_ButNeverAboveTheStageThatPetHasReached()
        {
            PetDefinition worn = Pet(8003, "Tidecaller"), fox = Pet(8004, "Kitsune Spirit");

            Assert.IsNull(MorphRules.ApplyBlockedReason(worn, 4, fox, 4, owned: true), "the 100 % look over a 100 % pet");
            Assert.IsNull(MorphRules.ApplyBlockedReason(worn, 4, fox, 1, owned: true), "an earlier look over a grown pet");
            Assert.IsNull(MorphRules.ApplyBlockedReason(worn, 2, worn, 0, owned: true), "its own egg over itself");
            Assert.AreEqual(MorphRules.ReasonNotGrown(4), MorphRules.ApplyBlockedReason(worn, 3, fox, 4, owned: true));
            StringAssert.Contains("100%", MorphRules.ReasonNotGrown(4));
            StringAssert.Contains("25%", MorphRules.ReasonNotGrown(1));
        }

        [Test]
        public void AMorphMustBeOwned_AndNeedsAPetOut()
        {
            PetDefinition worn = Pet(8003, "Tidecaller"), fox = Pet(8004, "Kitsune Spirit");
            Assert.AreEqual(MorphRules.ReasonNotOwned, MorphRules.ApplyBlockedReason(worn, 4, fox, 4, owned: false));
            Assert.AreEqual(MorphRules.ReasonNoPet, MorphRules.ApplyBlockedReason(null, 0, fox, 0, owned: true));
            Assert.AreEqual(MorphRules.ReasonNotAMorph, MorphRules.ApplyBlockedReason(worn, 4, null, 0, owned: true));
            Assert.AreEqual(MorphRules.ReasonNotAMorph, MorphRules.ApplyBlockedReason(worn, 4, fox, 9, owned: true));
        }

        [Test]
        public void ThePetShowsTheMorph_UntilAPetNotGrownThatFarIsOut()
        {
            PetDefinition worn = Pet(8003, "Tidecaller"), fox = Pet(8004, "Kitsune Spirit");

            Assert.IsTrue(MorphRules.Shown(worn, 4, fox, 4, out PetDefinition seen, out int stage));
            Assert.AreSame(fox, seen);
            Assert.AreEqual(4, stage);

            // The Morph stays on the slot: a younger pet shows itself, and the Morph is back with the grown one.
            Assert.IsFalse(MorphRules.Shown(worn, 0, fox, 4, out seen, out stage));
            Assert.AreSame(worn, seen);
            Assert.AreEqual(0, stage);

            Assert.IsFalse(MorphRules.Shown(worn, 3, null, 0, out seen, out stage), "no Morph");
            Assert.AreSame(worn, seen);
            Assert.AreEqual(3, stage);
            Assert.IsFalse(MorphRules.Shown(worn, 3, worn, 3, out _, out _), "its own look is no Morph");
            Assert.IsFalse(MorphRules.Shown(null, 0, fox, 0, out seen, out _), "no pet out");
            Assert.IsNull(seen);
        }

        [Test]
        public void AMorphReadsAsThePetAndItsStage()
        {
            PetDefinition fox = Pet(8004, "Kitsune Spirit");
            fox.stages[4].name = "Nine Tails";
            Assert.AreEqual("Kitsune Spirit, Nine Tails", MorphRules.NameOf(fox, 4));
            Assert.AreEqual("Kitsune Spirit, Stage 2", MorphRules.NameOf(fox, 1), "a stage with no name of its own");
            Assert.AreEqual(string.Empty, MorphRules.NameOf(null, 0));
        }

        [Test]
        public void TheLooksEveryoneSees_CarryTheMorph()
        {
            var looks = default(EquipmentLooks);
            looks.SetMorph(8004, 3);
            Assert.AreEqual(8004, looks.pet);
            Assert.AreEqual(3, looks.petStage);
            Assert.AreNotEqual(default(EquipmentLooks), looks);

            var other = default(EquipmentLooks);
            other.SetMorph(8004, 2);
            Assert.AreNotEqual(looks, other, "another stage is another look");
            Assert.AreEqual(0, looks.GetWeave(EquipSlot.Pet), "a Morph is not a gear Weave");

            looks.SetMorph(0, 3);
            Assert.AreEqual(default(EquipmentLooks), looks, "taking it off leaves nothing behind");
        }

        [Test]
        public void EveryPetInTheGame_HasAMorphAtEachOfItsFiveStages()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<PetCatalog>(CatalogPath);
            Assert.IsNotNull(catalog, CatalogPath);
            Assert.Greater(catalog.Pets.Count, 0);
            var keys = new HashSet<uint>();
            foreach (PetDefinition pet in catalog.Pets)
            {
                Assert.IsNotNull(pet);
                for (int stage = 0; stage < PetRules.StageCount; stage++)
                {
                    uint key = MorphRules.Key(pet.itemId, stage);
                    Assert.AreNotEqual(0u, key, $"{pet.displayName} stage {stage}");
                    Assert.IsTrue(keys.Add(key), $"{pet.displayName} stage {stage} shares a key");
                    Assert.AreSame(pet, catalog.Get(MorphRules.PetOf(key)));
                    StringAssert.StartsWith(pet.displayName + ", ", MorphRules.NameOf(pet, stage));
                    Assert.Greater(pet.Stage(stage).scale, 0f);
                }
            }
        }
    }
}
