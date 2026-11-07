using System.IO;
using KryticalNova.Mounts;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the shared mount appear/vanish effect's assets (owner, 6 Nov 2026: Torrent's timing, our own look).
    /// It makes the material of the shared <c>MountMaterialise</c> graph and, for the Brambleback, its leaf-vein noise,
    /// a leaf texture, the appear and vanish particle prefabs and its <see cref="MountMaterialiseSettings"/>. The
    /// settings are given to the Brambleback's definition. Re-runnable: everything here is rewritten.
    /// Menu Krytical Nova ▸ Mounts ▸ Build Materialise Assets, or -executeMethod
    /// KryticalNova.EditorTools.MountMaterialiseBuilder.Build. <see cref="RenderPreview"/> renders the Brambleback
    /// at _Spawn 0, 0.25, 0.5, 0.75 and 1 into one picture for review.
    /// </summary>
    public static class MountMaterialiseBuilder
    {
        public const string ShaderFolder = "Assets/_Game/Gameplay/Mounts/Shaders";
        public const string GraphPath = ShaderFolder + "/MountMaterialise.shadergraph";
        public const string MaterialPath = ShaderFolder + "/MountMaterialise.mat";
        public const string LookFolder = MountAssetBuilder.MountsRoot + "/Materialise";
        private const string BrambleKey = "Brambleback";
        private static string BrambleDefinitionPath => MountAssetBuilder.MountsRoot + "/MountDef_Brambleback.asset";
        private static string LookPath(string key) => $"{LookFolder}/MountMaterialise_{key}.asset";
        private const string PreviewDrive = "G:/My Drive/KryticalNova/AI Outputs/MountMaterialise";

        // Brambleback (owner-approved research, 6 Nov 2026): moss green edge, amber rim, leaf-vein noise, leaves.
        private static readonly Color BrambleEdge = new Color(0.45f, 2.4f, 0.3f);
        private static readonly Color BrambleRim = new Color(2.4f, 1.15f, 0.12f);
        private static readonly Color Moss = new Color(0.36f, 0.62f, 0.2f);
        private static readonly Color Amber = new Color(0.95f, 0.62f, 0.15f);

        [MenuItem("Krytical Nova/Mounts/Build Materialise Assets")]
        public static void Build()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GraphPath);
            if (shader == null) { Debug.LogError($"[Mounts] No shader at {GraphPath}: import the graph first."); return; }
            WeatherVfxBuilder.EnsureFolder(LookFolder);

            Material material = MakeMaterial(shader);
            Texture2D veins = MakeTexture($"{LookFolder}/Brambleback_LeafVeins.png", 256, LeafVeins, true, TextureWrapMode.Repeat);
            Texture2D leaf = MakeTexture($"{LookFolder}/Brambleback_Leaf.png", 64, LeafShape, false, TextureWrapMode.Clamp);
            Material leafMaterial = MakeParticleMaterial($"{LookFolder}/Brambleback_Leaf.mat", leaf);
            GameObject appear = MakeParticles($"{LookFolder}/Brambleback_Appear.prefab", leafMaterial, true);
            GameObject vanish = MakeParticles($"{LookFolder}/Brambleback_Vanish.prefab", leafMaterial, false);

            var look = LoadOrCreate<MountMaterialiseSettings>(LookPath(BrambleKey));
            look.material = material;
            look.edgeColorA = BrambleEdge;
            look.edgeColorB = BrambleRim;
            look.burnWidth = 0.08f;
            look.rimStrength = 1.2f;
            look.noise = veins;
            look.noiseTiling = new Vector2(3f, 3f);
            look.pattern = MountMaterialiseSettings.Pattern.None;
            look.direction = MountMaterialiseSettings.Direction.BottomUp;
            look.directionWeight = 0.85f; // owner, 6 Nov 2026: clearly from the feet up; the veins only break up the edge
            look.inflate = 0.015f;
            look.appearParticles = appear;
            look.vanishParticles = vanish;
            look.particleLead = 0.05f;
            look.particleSeconds = 2f;
            EditorUtility.SetDirty(look);

            var bramble = AssetDatabase.LoadAssetAtPath<MountDefinition>(BrambleDefinitionPath);
            if (bramble != null)
            {
                bramble.materialise = look;
                EditorUtility.SetDirty(bramble);
            }
            else Debug.LogWarning($"[Mounts] No Brambleback definition at {BrambleDefinitionPath}: its look was made but not given.");

            AssetDatabase.SaveAssets();
            Debug.Log($"[Mounts] Materialise assets built: {MaterialPath}, {LookPath(BrambleKey)} (given to the Brambleback: {bramble != null}).");
        }

        // ------------------------------------------------------------------ assets

        private static Material MakeMaterial(Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "MountMaterialise" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetFloat("_Spawn", 1f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private delegate float Pixel(float u, float v);

        private static Texture2D MakeTexture(string path, int size, Pixel pixel, bool greyscale, TextureWrapMode wrap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float value = pixel((x + 0.5f) / size, (y + 0.5f) / size);
                pixels[y * size + x] = greyscale ? new Color(value, value, value, 1f) : new Color(1f, 1f, 1f, value);
            }
            texture.SetPixels(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = !greyscale;
                importer.alphaIsTransparency = !greyscale;
                importer.wrapMode = wrap;
                importer.mipmapEnabled = greyscale;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Leaf veins: the distance to the nearest edge between tiling cells (dark veins, bright cell middles), so the
        // dissolve eats through along vein lines. Wraps at the edges.
        private static float LeafVeins(float u, float v)
        {
            const int cells = 7;
            float first = float.MaxValue, second = float.MaxValue;
            for (int cy = -1; cy <= cells; cy++)
            for (int cx = -1; cx <= cells; cx++)
            {
                int wx = (cx + cells) % cells, wy = (cy + cells) % cells;
                float h = Hash(wx, wy);
                var point = new Vector2((cx + 0.2f + 0.6f * h) / cells, (cy + 0.2f + 0.6f * Hash(wy + 17, wx + 31)) / cells);
                float d = Vector2.Distance(new Vector2(u, v), point);
                if (d < first) { second = first; first = d; }
                else if (d < second) second = d;
            }
            float edge = Mathf.Clamp01((second - first) * cells * 1.6f);
            return Mathf.SmoothStep(0f, 1f, edge);
        }

        private static float Hash(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }

        // A leaf: a pointed oval with a soft edge (alpha only; the particles colour it).
        private static float LeafShape(float u, float v)
        {
            float x = (u - 0.5f) * 2f, y = (v - 0.5f) * 2f;
            float half = 0.55f * Mathf.Sqrt(Mathf.Max(0f, 1f - y * y)) * (1f - 0.35f * y);
            float inside = half - Mathf.Abs(x);
            float vein = Mathf.Abs(x) < 0.04f && y > -0.9f ? 0.65f : 1f;
            return Mathf.Clamp01(inside * 12f) * vein;
        }

        private static Material MakeParticleMaterial(string path, Texture2D texture)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Surface", 1f); // transparent
            material.SetFloat("_Blend", 0f);   // alpha
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // Appear: 40 leaves rise from a ring round the feet over 0.3 s and curl in. Vanish: 40 leaves shed from the
        // body and drift down. Both destroy themselves (the MountMaterialiser also removes them after Particle Seconds).
        private static GameObject MakeParticles(string path, Material material, bool appear)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            var ps = root.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = appear ? new ParticleSystem.MinMaxCurve(1.2f, 2.4f) : new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(Moss, Amber);
            main.gravityModifier = appear ? -0.15f : 0.6f;
            main.simulationSpace = appear ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.maxParticles = 64;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // 40 leaves, spread over the first 0.3 s.
            emission.SetBursts(new[]
            {
                new ParticleSystem.Burst(0f, 14), new ParticleSystem.Burst(0.1f, 14), new ParticleSystem.Burst(0.2f, 12),
            });

            var shape = ps.shape;
            shape.enabled = true;
            if (appear)
            {
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 1.1f;
                shape.radiusThickness = 0.3f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // flat on the ground, leaves going up
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(1.2f, 1.3f, 2.2f);
                shape.position = new Vector3(0f, 1.0f, 0f);
            }

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-4f, 4f);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = appear ? 0.6f : 0.35f;
            noise.frequency = 1.2f;

            if (appear)
            {
                // Curl in toward the body as they rise.
                var orbit = ps.velocityOverLifetime;
                orbit.enabled = true;
                orbit.space = ParticleSystemSimulationSpace.Local;
                orbit.orbitalY = new ParticleSystem.MinMaxCurve(2.5f);
                orbit.radial = new ParticleSystem.MinMaxCurve(-1.2f);
            }

            var renderer = root.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------ review picture

        /// <summary>The Brambleback (first stage) at _Spawn 0, 0.25, 0.5, 0.75 and 1, side by side, into one PNG in
        /// Drive (AI Outputs/MountMaterialise), or the project's Logs folder when Drive is not there.</summary>
        [MenuItem("Krytical Nova/Mounts/Render Materialise Preview")]
        public static void RenderPreview()
        {
            var look = AssetDatabase.LoadAssetAtPath<MountMaterialiseSettings>(LookPath(BrambleKey));
            var bramble = AssetDatabase.LoadAssetAtPath<MountDefinition>(BrambleDefinitionPath);
            GameObject model = bramble != null ? bramble.Stage(0).model : null;
            if (look == null || look.material == null || model == null)
            {
                Debug.LogError("[Mounts] Build the materialise assets first (and the Brambleback needs its first-stage model).");
                return;
            }

            float[] spawns = { 0f, 0.25f, 0.5f, 0.75f, 1f };
            const int tile = 512;
            var sheet = new Texture2D(tile * spawns.Length, tile, TextureFormat.RGBA32, false);
            var target = new RenderTexture(tile, tile, 24, RenderTextureFormat.ARGB32);
            var stage = new GameObject("MaterialisePreview") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.position = new Vector3(5000f, 0f, 5000f);
            try
            {
                GameObject subject = Object.Instantiate(model, stage.transform);
                subject.transform.localPosition = Vector3.zero;
                subject.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // side on
                var renderers = subject.GetComponentsInChildren<Renderer>();
                Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(stage.transform.position, Vector3.one);
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);

                var light = new GameObject("Key").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);

                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                float size = bounds.extents.magnitude;
                camera.transform.position = bounds.center + new Vector3(-0.6f, 0.35f, -1f).normalized * size * 2.6f;
                camera.transform.LookAt(bounds.center);
                camera.fieldOfView = 35f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.08f, 0.07f, 0.17f, 1f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = size * 10f;
                camera.targetTexture = target;

                // The same swap the MountMaterialiser makes in game.
                var copies = new System.Collections.Generic.List<Material>();
                foreach (Renderer r in renderers)
                {
                    Material[] originals = r.sharedMaterials;
                    var swapped = new Material[originals.Length];
                    for (int i = 0; i < originals.Length; i++)
                    {
                        var m = new Material(look.material);
                        if (originals[i] != null)
                            foreach (string p in new[] { "_BaseMap", "_BumpMap", "_EmissionMap" })
                                if (originals[i].HasProperty(p) && m.HasProperty(p)) m.SetTexture(p, originals[i].GetTexture(p));
                        if (originals[i] != null && originals[i].HasProperty("_BaseColor")) m.SetColor("_BaseColor", originals[i].GetColor("_BaseColor"));
                        m.SetColor("_EdgeColorA", look.edgeColorA);
                        m.SetColor("_EdgeColorB", look.edgeColorB);
                        m.SetFloat("_BurnWidth", look.burnWidth);
                        m.SetFloat("_RimStrength", look.rimStrength);
                        if (look.noise != null) m.SetTexture("_FadeTex", look.noise);
                        m.SetVector("_FadeTiling", look.noiseTiling);
                        m.SetFloat("_DirectionWeight", look.directionWeight);
                        m.SetFloat("_Inflate", look.inflate);
                        m.SetVector("_SweepOrigin", new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                        m.SetVector("_SweepAxis", Vector3.up);
                        m.SetFloat("_SweepLength", Mathf.Max(0.01f, bounds.size.y));
                        swapped[i] = m;
                        copies.Add(m);
                    }
                    r.sharedMaterials = swapped;
                }

                RenderTexture before = RenderTexture.active;
                for (int s = 0; s < spawns.Length; s++)
                {
                    foreach (Material m in copies) m.SetFloat("_Spawn", spawns[s]);
                    camera.Render();
                    RenderTexture.active = target;
                    sheet.ReadPixels(new Rect(0, 0, tile, tile), s * tile, 0);
                }
                RenderTexture.active = before;
                sheet.Apply();
                foreach (Material m in copies) Object.DestroyImmediate(m);
                camera.targetTexture = null;
            }
            finally
            {
                Object.DestroyImmediate(stage);
                target.Release();
                Object.DestroyImmediate(target);
            }

            string folder = Directory.Exists(Path.GetDirectoryName(PreviewDrive)) ? PreviewDrive
                : Path.Combine(Directory.GetCurrentDirectory(), "Logs", "MountMaterialise");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "Brambleback_Spawn_0_25_50_75_100.png");
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Debug.Log($"[Mounts] Materialise preview written: {file} (left to right: _Spawn 0, 0.25, 0.5, 0.75, 1).");
        }
    }
}
