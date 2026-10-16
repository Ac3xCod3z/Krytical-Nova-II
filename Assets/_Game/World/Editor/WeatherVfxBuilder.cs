using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds performance-tuned, URP-safe weather VFX prefabs from the Synty packs into
    /// Assets/_Game/VFX/Environment/Weather/. Synty folders are never modified: prefabs are unpacked
    /// copies, and legacy Built-in particle materials are replaced by URP Particles/Unlit copies.
    /// Every copy: no collision, no lights, no shadows, capped particle counts.
    /// </summary>
    public static class WeatherVfxBuilder
    {
        public const string OutputFolder = "Assets/_Game/VFX/Environment/Weather";
        private const string MaterialFolder = "Assets/_Game/VFX/Materials";
        private const string Generic = "Assets/Synty/PolygonGeneric/Prefabs/FX/";
        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string CircleTexture = "Assets/Synty/PolygonGeneric/Textures/FX/Generic_Circle_01.png";
        private const string SoftCircleTexture = "Assets/Synty/PolygonGeneric/Textures/FX/Generic_Circle_Soft_01.png";

        public const string RainPath = OutputFolder + "/PFX_Rain.prefab";
        public const string RainSplashPath = OutputFolder + "/PFX_RainSplash.prefab";
        public const string SnowPath = OutputFolder + "/PFX_Snow.prefab";
        public const string BlizzardPath = OutputFolder + "/PFX_Blizzard.prefab";
        public const string MistPath = OutputFolder + "/PFX_Mist.prefab";
        public const string LightningPath = OutputFolder + "/PFX_LightningStrike.prefab";

        public const string WindGustsPath = OutputFolder + "/PFX_WindGusts.prefab";

        private static readonly Dictionary<Material, Material> ConvertedMaterials = new Dictionary<Material, Material>();

        /// <summary>
        /// Wind gust streaks (Polygon Particle FX "FX_Wind_Hard_01" trails), driven at runtime by PrecipitationController:
        /// rotated to the storm's wind heading, emission and speed scaled by wind strength + gusts.
        /// Separate from <see cref="BuildAll"/> so it can be (re)built without touching the existing weather prefabs.
        /// </summary>
        [MenuItem("Krytical Nova/World/Build Wind Gust VFX Prefab")]
        public static void BuildWindGusts()
        {
            EnsureFolder(OutputFolder);
            EnsureFolder(MaterialFolder);
            Build(ParticleFx + "FX_Wind_Hard_01.prefab", WindGustsPath, root =>
            {
                root.transform.localRotation = Quaternion.identity;
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.simulationSpace = ParticleSystemSimulationSpace.World; // streaks don't drag along with the camera
                main.maxParticles = 300;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 14f); // runtime scales this to the wind speed
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f); // trail width: thin wisps, not ribbons
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 15f;
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(30f, 3.5f, 2f); // a wide curtain upwind of the camera, blowing along +Z
                shape.position = new Vector3(0f, 6.25f, -10f); // 4.5-8 m up: well above head height
                shape.rotation = Vector3.zero;
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.35f; // a gentle curl, not ruler-straight
                noise.frequency = 0.35f;
                var trails = ps.trails;
                trails.enabled = true;
                trails.inheritParticleColor = true;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.5f);
                Fade(ps, new Color(0.72f, 0.76f, 0.82f), 0.15f); // muted grey-blue, 15% opacity
                // The particle itself uses Synty's empty material (converted to URP); only the trail is visible.
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                // Alpha-blend instead of Synty's additive: additive brightens everything behind it and read as glowing.
                var trailMat = ToUrp(renderer.trailMaterial);
                if (trailMat != null)
                {
                    SetupUrpParticle(trailMat, trailMat.GetTexture("_BaseMap"), Color.white, false);
                    renderer.trailMaterial = trailMat;
                }
            });
            AssetDatabase.SaveAssets();
            Debug.Log($"[WeatherVfxBuilder] Built {WindGustsPath}.");
        }

        [MenuItem("Krytical Nova/World/Build Weather VFX Prefabs")]
        public static void BuildAll()
        {
            ConvertedMaterials.Clear();
            EnsureFolder(OutputFolder);
            EnsureFolder(MaterialFolder);
            // Plain URP Particles/Unlit (alpha blend, no soft-particle / camera-fade keywords):
            // the Synty particle shader's fade + clip keywords swallow thin rain streaks.
            var precipitationMat = ParticleMaterial("M_FX_Precipitation_URP", CircleTexture, false);
            var softMat = ParticleMaterial("M_FX_SoftCircle_URP", SoftCircleTexture, false);

            Build(Generic + "FX_Rain_01.prefab", RainPath, root =>
            {
                DestroyChild(root, "FX_Rain_Splash_01");
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.maxParticles = 3000;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 1500f;
                var shape = ps.shape;
                shape.radius = 14f;
                shape.position = Vector3.zero; // Synty bakes a 15 m offset; the controller owns the height
                var sub = ps.subEmitters;
                sub.enabled = false;
                // Thin, short streaks at a believable fall speed (Synty's gravity 5 reaches ~60 m/s = metres-long lines).
                // PrecipitationController applies the same values at runtime (and tints them by scene light).
                main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.02f);
                main.gravityModifierMultiplier = 2.2f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 2.4f); // safety cap; collision ends drops at the ground
                var sizeOverLifetime = ps.sizeOverLifetime;
                sizeOverLifetime.enabled = false;
                Fade(ps, new Color(0.82f, 0.88f, 1f), 0.55f);
                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.velocityScale = 0.03f;
                renderer.lengthScale = 1f;
                renderer.sharedMaterial = precipitationMat;
            });

            Build(Generic + "FX_Rain_01.prefab", RainSplashPath, root =>
            {
                // Promote the collision-splash sub-emitter to a standalone ground emitter (no collision).
                var splash = root.transform.Find("FX_Rain_Splash_01");
                splash.SetParent(null, false);
                Object.DestroyImmediate(root);
                return splash.gameObject;
            }, root =>
            {
                root.name = "PFX_RainSplash";
                root.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                root.transform.localScale = Vector3.one;
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.maxParticles = 150;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTimeMultiplier = 110f;
                emission.SetBursts(Array.Empty<ParticleSystem.Burst>());
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 10f;
                shape.radiusThickness = 1f;
                shape.scale = Vector3.one;
                shape.rotation = Vector3.zero;
                // Tiny droplets with a short stretch (the Synty sub-emitter's 5× stretch read as big ovals).
                main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.012f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                root.GetComponent<ParticleSystemRenderer>().lengthScale = 2.5f;
                Fade(ps, new Color(0.85f, 0.9f, 1f), 0.6f);
                root.GetComponent<ParticleSystemRenderer>().sharedMaterial = precipitationMat;
            });

            Build(Generic + "FX_Snow_01.prefab", SnowPath, root =>
            {
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.maxParticles = 2500;
                main.startLifetime = new ParticleSystem.MinMaxCurve(8f, 10f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.18f);
                main.gravityModifierMultiplier = 0.06f;
                var snowSize = ps.sizeOverLifetime;
                snowSize.enabled = false;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 260f;
                var shape = ps.shape;
                shape.scale = new Vector3(18f, 18f, 6f);
                shape.position = Vector3.zero;
                Fade(ps, Color.white, 0.9f);
                root.GetComponent<ParticleSystemRenderer>().sharedMaterial = softMat;
            });

            Build(ParticleFx + "FX_Blizzard_Snow_01.prefab", BlizzardPath, root =>
            {
                DestroyChild(root, "FX_Blizzard_Cloud_01"); // huge mesh particles = heavy overdraw; fog does the haze
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.maxParticles = 3000;
                main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.13f);
                var blizzardSize = ps.sizeOverLifetime;
                blizzardSize.enabled = false;
                var shape = ps.shape;
                shape.position = Vector3.zero;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 750f;
                Fade(ps, Color.white, 0.8f);
                root.GetComponent<ParticleSystemRenderer>().sharedMaterial = softMat;
            });

            Build(Generic + "FX_Fog_01.prefab", MistPath, root =>
            {
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.maxParticles = 24;
                main.startSize = new ParticleSystem.MinMaxCurve(14f, 28f);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 1.5f;
            });

            Build(ParticleFx + "FX_LightningStrike_01.prefab", LightningPath, root =>
            {
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = false;
            });

            BuildWindGusts();
            AssetDatabase.SaveAssets();
            Debug.Log($"[WeatherVfxBuilder] Built weather VFX into {OutputFolder}.");
        }

        // ---------------------------------------------------------------- build helpers

        public static void Build(string source, string destination, Action<GameObject> modify)
        {
            Build(source, destination, null, modify);
        }

        public static void Build(string source, string destination, Func<GameObject, GameObject> extract, Action<GameObject> modify)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (src == null)
            {
                Debug.LogError($"[WeatherVfxBuilder] Missing source prefab {source}");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (extract != null) instance = extract(instance);
            instance.name = Path.GetFileNameWithoutExtension(destination);
            instance.transform.position = Vector3.zero;

            modify(instance);
            MakeCheap(instance);

            PrefabUtility.SaveAsPrefabAsset(instance, destination);
            Object.DestroyImmediate(instance);
        }

        /// <summary>Applies the performance rules to every particle system in the hierarchy.</summary>
        private static void MakeCheap(GameObject root)
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.gameObject == root || light.GetComponents<Component>().Length > 2)
                    Object.DestroyImmediate(light);
                else
                    Object.DestroyImmediate(light.gameObject);
            }

            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var collision = ps.collision;
                collision.enabled = false;
                var lights = ps.lights;
                lights.enabled = false;
                var noise = ps.noise;
                if (noise.enabled) noise.quality = ParticleSystemNoiseQuality.Low;

                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null) continue;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.sharedMaterial = ToUrp(renderer.sharedMaterial);
                if (renderer.trailMaterial != null) renderer.trailMaterial = ToUrp(renderer.trailMaterial);
            }
        }

        public static Material ParticleMaterial(string name, string texturePath, bool additive)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            SetupUrpParticle(mat, AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath), Color.white, additive);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        internal static void SetupUrpParticle(Material mat, Texture texture, Color tint, bool additive)
        {
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_Surface", 1f);                  // transparent
            mat.SetFloat("_Blend", additive ? 2f : 0f);    // additive : alpha
            mat.SetFloat("_ColorMode", 0f);                // multiply vertex colour
            // Both faces, like the legacy particle shaders these replace: flat rings and swirls laid on the ground
            // face down, and would be culled from above.
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.SetFloat("_SoftParticlesEnabled", 0f);
            mat.SetFloat("_CameraFadingEnabled", 0f);
            BaseShaderGUI.SetMaterialKeywords(mat, ParticleGUI.SetMaterialKeywords);
        }

        /// <summary>A flat, opaque URP particle material: it writes depth, so the comic outline pass inks the particles.</summary>
        public static Material OpaqueParticleMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", null);
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 0f);                  // opaque
            mat.SetFloat("_ColorMode", 0f);                // multiply vertex colour
            mat.SetFloat("_SoftParticlesEnabled", 0f);
            mat.SetFloat("_CameraFadingEnabled", 0f);
            BaseShaderGUI.SetMaterialKeywords(mat, ParticleGUI.SetMaterialKeywords);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Legacy Built-in particle materials render magenta in URP; make a URP Particles/Unlit copy.</summary>
        public static Material ToUrp(Material source)
        {
            if (source == null || source.shader == null) return source;
            if (!source.shader.name.StartsWith("Legacy Shaders/", StringComparison.Ordinal)) return source;
            if (ConvertedMaterials.TryGetValue(source, out var cached)) return cached;

            string path = $"{MaterialFolder}/{source.name}_URP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));

            bool additive = source.shader.name.Contains("Additive");
            Color tint = source.HasProperty("_TintColor") ? source.GetColor("_TintColor") * 2f
                : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            tint.a = Mathf.Clamp01(tint.a);

            SetupUrpParticle(mat, source.mainTexture, tint, additive);

            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            ConvertedMaterials[source] = mat;
            return mat;
        }

        /// <summary>Fade in → hold → fade out over the particle lifetime, in a single colour.</summary>
        private static void Fade(ParticleSystem ps, Color color, float alpha)
        {
            var main = ps.main;
            main.startColor = Color.white;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(alpha, 0.15f), new GradientAlphaKey(alpha, 0.85f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void DestroyChild(GameObject root, string name)
        {
            var child = root.transform.Find(name);
            if (child != null) Object.DestroyImmediate(child.gameObject);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
