using System;
using System.Collections.Generic;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Seasons (owner's DAY list item 21; design note seasons-plan.md, evals 1-5): the season from real UTC time, the blend, the daylight
    /// warp, the snow cover and the weather weights, all pure.
    /// </summary>
    public class SeasonRulesTests
    {
        static double Unix(int y, int m, int d, int h = 0, int min = 0) =>
            (new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

        static SeasonCalendar Calendar(double offsetHours = 0, double offsetDays = 0) => new SeasonCalendar
        {
            YearStartUnix = Unix(2026, 1, 5), SeasonSeconds = 14 * 86400.0, OffsetSeconds = offsetHours * 3600 + offsetDays * 86400,
            TransitionSeconds = 6 * 3600.0,
        };

        [Test]
        public void SeasonFromUtc_TwoWeeksEach_TurningMondays_WinterFrom23Nov()
        {
            SeasonCalendar c = Calendar();
            SeasonNow now = SeasonRules.At(c, Unix(2026, 11, 13, 12));
            Assert.AreEqual(Season.Autumn, now.Season, "13 Nov 2026 is Autumn");
            Assert.AreEqual(5, now.Day, "day 5 of 14");
            Assert.AreEqual(Unix(2026, 11, 9), now.StartUnix, 1e-6, "Autumn began Mon 9 Nov 00:00 UTC");
            Assert.AreEqual(Season.Autumn, SeasonRules.At(c, Unix(2026, 11, 22, 23, 59)).Season, "still Autumn a minute before");
            SeasonNow winter = SeasonRules.At(c, Unix(2026, 11, 23));
            Assert.AreEqual(Season.Winter, winter.Season, "Winter from Mon 23 Nov 00:00 UTC (owner 13 Nov, pick 21.1 A)");
            Assert.AreEqual(1, winter.Day);
            Assert.AreEqual(Season.Autumn, winter.Previous);
            Assert.AreEqual(Season.Spring, SeasonRules.At(c, Unix(2026, 12, 7)).Season, "Spring from Mon 7 Dec");
            Assert.AreEqual(Season.Summer, SeasonRules.At(c, Unix(2026, 12, 21)).Season, "Summer from Mon 21 Dec");
            Assert.AreEqual(Unix(2026, 11, 23), SeasonRules.NextTurnUnix(c, Unix(2026, 11, 13, 12)), 1e-6, "the next turn");

            // a year is 56 days: the same season again
            for (double t = Unix(2026, 3, 1); t < Unix(2027, 3, 1); t += 86400 * 3.7)
                Assert.AreEqual(SeasonRules.At(c, t).Season, SeasonRules.At(c, t + 56 * 86400.0).Season, "a year later");

            // before the first Spring the calendar still runs backwards cleanly
            Assert.AreEqual(Season.Winter, SeasonRules.At(c, Unix(2026, 1, 4)).Season, "the day before the first Spring is Winter");

            // the offsets move the turn
            Assert.AreEqual(Season.Autumn, SeasonRules.At(Calendar(18), Unix(2026, 11, 23, 10)).Season, "turn hour 18:00: not yet Winter at 10:00");
            Assert.AreEqual(Season.Winter, SeasonRules.At(Calendar(18), Unix(2026, 11, 23, 18)).Season, "Winter at 18:00");
            Assert.AreEqual(Season.Winter, SeasonRules.At(Calendar(0, -10), Unix(2026, 11, 13, 12)).Season, "10 days earlier: Winter now");
        }

        [Test]
        public void Blend_RunsZeroToOne_AndNeverJumps()
        {
            double start = 1000, seconds = 6 * 3600;
            Assert.AreEqual(0f, SeasonRules.Blend(start, seconds, start - 5), "before");
            Assert.AreEqual(0f, SeasonRules.Blend(start, seconds, start), 1e-6, "at the start");
            Assert.AreEqual(0.5f, SeasonRules.Blend(start, seconds, start + seconds / 2), 1e-5, "halfway");
            Assert.AreEqual(1f, SeasonRules.Blend(start, seconds, start + seconds), 1e-6, "at the end");
            Assert.AreEqual(1f, SeasonRules.Blend(start, seconds, start + seconds * 3), "after");
            float last = 0f;
            for (double t = start; t <= start + seconds; t += 60)
            {
                float b = SeasonRules.Blend(start, seconds, t);
                Assert.GreaterOrEqual(b, last, "never goes back");
                Assert.Less(b - last, 0.01f, "no jump over a minute");
                last = b;
            }
            Assert.AreEqual(1f, SeasonRules.Blend(start, 0, start), "a zero blend is at once");
        }

        [Test]
        public void SunTime_IsContinuous_KeepsNoon_AndGivesShortWinterDays()
        {
            for (float t = 0f; t < 1f; t += 0.01f)
                Assert.AreEqual(t, SeasonRules.SunTime(t, 0.5f), 1e-5, "share 0.5 changes nothing");
            foreach (float share in new[] { 0.375f, 0.5f, 0.625f })
            {
                Assert.AreEqual(0.5f, SeasonRules.SunTime(0.5f, share), 1e-5, "noon stays noon");
                Assert.AreEqual(0f, SeasonRules.SunTime(0f, share), 1e-5, "midnight stays midnight");
                float last = SeasonRules.SunTime(0f, share);
                for (float t = 0.001f; t < 1f; t += 0.001f)
                {
                    float s = SeasonRules.SunTime(t, share);
                    Assert.Greater(s, last - 1e-6f, $"never goes back (share {share}, t {t})");
                    Assert.Less(s - last, 0.01f, $"never jumps (share {share}, t {t})");
                    last = s;
                }
            }
            // winter: sun up 07:30-16:30
            Assert.AreEqual(0.25f, SeasonRules.SunTime(7.5f / 24f, 0.375f), 1e-5, "sunrise at 07:30");
            Assert.AreEqual(0.75f, SeasonRules.SunTime(16.5f / 24f, 0.375f), 1e-5, "sunset at 16:30");
            Assert.IsTrue(SeasonRules.IsNight(7f / 24f, 0.375f), "07:00 in Winter is still night");
            Assert.IsFalse(SeasonRules.IsNight(7f / 24f, 0.5f), "07:00 in Spring is day");
            Assert.AreEqual(7.5f / 24f, SeasonRules.Sunrise(0.375f), 1e-5);
            // summer: 04:30-19:30
            Assert.AreEqual(0.25f, SeasonRules.SunTime(4.5f / 24f, 0.625f), 1e-5, "summer sunrise at 04:30");
        }

        static SnowRates Rates => new SnowRates
        {
            LightIntensity = 0.5f, HeavyIntensity = 1f, LightBuildPerSecond = 1f / 2400f, HeavyBuildPerSecond = 1f / 480f,
            LightCap = 0.35f, HeavyCap = 1f,
            SnowBelowC = 0.5f, ThawPerDegreeSecond = 1f / 14400f, SunThawBonus = 0.6f, RainFactor = 3f,
        };

        static SnowTrend Trend(PrecipitationType p, float intensity, float tempC = -5f, bool sunUp = true, float cover = 0f) =>
            SeasonRules.SnowTrendOf(Rates, p, intensity, tempC, sunUp, cover);

        /// <summary>Cover after <paramref name="seconds"/> of steady weather, re-anchoring each minute as the server does when the trend changes.</summary>
        static float Run(PrecipitationType p, float intensity, double seconds, float start = 0f, float tempC = -5f, bool sunUp = true)
        {
            float cover = start;
            for (double t = 0; t < seconds; t += 60)
            {
                SnowTrend tr = Trend(p, intensity, tempC, sunUp, cover);
                cover = SeasonRules.SnowCover(cover, tr.Rate, 0, Math.Min(60, seconds - t), 0f, tr.Ceiling);
            }
            return cover;
        }

        [Test]
        public void SnowBuildUp_LightIsSlowAndLow_HeavyIsFastAndHigh()
        {
            // owner 14 Nov: light snow takes longer and stays low; bad snow builds fast and higher
            SnowTrend light = Trend(PrecipitationType.Snow, 0.5f);
            SnowTrend heavy = Trend(PrecipitationType.Snow, 1f);
            Assert.Greater(heavy.Rate, light.Rate * 4f, "a blizzard builds at least four times as fast");
            Assert.AreEqual(0.35f, light.Ceiling, 1e-5, "light snow stops at its cap");
            Assert.AreEqual(1f, heavy.Ceiling, 1e-5, "a blizzard goes all the way");

            Assert.AreEqual(0.35f, Run(PrecipitationType.Snow, 0.5f, 7200), 1e-4, "two hours of light snow: capped at 0.35");
            Assert.Less(Run(PrecipitationType.Snow, 0.5f, 600), 0.3f, "ten minutes of light snow: still thin");
            Assert.AreEqual(1f, Run(PrecipitationType.Snow, 1f, 600), 1e-4, "ten minutes of blizzard: full depth");
            float mid = Run(PrecipitationType.Snow, 0.75f, 7200);
            Assert.That(mid, Is.GreaterThan(0.35f).And.LessThan(1f), "in-between snow stops in between");
            Assert.Less(Run(PrecipitationType.Snow, 0.2f, 600), Run(PrecipitationType.Snow, 0.5f, 600), "flurries build slower than light snow");

            // deeper than light snow can make (after a blizzard): light snow neither adds nor melts it
            Assert.AreEqual(0f, Trend(PrecipitationType.Snow, 0.5f, -5f, true, 0.9f).Rate, "light snow on deep snow holds it");
            Assert.AreEqual(0.9f, Run(PrecipitationType.Snow, 0.5f, 3600, 0.9f), 1e-4);
        }

        [Test]
        public void SnowByTemperature_BuildsBelowZero_HoldsColdAndDry_ThawsAboveZero_FasterInSunAndRain()
        {
            // owner 14 Nov: build-up and defrost periods
            Assert.Greater(Run(PrecipitationType.Snow, 1f, 1200, 0f, -4f), 0.9f, "a cold snowy day builds");
            Assert.AreEqual(0.6f, Run(PrecipitationType.None, 0f, 7200, 0.6f, -6f, false), 1e-5, "a cold clear night holds it");
            Assert.AreEqual(0.6f, Run(PrecipitationType.None, 0f, 7200, 0.6f, 0f, true), 1e-5, "at 0 C it holds, sun or not");
            Assert.Less(Run(PrecipitationType.Snow, 1f, 1200, 0.5f, 3f), 0.5f, "warm snow is sleet: it thaws");
            Assert.AreEqual(0f, Run(PrecipitationType.None, 0f, 7200, 1f, 6f, true), 1e-4, "a warm sunny day after melts it");
            float shade = Trend(PrecipitationType.None, 0f, 2f, false).Rate, sun = Trend(PrecipitationType.None, 0f, 2f, true).Rate;
            Assert.Less(sun, shade, "sunshine thaws faster");
            Assert.Less(Trend(PrecipitationType.None, 0f, 4f, false).Rate, shade, "each degree thaws faster");
            Assert.Less(Trend(PrecipitationType.Rain, 0.8f, 2f, false).Rate, shade * 2.5f, "rain washes it off");
            Assert.AreEqual(1f, Trend(PrecipitationType.None, 0f, 5f).Ceiling, "thawing is never capped");

            // a few days of a cold snowy spell, then a thaw: up, then down (one in-game day = an hour of the clock)
            float cover = 0f, peak = 0f;
            for (int hour = 0; hour < 72; hour++)
            {
                bool snowDay = hour < 24, thaw = hour >= 48;
                float temp = thaw ? 5f : -5f + (hour % 2 == 0 ? 2f : -2f);
                cover = Run(snowDay && hour % 3 != 2 ? PrecipitationType.Snow : PrecipitationType.None, 0.7f, 3600, cover, temp, hour % 2 == 0);
                peak = Math.Max(peak, cover);
                if (hour == 47) Assert.AreEqual(peak, cover, 1e-4, "the cold dry day after keeps all of it");
            }
            Assert.Greater(peak, 0.5f, "the snowy day built it up");
            Assert.AreEqual(0f, cover, 1e-4, "the thaw took it away");

            for (double t = 0; t < 100000; t += 777)
            {
                Assert.GreaterOrEqual(SeasonRules.SnowCover(0.9f, -0.01f, 0, t, 0.6f), 0.6f, "never below an always-winter map floor");
                Assert.LessOrEqual(SeasonRules.SnowCover(0.1f, 0.01f, 0, t, 0f, 0.35f), 0.35f + 1e-6, "never above the ceiling");
                Assert.LessOrEqual(SeasonRules.SnowCover(0.5f, 0.5f, 0, t, 0f), 1f, "never above the max depth");
            }
            Assert.AreEqual(0.3f, SeasonRules.SnowCover(0.3f, 0.5f, 10, 5, 0f), 1e-6, "nothing before the anchor");
        }

        [Test]
        public void Temperature_ColdestBeforeDawn_WarmestMidAfternoon_Smooth()
        {
            var curve = new TemperatureCurve { BaseC = 0f, SwingC = 10f };
            float share = 0.5f, peak = 0.625f;
            float dawn = SeasonRules.TemperatureAt(curve, 0.249f, share, peak, 0f, 0f, 0.5f, 0f);
            float afternoon = SeasonRules.TemperatureAt(curve, 0.625f, share, peak, 0f, 0f, 0.5f, 0f);
            Assert.AreEqual(-5f, dawn, 0.05f, "coldest just before sunrise");
            Assert.AreEqual(5f, afternoon, 1e-3, "warmest at 15:00");
            float last = SeasonRules.DailyCurve(0f, share, peak);
            for (float t = 0.001f; t < 1f; t += 0.001f)
            {
                float v = SeasonRules.DailyCurve(t, share, peak);
                Assert.Less(Math.Abs(v - last), 0.02f, $"no jump at {t:0.000}");
                Assert.That(v, Is.InRange(-0.5f, 0.5f));
                last = v;
            }
            Assert.AreEqual(3f, SeasonRules.TemperatureAt(curve, 0.625f, share, peak, -4f, 2f, 0.5f, 3f), 1e-3, "spell and weather offsets add");
            Assert.AreEqual(1.5f, SeasonRules.Quantize(1.6f, 0.5f), 1e-6);
        }

        static List<SpellChoice> Table(SeasonProfile p)
        {
            var t = new List<SpellChoice>();
            t.AddRange(p.SpellTable);
            return t;
        }

        [Test]
        public void WeatherSpells_RunOverDays_SpringWetterThanSummer_SummerDrySpellsLonger_WinterSnowsSomeDays_StormsHappen()
        {
            SeasonSettings s = Resources.Load<SeasonSettings>(SeasonSettings.ResourcePath);
            Assert.IsNotNull(s);
            int seed = s.weatherSeed;
            const int days = 2000;
            (int wet, int longestDry, int storms, int snowy, int longestWet) Count(Season season)
            {
                SeasonProfile p = s.Profile(season);
                List<SpellChoice> table = Table(p);
                int wet = 0, storms = 0, snowy = 0, dryRun = 0, longestDry = 0, wetRun = 0, longestWet = 0;
                for (long d = 0; d < days; d++)
                {
                    SpellDay sd = SeasonRules.SpellAt(table, seed, season, d, s.spellBlockDays);
                    Assert.That(sd.Spell, Is.InRange(0, table.Count - 1));
                    Assert.Less(sd.DayInSpell, sd.Length);
                    WeatherSpell w = p.Spell(sd.Spell);
                    if (w.wet) wet++;
                    if (w.storm) storms++;
                    if (w.snowy) snowy++;
                    dryRun = w.dry ? dryRun + 1 : 0;
                    wetRun = w.wet ? wetRun + 1 : 0;
                    longestDry = Math.Max(longestDry, dryRun);
                    longestWet = Math.Max(longestWet, wetRun);
                }
                return (wet, longestDry, storms, snowy, longestWet);
            }
            var spring = Count(Season.Spring);
            var summer = Count(Season.Summer);
            var winter = Count(Season.Winter);
            Assert.Greater(spring.wet, summer.wet * 2, "spring has many more wet days than summer");
            Assert.Greater(summer.longestDry, spring.longestDry, "summer's longest dry spell beats spring's");
            Assert.GreaterOrEqual(summer.longestDry, 7, "summer has dry spells of a week or more");
            Assert.GreaterOrEqual(spring.longestWet, 2, "spring has wet runs of days");
            Assert.GreaterOrEqual(summer.longestWet, 2, "summer sometimes has a wet run");
            Assert.Greater(spring.storms + summer.storms, 0, "storms happen");
            Assert.That(winter.snowy / (float)days, Is.InRange(0.35f, 0.65f), "about half the winter days snow, not every day");

            // the same seed gives the same days; another seed others
            List<SpellChoice> wt = Table(s.winter);
            int same = 0, differ = 0;
            for (long d = 0; d < 200; d++)
            {
                if (SeasonRules.SpellAt(wt, seed, Season.Winter, d).Spell == SeasonRules.SpellAt(wt, seed, Season.Winter, d).Spell) same++;
                if (SeasonRules.SpellAt(wt, seed, Season.Winter, d).Spell != SeasonRules.SpellAt(wt, seed + 1, Season.Winter, d).Spell) differ++;
            }
            Assert.AreEqual(200, same, "deterministic");
            Assert.Greater(differ, 20, "the seed matters");
            Assert.AreEqual(-1, SeasonRules.SpellAt(new List<SpellChoice>(), seed, Season.Winter, 5).Spell, "no table, no spell");
            Assert.Greater(SeasonRules.RunOfDays(Table(s.summer), seed, Season.Summer, 500, i => s.summer.Spell(i).dry, 14), -1);
        }

        [Test]
        public void Spring_GrassGrowsGreen_BudsThenBlossomThenPetals_Summer_IsFull()
        {
            var c = new FoliageCurve { Bare = 0.08f, LeafOutBy = 0.35f, GrassBy = 0.4f, BlossomFrom = 0.05f, BlossomPeak = 0.22f, BlossomGone = 0.5f, TurnBy = 0.45f, DropFrom = 0.5f, DullBy = 0.7f };
            FoliageNow start = SeasonRules.FoliageAt(Season.Spring, 0f, c), peak = SeasonRules.FoliageAt(Season.Spring, 0.22f, c);
            FoliageNow late = SeasonRules.FoliageAt(Season.Spring, 0.9f, c), winter = SeasonRules.FoliageAt(Season.Winter, 0.5f, c);
            Assert.AreEqual(0f, start.Grow, 1e-4, "grass starts short after the snow");
            Assert.AreEqual(winter.Grow, start.Grow, 1e-4, "no jump from Winter");
            Assert.Greater(start.Brown, 0.9f, "brown and flattened at first");
            Assert.AreEqual(1f, late.Grow, 1e-4, "full height by late Spring");
            Assert.Greater(SeasonRules.FoliageAt(Season.Spring, 0.04f, c).Bud, 0.3f, "buds early");
            Assert.AreEqual(1f, peak.Blossom, 1e-3, "blossom at its fullest");
            Assert.AreEqual(0f, late.Blossom, 1e-4, "blossom gone by late Spring");
            Assert.Greater(SeasonRules.FoliageAt(Season.Spring, 0.36f, c).Petals, 0.5f, "petals fall as it goes");
            Assert.AreEqual(1f, SeasonRules.FoliageAt(Season.Summer, 0.5f, c).Canopy, "summer: full canopies");
        }

        static float Up(float degrees) => (float)Math.Cos(degrees * Math.PI / 180.0);

        [Test]
        public void SnowSlope_FullOnTheFlat_NoneOnSteepFaces_SmoothBetween()
        {
            Assert.AreEqual(1f, SeasonRules.SnowSlope(1f, 35f, 55f), 1e-5, "level ground");
            Assert.AreEqual(1f, SeasonRules.SnowSlope(Up(30f), 35f, 55f), 1e-5, "a 30 degree roof holds it all");
            Assert.AreEqual(1f, SeasonRules.SnowSlope(Up(35f), 35f, 55f), 1e-4, "full at the full angle");
            Assert.AreEqual(0f, SeasonRules.SnowSlope(Up(55f), 35f, 55f), 1e-4, "none at the none angle");
            Assert.AreEqual(0f, SeasonRules.SnowSlope(Up(70f), 35f, 55f), 1e-5, "a steep roof sheds it");
            Assert.AreEqual(0f, SeasonRules.SnowSlope(0f, 35f, 55f), 1e-5, "walls never");
            float half = SeasonRules.SnowSlope(Up(45f), 35f, 55f);
            Assert.That(half, Is.GreaterThan(0.2f).And.LessThan(0.8f), "part-covered in between");
            float last = 1f;
            for (int d = 0; d <= 90; d += 5) { float v = SeasonRules.SnowSlope(Up(d), 35f, 55f); Assert.LessOrEqual(v, last + 1e-5); last = v; }
            var (cosNone, inv) = SeasonRules.SnowSlopeShape(35f, 55f);
            Assert.AreEqual(Up(55f), cosNone, 1e-5, "the shader number");
            Assert.AreEqual(1f / (Up(35f) - Up(55f)), inv, 1e-3);
        }

        static FoliageCurve Curve => new FoliageCurve { TurnBy = 0.45f, DropFrom = 0.5f, Bare = 0.08f, LeafOutBy = 0.35f, DullBy = 0.7f };

        [Test]
        public void Foliage_TurnsThroughAutumn_BareBeforeWinter_BackInSpring()
        {
            FoliageCurve c = Curve;
            FoliageNow summer = SeasonRules.FoliageAt(Season.Summer, 0.5f, c);
            Assert.AreEqual(0f, summer.Turn); Assert.AreEqual(1f, summer.Canopy); Assert.AreEqual(0f, summer.Dull);

            FoliageNow early = SeasonRules.FoliageAt(Season.Autumn, 0.1f, c);
            FoliageNow peak = SeasonRules.FoliageAt(Season.Autumn, 0.45f, c);
            FoliageNow late = SeasonRules.FoliageAt(Season.Autumn, 0.95f, c);
            Assert.That(early.Turn, Is.GreaterThan(0f).And.LessThan(0.3f), "early Autumn is only starting to turn");
            Assert.AreEqual(1f, early.Canopy, 1e-5, "and keeps its leaves");
            Assert.AreEqual(1f, peak.Turn, 1e-5, "every kind has turned by 45 %");
            Assert.AreEqual(1f, peak.Canopy, 1e-5);
            Assert.Less(late.Canopy, 0.15f, "almost bare late in Autumn");
            Assert.AreEqual(0.08f, SeasonRules.FoliageAt(Season.Autumn, 1f, c).Canopy, 1e-5, "bare at the door of Winter");
            Assert.GreaterOrEqual(late.Dull, peak.Dull, "the grass keeps dulling");
            Assert.AreEqual(1f, SeasonRules.FoliageAt(Season.Autumn, 0.8f, c).Dull, 1e-5, "dull by 70 %");
            Assert.Greater(SeasonRules.FoliageAt(Season.Autumn, 0.75f, c).Falling, early.Falling, "most leaves fall while the canopies thin");
            float last = 1f;
            for (float p = 0f; p <= 1f; p += 0.05f) { float v = SeasonRules.FoliageAt(Season.Autumn, p, c).Canopy; Assert.LessOrEqual(v, last + 1e-5); last = v; }

            FoliageNow winter = SeasonRules.FoliageAt(Season.Winter, 0.5f, c);
            Assert.AreEqual(0.08f, winter.Canopy, 1e-5, "bare through Winter (evergreens are kept by the shader)");
            Assert.AreEqual(0f, winter.Falling, "nothing left to fall");
            Assert.AreEqual(0.08f, SeasonRules.FoliageAt(Season.Spring, 0f, c).Canopy, 1e-5, "Spring begins bare");
            Assert.AreEqual(1f, SeasonRules.FoliageAt(Season.Spring, 0.35f, c).Canopy, 1e-5, "and is green again by 35 %");
            Assert.AreEqual(0f, SeasonRules.FoliageAt(Season.Spring, 0.2f, c).Turn, "spring leaves are green");
        }

        [Test]
        public void Aurora_OnlyInWinter_OrOnAnAlwaysWinterMap()
        {
            var s = new SeasonState { FromSeason = (byte)Season.Autumn, ToSeason = (byte)Season.Winter, BlendStart = 100, BlendSeconds = 100 };
            Assert.IsFalse(s.AuroraAllowedAt(120), "Autumn still shows more");
            Assert.IsTrue(s.AuroraAllowedAt(160), "Winter from halfway through the blend");
            Assert.IsTrue(s.AuroraAllowedAt(1000));
            s.FromSeason = (byte)Season.Winter; s.ToSeason = (byte)Season.Spring;
            Assert.IsFalse(s.AuroraAllowedAt(1000), "not in Spring");
            s.Flags = SeasonState.FlagAlwaysWinter;
            Assert.IsTrue(s.AuroraAllowedAt(1000), "an always-winter map has it all year");
        }

        [Test]
        public void Foliage_FollowsTheSeasonBlend_AndAGmProgress()
        {
            FoliageCurve c = Curve;
            var s = new SeasonState
            {
                FromSeason = (byte)Season.Autumn, ToSeason = (byte)Season.Winter, BlendStart = 100, BlendSeconds = 100,
                SeasonStart = 100, SeasonSeconds = 1000, Progress = -1f,
            };
            Assert.AreEqual(SeasonRules.FoliageAt(Season.Autumn, 1f, c).Canopy, s.FoliageAt(100, c).Canopy, 1e-5, "the blend starts from late Autumn");
            Assert.AreEqual(0.08f, s.FoliageAt(400, c).Canopy, 1e-5, "and ends bare");
            Assert.AreEqual(0.3f, s.ProgressAt(400), 1e-5, "the calendar share");
            s.Progress = 0.9f;
            Assert.AreEqual(0.9f, s.ProgressAt(400), 1e-5, "a GM share wins");
            s.Progress = -1f; s.SeasonSeconds = 0f;
            Assert.AreEqual(0.5f, s.ProgressAt(400), 1e-5, "no length: the middle");
            s.Flags = SeasonState.FlagAlwaysWinter;
            Assert.AreEqual(SeasonRules.FoliageAt(Season.Winter, 0.5f, c).Canopy, s.FoliageAt(400, c).Canopy, 1e-5, "an always-winter map stays bare");
        }

        readonly List<UnityEngine.Object> m_Made = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in m_Made) UnityEngine.Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        WeatherProfile Weather(string name)
        {
            var p = ScriptableObject.CreateInstance<WeatherProfile>();
            p.name = name;
            m_Made.Add(p);
            return p;
        }

        SeasonProfile Profile(params (WeatherProfile w, float m)[] weights)
        {
            var p = ScriptableObject.CreateInstance<SeasonProfile>();
            foreach ((WeatherProfile w, float m) in weights) p.weatherWeights.Add(new SeasonProfile.WeatherWeight { profile = w, multiplier = m });
            m_Made.Add(p);
            return p;
        }

        /// <summary>The weather pick before seasons, word for word (WorldEnvironmentNetwork.AdvanceWeather, 13 Nov 2026).</summary>
        static WeatherProfile OldPick(List<WeatherProfile.NextWeather> next, WeatherProfile current, float roll)
        {
            float total = 0f;
            foreach (var n in next) if (n.profile != null) total += n.weight;
            float pick = roll * total;
            WeatherProfile chosen = current;
            foreach (var n in next)
            {
                if (n.profile == null) continue;
                pick -= n.weight;
                if (pick <= 0f) { chosen = n.profile; break; }
            }
            return chosen;
        }

        [Test]
        public void WeatherWeights_WinterNeverThunder_SummerNeverSnow_AndNoSeasonPicksAsBefore()
        {
            WeatherProfile clear = Weather("Clear"), snow = Weather("Snow"), storm = Weather("Thunderstorm"), overcast = Weather("Overcast");
            var next = new List<WeatherProfile.NextWeather>
            {
                new WeatherProfile.NextWeather { profile = clear, weight = 2 }, new WeatherProfile.NextWeather { profile = snow, weight = 0.4f },
                new WeatherProfile.NextWeather { profile = null, weight = 5 }, new WeatherProfile.NextWeather { profile = storm, weight = 1 },
                new WeatherProfile.NextWeather { profile = overcast, weight = 1.5f },
            };
            SeasonProfile winter = Profile((snow, 3f), (storm, 0f));
            winter.extraNext.Add(new WeatherProfile.NextWeather { profile = snow, weight = 1f });
            SeasonProfile summer = Profile((snow, 0f), (clear, 2f));
            var rng = new System.Random(21);
            int winterSnow = 0, noSeasonSnow = 0;
            for (int i = 0; i < 10000; i++)
            {
                float roll = (float)rng.NextDouble();
                WeatherProfile w = SeasonRules.Pick(next, winter.extraNext, winter.WeightOf, roll);
                Assert.AreNotSame(storm, w, "Winter never picks Thunderstorm");
                if (w == snow) winterSnow++;
                WeatherProfile s = SeasonRules.Pick(next, summer.extraNext, summer.WeightOf, roll);
                Assert.AreNotSame(snow, s, "Summer never picks Snow");
                WeatherProfile none = SeasonRules.Pick(next, null, null, roll) ?? overcast;
                Assert.AreSame(OldPick(next, overcast, roll), none, $"with no season the pick is as before (roll {roll})");
                if (none == snow) noSeasonSnow++;
            }
            Assert.Greater(winterSnow, noSeasonSnow * 4, $"Winter snows far more often ({winterSnow} vs {noSeasonSnow} of 10000)");
            Assert.IsNull(SeasonRules.Pick(new List<WeatherProfile.NextWeather>(), null, null, 0.5f), "nothing to pick: keep the weather");
            Assert.AreSame(snow, SeasonRules.Pick(new List<WeatherProfile.NextWeather>(), winter.extraNext, winter.WeightOf, 0.5f),
                "an empty Next list still snows in Winter (the extra choices)");
        }

        [Test]
        public void SeasonNames_Parse()
        {
            Assert.IsTrue(SeasonRules.TryParse("winter", out Season s) && s == Season.Winter);
            Assert.IsTrue(SeasonRules.TryParse("Win", out s) && s == Season.Winter);
            Assert.IsTrue(SeasonRules.TryParse("fall", out s) && s == Season.Autumn);
            Assert.IsTrue(SeasonRules.TryParse("sp", out s) && s == Season.Spring);
            Assert.IsFalse(SeasonRules.TryParse("s", out _), "one letter is not enough (spring or summer)");
            Assert.IsFalse(SeasonRules.TryParse("monsoon", out _));
        }

        [Test]
        public void SettingsAsset_IsInResources_WithFourProfiles_AndTheOwnersCalendar()
        {
            SeasonSettings s = Resources.Load<SeasonSettings>(SeasonSettings.ResourcePath);
            Assert.IsNotNull(s, "World/Resources/SeasonSettings.asset");
            foreach (Season season in new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter })
            {
                Assert.IsNotNull(s.Profile(season), $"{season} profile");
                Assert.AreEqual(season, s.Profile(season).season, $"{season} profile names its season");
            }
            Assert.AreEqual(14f, s.seasonLengthDays, "two real weeks a season (owner 13 Nov)");
            Assert.AreEqual(Season.Winter, SeasonRules.At(s.Calendar, Unix(2026, 11, 23, 1)).Season, "Winter from 23 Nov (owner pick 21.1 A)");
            Assert.Less(s.winter.daylightShare, s.spring.daylightShare, "short winter days");
            Assert.Greater(s.winter.frost, 0f, "frosted winter trees (owner pick)");
            Assert.IsTrue(s.IsAlwaysWinter("Sunderpeak", null), "the Sunderpeak maps stay winter (owner pick 21.2 A)");
            Assert.IsFalse(s.IsAlwaysWinter("Meridian Sprawl", "MeridianSprawl_NewMeridian"));
            Assert.IsNotNull(s.trackCompute, "the snow track compute");
            Assert.AreEqual(4, s.precipitationByTier.Length, "one rate per World Effects tier");
        }

        [Test]
        public void NightAmount_NoonDay_MidnightNight_DuskTurns()
        {
            Assert.AreEqual(0f, SeasonRules.NightAmount(0.5f), 1e-4f, "noon is day (before part 3 the curve was upside down)");
            Assert.AreEqual(0f, SeasonRules.NightAmount(0.3f), 1e-4f, "morning is day");
            Assert.AreEqual(1f, SeasonRules.NightAmount(0f), 1e-4f, "midnight is night");
            Assert.AreEqual(0f, SeasonRules.NightAmount(0.75f), 1e-3f, "night starts at sunset");
            Assert.Greater(SeasonRules.NightAmount(0.8f), 0.8f, "soon after dusk it is night");
        }

        [Test]
        public void LeafKindOf_TagsSyntyTreeInstancesByName()
        {
            var s = Resources.Load<SeasonSettings>("SeasonSettings");
            Assert.IsNotNull(s, "Resources/SeasonSettings");
            int kind = s.LeafKindOf("SM_Env_Tree_01 (3)");
            Assert.Greater(kind, 0, "a scene copy of a Synty tree gets its kind");
            Assert.AreEqual(kind, s.LeafKindOf("sm_env_tree_01"), "any case");
            Assert.AreEqual(0, s.LeafKindOf("SM_Bld_Roof_01"), "a roof is no tree");
            Assert.AreEqual(0, s.LeafKindOf(null));
        }

        [Test]
        public void NameHasWord_WholeWordsOnly_StreetlightIsNeitherRoadNorTree()
        {
            Assert.IsTrue(SeasonSettings.NameHasWord("SM_Env_Sidewalk_01_SF", "Sidewalk"));
            Assert.IsTrue(SeasonSettings.NameHasWord("Road_01", "road"));
            Assert.IsTrue(SeasonSettings.NameHasWord("RoadMarkings", "Road"), "a capital after a small letter starts a word");
            Assert.IsTrue(SeasonSettings.NameHasWord("SM_Env_Tree_03 (2)", "Tree"));
            Assert.IsFalse(SeasonSettings.NameHasWord("SM_Prop_Streetlight_01", "Street"), "the 88 lamps were roads (owner 15 Nov)");
            Assert.IsFalse(SeasonSettings.NameHasWord("SM_Prop_Streetlight_01", "Tree"), "and tree canopies");
            Assert.IsFalse(SeasonSettings.NameHasWord(null, "Road"));
        }

        [Test]
        public void SummerSettings_HazeOffOnLow_MotesHaveAMaterial()
        {
            var s = Resources.Load<SeasonSettings>("SeasonSettings");
            Assert.AreEqual(0f, s.HeatHaze(KryticalNova.Rendering.WorldEffects.Low), "no haze on Low");
            Assert.AreEqual(0, s.Motes(KryticalNova.Rendering.WorldEffects.Low), "no motes on Low");
            Assert.AreEqual(0, s.Fireflies(KryticalNova.Rendering.WorldEffects.Low), "no fireflies on Low");
            Assert.Greater(s.HeatHaze(KryticalNova.Rendering.WorldEffects.High), 0f);
            Assert.IsNotNull(s.motesMaterial, "the motes' soft additive material");
            Assert.Less(s.heatHazeDistance.x, s.heatHazeDistance.y, "the haze begins before it is full");
        }

        [Test]
        public void Litter_BuildsAsTheCanopyThins_AndGoesWithSpringsGreen()
        {
            Assert.AreEqual(0f, SeasonRules.LitterAmount(new FoliageNow { Canopy = 1f }), 1e-5f, "full canopy in Summer: no litter");
            float half = SeasonRules.LitterAmount(new FoliageNow { Canopy = 0.5f });
            float bare = SeasonRules.LitterAmount(new FoliageNow { Canopy = 0.08f });
            Assert.Greater(bare, half, "more litter as the canopy thins");
            Assert.AreEqual(0.92f, bare, 1e-4f, "Winter: all the shed leaves (under the snow)");
            Assert.Less(SeasonRules.LitterAmount(new FoliageNow { Canopy = 0.4f, Fresh = 0.9f }), 0.1f, "Spring's green takes it away");
        }

        [Test]
        public void LitterSweep_NoneInACalm_AllInAStrongGust()
        {
            var range = (9f, 16f);
            Assert.AreEqual(0f, SeasonRules.LitterSweep(4f, 1f, range), 1e-5f, "a light wind sweeps nothing, gust or not");
            Assert.AreEqual(1f, SeasonRules.LitterSweep(17f, 0f, range), 1e-5f, "a gale sweeps all");
            Assert.Greater(SeasonRules.LitterSweep(12f, 0.8f, range), SeasonRules.LitterSweep(12f, 0f, range), "a gust sweeps more");
        }

        [Test]
        public void LawnGreen_FullInSummer_NoneInAutumnDullOrAfterSnow_GrowsBackInSpring()
        {
            Assert.AreEqual(1f, SeasonRules.LawnGreen(new FoliageNow { Grow = 1f }, 0f), 1e-5f, "Summer: green");
            Assert.AreEqual(0f, SeasonRules.LawnGreen(new FoliageNow { Grow = 1f, Dull = 1f }, 0f), 1e-5f, "late Autumn: dull, not green");
            Assert.AreEqual(0f, SeasonRules.LawnGreen(new FoliageNow { Grow = 0f, Brown = 1f }, 0f), 1e-5f, "Winter: brown after the snow");
            Assert.AreEqual(0.5f, SeasonRules.LawnGreen(new FoliageNow { Grow = 0.5f }, 0f), 1e-5f, "Spring: as it grows back");
            Assert.AreEqual(0f, SeasonRules.LawnGreen(new FoliageNow { Grow = 1f }, 1f), 1e-5f, "a dry spell: golden instead");
        }
    }
}
