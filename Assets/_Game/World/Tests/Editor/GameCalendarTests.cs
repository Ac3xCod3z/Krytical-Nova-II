using System;
using System.Collections.Generic;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The game calendar (owner, 15 Nov 2026): the date from the season clock, the moon by the sky presenter's own formula, sky events
    /// with countdowns found by the sky's own rules, the forecast's spells and temperatures by the server's, and the weather shares.
    /// The server-against-calendar forecast check is <c>SeasonSyncTests.Calendar_Forecast_MatchesTheServersSpells_AndAir</c>.
    /// </summary>
    public class GameCalendarTests
    {
        readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
        T Make<T>() where T : ScriptableObject { var o = ScriptableObject.CreateInstance<T>(); made.Add(o); return o; }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in made) UnityEngine.Object.DestroyImmediate(o);
            made.Clear();
        }

        static double Unix(int y, int m, int d, int h = 0) => (new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalSeconds;

        static CalendarClock Clock(double unix, long day = 7000, float time01 = 0.5f) => new CalendarClock
        {
            UnixNow = unix, Day = day, DayBase = 200, Time01 = time01, DaySeconds = 3600, SkySeed = 1234, LunarCycleDays = 8f, WeatherSeed = 99,
        };

        [Test]
        public void Date_IsTheSeasonDayOf14_TheYear_AndWhenTheNextSeasonBegins()
        {
            var s = Make<SeasonSettings>();
            CalendarDate d = GameCalendar.Date(Clock(Unix(2026, 11, 13, 12), time01: 0.875f), s.Calendar);
            Assert.AreEqual(Season.Autumn, d.Season, "13 Nov 2026 is Autumn");
            Assert.AreEqual(5, d.DayOfSeason);
            Assert.AreEqual(14, d.SeasonDays);
            Assert.AreEqual(6, d.Year, "the sixth year of seasons since 5 Jan 2026");
            Assert.AreEqual(Season.Winter, d.NextSeason);
            Assert.AreEqual(Unix(2026, 11, 23) - Unix(2026, 11, 13, 12), d.SecondsToNextSeason, 1.0, "Winter begins 23 Nov 00:00 UTC");
            Assert.AreEqual(21, d.Hour);
            Assert.AreEqual(0, d.Minute);
        }

        [Test]
        public void Moon_FollowsThePresentersPhase_AndCountsToTheNextFullAndNewMoon()
        {
            CalendarClock c = Clock(Unix(2026, 11, 15), day: 7001, time01: 0.25f);
            MoonNow m = GameCalendar.Moon(c);
            float presenter = Mathf.Repeat((7001 - 200 + 0.25f) / 8f, 1f);   // EnvironmentPresenter: (DayCount + time01) / lunarCycleDays
            Assert.AreEqual(presenter, m.Phase01, 1e-5f);
            // walk forward the days it names and the phase is full / new there
            double t = 7001 + 0.25 + m.DaysToFull;
            Assert.AreEqual(0.5f, GameCalendar.MoonPhase(c, (long)Math.Floor(t), (float)(t - Math.Floor(t))), 1e-4f, "full after DaysToFull");
            t = 7001 + 0.25 + m.DaysToNew;
            float newPhase = GameCalendar.MoonPhase(c, (long)Math.Floor(t), (float)(t - Math.Floor(t)));
            Assert.That(Mathf.Min(newPhase, 1f - newPhase), Is.LessThan(1e-4f), "new after DaysToNew");
            Assert.Less(m.DaysToFull, 8.0001); Assert.Less(m.DaysToNew, 8.0001);
            Assert.AreEqual("Full Moon", GameCalendar.MoonNames[GameCalendar.MoonIndex(0.5f)]);
            Assert.AreEqual("New Moon", GameCalendar.MoonNames[GameCalendar.MoonIndex(0.99f)]);
        }

        [Test]
        public void Grid_StartsOnANewMoon_AndMarksTheFullMoonOnce_PerCycle()
        {
            var s = Make<SeasonSettings>();
            var sky = Make<SkyEventSettings>();
            CalendarClock c = Clock(Unix(2026, 11, 15), day: 7005);
            var days = new List<CalendarDay>();
            GameCalendar.Days(c, sky, s.Calendar, GameCalendar.MoonCycleStart(c), 32, days);
            Assert.AreEqual(32, days.Count);
            Assert.IsTrue(days.Exists(x => x.Today), "today is on the grid");
            for (int r = 0; r < 4; r++)
            {
                Assert.AreEqual(0, days[r * 8].MoonIndex, "each row begins with the new moon");
                int full = 0;
                for (int k = 0; k < 8; k++) if ((days[r * 8 + k].Marks & SkyMarks.FullMoon) != 0) full++;
                Assert.AreEqual(1, full, "one full moon a cycle");
            }
        }

        [Test]
        public void Upcoming_FindsTheSkysOwnNights_WithCountdownsToTheirStart()
        {
            var s = Make<SeasonSettings>();
            var sky = Make<SkyEventSettings>();
            CalendarClock c = Clock(Unix(2026, 11, 15), day: 7000, time01: 0.5f);
            var events = new List<UpcomingEvent>();
            GameCalendar.Upcoming(c, sky, s.Calendar, events);
            UpcomingEvent ember = events.Find(e => e.Kind == CalendarEventKind.Emberfall);
            Assert.AreEqual(sky.showerDayOfYear, ((ember.Day % sky.gameYearDays) + sky.gameYearDays) % sky.gameYearDays, "The Emberfall's night of the year");
            Assert.IsTrue(SkyCalendar.IsShowerNight(ember.Day, sky));
            Assert.AreEqual((ember.Day - c.Day + 0.75 - c.Time01) * c.DaySeconds, ember.SecondsUntil, 0.01, "counts to 18:00 of its night");
            // the sky draws it that night: Evaluate at its midnight says so
            var mid = new SkyClock { Seed = c.SkySeed, Day = ember.Day + 1, Time01 = 0f, DayLengthSeconds = 3600, MonthDays = 8 };
            Assert.IsTrue(SkyCalendar.Evaluate(mid, sky).ShowerNight);

            UpcomingEvent comet = events.Find(e => e.Kind == CalendarEventKind.PaleWanderer);
            Assert.IsTrue(SkyCalendar.IsCometNight(c.SkySeed, comet.Day, sky, out _), "The Pale Wanderer's night");
            for (long n = c.Day; n < comet.Day; n++) Assert.IsFalse(SkyCalendar.IsCometNight(c.SkySeed, n, sky, out _), "and no earlier one");
            mid.Day = comet.Day + 1;
            Assert.Greater(SkyCalendar.Evaluate(mid, sky).Comet, 0f, "the comet is in that night's sky");

            UpcomingEvent eclipse = events.Find(e => e.Kind == CalendarEventKind.Eclipse);
            Assert.IsTrue(SkyCalendar.IsEclipseDay(eclipse.Day, sky));
            var noon = new SkyClock { Seed = c.SkySeed, Day = eclipse.Day, Time01 = sky.eclipsePeak01, DayLengthSeconds = 3600, MonthDays = 8 };
            Assert.Greater(SkyCalendar.Evaluate(noon, sky).Eclipse, 0.9f, "the sun is covered at its peak");

            for (int i = 1; i < events.Count; i++) Assert.LessOrEqual(events[i - 1].StartUnix, events[i].StartUnix, "soonest first");
        }

        [Test]
        public void Upcoming_AlwaysFindsEveryEvent_WhereverInItsCycleTodayFalls()
        {
            var s = Make<SeasonSettings>();
            var sky = Make<SkyEventSettings>();
            var events = new List<UpcomingEvent>();
            // the watched check's day (7,636, a Pale Wanderer visit just gone) and days spread over two comet periods
            for (long day = 7636; day < 7636 + 2 * sky.cometPeriodDays; day += 97)
            {
                GameCalendar.Upcoming(Clock(Unix(2026, 11, 15), day: day), sky, s.Calendar, events);
                Assert.IsTrue(events.Exists(e => e.Kind == CalendarEventKind.PaleWanderer), $"day {day}: The Pale Wanderer is listed");
                Assert.IsTrue(events.Exists(e => e.Kind == CalendarEventKind.Eclipse), $"day {day}: the eclipse is listed");
                Assert.IsTrue(events.Exists(e => e.Kind == CalendarEventKind.Emberfall), $"day {day}: The Emberfall is listed");
            }
        }

        [Test]
        public void Aurora_IsListed_OnlyOnWinterNights_OrOnAnAlwaysWinterMap()
        {
            var s = Make<SeasonSettings>();
            var sky = Make<SkyEventSettings>();
            sky.auroraNightShare = 0.5f;
            var events = new List<UpcomingEvent>();
            // mid Autumn (Winter from 23 Nov): the next aurora night listed must be in the coming Winter, never this Autumn
            CalendarClock c = Clock(Unix(2026, 11, 15), day: 4000);
            GameCalendar.Upcoming(c, sky, s.Calendar, events);
            UpcomingEvent a = events.Find(e => e.Kind == CalendarEventKind.Aurora);
            Assert.AreEqual(CalendarEventKind.Aurora, a.Kind, "one is found within the horizon");
            Assert.AreEqual(Season.Winter, GameCalendar.DominantAt(s.Calendar, GameCalendar.DayStartUnix(c, a.Day + 1)), "on a winter night");
            // an always-winter map: the very next aurora night of the sky
            c.AlwaysWinter = true;
            GameCalendar.Upcoming(c, sky, s.Calendar, events);
            a = events.Find(e => e.Kind == CalendarEventKind.Aurora);
            long first = c.Day;
            while (!SkyCalendar.IsAuroraNight(c.SkySeed, first, sky)) first++;
            Assert.AreEqual(first, a.Day);
        }

        [Test]
        public void Forecast_RollsTheServersSpells_AndBracketsTheDaysAir()
        {
            var s = Make<SeasonSettings>();
            var autumn = Make<SeasonProfile>();
            autumn.season = Season.Autumn;
            autumn.baseTemperature = 8f; autumn.dailySwing = 6f;
            autumn.spells.Add(new WeatherSpell { name = "Wet Run", chance = 1f, minDays = 1, maxDays = 3, wet = true, temperatureOffset = -2f });
            autumn.spells.Add(new WeatherSpell { name = "Crisp", chance = 1f, minDays = 1, maxDays = 2, dry = true, temperatureOffset = 1f });
            s.autumn = autumn;
            CalendarClock c = Clock(Unix(2026, 11, 13, 12), day: 7100, time01: 0.5f);
            var days = new List<ForecastDay>();
            GameCalendar.Forecast(c, s, null, 7, days);
            Assert.AreEqual(7, days.Count);
            for (int i = 0; i < 7; i++)
            {
                ForecastDay d = days[i];
                Assert.AreEqual(c.Day + i, d.Day);
                Assert.AreEqual(Season.Autumn, d.Season);
                int spell = SeasonRules.SpellAt(autumn.SpellTable, c.WeatherSeed, Season.Autumn, d.Day, s.spellBlockDays).Spell;
                Assert.AreEqual(spell, d.Spell, "the server's roll for that day");
                Assert.AreEqual(autumn.spells[spell].name, d.SpellName);
                Assert.AreEqual(autumn.spells[spell].wet ? ForecastLook.Rain : ForecastLook.Clear, d.Look);
                for (float t = 0f; t < 1f; t += 0.1f)
                {
                    float air = SeasonRules.Quantize(GameCalendar.TemperatureAt(c, s, s.Calendar, d.Day, t, autumn.spells[spell]), s.temperatureStep);
                    Assert.That(air, Is.InRange(d.LowC, d.HighC), "every hour's air lies between the low and the high");
                }
                Assert.Greater(d.HighC, d.LowC);
            }
        }

        [Test]
        public void WeatherShares_AreTheLongRunMix_OfTheWeightedPick()
        {
            var clear = Make<WeatherProfile>(); clear.name = "WP_Clear";
            var rain = Make<WeatherProfile>(); rain.name = "WP_Rain";
            rain.precipitation = PrecipitationType.Rain; rain.precipitationIntensity = 0.6f;
            foreach (WeatherProfile p in new[] { clear, rain })
            {
                p.durationMin = p.durationMax = 300f;
                p.next.Add(new WeatherProfile.NextWeather { profile = clear, weight = 1f });
                p.next.Add(new WeatherProfile.NextWeather { profile = rain, weight = 1f });
            }
            var catalog = Make<WeatherCatalog>();
            catalog.SetProfiles(new List<WeatherProfile> { clear, rain });
            GameCalendar.WeatherShares(catalog, null, null, out float r, out float sn, out float st);
            Assert.AreEqual(0.5f, r, 1e-3f, "even weights: half the day rains");
            Assert.AreEqual(0f, sn); Assert.AreEqual(0f, st);
            var wet = new WeatherSpell { name = "Wet" };
            wet.weatherWeights.Add(new SeasonProfile.WeatherWeight { profile = rain, multiplier = 3f });
            GameCalendar.WeatherShares(catalog, null, wet, out r, out _, out _);
            Assert.AreEqual(0.75f, r, 1e-3f, "the spell triples rain's weight");
            rain.durationMin = rain.durationMax = 900f;
            GameCalendar.WeatherShares(catalog, null, null, out r, out _, out _);
            Assert.AreEqual(0.75f, r, 1e-3f, "rain that lasts three times as long fills three quarters of the day");
        }

        [Test]
        public void Words_NameWeather_AndSpans()
        {
            Assert.AreEqual("Partly Cloudy", GameCalendar.WeatherName("WP_PartlyCloudy"));
            Assert.AreEqual("Light Rain", GameCalendar.WeatherName("WP_LightRain"));
            Assert.AreEqual("now", GameCalendar.Span(30));
            Assert.AreEqual("12 min", GameCalendar.Span(12 * 60 + 5));
            Assert.AreEqual("3 h 20 min", GameCalendar.Span(3 * 3600 + 20 * 60));
            Assert.AreEqual("4 d 6 h", GameCalendar.Span(4 * 86400 + 6 * 3600));
        }
    }
}
