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
            BuildPerSecond = 1f / 600f, MeltDay = 1f / 1800f, NightFactor = 0.25f, RainFactor = 3f, WinterMeltDay = 1f / 14400f, WinterNightFactor = 0f,
        };

        [Test]
        public void SnowCover_BuildsAndMelts_AtTheSettingsRates_StaysInRange_AndKeepsTheFloor()
        {
            SnowRates r = Rates;
            float snowing = SeasonRules.SnowRate(r, PrecipitationType.Snow, 0.6f, false, Season.Winter, false);
            Assert.AreEqual(0.6f / 600f, snowing, 1e-7, "piles up by intensity");
            Assert.AreEqual(1f, SeasonRules.SnowCover(0f, snowing, 0, 1000.0, 0f), 1e-4, "Snow 0.6 covers fully in about 17 minutes");
            Assert.Less(SeasonRules.SnowCover(0f, snowing, 0, 900.0, 0f), 1f, "not yet at 15 minutes");

            float day = SeasonRules.SnowRate(r, PrecipitationType.None, 0f, false, Season.Spring, false);
            Assert.AreEqual(-1f / 1800f, day, 1e-7, "melts in 30 minutes on a dry day");
            Assert.AreEqual(0f, SeasonRules.SnowCover(1f, day, 0, 1800.0, 0f), 1e-4);
            float night = SeasonRules.SnowRate(r, PrecipitationType.None, 0f, true, Season.Autumn, false);
            Assert.AreEqual(day * 0.25f, night, 1e-7, "lingers at night");
            float rain = SeasonRules.SnowRate(r, PrecipitationType.Rain, 0.8f, true, Season.Spring, false);
            Assert.AreEqual(day * 3f, rain, 1e-7, "rain washes it off three times as fast");
            float winterDay = SeasonRules.SnowRate(r, PrecipitationType.None, 0f, false, Season.Winter, false);
            Assert.AreEqual(-1f / 14400f, winterDay, 1e-7, "lasts about 4 hours on a Winter day");
            Assert.AreEqual(0f, SeasonRules.SnowRate(r, PrecipitationType.None, 0f, true, Season.Winter, false), "a Winter night keeps it");
            Assert.AreEqual(winterDay, SeasonRules.SnowRate(r, PrecipitationType.None, 0f, false, Season.Summer, true), 1e-9, "an always-winter map melts as in Winter");

            for (double t = 0; t < 100000; t += 777)
            {
                float up = SeasonRules.SnowCover(0.3f, 0.01f, 0, t, 0f), down = SeasonRules.SnowCover(0.3f, -0.01f, 0, t, 0f);
                Assert.That(up, Is.InRange(0f, 1f)); Assert.That(down, Is.InRange(0f, 1f));
                Assert.GreaterOrEqual(SeasonRules.SnowCover(0.9f, -0.01f, 0, t, 0.6f), 0.6f, "never below an always-winter map's floor");
            }
            Assert.AreEqual(0.3f, SeasonRules.SnowCover(0.3f, 0.5f, 10, 5, 0f), 1e-6, "nothing before the anchor");
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
    }
}
