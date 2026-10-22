using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    public static partial class GameFeelBuilder
    {
        private const string RunWindName = "PFX_Run_Wind";
        private const string RunWindMaterialPath = MaterialFolder + "/M_RunWind.mat";

        /// <summary>
        /// The wind lines of a run (owner, 22 Oct 2026: "lines or wind leaving behind the player to really sell the
        /// quickness, that increase as the run skill is increased in rank"): thin streaks of air born round the body
        /// that fly back and stay in the world, so they trail behind a runner, and a few long curls of wind off the
        /// shoulders. The prefab is authored at its fullest (Run rank 20); <see cref="KryticalNova.GameFeel.ActorVfx"/>
        /// thins it out for a lower rank. A full Build Game Feel builds it and puts it on the presenter too.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Build Run Wind")]
        public static void BuildRunWind()
        {
            BuildRunWindPrefab();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var so = new SerializedObject(root.GetComponent<KryticalNova.GameFeel.GameFeelPresenter>());
                so.FindProperty("runWindVfx").objectReferenceValue = Load(Vfx(RunWindName));
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Run wind built into {Vfx(RunWindName)} and put on {PrefabPath}.");
        }

        private static void BuildRunWindPrefab()
        {
            Material material = RunWindMaterial();
            var root = new GameObject(RunWindName);
            try
            {
                // Streaks: short, thin, stretched along the way they fly (back from the runner), left in the world.
                ParticleSystem streaks = WindSystem(root, "Streaks", material);
                var main = streaks.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.38f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.075f);
                main.startColor = new Color(0.88f, 0.96f, 1f, 0.75f);
                main.maxParticles = 160;
                var emission = streaks.emission;
                emission.rateOverTime = 90f;
                var shape = streaks.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(1.1f, 1.5f, 0.5f);
                var velocity = streaks.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.z = new ParticleSystem.MinMaxCurve(-9f, -14f);
                velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
                velocity.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.3f);
                FadeOut(streaks);
                var renderer = streaks.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = 3f;

                // Curls: a few ribbons of wind off the shoulders that bend as they trail.
                ParticleSystem curls = WindSystem(root, "Curls", material);
                main = curls.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
                main.startSize = 0.07f;
                main.startColor = new Color(0.9f, 0.97f, 1f, 0.6f);
                main.maxParticles = 24;
                emission = curls.emission;
                emission.rateOverTime = 7f;
                shape = curls.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.9f, 0.4f, 0.2f);
                shape.position = new Vector3(0f, 0.45f, 0f);
                velocity = curls.velocityOverLifetime;
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.z = new ParticleSystem.MinMaxCurve(-3f, -5f);
                var noise = curls.noise;
                noise.enabled = true;
                noise.strength = 1.6f;
                noise.frequency = 1.2f;
                noise.scrollSpeed = 0.8f;
                var trails = curls.trails;
                trails.enabled = true;
                trails.mode = ParticleSystemTrailMode.PerParticle;
                trails.lifetime = 0.6f;
                trails.minVertexDistance = 0.04f;
                trails.worldSpace = true;
                trails.dieWithParticles = false;
                trails.inheritParticleColor = true;
                trails.sizeAffectsWidth = true;
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
                trails.colorOverTrail = new ParticleSystem.MinMaxGradient(new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) },
                });
                FadeOut(curls);
                renderer = curls.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.None;
                renderer.trailMaterial = material;

                SavePrefab(root, Vfx(RunWindName));
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static ParticleSystem WindSystem(GameObject root, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = true;
            main.loop = true;
            main.duration = 1f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World; // left behind: the runner moves on, the air stays
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        private static void FadeOut(ParticleSystem system)
        {
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) },
            });
        }

        // Unlit and alpha-blended, reading the particles' colour: white air that shows on bright and dark ground alike.
        private static Material RunWindMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RunWindMaterialPath);
            if (material == null)
            {
                WeatherVfxBuilder.EnsureFolder(MaterialFolder);
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "M_RunWind" };
                AssetDatabase.CreateAsset(material, RunWindMaterialPath);
            }
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_BLENDMODE_ADD");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
