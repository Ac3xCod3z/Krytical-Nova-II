using System.Text;
using KryticalNova.World;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console dev commands for the world clock and weather. They only work on the host,
    /// server or offline (authority), because they call the server-only API.
    /// TODO(GM): the GM mod should wrap the same WorldEnvironmentNetwork API with permission checks.
    /// </summary>
    public static class WorldEnvironmentCommands
    {
        private const string NoWorld = "No WorldEnvironmentNetwork in the scene.";
        private const string NoAuthority = "Only the host/server can change the world.";

        [Command("time.set", "Set the time of day in hours (0-24).")]
        private static string SetTime(float hours)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            return env.SetTimeOfDay(hours) ? $"Time set to {hours:0.00}h." : NoAuthority;
        }

        [Command("time.get", "Print the current time of day and weather.")]
        private static string GetTime()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            float h = env.TimeOfDay01 * 24f;
            string to = env.ToWeather != null ? env.ToWeather.name : "none";
            return $"{(int)h:00}:{(int)((h % 1f) * 60f):00}  weather {to} ({env.WeatherBlendT:P0})  coverage {env.Weather.coverage:0.00}  day {env.State.DayLengthSeconds:0}s x{env.State.TimeScale:0.##}";
        }

        [Command("time.daylength", "Set real seconds per in-game day (keeps the current time).")]
        private static string SetDayLength(float seconds)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            return env.SetDayLength(seconds) ? $"Day length {seconds:0}s." : NoAuthority + " (min 10s)";
        }

        [Command("time.scale", "Clock speed: 1 normal, 0 pause, >1 fast-forward.")]
        private static string SetTimeScale(float scale)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            return env.SetTimeScale(scale) ? $"Time scale {scale:0.##}." : NoAuthority;
        }

        [Command("weather.set", "Blend to a weather profile by name, e.g. weather.set Thunderstorm 20")]
        private static string SetWeather(string name, float transitionSeconds = 15f)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || env.Catalog == null) return NoWorld;
            int index = env.Catalog.IndexOf(name);
            if (index < 0) return $"Unknown weather '{name}'. Try weather.list.";
            return env.ForceWeather(index, transitionSeconds) ? $"Weather → {env.Catalog.Get(index).name} over {transitionSeconds:0}s." : NoAuthority;
        }

        [Command("weather.wind", "Print the current wind: heading (held for this weather cycle), speed and gust.")]
        private static string Wind()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            var w = env.Weather;
            float heading = UnityEngine.Mathf.Repeat(UnityEngine.Mathf.Atan2(w.wind.x, w.wind.y) * UnityEngine.Mathf.Rad2Deg, 360f);
            return $"Wind toward {heading:0}° at {w.wind.magnitude:0.0} m/s  ·  gustiness {w.gustiness:0.00}  gust now {w.gust:0.00}  " +
                   $"(cycle heading {env.State.ToWindHeading:0}°, previous {env.State.FromWindHeading:0}°)";
        }

        [Command("weather.lock", "Stop (true) or resume (false) automatic weather changes.")]
        private static string LockWeather(bool locked)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            return env.SetWeatherLocked(locked) ? (locked ? "Weather locked." : "Weather unlocked.") : NoAuthority;
        }

        [Command("weather.list", "List weather profiles.")]
        private static string ListWeather()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || env.Catalog == null) return NoWorld;
            var sb = new StringBuilder();
            for (int i = 0; i < env.Catalog.Count; i++)
            {
                var p = env.Catalog.Get(i);
                if (p != null) sb.Append(i).Append(": ").AppendLine(p.name);
            }
            return sb.ToString();
        }
    }
}
