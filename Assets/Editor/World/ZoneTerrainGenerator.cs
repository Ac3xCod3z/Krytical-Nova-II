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
        public const string ZoneFolder = "Assets/GameData/Zones/Zone01";
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
        private static readonly string[] GroundCoverPrefabs =
        {
            "SM_Gen_Env_Bush_01", "SM_Gen_Env_Bush_02", "SM_Gen_Env_Bush_03", "SM_Gen_Env_Bush_04",
            "SM_Gen_Env_Grass_04", "SM_Gen_Env_Grass_05", "SM_Gen_Env_Fern_02", "SM_Gen_Env_Fern_03",
            "SM_Gen_Env_Flowers_01", "SM_Gen_Env_Flowers_04", "SM_Gen_Env_Flowers_05"
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
            }
            else
            {
                Debug.Log("[ZoneTerrain] Terrain data exists; keeping hand edits. Use Force Regenerate to rebuild it.");
            }

            var terrain = EnsureTerrainObject(profile, data);
            EnsureWater(profile);
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

        /// <summary>The reference map's layout: origin is the town centre, +Z is north.</summary>
        private static void FillDefaultLayout(ZoneTerrainProfile p)
        {
            p.rivers.Add(Line("North river (NE source → NW lake)",
                310, 178, 230, 152, 140, 130, 60, 112, 0, 108, -60, 112, -140, 142, -205, 188, -236, 214));
            p.rivers.Add(Line("East branch (→ SE lake)",
                140, 130, 182, 78, 202, 0, 196, -84, 182, -148));
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-236, 218), radius = 40f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-228, 38), radius = 34f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(-72, -228), radius = 40f });
            p.lakes.Add(new ZoneTerrainProfile.Lake { center = new Vector2(176, -176), radius = 36f });

            p.paths.Add(Line("North gate → bridge → portal", 0, 48, 0, 150, 70, 178, 140, 205, 215, 232));
            p.paths.Add(Line("South gate", 0, -48, 0, -120, -18, -170));
            p.paths.Add(Line("East gate", 48, 0, 120, 4, 168, 10));
            p.paths.Add(Line("West gate", -48, 0, -120, -6, -172, -2));
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
                    var pos = new Vector2(x, z);
                    float height = data.GetInterpolatedHeight(nx, nz) + p.baseHeight;
                    float slope = data.GetSteepness(nx, nz);

                    float rock = Smooth(p.rockSlopeStart, p.rockSlopeFull, slope);
                    float path = 1f - Smooth(p.pathWidth * 0.5f - 1f, p.pathWidth * 0.5f + 2.5f, DistanceToLines(p.paths, pos));
                    float pad = 1f - Smooth(0f, 5f, Vector2.Distance(pos, p.portalCenter) - p.portalPadRadius);
                    float wet = 1f - Smooth(p.waterLevel + 0.6f, p.waterLevel + 1.6f, height);
                    float town = 1f - Smooth(-3f, 1f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - (p.townHalfSize - 8f));
                    float patchy = Smooth(0.55f, 0.75f, Mathf.PerlinNoise(x * 0.05f + off.x, z * 0.05f + off.y)) * 0.35f;

                    float dirt = Mathf.Max(Mathf.Max(path, pad), Mathf.Max(wet, patchy));
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
                metallic = 0f
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
            var prefab = LoadPrefab(GenericNature + name);
            if (prefab != null) list.Add(new TreePrototype { prefab = prefab });
        }

        private static Vector2 RandomPoint(ZoneTerrainProfile p, System.Random rng)
        {
            float half = p.size * 0.5f - 6f;
            return new Vector2(Range(rng, -half, half), Range(rng, -half, half));
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
