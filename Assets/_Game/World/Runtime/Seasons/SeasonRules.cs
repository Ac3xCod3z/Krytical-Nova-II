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
        /// <summary>Snow only settles at or below this temperature (deg C); warmer snow is sleet and thaws.</summary>
        public float SnowBelowC;
        /// <summary>Cover per second that melts for each degree above 0 C (in shade), the share more in sunshine, and the rain's factor.</summary>
        public float ThawPerDegreeSecond, SunThawBonus, RainFactor;
    }

    /// <summary>One season's temperature (from its <see cref="SeasonProfile"/>): the mean, and the swing from before dawn to mid-afternoon.</summary>
    public struct TemperatureCurve
    {
        public float BaseC, SwingC;

        public static TemperatureCurve Lerp(in TemperatureCurve a, in TemperatureCurve b, float t) =>
            new TemperatureCurve { BaseC = a.BaseC + (b.BaseC - a.BaseC) * t, SwingC = a.SwingC + (b.SwingC - a.SwingC) * t };
    }

    /// <summary>One kind of weather spell a season can roll (from <see cref="WeatherSpell"/>): how often, and how many days it lasts.</summary>
    public struct SpellChoice
    {
        public float Chance;
        public int MinDays, MaxDays;
    }

    /// <summary>The spell a day falls in: its index in the season's list (-1 none), the day within it (0 = its first) and its length.</summary>
    public struct SpellDay
    {
        public int Spell, DayInSpell, Length;
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
        /// <summary>Spring: the share by which the grass is green and full again, and blossom's open, fullest and gone shares.</summary>
        public float GrassBy, BlossomFrom, BlossomPeak, BlossomGone;
    }

    /// <summary>
    /// The leaves at a moment: <see cref="Turn"/> 0 green .. 1 every kind's fall colour, <see cref="Canopy"/> 1 full .. bare share,
    /// <see cref="Dull"/> 0 .. 1 the autumn dull tone on grass and leaves, <see cref="Falling"/> 0 .. 1 how many leaves come down.
    /// Spring (owner 14 Nov: the world comes back to life): <see cref="Grow"/> 0 winter-short .. 1 full grass, <see cref="Brown"/> the
    /// flattened brown of grass after the snow, <see cref="Fresh"/> the bright new green, <see cref="Bud"/> pale buds on bare twigs,
    /// <see cref="Blossom"/> flowers on the kinds that blossom, <see cref="Petals"/> how many petals drift down.
    /// </summary>
    public struct FoliageNow
    {
        public float Turn, Canopy, Dull, Falling;
        public float Grow, Brown, Fresh, Bud, Blossom, Petals;

        static float L(float a, float b, float t) => a + (b - a) * t;

        public static FoliageNow Lerp(in FoliageNow a, in FoliageNow b, float t) => new FoliageNow
        {
            Turn = L(a.Turn, b.Turn, t), Canopy = L(a.Canopy, b.Canopy, t), Dull = L(a.Dull, b.Dull, t), Falling = L(a.Falling, b.Falling, t),
            Grow = L(a.Grow, b.Grow, t), Brown = L(a.Brown, b.Brown, t), Fresh = L(a.Fresh, b.Fresh, t), Bud = L(a.Bud, b.Bud, t),
            Blossom = L(a.Blossom, b.Blossom, t), Petals = L(a.Petals, b.Petals, t),
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
        /// Where the snow cover is heading now (owner, 14 Nov 2026: build-up and defrost periods, like real life). While it snows at or below
        /// <see cref="SnowRates.SnowBelowC"/> it builds toward a ceiling: light snow slowly to a low ceiling, a blizzard fast to a high one
        /// (blended by intensity; flurries under light snow build slower still); snow already deeper than the ceiling stays. Dry and at or
        /// below 0 C it holds. Above 0 C it thaws, faster each degree, faster in sunshine, fastest in rain (warm snow is sleet and thaws too).
        /// </summary>
        public static SnowTrend SnowTrendOf(in SnowRates r, PrecipitationType falling, float intensity, float temperatureC, bool sunUp,
            float coverNow)
        {
            bool snowing = falling == PrecipitationType.Snow && intensity > 0.001f;
            if (snowing && temperatureC <= r.SnowBelowC)
            {
                float t = SnowSeverity(r, intensity);
                float cap = r.LightCap + (r.HeavyCap - r.LightCap) * t;
                float rate = r.LightBuildPerSecond + (r.HeavyBuildPerSecond - r.LightBuildPerSecond) * t;
                if (r.LightIntensity > 0.001f && intensity < r.LightIntensity) rate *= intensity / r.LightIntensity;
                return coverNow >= cap ? new SnowTrend { Rate = 0f, Ceiling = 1f } : new SnowTrend { Rate = rate, Ceiling = cap };
            }
            if (temperatureC <= 0f) return new SnowTrend { Rate = 0f, Ceiling = 1f };
            float melt = r.ThawPerDegreeSecond * temperatureC * (sunUp ? 1f + r.SunThawBonus : 1f);
            bool wet = (falling == PrecipitationType.Rain || falling == PrecipitationType.Snow) && intensity > 0.001f;
            if (wet) melt = Math.Max(melt, r.ThawPerDegreeSecond) * r.RainFactor;
            return new SnowTrend { Rate = -melt, Ceiling = 1f };
        }

        /// <summary>
        /// The day's temperature shape, -0.5 .. +0.5 (owner 14 Nov: coldest before dawn, warmest mid-afternoon): rises from sunrise (the
        /// warped sun's, <paramref name="daylightShare"/>) to <paramref name="peakClock01"/>, then falls through the night back to sunrise.
        /// </summary>
        public static float DailyCurve(float clock01, float daylightShare, float peakClock01)
        {
            float t = clock01 - (float)Math.Floor(clock01);
            float rise = Sunrise(daylightShare);
            float peak = Math.Clamp(peakClock01, rise + 0.02f, 0.98f);
            if (t >= rise && t <= peak) return -0.5f * (float)Math.Cos(Math.PI * (t - rise) / (peak - rise));
            float since = t > peak ? t - peak : t + 1f - peak;
            return 0.5f * (float)Math.Cos(Math.PI * since / (1f - (peak - rise)));
        }

        /// <summary>
        /// The air temperature (deg C): the season's mean (blended), its day swing by the clock, the spell's offset (a cold snap, a
        /// heatwave), the weather's (clear warmer, storms colder) and the day's own wobble (<paramref name="dayWobble01"/> 0-1 from the seed).
        /// </summary>
        public static float TemperatureAt(in TemperatureCurve curve, float clock01, float daylightShare, float peakClock01, float spellOffsetC,
            float weatherOffsetC, float dayWobble01, float wobbleC)
        {
            return curve.BaseC + curve.SwingC * DailyCurve(clock01, daylightShare, peakClock01) + spellOffsetC + weatherOffsetC
                   + (dayWobble01 * 2f - 1f) * wobbleC;
        }

        /// <summary>A value in steps (the server re-anchors the snow and resends only when the temperature's step changes).</summary>
        public static float Quantize(float value, float step) => step <= 0f ? value : (float)Math.Round(value / step) * step;

        /// <summary>
        /// The weather seed for one map: the settings' seed mixed with the scene's name (each map its own weather). The server rolls the
        /// spells with it and the game calendar's forecast reads the same days with it.
        /// </summary>
        public static int WeatherSeed(int settingsSeed, string sceneName)
        {
            int h = settingsSeed;
            if (!string.IsNullOrEmpty(sceneName))
                foreach (char c in sceneName) h = unchecked(h * 31 + char.ToLowerInvariant(c));
            return h;
        }

        /// <summary>The temperature wobble's seed (the day's own few degrees up or down) from the weather seed.</summary>
        public static int WobbleSeed(int weatherSeed) => weatherSeed ^ 0x2F6B;

        /// <summary>A deterministic 0-1 from a seed and a number (SplitMix64): the same on every machine and in tests.</summary>
        public static float Hash01(int seed, long n)
        {
            unchecked
            {
                ulong z = (ulong)n * 0x9E3779B97F4A7C15UL + (ulong)(uint)seed * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (float)((z >> 40) / (double)(1UL << 24));
            }
        }

        /// <summary>
        /// The weather spell a day falls in (owner, 14 Nov 2026: rainy for a few days in spring, long dry spells in summer, not every winter
        /// day snows): the days are cut into blocks of <paramref name="blockDays"/>; each block is a run of spells rolled from the seed, each
        /// spell's kind by its chance and its length between its min and max days (cut at the block's end). Pure and the same on every
        /// machine; the season is mixed into the seed. Spell -1 with an empty table.
        /// </summary>
        public static SpellDay SpellAt(IReadOnlyList<SpellChoice> table, int seed, Season season, long day, int blockDays = 64)
        {
            var none = new SpellDay { Spell = -1, DayInSpell = 0, Length = 1 };
            if (table == null || table.Count == 0) return none;
            float total = 0f;
            for (int i = 0; i < table.Count; i++) total += Math.Max(0f, table[i].Chance);
            if (total <= 0f) return none;
            int block = Math.Max(1, blockDays);
            long blockIndex = (long)Math.Floor(day / (double)block);
            long start = blockIndex * block, end = start + block;
            int s = unchecked(seed * 31 + ((int)season + 1) * 7919);
            long d = start;
            for (int k = 0; k < block; k++)
            {
                long n = blockIndex * 1024 + k * 2;
                float pick = Hash01(s, n) * total;
                int spell = -1;
                for (int i = 0; i < table.Count; i++)
                {
                    if (table[i].Chance <= 0f) continue;
                    spell = i;
                    pick -= table[i].Chance;
                    if (pick <= 0f) break;
                }
                int min = Math.Max(1, table[spell].MinDays), max = Math.Max(min, table[spell].MaxDays);
                int length = Math.Min(max, min + (int)Math.Floor(Hash01(s, n + 1) * (max - min + 1)));
                long last = Math.Min(end, d + length);
                if (day < last) return new SpellDay { Spell = spell, DayInSpell = (int)(day - d), Length = (int)(last - d) };
                d = last;
                if (d >= end) break;
            }
            return none;
        }

        /// <summary>How many days in a row up to and including <paramref name="day"/> fall in spells that <paramref name="counts"/> (at most <paramref name="max"/>).</summary>
        public static int RunOfDays(IReadOnlyList<SpellChoice> table, int seed, Season season, long day, Func<int, bool> counts, int max,
            int blockDays = 64)
        {
            int run = 0;
            for (long d = day; run < max; d--)
            {
                SpellDay sd = SpellAt(table, seed, season, d, blockDays);
                if (sd.Spell < 0 || !counts(sd.Spell)) break;
                run++;
            }
            return run;
        }

        /// <summary>
        /// How much of the night it is at a presented time of day (0.5 noon, 0.25 sunrise, 0.75 sunset, 0 midnight): 0 from sunrise to
        /// sunset, 1 soon after dusk until soon before dawn. The night frost, the heat haze, the motes and the fireflies follow it.
        /// </summary>
        public static float NightAmount(float timeOfDay01) => (float)Math.Min(1.0, Math.Max(0.0, Math.Cos(timeOfDay01 * 2.0 * Math.PI) * 3.0));

        /// <summary>
        /// Leaf litter under the canopies (owner 15 Nov, T3), 0-1: as much as the canopies have shed (none in Summer, building through
        /// Autumn, all of it under Winter's snow), going as Spring's fresh green comes in.
        /// </summary>
        public static float LitterAmount(in FoliageNow f) => Clamp01((1f - f.Canopy) * (1f - f.Fresh));

        /// <summary>
        /// How hard the wind sweeps the litter now (owner 15 Nov, T4), 0-1: none below <paramref name="fromToSpeed"/>.x m/s, all above .y.
        /// <paramref name="windSpeed"/> already holds the current gust; a gust (0-1) adds up to half the range again.
        /// </summary>
        public static float LitterSweep(float windSpeed, float gust, (float from, float to) fromToSpeed)
        {
            float range = Math.Max(0.01f, fromToSpeed.to - fromToSpeed.from);
            float t = Clamp01((windSpeed - fromToSpeed.from) / range + Clamp01(gust) * 0.5f * (windSpeed > fromToSpeed.from ? 1f : 0f));
            return t * t * (3f - 2f * t);
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

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
                {
                    float leafOut = Math.Max(0.01f, c.LeafOutBy);
                    float grow = Smooth(0f, Math.Max(0.01f, c.GrassBy), p);
                    float from = Math.Clamp(c.BlossomFrom, 0f, 0.98f);
                    float peak = Math.Clamp(c.BlossomPeak, from + 0.01f, 0.99f);
                    float gone = Math.Clamp(c.BlossomGone, peak + 0.01f, 1f);
                    float blossom = Smooth(from, peak, p) * (1f - Smooth(peak, gone, p));
                    float petals = p > peak && p < gone ? (float)Math.Sin(Math.PI * (p - peak) / (gone - peak)) : 0f;
                    return new FoliageNow
                    {
                        Turn = 0f, Canopy = bare + (1f - bare) * Smooth(0f, leafOut, p),
                        Grow = grow, Brown = 1f - grow, Fresh = grow,
                        Bud = Smooth(0f, 0.06f, p) * (1f - Smooth(leafOut * 0.55f, leafOut, p)),
                        Blossom = blossom, Petals = petals,
                    };
                }
                case Season.Summer:
                    return new FoliageNow { Canopy = 1f, Grow = 1f, Fresh = 1f - 0.5f * p };
                case Season.Autumn:
                {
                    float drop = Smooth(Math.Clamp(c.DropFrom, 0f, 0.99f), 1f, p);
                    float turn = Smooth(0f, Math.Max(0.01f, c.TurnBy), p);
                    // most leaves come down while the canopy thins, a few from the first turn
                    float falling = Math.Clamp(turn * 0.35f + drop * (1f - drop) * 4f * 0.65f + (p > c.DropFrom && p < 1f ? 0.25f : 0f), 0f, 1f);
                    return new FoliageNow
                    {
                        Turn = turn, Canopy = 1f - (1f - bare) * drop, Dull = Smooth(0f, Math.Max(0.01f, c.DullBy), p), Falling = falling,
                        Grow = 1f - 0.3f * Smooth(0.5f, 1f, p),
                    };
                }
                default:
                    return new FoliageNow { Turn = 1f, Canopy = bare, Dull = 1f, Falling = 0f, Grow = 0f, Brown = 1f };
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
