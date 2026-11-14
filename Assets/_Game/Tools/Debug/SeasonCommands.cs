using System;
using System.Globalization;
using KryticalNova.World;
using KryticalNova.World.Entities;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// GM commands for the seasons (owner's DAY list item 21): hold a season, go back to the calendar, set the snow cover, make this map
    /// always winter, and see where the calendar stands. They run where the server's season clock runs (the server, the host, the Editor);
    /// the GM window and the control page send the same lines. A hold lives in the server's memory only: a restart goes back to the calendar.
    /// </summary>
    public static class SeasonCommands
    {
        const string NoServer = "No season clock here: run this on the server (or the host / the Editor's Play).";
        const string NoSettings = "The seasons have no settings (World/Resources/SeasonSettings is missing).";

        [Command("gm.season", "Seasons: gm.season status | spring | summer | autumn | winter [blend seconds] (holds it) | auto [blend seconds] (back to the calendar) | climate auto | winter | temperate (this map, until restart).")]
        static string SeasonCommand(string what, string arg)
        {
            SeasonServerSystem clock = SeasonServerSystem.Find();
            if (clock == null) return NoServer;
            string w = (what ?? string.Empty).Trim().ToLowerInvariant();
            float blend = -1f;
            if (w != "climate" && !string.IsNullOrWhiteSpace(arg) && !float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out blend))
                return "The blend is in seconds, for example: gm.season winter 20";
            switch (w)
            {
                case "status": case "": return clock.Step() ? Status(clock) : NoSettings;
                case "auto": case "calendar": return clock.Auto(blend) ? "Back to the calendar. " + Status(clock) : NoSettings;
                case "climate":
                    string c = (arg ?? string.Empty).Trim().ToLowerInvariant();
                    SeasonClimate climate = c == "winter" || c == "always" ? SeasonClimate.AlwaysWinter
                        : c == "temperate" || c == "normal" ? SeasonClimate.Temperate : SeasonClimate.Auto;
                    if (c != "" && c != "auto" && climate == SeasonClimate.Auto) return "gm.season climate auto | winter | temperate";
                    return clock.SetClimate(climate) ? $"This map's climate: {climate}. " + Status(clock) : NoSettings;
            }
            if (!SeasonRules.TryParse(w, out Season season)) return "gm.season status | spring | summer | autumn | winter [blend s] | auto [blend s] | climate auto|winter|temperate";
            return clock.Hold(season, blend) ? $"{season} held (gm.season auto goes back to the calendar). " + Status(clock) : NoSettings;
        }

        [Command("gm.season")]
        static string SeasonCommand(string what) => SeasonCommand(what, null);

        [Command("gm.season")]
        static string SeasonCommand() => SeasonCommand("status", null);

        [Command("gm.snow", "Snow cover now, 0-1 (gm.snow 1 = white at once); it builds or melts from there with the weather.")]
        static string Snow(string value)
        {
            SeasonServerSystem clock = SeasonServerSystem.Find();
            if (clock == null) return NoServer;
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float cover) || cover < 0f || cover > 1f)
                return "gm.snow <0-1>, for example gm.snow 0.5";
            return clock.SetSnow(cover) ? $"Snow cover set to {cover:0.00}. " + Status(clock) : NoSettings;
        }

        /// <summary>Where the seasons stand, in one line: what shows, the calendar's season and day, the snow, the daylight and the next turn.</summary>
        public static string Status(SeasonServerSystem clock)
        {
            SeasonState s = clock.Current;
            if (!clock.Running) return "Seasons: not running (no settings).";
            SeasonSettings settings = clock.Settings != null ? clock.Settings : SeasonSettings.Instance;
            double now = clock.Now;
            int day = (int)Math.Floor((now - s.SeasonStart) / 86400.0) + 1;
            int days = (int)Math.Round(s.SeasonSeconds / 86400.0);
            string blend = s.From == s.To || s.BlendAt(now) >= 1f ? "" : $", blending {s.From} -> {s.To} {s.BlendAt(now) * 100f:0} %";
            string hold = s.GmHold ? " (GM hold)" : "";
            string zone = s.AlwaysWinter ? "; always-winter map" : "";
            float share = settings != null ? clock.DaylightShare(settings, now) : 0.5f;
            string light = $"sun up {Hours(SeasonRules.Sunrise(share))}-{Hours(1f - SeasonRules.Sunrise(share))}";
            string next = "";
            if (settings != null)
            {
                DateTime turn = DateTime.UnixEpoch.AddSeconds(SeasonRules.NextTurnUnix(settings.Calendar, clock.UnixOf(now)));
                next = $"; next season {turn:ddd d MMM HH:mm} UTC ({turn.ToLocalTime():ddd HH:mm} server time)";
            }
            return $"Season: {s.DominantAt(now)}{hold}{blend}; calendar {(Season)s.CalendarSeason} day {day} of {days}; snow {s.SnowAt(now):0.00} " +
                   $"({(s.SnowRate > 0 ? "piling up" : s.SnowRate < 0 ? "melting" : "steady")}); {light}{zone}{next}.";
        }

        static string Hours(float day01)
        {
            int minutes = (int)Math.Round(day01 * 24f * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }
}
