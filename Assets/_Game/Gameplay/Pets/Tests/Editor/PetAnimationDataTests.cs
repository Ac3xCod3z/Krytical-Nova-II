using KryticalNova.Pets;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Wave 3, 12 Nov 2026: gm.pet.anims / gm.pet.anim on a server with no model of the pet (the entity netcode's server body) read the
    /// animation names from the pet data, stage by stage, as the pet builder wrote them from each stage's model.
    /// </summary>
    public sealed class PetAnimationDataTests
    {
        const string Kitsune = "Assets/_Game/Gameplay/Pets/Data/Assets/PetDef_KitsuneSpirit.asset";

        [Test]
        public void TheKitsuneData_HoldsEachStagesAnimations()
        {
            var pet = AssetDatabase.LoadAssetAtPath<PetDefinition>(Kitsune);
            Assert.IsNotNull(pet);
            CollectionAssert.AreEqual(new[] { "Blink", "LookAround", "PawLift", "SpinChase", "TailCurl" }, PlayerPet.DataAnimationNames(pet, 0));
            CollectionAssert.Contains(PlayerPet.DataAnimationNames(pet, 4), "NinefoldBlaze", "the last form's own");
            CollectionAssert.DoesNotContain(PlayerPet.DataAnimationNames(pet, 0), "NinefoldBlaze", "a form plays only its model's");
        }

        [Test]
        public void NoPetOrNoList_IsEmpty()
        {
            Assert.AreEqual(0, PlayerPet.DataAnimationNames(null, 0).Count);
            var bare = ScriptableObject.CreateInstance<PetDefinition>();
            Assert.AreEqual(0, PlayerPet.DataAnimationNames(bare, 2).Count);
            Object.DestroyImmediate(bare);
        }

        [Test]
        public void ANameMatches_WhateverItsCapitals()
        {
            var names = new[] { "Blink", "SpinChase" };
            Assert.AreEqual("SpinChase", PlayerPet.FindAnimation(names, "spinchase"));
            Assert.IsNull(PlayerPet.FindAnimation(names, "Pounce"));
            Assert.IsNull(PlayerPet.FindAnimation(null, "Blink"));
        }
    }
}
