namespace KryticalNova.Crossfire
{
    /// <summary>What the Crossfire HUD shows at one moment: nothing, a small timer, or the big call banner on top of it.</summary>
    public struct CrossfireHudLine
    {
        public bool Show;
        /// <summary>The big banner is up (the call, repeated; every phase's first seconds).</summary>
        public bool Banner;
        public string Heading;
        public string Timer;
        /// <summary>How much of the phase is left, 0-1 (the bar).</summary>
        public float Left;
    }

    /// <summary>
    /// The words and the timers of the Crossfire HUD (build 17, Crossfire step 5b), as a pure rule so they are tested: the HUD only
    /// draws this. Shown only to a player the war calls (<see cref="CrossfireState.BracketOf"/> above 0). The server decides the war.
    /// </summary>
    public static class CrossfireHudText
    {
        /// <summary>m:ss, rounded up (a countdown never shows 0:00 before it ends).</summary>
        public static string Clock(double seconds)
        {
            int s = seconds <= 0 ? 0 : (int)System.Math.Ceiling(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <param name="bannerSeconds">How long the big banner stays up each time (CrossfireSettings.bannerSeconds).</param>
        public static CrossfireHudLine At(in CrossfireState s, double now, int level, float bannerSeconds)
        {
            var line = new CrossfireHudLine();
            if (!s.Known || s.Now.Phase == CrossfirePhase.Gap) return line;
            int bracket = s.BracketOf(level);
            if (bracket <= 0) return line;

            double sincePhase = now - s.Now.PhaseStart, left = s.Now.PhaseEnd - now, length = s.Now.PhaseEnd - s.Now.PhaseStart;
            line.Show = true;
            line.Left = length > 0 ? (float)System.Math.Max(0, System.Math.Min(1, left / length)) : 0f;
            line.Banner = sincePhase >= 0 && sincePhase < bannerSeconds;
            switch (s.Now.Phase)
            {
                case CrossfirePhase.Call:
                    line.Heading = "Crossfire at nightfall - Bracket " + CrossfireRules.Roman(bracket);
                    line.Timer = s.JoinOpen(now) ? "Muster Yards open - war in " + Clock(s.Now.WarStart - now)
                                                 : "War in " + Clock(s.Now.WarStart - now);
                    // the call repeats every CallRepeatSeconds
                    if (s.CallRepeatSeconds > 0 && sincePhase >= 0)
                        line.Banner = sincePhase % s.CallRepeatSeconds < bannerSeconds;
                    break;
                case CrossfirePhase.Muster:
                    line.Heading = "Crossfire - Bracket " + CrossfireRules.Roman(bracket);
                    line.Timer = "Gates open in " + Clock(left);
                    break;
                case CrossfirePhase.War:
                    line.Heading = "Crossfire! The gates are open";
                    line.Timer = Clock(left) + " left";
                    break;
                default: // End
                    line.Heading = "Crossfire over";
                    line.Timer = "Back in " + Clock(left);
                    line.Banner = true;
                    break;
            }
            return line;
        }
    }
}
