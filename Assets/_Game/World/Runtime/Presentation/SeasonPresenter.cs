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

        /// <summary>The one presenter (tests and the GM window read it).</summary>
        public static SeasonPresenter Instance { get; private set; }

        /// <summary>What this client draws now (GM window "Now" line).</summary>
        public float Blend { get; private set; } = 1f;
        public float SnowCover { get; private set; }
        public bool Publishing { get; private set; }

        readonly SnowTrackMap m_Tracks = new SnowTrackMap();
        readonly Dictionary<Terrain, float> m_FlowerBase = new Dictionary<Terrain, float>();
        readonly List<Terrain> m_Terrains = new List<Terrain>();
        readonly List<Terrain> m_GoneTerrains = new List<Terrain>();
        float m_FlowerApplied = -1f, m_NextTerrainLook;
        bool m_PublishedOff;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SeasonPresenter") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SeasonPresenter>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
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

            UpdateTracks(settings, cover);
            UpdateFlowers(settings, Mathf.Lerp(from.flowerDensity, to.flowerDensity, b));
        }

        void UpdateTracks(SeasonSettings settings, float cover)
        {
            Camera cam = Camera.main;
            if (cover < settings.trackMinCover || settings.trackCompute == null || cam == null)
            {
                if (m_Tracks.Allocated) m_Tracks.Release();
                return;
            }
            // falling snow fills the tracks faster
            WeatherBlend w = WorldClock.Weather;
            float fill = settings.trackFillSeconds / (1f + settings.freshSnowFill * Mathf.Clamp01(w.snow));
            m_Tracks.Update(settings.trackCompute, cam.transform.position, settings.trackMetres,
                settings.TrackTexels(WorldEffectsRuntime.Quality), fill, 0.6f);
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
