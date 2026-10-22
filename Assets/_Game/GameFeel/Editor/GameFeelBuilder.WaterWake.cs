using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    public static partial class GameFeelBuilder
    {
        private const string WaterWakeName = "PFX_Water_Wake";
        private const string WaterStepName = "PFX_Water_Step";
        private const string WaterStepPlayer = "Water Step";

        /// <summary>
        /// The wake of Waterstride (owner, 22 Oct 2026): rings that spread on the water where the feet strike it, more
        /// the faster the runner goes and a slow few when standing, and a spray of drops kicked up behind. Left in the
        /// world, so they trail behind a runner. A full Build Game Feel builds it and puts it on the presenter too.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Build Water Wake")]
        public static void BuildWaterWake()
        {
            BuildWaterWakePrefab();
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var so = new SerializedObject(root.GetComponent<KryticalNova.GameFeel.GameFeelPresenter>());
                so.FindProperty("waterWakeVfx").objectReferenceValue = Load(Vfx(WaterWakeName));
                // Its footsteps, built again in place: a splash and a ripple for everyone, a splash sound for the player.
                foreach (Transform old in root.GetComponentsInChildren<Transform>(true))
                    if (old != null && old.name == WaterStepPlayer) Object.DestroyImmediate(old.gameObject);
                Transform world = root.transform.Find("World");
                AddWaterStepFeel(root, world != null ? world.gameObject : root, so);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Water wake built into {Vfx(WaterWakeName)} and put on {PrefabPath}.");
        }

        private static void BuildWaterWakePrefab()
        {
            var root = new GameObject(WaterWakeName);
            try
            {
                // Rings: flat on the water, growing and fading.
                ParticleSystem rings = WakeSystem(root, "Rings", WeatherVfxBuilder.ParticleMaterial("M_WaterWake_Ring", RingTexture, additive: false));
                var main = rings.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.3f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
                main.startColor = new Color(0.85f, 0.97f, 1f, 0.7f);
                main.maxParticles = 60;
                var emission = rings.emission;
                emission.rateOverTime = 1.5f;      // standing still: a slow ring now and then
                emission.rateOverDistance = 0.9f;  // running: one every metre or so
                var shape = rings.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.25f;
                shape.rotation = new Vector3(90f, 0f, 0f);
                var size = rings.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f, 0f, 2f), new Keyframe(1f, 3.2f)));
                FadeOutWake(rings, 0.08f);
                var renderer = rings.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                rings.transform.localPosition = new Vector3(0f, 0.03f, 0f);

                // Spray: drops thrown up and back from the feet, falling again.
                ParticleSystem spray = WakeSystem(root, "Spray", WeatherVfxBuilder.ParticleMaterial("M_WaterWake_Drop", DiscTexture, additive: false));
                main = spray.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
                main.startColor = new Color(0.92f, 0.98f, 1f, 0.85f);
                main.gravityModifier = 1.2f;
                main.maxParticles = 160;
                emission = spray.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 9f; // only when running
                shape = spray.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 28f;
                shape.radius = 0.15f;
                shape.rotation = new Vector3(-70f, 180f, 0f); // up and back from the runner
                FadeOutWake(spray, 0.02f);
                spray.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
                spray.transform.localPosition = new Vector3(0f, 0.05f, 0f);

                SavePrefab(root, Vfx(WaterWakeName));
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// Each footstep on the water (owner, 22 Oct 2026: "each footstep should have slight splash vfx and needs a
        /// soundfx ... and a little water ripple where the foot steps"): the splash and its ripple on every client, at
        /// the foot (ActorVfx tells when it strikes); the splash sound only on the player's own game, like the landing.
        /// </summary>
        private static void AddWaterStepFeel(GameObject root, GameObject world, SerializedObject so)
        {
            BuildWaterStepPrefab();
            so.FindProperty("waterStepFeel").objectReferenceValue = Player(root, WaterStepPlayer, p => Sound(p, "Water Step"));
            so.FindProperty("waterStepWorldFeel").objectReferenceValue = World(world, WaterStepPlayer, new Fx(WaterStepName, warm: 6));
        }

        private static void BuildWaterStepPrefab()
        {
            var root = new GameObject(WaterStepName);
            try
            {
                // A little ring where the foot went in.
                ParticleSystem ring = WakeSystem(root, "Ripple", WeatherVfxBuilder.ParticleMaterial("M_WaterWake_Ring", RingTexture, additive: false));
                OneShot(ring, 1);
                var main = ring.main;
                main.startLifetime = 0.8f;
                main.startSize = 0.35f;
                main.startColor = new Color(0.88f, 0.98f, 1f, 0.75f);
                var size = ring.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f, 0f, 2.2f), new Keyframe(1f, 3.4f)));
                FadeOutWake(ring, 0.06f);
                ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                ring.transform.localPosition = new Vector3(0f, 0.03f, 0f);

                // A slight splash: a few drops up round the foot, falling back.
                ParticleSystem splash = WakeSystem(root, "Splash", WeatherVfxBuilder.ParticleMaterial("M_WaterWake_Drop", DiscTexture, additive: false));
                OneShot(splash, 9);
                main = splash.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                main.startColor = new Color(0.92f, 0.98f, 1f, 0.9f);
                main.gravityModifier = 1.4f;
                var shape = splash.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 25f;
                shape.radius = 0.08f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // straight up round the foot
                FadeOutWake(splash, 0.02f);
                splash.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.Billboard;
                splash.transform.localPosition = new Vector3(0f, 0.04f, 0f);

                SavePrefab(root, Vfx(WaterStepName));
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>One burst and done: a footstep's effect, played by the world player and handed back to the pool.</summary>
        private static void OneShot(ParticleSystem system, int count)
        {
            var main = system.main;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = Mathf.Max(count, 1);
            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        }

        private static ParticleSystem WakeSystem(GameObject root, string name, Material material)
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
            main.simulationSpace = ParticleSystemSimulationSpace.World; // left behind on the water
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            return system;
        }

        private static void FadeOutWake(ParticleSystem system, float fadeIn)
        {
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            colour.color = new ParticleSystem.MinMaxGradient(new Gradient
            {
                colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                alphaKeys = new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) },
            });
        }
    }
}
