using System.Text;
using KryticalNova.World;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// GM commands for A Unique Sky (owner's NIGHT list item 24). Unlike the presentation-only sky.* overrides these run on the server
    /// (GM console relay, GM window, control page): the force travels with the world's state, so every client shows the same event at the
    /// same moment for <see cref="SkyEventSettings.forcedSeconds"/>. Night events (meteor, shower, comet, nebula, aurora) need a night sky:
    /// use time.midnight first.
    /// </summary>
    public static class SkyEventCommands
    {
        private const string NoWorld = "No WorldEnvironmentNetwork in the scene.";
        private const string NoAuthority = "Sky events are forced on the server (GM console, GM window or control page).";

        public static string Force(SkyEventKind kind)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            if (!env.ForceSkyEvent(kind)) return NoAuthority;
            if (kind == SkyEventKind.None) return "Sky event cleared: the sky follows its calendar again.";
            var s = SkyEventSettings.Default;
            float secs = SkyCalendar.ForcedLength(kind, s);
            bool night = kind == SkyEventKind.Meteor || kind == SkyEventKind.Shower || kind == SkyEventKind.Comet ||
                         kind == SkyEventKind.Nebula || kind == SkyEventKind.Aurora;
            bool isNight = env.DayCycle != null && env.DayCycle.SunDirection(env.TimeOfDay01).y < 0f;
            return $"{Name(kind)} forced for every player for {secs:0} s." + (night && !isNight ? " It is daytime: time.midnight to see it." : "");
        }

        public static string Name(SkyEventKind kind) => kind switch
        {
            SkyEventKind.Meteor => "Shooting stars",
            SkyEventKind.Shower => "The Emberfall (meteor shower)",
            SkyEventKind.Comet => "The Pale Wanderer (comet)",
            SkyEventKind.Nebula => "A nebula (tonight's pick of Soul Tide, Ember Rose, Veil Ribbon)",
            SkyEventKind.Sun2 => "The second sun",
            SkyEventKind.Eclipse => "A total eclipse",
            SkyEventKind.Aurora => "The aurora",
            SkyEventKind.Migration => "A sky migration",
            _ => "Nothing",
        };

        [Command("sky.meteor", "Shooting stars for every player (one every 4 s while the force lasts). Night only.")]
        private static string Meteor() => Force(SkyEventKind.Meteor);

        [Command("sky.shower", "The Emberfall meteor shower for every player. Night only.")]
        private static string Shower() => Force(SkyEventKind.Shower);

        [Command("sky.comet", "The Pale Wanderer comet for every player. Night only.")]
        private static string Comet() => Force(SkyEventKind.Comet);

        [Command("sky.nebula", "Tonight's nebula (Soul Tide, Ember Rose or Veil Ribbon, picked by the calendar) for every player. Night only.")]
        private static string Nebula() => Force(SkyEventKind.Nebula);

        [Command("sky.nebula.look", "THIS client only, for proof shots: show nebula look 0 Soul Tide, 1 Ember Rose, 2 Veil Ribbon; -1 = the calendar's pick.")]
        private static string NebulaLook(int look)
        {
            var s = SkyEventSettings.Default;
            int count = s != null && s.nebulas != null ? s.nebulas.Length : 0;
            if (look >= count) return $"Look {look} does not exist (0-{count - 1}, or -1).";
            SkyEventPresenter.NebulaLookOverride = look < 0 ? -1 : look;
            return look < 0 ? "Nebula back to the calendar's pick." : $"This client shows {s.nebulas[look].name} on nebula nights (others see the calendar's pick).";
        }

        [Command("sky.sun2", "Puts the second sun high in the sky for every player.")]
        private static string Sun2() => Force(SkyEventKind.Sun2);

        [Command("sky.eclipse", "A total eclipse for every player, starting now (needs the sun up: time.noon).")]
        private static string Eclipse() => Force(SkyEventKind.Eclipse);

        [Command("sky.aurora", "The aurora for every player, even outside winter. Night only.")]
        private static string Aurora() => Force(SkyEventKind.Aurora);

        [Command("sky.migration", "A sky migration crossing for every player (the flock layer is still to come; the calendar entry shows in sky.events).")]
        private static string Migration() => Force(SkyEventKind.Migration);

        [Command("sky.events.clear", "Ends a forced sky event; the sky follows its calendar again.")]
        private static string Clear() => Force(SkyEventKind.None);

        [Command("sky.events", "What the sky shows now and when the next natural events come (in game days).")]
        private static string Events()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return NoWorld;
            var s = SkyEventSettings.Default;
            if (s == null) return "No sky settings yet: Krytical Nova ▸ World ▸ Sky ▸ Bake Star Map.";
            var st = env.State;
            long day = st.CalendarDay(env.Now);
            SkyEventKind forced = env.ForcedSkyEvent(out double age);
            var clock = new SkyClock
            {
                Seed = st.Seed, Day = day, Time01 = env.TimeOfDay01, DayLengthSeconds = st.DayLengthSeconds,
                MonthDays = env.DayCycle != null ? env.DayCycle.lunarCycleDays : 8f, Forced = forced, ForcedAge = age,
            };
            var now = SkyCalendar.Evaluate(clock, s);
            var sb = new StringBuilder();
            sb.Append($"Day {day}  overhead tonight: {StarCharts.All[now.OverheadConstellation].Name}");
            if (forced != SkyEventKind.None && age <= s.forcedSeconds) sb.Append($"  forced: {Name(forced)} ({age:0} s)");
            string nebulaName = now.NebulaLook >= 0 && s.nebulas != null && now.NebulaLook < s.nebulas.Length ? s.nebulas[now.NebulaLook].name : "none";
            sb.Append($"\nNow: nebula {now.Nebula:0.00} ({nebulaName})  streaks {now.StreakCount}  comet {now.Comet:0.00}  sun2 {now.Sun2:0.00}  eclipse {now.Eclipse:0.00}  aurora {now.Aurora:0.00}");
            sb.Append($"\nNext: shower in {NextDay(day, d => SkyCalendar.Evaluate(At(clock, d, 0.9f), s).ShowerNight)} days");
            sb.Append($", comet in {NextDay(day, d => SkyCalendar.Evaluate(At(clock, d, 0.95f), s).Comet > 0f)}");
            sb.Append($", eclipse in {NextDay(day, d => SkyCalendar.Evaluate(At(clock, d, s.eclipsePeak01), s).EclipseDay)}");
            return sb.ToString();
        }

        static SkyClock At(SkyClock c, long day, float t) { c.Day = day; c.Time01 = t; c.Forced = SkyEventKind.None; c.ForcedAge = -1; return c; }

        static string NextDay(long from, System.Func<long, bool> test)
        {
            for (long d = from; d < from + 6000; d++) if (test(d)) return (d - from).ToString();
            return "6000+";
        }
    }
}
