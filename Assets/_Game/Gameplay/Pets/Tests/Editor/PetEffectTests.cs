using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Pets.Tests
{
    /// <summary>The effects that are a pet's own mark: each names an effect that exists and bones its model has.</summary>
    public sealed class PetEffectTests
    {
        private const string Definitions = "Assets/_Game/Gameplay/Pets/Data/Assets";
        private const string Models = "Assets/_Game/Characters/Pets";

        private static IEnumerable<PetDefinition> All()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PetDefinition", new[] { Definitions }))
                yield return AssetDatabase.LoadAssetAtPath<PetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
        }

        [Test]
        public void Every_effect_of_a_pet_is_there_and_is_worn_on_bones_its_model_has()
        {
            int seen = 0;
            foreach (PetDefinition pet in All())
            {
                if (pet.effects == null) continue;
                foreach (PetEffect effect in pet.effects)
                {
                    seen++;
                    Assert.IsNotNull(effect.prefab, $"{pet.displayName}: an effect with no prefab");
                    Assert.IsNotNull(effect.prefab.GetComponentInChildren<ParticleSystem>(true), $"{pet.displayName}: {effect.prefab.name} has no particles");
                    string name = pet.displayName.Replace(" ", string.Empty);
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{name}/Pet_{name}_S1.prefab");
                    Assert.IsNotNull(model, $"{pet.displayName}: no model of its own to wear {effect.prefab.name} on");
                    var bones = new HashSet<string>();
                    foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) bones.Add(bone.name);
                    foreach (string bone in effect.bones)
                        Assert.IsTrue(bones.Contains(bone), $"{pet.displayName}: its model has no bone '{bone}' for {effect.prefab.name}");
                }
            }
            Assert.GreaterOrEqual(seen, 2, "the Emberwing and the Drone Core each have an effect of their own");
        }

        // A trail is left where it was shed: carried along in the pet's own space it would be a cloud round the pet.
        [Test]
        public void The_effects_of_the_two_flyers_stay_behind_in_the_world()
        {
            foreach (PetDefinition pet in All())
            {
                if (pet.effects == null || pet.bobs == false) continue;
                foreach (PetEffect effect in pet.effects)
                    foreach (ParticleSystem particles in effect.prefab.GetComponentsInChildren<ParticleSystem>(true))
                        Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace, $"{pet.displayName}: {effect.prefab.name}");
            }
        }
    }
}
