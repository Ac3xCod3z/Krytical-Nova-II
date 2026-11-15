using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine.SceneManagement;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The season on this client (owner's DAY list item 21): turns the state the server sent (<see cref="SeasonView.Client"/>) into a few
    /// shader globals that the ground, buildings, grass and trees read (<c>KNSeason.hlsl</c>), the daylight share the sun follows (short
    /// winter days), the terrain flower density, and the snow track map. A handful of <c>Shader.SetGlobal*</c> calls a frame, no garbage.
    /// <para>
    /// Build 12 black-sky rule: it is made at runtime by its own bootstrap (never placed in a scene, so no serialized switch a batch build
    /// could pack off), on every machine alike, and it never switches anything else off. With no season known it publishes "no season" and
    /// every shader draws exactly as before seasons.
    /// </para>
    /// </summary>
    public sealed class SeasonPresenter : MonoBehaviour
    {
        static readonly int ParamsId = Shader.PropertyToID("_KN_SeasonParams");
        static readonly int GrassId = Shader.PropertyToID("_KN_SeasonGrass");
        static readonly int LeafFromId = Shader.PropertyToID("_KN_SeasonLeafFrom");
        static readonly int LeafToId = Shader.PropertyToID("_KN_SeasonLeafTo");
        static readonly int MiscId = Shader.PropertyToID("_KN_SeasonMisc");
        static readonly int SnowColorId = Shader.PropertyToID("_KN_SnowColor");
        static readonly int SnowShapeId = Shader.PropertyToID("_KN_SnowShape");
        static readonly int FoliageId = Shader.PropertyToID("_KN_SeasonFoliage");
        static readonly int DullId = Shader.PropertyToID("_KN_SeasonDull");
        static readonly int LeafPeakId = Shader.PropertyToID("_KN_LeafPeak");
        static readonly int LeafLateId = Shader.PropertyToID("_KN_LeafLate");
        static readonly int FieldId = Shader.PropertyToID("_KN_SnowField");
        static readonly int ShelterKId = Shader.PropertyToID("_KN_SnowShelterK");
        static readonly int DepthId = Shader.PropertyToID("_KN_SnowDepth");
        static readonly int SlushId = Shader.PropertyToID("_KN_SlushColor");
        static readonly int SpringId = Shader.PropertyToID("_KN_SeasonSpring");
        static readonly int SummerId = Shader.PropertyToID("_KN_SeasonSummer");
        static readonly int ThawGrassId = Shader.PropertyToID("_KN_ThawGrass");
        static readonly int SpringGreenId = Shader.PropertyToID("_KN_SpringGreen");
        static readonly int BudId = Shader.PropertyToID("_KN_BudColor");
        static readonly int GoldenId = Shader.PropertyToID("_KN_GoldenGrass");
        static readonly int LeafBlossomId = Shader.PropertyToID("_KN_LeafBlossom");
        const int LeafKinds = 16;   // KNSeason.hlsl: rendering layer bits 8-11
        /// <summary>Rendering layer bit 6: a road or path (tagged at runtime by material name; KNSeason.hlsl draws it trodden).</summary>
        public const uint RoadLayerBit = 1u << 6;
        const uint NoSnowLayerBit = 1u << 7;

        /// <summary>The one presenter (tests and the GM window read it).</summary>
        public static SeasonPresenter Instance { get; private set; }

        /// <summary>What this client draws now (GM window "Now" line).</summary>
        public float Blend { get; private set; } = 1f;
        public float SnowCover { get; private set; }
        public bool Publishing { get; private set; }
        /// <summary>The leaves this client draws now (GM window, tests).</summary>
        public FoliageNow Foliage { get; private set; }

        /// <summary>
        /// How many falling leaves the season adds now (0 none; the windy-day rate times this at the height of Autumn), read by
        /// <see cref="WindLeavesController"/>.
        /// </summary>
        public static float FallingLeaves { get; private set; }

        /// <summary>The heat haze now, 0 none .. 1 a hot afternoon at full strength (times the World Effects tier's; 0 on Low), and the
        /// metres from the camera where it begins and is full. Read by the heat haze screen pass.</summary>
        public static float HeatHaze { get; private set; }
        public static Vector2 HeatHazeDistance { get; private set; }

        /// <summary>The air temperature this client draws (deg C; GM window, motes, haze).</summary>
        public float Temperature { get; private set; }

        readonly SnowTrackMap m_Tracks = new SnowTrackMap();
        readonly Vector4[] m_LeafPeak = new Vector4[LeafKinds];
        readonly Vector4[] m_LeafLate = new Vector4[LeafKinds];
        readonly Vector4[] m_LeafBlossom = new Vector4[LeafKinds];
        readonly SnowShelterMap m_Shelter = new SnowShelterMap();
        struct ShelterShape { public Vector2 Centre, Half; public SnowShelterKind Kind; }
        readonly List<ShelterShape> m_ShelterAll = new List<ShelterShape>(2048);
        readonly List<(float distance, int index)> m_ShelterNear = new List<(float, int)>(1024);
        int m_ShelterSceneKey = int.MinValue;
        Vector3 m_ShelterBuiltAt;
        Vector2 m_ShelterWind;
        float m_NextShelterLook;
        bool m_ShelterDirty;
        SnowPrintWatcher m_Prints;
        readonly Dictionary<Terrain, float> m_FlowerBase = new Dictionary<Terrain, float>();
        readonly List<Terrain> m_Terrains = new List<Terrain>();
        readonly List<Terrain> m_GoneTerrains = new List<Terrain>();
        float m_FlowerApplied = -1f, m_NextTerrainLook;
        bool m_PublishedOff;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { FallingLeaves = 0f; HeatHaze = 0f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // the aurora comes in Winter, on the sky's own schedule (and all year on an always-winter map)
            SkyEventPresenter.AuroraAllowed = () => SeasonView.HasClient && SeasonView.Client.AuroraAllowedAt(NetClock.Now);
            if (Instance != null) return;
            var go = new GameObject("SeasonPresenter") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SeasonPresenter>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            // the foot watcher lives beside it (made here: Awake runs inside AddComponent, before the bootstrap could add it)
            m_Prints = GetComponent<SnowPrintWatcher>();
            if (m_Prints == null) m_Prints = gameObject.AddComponent<SnowPrintWatcher>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            PublishOff();
        }

        void LateUpdate()
        {
            SeasonSettings settings = SeasonSettings.Instance;
            if (!SeasonView.HasClient || settings == null)
            {
                if (!m_PublishedOff) PublishOff();
                return;
            }
            m_PublishedOff = false;
            Publishing = true;
            SeasonState s = SeasonView.Client;
            double now = NetClock.Now;
            float b = s.BlendAt(now);
            Blend = b;

            // the calendar's seasons light the day (an always-winter map still has long summer days)
            SeasonProfile dayFrom = settings.Profile(s.From), dayTo = settings.Profile(s.To);
            float shareFrom = dayFrom != null ? dayFrom.daylightShare : 0.5f, shareTo = dayTo != null ? dayTo.daylightShare : 0.5f;
            SeasonView.DaylightShare = Mathf.Lerp(shareFrom, shareTo, b);

            // the look: Winter's all year on an always-winter map
            SeasonProfile from = s.AlwaysWinter ? settings.winter : dayFrom;
            SeasonProfile to = s.AlwaysWinter ? settings.winter : dayTo;
            if (from == null || to == null)
            {
                PublishOff();
                return;
            }

            float t01 = EnvironmentPresenter.PresentedTimeOfDay01;   // already warped: night by the season's sun
            float night = Mathf.Clamp01(-Mathf.Cos(t01 * 2f * Mathf.PI) * 3f);   // 0 from sunrise to sunset, 1 soon after dusk
            float frost = Mathf.Lerp(from.frost, to.frost, b) + Mathf.Lerp(from.nightFrost, to.nightFrost, b) * night;
            float cover = s.SnowAt(now);
            SnowCover = cover;

            Shader.SetGlobalVector(ParamsId, new Vector4(cover, Mathf.Clamp01(frost), settings.roofBias, 1f));
            Color grass = Color.Lerp(from.grassTint, to.grassTint, b);
            Shader.SetGlobalVector(GrassId, new Vector4(grass.r, grass.g, grass.b, Mathf.Lerp(from.grassTipFrost, to.grassTipFrost, b)));
            Shader.SetGlobalVector(LeafFromId, new Vector4(from.leafColor.r, from.leafColor.g, from.leafColor.b, from.leafAmount));
            Shader.SetGlobalVector(LeafToId, new Vector4(to.leafColor.r, to.leafColor.g, to.leafColor.b, to.leafAmount));
            Shader.SetGlobalVector(MiscId, new Vector4(b, settings.leafTurnSpread, settings.snowEdgeSoftness, settings.grassHeightUnderSnow));
            Color snow = settings.snowColor;
            Shader.SetGlobalVector(SnowColorId, new Vector4(snow.r, snow.g, snow.b, settings.trackRimDarkness));

            // the snow's shape: where it sticks (slope), how deep it lies and how much of that is raised (World Effects)
            var (cosNone, invRange) = SeasonRules.SnowSlopeShape(settings.snowFullBelowDegrees, settings.snowNoneAboveDegrees);
            Shader.SetGlobalVector(SnowShapeId, new Vector4(cosNone, invRange, cover * settings.snowDepthMetres,
                settings.SnowLift(WorldEffectsRuntime.Quality)));

            // the leaves' year: every kind turns its own colour through Autumn, the canopies thin, bare through Winter
            FoliageNow leaves = s.FoliageAt(now, settings.Foliage);
            Foliage = leaves;
            Shader.SetGlobalVector(FoliageId, new Vector4(leaves.Turn, leaves.Canopy, leaves.Dull, settings.leafTurnSpread));
            Color dull = settings.autumnDull;
            Shader.SetGlobalVector(DullId, new Vector4(dull.r, dull.g, dull.b, 1f));
            PublishLeafKinds(settings);
            // falling leaves in Autumn, drifting petals as Spring's blossom drops
            FallingLeaves = Mathf.Max(leaves.Falling, leaves.Petals * settings.petalDrift) * settings.FallLeaves(WorldEffectsRuntime.Quality);

            // part 2 (owner 14 Nov): drifts, shelter, trodden roads, ploughed paths
            Shader.SetGlobalVector(FieldId, new Vector4(1f / Mathf.Max(2f, settings.driftSize), settings.driftAmount, settings.rippleAmount,
                settings.slopeThinning));
            Shader.SetGlobalVector(ShelterKId, new Vector4(settings.underCanopyThinning, settings.windwardPile, settings.roadSnowShare,
                s.SnowRate < 0f ? settings.sunFacingThaw : 0f));
            Shader.SetGlobalVector(DepthId, new Vector4(settings.snowDepthMetres, settings.pathPress, 0f, 0f));
            Color slush = settings.slushColor;
            Shader.SetGlobalVector(SlushId, new Vector4(slush.r, slush.g, slush.b, 1f));

            // spring and summer: grass grows back green, buds and blossom; golden grass on dry spells; heat haze on hot afternoons
            Temperature = s.Temperature;
            float summer = (s.From == Season.Summer && !s.AlwaysWinter ? 1f - b : 0f) + (s.To == Season.Summer && !s.AlwaysWinter ? b : 0f);
            float golden = summer * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.goldenGrassDays.x, settings.goldenGrassDays.y, s.DryDays));
            float heat = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(settings.heatHazeFromTo.x, settings.heatHazeFromTo.y, s.Temperature)) * (1f - night);
            HeatHaze = heat * settings.HeatHaze(WorldEffectsRuntime.Quality);
            HeatHazeDistance = settings.heatHazeDistance;
            Shader.SetGlobalVector(SpringId, new Vector4(leaves.Grow, leaves.Brown, leaves.Fresh, leaves.Bud));
            Shader.SetGlobalVector(SummerId, new Vector4(leaves.Blossom, settings.winterGrassHeight, golden, heat));
            SetColour(ThawGrassId, settings.thawGrass);
            SetColour(SpringGreenId, settings.springGreen);
            SetColour(BudId, settings.budColor);
            SetColour(GoldenId, settings.goldenGrass);

            UpdateShelter(settings, cover);
            UpdateTracks(settings, cover);
            UpdateFlowers(settings, Mathf.Lerp(from.flowerDensity, to.flowerDensity, b));
        }

        void UpdateTracks(SeasonSettings settings, float cover)
        {
            Camera cam = Camera.main;
            bool wanted = cover >= settings.trackMinCover && settings.trackCompute != null && cam != null;
            SnowPrints.Wanted = wanted;
            if (m_Prints != null) m_Prints.Watch(wanted ? cam.transform.position : (Vector3?)null, settings, cover * settings.snowDepthMetres);
            if (!wanted)
            {
                if (m_Tracks.Allocated) m_Tracks.Release();
                return;
            }
            // falling snow fills the prints faster (heavy snow fastest)
            WeatherBlend w = WorldClock.Weather;
            float fill = settings.trackFillSeconds / (1f + settings.freshSnowFill * Mathf.Clamp01(w.snow));
            m_Tracks.Update(settings.trackCompute, cam.transform.position, settings.trackMetres,
                settings.TrackTexels(WorldEffectsRuntime.Quality), fill);
        }

        /// <summary>Each tree kind's fall colours into the shader's palette (index 0 = foliage with no kind: the Autumn profile's colour).</summary>
        void PublishLeafKinds(SeasonSettings settings)
        {
            Color baseColor = settings.autumn != null ? settings.autumn.leafColor : new Color(0.9f, 0.45f, 0.12f);
            m_LeafPeak[0] = new Vector4(baseColor.r, baseColor.g, baseColor.b, 0f);
            m_LeafLate[0] = new Vector4(baseColor.r * 0.62f, baseColor.g * 0.55f, baseColor.b * 0.6f, 0f);
            for (int i = 1; i < LeafKinds; i++)
            {
                LeafKind k = settings.leafKinds != null && i - 1 < settings.leafKinds.Count ? settings.leafKinds[i - 1] : null;
                if (k == null) { m_LeafPeak[i] = m_LeafPeak[0]; m_LeafLate[i] = m_LeafLate[0]; continue; }
                m_LeafPeak[i] = new Vector4(k.peak.r, k.peak.g, k.peak.b, k.evergreen ? 1f : 0f);
                m_LeafLate[i] = new Vector4(k.late.r, k.late.g, k.late.b, 0f);
                m_LeafBlossom[i] = new Vector4(k.blossom.r, k.blossom.g, k.blossom.b, k.blossoms && !k.evergreen ? 1f : 0f);
            }
            Shader.SetGlobalVectorArray(LeafPeakId, m_LeafPeak);
            Shader.SetGlobalVectorArray(LeafLateId, m_LeafLate);
            Shader.SetGlobalVectorArray(LeafBlossomId, m_LeafBlossom);
        }

        static void SetColour(int id, Color c) => Shader.SetGlobalVector(id, new Vector4(c.r, c.g, c.b, 1f));

        /// <summary>
        /// The shelter map (owner 14 Nov: thinner under trees, piles against walls, thin trodden roads): the map's renderers are sorted once
        /// per loaded map into canopies, walls and roads (roads by material or mesh name, and tagged with <see cref="RoadLayerBit"/> so
        /// their own surfaces draw trodden); the nearest are drawn into the map when the camera has moved far or the wind has turned.
        /// </summary>
        void UpdateShelter(SeasonSettings settings, float cover)
        {
            Camera cam = Camera.main;
            int texels = settings.ShelterTexels(WorldEffectsRuntime.Quality);
            if (cover < settings.trackMinCover || texels <= 0 || settings.trackCompute == null || cam == null)
            {
                if (m_Shelter.Allocated) m_Shelter.Release();
                return;
            }
            if (Time.unscaledTime >= m_NextShelterLook)
            {
                m_NextShelterLook = Time.unscaledTime + 3f;
                int key = SceneKey();
                if (key != m_ShelterSceneKey)
                {
                    m_ShelterSceneKey = key;
                    GatherShelter(settings);
                    m_ShelterDirty = true;
                }
            }
            Vector3 at = cam.transform.position;
            Vector2 wind = WorldClock.Weather.wind;
            float far = settings.shelterMetres * 0.15f;
            bool moved = (at - m_ShelterBuiltAt).sqrMagnitude > far * far;
            bool turned = wind.sqrMagnitude > 0.25f && Vector2.Angle(wind, m_ShelterWind) > 25f;
            if (!m_ShelterDirty && !moved && !turned && m_Shelter.Allocated) return;
            m_ShelterDirty = false;
            m_ShelterBuiltAt = at;
            if (wind.sqrMagnitude > 0.25f) m_ShelterWind = wind;

            float reach = settings.shelterMetres * 0.5f + settings.pileReach;
            m_ShelterNear.Clear();
            for (int i = 0; i < m_ShelterAll.Count; i++)
            {
                ShelterShape sh = m_ShelterAll[i];
                float dx = Mathf.Max(0f, Mathf.Abs(sh.Centre.x - at.x) - sh.Half.x), dz = Mathf.Max(0f, Mathf.Abs(sh.Centre.y - at.z) - sh.Half.y);
                if (dx > reach || dz > reach) continue;
                m_ShelterNear.Add((dx * dx + dz * dz, i));
            }
            m_ShelterNear.Sort((x, y) => x.distance.CompareTo(y.distance));
            m_Shelter.Begin();
            int most = Mathf.Min(settings.shelterShapesMax, SnowShelterMap.MaxShapes);
            for (int i = 0; i < m_ShelterNear.Count && i < most; i++)
            {
                ShelterShape sh = m_ShelterAll[m_ShelterNear[i].index];
                m_Shelter.Add(sh.Centre, sh.Half, sh.Kind);
            }
            Vector2 windTo = m_ShelterWind.sqrMagnitude > 0.01f ? m_ShelterWind : new Vector2(0.5f, 0.866f);
            m_Shelter.Build(settings.trackCompute, at, settings.shelterMetres, texels, windTo, settings.pileReach);
        }

        static int SceneKey()
        {
            int key = SceneManager.sceneCount * 7919;
            for (int i = 0; i < SceneManager.sceneCount; i++) key = unchecked(key * 31 + SceneManager.GetSceneAt(i).GetHashCode());
            return key;
        }

        void GatherShelter(SeasonSettings settings)
        {
            m_ShelterAll.Clear();
            foreach (MeshRenderer r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                uint layer = r.renderingLayerMask;
                if ((layer & NoSnowLayerBit) != 0) continue;   // interiors, characters, previews
                Bounds b = r.bounds;
                Vector3 size = b.size;
                var centre = new Vector2(b.center.x, b.center.z);
                var half = new Vector2(b.extents.x, b.extents.z);
                Material m = r.sharedMaterial;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                bool road = (layer & RoadLayerBit) != 0 || (m != null && settings.IsRoadName(m.name))
                            || (mf != null && mf.sharedMesh != null && settings.IsRoadName(mf.sharedMesh.name));
                if (road)
                {
                    r.renderingLayerMask = layer | RoadLayerBit;
                    if (size.x * size.z < 3600f && size.y < 3f)
                        m_ShelterAll.Add(new ShelterShape { Centre = centre, Half = half, Kind = SnowShelterKind.Road });
                    continue;
                }
                bool tree = ((layer >> 8) & 15u) != 0u || r.gameObject.name.IndexOf("Tree", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (tree)
                {
                    if (size.y > 2f) m_ShelterAll.Add(new ShelterShape { Centre = centre, Half = half * 0.85f, Kind = SnowShelterKind.Canopy });
                    continue;
                }
                if (size.y > 1.2f && size.y < 60f && Mathf.Min(size.x, size.z) > 0.6f && size.x * size.z < 2500f)
                    m_ShelterAll.Add(new ShelterShape { Centre = centre, Half = half, Kind = SnowShelterKind.Wall });
            }
        }

        void UpdateFlowers(SeasonSettings settings, float density)
        {
            float step = Mathf.Max(0.05f, settings.flowerStep);
            float q = Mathf.Clamp01(Mathf.Round(density / step) * step);
            bool look = Time.unscaledTime >= m_NextTerrainLook;   // new terrains (a map loaded) are looked for every 2 s, not every frame
            if (!look && Mathf.Approximately(q, m_FlowerApplied)) return;
            bool fresh = false;
            if (look)
            {
                m_NextTerrainLook = Time.unscaledTime + 2f;
                m_GoneTerrains.Clear();
                foreach (Terrain t in m_FlowerBase.Keys) if (t == null) m_GoneTerrains.Add(t);
                foreach (Terrain t in m_GoneTerrains) m_FlowerBase.Remove(t);
                Terrain.GetActiveTerrains(m_Terrains);
                for (int i = 0; i < m_Terrains.Count; i++)
                    if (m_Terrains[i] != null && !m_FlowerBase.ContainsKey(m_Terrains[i])) { m_FlowerBase[m_Terrains[i]] = m_Terrains[i].detailObjectDensity; fresh = true; }
            }
            if (!fresh && Mathf.Approximately(q, m_FlowerApplied)) return;
            m_FlowerApplied = q;
            foreach (KeyValuePair<Terrain, float> kv in m_FlowerBase)
                if (kv.Key != null) kv.Key.detailObjectDensity = kv.Value * q;
        }

        void PublishOff()
        {
            m_PublishedOff = true;
            Publishing = false;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            Shader.SetGlobalVector(SnowShapeId, Vector4.zero);
            Shader.SetGlobalVector(FoliageId, new Vector4(0f, 1f, 0f, 0f));
            Shader.SetGlobalVector(SpringId, new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalVector(SummerId, new Vector4(0f, 1f, 0f, 0f));
            FallingLeaves = 0f;
            HeatHaze = 0f;
            m_Shelter.Release();
            m_ShelterSceneKey = int.MinValue;
            SnowPrints.Wanted = false;
            if (m_Prints != null) m_Prints.Watch(null, null);
            SeasonView.DaylightShare = 0.5f;
            SnowCover = 0f;
            Blend = 1f;
            m_Tracks.Release();
            foreach (KeyValuePair<Terrain, float> kv in m_FlowerBase)
                if (kv.Key != null) kv.Key.detailObjectDensity = kv.Value;
            m_FlowerBase.Clear();
            m_FlowerApplied = -1f;
        }
    }
}
