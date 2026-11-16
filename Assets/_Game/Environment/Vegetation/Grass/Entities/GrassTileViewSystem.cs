using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace KryticalNova.Vegetation.Grass.Entities
{
    /// <summary>
    /// Main thread, client and presentation worlds only. Makes the tiles once from the scene's grass (its mask, terrain and
    /// rule set), remakes them when the grass changes (another map), and each frame hands the jobs the game camera and the
    /// ranges in force. It also keeps the debug line (grass.tiles): tile counts, and the milliseconds of the main thread
    /// and of the worker jobs, averaged since the line was last read.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.Presentation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class GrassTileViewSystem : SystemBase
    {
        const float TallestGrass = 2f;      // metres above the ground a tile's box reaches (the frustum test)
        const int SamplesPerTile = 8;       // mask texels read per tile side when the tiles are made

        GrassRenderer m_For;
        Entity m_ViewEntity, m_StatsEntity;
        EntityQuery m_AllTiles, m_TileChunks;
        readonly Plane[] m_Planes = new Plane[6];
        readonly Stopwatch m_Watch = new Stopwatch();
        ProfilerRecorder m_Cull, m_Stream, m_Schedule;
        double m_MainMs, m_WorkerMs;
        int m_Frames;

        protected override void OnCreate()
        {
            m_ViewEntity = EntityManager.CreateSingleton(new GrassView());
            m_StatsEntity = EntityManager.CreateSingleton(new GrassTileStats());
            m_AllTiles = GetEntityQuery(ComponentType.ReadOnly<GrassTile>());
            m_TileChunks = GetEntityQuery(ComponentType.ReadOnly<GrassTile>(), ComponentType.ChunkComponent<GrassChunkBounds>());
            m_Cull = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GrassTileMarkers.Cull);
            m_Stream = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GrassTileMarkers.Stream);
            m_Schedule = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, GrassTileMarkers.Schedule);
        }

        protected override void OnDestroy()
        {
            m_Cull.Dispose();
            m_Stream.Dispose();
            m_Schedule.Dispose();
        }

        protected override void OnUpdate()
        {
            m_Watch.Restart();
            GrassRenderer grass = GrassRenderer.Instance;
            bool on = grass != null && grass.isActiveAndEnabled && grass.Rules != null && grass.Rules.ecsTiles;
            if (!on || grass != m_For)
            {
                ClearTiles();
                m_For = null;
                if (on && MakeTiles(grass)) m_For = grass;
            }
            var view = new GrassView();
            Camera cam = Camera.main;
            if (m_For != null && cam != null)
            {
                GrassRuleSet rules = grass.Rules;
                GeometryUtility.CalculateFrustumPlanes(cam, m_Planes);
                view = new GrassView
                {
                    Camera = cam.transform.position,
                    Plane0 = Pack(m_Planes[0]), Plane1 = Pack(m_Planes[1]), Plane2 = Pack(m_Planes[2]),
                    Plane3 = Pack(m_Planes[3]), Plane4 = Pack(m_Planes[4]), Plane5 = Pack(m_Planes[5]),
                    NearEnd = grass.NearEnd, FarEnd = grass.FarEnd, Hysteresis = rules.tileHysteresis,
                    DensityScale = grass.CurrentDensity, StreamBudget = rules.tileStreamBudget, Valid = true,
                };
            }
            EntityManager.SetComponentData(m_ViewEntity, view);
            m_Watch.Stop();

            // The last frame's costs (the jobs of this frame are still running).
            if (m_For != null)
            {
                m_MainMs += m_Watch.Elapsed.TotalMilliseconds + m_Schedule.LastValue * 1e-6;
                m_WorkerMs += (m_Cull.LastValue + m_Stream.LastValue) * 1e-6;
                m_Frames++;
                GrassTileStats s = EntityManager.GetComponentData<GrassTileStats>(m_StatsEntity);
                GrassRenderer.TileLine = $"grass tiles {s.Tiles}: near {s.Near}, far {s.Far}, visible {s.Visible}, resident {s.Resident}, " +
                    $"queued {s.Queued}, in/out this frame {s.StreamedIn}/{s.StreamedOut}; ms per frame over {m_Frames} frames: " +
                    $"main {m_MainMs / m_Frames:0.000}, workers {m_WorkerMs / m_Frames:0.000}";
                if (GrassRenderer.TileLineRead) { GrassRenderer.TileLineRead = false; m_MainMs = m_WorkerMs = 0; m_Frames = 0; }
            }
            else GrassRenderer.TileLine = "grass tiles: off (no grass, or the rule set's ecsTiles is off)";
        }

        static float4 Pack(Plane p) => new float4(p.normal.x, p.normal.y, p.normal.z, p.distance);

        void ClearTiles()
        {
            if (!m_AllTiles.IsEmptyIgnoreFilter) EntityManager.DestroyEntity(m_AllTiles);
        }

        // One entity per tile that has any grass. Tiles of a section share a chunk (GrassSection), whose bounds are set after.
        bool MakeTiles(GrassRenderer grass)
        {
            GrassRuleSet rules = grass.Rules;
            Terrain terrain = grass.Terrain;
            if (rules.mask == null || terrain == null) return false;
            Vector3 tPos = terrain.transform.position, tSize = terrain.terrainData.size;
            float tileSize = Mathf.Max(4f, rules.tileSize);
            int across = Mathf.CeilToInt(tSize.x / tileSize), along = Mathf.CeilToInt(tSize.z / tileSize);
            int res = Mathf.Clamp(Mathf.Max(across, along) * SamplesPerTile, 64, 2048);
            Color[] px = ReadMask(rules.mask, res);
            int sectionTiles = Mathf.Max(1, rules.sectionTiles);

            var archetype = EntityManager.CreateArchetype(typeof(GrassTile), typeof(GrassTileState), typeof(GrassSection),
                ComponentType.ChunkComponent<GrassChunkBounds>());
            int made = 0;
            for (int tz = 0; tz < along; tz++)
                for (int tx = 0; tx < across; tx++)
                {
                    float density = 0f; byte biomes = 0;
                    int x0 = tx * res / across, x1 = Mathf.Max(x0 + 1, (tx + 1) * res / across);
                    int z0 = tz * res / along, z1 = Mathf.Max(z0 + 1, (tz + 1) * res / along);
                    for (int z = z0; z < z1 && z < res; z++)
                        for (int x = x0; x < x1 && x < res; x++)
                        {
                            Color c = px[z * res + x];
                            if (c.r <= 0.01f) continue;
                            density = Mathf.Max(density, c.r);
                            biomes |= (byte)(1 << Mathf.Clamp(Mathf.RoundToInt(c.g * 3f), 0, 3));
                        }
                    if (density <= 0f) continue;
                    var min = new float2(tPos.x + tx * tileSize, tPos.z + tz * tileSize);
                    var max = math.min(min + tileSize, new float2(tPos.x + tSize.x, tPos.z + tSize.z));
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int j = 0; j <= 2; j++)
                        for (int i = 0; i <= 2; i++)
                        {
                            float2 p = math.lerp(min, max, new float2(i * 0.5f, j * 0.5f));
                            float h = terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + tPos.y;
                            lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h);
                        }
                    Entity e = EntityManager.CreateEntity(archetype);
                    EntityManager.SetComponentData(e, new GrassTile
                    {
                        Index = new int2(tx, tz), Min = min, Max = max, MinY = lo - 1f, MaxY = hi + TallestGrass,
                        Density = density, Biomes = biomes,
                    });
                    EntityManager.SetComponentData(e, new GrassTileState { Lod = GrassTileLod.Out, DensityScale = 1f, Tint = new float4(1f, 1f, 1f, 0f) });
                    EntityManager.SetSharedComponent(e, new GrassSection { Index = new int2(tx / sectionTiles, tz / sectionTiles) });
                    made++;
                }
            SetChunkBounds();
            UnityEngine.Debug.Log($"[Grass] {made} tiles of {tileSize:0} m made for {grass.name} ({across}x{along} grid, sections of {sectionTiles}x{sectionTiles})");
            return made > 0;
        }

        void SetChunkBounds()
        {
            var tileHandle = CheckedStateRef.GetComponentTypeHandle<GrassTile>(true);
            var boundsHandle = CheckedStateRef.GetComponentTypeHandle<GrassChunkBounds>(false);
            using NativeArray<ArchetypeChunk> chunks = m_TileChunks.ToArchetypeChunkArray(Allocator.Temp);
            foreach (ArchetypeChunk chunk in chunks)
            {
                NativeArray<GrassTile> tiles = chunk.GetNativeArray(ref tileHandle);
                var b = new GrassChunkBounds { Min = float.MaxValue, Max = float.MinValue, MinY = float.MaxValue, MaxY = float.MinValue };
                for (int i = 0; i < tiles.Length; i++)
                {
                    b.Min = math.min(b.Min, tiles[i].Min); b.Max = math.max(b.Max, tiles[i].Max);
                    b.MinY = math.min(b.MinY, tiles[i].MinY); b.MaxY = math.max(b.MaxY, tiles[i].MaxY);
                }
                chunk.SetChunkComponentData(ref boundsHandle, b);
            }
        }

        static Color[] ReadMask(Texture2D mask, int res)
        {
            var rt = RenderTexture.GetTemporary(res, res, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(mask, rt);
            var read = new Texture2D(res, res, TextureFormat.RGBA32, false, true);
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, res, res), 0, 0);
            read.Apply(false);
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(rt);
            Color[] px = read.GetPixels();
            UnityEngine.Object.DestroyImmediate(read);
            return px;
        }
    }
}
