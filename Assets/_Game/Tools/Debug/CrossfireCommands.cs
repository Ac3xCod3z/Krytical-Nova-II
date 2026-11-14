using KryticalNova.Crossfire;
using KryticalNova.Crossfire.Entities;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// GM commands for Crossfire, the hourly war (owner's DAY list item 17): start it now, skip to the next phase, end it, and see where the
    /// clock stands. They work where the server's clock runs (the server, the host, the Editor); the GM window and the control page send the
    /// same lines.
    /// </summary>
    public static class CrossfireCommands
    {
        const string NoServer = "No Crossfire clock here: run this on the server (or the host / the Editor's Play).";

        [Command("gm.crossfire", "Crossfire, the hourly war: gm.crossfire status | start (the rally now) | skip (the next phase now) | end (the war ends now; a war only called is called off).")]
        static string Crossfire(string what)
        {
            CrossfireClockServerSystem clock = CrossfireClockServerSystem.Find();
            if (clock == null) return NoServer;
            switch ((what ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "start": return clock.Run(CrossfireCommand.StartNow) ? "Crossfire starts now: the rally, gates shut. " + Status(clock) : NoSettings;
                case "skip": return clock.Run(CrossfireCommand.Skip) ? "Skipped. " + Status(clock) : NoSettings;
                case "end": return clock.Run(CrossfireCommand.End) ? "Ended. " + Status(clock) : NoSettings;
                case "status": case "": return Status(clock);
                default: return "gm.crossfire status | start | skip | end";
            }
        }

        [Command("gm.crossfire")]
        static string Crossfire() => Crossfire("status");

        const string NoSettings = "Crossfire has no settings (Resources/CrossfireSettings is missing).";

        /// <summary>Where the clock stands, in one line: the phase, the time left in it and the next war.</summary>
        public static string Status(CrossfireClockServerSystem clock)
        {
            CrossfireState s = clock.Current;
            if (!s.Known) return "Crossfire: not running (no settings).";
            double now = clock.Now;
            string left = double.IsInfinity(s.Now.PhaseEnd) ? "" : $", {Clock(s.Now.PhaseEnd - now)} left";
            string next = s.Now.Phase == CrossfirePhase.Gap ? $"; next war in {Clock(s.Now.WarStart - now)}" : "";
            string cycle = clock.UsingFixedCycle ? " (fixed cycle: no world clock or the nightfall anchor is off)" : " (at nightfall)";
            return $"Crossfire: {CrossfireSettings.PhaseName(s.Now.Phase)}{left}{next}{cycle}.";
        }

        static string Clock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = (int)System.Math.Ceiling(seconds);
            return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
        }
    }
}
