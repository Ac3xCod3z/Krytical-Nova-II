using System;
using System.Collections.Generic;
using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Every number of the seasons (owner's DAY list item 21; design note <c>_Docs/Plans/seasons-plan.md</c>): the calendar (two real weeks
    /// a season, turning Mondays 00:00 UTC, Winter from 23 Nov 2026), how fast snow piles up and melts, the snow's look, footprints, and how
    /// much falls on each World Effects tier. One asset, <c>World/Resources/SeasonSettings.asset</c>, read by the server (the calendar, the
    /// snow) and by every client (the look). The server decides; clients only draw what it sent.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Season Settings")]
    public sealed class SeasonSettings : ScriptableObject
    {
        [Header("Calendar (real UTC time decides; owner 13 Nov: 2 real weeks a season, Mondays 00:00 UTC)")]
        [Tooltip("The first Spring began on this UTC date at 00:00 (a Monday). 2026-01-05 puts Winter at Mon 23 Nov 2026.")]
        public int yearStartYear = 2026;
        [Range(1, 12)] public int yearStartMonth = 1;
        [Range(1, 31)] public int yearStartDay = 5;
        [Tooltip("Real days a season lasts. 14 = two weeks; a year is four of them.")]
        [Min(0.01f)] public float seasonLengthDays = 14f;
        [Tooltip("Hours after 00:00 UTC that a season turns (0 = midnight UTC).")]
        [Range(0f, 23.99f)] public float turnHourOffset = 0f;
        [Tooltip("Moves the whole calendar by whole or part days (positive = later).")]
        public float seasonOffsetDays = 0f;
        [Tooltip("Real hours a new season takes to blend in at its start (6 = six in-game days of one hour).")]
        [Min(0f)] public float transitionHours = 6f;
        [Tooltip("Seconds a GM switch (gm.season) takes to blend, so a test is quick.")]
        [Min(0f)] public float gmBlendSeconds = 20f;

        [Header("The four seasons")]
        public SeasonProfile spring;
        public SeasonProfile summer;
        public SeasonProfile autumn;
        public SeasonProfile winter;

        [Header("Snow cover (server): minutes for a full change")]
        [Tooltip("Minutes of snowing at intensity 1 to go from bare to full cover (Snow 0.6 takes this / 0.6).")]
        [Min(0.1f)] public float buildMinutesAtFullSnow = 10f;
        [Tooltip("Minutes for full cover to melt on a dry day, outside Winter.")]
        [Min(0.1f)] public float meltMinutesDay = 30f;
        [Tooltip("Melt at night, times the day's (outside Winter).")]
        [Range(0f, 1f)] public float nightMeltFactor = 0.25f;
        [Tooltip("Melt while it rains, times the day's.")]
        [Min(0f)] public float rainMeltFactor = 3f;
        [Tooltip("Minutes for full cover to melt on a dry Winter day.")]
        [Min(0.1f)] public float winterMeltMinutesDay = 240f;
        [Tooltip("Melt on a Winter night, times the Winter day's (0 = none).")]
        [Range(0f, 1f)] public float winterNightMeltFactor = 0f;
        [Tooltip("An always-winter map never has less cover than this.")]
        [Range(0f, 1f)] public float alwaysWinterFloor = 0.6f;

        [Header("Always-winter maps (owner 13 Nov: the Sunderpeak maps only)")]
        [Tooltip("A map whose Zone Catalog region (or scene name) is listed here keeps Winter's weather, colours and snow all year, but takes " +
                 "the calendar's daylight (short winter days, long summer ones).")]
        public List<string> alwaysWinterRegions = new List<string> { "Sunderpeak" };

        [Header("Look (every client)")]
        [Tooltip("The flat comic colour of snow (no texture).")]
        public Color snowColor = new Color(0.9f, 0.94f, 1f);
        [Tooltip("Roofs and ledges show snow this much sooner than the ground.")]
        [Range(0f, 0.5f)] public float roofBias = 0.1f;
        [Tooltip("How soft the snow's ragged edge is.")]
        [Range(0.01f, 0.3f)] public float snowEdgeSoftness = 0.08f;
        [Tooltip("How much darker the thin rim of a footprint is.")]
        [Range(0f, 1f)] public float trackRimDarkness = 0.25f;
        [Tooltip("Grass height under full cover (1 = stands, 0.35 = pressed well down).")]
        [Range(0.05f, 1f)] public float grassHeightUnderSnow = 0.35f;
        [Tooltip("How much each tree turns before or after the others (0 = the whole forest at once).")]
        [Range(0f, 1f)] public float leafTurnSpread = 0.25f;
        [Tooltip("Flower density changes only in steps this big (a terrain rebuilds its detail patches on each change).")]
        [Range(0.05f, 0.5f)] public float flowerStep = 0.1f;

        [Header("Footprints and trails (every client)")]
        [Tooltip("The track compute (Rendering/Shaders/Seasons/SnowTrack.compute).")]
        public ComputeShader trackCompute;
        [Tooltip("Metres of ground round the camera the track map covers.")]
        [Min(8f)] public float trackMetres = 64f;
        [Tooltip("Track map texels across, per World Effects tier (Low, Medium, High, Ultra).")]
        public int[] trackTexelsByTier = { 128, 256, 256, 512 };
        [Tooltip("Seconds for a footprint to fill back in.")]
        [Min(1f)] public float trackFillSeconds = 120f;
        [Tooltip("Falling snow fills tracks faster: times (1 + this x snow intensity).")]
        [Min(0f)] public float freshSnowFill = 4f;
        [Tooltip("Below this cover there are no tracks and the map is let go (0 cost in summer).")]
        [Range(0f, 0.2f)] public float trackMinCover = 0.02f;

        [Header("Precipitation by World Effects (Low, Medium, High, Ultra)")]
        [Tooltip("Rain and snow particle rates, times the art's, per World Effects tier. High = the authored look.")]
        public float[] precipitationByTier = { 0.4f, 0.7f, 1f, 1.3f };

        /// <summary>The profile of a season (null if not set).</summary>
        public SeasonProfile Profile(Season s) => s switch
        {
            Season.Spring => spring, Season.Summer => summer, Season.Autumn => autumn, _ => winter,
        };

        /// <summary>The calendar in seconds, for <see cref="SeasonRules"/>.</summary>
        public SeasonCalendar Calendar
        {
            get
            {
                double start;
                try { start = (new DateTime(yearStartYear, yearStartMonth, yearStartDay, 0, 0, 0, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds; }
                catch (ArgumentOutOfRangeException) { start = (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds; }
                return new SeasonCalendar
                {
                    YearStartUnix = start,
                    SeasonSeconds = Math.Max(60.0, seasonLengthDays * 86400.0),
                    OffsetSeconds = turnHourOffset * 3600.0 + seasonOffsetDays * 86400.0,
                    TransitionSeconds = transitionHours * 3600.0,
                };
            }
        }

        /// <summary>The snow rates in cover per second, for <see cref="SeasonRules.SnowRate"/>.</summary>
        public SnowRates Rates => new SnowRates
        {
            BuildPerSecond = 1f / (buildMinutesAtFullSnow * 60f),
            MeltDay = 1f / (meltMinutesDay * 60f),
            NightFactor = nightMeltFactor,
            RainFactor = rainMeltFactor,
            WinterMeltDay = 1f / (winterMeltMinutesDay * 60f),
            WinterNightFactor = winterNightMeltFactor,
        };

        /// <summary>True when a map of this region or scene keeps Winter all year.</summary>
        public bool IsAlwaysWinter(string region, string sceneName)
        {
            for (int i = 0; i < alwaysWinterRegions.Count; i++)
            {
                string r = alwaysWinterRegions[i];
                if (string.IsNullOrWhiteSpace(r)) continue;
                if (string.Equals(r, region, StringComparison.OrdinalIgnoreCase)) return true;
                if (!string.IsNullOrEmpty(sceneName) && sceneName.StartsWith(r, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Rain and snow particle rate for a World Effects tier.</summary>
        public float PrecipitationScale(WorldEffects tier) => ByTier(precipitationByTier, (int)tier, 1f);

        /// <summary>Track map texels for a World Effects tier.</summary>
        public int TrackTexels(WorldEffects tier) => (int)ByTier(trackTexelsByTier, (int)tier, 256);

        static float ByTier(float[] values, int tier, float fallback) =>
            values == null || values.Length == 0 ? fallback : values[Mathf.Clamp(tier, 0, values.Length - 1)];

        static float ByTier(int[] values, int tier, int fallback) =>
            values == null || values.Length == 0 ? fallback : values[Mathf.Clamp(tier, 0, values.Length - 1)];

        /// <summary>Where the one asset lives: <c>World/Resources/SeasonSettings.asset</c>.</summary>
        public const string ResourcePath = "SeasonSettings";
        static SeasonSettings s_Loaded;
        static bool s_Looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Loaded = null; s_Looked = false; }

        /// <summary>The one settings asset (null when it is missing: no seasons, everything as before).</summary>
        public static SeasonSettings Instance
        {
            get
            {
                if (!s_Looked)
                {
                    s_Looked = true;
                    s_Loaded = Resources.Load<SeasonSettings>(ResourcePath);
                }
                return s_Loaded;
            }
        }
    }
}
