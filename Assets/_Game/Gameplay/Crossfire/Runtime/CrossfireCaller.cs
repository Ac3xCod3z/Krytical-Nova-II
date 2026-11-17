namespace KryticalNova.Crossfire
{
    /// <summary>
    /// When the server announces Crossfire (owner, 13 Nov 2026, answers 3.4): through the game's one announcement system (the top-of-screen
    /// news that GM broadcasts and shutdowns use), never a HUD of its own.
    /// <list type="bullet">
    /// <item>The call is one pop-up per whole minute left before the gates open ("War starts in N minutes"): at the Call's start and on
    /// every minute mark after it (<c>CrossfireSettings.callRepeatSeconds</c>). It never names seconds, and N is exact: the whole minutes
    /// still left, never rounded up (owner, 13 Nov 2026: a call with 2:15 to go said "3 minutes"). A call that starts between two marks
    /// names the minutes it still has in full, and the next pop-up comes on the next mark.</item>
    /// <item>Only the last <c>finalCountdownSeconds</c> (10) count down, one line a second: 10 .. 1 (<paramref name="countdown"/> says a
    /// line is one of them, so the news bar shows it standing in place). Then the gates open and the calls stop.</item>
    /// <item>When the gates close after a war: one gates-closed line.</item>
    /// </list>
    /// A pure rule, kept by the server's clock system; the words are in <see cref="CrossfireSettings"/>.
    /// </summary>
    public struct CrossfireCaller
    {
        /// <summary>What the caller says now: nothing, the call (with whole minutes left), a countdown number, or the gates-closed line.</summary>
        public enum LineKind : byte { None = 0, Call = 1, Count = 2, Closed = 3 }

        /// <summary>One moment to announce, the same for the whole server; each player reads it for their own bracket (<see cref="Text"/>).</summary>
        public struct Line
        {
            public LineKind Kind;
            /// <summary>The call: seconds the minutes are named from; the countdown: its number.</summary>
            public double Seconds;
            public bool IsCountdown => Kind == LineKind.Count;
        }

        /// <summary>A Call seen this long after its start still counts as starting on its minute mark (the server looks a few times a second).</summary>
        public const double StartSlackSeconds = 1.0;

        double m_CalledFor, m_OpenedFor, m_ClosedFor;
        int m_LastMark, m_LastCount;
        bool m_Started;

        /// <summary>
        /// The line every player is told now (one per server, so each player's minutes and numbers are the same), or None. The words are
        /// <see cref="Text"/>: each player's own bracket (owner, 16 Nov 2026).
        /// </summary>
        public Line NextLine(in CrossfireState s, double now, double gatesOpenAt, CrossfireSettings settings)
        {
            if (!m_Started) { m_Started = true; m_CalledFor = m_OpenedFor = m_ClosedFor = double.NaN; }
            if (!s.Known || settings == null) return default;
            double war = s.Now.WarStart;
            switch (s.Now.Phase)
            {
                case CrossfirePhase.Call:
                case CrossfirePhase.Muster:
                    if (!m_CalledFor.Equals(war)) { m_CalledFor = war; m_LastMark = int.MinValue; m_LastCount = int.MinValue; }
                    double left = gatesOpenAt - now;
                    if (left <= 0) return default;
                    float lastSeconds = System.Math.Max(0f, settings.finalCountdownSeconds);
                    if (left <= lastSeconds)
                    {
                        // the last seconds: 10 .. 1, one line each
                        int n = System.Math.Max(1, (int)System.Math.Ceiling(left - 1e-6));
                        if (n == m_LastCount) return default;
                        m_LastCount = n;
                        return new Line { Kind = LineKind.Count, Seconds = n };
                    }
                    // one pop-up per interval mark: "War starts in N minutes", said as the mark is reached, so N is exact (never seconds).
                    double interval = System.Math.Max(1f, settings.callRepeatSeconds);
                    int mark = (int)System.Math.Ceiling(left / interval - 1e-6);
                    if (m_LastMark == int.MinValue)
                    {
                        // The Call's first pop-up. A call that starts between two marks (2:15 to go) names the whole minutes it still
                        // has (2), never the next one up (3), and the pop-up of that mark itself is then not said twice.
                        bool onMark = mark * interval - left <= StartSlackSeconds;
                        if (!onMark) mark = System.Math.Max(1, (int)System.Math.Floor(left / interval + 1e-6));
                    }
                    else if (mark >= m_LastMark) return default;
                    m_LastMark = mark;
                    return new Line { Kind = LineKind.Call, Seconds = mark * interval };
                case CrossfirePhase.War:
                    m_OpenedFor = war;
                    return default;
                default:   // End or Gap: the gates are shut again
                    if (double.IsNaN(m_OpenedFor) || m_ClosedFor.Equals(m_OpenedFor)) return default;
                    m_ClosedFor = m_OpenedFor;
                    return new Line { Kind = LineKind.Closed };
            }
        }

        /// <summary>
        /// What a player of <paramref name="bracket"/> reads for <paramref name="line"/>, or null when they are not told it (owner,
        /// 16 Nov 2026): below the war's level nothing; the call goes to every bracket, each its own (and a bracket with no zone yet is
        /// told so); the countdown and the gates-closed line go only to brackets with a zone, the only ones whose gates open.
        /// </summary>
        public static string Text(in Line line, CrossfireSettings settings, int bracket, in CrossfireState s)
        {
            if (settings == null || bracket <= 0 || line.Kind == LineKind.None) return null;
            bool zone = settings.HasZone(bracket);
            switch (line.Kind)
            {
                case LineKind.Call:
                    string where = zone ? settings.whereText : settings.noZoneText;
                    string call = (settings.callText ?? string.Empty).Replace("{where}", where ?? string.Empty);
                    return Fill(Fill(call, settings, bracket), line.Seconds, s).Trim();   // the bracket's {level} first, then the time
                case LineKind.Count: return zone ? Fill(settings.countdownText, line.Seconds, s) : null;
                default: return zone ? Fill(settings.gatesClosedText, 0, s) : null;
            }
        }

        /// <summary>{time}: whole minutes until the gates open, in words; {n}: the countdown number; {level}: the lowest level called.</summary>
        public static string Fill(string text, double seconds, in CrossfireState s) =>
            (text ?? string.Empty).Replace("{time}", InWords(seconds)).Replace("{n}", ((int)System.Math.Ceiling(seconds)).ToString())
                .Replace("{level}", s.MinLevel.ToString());

        /// <summary>A bracket's words: {bracket} its numeral, {levels} its levels, {level} its lowest level.</summary>
        public static string Fill(string text, CrossfireSettings settings, int bracket)
        {
            string levels = settings.LevelsOf(bracket);
            int low = settings.minLevel + (bracket - 1) * settings.bracketSize;
            return (text ?? string.Empty).Replace("{bracket}", CrossfireRules.Roman(bracket)).Replace("{levels}", levels).Replace("{level}", low.ToString());
        }

        /// <summary>"10 minutes", "1 minute": the whole minutes in it (never rounded up, never seconds; at least 1).</summary>
        public static string InWords(double seconds)
        {
            int min = System.Math.Max(1, (int)System.Math.Floor(seconds / 60.0 + 1e-6));
            return min + (min == 1 ? " minute" : " minutes");
        }
    }
}
