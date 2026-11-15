using System;

namespace KryticalNova.World
{
    /// <summary>
    /// Fills a <see cref="CalendarClock"/> from what this machine already has: the world clock (<see cref="WorldEnvironmentNetwork"/>), the
    /// season the server sent (<see cref="SeasonView"/>), the map (<see cref="ZoneRules"/>) and the settings. Read only; nothing is sent.
    /// </summary>
    public static class GameCalendarSource
    {
        /// <summary>The settings the calendar reads (null when the world has none loaded).</summary>
        public static SeasonSettings Seasons => SeasonSettings.Instance;
        public static SkyEventSettings Sky => SkyEventSettings.Default;
        public static WeatherCatalog Catalog => WorldEnvironmentNetwork.Instance != null ? WorldEnvironmentNetwork.Instance.Catalog : null;

        /// <summary>The calendar's clock now. False before the world clock exists.</summary>
        public static bool TryRead(out CalendarClock clock)
        {
            clock = default;
            WorldEnvironmentNetwork env = WorldEnvironmentNetwork.Instance;
            SeasonSettings s = Seasons;
            if (env == null || env.DayCycle == null || s == null) return false;
            WorldEnvironmentState st = env.State;
            double now = env.Now;
            float scale = st.TimeScale > 0f ? st.TimeScale : 1f;
            long calendarDay = st.CalendarDay(now);
            clock = new CalendarClock
            {
                UnixNow = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds,
                Day = calendarDay,
                DayBase = calendarDay - st.DayCount(now),
                Time01 = env.TimeOfDay01,
                DaySeconds = Math.Max(1.0, st.DayLengthSeconds / scale),
                SkySeed = st.Seed,
                LunarCycleDays = env.DayCycle.lunarCycleDays,
                WeatherSeed = SeasonRules.WeatherSeed(s.weatherSeed, SceneName()),
                AlwaysWinter = SeasonView.HasClient ? SeasonView.Client.AlwaysWinter : SeasonView.HasServer && SeasonView.Server.AlwaysWinter,
            };
            return true;
        }

        /// <summary>The map's scene name as the season server names it (its zone entry's, else the scene's own).</summary>
        static string SceneName()
        {
            ZoneRules z = ZoneRules.Current;
            if (z == null) return null;
            return z.Entry != null ? z.Entry.sceneName : z.gameObject.scene.name;
        }

        /// <summary>The air now (deg C) as the server sent it; false before the season arrives.</summary>
        public static bool TryTemperature(out float celsius)
        {
            celsius = 0f;
            if (SeasonView.HasClient) { celsius = SeasonView.Client.Temperature; return true; }
            if (SeasonView.HasServer) { celsius = SeasonView.Server.Temperature; return true; }
            return false;
        }

        /// <summary>The weather showing now, in players' words ("" before the world clock).</summary>
        public static string WeatherNow()
        {
            WorldEnvironmentNetwork env = WorldEnvironmentNetwork.Instance;
            if (env == null) return "";
            WeatherProfile p = env.WeatherBlendT >= 0.5f ? env.ToWeather : env.FromWeather;
            if (p == null) p = env.ToWeather;
            return p != null ? GameCalendar.WeatherName(p.name) : "";
        }
    }
}
