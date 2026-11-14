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

        /// <summary>This season's multiplier for a weather (1 when not listed).</summary>
        public float WeightOf(WeatherProfile weather)
        {
            if (weather == null) return 1f;
            for (int i = 0; i < weatherWeights.Count; i++)
                if (weatherWeights[i].profile == weather) return weatherWeights[i].multiplier;
            return 1f;
        }
    }
}
