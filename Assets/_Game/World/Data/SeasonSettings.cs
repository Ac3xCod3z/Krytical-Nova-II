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

        [Header("Temperature and thaw (server; owner 14 Nov: build-up and defrost periods)")]
        [Tooltip("Snow settles only at or below this air temperature (deg C); warmer snow is sleet and thaws.")]
        [Range(-5f, 3f)] public float snowBelowTemperature = 0.5f;
        [Tooltip("Minutes full cover takes to thaw at +1 C in the shade (at +4 C it takes a quarter of that). Dry at or below 0 C it holds.")]
        [Min(1f)] public float thawMinutesPerDegree = 240f;
        [Tooltip("Thaw in sunshine, times more than in the shade (0.6 = 60 % faster by day).")]
        [Range(0f, 3f)] public float sunThawBonus = 0.6f;
        [Tooltip("Thaw while it rains (or sleets), times the dry thaw.")]
        [Min(0f)] public float rainMeltFactor = 3f;
        [Tooltip("The warmest hour of the day as a share of the clock (0.625 = 15:00); the coldest is just before sunrise.")]
        [Range(0.4f, 0.9f)] public float warmestClock = 0.625f;
        [Tooltip("Each day is up to this many degrees warmer or colder than its season and spell (from the world's seed).")]
        [Range(0f, 10f)] public float dayWobble = 2.5f;
        [Tooltip("The temperature is sent in steps this big (the snow's trend and the clients' heat change only per step).")]
        [Range(0.1f, 2f)] public float temperatureStep = 0.5f;

        [Header("Weather spells (server; owner 14 Nov: runs of days, a worst storm tier)")]
        [Tooltip("The world's weather seed: with the map's name it decides every day's spell, the same on every restart.")]
        public int weatherSeed = 20261114;
        [Tooltip("Spells are rolled in blocks of this many in-game days (a spell is cut at a block's end).")]
        [Range(8, 256)] public int spellBlockDays = 64;
        [Tooltip("An always-winter map never has less cover than this.")]
        [Range(0f, 1f)] public float alwaysWinterFloor = 0.6f;

        [Header("Always-winter maps (owner 13 Nov: the Sunderpeak maps only)")]
        [Tooltip("A map whose Zone Catalog region (or scene name) is listed here keeps Winter's weather, colours and snow all year, but takes " +
                 "the calendar's daylight (short winter days, long summer ones).")]
        public List<string> alwaysWinterRegions = new List<string> { "Sunderpeak" };

        [Header("Look (every client)")]
        [Tooltip("The flat comic colour of snow (no texture).")]
        public Color snowColor = new Color(0.44f, 0.48f, 0.56f);
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

        [Header("Drifts (every client; owner 14 Nov: the max is a ceiling, never a flat sheet)")]
        [Tooltip("Metres across a drift and dip of the low wavy field.")]
        [Min(2f)] public float driftSize = 18f;
        [Tooltip("How much deeper and shallower drifts and dips are than the snow's mean (0.4 = +-40 %); the max depth stays the ceiling.")]
        [Range(0f, 1f)] public float driftAmount = 0.4f;
        [Tooltip("Small ripples on top, share of the depth.")]
        [Range(0f, 0.5f)] public float rippleAmount = 0.12f;
        [Tooltip("How much thinner snow lies under a tree's canopy (1 = none under it).")]
        [Range(0f, 1f)] public float underCanopyThinning = 0.65f;
        [Tooltip("How much deeper snow piles against the windward side of walls, buildings and rocks (share of the depth).")]
        [Range(0f, 1.5f)] public float windwardPile = 0.6f;
        [Tooltip("Metres a pile reaches out from the wall it lies against.")]
        [Range(0.5f, 8f)] public float pileReach = 2.5f;
        [Tooltip("Slopes hold thinner snow than flat ground before they shed it (share at the steepest angle that holds any).")]
        [Range(0f, 1f)] public float slopeThinning = 0.5f;
        [Tooltip("Roof middles hold this share more than their edges (ridges and eaves shed).")]
        [Range(0f, 0.5f)] public float roofMiddle = 0.15f;
        [Tooltip("While it thaws, surfaces facing the sun lose their snow this much sooner.")]
        [Range(0f, 0.5f)] public float sunFacingThaw = 0.25f;
        [Tooltip("Metres round the camera the shelter map (canopies, walls, roads) covers, and its texels across per World Effects tier.")]
        [Min(32f)] public float shelterMetres = 160f;
        public int[] shelterTexelsByTier = { 0, 256, 512, 512 };
        [Tooltip("Shapes stamped into the shelter map at most (nearest the camera first).")]
        [Range(16, 512)] public int shelterShapesMax = 256;

        [Header("Town roads (every client; owner 14 Nov: thinner, trodden roads)")]
        [Tooltip("Renderers whose material (or mesh) name contains any of these are roads and paths: much less snow, packed and slushy.")]
        public List<string> roadNameContains = new List<string> { "Road", "Street", "Pavement", "Sidewalk", "Path", "Plaza", "Crossing", "Curb", "Kerb" };
        [Tooltip("Share of the snow a road keeps (0.15 = a thin trodden layer while open ground and roofs fill).")]
        [Range(0f, 1f)] public float roadSnowShare = 0.15f;
        [Tooltip("The packed, slushy colour of trodden snow (multiplies the snow colour).")]
        public Color slushColor = new Color(0.62f, 0.6f, 0.6f);

        [Header("Ploughed paths (every client; owner 14 Nov: deep snow leaves a path, not prints)")]
        [Tooltip("Snow deeper than this (m) at the feet makes a continuous path instead of footprints; blended near it.")]
        [Range(0.02f, 0.4f)] public float printMaxDepth = 0.12f;
        [Tooltip("Half the path's width for a person (m); mounts and beasts times their size.")]
        [Range(0.1f, 1.5f)] public float pathHalfWidth = 0.32f;
        [Tooltip("How far down a path presses the raised snow (1 = to the ground).")]
        [Range(0f, 1f)] public float pathPress = 0.8f;
        [Tooltip("Metres of a stride for a person (a print every stride, left and right in turn, whatever the walk clip does); times the body's size.")]
        [Range(0.3f, 2f)] public float strideMetres = 0.8f;

        [Header("Spring (every client; owner 14 Nov: the world comes back to life)")]
        [Tooltip("Share of Spring by which the grass is green and full height again, and its height at the end of Winter (share of full).")]
        [Range(0.05f, 1f)] public float springGrassBy = 0.4f;
        [Range(0.1f, 1f)] public float winterGrassHeight = 0.45f;
        [Tooltip("The brown, flattened tone of grass after the snow (multiplies the art), and the fresh spring green it turns to.")]
        public Color thawGrass = new Color(0.72f, 0.62f, 0.42f);
        public Color springGreen = new Color(0.9f, 1.12f, 0.8f);
        [Tooltip("Pale-green buds on bare trees early in Spring.")]
        public Color budColor = new Color(0.62f, 0.82f, 0.42f);
        [Tooltip("Shares of Spring when blossom opens, is at its fullest and is gone (the petals fall in the last part).")]
        public Vector3 blossomShares = new Vector3(0.05f, 0.22f, 0.5f);
        [Tooltip("Petals drifting while blossom drops, times the falling-leaf rate.")]
        [Range(0f, 2f)] public float petalDrift = 0.8f;

        [Header("Summer (every client; owner 14 Nov: lush, hot afternoons shimmer)")]
        [Tooltip("Temperatures (deg C) where the heat haze starts and is at its strongest (only by day).")]
        public Vector2 heatHazeFromTo = new Vector2(27f, 34f);
        [Tooltip("Heat haze strength per World Effects tier (Low, Medium, High, Ultra). 0 = off (Low).")]
        public float[] heatHazeByTier = { 0f, 0.6f, 1f, 1f };
        [Tooltip("Metres from the camera where the shimmer begins and where it is full.")]
        public Vector2 heatHazeDistance = new Vector2(25f, 90f);
        [Tooltip("Dry days in a row where the grass starts to go golden, and where it is fully golden.")]
        public Vector2 goldenGrassDays = new Vector2(3f, 7f);
        public Color goldenGrass = new Color(1.12f, 0.92f, 0.52f);
        [Tooltip("A touch more warmth in the sunlight on hot afternoons (multiplies the sun's colour at full heat).")]
        public Color hotSunWarmth = new Color(1.06f, 1f, 0.9f);
        [Tooltip("Dust and pollen motes by day in Summer and late Spring, and fireflies in the grass at dusk; the counts per World Effects tier.")]
        public Material motesMaterial;
        public int[] motesByTier = { 0, 40, 90, 140 };
        public int[] firefliesByTier = { 0, 30, 70, 110 };
        [Tooltip("Fireflies come out at or above this temperature (deg C) from sunset to the middle of the night.")]
        [Range(0f, 30f)] public float fireflyTemperature = 14f;
        [Tooltip("The motes' colour (alpha = how strong), their size in metres, how long one lives, and the box around the camera they fill.")]
        public Color motesColor = new Color(1f, 0.93f, 0.72f, 0.5f);
        public float motesSize = 0.05f;
        public float motesLifetime = 7f;
        public Vector3 motesBox = new Vector3(26f, 7f, 26f);
        [Tooltip("The fireflies' glow colour, size in metres, life, the box they fill, and how far below the camera its middle sits (in the grass).")]
        public Color fireflyColor = new Color(0.78f, 1f, 0.35f, 1f);
        public float fireflySize = 0.11f;
        public float fireflyLifetime = 5f;
        public Vector3 fireflyBox = new Vector3(22f, 1.8f, 22f);
        public float fireflyHeight = -2.2f;

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
            SnowBelowC = snowBelowTemperature,
            ThawPerDegreeSecond = 1f / (thawMinutesPerDegree * 60f),
            SunThawBonus = sunThawBonus,
            RainFactor = rainMeltFactor,
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

        /// <summary>Shelter map texels on a World Effects tier (0 = none: Low keeps the plain field).</summary>
        public int ShelterTexels(WorldEffects tier) => (int)ByTier(shelterTexelsByTier, (int)tier, 0);

        /// <summary>Heat haze strength on a World Effects tier (0 = off).</summary>
        public float HeatHaze(WorldEffects tier) => ByTier(heatHazeByTier, (int)tier, 0f);

        /// <summary>The tree kind (1 up) whose words a prefab or object name holds, 0 for none; the first kind that matches wins. Used by
        /// the Tag Tree Kinds builder on Foliage Wind prefabs and at runtime on the town's Synty trees (their prefabs are never edited).</summary>
        public int LeafKindOf(string name)
        {
            if (leafKinds == null || string.IsNullOrEmpty(name)) return 0;
            for (int i = 0; i < leafKinds.Count && i < 15; i++)
            {
                LeafKind k = leafKinds[i];
                if (k?.prefabNameContains == null) continue;
                foreach (string word in k.prefabNameContains)
                    if (!string.IsNullOrWhiteSpace(word) && name.IndexOf(word.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return i + 1;
            }
            return 0;
        }

        /// <summary>Dust motes and fireflies on a World Effects tier.</summary>
        public int Motes(WorldEffects tier) => (int)ByTier(motesByTier, (int)tier, 0);
        public int Fireflies(WorldEffects tier) => (int)ByTier(firefliesByTier, (int)tier, 0);

        /// <summary>True when a material or mesh name is a road's (<see cref="roadNameContains"/>).</summary>
        public bool IsRoadName(string name)
        {
            if (string.IsNullOrEmpty(name) || roadNameContains == null) return false;
            for (int i = 0; i < roadNameContains.Count; i++)
                if (!string.IsNullOrEmpty(roadNameContains[i]) && name.IndexOf(roadNameContains[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>Falling-leaf rate at the height of Autumn on a World Effects tier.</summary>
        public float FallLeaves(WorldEffects tier) => ByTier(fallLeavesByTier, (int)tier, 1f);

        /// <summary>The leaves' year, for <see cref="SeasonRules.FoliageAt"/>.</summary>
        public FoliageCurve Foliage => new FoliageCurve
        {
            TurnBy = autumnTurnBy, DropFrom = autumnDropFrom, Bare = bareCanopy, LeafOutBy = springLeafOutBy, DullBy = autumnDullBy,
            GrassBy = springGrassBy, BlossomFrom = blossomShares.x, BlossomPeak = blossomShares.y, BlossomGone = blossomShares.z,
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
