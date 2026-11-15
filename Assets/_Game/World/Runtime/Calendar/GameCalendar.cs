using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>The clocks the game calendar reads (from <see cref="GameCalendarSource"/>; tests build their own).</summary>
    public struct CalendarClock
    {
        /// <summary>Real time now, Unix seconds (UTC): the seasons turn on it.</summary>
        public double UnixNow;
        /// <summary>The in-game calendar day (<see cref="WorldEnvironmentState.CalendarDay"/>): weather spells and sky events count it.</summary>
        public long Day;
        /// <summary>The day the moon counts from (<c>CalendarDay - DayCount</c>, as the sky presenter's moon does).</summary>
        public long DayBase;
        /// <summary>The time of the in-game day, 0 = midnight.</summary>
        public float Time01;
        /// <summary>Real seconds one in-game day lasts (the day length over the time scale; 3600 = one real hour).</summary>
        public double DaySeconds;
        /// <summary>The world's sky seed (<see cref="WorldEnvironmentState.Seed"/>).</summary>
        public int SkySeed;
        /// <summary>In-game days in one moon cycle (<see cref="DayCycleProfile.lunarCycleDays"/>).</summary>
        public float LunarCycleDays;
        /// <summary>This map's weather seed (<see cref="SeasonRules.WeatherSeed"/>), the one the server rolls the spells with.</summary>
        public int WeatherSeed;
        /// <summary>An always-winter map: Winter's weather, and the aurora every season.</summary>
        public bool AlwaysWinter;
    }

    /// <summary>Today's date: the season, its day (real days), the year of seasons, and the in-game day and time.</summary>
    public struct CalendarDate
    {
        public Season Season;
        /// <summary>Day of the season, from 1 (real days).</summary>
        public int DayOfSeason;
        /// <summary>Days in one season (14).</summary>
        public int SeasonDays;
        /// <summary>The year of seasons, from 1 (the first Spring began Year 1).</summary>
        public long Year;
        /// <summary>0 .. 1 through the season.</summary>
        public float SeasonProgress;
        public Season NextSeason;
        public double SecondsToNextSeason;
        public long Day;
        public int Hour, Minute;
    }

    /// <summary>The moon: its phase (0 new, 0.5 full), the phase's index in <see cref="GameCalendar.MoonNames"/>, and the next full and new moon.</summary>
    public struct MoonNow
    {
        public float Phase01;
        public int Index;
        public float Lit;
        /// <summary>In-game days until the next full moon and new moon (0 = now).</summary>
        public double DaysToFull, DaysToNew;
    }

    /// <summary>What a calendar day holds (its night: the evening of that day into the next morning).</summary>
    [Flags]
    public enum SkyMarks : ushort
    {
        None = 0, Emberfall = 1, PaleWanderer = 2, Eclipse = 4, Aurora = 8, Nebula = 16, FullMoon = 32, NewMoon = 64,
    }

    /// <summary>One cell of the month grid: an in-game day.</summary>
    public struct CalendarDay
    {
        public long Day;
        public double StartUnix;
        /// <summary>The moon's phase index at that night's midnight.</summary>
        public int MoonIndex;
        public SkyMarks Marks;
        /// <summary>The constellation overhead at that night's midnight (<see cref="StarCharts.All"/>).</summary>
        public int Constellation;
        public bool Today;
    }

    public enum CalendarEventKind : byte { FullMoon, NewMoon, Emberfall, PaleWanderer, Eclipse, Aurora, Season }

    /// <summary>A coming event: when it starts (real Unix seconds), how long until then, and whether it is on now.</summary>
    public struct UpcomingEvent
    {
        public CalendarEventKind Kind;
        public long Day;
        public double StartUnix;
        public double SecondsUntil;
        /// <summary>Nights it lasts from its start (The Pale Wanderer: the nights of its visit still to come).</summary>
        public int Nights;
        public bool Now;
        /// <summary>For <see cref="CalendarEventKind.Season"/>: the season that begins.</summary>
        public Season Season;
    }

    public enum ForecastLook : byte { Clear, Mixed, Rain, Snow, Storm, Blizzard, Heat }

    /// <summary>One day of the forecast: the season's weather spell, the high and low, and the share of the day it rains, snows and storms.</summary>
    public struct ForecastDay
    {
        public long Day;
        public double StartUnix;
        public Season Season;
        /// <summary>The spell's index in the season's list (-1 none) and its name ("" none).</summary>
        public int Spell;
        public string SpellName;
        /// <summary>High and low (deg C, in the settings' steps) before the passing weather's own few degrees.</summary>
        public float HighC, LowC;
        /// <summary>Shares 0-1 of the day the weather mix spends raining, snowing and in thunderstorms.</summary>
        public float Rain, Snow, Storm;
        public bool HeatHaze;
        public ForecastLook Look;
    }

    /// <summary>
    /// The game calendar (owner, 15 Nov 2026: "a game based calendar to keep track of lunar calendars, comets, seasons, weather forecasts"):
    /// today's date, the moon, the sky's events and a 7-day forecast, all worked out on the player's own machine from the same pure rules the
    /// world uses (<see cref="SeasonRules"/>, <see cref="SkyCalendar"/>), so it needs nothing new from the server and it always matches what
    /// then happens. Pure and tested (<c>GameCalendarTests</c>); the Map window's Calendar page and the minimap's date line show it.
    /// </summary>
    public static class GameCalendar
    {
        public static readonly string[] MoonNames =
        {
            "New Moon", "Waxing Crescent", "First Quarter", "Waxing Gibbous", "Full Moon", "Waning Gibbous", "Last Quarter", "Waning Crescent",
        };

        static long Mod(long a, long m) => ((a % m) + m) % m;

        /// <summary>Real Unix seconds when an in-game day begins (its midnight).</summary>
        public static double DayStartUnix(in CalendarClock c, long day) => c.UnixNow - c.Time01 * c.DaySeconds + (day - c.Day) * c.DaySeconds;

        /// <summary>Today's date.</summary>
        public static CalendarDate Date(in CalendarClock c, in SeasonCalendar cal)
        {
            SeasonNow n = SeasonRules.At(cal, c.UnixNow);
            int days = Math.Max(1, (int)Math.Round(cal.SeasonSeconds / 86400.0));
            float minutes = Mathf.Repeat(c.Time01, 1f) * 1440f;
            return new CalendarDate
            {
                Season = n.Season,
                DayOfSeason = Math.Clamp(n.Day, 1, days),
                SeasonDays = days,
                Year = (long)Math.Floor(n.Index / 4.0) + 1,
                SeasonProgress = cal.SeasonSeconds > 0 ? (float)Math.Clamp(n.Elapsed / cal.SeasonSeconds, 0.0, 1.0) : 0f,
                NextSeason = (Season)(((int)n.Season + 1) % 4),
                SecondsToNextSeason = Math.Max(0.0, SeasonRules.NextTurnUnix(cal, c.UnixNow) - c.UnixNow),
                Day = c.Day,
                Hour = Math.Min(23, (int)(minutes / 60f)),
                Minute = Math.Min(59, (int)(minutes % 60f)),
            };
        }

        // ------------------------------------------------------------------ the moon

        /// <summary>The moon's phase on day <paramref name="day"/> at <paramref name="time01"/>: the sky presenter's own formula.</summary>
        public static float MoonPhase(in CalendarClock c, long day, float time01) =>
            Mathf.Repeat((float)((day - c.DayBase) + (double)time01) / Mathf.Max(1f, c.LunarCycleDays), 1f);

        /// <summary>The phase's index in <see cref="MoonNames"/> (0 new, 4 full).</summary>
        public static int MoonIndex(float phase01) => (int)Mod((long)Math.Floor(phase01 * 8f + 0.5f), 8);

        /// <summary>The moon now, and the next full and new moon.</summary>
        public static MoonNow Moon(in CalendarClock c)
        {
            float phase = MoonPhase(c, c.Day, c.Time01);
            float cycle = Mathf.Max(1f, c.LunarCycleDays);
            return new MoonNow
            {
                Phase01 = phase,
                Index = MoonIndex(phase),
                Lit = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f),
                DaysToFull = Mathf.Repeat(0.5f - phase, 1f) * cycle,
                DaysToNew = Mathf.Repeat(1f - phase, 1f) * cycle,
            };
        }

        // ------------------------------------------------------------------ seasons by the day

        /// <summary>The season showing at a real moment: the calendar's, until its blend is half way in (the server's own rule).</summary>
        public static Season DominantAt(in SeasonCalendar cal, double unix)
        {
            SeasonNow n = SeasonRules.At(cal, unix);
            return SeasonRules.Dominant(n.Previous, n.Season, SeasonRules.Blend(n.StartUnix, cal.TransitionSeconds, unix));
        }

        static bool AuroraAllowed(in CalendarClock c, in SeasonCalendar cal, long night) =>
            c.AlwaysWinter || DominantAt(cal, DayStartUnix(c, night + 1)) == Season.Winter;

        // ------------------------------------------------------------------ the month grid

        /// <summary>The first day of the moon cycle today is in (its column 0 is the new moon's night).</summary>
        public static long MoonCycleStart(in CalendarClock c)
        {
            int cycle = Math.Max(1, Mathf.RoundToInt(c.LunarCycleDays));
            return c.Day - Mod(c.Day + 1 - c.DayBase, cycle);
        }

        /// <summary>The sky's marks of one day and its night.</summary>
        public static SkyMarks MarksOf(in CalendarClock c, SkyEventSettings sky, in SeasonCalendar cal, long day)
        {
            var m = SkyMarks.None;
            int index = MoonIndex(MoonPhase(c, day + 1, 0f));
            if (index == 4) m |= SkyMarks.FullMoon;
            if (index == 0) m |= SkyMarks.NewMoon;
            if (sky == null) return m;
            if (SkyCalendar.IsShowerNight(day, sky)) m |= SkyMarks.Emberfall;
            if (SkyCalendar.IsCometNight(c.SkySeed, day, sky, out _)) m |= SkyMarks.PaleWanderer;
            if (SkyCalendar.IsEclipseDay(day, sky)) m |= SkyMarks.Eclipse;
            if (SkyCalendar.IsNebulaNight(day, SkyCalendar.MonthOf(sky, c.LunarCycleDays), sky)) m |= SkyMarks.Nebula;
            if (SkyCalendar.IsAuroraNight(c.SkySeed, day, sky) && AuroraAllowed(c, cal, day)) m |= SkyMarks.Aurora;
            return m;
        }

        /// <summary>The constellation overhead at midnight of a day's night.</summary>
        public static int ConstellationOf(in CalendarClock c, SkyEventSettings sky, long night)
        {
            var clock = new SkyClock { Seed = c.SkySeed, Day = night + 1, Time01 = 0f, MonthDays = c.LunarCycleDays, DayLengthSeconds = (float)c.DaySeconds };
            return StarCharts.NearestTo(SkyCalendar.DomeAngle(clock, sky));
        }

        /// <summary>The month grid: <paramref name="count"/> days from <paramref name="first"/>.</summary>
        public static void Days(in CalendarClock c, SkyEventSettings sky, in SeasonCalendar cal, long first, int count, List<CalendarDay> into)
        {
            into.Clear();
            for (long d = first; d < first + count; d++)
                into.Add(new CalendarDay
                {
                    Day = d, StartUnix = DayStartUnix(c, d), MoonIndex = MoonIndex(MoonPhase(c, d + 1, 0f)), Marks = MarksOf(c, sky, cal, d),
                    Constellation = ConstellationOf(c, sky, d), Today = d == c.Day,
                });
        }

        // ------------------------------------------------------------------ coming events

        /// <summary>Real Unix seconds a day's night begins (18:00) and ends (06:00 the next day).</summary>
        public static (double start, double end) NightSpan(in CalendarClock c, long night) =>
            (DayStartUnix(c, night) + 0.75 * c.DaySeconds, DayStartUnix(c, night + 1) + 0.25 * c.DaySeconds);

        /// <summary>
        /// The next of each event (one each, soonest first): full and new moon, The Emberfall, The Pale Wanderer, a total eclipse, an aurora
        /// night (only where the season allows), the next season. An event on now counts (<see cref="UpcomingEvent.Now"/>).
        /// </summary>
        public static void Upcoming(in CalendarClock c, SkyEventSettings sky, in SeasonCalendar cal, List<UpcomingEvent> into, int horizonDays = 3000)
        {
            into.Clear();
            MoonNow moon = Moon(c);
            into.Add(At(c, CalendarEventKind.FullMoon, c.UnixNow + moon.DaysToFull * c.DaySeconds, 1));
            into.Add(At(c, CalendarEventKind.NewMoon, c.UnixNow + moon.DaysToNew * c.DaySeconds, 1));
            double turn = SeasonRules.NextTurnUnix(cal, c.UnixNow);
            var season = At(c, CalendarEventKind.Season, turn, 0);
            season.Season = (Season)(((int)SeasonRules.At(cal, c.UnixNow).Season + 1) % 4);
            into.Add(season);

            if (sky != null)
            {
                bool shower = false, comet = false, eclipse = false, aurora = false;
                long first = SkyCalendar.NightOf(c.Day, c.Time01);
                for (long n = first; n < first + horizonDays && !(shower && comet && eclipse && aurora); n++)
                {
                    (double start, double end) = NightSpan(c, n);
                    bool open = end > c.UnixNow;
                    if (!shower && open && SkyCalendar.IsShowerNight(n, sky)) { shower = true; into.Add(Night(c, CalendarEventKind.Emberfall, n, start, 1)); }
                    if (!comet && open && SkyCalendar.IsCometNight(c.SkySeed, n, sky, out int visit))
                    {
                        comet = true;
                        into.Add(Night(c, CalendarEventKind.PaleWanderer, n, start, Math.Max(1, sky.cometNights - visit)));
                    }
                    if (!aurora && open && SkyCalendar.IsAuroraNight(c.SkySeed, n, sky) && AuroraAllowed(c, cal, n))
                    {
                        aurora = true;
                        into.Add(Night(c, CalendarEventKind.Aurora, n, start, 1));
                    }
                    if (!eclipse && n >= c.Day && SkyCalendar.IsEclipseDay(n, sky))
                    {
                        double reach = (sky.eclipseSeconds * 0.5 + sky.eclipseApproachSeconds) / Math.Max(1.0, c.DaySeconds);
                        double peak = DayStartUnix(c, n) + sky.eclipsePeak01 * c.DaySeconds;
                        if (peak + reach * c.DaySeconds > c.UnixNow)
                        {
                            eclipse = true;
                            var e = At(c, CalendarEventKind.Eclipse, peak - reach * c.DaySeconds, 1);
                            e.Day = n;
                            e.Now = e.StartUnix <= c.UnixNow;
                            into.Add(e);
                        }
                    }
                }
            }
            into.Sort((a, b) => a.StartUnix.CompareTo(b.StartUnix));
        }

        static UpcomingEvent At(in CalendarClock c, CalendarEventKind kind, double unix, int nights)
        {
            long day = c.Day + (long)Math.Floor((unix - DayStartUnix(c, c.Day)) / Math.Max(1.0, c.DaySeconds));
            return new UpcomingEvent { Kind = kind, Day = day, StartUnix = unix, SecondsUntil = Math.Max(0.0, unix - c.UnixNow), Nights = nights };
        }

        static UpcomingEvent Night(in CalendarClock c, CalendarEventKind kind, long night, double start, int nights) => new UpcomingEvent
        {
            Kind = kind, Day = night, StartUnix = start, SecondsUntil = Math.Max(0.0, start - c.UnixNow), Nights = nights, Now = start <= c.UnixNow,
        };

        // ------------------------------------------------------------------ the forecast

        /// <summary>
        /// The forecast for <paramref name="days"/> in-game days from today: each day's weather spell (rolled exactly as the server rolls it),
        /// its high and low (the season's curve, the spell's offset and the day's own wobble, as the server works the air out) and the share
        /// of the day the season's and the spell's weather mix spends in rain, snow and thunderstorms.
        /// </summary>
        public static void Forecast(in CalendarClock c, SeasonSettings s, WeatherCatalog catalog, int days, List<ForecastDay> into)
        {
            into.Clear();
            if (s == null) return;
            SeasonCalendar cal = s.Calendar;
            for (long d = c.Day; d < c.Day + days; d++)
            {
                double start = DayStartUnix(c, d);
                Season season = c.AlwaysWinter ? Season.Winter : DominantAt(cal, start);
                SeasonProfile profile = s.Profile(season);
                var day = new ForecastDay { Day = d, StartUnix = start, Season = season, Spell = -1, SpellName = "" };
                WeatherSpell spell = null;
                if (profile != null && profile.spells.Count > 0)
                {
                    day.Spell = SeasonRules.SpellAt(profile.SpellTable, c.WeatherSeed, season, d, s.spellBlockDays).Spell;
                    spell = profile.Spell(day.Spell);
                    if (spell != null) day.SpellName = spell.name;
                }
                (day.HighC, day.LowC) = HighLow(c, s, cal, d, spell);
                WeatherShares(catalog, profile, spell, out day.Rain, out day.Snow, out day.Storm);
                day.HeatHaze = day.HighC >= s.heatHazeFromTo.x;
                day.Look = LookOf(day, spell);
                into.Add(day);
            }
        }

        /// <summary>The day's temperature at a clock time (deg C, unquantized), as the server works it out with no weather offset.</summary>
        public static float TemperatureAt(in CalendarClock c, SeasonSettings s, in SeasonCalendar cal, long day, float clock01, WeatherSpell spell)
        {
            double unix = DayStartUnix(c, day) + clock01 * c.DaySeconds;
            SeasonNow n = SeasonRules.At(cal, unix);
            float blend = SeasonRules.Blend(n.StartUnix, cal.TransitionSeconds, unix);
            SeasonProfile from = s.Profile(c.AlwaysWinter ? Season.Winter : n.Previous), to = s.Profile(c.AlwaysWinter ? Season.Winter : n.Season);
            TemperatureCurve a = from != null ? from.Temperature : new TemperatureCurve { BaseC = 12f, SwingC = 8f };
            TemperatureCurve b = to != null ? to.Temperature : a;
            TemperatureCurve curve = TemperatureCurve.Lerp(a, b, blend);
            float share = DaylightShare(s, n, blend);
            float wobble = SeasonRules.Hash01(SeasonRules.WobbleSeed(c.WeatherSeed), day);
            return SeasonRules.TemperatureAt(curve, clock01, share, s.warmestClock, spell != null ? spell.temperatureOffset : 0f, 0f, wobble, s.dayWobble);
        }

        /// <summary>The share of the day the sun is up: the calendar's seasons blended, never the climate's (the server's DaylightShare).</summary>
        static float DaylightShare(SeasonSettings s, in SeasonNow n, float blend)
        {
            SeasonProfile from = s.Profile(n.Previous), to = s.Profile(n.Season);
            float a = from != null ? from.daylightShare : 0.5f, b = to != null ? to.daylightShare : 0.5f;
            return a + (b - a) * blend;
        }

        static (float high, float low) HighLow(in CalendarClock c, SeasonSettings s, in SeasonCalendar cal, long day, WeatherSpell spell)
        {
            float high = float.MinValue, low = float.MaxValue;
            // every quarter hour, plus the curve's coldest (sunrise) and warmest moments
            double mid = DayStartUnix(c, day) + 0.5 * c.DaySeconds;
            SeasonNow n = SeasonRules.At(cal, mid);
            float sunrise = SeasonRules.Sunrise(DaylightShare(s, n, SeasonRules.Blend(n.StartUnix, cal.TransitionSeconds, mid)));
            for (int k = 0; k <= 97; k++)
            {
                float clock = k < 96 ? k / 96f : k == 96 ? s.warmestClock : sunrise;
                float t = TemperatureAt(c, s, cal, day, clock, spell);
                high = Math.Max(high, t);
                low = Math.Min(low, t);
            }
            return (SeasonRules.Quantize(high, s.temperatureStep), SeasonRules.Quantize(low, s.temperatureStep));
        }

        static float[] s_Pi = new float[0], s_Next = new float[0];

        /// <summary>
        /// The share of the day the weather spends raining, snowing and storming under a season and a spell: the long-run mix of the
        /// weather's own pick (each weather's Next list with the season's and the spell's weights and extra choices, as
        /// <see cref="SeasonRules.Pick"/>), each weather weighted by how long it lasts. Main thread (shared scratch).
        /// </summary>
        public static void WeatherShares(WeatherCatalog catalog, SeasonProfile profile, WeatherSpell spell, out float rain, out float snow, out float storm)
        {
            rain = snow = storm = 0f;
            int count = catalog != null ? catalog.Count : 0;
            if (count == 0) return;
            if (s_Pi.Length < count) { s_Pi = new float[count]; s_Next = new float[count]; }
            for (int i = 0; i < count; i++) s_Pi[i] = 1f / count;
            for (int step = 0; step < 48; step++)
            {
                Array.Clear(s_Next, 0, count);
                for (int i = 0; i < count; i++)
                {
                    WeatherProfile from = catalog.Get(i);
                    float total = Total(from != null ? from.next : null, catalog, profile, spell, null, 0f)
                                  + Total(profile != null ? profile.extraNext : null, catalog, profile, spell, null, 0f)
                                  + Total(spell != null ? spell.extraNext : null, catalog, profile, spell, null, 0f);
                    if (total <= 0f) { s_Next[i] += s_Pi[i]; continue; }
                    float share = s_Pi[i] / total;
                    Total(from != null ? from.next : null, catalog, profile, spell, s_Next, share);
                    Total(profile != null ? profile.extraNext : null, catalog, profile, spell, s_Next, share);
                    Total(spell != null ? spell.extraNext : null, catalog, profile, spell, s_Next, share);
                }
                (s_Pi, s_Next) = (s_Next, s_Pi);
            }
            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                WeatherProfile w = catalog.Get(i);
                float t = s_Pi[i] * (w != null ? (w.durationMin + w.durationMax) * 0.5f : 0f);
                sum += t;
                if (w == null) continue;
                if (w.RainIntensity > 0f) rain += t;
                if (w.SnowIntensity > 0f) snow += t;
                if (w.lightningPerMinute > 0f) storm += t;
            }
            if (sum <= 0f) { rain = snow = storm = 0f; return; }
            rain /= sum; snow /= sum; storm /= sum;
        }

        /// <summary>With <paramref name="into"/> null: the list's total weight. Otherwise adds each choice's weight times <paramref name="share"/>.</summary>
        static float Total(List<WeatherProfile.NextWeather> list, WeatherCatalog catalog, SeasonProfile profile, WeatherSpell spell, float[] into, float share)
        {
            float total = 0f;
            if (list == null) return 0f;
            for (int k = 0; k < list.Count; k++)
            {
                WeatherProfile p = list[k].profile;
                if (p == null) continue;
                float scale = (profile != null ? profile.WeightOf(p) : 1f) * (spell != null ? spell.WeightOf(p) : 1f);
                float w = Math.Max(0f, list[k].weight * Math.Max(0f, scale));
                if (w <= 0f) continue;
                total += w;
                if (into == null) continue;
                int j = catalog.IndexOf(p);
                if (j >= 0) into[j] += w * share;
            }
            return total;
        }

        static ForecastLook LookOf(in ForecastDay d, WeatherSpell spell)
        {
            if (spell != null)
            {
                if (spell.storm && spell.snowy) return ForecastLook.Blizzard;
                if (spell.storm) return ForecastLook.Storm;
                if (spell.snowy) return ForecastLook.Snow;
                if (spell.wet) return ForecastLook.Rain;
                if (spell.dry) return d.HeatHaze ? ForecastLook.Heat : ForecastLook.Clear;
            }
            if (d.Storm >= 0.25f) return d.Snow > d.Rain ? ForecastLook.Blizzard : ForecastLook.Storm;
            if (d.Snow >= 0.35f) return ForecastLook.Snow;
            if (d.Rain >= 0.35f) return ForecastLook.Rain;
            if (d.HeatHaze) return ForecastLook.Heat;
            return d.Rain + d.Snow > 0.1f ? ForecastLook.Mixed : ForecastLook.Clear;
        }

        // ------------------------------------------------------------------ words

        /// <summary>A weather profile's name for players: "WP_PartlyCloudy" -> "Partly Cloudy".</summary>
        public static string WeatherName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return "";
            string n = assetName.StartsWith("WP_", StringComparison.Ordinal) ? assetName.Substring(3) : assetName;
            var b = new System.Text.StringBuilder(n.Length + 4);
            for (int i = 0; i < n.Length; i++)
            {
                char ch = n[i];
                if (ch == '_') { b.Append(' '); continue; }
                if (i > 0 && char.IsUpper(ch) && char.IsLower(n[i - 1])) b.Append(' ');
                b.Append(ch);
            }
            return b.ToString();
        }

        /// <summary>A real-time span for players: "now", "12 min", "3 h 20 min", "4 d 6 h".</summary>
        public static string Span(double seconds)
        {
            if (seconds < 60) return "now";
            long m = (long)Math.Floor(seconds / 60.0);
            if (m < 60) return $"{m} min";
            long h = m / 60;
            if (h < 48) return m % 60 == 0 ? $"{h} h" : $"{h} h {m % 60} min";
            long d = h / 24;
            return h % 24 == 0 ? $"{d} d" : $"{d} d {h % 24} h";
        }

        public static string EventName(CalendarEventKind kind, Season season = Season.Spring) => kind switch
        {
            CalendarEventKind.FullMoon => "Full Moon",
            CalendarEventKind.NewMoon => "New Moon",
            CalendarEventKind.Emberfall => "The Emberfall",
            CalendarEventKind.PaleWanderer => "The Pale Wanderer",
            CalendarEventKind.Eclipse => "Total eclipse",
            CalendarEventKind.Aurora => "Aurora night",
            CalendarEventKind.Season => SeasonRules.Name(season) + " begins",
            _ => kind.ToString(),
        };

        public static string LookName(ForecastLook look) => look switch
        {
            ForecastLook.Clear => "Clear",
            ForecastLook.Mixed => "Mixed",
            ForecastLook.Rain => "Rain",
            ForecastLook.Snow => "Snow",
            ForecastLook.Storm => "Storms",
            ForecastLook.Blizzard => "Blizzard",
            ForecastLook.Heat => "Heat haze",
            _ => look.ToString(),
        };
    }
}
