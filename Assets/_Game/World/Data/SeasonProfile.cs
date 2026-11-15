using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>The four seasons, in the order the calendar turns them (owner's DAY list item 21). One byte on the wire.</summary>
    public enum Season : byte { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    /// <summary>
    /// One season's weather mix, daylight and look (owner's DAY list item 21; design note <c>_Docs/Plans/seasons-plan.md</c>). The server
    /// uses the weather part to pick the next weather; every client uses the rest to colour grass and trees and to light the day. Four of
    /// them, one per season, named in <see cref="SeasonSettings"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Season Profile")]
    public sealed class SeasonProfile : ScriptableObject
    {
        /// <summary>How much more (or less) often a weather is picked in this season. Weathers not listed keep their weight (x1).</summary>
        [Serializable]
        public struct WeatherWeight
        {
            public WeatherProfile profile;
            [Min(0f)] public float multiplier;
        }

        public Season season;

        [Header("Daylight (owner 13 Nov: less daylight inside the same one-hour day)")]
        [Tooltip("Share of the day the sun is up. 0.5 = 06:00-18:00; 0.375 = 07:30-16:30 (short winter days); 0.625 = 04:30-19:30. " +
                 "The clock still runs one real hour a day: only the sun's path is warped (SeasonRules.SunTime).")]
        [Range(0.2f, 0.8f)] public float daylightShare = 0.5f;

        [Header("Weather mix (server)")]
        [Tooltip("Multiplies the weights of each weather's Next list while this season holds. 0 = never in this season.")]
        public List<WeatherWeight> weatherWeights = new List<WeatherWeight>();
        [Tooltip("Extra choices added to every weather pick in this season (for example Snow in Winter, even after a clear sky).")]
        public List<WeatherProfile.NextWeather> extraNext = new List<WeatherProfile.NextWeather>();

        [Header("Temperature (owner 14 Nov: snow builds below 0 C and thaws above it)")]
        [Tooltip("The season's mean air temperature, deg C.")]
        [Range(-30f, 40f)] public float baseTemperature = 10f;
        [Tooltip("Degrees between the coldest hour (before dawn) and the warmest (mid-afternoon).")]
        [Range(0f, 25f)] public float dailySwing = 8f;

        [Header("Weather spells (server; owner 14 Nov: runs of days, not a fresh roll every hour)")]
        [Tooltip("The kinds of days this season rolls, each lasting a few in-game days: a wet run, a dry spell, a cold snap, a storm. The " +
                 "day's spell multiplies the weather weights above and adds its own choices. Empty = every day as the season's mix.")]
        public List<WeatherSpell> spells = new List<WeatherSpell>();

        [Header("Grass")]
        [Tooltip("Multiplies every grass blade's colour (white = the art as it is).")]
        public Color grassTint = Color.white;
        [Tooltip("How pale the blade tips go (frost).")]
        [Range(0f, 1f)] public float grassTipFrost = 0f;

        [Header("Trees and bushes")]
        [Tooltip("The colour the green part of the foliage atlas turns to (trunks and rocks stay).")]
        public Color leafColor = new Color(0.45f, 0.7f, 0.25f);
        [Tooltip("How far the leaves turn to Leaf Color (0 = the art's own green).")]
        [Range(0f, 1f)] public float leafAmount = 0f;
        [Tooltip("Frost on the top of every canopy, all day (owner 13 Nov: winter trees are frosted, leaves kept).")]
        [Range(0f, 1f)] public float frost = 0f;
        [Tooltip("Extra frost at night.")]
        [Range(0f, 1f)] public float nightFrost = 0f;

        [Header("Flowers")]
        [Tooltip("Terrain detail (flower) density, times the map's own. Changed in small steps only while the season blends.")]
        [Range(0f, 1f)] public float flowerDensity = 1f;

        /// <summary>The temperature curve, for <see cref="SeasonRules.TemperatureAt"/>.</summary>
        public TemperatureCurve Temperature => new TemperatureCurve { BaseC = baseTemperature, SwingC = dailySwing };

        /// <summary>The spells as the pure rules see them (cached; edits in Play are picked up on the next domain reload).</summary>
        public IReadOnlyList<SpellChoice> SpellTable
        {
            get
            {
                if (m_Table == null || m_Table.Count != spells.Count)
                {
                    m_Table = new List<SpellChoice>(spells.Count);
                    for (int i = 0; i < spells.Count; i++)
                        m_Table.Add(spells[i] != null
                            ? new SpellChoice { Chance = spells[i].chance, MinDays = spells[i].minDays, MaxDays = spells[i].maxDays }
                            : default);
                }
                return m_Table;
            }
        }
        [NonSerialized] List<SpellChoice> m_Table;

        /// <summary>A spell by index (null when out of range).</summary>
        public WeatherSpell Spell(int index) => index >= 0 && index < spells.Count ? spells[index] : null;

        /// <summary>This season's multiplier for a weather (1 when not listed).</summary>
        public float WeightOf(WeatherProfile weather)
        {
            if (weather == null) return 1f;
            for (int i = 0; i < weatherWeights.Count; i++)
                if (weatherWeights[i].profile == weather) return weatherWeights[i].multiplier;
            return 1f;
        }
    }

    /// <summary>
    /// One kind of tree's fall colours (owner, 14 Nov 2026: every kind has its own, a forest is a mix). Its index in
    /// <see cref="SeasonSettings.leafKinds"/> (from 1) is stamped on its prefabs' renderers (rendering layer bits 8-11) by the Tag Tree
    /// Kinds builder, so the foliage shader picks it with no script on any tree.
    /// </summary>
    [Serializable]
    public sealed class LeafKind
    {
        public string name = "Maple";
        [Tooltip("Prefabs whose name contains any of these (any case) are this kind.")]
        public List<string> prefabNameContains = new List<string>();
        [Tooltip("Evergreen (pines, firs): never turns, never drops; frost and snow only.")]
        public bool evergreen;
        [Tooltip("The colour at the height of the turn, and the colour it fades to as the leaves fall. Each tree picks its own mix of the two.")]
        public Color peak = new Color(0.78f, 0.16f, 0.08f);
        public Color late = new Color(0.55f, 0.3f, 0.12f);
        [Tooltip("Spring blossom (owner 14 Nov: some kinds flower pink or white early in Spring, then drop their petals).")]
        public bool blossoms;
        public Color blossom = new Color(0.98f, 0.78f, 0.86f);
    }

    /// <summary>
    /// One kind of weather spell (owner, 14 Nov 2026: rainy for a few days in spring and summer, longer dry spells in summer, not every
    /// winter day snows, a worst storm tier). The server rolls a run of spells from the world's seed; each lasts between its min and max
    /// in-game days and, while it holds, scales the season's weather weights and warms or cools the air.
    /// </summary>
    [Serializable]
    public sealed class WeatherSpell
    {
        public string name = "Mixed";
        [Tooltip("How often this spell is rolled, against the season's other spells.")]
        [Min(0f)] public float chance = 1f;
        [Tooltip("In-game days it lasts (one real hour each).")]
        [Min(1)] public int minDays = 1;
        [Min(1)] public int maxDays = 3;
        [Tooltip("Degrees it adds to the season's temperature (a cold snap below 0, a heatwave above).")]
        [Range(-20f, 20f)] public float temperatureOffset = 0f;
        [Tooltip("A wet run (rain most of the day).")]
        public bool wet;
        [Tooltip("A dry spell (counts toward golden summer grass).")]
        public bool dry;
        [Tooltip("A snow day (Winter).")]
        public bool snowy;
        [Tooltip("A storm day (the worst tier: heavy rain, strong wind, lightning).")]
        public bool storm;
        [Tooltip("Times the season's weight of each weather listed (weathers not listed keep the season's weight).")]
        public List<SeasonProfile.WeatherWeight> weatherWeights = new List<SeasonProfile.WeatherWeight>();
        [Tooltip("Extra choices added to every weather pick while this spell holds.")]
        public List<WeatherProfile.NextWeather> extraNext = new List<WeatherProfile.NextWeather>();

        /// <summary>This spell's multiplier for a weather (1 when not listed).</summary>
        public float WeightOf(WeatherProfile weather)
        {
            if (weather == null) return 1f;
            for (int i = 0; i < weatherWeights.Count; i++)
                if (weatherWeights[i].profile == weather) return weatherWeights[i].multiplier;
            return 1f;
        }
    }
}
