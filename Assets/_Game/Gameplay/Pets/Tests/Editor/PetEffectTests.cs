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

        /// <summary>
        /// The models a pet wears an effect on: its model at each growth stage the effect is worn at (a stage with
        /// no model of its own shows the pet's first one).
        /// </summary>
        private static IEnumerable<GameObject> ModelsFor(PetDefinition pet, PetEffect effect)
        {
            string name = pet.displayName.Replace(" ", string.Empty);
            var first = AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{name}/Pet_{name}_S1.prefab");
            var seen = new HashSet<GameObject>();
            for (int stage = Mathf.Max(0, effect.stages.x); stage <= Mathf.Min(PetRules.LastStage, effect.stages.y); stage++)
            {
                GameObject model = pet.Stage(stage).model != null ? pet.Stage(stage).model : first;
                if (model != null && seen.Add(model)) yield return model;
            }
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
                    int models = 0;
                    foreach (GameObject model in ModelsFor(pet, effect))
                    {
                        models++;
                        var bones = new HashSet<string>();
                        foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) bones.Add(bone.name);
                        foreach (string bone in effect.bones)
                            Assert.IsTrue(bones.Contains(bone), $"{pet.displayName}: {model.name} has no bone '{bone}' for {effect.prefab.name}");
                    }
                    Assert.Greater(models, 0, $"{pet.displayName}: no model of its own to wear {effect.prefab.name} on at stages {effect.stages.x} to {effect.stages.y}");
                }
            }
            Assert.GreaterOrEqual(seen, 2, "the Emberwing and the Drone Core each have an effect of their own");
        }

        // An effect that belongs to an animation waits for a state of that name: a name the Animator does not have
        // is an effect that never plays, and says nothing about it.
        [Test]
        public void An_effect_that_belongs_to_an_animation_names_one_the_pet_has_and_moments_inside_it()
        {
            int seen = 0;
            foreach (PetDefinition pet in All())
            {
                if (pet.effects == null) continue;
                foreach (PetEffect effect in pet.effects)
                {
                    if (string.IsNullOrEmpty(effect.animation)) continue;
                    seen++;
                    int models = 0;
                    foreach (GameObject model in ModelsFor(pet, effect))
                    {
                        models++;
                        var controller = model.GetComponentInChildren<Animator>(true).runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                        Assert.IsNotNull(controller, $"{pet.displayName}: {model.name} has no Animator controller");
                        var states = new HashSet<string>();
                        foreach (var layer in controller.layers)
                            foreach (var state in layer.stateMachine.states) states.Add(state.state.name);
                        Assert.IsTrue(states.Contains(effect.animation), $"{pet.displayName}: {model.name} has no animation '{effect.animation}' for {effect.prefab.name}");
                    }
                    Assert.Greater(models, 0, $"{pet.displayName}: no model of its own at stages {effect.stages.x} to {effect.stages.y}");
                    foreach (float moment in effect.at)
                        Assert.IsTrue(moment >= 0f && moment <= 1f, $"{pet.displayName}: {effect.prefab.name} plays at {moment}, outside {effect.animation}");
                    Assert.IsTrue(effect.during.x >= 0f && effect.during.y <= 1f && effect.during.x <= effect.during.y,
                        $"{pet.displayName}: {effect.prefab.name} plays from {effect.during.x} to {effect.during.y} of {effect.animation}");
                }
            }
            Assert.GreaterOrEqual(seen, 5, "the Tidecaller's hop, bounce, roll, nap and tide call each have an effect");
        }

        // The Tidecaller's four: dust on a hop and on a bounce, dust while it rolls, Zs while it naps, a wave on the tide call.
        [Test]
        public void The_Tidecaller_has_an_effect_for_each_of_its_animations_that_was_asked_for()
        {
            PetDefinition tidecaller = null;
            foreach (PetDefinition pet in All())
                if (pet.displayName == "Tidecaller") tidecaller = pet;
            Assert.IsNotNull(tidecaller);
            var wanted = new Dictionary<string, string>
            {
                { "HopSpin", "PFX_Pet_HopDust" }, { "VictoryBounce", "PFX_Pet_HopDust" }, { "Move", "PFX_Pet_RollDust" },
                { "WobbleNap", "PFX_Pet_Zzz" }, { "TideCall", "PFX_Pet_TideWave" },
            };
            foreach (var pair in wanted)
            {
                bool found = false;
                foreach (PetEffect effect in tidecaller.effects)
                    found |= effect.animation == pair.Key && effect.prefab != null && effect.prefab.name == pair.Value && effect.stages == new Vector2Int(0, 0);
                Assert.IsTrue(found, $"{pair.Value} on {pair.Key}, for the egg only");
            }
        }

        // From 25 % the Tidecaller is a swimmer, another creature at each stage, with effects that are not the egg's:
        // the same wave on its tide call, water streaming past while it swims, water dragged round its somersault,
        // and a bump where its ram lands. It also names the charge that turns it on its owner.
        [Test]
        public void The_grown_Tidecallers_are_four_models_with_effects_of_their_own()
        {
            PetDefinition tidecaller = null;
            foreach (PetDefinition pet in All())
                if (pet.displayName == "Tidecaller") tidecaller = pet;
            Assert.IsNotNull(tidecaller);
            var models = new HashSet<GameObject>();
            for (int stage = 1; stage <= PetRules.LastStage; stage++)
            {
                Assert.IsNotNull(tidecaller.Stage(stage).model, $"stage {stage} has a model of its own");
                models.Add(tidecaller.Stage(stage).model);
                Assert.Greater(tidecaller.Stage(stage).scale, tidecaller.Stage(stage - 1).scale, $"stage {stage} is larger than the one before");
            }
            Assert.AreEqual(4, models.Count, "a different creature at each stage");
            var wanted = new Dictionary<string, string>
            {
                { "TideCall", "PFX_Pet_TideWave" }, { "Move", "PFX_Pet_TideFlow" }, { "SwimFlip", "PFX_Pet_TideSwirl" }, { "Ram", "PFX_Pet_RamBump" },
            };
            foreach (var pair in wanted)
            {
                bool found = false;
                foreach (PetEffect effect in tidecaller.effects)
                    found |= effect.animation == pair.Key && effect.prefab != null && effect.prefab.name == pair.Value && effect.stages == new Vector2Int(1, PetRules.LastStage);
                Assert.IsTrue(found, $"{pair.Value} on {pair.Key}, for the swimmers only");
            }
            Assert.AreEqual("Ram", tidecaller.chargeAnimation);
            foreach (GameObject model in models)
            {
                bool hasBone = false;
                foreach (Transform bone in model.GetComponentsInChildren<Transform>(true)) hasBone |= bone.name == tidecaller.chargeBone;
                Assert.IsTrue(hasBone, $"{model.name} has the bone its charge is read from ('{tidecaller.chargeBone}')");
            }
        }

        // A trail is left where it was shed: carried along in the pet's own space it would be a cloud round the pet.
        // (An effect that belongs to an animation is another matter: the nap's bubble and the wave travel with the pet.)
        [Test]
        public void The_effects_a_flyer_wears_all_the_time_stay_behind_in_the_world()
        {
            foreach (PetDefinition pet in All())
            {
                if (pet.effects == null || pet.bobs == false) continue;
                foreach (PetEffect effect in pet.effects)
                {
                    if (!string.IsNullOrEmpty(effect.animation)) continue;
                    foreach (ParticleSystem particles in effect.prefab.GetComponentsInChildren<ParticleSystem>(true))
                        Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace, $"{pet.displayName}: {effect.prefab.name}");
                }
            }
        }

        // The wave is a flip book: the swell and the shapes of its break, one after the other with no gap, and then
        // the water that the break throws up. A shape left out, or one that outstays the next, shows as a wave that
        // blinks or doubles.
        [Test]
        public void The_wave_is_a_run_of_shapes_with_no_gap_and_a_break_after_them()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/VFX/GameFeel/PFX_Pet_TideWave.prefab");
            Assert.IsNotNull(prefab);
            var shapes = new List<ParticleSystem>();
            var thrown = new List<ParticleSystem>();
            foreach (ParticleSystem particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles.name.StartsWith("Wave")) shapes.Add(particles);
                else if (particles.name.StartsWith("Break")) thrown.Add(particles);
            }
            Assert.GreaterOrEqual(shapes.Count, 16, "the swell and the shapes of the break");
            Assert.GreaterOrEqual(thrown.Count, 5, "foam along the floor, foam thrown up, shards, drops and mist");
            shapes.Sort((a, b) => a.main.startDelay.constant.CompareTo(b.main.startDelay.constant));
            var meshes = new HashSet<Mesh>();
            float ends = 0f;
            foreach (ParticleSystem shape in shapes)
            {
                var main = shape.main;
                Assert.AreEqual(ends, main.startDelay.constant, 0.002f, $"{shape.name} starts when the one before it ends");
                ends = main.startDelay.constant + main.startLifetime.constant;
                Assert.AreEqual(1, main.maxParticles, shape.name);
                Mesh mesh = shape.GetComponent<ParticleSystemRenderer>().mesh;
                Assert.IsNotNull(mesh, shape.name);
                meshes.Add(mesh);
            }
            Assert.GreaterOrEqual(meshes.Count, shapes.Count - 1, "a shape of its own for every moment (the swell shares the first)");
            // What the break throws up starts as the lip comes down, not before.
            float lipDown = ends - 0.2f;
            foreach (ParticleSystem part in thrown)
                Assert.GreaterOrEqual(part.main.startDelay.constant, lipDown - 0.05f, part.name);
        }

        // Dust is raised on the floor and left there; it must not be carried along by the pet that raised it.
        [Test]
        public void Dust_is_left_where_it_was_raised()
        {
            foreach (string path in new[] { "Assets/_Game/VFX/GameFeel/PFX_Pet_HopDust.prefab", "Assets/_Game/VFX/GameFeel/PFX_Pet_RollDust.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                foreach (ParticleSystem particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
                    Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace, path);
            }
        }
    }
}
