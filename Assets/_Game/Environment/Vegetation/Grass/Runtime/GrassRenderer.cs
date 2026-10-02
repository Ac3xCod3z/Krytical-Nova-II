using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// GPU grass. For each rendering camera (game camera, and the Scene view in the editor) a compute shader builds
    /// this frame's grass from a world-snapped grid around the camera: baked mask × biome × exclusion volumes ×
    /// distance LOD × frustum. Two passes share the same rules: real clumps near the camera, and cheap far tufts
    /// that take over across a hand-off band so the field never ends in a hard line. Every (type, pass) pair is one
    /// Graphics.RenderMeshIndirect call reading a region of one shared instance buffer. No per-blade GameObjects,
    /// no CPU loops over blades, nothing stored between frames; presentation only (nothing is networked).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class GrassRenderer : MonoBehaviour
    {
        public const int MaxTypes = 8;          // GrassGenerate.compute MAX_TYPES
        public const int InstanceStride = 32;   // GrassCommon.hlsl GrassInstance
        private const int MaxVolumes = 16;
        private const int DrawCount = MaxTypes * 2; // near + far per type

        [SerializeField] private GrassRuleSet rules;
        [SerializeField] private Terrain terrain;
        [SerializeField] private ComputeShader generateShader;
        [SerializeField] private Material grassMaterial;
        [Tooltip("Draw in the Scene view while editing (handy for tuning; off saves editor GPU).")]
        [SerializeField] private bool drawInSceneView = true;

        public static GrassRenderer Instance { get; private set; }
        public GrassRuleSet Rules => rules;

        /// <summary>0 off, 1 placement (green dense → yellow sparse, red = no grass allowed), 2 LOD bands (blue = far tufts), 3 biomes.</summary>
        public static int DebugMode
        {
            get => debugMode;
            set { debugMode = Mathf.Clamp(value, 0, 3); Shader.SetGlobalInt(DebugId, debugMode); }
        }
        private static int debugMode;

        /// <summary>Runtime quality overrides (NaN = use the rule set). Not saved to the asset.</summary>
        public static float DensityOverride = float.NaN;
        public static float DistanceOverride = float.NaN;
        private float Density => float.IsNaN(DensityOverride) ? rules.densityScale : DensityOverride;
        private float NearDistance => float.IsNaN(DistanceOverride) ? rules.maxDistance : DistanceOverride;

        private static readonly int DebugId = Shader.PropertyToID("_KN_GrassDebug");
        private static readonly int HeightmapId = Shader.PropertyToID("_Heightmap");
        private static readonly int MaskId = Shader.PropertyToID("_GrassMask");
        private static readonly int TerrainPosId = Shader.PropertyToID("_TerrainPos");
        private static readonly int TerrainSizeId = Shader.PropertyToID("_TerrainSize");
        private static readonly int GridOriginId = Shader.PropertyToID("_GridOrigin");
        private static readonly int GridId = Shader.PropertyToID("_Grid");
        private static readonly int CamPosId = Shader.PropertyToID("_CamPos");
        private static readonly int LodId = Shader.PropertyToID("_Lod");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int FarParamsId = Shader.PropertyToID("_FarParams");
        private static readonly int FrustumId = Shader.PropertyToID("_Frustum");
        private static readonly int BiomeParamsId = Shader.PropertyToID("_BiomeParams");
        private static readonly int BiomeSlotsId = Shader.PropertyToID("_BiomeSlots");
        private static readonly int TypeAId = Shader.PropertyToID("_TypeA");
        private static readonly int TypeBId = Shader.PropertyToID("_TypeB");
        private static readonly int TypeCountId = Shader.PropertyToID("_TypeCount");
        private static readonly int VolumesId = Shader.PropertyToID("_Volumes");
        private static readonly int VolumeCountId = Shader.PropertyToID("_VolumeCount");
        private static readonly int DebugModeId = Shader.PropertyToID("_DebugMode");
        private static readonly int DrawBaseId = Shader.PropertyToID("_DrawBase");
        private static readonly int RegionBaseId = Shader.PropertyToID("_RegionBase");
        private static readonly int RegionCapacityId = Shader.PropertyToID("_RegionCapacity");
        private static readonly int ClampCapsId = Shader.PropertyToID("_ClampCaps");
        private static readonly int ArgsId = Shader.PropertyToID("_Args");
        private static readonly int InstancesId = Shader.PropertyToID("_Instances");
        private static readonly int OffsetId = Shader.PropertyToID("_InstanceOffset");
        private static readonly int CapacityId = Shader.PropertyToID("_InstanceCapacity");
        private static readonly int RootColorId = Shader.PropertyToID("_RootColor");
        private static readonly int MidColorId = Shader.PropertyToID("_MidColor");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int PatchTintId = Shader.PropertyToID("_PatchTint");
        private static readonly int WetTintId = Shader.PropertyToID("_WetTint");
        private static readonly int GrassParamsId = Shader.PropertyToID("_GrassParams");
        private static readonly int GrassMotionId = Shader.PropertyToID("_GrassMotion");

        private readonly List<GrassType> types = new List<GrassType>(MaxTypes);
        private readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[DrawCount];
        private GraphicsBuffer instances;
        private GraphicsBuffer argsBuffer;   // IndirectArguments: what RenderMeshIndirect reads
        private GraphicsBuffer countBuffer;  // Raw twin the compute shader counts into, copied to argsBuffer on the GPU
        private uint[] argsTemplate;
        private int generateKernel = -1, clampKernel = -1;
        private int nearCapacity, farCapacity;

        private readonly Vector4[] biomeParams = new Vector4[GrassRuleSet.MaxBiomes];
        private readonly Vector4[] biomeSlots = new Vector4[GrassRuleSet.MaxBiomes * GrassBiome.MaxEntries];
        private readonly Vector4[] typeA = new Vector4[MaxTypes];
        private readonly Vector4[] typeB = new Vector4[MaxTypes];
        private readonly Vector4[] frustum = new Vector4[6];
        private readonly Plane[] planes = new Plane[6];
        private readonly Vector4[] volumes = new Vector4[MaxVolumes * 2];

        private void OnEnable()
        {
            Instance = this;
            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[Grass] Compute shaders unsupported; GPU grass disabled.");
                enabled = false;
                return;
            }
            Shader.SetGlobalInt(DebugId, debugMode);
            Rebuild();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Release();
            if (Instance == this) Instance = null;
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled && argsBuffer != null) Rebuild();
        }

        /// <summary>(Re)reads the rule set and (re)allocates GPU buffers. Call after editing grass assets.</summary>
        public void Rebuild()
        {
            Release();
            if (rules == null || generateShader == null || grassMaterial == null) return;
            if (terrain == null) terrain = Terrain.activeTerrain;

            types.Clear();
            System.Array.Clear(biomeSlots, 0, biomeSlots.Length);
            for (int b = 0; b < GrassRuleSet.MaxBiomes; b++)
            {
                var biome = b < rules.biomes.Count ? rules.biomes[b] : null;
                int count = 0;
                if (biome != null)
                {
                    foreach (var e in biome.entries)
                    {
                        if (e.type == null || e.type.mesh == null || count >= GrassBiome.MaxEntries) continue;
                        int t = types.IndexOf(e.type);
                        if (t < 0)
                        {
                            if (types.Count >= MaxTypes) { Debug.LogWarning($"[Grass] More than {MaxTypes} grass types; '{e.type.name}' skipped."); continue; }
                            types.Add(e.type);
                            t = types.Count - 1;
                        }
                        biomeSlots[b * GrassBiome.MaxEntries + count] = new Vector4(t, e.weight, e.patchiness, 1f / Mathf.Max(e.patchSize, 1f));
                        count++;
                    }
                }
                biomeParams[b] = new Vector4(biome != null ? biome.density : 0f, count, 0f, 0f);
            }

            nearCapacity = Mathf.Max(1024, rules.capacityPerType);
            farCapacity = Mathf.Max(1024, rules.farCapacityPerType);
            instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxTypes * (nearCapacity + farCapacity), InstanceStride);
            // Indirect-args buffers can't also be raw, so the compute shader counts into a raw twin with the same
            // layout and the GPU copies it across each frame.
            argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.CopyDestination,
                DrawCount, GraphicsBuffer.IndirectDrawIndexedArgs.size);
            countBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopySource, DrawCount * 5, sizeof(uint));
            argsTemplate = new uint[DrawCount * 5];

            Mesh farMesh = rules.farMesh;
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                typeA[i] = new Vector4(type.density, type.heightRange.x, type.heightRange.y, type.MeshHeight);
                typeB[i] = new Vector4(type.widthRange.x, type.widthRange.y, farMesh != null ? Mathf.Max(farMesh.bounds.max.y, 0.01f) : 1f, 0f);
                SetupDraw(i, type, type.mesh, i * nearCapacity, nearCapacity, type.MeshHeight);
                if (farMesh != null)
                    SetupDraw(MaxTypes + i, type, farMesh, MaxTypes * nearCapacity + i * farCapacity, farCapacity, Mathf.Max(farMesh.bounds.max.y, 0.01f));
            }
            generateKernel = generateShader.FindKernel("Generate");
            clampKernel = generateShader.FindKernel("ClampArgs");
        }

        private void SetupDraw(int draw, GrassType type, Mesh mesh, int offset, int capacity, float meshHeight)
        {
            argsTemplate[draw * 5 + 0] = mesh.GetIndexCount(0);
            argsTemplate[draw * 5 + 1] = 0;
            argsTemplate[draw * 5 + 2] = mesh.GetIndexStart(0);
            argsTemplate[draw * 5 + 3] = mesh.GetBaseVertex(0);
            argsTemplate[draw * 5 + 4] = 0;

            var block = blocks[draw] ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetBuffer(InstancesId, instances);
            block.SetInt(OffsetId, offset);
            block.SetInt(CapacityId, capacity);
            block.SetColor(RootColorId, type.rootColor.linear);
            block.SetColor(MidColorId, type.midColor.linear);
            block.SetColor(TipColorId, type.tipColor.linear);
            var patch = type.patchTint.linear;
            var wet = type.wetTint.linear;
            block.SetColor(PatchTintId, new Color(patch.r, patch.g, patch.b, type.patchTint.a));
            block.SetColor(WetTintId, new Color(wet.r, wet.g, wet.b, type.wetTint.a));
            block.SetVector(GrassParamsId, new Vector4(type.midPoint, type.brightnessVariation, type.rootOcclusion, type.normalUp));
            block.SetVector(GrassMotionId, new Vector4(type.windStrength, type.flutter, type.playerBend, meshHeight));
        }

        private void Release()
        {
            argsBuffer?.Release();
            argsBuffer = null;
            countBuffer?.Release();
            countBuffer = null;
            instances?.Release();
            instances = null;
        }

        private bool ShouldDraw(Camera cam)
        {
            if (cam.cameraType == CameraType.SceneView) return drawInSceneView;
            if (cam.cameraType != CameraType.Game) return false;
            // Only the main gameplay camera (not the minimap / UI / preview cameras).
            return cam.CompareTag("MainCamera");
        }

        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (argsBuffer == null || types.Count == 0 || rules.mask == null || terrain == null || !ShouldDraw(cam)) return;
            var data = terrain.terrainData;
            var heightmap = data.heightmapTexture;
            if (heightmap == null) return;

            Vector3 camPos = cam.transform.position;
            float nearMax = NearDistance;
            bool far = rules.farGrass && rules.farMesh != null;
            // Without far grass the real grass still thins out over its last 15% instead of stopping dead.
            float handOffStart = far ? Mathf.Min(rules.handOffBand.x, nearMax - 1f) : nearMax * 0.85f;
            float farMax = far ? Mathf.Max(rules.farDistance, nearMax + 10f) : nearMax;

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            for (int i = 0; i < 6; i++) frustum[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);

            int volumeCount = 0;
            foreach (var v in GrassExclusionVolume.Active)
            {
                if (volumeCount >= MaxVolumes) break;
                if ((v.transform.position - camPos).sqrMagnitude > (farMax + 30f) * (farMax + 30f)) continue;
                v.Pack(out volumes[volumeCount * 2], out volumes[volumeCount * 2 + 1]);
                volumeCount++;
            }

            Vector3 tPos = terrain.transform.position;
            Vector3 tSize = data.size;
            var cs = generateShader;
            countBuffer.SetData(argsTemplate);
            cs.SetTexture(generateKernel, HeightmapId, heightmap);
            cs.SetTexture(generateKernel, MaskId, rules.mask);
            cs.SetBuffer(generateKernel, InstancesId, instances);
            cs.SetBuffer(generateKernel, ArgsId, countBuffer);
            cs.SetVector(TerrainPosId, new Vector4(tPos.x, tPos.y, tPos.z, tSize.y * 2f));
            cs.SetVector(TerrainSizeId, new Vector4(tSize.x, tSize.z, data.heightmapResolution, rules.mask.width));
            cs.SetVector(CamPosId, camPos);
            cs.SetVector(LodId, new Vector4(rules.lodDistances.x, rules.lodDistances.y, rules.lodKeep.x, rules.lodKeep.y));
            cs.SetVector(FadeId, new Vector4(handOffStart, nearMax, farMax, rules.farKeepAtEnd));
            cs.SetVectorArray(FrustumId, frustum);
            cs.SetVectorArray(BiomeParamsId, biomeParams);
            cs.SetVectorArray(BiomeSlotsId, biomeSlots);
            cs.SetVectorArray(TypeAId, typeA);
            cs.SetVectorArray(TypeBId, typeB);
            cs.SetInt(TypeCountId, types.Count);
            cs.SetVectorArray(VolumesId, volumes);
            cs.SetInt(VolumeCountId, volumeCount);
            cs.SetInt(DebugModeId, debugMode);

            // Near pass: real clumps.
            Dispatch(cs, camPos, rules.cellSize, nearMax, false, 0, 0, nearCapacity);
            // Far pass: cheap tufts on a coarse grid, same rules.
            if (far) Dispatch(cs, camPos, rules.farCellSize, farMax, true, MaxTypes, MaxTypes * nearCapacity, farCapacity);

            cs.SetBuffer(clampKernel, ArgsId, countBuffer);
            cs.SetInts(ClampCapsId, nearCapacity, farCapacity, MaxTypes, 0);
            cs.Dispatch(clampKernel, 1, 1, 1);
            Graphics.CopyBuffer(countBuffer, argsBuffer);

            var nearBounds = new Bounds(camPos, new Vector3(nearMax * 2f, tSize.y + 20f, nearMax * 2f));
            var farBounds = new Bounds(camPos, new Vector3(farMax * 2f, tSize.y + 20f, farMax * 2f));
            for (int i = 0; i < types.Count; i++)
            {
                Draw(cam, types[i].mesh, i, nearBounds);
                if (far) Draw(cam, rules.farMesh, MaxTypes + i, farBounds);
            }
        }

        private void Dispatch(ComputeShader cs, Vector3 camPos, float cell, float range, bool far, int drawBase, int regionBase, int capacity)
        {
            int dim = Mathf.CeilToInt(range * 2f / cell) + 1;
            var origin = new Vector2Int(Mathf.FloorToInt((camPos.x - range) / cell), Mathf.FloorToInt((camPos.z - range) / cell));
            cs.SetInts(GridOriginId, origin.x, origin.y);
            cs.SetVector(GridId, new Vector4(cell, dim, range, Density));
            cs.SetVector(FarParamsId, new Vector4(far ? 1f : 0f, rules.farWidthScale, 0f, 0f));
            cs.SetInt(DrawBaseId, drawBase);
            cs.SetInt(RegionBaseId, regionBase);
            cs.SetInt(RegionCapacityId, capacity);
            int groups = Mathf.CeilToInt(dim / 8f);
            cs.Dispatch(generateKernel, groups, groups, 1);
        }

        private void Draw(Camera cam, Mesh mesh, int draw, Bounds bounds)
        {
            var rp = new RenderParams(grassMaterial)
            {
                camera = cam,
                worldBounds = bounds,
                matProps = blocks[draw],
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = true,
                layer = gameObject.layer,
            };
            Graphics.RenderMeshIndirect(rp, mesh, argsBuffer, 1, draw);
        }

        /// <summary>Synchronous GPU readback of the last camera's counts (debug command only; stalls a frame).</summary>
        public string Stats()
        {
            if (argsBuffer == null) return "Grass not initialised.";
            var counts = new uint[DrawCount * 5];
            argsBuffer.GetData(counts);
            var sb = new System.Text.StringBuilder();
            long tris = 0;
            for (int i = 0; i < types.Count; i++)
            {
                uint near = counts[i * 5 + 1], farCount = counts[(MaxTypes + i) * 5 + 1];
                long t = near * (long)types[i].mesh.GetIndexCount(0) / 3;
                long tf = rules.farMesh != null ? farCount * (long)rules.farMesh.GetIndexCount(0) / 3 : 0;
                tris += t + tf;
                sb.Append(types[i].name).Append(": ").Append(near.ToString("N0")).Append(" clumps (").Append(t.ToString("N0"))
                  .Append(" tris) + ").Append(farCount.ToString("N0")).Append(" far tufts (").Append(tf.ToString("N0")).AppendLine(" tris)");
                if (near >= nearCapacity || farCount >= farCapacity) sb.AppendLine("  (at capacity — raise the capacity or lower density)");
            }
            sb.Append("Total ").Append(tris.ToString("N0")).Append(" tris  ·  real ").Append(NearDistance.ToString("0"))
              .Append(" m, far ").Append(rules.farDistance.ToString("0")).Append(" m  ·  density x").Append(Density.ToString("0.##"));
            return sb.ToString();
        }
    }
}
