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

        [Header("Snow build-up (server; owner 14 Nov: light snow slow and low, bad snow fast and high)")]
        [Tooltip("Snow intensity counted as light snow (WP_Snow is 0.6) and as the worst (WP_Blizzard is 1). In between, the rate and the " +
                 "height it stops at are blended.")]
        [Range(0f, 1f)] public float lightSnowIntensity = 0.5f;
        [Range(0f, 1f)] public float heavySnowIntensity = 1f;
        [Tooltip("Light snow: minutes it would take to reach full depth (it stops at Light Cap first), and the cover it stops at.")]
        [Min(0.1f)] public float lightBuildMinutes = 40f;
        [Range(0f, 1f)] public float lightCap = 0.35f;
        [Tooltip("The worst snow (a blizzard): minutes from bare to Heavy Cap, and the cover it stops at.")]
        [Min(0.1f)] public float heavyBuildMinutes = 8f;
        [Range(0f, 1f)] public float heavyCap = 1f;
        [Tooltip("Metres of snow at full cover (1). The ground rises by cover x this; grass shorter than the depth is buried.")]
        [Range(0f, 1f)] public float snowDepthMetres = 0.4f;
        [Tooltip("Snow settles fully on surfaces flatter than this (degrees from level).")]
        [Range(0f, 90f)] public float snowFullBelowDegrees = 35f;
        [Tooltip("No snow sticks on surfaces steeper than this (degrees from level); smooth in between.")]
        [Range(0f, 90f)] public float snowNoneAboveDegrees = 55f;
        [Tooltip("How much of the depth is shown as a real raised layer, per World Effects tier (Low, Medium, High, Ultra). 0 = colour only.")]
        public float[] snowLiftByTier = { 0f, 1f, 1f, 1f };
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
        public Color snowColor = new Color(0.6f, 0.65f, 0.74f);
        [Tooltip("Roofs and ledges show snow this much sooner than the ground.")]
        [Range(0f, 0.5f)] public float roofBias = 0.1f;
        [Tooltip("How soft the snow's ragged edge is.")]
        [Range(0.01f, 0.3f)] public float snowEdgeSoftness = 0.08f;
        [Tooltip("How much darker the thin rim of a footprint is.")]
        [Range(0f, 1f)] public float trackRimDarkness = 0.25f;
        [Tooltip("Grass height under full cover where the snow is not shown raised (Low): 1 = stands, 0.35 = pressed well down.")]
        [Range(0.05f, 1f)] public float grassHeightUnderSnow = 0.35f;
        [Tooltip("How much each tree turns before or after the others (0 = the whole forest at once).")]
        [Range(0f, 1f)] public float leafTurnSpread = 0.25f;

        [Header("Leaves through the year (owner 14 Nov: fall turns every kind its own colour, bare before Winter)")]
        [Tooltip("Share of Autumn by which the leaves have fully turned.")]
        [Range(0.05f, 1f)] public float autumnTurnBy = 0.45f;
        [Tooltip("Share of Autumn when the canopies start to thin, and the share left of a deciduous canopy when bare.")]
        [Range(0f, 1f)] public float autumnDropFrom = 0.5f;
        [Range(0f, 1f)] public float bareCanopy = 0.08f;
        [Tooltip("Share of Spring by which the leaves are all back.")]
        [Range(0.05f, 1f)] public float springLeafOutBy = 0.35f;
        [Tooltip("The dull, almost yellow tone grass and leaves take on through Autumn (times the art), and the share of Autumn it is reached by.")]
        public Color autumnDull = new Color(0.86f, 0.78f, 0.42f);
        [Range(0.05f, 1f)] public float autumnDullBy = 0.7f;
        [Tooltip("Each tree kind's fall colours (index 1 up = the kind tagged on its prefab; Krytical Nova > World > Seasons > Tag Tree Kinds). " +
                 "Index 0 is any foliage with no kind (bushes, grass cards). At most 15 kinds.")]
        public List<LeafKind> leafKinds = new List<LeafKind>();

        [Header("Falling leaves (every client)")]
        [Tooltip("Falling and blowing leaves at the height of Autumn, times the windy-day rate, per World Effects tier (Low, Medium, High, Ultra).")]
        public float[] fallLeavesByTier = { 0f, 3f, 6f, 8f };
        [Tooltip("Flower density changes only in steps this big (a terrain rebuilds its detail patches on each change).")]
        [Range(0.05f, 0.5f)] public float flowerStep = 0.1f;

        [Header("Footprints (every client; owner 14 Nov: a print where each foot plants, no trail)")]
        [Tooltip("The track compute (Rendering/Shaders/Seasons/SnowTrack.compute).")]
        public ComputeShader trackCompute;
        [Tooltip("Metres of ground round the camera the footprint map covers.")]
        [Min(8f)] public float trackMetres = 24f;
        [Tooltip("Footprint map texels across, per World Effects tier (Low, Medium, High, Ultra). 1024 over 24 m = 2.3 cm a texel.")]
        public int[] trackTexelsByTier = { 256, 512, 1024, 1024 };
        [Tooltip("Characters whose feet are watched for prints, nearest the camera first, per World Effects tier.")]
        public int[] printWalkersByTier = { 4, 12, 24, 32 };
        [Tooltip("A boot print: length and width in metres.")]
        public Vector2 bootPrint = new Vector2(0.3f, 0.12f);
        [Tooltip("A paw print (mounts, beasts): length and width in metres, times the body's size.")]
        public Vector2 pawPrint = new Vector2(0.16f, 0.14f);
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
            LightIntensity = lightSnowIntensity,
            HeavyIntensity = heavySnowIntensity,
            LightBuildPerSecond = 1f / (lightBuildMinutes * 60f),
            HeavyBuildPerSecond = 1f / (heavyBuildMinutes * 60f),
            LightCap = lightCap,
            HeavyCap = heavyCap,
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

        /// <summary>How much of the snow's depth is shown raised on a World Effects tier (0 = colour only).</summary>
        public float SnowLift(WorldEffects tier) => ByTier(snowLiftByTier, (int)tier, 1f);

        /// <summary>Characters watched for footprints on a World Effects tier.</summary>
        public int PrintWalkers(WorldEffects tier) => (int)ByTier(printWalkersByTier, (int)tier, 12);

        /// <summary>Falling-leaf rate at the height of Autumn on a World Effects tier.</summary>
        public float FallLeaves(WorldEffects tier) => ByTier(fallLeavesByTier, (int)tier, 1f);

        /// <summary>The leaves' year, for <see cref="SeasonRules.FoliageAt"/>.</summary>
        public FoliageCurve Foliage => new FoliageCurve
        {
            TurnBy = autumnTurnBy, DropFrom = autumnDropFrom, Bare = bareCanopy, LeafOutBy = springLeafOutBy, DullBy = autumnDullBy,
        };

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
