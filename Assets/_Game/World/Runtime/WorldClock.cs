using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Read-only gameplay access to the world clock (works on server, host, clients and offline).
    /// Example: <c>if (WorldClock.IsNight) spawnTable = nightSpawns;</c>
    /// </summary>
    public static class WorldClock
    {
        private const float DefaultTimeOfDay = 0.5f;

        public static bool IsAvailable => WorldEnvironmentNetwork.Instance != null;

        /// <summary>0 = midnight, 0.25 = 06:00, 0.5 = noon, 0.75 = 18:00.</summary>
        public static float TimeOfDay01 => WorldEnvironmentNetwork.Instance != null ? WorldEnvironmentNetwork.Instance.TimeOfDay01 : DefaultTimeOfDay;

        public static float Hours => TimeOfDay01 * 24f;

        /// <summary>
        /// The in-game calendar day: it turns over at in-game midnight, goes on from one session to the next, and
        /// is the same number on every machine. 0 with no clock.
        /// </summary>
        public static long Day
        {
            get
            {
                var env = WorldEnvironmentNetwork.Instance;
                return env != null ? env.State.CalendarDay(env.Now) : 0;
            }
        }

        /// <summary>
        /// Real seconds until the in-game calendar reaches <paramref name="day"/> (0 when it has). -1 when it is not
        /// known: no clock, or the clock is stopped.
        /// </summary>
        public static double SecondsUntilDay(long day)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return -1;
            var state = env.State;
            if (state.DayLengthSeconds <= 0f || state.TimeScale <= 0f) return -1;
            double days = day - Day - TimeOfDay01;
            return days <= 0 ? 0 : days * state.DayLengthSeconds / state.TimeScale;
        }

        /// <summary>True while the sun is below the horizon.</summary>
        public static bool IsNight => SunDirection.y < 0f;

        public static Vector3 SunDirection
        {
            get
            {
                var env = WorldEnvironmentNetwork.Instance;
                return env != null && env.DayCycle != null ? env.DayCycle.SunDirection(env.TimeOfDay01) : Vector3.up;
            }
        }

        public static Vector3 MoonDirection
        {
            get
            {
                var env = WorldEnvironmentNetwork.Instance;
                return env != null && env.DayCycle != null ? env.DayCycle.MoonDirection(env.TimeOfDay01) : Vector3.down;
            }
        }

        /// <summary>Current blended weather parameters.</summary>
        public static WeatherBlend Weather => WorldEnvironmentNetwork.Instance != null ? WorldEnvironmentNetwork.Instance.Weather : WeatherBlend.Clear();
    }
}
