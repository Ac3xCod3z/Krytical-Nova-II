using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

        [Command("gm.season", "Seasons: gm.season status | spring | summer | autumn | winter [blend seconds] (holds it) | auto [blend seconds] (back to the calendar) | climate auto | winter | temperate (this map, until restart) | progress <0-1> | progress auto (how far into the season it looks: autumn 0.2 turning, 0.9 almost bare) | temp <C> | temp auto (hold the air: 34 = a hot day, -8 = a hard frost) | spell <name> | spell auto (hold today's weather spell: storm, heatwave, wet...) | week (the next 7 days' spells).")]
        static string SeasonCommand(string what, string arg)
        {
            SeasonServerSystem clock = SeasonServerSystem.Find();
            if (clock == null) return NoServer;
            string w = (what ?? string.Empty).Trim().ToLowerInvariant();
            float blend = -1f;
            if (w == "progress")
            {
                string a = (arg ?? string.Empty).Trim().ToLowerInvariant();
                float p = -1f;
                if (a != "auto" && a != "calendar" && (!float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out p) || p < 0f || p > 1f))
                    return "gm.season progress <0-1> | auto, for example gm.season progress 0.9 (late in the season)";
                return clock.SetProgress(p) ? (p < 0f ? "Season progress: the calendar's. " : $"Season progress held at {p:0.00}. ") + Status(clock) : NoSettings;
            }
            if (w == "temp" || w == "temperature")
            {
                string a = (arg ?? string.Empty).Trim().ToLowerInvariant();
                float c = float.NaN;
                if (a != "auto" && (!float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out c) || c < -40f || c > 50f))
                    return "gm.season temp <-40..50 C> | auto, for example gm.season temp 34 (a hot afternoon) or gm.season temp -8";
                return clock.SetTemperature(c) ? (float.IsNaN(c) ? "Temperature: the weather's. " : $"Air held at {c:0.#} C ({KryticalNova.Core.TemperatureUnits.ToFahrenheit(c):0} F). ") + Status(clock) : NoSettings;
            }
            if (w == "spell")
            {
                string a = (arg ?? string.Empty).Trim();
                if (a.Length == 0 || a.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    return clock.SetSpell(-1) ? "Weather spell: the seed's. " + Status(clock) : NoSettings;
                SeasonProfile profile = ShowingProfile(clock);
                if (profile == null || profile.spells.Count == 0) return "This season has no weather spells.";
                int found = -1;
                for (int i = 0; i < profile.spells.Count; i++)
                    if (profile.spells[i] != null && profile.spells[i].name.StartsWith(a, StringComparison.OrdinalIgnoreCase)) { found = i; break; }
                if (found < 0) return "gm.season spell <" + SpellNames(profile) + "> | auto";
                return clock.SetSpell(found) ? $"Weather spell held: {profile.spells[found].name} (the weather rolls again now). " + Status(clock) : NoSettings;
            }
            if (w == "week") return clock.Step() ? Week(clock) : NoSettings;
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
            string ceiling = s.SnowRate > 0 && s.SnowCeiling < 1f ? $" toward {s.SnowCeiling:0.00}" : "";
            string progress = $"{s.ProgressAt(now) * 100f:0} % into it{(s.Progress >= 0f ? " (GM)" : "")}";
            WeatherSpell spell = clock.SpellNow;
            string air = $"; air {s.Temperature:0.#} C / {KryticalNova.Core.TemperatureUnits.ToFahrenheit(s.Temperature):0} F{(s.GmTemperature ? " (GM)" : "")}";
            string spellText = spell != null
                ? $"; spell {spell.name}{(s.GmSpell ? " (GM)" : $" day {clock.SpellDayNow.DayInSpell + 1} of {clock.SpellDayNow.Length}")}{(s.DryDays > 0 ? $", {s.DryDays} dry days" : "")}"
                : "";
            return $"Season: {s.DominantAt(now)}{hold}{blend}, {progress}; calendar {(Season)s.CalendarSeason} day {day} of {days}{air}{spellText}; snow {s.SnowAt(now):0.00} " +
                   $"({(s.SnowRate > 0 ? "piling up" + ceiling : s.SnowRate < 0 ? "thawing" : "holding")}); {light}{zone}{next}.";
        }

        static readonly List<(long day, WeatherSpell spell)> s_Week = new List<(long, WeatherSpell)>();

        /// <summary>The next seven days' spells, one line (the showcase's 'week of weather').</summary>
        public static string Week(SeasonServerSystem clock)
        {
            clock.Forecast(s_Week, 7);
            if (s_Week.Count == 0) return "This season has no weather spells.";
            var b = new StringBuilder("The coming week (one in-game day = one real hour): ");
            for (int i = 0; i < s_Week.Count; i++)
                b.Append(i == 0 ? "today " : $"+{i} ").Append(s_Week[i].spell != null ? s_Week[i].spell.name : "-").Append(i < s_Week.Count - 1 ? ", " : ".");
            return b.ToString();
        }

        static SeasonProfile ShowingProfile(SeasonServerSystem clock)
        {
            SeasonSettings settings = clock.Settings != null ? clock.Settings : SeasonSettings.Instance;
            if (settings == null || !clock.Running) return null;
            SeasonState s = clock.Current;
            return settings.Profile(s.AlwaysWinter ? Season.Winter : s.DominantAt(clock.Now));
        }

        static string SpellNames(SeasonProfile profile)
        {
            var b = new StringBuilder();
            for (int i = 0; i < profile.spells.Count; i++)
                if (profile.spells[i] != null) b.Append(b.Length > 0 ? " | " : "").Append(profile.spells[i].name.ToLowerInvariant());
            return b.ToString();
        }

        static string Hours(float day01)
        {
            int minutes = (int)Math.Round(day01 * 24f * 60f);
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }
}
