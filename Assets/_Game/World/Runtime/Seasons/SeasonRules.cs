using System;
using System.Collections.Generic;

namespace KryticalNova.World
{
    /// <summary>The season calendar in seconds (from <see cref="SeasonSettings.Calendar"/>; tests build their own).</summary>
    public struct SeasonCalendar
    {
        /// <summary>Unix seconds (UTC) when the first Spring began.</summary>
        public double YearStartUnix;
        /// <summary>Real seconds a season lasts.</summary>
        public double SeasonSeconds;
        /// <summary>Seconds the whole calendar is moved (turn hour + offset days).</summary>
        public double OffsetSeconds;
        /// <summary>Real seconds a new season takes to blend in at its start.</summary>
        public double TransitionSeconds;
    }

    /// <summary>Where the calendar stands at a moment: the season, when it began and how far in it is.</summary>
    public struct SeasonNow
    {
        public Season Season;
        /// <summary>Seasons since the first Spring (negative before it).</summary>
        public long Index;
        /// <summary>Unix seconds when this season began.</summary>
        public double StartUnix;
        /// <summary>Real seconds since it began.</summary>
        public double Elapsed;
        /// <summary>The season before it.</summary>
        public Season Previous => (Season)(((int)Season + 3) % 4);
        /// <summary>Day of the season, from 1.</summary>
        public int Day => (int)Math.Floor(Elapsed / 86400.0) + 1;
    }

    /// <summary>The snow rates in cover per second (from <see cref="SeasonSettings.Rates"/>).</summary>
    public struct SnowRates
    {
        public float LightIntensity, HeavyIntensity, LightBuildPerSecond, HeavyBuildPerSecond, LightCap, HeavyCap;
        public float MeltDay, NightFactor, RainFactor, WinterMeltDay, WinterNightFactor;
    }

    /// <summary>Where the snow is heading now: its rate (cover per second, signed) and the cover a build-up stops at.</summary>
    public struct SnowTrend
    {
        public float Rate, Ceiling;
    }

    /// <summary>The leaves' year in shares of a season (from <see cref="SeasonSettings.Foliage"/>).</summary>
    public struct FoliageCurve
    {
        public float TurnBy, DropFrom, Bare, LeafOutBy, DullBy;
    }

    /// <summary>
    /// The leaves at a moment: <see cref="Turn"/> 0 green .. 1 every kind's fall colour, <see cref="Canopy"/> 1 full .. bare share,
    /// <see cref="Dull"/> 0 .. 1 the autumn dull tone on grass and leaves, <see cref="Falling"/> 0 .. 1 how many leaves come down.
    /// </summary>
    public struct FoliageNow
    {
        public float Turn, Canopy, Dull, Falling;

        public static FoliageNow Lerp(in FoliageNow a, in FoliageNow b, float t) => new FoliageNow
        {
            Turn = a.Turn + (b.Turn - a.Turn) * t, Canopy = a.Canopy + (b.Canopy - a.Canopy) * t,
            Dull = a.Dull + (b.Dull - a.Dull) * t, Falling = a.Falling + (b.Falling - a.Falling) * t,
        };
    }

    /// <summary>
    /// The seasons' maths, pure and tested (owner's DAY list item 21; <c>SeasonRulesTests</c>): the season from real UTC time, the blend
    /// between two seasons, the daylight warp (short winter days inside the same one-hour day), the snow cover's build and melt, and the
    /// weather pick with the season's weights. Used by the server (which decides) and by every client (which draws).
    /// </summary>
    public static class SeasonRules
    {
        /// <summary>The season at <paramref name="unixSeconds"/> (UTC). Seasons turn Spring, Summer, Autumn, Winter and around.</summary>
        public static SeasonNow At(in SeasonCalendar c, double unixSeconds)
        {
            double length = c.SeasonSeconds > 0 ? c.SeasonSeconds : 14 * 86400.0;
            double origin = c.YearStartUnix + c.OffsetSeconds;
            long index = (long)Math.Floor((unixSeconds - origin) / length);
            int season = (int)(((index % 4) + 4) % 4);
            double start = origin + index * length;
            return new SeasonNow { Season = (Season)season, Index = index, StartUnix = start, Elapsed = unixSeconds - start };
        }

        /// <summary>Unix seconds when the next season begins.</summary>
        public static double NextTurnUnix(in SeasonCalendar c, double unixSeconds)
        {
            SeasonNow now = At(c, unixSeconds);
            return now.StartUnix + (c.SeasonSeconds > 0 ? c.SeasonSeconds : 14 * 86400.0);
        }

        /// <summary>A blend that began at <paramref name="start"/> and lasts <paramref name="seconds"/>: 0 to 1, smooth at both ends.</summary>
        public static float Blend(double start, double seconds, double now)
        {
            if (seconds <= 0.0) return now >= start ? 1f : 0f;
            double t = (now - start) / seconds;
            if (t <= 0.0) return 0f;
            if (t >= 1.0) return 1f;
            float x = (float)t;
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// The daylight warp (owner 13 Nov: less daylight inside the same one-hour day). The clock's day stays as it is; the sun's path is
        /// moved so that the clock's sunrise-sunset span (<paramref name="daylightShare"/> of the day, centred on noon) lands on 0.25-0.75 of
        /// the art's curves and the night on the rest. Continuous, noon stays 0.5 and midnight 0; share 0.5 changes nothing.
        /// </summary>
        public static float SunTime(float clock01, float daylightShare)
        {
            float share = Math.Clamp(daylightShare, 0.05f, 0.95f);
            float t = clock01 - (float)Math.Floor(clock01);
            float rise = 0.5f - share * 0.5f, set = 0.5f + share * 0.5f;
            if (t >= rise && t <= set) return 0.25f + (t - rise) / share * 0.5f;
            // the night, from sunset over midnight to sunrise, onto 0.75 .. 1.25
            float night = 1f - share;
            float since = t > set ? t - set : t + 1f - set;
            float warped = 0.75f + since / night * 0.5f;
            return warped >= 1f ? warped - 1f : warped;
        }

        /// <summary>The clock time (0-1) of sunrise for a daylight share.</summary>
        public static float Sunrise(float daylightShare) => 0.5f - Math.Clamp(daylightShare, 0.05f, 0.95f) * 0.5f;

        /// <summary>True while the warped sun is down.</summary>
        public static bool IsNight(float clock01, float daylightShare)
        {
            float s = SunTime(clock01, daylightShare);
            return s < 0.25f || s > 0.75f;
        }

        /// <summary>
        /// Where the snow cover is heading now (owner, 14 Nov 2026: like real life, light snow builds slowly and stays low, bad snow
        /// builds fast and high). While it snows it builds toward a ceiling: light snow slowly to a low ceiling, a blizzard fast to a high
        /// one (blended between by intensity; flurries under light snow build slower still). Snow already deeper than the ceiling stays as
        /// it is while it snows. Otherwise it melts by day, lingers at night, washes off in rain and lasts in Winter (an always-winter map
        /// melts as in Winter).
        /// </summary>
        public static SnowTrend SnowTrendOf(in SnowRates r, PrecipitationType falling, float intensity, bool night, Season season,
            bool alwaysWinter, float coverNow)
        {
            if (falling == PrecipitationType.Snow && intensity > 0.001f)
            {
                float t = SnowSeverity(r, intensity);
                float cap = r.LightCap + (r.HeavyCap - r.LightCap) * t;
                float rate = r.LightBuildPerSecond + (r.HeavyBuildPerSecond - r.LightBuildPerSecond) * t;
                if (r.LightIntensity > 0.001f && intensity < r.LightIntensity) rate *= intensity / r.LightIntensity;
                return coverNow >= cap ? new SnowTrend { Rate = 0f, Ceiling = 1f } : new SnowTrend { Rate = rate, Ceiling = cap };
            }
            bool winter = alwaysWinter || season == Season.Winter;
            float melt = winter ? r.WinterMeltDay * (night ? r.WinterNightFactor : 1f) : r.MeltDay * (night ? r.NightFactor : 1f);
            if (falling == PrecipitationType.Rain && intensity > 0.001f) melt = Math.Max(melt, r.MeltDay) * r.RainFactor;
            return new SnowTrend { Rate = -melt, Ceiling = 1f };
        }

        /// <summary>
        /// The slope rule's two shader numbers (owner, 14 Nov 2026: snow settles on what is flat enough, like real life): the cosine of the
        /// steepest angle snow holds on, and one over the span to the angle it settles fully under. <c>KNSnowSlope</c> uses them.
        /// </summary>
        public static (float CosNone, float InvRange) SnowSlopeShape(float fullBelowDegrees, float noneAboveDegrees)
        {
            float none = Math.Clamp(noneAboveDegrees, 1f, 90f);
            float full = Math.Clamp(Math.Min(fullBelowDegrees, none - 1f), 0f, 89f);
            float cosNone = (float)Math.Cos(none * Math.PI / 180.0), cosFull = (float)Math.Cos(full * Math.PI / 180.0);
            return (cosNone, 1f / Math.Max(0.01f, cosFull - cosNone));
        }

        /// <summary>How much snow a surface holds by its slope, 0-1: the C# twin of <c>KNSnowSlope</c> (normalY = the world normal's up part).</summary>
        public static float SnowSlope(float normalY, float fullBelowDegrees, float noneAboveDegrees)
        {
            var (cosNone, inv) = SnowSlopeShape(fullBelowDegrees, noneAboveDegrees);
            return Math.Clamp((normalY - cosNone) * inv, 0f, 1f);
        }

        /// <summary>0 at light snow (or less), 1 at the worst snow: how far toward a blizzard this intensity is.</summary>
        public static float SnowSeverity(in SnowRates r, float intensity)
        {
            float span = r.HeavyIntensity - r.LightIntensity;
            if (span <= 0.0001f) return intensity >= r.HeavyIntensity ? 1f : 0f;
            return Math.Clamp((intensity - r.LightIntensity) / span, 0f, 1f);
        }

        /// <summary>The cover at <paramref name="now"/> from an anchor: never below the floor, never above the ceiling (or 1).</summary>
        public static float SnowCover(float coverAtAnchor, float ratePerSecond, double anchor, double now, float floor, float ceiling = 1f)
        {
            double c = coverAtAnchor + ratePerSecond * Math.Max(0.0, now - anchor);
            float lo = Math.Clamp(floor, 0f, 1f);
            float hi = Math.Max(lo, Math.Clamp(ceiling <= 0f ? 1f : ceiling, 0f, 1f));
            return (float)Math.Clamp(c, lo, hi);
        }

        /// <summary>
        /// The leaves in a season at <paramref name="progress"/> (0 its first day .. 1 its last), owner 14 Nov 2026: through Autumn
        /// the grass and leaves go dull, every kind turns its own colour, and late in Autumn the canopies thin until they are almost bare
        /// before Winter; bare through Winter; back early in Spring. Evergreens ignore Turn and Canopy (the shader keeps them).
        /// </summary>
        public static FoliageNow FoliageAt(Season season, float progress, in FoliageCurve c)
        {
            float p = Math.Clamp(progress, 0f, 1f);
            float bare = Math.Clamp(c.Bare, 0f, 1f);
            switch (season)
            {
                case Season.Spring:
                    return new FoliageNow { Turn = 0f, Canopy = bare + (1f - bare) * Smooth(0f, Math.Max(0.01f, c.LeafOutBy), p) };
                case Season.Summer:
                    return new FoliageNow { Canopy = 1f };
                case Season.Autumn:
                {
                    float drop = Smooth(Math.Clamp(c.DropFrom, 0f, 0.99f), 1f, p);
                    float turn = Smooth(0f, Math.Max(0.01f, c.TurnBy), p);
                    // most leaves come down while the canopy thins, a few from the first turn
                    float falling = Math.Clamp(turn * 0.35f + drop * (1f - drop) * 4f * 0.65f + (p > c.DropFrom && p < 1f ? 0.25f : 0f), 0f, 1f);
                    return new FoliageNow
                    {
                        Turn = turn, Canopy = 1f - (1f - bare) * drop, Dull = Smooth(0f, Math.Max(0.01f, c.DullBy), p), Falling = falling,
                    };
                }
                default:
                    return new FoliageNow { Turn = 1f, Canopy = bare, Dull = 1f, Falling = 0f };
            }
        }

        static float Smooth(float from, float to, float x)
        {
            float t = Math.Clamp((x - from) / Math.Max(1e-4f, to - from), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The season that shows more in a blend (the one blended to from halfway).</summary>
        public static Season Dominant(Season from, Season to, float blend) => blend >= 0.5f ? to : from;

        /// <summary>
        /// One weighted weather pick (<c>WorldEnvironmentNetwork.AdvanceWeather</c>): the current weather's Next list, each weight times the
        /// season's <paramref name="scale"/>, plus the season's <paramref name="extra"/> choices; <paramref name="roll"/> is 0-1. Null when
        /// nothing can be picked (the caller keeps the weather). With no scale and no extra it picks exactly as before seasons.
        /// </summary>
        public static WeatherProfile Pick(IReadOnlyList<WeatherProfile.NextWeather> next, IReadOnlyList<WeatherProfile.NextWeather> extra,
            Func<WeatherProfile, float> scale, float roll)
        {
            float total = Sum(next, scale) + Sum(extra, scale);
            if (total <= 0f) return null;
            float pick = roll * total;
            if (Walk(next, scale, ref pick, out WeatherProfile chosen)) return chosen;
            if (Walk(extra, scale, ref pick, out chosen)) return chosen;
            return null;   // rounding left a sliver: keep the weather, as before seasons
        }

        static float Sum(IReadOnlyList<WeatherProfile.NextWeather> list, Func<WeatherProfile, float> scale)
        {
            float total = 0f;
            if (list == null) return total;
            for (int i = 0; i < list.Count; i++)
                if (list[i].profile != null) total += Weight(list[i], scale);
            return total;
        }

        static bool Walk(IReadOnlyList<WeatherProfile.NextWeather> list, Func<WeatherProfile, float> scale, ref float pick,
            out WeatherProfile chosen)
        {
            chosen = null;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].profile == null) continue;
                float w = Weight(list[i], scale);
                if (w <= 0f) continue;
                pick -= w;
                if (pick <= 0f) { chosen = list[i].profile; return true; }
            }
            return false;
        }

        static float Weight(in WeatherProfile.NextWeather n, Func<WeatherProfile, float> scale) =>
            Math.Max(0f, n.weight * (scale != null ? Math.Max(0f, scale(n.profile)) : 1f));

        /// <summary>A season's name for players and GM lines.</summary>
        public static string Name(Season s) => s.ToString();

        /// <summary>Reads a season name or its first letters (gm.season win); false if it is none.</summary>
        public static bool TryParse(string text, out Season season)
        {
            season = Season.Spring;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string t = text.Trim().ToLowerInvariant();
            if (t == "fall") t = "autumn";
            for (int i = 0; i < 4; i++)
            {
                string n = ((Season)i).ToString().ToLowerInvariant();
                if (t.Length >= 2 && n.StartsWith(t, StringComparison.Ordinal)) { season = (Season)i; return true; }
            }
            return false;
        }
    }
}
