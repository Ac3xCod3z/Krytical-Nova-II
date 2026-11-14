using System.Collections.Generic;
using KryticalNova.Core;
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
        const int LeafKinds = 16;   // KNSeason.hlsl: rendering layer bits 8-11

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

        readonly SnowTrackMap m_Tracks = new SnowTrackMap();
        readonly Vector4[] m_LeafPeak = new Vector4[LeafKinds];
        readonly Vector4[] m_LeafLate = new Vector4[LeafKinds];
        SnowPrintWatcher m_Prints;
        readonly Dictionary<Terrain, float> m_FlowerBase = new Dictionary<Terrain, float>();
        readonly List<Terrain> m_Terrains = new List<Terrain>();
        readonly List<Terrain> m_GoneTerrains = new List<Terrain>();
        float m_FlowerApplied = -1f, m_NextTerrainLook;
        bool m_PublishedOff;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => FallingLeaves = 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SeasonPresenter") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SeasonPresenter>();
            go.AddComponent<SnowPrintWatcher>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            m_Prints = GetComponent<SnowPrintWatcher>();
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
            FallingLeaves = leaves.Falling * settings.FallLeaves(WorldEffectsRuntime.Quality);

            UpdateTracks(settings, cover);
            UpdateFlowers(settings, Mathf.Lerp(from.flowerDensity, to.flowerDensity, b));
        }

        void UpdateTracks(SeasonSettings settings, float cover)
        {
            Camera cam = Camera.main;
            bool wanted = cover >= settings.trackMinCover && settings.trackCompute != null && cam != null;
            SnowPrints.Wanted = wanted;
            if (m_Prints != null) m_Prints.Watch(wanted ? cam.transform.position : (Vector3?)null, settings);
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
            }
            Shader.SetGlobalVectorArray(LeafPeakId, m_LeafPeak);
            Shader.SetGlobalVectorArray(LeafLateId, m_LeafLate);
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
            FallingLeaves = 0f;
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
