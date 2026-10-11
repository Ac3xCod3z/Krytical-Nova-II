using System.Collections.Generic;
using System.IO;
using KryticalNova.World;
using KryticalNova.Vegetation.Grass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Sets up the GPU grass for Zone01: default grass types / biomes / rule set, procedural blade-clump meshes,
    /// the baked grass mask (R density, G biome, B moisture) and the scene GrassRenderer. Re-run "Bake Grass Mask"
    /// after the town or terrain changes.
    /// </summary>
    public static class GrassSystemBuilder
    {
        public const string Folder = "Assets/_Game/Environment/Vegetation/Grass/Data/Assets";
        private const string RulesPath = Folder + "/GRS_Zone01.asset";
        private const string MaskPath = ZoneTerrainGenerator.ZoneFolder + "/T_GrassMask_Zone01.png";
        private const string MaterialPath = "Assets/_Game/Environment/Vegetation/Grass/Materials/M_GrassBlades.mat";
        private const string ComputePath = "Assets/_Game/Environment/Vegetation/Grass/Compute/GrassGenerate.compute";
        private const string TrampleComputePath = "Assets/_Game/Environment/Vegetation/Grass/Compute/GrassTrample.compute";

        /// <summary>The rule set gets the trample map's compute shader (it is data the renderer reads from the rule set).</summary>
        [MenuItem("Krytical Nova/Zone/Grass/Link Trample Map")]
        public static void LinkTrampleMap()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GrassRuleSet>(RulesPath);
            var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(TrampleComputePath);
            if (rules == null || shader == null) { Debug.LogError("[Grass] The rule set or GrassTrample.compute is missing."); return; }
            if (rules.trampleShader == shader) return;
            rules.trampleShader = shader;
            EditorUtility.SetDirty(rules);
            AssetDatabase.SaveAssets();
            Debug.Log("[Grass] The rule set now holds the trample map's compute shader.");
        }
        private const string RendererName = "GPU Grass";

        [MenuItem("Krytical Nova/Zone/Grass/Build GPU Grass (Assets + Mask + Scene)")]
        public static void BuildAll()
        {
            var rules = EnsureAssets();
            LinkTrampleMap();
            BakeMask(rules);
            EnsureSceneRenderer(rules);
            // Grass blades are now GPU grass; terrain details keep only the flowers.
            ZoneTerrainGenerator.RepaintDetailsMenu();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        [MenuItem("Krytical Nova/Zone/Grass/Bake Grass Mask")]
        public static void BakeMaskMenu()
        {
            var rules = AssetDatabase.LoadAssetAtPath<GrassRuleSet>(RulesPath) ?? EnsureAssets();
            BakeMask(rules);
            var r = Object.FindAnyObjectByType<GrassRenderer>();
            if (r != null) r.Rebuild();
        }

        [MenuItem("Krytical Nova/Zone/Grass/Debug View: Off")] private static void DebugOff() => SetDebug(0);
        [MenuItem("Krytical Nova/Zone/Grass/Debug View: Placement")] private static void DebugPlacement() => SetDebug(1);
        [MenuItem("Krytical Nova/Zone/Grass/Debug View: LOD Bands")] private static void DebugLod() => SetDebug(2);
        [MenuItem("Krytical Nova/Zone/Grass/Debug View: Biomes")] private static void DebugBiomes() => SetDebug(3);

        private static void SetDebug(int mode)
        {
            GrassRenderer.DebugMode = mode;
            SceneView.RepaintAll();
        }

        // ---------------------------------------------------------------- assets

        public static GrassRuleSet EnsureAssets()
        {
            WeatherVfxBuilder.EnsureFolder(Folder);
            var meadowClump = ClumpMesh("GrassClump_Meadow", blades: 7, radius: 0.12f, baseWidth: 0.05f, lean: 16f, curve: 0.22f, seed: 11);
            // Blades of nearly one length: a mown lawn, not a tuft.
            var lawnClump = ClumpMesh("GrassClump_Lawn", blades: 9, radius: 0.16f, baseWidth: 0.045f, lean: 10f, curve: 0.12f, seed: 23,
                shortestBlade: 0.88f);
            var syntyTallA = SyntyMesh("SM_Gen_Env_Grass_Tall_01");
            var syntyTallB = SyntyMesh("SM_Gen_Env_Grass_Tall_02");
            var farTuft = TuftMesh("GrassTuft_Far", blades: 6, radius: 0.32f, baseWidth: 0.16f, seed: 37);
            var fillTuft = TuftMesh("GrassTuft_Fill", blades: 4, radius: 0.14f, baseWidth: 0.05f, seed: 53);

            var tallA = Type("GT_TallGrass_A", syntyTallA, t =>
            {
                t.heightRange = new Vector2(0.55f, 1.3f); t.widthRange = new Vector2(0.85f, 1.2f); t.density = 0.28f;
                t.windStrength = 1f; t.flutter = 0.35f; t.playerBend = 1.1f;
            });
            var tallB = Type("GT_TallGrass_B", syntyTallB, t =>
            {
                t.heightRange = new Vector2(0.5f, 1.2f); t.widthRange = new Vector2(0.85f, 1.2f); t.density = 0.28f;
                t.windStrength = 1f; t.flutter = 0.35f; t.playerBend = 1.1f;
            });
            // Same tall-grass meshes and behaviour at medium and short heights. Uniform scaling shrinks the clump
            // footprint with its height, so shorter types get wider width multipliers to keep their coverage.
            var medium = Type("GT_TallGrass_Medium", syntyTallA, t =>
            {
                t.heightRange = new Vector2(0.35f, 0.6f); t.widthRange = new Vector2(1.4f, 1.9f); t.density = 0.3f;
                t.windStrength = 1f; t.flutter = 0.35f; t.playerBend = 1.1f;
            });
            var shortGrass = Type("GT_TallGrass_Short", syntyTallB, t =>
            {
                t.heightRange = new Vector2(0.18f, 0.34f); t.widthRange = new Vector2(2f, 2.8f); t.density = 0.32f;
                t.windStrength = 1f; t.flutter = 0.35f; t.playerBend = 1.1f;
            });
            var blades = Type("GT_MeadowBlades", meadowClump, t =>
            {
                t.heightRange = new Vector2(0.28f, 0.7f); t.widthRange = new Vector2(1.3f, 1.8f); t.density = 0.7f;
                t.windStrength = 1.15f; t.flutter = 0.45f; t.playerBend = 1.2f;
            });
            var lawn = Type("GT_LawnBlades", lawnClump, t =>
            {
                t.heightRange = new Vector2(0.13f, 0.15f); t.widthRange = new Vector2(5.5f, 7f); t.density = 0.95f; // wide footprint, short even cut
                t.rootColor = new Color(0.14f, 0.3f, 0.12f); t.midColor = new Color(0.32f, 0.58f, 0.22f); t.tipColor = new Color(0.58f, 0.78f, 0.34f);
                t.patchTint = new Color(0.95f, 1f, 0.8f, 0.12f); t.brightnessVariation = 0.06f; t.rootOcclusion = 0.65f;
                t.windStrength = 0.6f; t.flutter = 0.25f; t.playerBend = 1.2f;
            });

            var meadow = Biome("GB_Meadow", b =>
            {
                b.density = 1f;
                FillMeadow(b, tallA, tallB, medium, shortGrass, blades);
            });
            var lawnBiome = Biome("GB_Lawn", b =>
            {
                b.density = 1f;
                b.fillDensity = 0.5f;
                b.entries.Add(new GrassBiome.Entry { type = lawn, weight = 1f, patchiness = 0f, patchSize = 10f });
            });

            // Existing meadow biomes pick up new types (medium / short); other assets keep their hand tuning.
            if (meadow.entries.Find(e => e.type == medium) == null || meadow.entries.Find(e => e.type == shortGrass) == null)
            {
                meadow.entries.Clear();
                FillMeadow(meadow, tallA, tallB, medium, shortGrass, blades);
                EditorUtility.SetDirty(meadow);
            }

            var rules = AssetDatabase.LoadAssetAtPath<GrassRuleSet>(RulesPath);
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<GrassRuleSet>();
                rules.biomes.Add(meadow);
                rules.biomes.Add(lawnBiome);
                rules.lawnBiome = 1;
                rules.lawnRegionHalfSize = ZoneTerrainGenerator.LoadOrCreateProfile().pavedHalfSize;
                AssetDatabase.CreateAsset(rules, RulesPath);
            }
            rules.farMesh = farTuft;
            rules.fillMesh = fillTuft;
            EditorUtility.SetDirty(rules);

            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null)
                AssetDatabase.CreateAsset(new Material(Shader.Find("KryticalNova/Grass Blades")), MaterialPath);
            AssetDatabase.SaveAssets();
            return rules;
        }

        /// <summary>Meadow mix: stands of tall, medium and short grass with thin blade clumps between them.</summary>
        private static void FillMeadow(GrassBiome b, GrassType tallA, GrassType tallB, GrassType medium, GrassType shortGrass, GrassType blades)
        {
            b.entries.Add(new GrassBiome.Entry { type = tallA, weight = 0.8f, patchiness = 0.6f, patchSize = 14f });
            b.entries.Add(new GrassBiome.Entry { type = tallB, weight = 0.8f, patchiness = 0.6f, patchSize = 11f });
            b.entries.Add(new GrassBiome.Entry { type = medium, weight = 1.2f, patchiness = 0.45f, patchSize = 12f });
            b.entries.Add(new GrassBiome.Entry { type = shortGrass, weight = 1.2f, patchiness = 0.35f, patchSize = 9f });
            b.entries.Add(new GrassBiome.Entry { type = blades, weight = 0.9f, patchiness = 0.2f, patchSize = 8f });
        }

        /// <summary>
        /// Far-band and fill stand-in: a few single-triangle blades (1 m tall, root at the origin), 4-6 triangles per
        /// tuft. It reads as grass because it shares the real grass's colours, wind and placement.
        /// </summary>
        private static Mesh TuftMesh(string name, int blades, float radius, float baseWidth, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();
            for (int b = 0; b < blades; b++)
            {
                float a = (b + R() * 0.6f) / blades * Mathf.PI * 2f;
                var root = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius * Mathf.Lerp(0.3f, 1f, R());
                float facing = R() * Mathf.PI * 2f;
                var widthDir = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
                var tip = root * 1.6f + Vector3.up * Mathf.Lerp(0.75f, 1f, R());
                var n = Vector3.Cross(widthDir, (tip - root).normalized).normalized;
                int i = verts.Count;
                verts.Add(root - widthDir * baseWidth * 0.5f);
                verts.Add(root + widthDir * baseWidth * 0.5f);
                verts.Add(tip);
                normals.Add(n); normals.Add(n); normals.Add(n);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
            }
            float top = 0f;
            foreach (var v in verts) top = Mathf.Max(top, v.y);
            for (int i = 0; i < verts.Count; i++) verts[i] /= top;

            string path = $"{Folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static GrassType Type(string name, Mesh mesh, System.Action<GrassType> init)
        {
            string path = $"{Folder}/{name}.asset";
            var t = AssetDatabase.LoadAssetAtPath<GrassType>(path);
            if (t != null) return t; // keep hand tuning
            t = ScriptableObject.CreateInstance<GrassType>();
            t.mesh = mesh;
            init(t);
            AssetDatabase.CreateAsset(t, path);
            return t;
        }

        private static GrassBiome Biome(string name, System.Action<GrassBiome> init)
        {
            string path = $"{Folder}/{name}.asset";
            var b = AssetDatabase.LoadAssetAtPath<GrassBiome>(path);
            if (b != null) return b;
            b = ScriptableObject.CreateInstance<GrassBiome>();
            init(b);
            AssetDatabase.CreateAsset(b, path);
            return b;
        }

        private static Mesh SyntyMesh(string prefabName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{FoliageAssetBuilder.GenericNature}{prefabName}.prefab");
            var filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// A clump of thin tapered blades (1 m tall, root at the origin) bending outward. Cheap: 4 segments per blade.
        /// Regenerated every run so tweaks here apply; the asset GUID is kept.
        /// </summary>
        private static Mesh ClumpMesh(string name, int blades, float radius, float baseWidth, float lean, float curve, int seed,
            float shortestBlade = 0.72f)
        {
            const int segments = 4;
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int b = 0; b < blades; b++)
            {
                float angle = R() * Mathf.PI * 2f;
                var root = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius * Mathf.Sqrt(R());
                float facing = R() * Mathf.PI * 2f;
                var widthDir = new Vector3(Mathf.Cos(facing), 0f, Mathf.Sin(facing));
                var leanDir = Vector3.Cross(widthDir, Vector3.up).normalized * (R() < 0.5f ? -1f : 1f);
                // Lean outward from the clump centre a little too.
                if (root.sqrMagnitude > 1e-6f) leanDir = (leanDir + root.normalized * 0.8f).normalized;
                float height = Mathf.Lerp(shortestBlade, 1f, R());
                float leanAmount = Mathf.Tan(Mathf.Lerp(lean * 0.4f, lean, R()) * Mathf.Deg2Rad);
                float width = baseWidth * Mathf.Lerp(0.8f, 1.2f, R());
                int start = verts.Count;

                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    // Quadratic bend: straight at the root, curving at the tip.
                    var spine = root + Vector3.up * (height * t) + leanDir * (height * (leanAmount * t + curve * t * t));
                    var faceNormal = Vector3.Cross(widthDir, (leanDir * (leanAmount + 2f * curve * t) * height + Vector3.up * height).normalized).normalized;
                    if (s == segments)
                    {
                        verts.Add(spine);
                        normals.Add(faceNormal);
                        uvs.Add(new Vector2(0.5f, spine.y));
                        break;
                    }
                    float w = width * (1f - t * 0.85f) * 0.5f;
                    verts.Add(spine - widthDir * w); normals.Add(faceNormal); uvs.Add(new Vector2(0f, spine.y));
                    verts.Add(spine + widthDir * w); normals.Add(faceNormal); uvs.Add(new Vector2(1f, spine.y));
                }
                for (int s = 0; s < segments - 1; s++)
                {
                    int a = start + s * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
                int last = start + (segments - 1) * 2;
                tris.Add(last); tris.Add(last + 2); tris.Add(last + 1);
            }

            // Normalise so the tallest blade tip sits at exactly 1 m.
            float top = 0f;
            foreach (var v in verts) top = Mathf.Max(top, v.y);
            for (int i = 0; i < verts.Count; i++) verts[i] /= top;

            string path = $"{Folder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ---------------------------------------------------------------- mask

        /// <summary>
        /// R = density (ground × slope × altitude × road/building falloff × artist mask), G = biome index / 3,
        /// B = moisture (near water). Roads and buildings fade out over <see cref="GrassRuleSet.meshEdgeFalloff"/>.
        /// The town's kept lawns are full to their edges: every texel a lawn rectangle touches is lawn, whatever
        /// paving or props stand next to it, and the renderer cuts the grass off exactly at the rectangle.
        /// </summary>
        public static void BakeMask(GrassRuleSet rules)
        {
            var profile = ZoneTerrainGenerator.LoadOrCreateProfile();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(ZoneTerrainGenerator.TerrainDataPath);
            if (data == null) { Debug.LogError("[Grass] Generate the terrain first."); return; }

            int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(rules.maskResolution), 128, 4096);
            float half = profile.size * 0.5f;
            float texel = profile.size / res;
            int aRes = data.alphamapResolution;
            var alpha = data.GetAlphamaps(0, 0, aRes, aRes);
            int layer = Mathf.Clamp(rules.grassSplatLayer, 0, data.alphamapLayers - 1);

            // Distance (m) from road / sidewalk / building / prop meshes, and from water.
            var blocked = ZoneTerrainGenerator.MeshExclusionMask(profile, res, rules.meshMargin, false);
            var wet = new bool[res, res];
            var heights = new float[res, res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float h = data.GetInterpolatedHeight((x + 0.5f) / res, (z + 0.5f) / res) + profile.baseHeight;
                heights[z, x] = h;
                wet[z, x] = h < profile.waterLevel + 0.2f;
            }
            var meshDist = DistanceField(blocked, res, texel);
            var waterDist = DistanceField(wet, res, texel);

            Color[] artist = null;
            int artistRes = 0;
            if (rules.artistMask != null && rules.artistMask.isReadable)
            {
                artist = rules.artistMask.GetPixels();
                artistRes = rules.artistMask.width;
            }

            Rect[] keptLawns = StarterTownBuilder.PlazaLawnRects;
            Rect[] lawnHoles = StarterTownBuilder.PlazaLawnHoles;
            if (keptLawns.Length > GrassRuleSet.MaxLawnRects)
                Debug.LogWarning($"[Grass] {keptLawns.Length} kept lawns, but only {GrassRuleSet.MaxLawnRects} get a trimmed edge.");
            rules.lawnRects.Clear();
            for (int i = 0; i < keptLawns.Length && i < GrassRuleSet.MaxLawnRects; i++) rules.lawnRects.Add(keptLawns[i]);
            float halfTexel = texel * 0.5f;

            var pixels = new Color32[res * res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float nx = (x + 0.5f) / res, nz = (z + 0.5f) / res;
                float wx = -half + profile.size * nx, wz = -half + profile.size * nz;

                bool kept = false;
                foreach (Rect r in rules.lawnRects)
                    kept |= wx + halfTexel > r.xMin && wx - halfTexel < r.xMax && wz + halfTexel > r.yMin && wz - halfTexel < r.yMax;
                if (kept)
                    foreach (Rect hole in lawnHoles)
                        if (hole.Contains(new Vector2(wx, wz))) kept = false;

                bool lawn = kept || Mathf.Max(Mathf.Abs(wx), Mathf.Abs(wz)) < rules.lawnRegionHalfSize;
                float splat = SampleSplat(alpha, aRes, nx, nz, layer);
                var groundRange = lawn ? rules.lawnGroundWeightRange : rules.groundWeightRange;
                float ground = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(groundRange.x, groundRange.y, splat));
                float slope = Mathf.Clamp01(rules.slopeDensity.Evaluate(data.GetSteepness(nx, nz)));
                float altitude = Mathf.Clamp01(rules.altitudeDensity.Evaluate(heights[z, x] - profile.waterLevel));
                float falloff = lawn ? rules.lawnEdgeFalloff : rules.meshEdgeFalloff;
                float edge = blocked[z, x] ? 0f : falloff > 0f ? Mathf.SmoothStep(0f, 1f, meshDist[z, x] / falloff) : 1f;
                float art = 1f;
                if (artist != null)
                    art = artist[Mathf.Min(artistRes - 1, (int)(nz * artistRes)) * artistRes + Mathf.Min(artistRes - 1, (int)(nx * artistRes))].r;

                float density = kept ? art : ground * slope * altitude * edge * art;
                int biome = lawn ? rules.lawnBiome : 0;
                float moisture = 1f - Mathf.Clamp01(waterDist[z, x] / rules.moistureDistance);

                pixels[z * res + x] = new Color32(
                    (byte)Mathf.RoundToInt(density * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp(biome, 0, 3) / 3f * 255f),
                    (byte)Mathf.RoundToInt(moisture * 255f), 255);
            }

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(MaskPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(MaskPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(MaskPath);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();

            rules.mask = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath);
            EditorUtility.SetDirty(rules);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Grass] Baked grass mask {res}x{res} ({texel:0.00} m per texel).");
        }

        /// <summary>Bilinear splat weight (the mask is finer than the alphamap, so nearest sampling would step).</summary>
        private static float SampleSplat(float[,,] alpha, int aRes, float nx, float nz, int layer)
        {
            float fx = nx * aRes - 0.5f, fz = nz * aRes - 0.5f;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, aRes - 1), z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, aRes - 1);
            int x1 = Mathf.Min(x0 + 1, aRes - 1), z1 = Mathf.Min(z0 + 1, aRes - 1);
            float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);
            return Mathf.Lerp(Mathf.Lerp(alpha[z0, x0, layer], alpha[z0, x1, layer], tx),
                              Mathf.Lerp(alpha[z1, x0, layer], alpha[z1, x1, layer], tx), tz);
        }

        /// <summary>Approximate distance (m) to the nearest set cell (two-pass chamfer).</summary>
        private static float[,] DistanceField(bool[,] set, int res, float texel)
        {
            const float Diag = 1.41421356f;
            var d = new float[res, res];
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) d[z, x] = set[z, x] ? 0f : 1e6f;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float v = d[z, x];
                if (x > 0) v = Mathf.Min(v, d[z, x - 1] + 1f);
                if (z > 0)
                {
                    v = Mathf.Min(v, d[z - 1, x] + 1f);
                    if (x > 0) v = Mathf.Min(v, d[z - 1, x - 1] + Diag);
                    if (x < res - 1) v = Mathf.Min(v, d[z - 1, x + 1] + Diag);
                }
                d[z, x] = v;
            }
            for (int z = res - 1; z >= 0; z--)
            for (int x = res - 1; x >= 0; x--)
            {
                float v = d[z, x];
                if (x < res - 1) v = Mathf.Min(v, d[z, x + 1] + 1f);
                if (z < res - 1)
                {
                    v = Mathf.Min(v, d[z + 1, x] + 1f);
                    if (x < res - 1) v = Mathf.Min(v, d[z + 1, x + 1] + Diag);
                    if (x > 0) v = Mathf.Min(v, d[z + 1, x - 1] + Diag);
                }
                d[z, x] = v;
            }
            for (int z = 0; z < res; z++) for (int x = 0; x < res; x++) d[z, x] *= texel;
            return d;
        }

        // ---------------------------------------------------------------- scene

        private static void EnsureSceneRenderer(GrassRuleSet rules)
        {
            var env = GameObject.Find(ZoneTerrainGenerator.EnvironmentRootName);
            if (env == null) env = new GameObject(ZoneTerrainGenerator.EnvironmentRootName);
            var t = env.transform.Find(RendererName);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(RendererName);
                go.transform.SetParent(env.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Create GPU grass");
            }
            else go = t.gameObject;

            var renderer = ZoneTerrainGenerator.GetOrAdd<GrassRenderer>(go);
            var so = new SerializedObject(renderer);
            so.FindProperty("rules").objectReferenceValue = rules;
            so.FindProperty("terrain").objectReferenceValue = Object.FindAnyObjectByType<Terrain>();
            so.FindProperty("generateShader").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            so.FindProperty("grassMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            renderer.Rebuild();
        }
    }
}
