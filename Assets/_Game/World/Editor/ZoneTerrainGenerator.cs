using System.Collections.Generic;
using KryticalNova.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Generates the Zone01 outer field from <see cref="ZoneTerrainProfile"/>: rolling hills, a mountain rim,
    /// a flat town plateau, river channels, lake basins, a portal pad and flattened paths, painted by rule
    /// and scattered with Synty nature props. Existing terrain data is never overwritten unless forced,
    /// so hand sculpting with Terrain Tools survives re-runs.
    /// </summary>
    public static class ZoneTerrainGenerator
    {
        public const string ZoneFolder = "Assets/_Game/World/Zones/Zone01";
        public const string ProfilePath = ZoneFolder + "/ZTP_Zone01.asset";
        public const string TerrainDataPath = ZoneFolder + "/Zone01_TerrainData.asset";
        public const string TerrainObjectName = "Zone01 Terrain";
        public const string EnvironmentRootName = "--- Environment ---";
        private const string LayerFolder = ZoneFolder + "/TerrainLayers";
        private const string WaterMaterialPath = ZoneFolder + "/M_Water_Zone01.mat";
        private const string GroundLayerName = "Ground";
        private const string WaterLayerName = "Water";
        private const int NotWalkableArea = 1;

        private const string GenericTextures = "Assets/Synty/PolygonGeneric/Textures/";
        private const string GenericNature = "Assets/Synty/PolygonGeneric/Prefabs/Environment/";

        private static readonly string[] TreePrefabs =
        {
            "SM_Gen_Env_Tree_01", "SM_Gen_Env_Tree_02", "SM_Gen_Env_Tree_03",
            "SM_Gen_Env_Tree_Pine_01", "SM_Gen_Env_Tree_Pine_02", "SM_Gen_Env_Tree_Pine_03"
        };
        // Bushes and ferns stay terrain trees; grass and flowers are terrain details (see PaintDetails).
        private static readonly string[] GroundCoverPrefabs =
        {
            "SM_Gen_Env_Bush_01", "SM_Gen_Env_Bush_02", "SM_Gen_Env_Bush_03", "SM_Gen_Env_Bush_04",
            "SM_Gen_Env_Fern_02", "SM_Gen_Env_Fern_03"
        };
        private static readonly string[] RockPrefabs =
        {
            "SM_Gen_Env_Rock_01", "SM_Gen_Env_Rock_02", "SM_Gen_Env_Rock_03", "SM_Gen_Env_Rock_04",
            "SM_Gen_Env_Rock_05", "SM_Gen_Env_Rock_06", "SM_Gen_Env_Rock_07", "SM_Gen_Env_Stump_02", "SM_Gen_Env_Log_01"
        };

        // ---------------------------------------------------------------- menus

        [MenuItem("Krytical Nova/Zone/Generate Zone01 Terrain")]
        public static void GenerateMenu() => Generate(false);

        [MenuItem("Krytical Nova/Zone/Generate Zone01 Terrain (Force Regenerate)")]
        public static void ForceGenerateMenu()
        {
            if (EditorUtility.DisplayDialog("Regenerate Zone01 terrain?",
                    "This replaces the terrain heights, paint and trees, discarding any hand sculpting.", "Regenerate", "Cancel"))
                Generate(true);
        }

        [MenuItem("Krytical Nova/Zone/Apply City Layout To Terrain Profile")]
        public static void ApplyCityLayoutMenu()
        {
            var profile = LoadOrCreateProfile();
            Undo.RecordObject(profile, "Apply city layout");
            ApplyCityLayout(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            Debug.Log("[ZoneTerrain] City layout written to the profile. Run Force Regenerate to rebuild the terrain.");
        }

        [MenuItem("Krytical Nova/Zone/Repaint Terrain Layers Only")]
        public static void RepaintMenu()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null) { Debug.LogError("[ZoneTerrain] Generate the terrain first."); return; }
            Paint(LoadOrCreateProfile(), data);
            AssetDatabase.SaveAssets();
            Debug.Log("[ZoneTerrain] Repainted terrain layers (heights untouched).");
        }

        [MenuItem("Krytical Nova/Zone/Rescatter Trees and Rocks")]
        public static void RescatterMenu()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null) { Debug.LogError("[ZoneTerrain] Generate the terrain first."); return; }
            var profile = LoadOrCreateProfile();
            ScatterVegetation(profile, data);
            ScatterRocks(profile, data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        [MenuItem("Krytical Nova/Zone/Repaint Grass and Flowers Only")]
        public static void RepaintDetailsMenu()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null) { Debug.LogError("[ZoneTerrain] Generate the terrain first."); return; }
            var profile = LoadOrCreateProfile();
            PaintDetails(profile, data);
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain != null) ApplyDetailSettings(profile, terrain);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // ---------------------------------------------------------------- entry point

        /// <summary>Creates (or, with force, rebuilds) the terrain data, then makes sure the scene objects exist.</summary>
        public static Terrain Generate(bool force)
        {
            WeatherVfxBuilder.EnsureFolder(LayerFolder);
            var profile = LoadOrCreateProfile();
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            bool build = force || data == null;
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }

            if (build)
            {
                Sculpt(profile, data);
                Paint(profile, data);
                ScatterVegetation(profile, data);
                PaintDetails(profile, data);
            }
            else
            {
                Debug.Log("[ZoneTerrain] Terrain data exists; keeping hand edits. Use Force Regenerate to rebuild it.");
            }

            var terrain = EnsureTerrainObject(profile, data);
            EnsureWater(profile);
            EnsureWaterSurface(profile, data, build);
            if (build || GameObject.Find(EnvironmentRootName + "/Rocks") == null)
                ScatterRocks(profile, data);

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            return terrain;
        }

        // ---------------------------------------------------------------- profile

        public static ZoneTerrainProfile LoadOrCreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ZoneTerrainProfile>(ProfilePath);
            if (profile != null) return profile;
            WeatherVfxBuilder.EnsureFolder(ZoneFolder);
            profile = ScriptableObject.CreateInstance<ZoneTerrainProfile>();
            FillDefaultLayout(profile);
            AssetDatabase.CreateAsset(profile, ProfilePath);
            return profile;
        }

        private static void FillDefaultLayout(ZoneTerrainProfile p) => ApplyCityLayout(p);

        /// <summary>
        /// The walled-city layout (reference map): a 1 km field, walls at ±110 m on a flat plateau with a ruined
        /// outskirts band, a river cutting off the north island (portal) and a branch running south down the east
        /// side. Origin is the city centre, +Z is north. Overwrites size, plateau, water, paths and portal.
        /// </summary>
        public static void ApplyCityLayout(ZoneTerrainProfile p)
        {
            p.size = 1000f;
            p.heightRange = 90f;
            p.heightmapResolution = 1025;
            p.alphamapResolution = 1024;
            p.hillFadeDistance = new Vector2(200f, 320f);
            p.rimWidth = 60f;
            p.rimHeight = 48f;
            p.townHalfSize = 175f;
            p.townFalloff = 28f;
            p.pavedHalfSize = 112f;
            p.ruinsHalfSize = 178f;
            p.greenHalfSize = 20f;
            p.riverWidth = 22f;
            p.riverBankWidth = 8f;

            p.rivers.Clear();
            p.rivers.Add(Line("North river (east sea \u2192 west lake), cuts off the island",
                540, 250, 420, 238, 300, 222, 200, 212, 100, 208, 0, 206, -100, 210, -210, 224, -320, 252, -380, 280));
            p.rivers.Add(Line("East branch (\u2192 SE lake)",
                300, 222, 238, 160, 228, 60, 230, -40, 236, -140, 262, -240, 318, -322));
            p.lakes.Clear();
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-385, 288), radius = 46f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-330, 30), radius = 40f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-190, -340), radius = 44f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(330, -335), radius = 42f });

            p.paths.Clear();
            p.paths.Add(Line("North gate \u2192 bridge \u2192 island \u2192 portal", 0, 110, 0, 250, 80, 300, 170, 336, 262, 362));
            p.paths.Add(Line("South gate", 0, -110, 0, -250, -40, -380));
            p.paths.Add(Line("East gate \u2192 east bridge", 110, 0, 200, 0, 300, 10, 380, 30));
            p.paths.Add(Line("West gate", -110, 0, -230, -8, -380, 0));

            p.portalCenter = new Vector2(262f, 362f);
            p.portalPadRadius = 16f;
            p.treeCount = 900;
            p.rockCount = 240;
            p.grassCount = 700; // bushes and ferns only; grass and flowers are details
            FillDefaultDetailLayers(p);
        }

        /// <summary>Flower patches (terrain details). Grass is drawn by the GPU grass system.</summary>
        public static void FillDefaultDetailLayers(ZoneTerrainProfile p)
        {
            p.detailLayers.Clear();
            // Grass itself is GPU grass (GrassRenderer / GrassSystemBuilder); terrain details only carry flowers.
            p.detailLayers.Add(Detail("SM_Gen_Env_Flowers_02", 0.45f, 0.8f, 0.35f, 0.07f, 0.62f, 0.4f));
            p.detailLayers.Add(Detail("SM_Gen_Env_Flowers_03", 0.45f, 0.8f, 0.4f, 0.08f, 0.6f, 0.4f));
            p.detailLayers.Add(Detail("SM_Gen_Env_Flowers_05", 0.4f, 0.7f, 0.25f, 0.075f, 0.64f, 0.4f));
            p.detailLayers.Add(Detail("SM_Gen_Env_Flowers_06", 0.45f, 0.8f, 0.3f, 0.065f, 0.63f, 0.4f));
            p.detailLayers.Add(Detail("SM_Gen_Env_Flowers_07", 0.4f, 0.75f, 0.25f, 0.09f, 0.66f, 0.4f));
        }

        private static ZoneTerrainProfile.DetailLayer Detail(string prefab, float min, float max, float density,
            float patchScale, float threshold, float spread, float minHeight = 0f, float maxHeight = 0f)
        {
            return new ZoneTerrainProfile.DetailLayer
            {
                prefab = prefab, minScale = min, maxScale = max, density = density,
                patchScale = patchScale, patchThreshold = threshold, noiseSpread = spread,
                minHeight = minHeight, maxHeight = maxHeight
            };
        }

        private static ZoneTerrainProfile.DetailLayer Lawn(string prefab, float density)
        {
            return new ZoneTerrainProfile.DetailLayer
            {
                prefab = prefab, minScale = 0.7f, maxScale = 0.85f, minHeight = 0.36f, maxHeight = 0.42f,
                density = density, patchScale = 0.05f, patchThreshold = 0f, noiseSpread = 0.1f, lawn = true
            };
        }

        private static ZoneTerrainProfile.Polyline Line(string name, params float[] xz)
        {
            var line = new ZoneTerrainProfile.Polyline { name = name };
            for (int i = 0; i + 1 < xz.Length; i += 2) line.points.Add(new Vector2(xz[i], xz[i + 1]));
            return line;
        }

        // ---------------------------------------------------------------- shape

        private static void Sculpt(ZoneTerrainProfile p, TerrainData data)
        {
            int res = Mathf.ClosestPowerOfTwo(Mathf.Max(33, p.heightmapResolution - 1)) + 1;
            data.heightmapResolution = res;
            data.size = new Vector3(p.size, p.heightRange, p.size);

            var noiseOffset = NoiseOffset(p.seed);
            var heights = new float[res, res];
            float half = p.size * 0.5f;
            for (int zi = 0; zi < res; zi++)
            {
                float z = -half + p.size * zi / (res - 1);
                for (int xi = 0; xi < res; xi++)
                {
                    float x = -half + p.size * xi / (res - 1);
                    float h = HeightAt(p, x, z, noiseOffset);
                    heights[zi, xi] = Mathf.Clamp01((h - p.baseHeight) / p.heightRange);
                }
            }
            data.SetHeights(0, 0, heights);
        }

        private static float HeightAt(ZoneTerrainProfile p, float x, float z, Vector2 off)
        {
            var pos = new Vector2(x, z);
            float half = p.size * 0.5f;

            // Rolling field: a little undulation everywhere, real hills away from town.
            float n = Fbm(x * p.hillScale + off.x, z * p.hillScale + off.y);
            float hillMask = Smooth(p.hillFadeDistance.x, p.hillFadeDistance.y, pos.magnitude);
            float h = p.townHeight + 0.3f + n * 0.8f + n * p.hillAmplitude * hillMask;

            // Mountain rim along the map border.
            float edge = Mathf.Min(half - Mathf.Abs(x), half - Mathf.Abs(z));
            float rim = 1f - Smooth(0f, p.rimWidth, edge);
            float rimNoise = Fbm(x * 0.021f + off.y, z * 0.021f + off.x);
            h += Mathf.Pow(rim, 1.7f) * p.rimHeight * (0.55f + 0.9f * rimNoise);

            // Flat town plateau (square, matches the walls).
            float dTown = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - p.townHalfSize;
            h = Mathf.Lerp(p.townHeight, h, Smooth(0f, p.townFalloff, dTown));

            // Portal pad.
            float dPad = Vector2.Distance(pos, p.portalCenter) - p.portalPadRadius;
            h = Mathf.Lerp(p.portalPadHeight, h, Smooth(0f, 18f, dPad));

            // Paths are flattened to a walkable level.
            float dPath = DistanceToLines(p.paths, pos);
            h = Mathf.Lerp(p.pathHeight, h, Smooth(p.pathWidth * 0.5f, p.pathWidth * 0.5f + p.pathShoulder, dPath));

            // Rivers and lakes only ever cut down.
            float dRiver = DistanceToLines(p.rivers, pos);
            float riverT = Smooth(p.riverWidth * 0.25f, p.riverWidth * 0.5f + p.riverBankWidth, dRiver);
            h = Mathf.Min(h, Mathf.Lerp(p.riverBedHeight, h, riverT));

            for (int i = 0; i < p.lakes.Count; i++)
            {
                float dLake = LakeDistance(p.lakes[i], pos, off);
                h = Mathf.Min(h, Mathf.Lerp(p.lakeBedHeight, h, Smooth(-10f, p.lakeShoreWidth, dLake)));
            }
            return h;
        }

        /// <summary>Signed distance to a lake's wobbly shoreline (negative inside).</summary>
        private static float LakeDistance(ZoneTerrainProfile.Lake lake, Vector2 pos, Vector2 off)
        {
            Vector2 d = pos - lake.center;
            float angle = Mathf.Atan2(d.y, d.x);
            float wobble = Mathf.PerlinNoise(Mathf.Cos(angle) * 1.3f + off.x + lake.center.x * 0.01f,
                                             Mathf.Sin(angle) * 1.3f + off.y + lake.center.y * 0.01f) - 0.5f;
            return d.magnitude - lake.radius * (1f + 0.35f * wobble);
        }

        // ---------------------------------------------------------------- paint

        private static void Paint(ZoneTerrainProfile p, TerrainData data)
        {
            var layers = new[]
            {
                Layer("TL_Grass", GenericTextures + "Generic_Grass.png", GenericTextures + "Generic_Grass_Normals.png", 7f),
                Layer("TL_Dirt", GenericTextures + "Generic_Dirt.png", null, 6f),
                Layer("TL_Rock", GenericTextures + "Generic_Rock.png", GenericTextures + "Generic_Rock_Normals.png", 9f),
                Layer("TL_TownStone", GenericTextures + "Generic_Concrete.png", null, 5f),
            };
            data.terrainLayers = layers;
            data.alphamapResolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(p.alphamapResolution), 16, 2048);

            int res = data.alphamapResolution;
            var alpha = new float[res, res, layers.Length];
            var off = NoiseOffset(p.seed);
            float half = p.size * 0.5f;
            for (int zi = 0; zi < res; zi++)
            {
                float nz = (zi + 0.5f) / res;
                float z = -half + p.size * nz;
                for (int xi = 0; xi < res; xi++)
                {
                    float nx = (xi + 0.5f) / res;
                    float x = -half + p.size * nx;
                    bool inLawn = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < p.pavedHalfSize;
                    var pos = new Vector2(x, z);
                    float height = data.GetInterpolatedHeight(nx, nz) + p.baseHeight;
                    float slope = data.GetSteepness(nx, nz);

                    float rock = Smooth(p.rockSlopeStart, p.rockSlopeFull, slope);
                    float path = 1f - Smooth(p.pathWidth * 0.5f - 1f, p.pathWidth * 0.5f + 2.5f, DistanceToLines(p.paths, pos));
                    float pad = 1f - Smooth(0f, 5f, Vector2.Distance(pos, p.portalCenter) - p.portalPadRadius);
                    float wet = 1f - Smooth(p.waterLevel + 0.6f, p.waterLevel + 1.6f, height);
                    float square = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                    float town = 1f - Smooth(-3f, 1f, square - p.pavedHalfSize);
                    // The plaza lawns are grass inside the paving (walkway tiles are laid over them).
                    float green = p.greenHalfSize > 0f ? 1f - Smooth(-1.5f, 0.5f, square - p.greenHalfSize) : 0f;
                    town *= 1f - green;
                    float patchy = Smooth(0.55f, 0.75f, Mathf.PerlinNoise(x * 0.05f + off.x, z * 0.05f + off.y)) * 0.35f;
                    // Ruined outskirts: trampled dirt broken by grass, fading out past the band.
                    float ruins = p.ruinsHalfSize > 0f
                        ? (1f - Smooth(-8f, 14f, square - p.ruinsHalfSize)) * (0.55f + 0.45f * Mathf.PerlinNoise(x * 0.09f + off.y, z * 0.09f + off.x))
                        : 0f;

                    float dirt = Mathf.Max(Mathf.Max(path, pad), Mathf.Max(Mathf.Max(wet, patchy), ruins)) * (1f - green);
                    float wRock = rock * (1f - town);
                    float wDirt = dirt * (1f - rock) * (1f - town);
                    float wTown = town;
                    float wGrass = Mathf.Max(0f, 1f - wRock - wDirt - wTown);
                    float sum = wGrass + wDirt + wRock + wTown;
                    alpha[zi, xi, 0] = wGrass / sum;
                    alpha[zi, xi, 1] = wDirt / sum;
                    alpha[zi, xi, 2] = wRock / sum;
                    alpha[zi, xi, 3] = wTown / sum;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
        }

        private static TerrainLayer Layer(string name, string diffuse, string normal, float tile)
        {
            string path = $"{LayerFolder}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null) return layer; // keep hand-tuned layers
            layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(diffuse),
                normalMapTexture = normal != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(normal) : null,
                tileSize = new Vector2(tile, tile),
                smoothness = 0f,
                metallic = 0f,
                // Synty diffuse alpha is opaque (=1); the default DiffuseAlphaChannel source made every layer glossy.
                smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly
            };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
        }

        // ---------------------------------------------------------------- scatter

        private static void ScatterVegetation(ZoneTerrainProfile p, TerrainData data)
        {
            var prototypes = new List<TreePrototype>();
            foreach (var name in TreePrefabs) AddPrototype(prototypes, name);
            int treeKinds = prototypes.Count;
            foreach (var name in GroundCoverPrefabs) AddPrototype(prototypes, name);
            data.treePrototypes = prototypes.ToArray();

            var rng = new System.Random(p.seed + 11);
            var instances = new List<TreeInstance>(p.treeCount + p.grassCount);
            var occupied = new HashSet<Vector2Int>();

            int placed = 0;
            for (int attempt = 0; attempt < p.treeCount * 12 && placed < p.treeCount; attempt++)
            {
                Vector2 pos = RandomPoint(p, rng);
                if (!CanScatter(p, data, pos, p.scatterClearance + 2f, out float nHeight)) continue;
                // Woods: denser in noise clumps, sparse in meadows.
                if (Mathf.PerlinNoise(pos.x * 0.018f + 50f, pos.y * 0.018f + 50f) < 0.42f && rng.NextDouble() < 0.8) continue;
                var cell = new Vector2Int(Mathf.FloorToInt(pos.x / 4.5f), Mathf.FloorToInt(pos.y / 4.5f));
                if (!occupied.Add(cell)) continue;
                instances.Add(Tree(p, pos, nHeight, rng.Next(treeKinds), Range(rng, 0.85f, 1.35f), rng));
                placed++;
            }

            placed = 0;
            for (int attempt = 0; attempt < p.grassCount * 8 && placed < p.grassCount; attempt++)
            {
                Vector2 pos = RandomPoint(p, rng);
                if (!CanScatter(p, data, pos, 2.5f, out float nHeight)) continue;
                instances.Add(Tree(p, pos, nHeight, treeKinds + rng.Next(prototypes.Count - treeKinds), Range(rng, 0.8f, 1.3f), rng));
                placed++;
            }

            data.SetTreeInstances(instances.ToArray(), true);
            EditorUtility.SetDirty(data);
            Debug.Log($"[ZoneTerrain] Scattered {instances.Count} trees / plants.");
        }

        /// <summary>Rocks are scene objects (mesh colliders), so players and the NavMesh treat them as solid.</summary>
        private static void ScatterRocks(ZoneTerrainProfile p, TerrainData data)
        {
            var env = GetOrCreateRoot(EnvironmentRootName);
            var old = env.transform.Find("Rocks");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(env.transform, false);

            var rng = new System.Random(p.seed + 29);
            int placed = 0;
            for (int attempt = 0; attempt < p.rockCount * 10 && placed < p.rockCount; attempt++)
            {
                Vector2 pos = RandomPoint(p, rng);
                if (!CanScatter(p, data, pos, p.scatterClearance, out float nHeight, allowSteep: true)) continue;
                var prefab = LoadPrefab(GenericNature + RockPrefabs[rng.Next(RockPrefabs.Length)]);
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rocks);
                float y = nHeight * p.heightRange + p.baseHeight - 0.15f;
                go.transform.SetPositionAndRotation(new Vector3(pos.x, y, pos.y), Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f));
                go.transform.localScale = Vector3.one * Range(rng, 0.8f, 1.6f);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
                placed++;
            }
        }

        private static bool CanScatter(ZoneTerrainProfile p, TerrainData data, Vector2 pos, float clearance, out float normalizedHeight, bool allowSteep = false)
        {
            normalizedHeight = 0f;
            float half = p.size * 0.5f;
            float nx = (pos.x + half) / p.size, nz = (pos.y + half) / p.size;
            float height = data.GetInterpolatedHeight(nx, nz) + p.baseHeight;
            if (height < p.waterLevel + 0.8f) return false;
            if (!allowSteep && data.GetSteepness(nx, nz) > p.maxScatterSlope) return false;
            if (Mathf.Max(Mathf.Abs(pos.x), Mathf.Abs(pos.y)) < p.townHalfSize + clearance) return false;
            if (DistanceToLines(p.paths, pos) < p.pathWidth * 0.5f + clearance) return false;
            if (Vector2.Distance(pos, p.portalCenter) < p.portalPadRadius + clearance + 4f) return false;
            normalizedHeight = data.GetInterpolatedHeight(nx, nz) / p.heightRange;
            return true;
        }

        private static TreeInstance Tree(ZoneTerrainProfile p, Vector2 pos, float nHeight, int prototype, float scale, System.Random rng)
        {
            float half = p.size * 0.5f;
            return new TreeInstance
            {
                position = new Vector3((pos.x + half) / p.size, nHeight, (pos.y + half) / p.size),
                prototypeIndex = prototype,
                widthScale = scale,
                heightScale = scale * Range(rng, 0.9f, 1.1f),
                rotation = (float)rng.NextDouble() * Mathf.PI * 2f,
                color = Color.white,
                lightmapColor = Color.white
            };
        }

        private static void AddPrototype(List<TreePrototype> list, string name)
        {
            var prefab = FoliageAssetBuilder.Variant(name) ?? LoadPrefab(GenericNature + name);
            if (prefab != null) list.Add(new TreePrototype { prefab = prefab });
        }

        private static Vector2 RandomPoint(ZoneTerrainProfile p, System.Random rng)
        {
            float half = p.size * 0.5f - 6f;
            return new Vector2(Range(rng, -half, half), Range(rng, -half, half));
        }

        // ---------------------------------------------------------------- grass & flowers (terrain details)

        public static void ApplyDetailSettings(ZoneTerrainProfile p, Terrain terrain)
        {
            terrain.drawTreesAndFoliage = true;
            terrain.detailObjectDistance = p.detailDistance;
            terrain.detailObjectDensity = 1f;
        }

        /// <summary>
        /// Grows grass and flowers as instanced detail meshes (wind-reactive variants). Unity culls them per patch
        /// and stops drawing past <see cref="ZoneTerrainProfile.detailDistance"/>. Details only grow where the splat
        /// is grass (never dirt, rock or town stone), above the waterline, and off every road, sidewalk, building and
        /// prop mesh in the scene, so run this after the town is built.
        /// </summary>
        public static void PaintDetails(ZoneTerrainProfile p, TerrainData data)
        {
            if (p.detailLayers == null || p.detailLayers.Count == 0)
            {
                FillDefaultDetailLayers(p);
                EditorUtility.SetDirty(p);
            }

            var prototypes = new List<DetailPrototype>();
            var layers = new List<ZoneTerrainProfile.DetailLayer>();
            foreach (var layer in p.detailLayers)
            {
                var prefab = FoliageAssetBuilder.Variant(layer.prefab);
                if (prefab == null) { Debug.LogWarning($"[ZoneTerrain] Detail prefab '{layer.prefab}' not found."); continue; }
                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = layer.minScale, maxWidth = layer.maxScale,
                    minHeight = layer.maxHeight > 0f ? layer.minHeight : layer.minScale,
                    maxHeight = layer.maxHeight > 0f ? layer.maxHeight : layer.maxScale,
                    noiseSpread = layer.noiseSpread,
                    healthyColor = Color.white, dryColor = Color.white,
                    alignToGround = 0.4f,
                    positionJitter = 1f,
                });
                layers.Add(layer);
            }

            int res = Mathf.Clamp(Mathf.ClosestPowerOfTwo(p.detailResolution), 64, 4096);
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(res, Mathf.Clamp(p.detailResolutionPerPatch, 8, 128));
            data.detailPrototypes = prototypes.ToArray();

            int aRes = data.alphamapResolution;
            var alpha = data.GetAlphamaps(0, 0, aRes, aRes);
            bool[,] blocked = MeshExclusionMask(p, res);
            var off = NoiseOffset(p.seed);
            var rng = new System.Random(p.seed + 71);
            float half = p.size * 0.5f;
            float cell = p.size / res;
            long total = 0;

            var maps = new int[layers.Count][,];
            for (int l = 0; l < layers.Count; l++) maps[l] = new int[res, res];

            for (int zi = 0; zi < res; zi++)
            {
                float nz = (zi + 0.5f) / res;
                float z = -half + p.size * nz;
                int az = Mathf.Min(aRes - 1, (int)(nz * aRes));
                for (int xi = 0; xi < res; xi++)
                {
                    if (blocked[zi, xi]) continue;
                    float nx = (xi + 0.5f) / res;
                    int ax = Mathf.Min(aRes - 1, (int)(nx * aRes));
                    float grass = alpha[az, ax, 0];
                    if (grass < p.detailGrassThreshold) continue;
                    float height = data.GetInterpolatedHeight(nx, nz) + p.baseHeight;
                    if (height < p.waterLevel + 0.35f) continue;

                    float x = -half + p.size * nx;
                    bool inLawn = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) < p.pavedHalfSize;
                    // Meadows vs. thinner turf, and a soft fade toward the grass edge.
                    // (Lawns are kept even, like a mown carpet.)
                    float meadow = inLawn ? 1f : 0.55f + 0.45f * Mathf.PerlinNoise(x * 0.012f + off.y, z * 0.012f + off.x);
                    float edge = Mathf.InverseLerp(p.detailGrassThreshold, 1f, grass);
                    float baseAmount = meadow * Mathf.Lerp(0.5f, 1f, edge) * cell * cell;

                    for (int l = 0; l < layers.Count; l++)
                    {
                        var layer = layers[l];
                        if (layer.lawn != inLawn) continue;
                        float n = Mathf.PerlinNoise(x * layer.patchScale + off.x + l * 17.3f, z * layer.patchScale + off.y + l * 9.1f);
                        float patch = layer.lawn ? 1f
                            : layer.patchThreshold <= 0f
                            ? 0.6f + 0.4f * n
                            : Smooth(layer.patchThreshold, layer.patchThreshold + 0.12f, n);
                        float expected = layer.density * patch * baseAmount;
                        if (expected <= 0f) continue;
                        int count = (int)expected + (rng.NextDouble() < expected - (int)expected ? 1 : 0);
                        maps[l][zi, xi] = count;
                        total += count;
                    }
                }
            }

            for (int l = 0; l < layers.Count; l++) data.SetDetailLayer(0, 0, l, maps[l]);
            EditorUtility.SetDirty(data);
            Debug.Log($"[ZoneTerrain] Painted {total:N0} grass/flower instances across {layers.Count} detail layers ({res}x{res} cells).");
        }

        /// <summary>
        /// Cells covered by road, sidewalk, building and prop meshes standing on the ground (town, outskirts, portal,
        /// rocks). Paving tiles have no colliders, so renderer bounds are used. Foliage is ignored.
        /// </summary>
        public static bool[,] MeshExclusionMask(ZoneTerrainProfile p, int res) => MeshExclusionMask(p, res, p.detailMeshMargin, true);

        /// <param name="smallPropsBlock">
        /// False = only ground-covering or large meshes block (roads, sidewalks, floors, buildings, walls, rocks). Small
        /// props (benches, planters, lamps, bins) let grass grow right up to them, which keeps lawns full.
        /// </param>
        public static bool[,] MeshExclusionMask(ZoneTerrainProfile p, int res, float margin, bool smallPropsBlock)
        {
            var mask = new bool[res, res];
            var terrain = Object.FindAnyObjectByType<Terrain>();
            float half = p.size * 0.5f;
            string[] roots = { StarterTownBuilder.TownRootName, StarterTownBuilder.OutskirtsRootName, StarterTownBuilder.PortalRootName,
                               EnvironmentRootName + "/Rocks" };
            int stamped = 0;
            foreach (var rootName in roots)
            {
                var root = GameObject.Find(rootName);
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(r.gameObject);
                    var src = instanceRoot != null ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot) : null;
                    if (src != null && FoliageAssetBuilder.IsFoliage(src.name)) continue;

                    Bounds b = r.bounds;
                    float ground = terrain != null ? terrain.SampleHeight(b.center) + terrain.transform.position.y : 0f;
                    if (b.min.y > ground + 1.2f) continue; // signs, awnings, floating dressing
                    if (!smallPropsBlock && !rootName.EndsWith("/Rocks"))
                    {
                        bool flat = b.size.y < 0.6f;                             // paving, road, floor tiles
                        float area = b.size.x * b.size.z;
                        bool large = area > 12f || (b.size.y > 3f && area > 4f); // buildings, walls, ruins (not poles)
                        if (!flat && !large) continue;
                    }
                    float m = margin;
                    int x0 = Mathf.Clamp(Mathf.FloorToInt((b.min.x - m + half) / p.size * res), 0, res - 1);
                    int x1 = Mathf.Clamp(Mathf.CeilToInt((b.max.x + m + half) / p.size * res), 0, res - 1);
                    int z0 = Mathf.Clamp(Mathf.FloorToInt((b.min.z - m + half) / p.size * res), 0, res - 1);
                    int z1 = Mathf.Clamp(Mathf.CeilToInt((b.max.z + m + half) / p.size * res), 0, res - 1);
                    for (int zi = z0; zi <= z1; zi++)
                    for (int xi = x0; xi <= x1; xi++)
                        mask[zi, xi] = true;
                    stamped++;
                }
            }
            if (stamped == 0) Debug.LogWarning("[ZoneTerrain] No town meshes found for the grass mask; run again after building the town.");
            return mask;
        }

        // ---------------------------------------------------------------- scene objects

        private static Terrain EnsureTerrainObject(ZoneTerrainProfile p, TerrainData data)
        {
            var env = GetOrCreateRoot(EnvironmentRootName);
            Terrain terrain = null;
            var existing = env.transform.Find(TerrainObjectName);
            if (existing != null) terrain = existing.GetComponent<Terrain>();
            if (terrain == null)
            {
                var go = Terrain.CreateTerrainGameObject(data);
                go.name = TerrainObjectName;
                go.transform.SetParent(env.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Create Zone01 terrain");
                terrain = go.GetComponent<Terrain>();
            }

            terrain.terrainData = data;
            terrain.GetComponent<TerrainCollider>().terrainData = data;
            terrain.transform.position = new Vector3(-p.size * 0.5f, p.baseHeight, -p.size * 0.5f);
            terrain.gameObject.layer = LayerMask.NameToLayer(GroundLayerName);
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 250f;
            terrain.treeDistance = 320f;
            terrain.treeBillboardDistance = 320f;
            terrain.treeMaximumFullLODCount = 600;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            ApplyDetailSettings(p, terrain);
            GameObjectUtility.SetStaticEditorFlags(terrain.gameObject,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return terrain;
        }

        /// <summary>One opaque water sheet under the whole map; the terrain hides it except in channels and lakes.</summary>
        private static void EnsureWater(ZoneTerrainProfile p)
        {
            var env = GetOrCreateRoot(EnvironmentRootName);
            var t = env.transform.Find("Water");
            GameObject water;
            if (t == null)
            {
                water = new GameObject("Water");
                water.transform.SetParent(env.transform, false);
                Undo.RegisterCreatedObjectUndo(water, "Create water");
            }
            else water = t.gameObject;

            water.layer = LayerMask.NameToLayer(WaterLayerName);
            water.transform.position = new Vector3(0f, p.waterLevel, 0f);
            water.transform.localScale = new Vector3(p.size / 10f, 1f, p.size / 10f);

            var filter = GetOrAdd<MeshFilter>(water);
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("New-Plane.fbx");
            var renderer = GetOrAdd<MeshRenderer>(water);
            renderer.sharedMaterial = WaterMaterial();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var box = GetOrAdd<BoxCollider>(water);
            // A solid volume down past the deepest bed, so the NavMesh can't find walkable riverbed under the water.
            float depth = Mathf.Max(p.waterLevel - Mathf.Min(p.riverBedHeight, p.lakeBedHeight) + 2f, 1f);
            box.size = new Vector3(10f, depth, 10f);
            box.center = new Vector3(0f, -depth * 0.5f, 0f);

            // The water surface is not walkable...
            var modifier = GetOrAdd<NavMeshModifier>(water);
            modifier.overrideArea = true;
            modifier.area = NotWalkableArea;

            // ...and neither is anything under it (riverbeds, lake floors), which a collider alone doesn't remove.
            var volume = GetOrAdd<NavMeshModifierVolume>(water);
            volume.area = NotWalkableArea;
            volume.size = new Vector3(10f, depth, 10f);
            volume.center = new Vector3(0f, -depth * 0.5f - 0.05f, 0f);
            GameObjectUtility.SetStaticEditorFlags(water, StaticEditorFlags.BatchingStatic);
        }

        // ---------------------------------------------------------------- anime water surface

        private const string WaterSurfaceName = "Water Surface";
        private const string AnimeWaterMaterialPath = ZoneFolder + "/M_AnimeWater_Zone01.mat";
        private const string AnimeWaterSourceMaterial = "Assets/_ThirdParty/KumaBeer/Stylized anime water/Materials/anime water.mat";
        private const string WaterMeshPath = ZoneFolder + "/Zone01_WaterMesh.asset";
        private const string WaterFlowMapPath = ZoneFolder + "/T_WaterFlow_Zone01.png";
        private const float WaterCell = 4f;          // metres per water mesh quad
        private const float WaterUvMetres = 12f;     // metres per texture tile (matches the package's demo plane)
        private const int FlowMapResolution = 512;

        [MenuItem("Krytical Nova/Zone/Rebuild Water (Anime Water + River Flow)")]
        public static void RebuildWaterMenu()
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null) { Debug.LogError("[ZoneTerrain] Generate the terrain first."); return; }
            var profile = LoadOrCreateProfile();
            EnsureWater(profile);
            EnsureWaterSurface(profile, data, true);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>
        /// Visible water: one mesh covering only where the terrain dips under the waterline (rivers and lakes), shaded
        /// with KryticalNova/Anime Water. A baked flow map makes rivers run downstream while lakes stay still. The
        /// "Water" object keeps the collider and NavMesh modifiers; its flat plane renderer is switched off.
        /// </summary>
        private static void EnsureWaterSurface(ZoneTerrainProfile p, TerrainData data, bool rebuild)
        {
            var env = GetOrCreateRoot(EnvironmentRootName);
            var water = env.transform.Find("Water");
            if (water != null && water.TryGetComponent<MeshRenderer>(out var flat)) flat.enabled = false;

            var t = env.transform.Find(WaterSurfaceName);
            GameObject surface;
            if (t == null)
            {
                surface = new GameObject(WaterSurfaceName);
                surface.transform.SetParent(env.transform, false);
                Undo.RegisterCreatedObjectUndo(surface, "Create water surface");
                rebuild = true;
            }
            else surface = t.gameObject;

            surface.layer = LayerMask.NameToLayer(WaterLayerName);
            surface.transform.SetPositionAndRotation(new Vector3(0f, p.waterLevel, 0f), Quaternion.identity);
            surface.transform.localScale = Vector3.one;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WaterMeshPath);
            if (rebuild || mesh == null) mesh = BuildWaterMesh(p, data);
            var flowMap = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterFlowMapPath);
            if (rebuild || flowMap == null) flowMap = BakeWaterFlowMap(p);

            GetOrAdd<MeshFilter>(surface).sharedMesh = mesh;
            var renderer = GetOrAdd<MeshRenderer>(surface);
            renderer.sharedMaterial = AnimeWaterMaterial(p, flowMap);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            // Not navigation-relevant; the "Water" object carries the colliders and NavMesh modifiers.
            GameObjectUtility.SetStaticEditorFlags(surface, 0);
        }

        /// <summary>A grid of 4 m quads kept only where the terrain is under (or just above) the waterline.</summary>
        private static Mesh BuildWaterMesh(ZoneTerrainProfile p, TerrainData data)
        {
            int cells = Mathf.CeilToInt(p.size / WaterCell);
            float half = p.size * 0.5f;
            float threshold = p.waterLevel + 0.5f;

            // Which grid corners sit under water (with a one-cell margin so shorelines are covered).
            var under = new bool[cells + 1, cells + 1];
            for (int z = 0; z <= cells; z++)
            for (int x = 0; x <= cells; x++)
            {
                float nx = Mathf.Clamp01((float)x / cells), nz = Mathf.Clamp01((float)z / cells);
                under[z, x] = data.GetInterpolatedHeight(nx, nz) + p.baseHeight < threshold;
            }

            var index = new int[cells + 1, cells + 1];
            for (int z = 0; z <= cells; z++) for (int x = 0; x <= cells; x++) index[z, x] = -1;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int Vertex(int x, int z)
            {
                if (index[z, x] >= 0) return index[z, x];
                float wx = -half + x * WaterCell, wz = -half + z * WaterCell;
                index[z, x] = verts.Count;
                verts.Add(new Vector3(wx, 0f, wz));
                uvs.Add(new Vector2(wx, wz) / WaterUvMetres);
                return index[z, x];
            }

            for (int z = 0; z < cells; z++)
            for (int x = 0; x < cells; x++)
            {
                bool wet = false;
                for (int dz = -1; dz <= 2 && !wet; dz++)
                for (int dx = -1; dx <= 2 && !wet; dx++)
                {
                    int cx = x + dx, cz = z + dz;
                    if (cx >= 0 && cz >= 0 && cx <= cells && cz <= cells && under[cz, cx]) wet = true;
                }
                if (!wet) continue;
                int a = Vertex(x, z), b = Vertex(x + 1, z), c = Vertex(x + 1, z + 1), d = Vertex(x, z + 1);
                tris.Add(a); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(b);
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WaterMeshPath);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = "Zone01_WaterMesh" };
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            var normals = new Vector3[verts.Count];
            var tangents = new Vector4[verts.Count];
            for (int i = 0; i < verts.Count; i++) { normals[i] = Vector3.up; tangents[i] = new Vector4(1f, 0f, 0f, -1f); }
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.RecalculateBounds();
            // Room for the shader's vertex wave offset so the surface isn't culled at grazing angles.
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, 4f, 0f));
            mesh.bounds = bounds;
            if (create) AssetDatabase.CreateAsset(mesh, WaterMeshPath);
            EditorUtility.SetDirty(mesh);
            Debug.Log($"[ZoneTerrain] Water mesh: {verts.Count:N0} vertices, {tris.Count / 3:N0} triangles.");
            return mesh;
        }

        /// <summary>
        /// RG = downstream direction (each river runs from its first point to its last), B = current speed (0 in lakes
        /// and still water). Sampled in world XZ by KryticalNova/Anime Water.
        /// </summary>
        private static Texture2D BakeWaterFlowMap(ZoneTerrainProfile p)
        {
            int res = FlowMapResolution;
            float half = p.size * 0.5f;
            var off = NoiseOffset(p.seed);
            var dir = new Vector2[res * res];
            var speed = new float[res * res];

            for (int zi = 0; zi < res; zi++)
            for (int xi = 0; xi < res; xi++)
            {
                var pos = new Vector2(-half + p.size * (xi + 0.5f) / res, -half + p.size * (zi + 0.5f) / res);
                float best = float.MaxValue;
                Vector2 tangent = Vector2.zero;
                foreach (var line in p.rivers)
                {
                    for (int i = 0; i + 1 < line.points.Count; i++)
                    {
                        Vector2 a = line.points[i], b = line.points[i + 1];
                        Vector2 ab = b - a;
                        float t = Mathf.Clamp01(Vector2.Dot(pos - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-4f));
                        float d = Vector2.Distance(pos, a + ab * t);
                        if (d < best) { best = d; tangent = ab.normalized; }
                    }
                }

                // Full current mid-channel, easing to still water at the banks.
                float s = 1f - Smooth(p.riverWidth * 0.3f, p.riverWidth * 0.5f + p.riverBankWidth * 0.5f, best);
                // The current dies out as a river opens into a lake.
                for (int i = 0; i < p.lakes.Count; i++)
                    s *= Smooth(-p.lakes[i].radius * 0.25f, 18f, LakeDistance(p.lakes[i], pos, off));
                dir[zi * res + xi] = tangent;
                speed[zi * res + xi] = s;
            }

            // Soften junctions and bends so the flow turns smoothly.
            for (int pass = 0; pass < 2; pass++)
            {
                var d2 = new Vector2[dir.Length];
                for (int zi = 0; zi < res; zi++)
                for (int xi = 0; xi < res; xi++)
                {
                    Vector2 sum = Vector2.zero;
                    for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = Mathf.Clamp(xi + dx, 0, res - 1), z = Mathf.Clamp(zi + dz, 0, res - 1);
                        sum += dir[z * res + x] * (0.1f + speed[z * res + x]);
                    }
                    d2[zi * res + xi] = sum.sqrMagnitude > 1e-6f ? sum.normalized : dir[zi * res + xi];
                }
                dir = d2;
            }

            var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[res * res];
            float max = Mathf.Max(p.riverFlowSpeed, 0f);
            for (int i = 0; i < pixels.Length; i++)
            {
                Vector2 d = dir[i];
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt((d.x * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt((d.y * 0.5f + 0.5f) * 255f),
                    (byte)Mathf.RoundToInt(Mathf.Clamp01(speed[i] * max) * 255f), 255);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            System.IO.File.WriteAllBytes(WaterFlowMapPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(WaterFlowMapPath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(WaterFlowMapPath);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(WaterFlowMapPath);
        }

        private static Material AnimeWaterMaterial(ZoneTerrainProfile p, Texture2D flowMap)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(AnimeWaterMaterialPath);
            if (mat == null)
            {
                var shader = Shader.Find("KryticalNova/Anime Water");
                var source = AssetDatabase.LoadAssetAtPath<Material>(AnimeWaterSourceMaterial);
                if (shader == null) { Debug.LogError("[ZoneTerrain] KryticalNova/Anime Water shader missing; falling back to the old water."); return WaterMaterial(); }
                mat = source != null ? new Material(source) : new Material(shader);
                mat.shader = shader;
                mat.EnableKeyword("_USEREFLECTION_ON");
                // Slightly deeper, more saturated teal for the comic palette.
                mat.SetColor("_Maincolor", new Color(0.18f, 0.56f, 0.62f, 1f));
                mat.SetColor("_DepthColor", new Color(0.0f, 0.12f, 0.32f, 1f));
                mat.SetColor("_Foamcolor", new Color(0.95f, 0.97f, 1f, 0f));
                AssetDatabase.CreateAsset(mat, AnimeWaterMaterialPath);
            }
            mat.SetTexture("_FlowMap", flowMap);
            float half = p.size * 0.5f;
            mat.SetVector("_FlowMapBounds", new Vector4(-half, -half, p.size, p.size));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Material WaterMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.16f, 0.52f, 0.66f));
            mat.SetFloat("_Smoothness", 0.88f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 1f);
            AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            return mat;
        }

        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();
        }

        public static GameObject GetOrCreateRoot(string name)
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go;
        }

        // ---------------------------------------------------------------- math

        private static GameObject LoadPrefab(string pathWithoutExtension)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pathWithoutExtension + ".prefab");
            if (prefab == null) Debug.LogWarning($"[ZoneTerrain] Missing prefab {pathWithoutExtension}.prefab");
            return prefab;
        }

        public static float DistanceToLines(List<ZoneTerrainProfile.Polyline> lines, Vector2 p)
        {
            float best = float.MaxValue;
            for (int l = 0; l < lines.Count; l++)
            {
                var pts = lines[l].points;
                for (int i = 0; i + 1 < pts.Count; i++)
                    best = Mathf.Min(best, DistanceToSegment(p, pts[i], pts[i + 1]));
            }
            return best;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float x, float y)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < 4; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.03f;
            }
            return sum / norm;
        }

        private static Vector2 NoiseOffset(int seed)
        {
            var rng = new System.Random(seed);
            return new Vector2((float)rng.NextDouble() * 1000f, (float)rng.NextDouble() * 1000f);
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
