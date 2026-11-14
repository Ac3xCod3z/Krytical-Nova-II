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
        /// <summary>A Call seen this long after its start still counts as starting on its minute mark (the server looks a few times a second).</summary>
        public const double StartSlackSeconds = 1.0;

        double m_CalledFor, m_OpenedFor, m_ClosedFor;
        int m_LastMark, m_LastCount;
        bool m_Started;

        /// <summary>The line to announce now, or null. <paramref name="gatesOpenAt"/> is when this war's gates open (Muster's end).</summary>
        public string Next(in CrossfireState s, double now, double gatesOpenAt, CrossfireSettings settings) =>
            Next(s, now, gatesOpenAt, settings, out _);

        /// <summary>As above; <paramref name="countdown"/> is true when the line is one of the last seconds (10 .. 1).</summary>
        public string Next(in CrossfireState s, double now, double gatesOpenAt, CrossfireSettings settings, out bool countdown)
        {
            countdown = false;
            if (!m_Started) { m_Started = true; m_CalledFor = m_OpenedFor = m_ClosedFor = double.NaN; }
            if (!s.Known || settings == null) return null;
            double war = s.Now.WarStart;
            switch (s.Now.Phase)
            {
                case CrossfirePhase.Call:
                case CrossfirePhase.Muster:
                    if (!m_CalledFor.Equals(war)) { m_CalledFor = war; m_LastMark = int.MinValue; m_LastCount = int.MinValue; }
                    double left = gatesOpenAt - now;
                    if (left <= 0) return null;
                    float lastSeconds = System.Math.Max(0f, settings.finalCountdownSeconds);
                    if (left <= lastSeconds)
                    {
                        // the last seconds: 10 .. 1, one line each
                        int n = System.Math.Max(1, (int)System.Math.Ceiling(left - 1e-6));
                        if (n == m_LastCount) return null;
                        m_LastCount = n;
                        countdown = true;
                        return Fill(settings.countdownText, n, s);
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
                    else if (mark >= m_LastMark) return null;
                    m_LastMark = mark;
                    return Fill(settings.callText, mark * interval, s);
                case CrossfirePhase.War:
                    m_OpenedFor = war;
                    return null;
                default:   // End or Gap: the gates are shut again
                    if (double.IsNaN(m_OpenedFor) || m_ClosedFor.Equals(m_OpenedFor)) return null;
                    m_ClosedFor = m_OpenedFor;
                    return Fill(settings.gatesClosedText, 0, s);
            }
        }

        /// <summary>{time}: whole minutes until the gates open, in words; {n}: the countdown number; {level}: the lowest level called.</summary>
        public static string Fill(string text, double seconds, in CrossfireState s) =>
            (text ?? string.Empty).Replace("{time}", InWords(seconds)).Replace("{n}", ((int)System.Math.Ceiling(seconds)).ToString())
                .Replace("{level}", s.MinLevel.ToString());

        /// <summary>"10 minutes", "1 minute": the whole minutes in it (never rounded up, never seconds; at least 1).</summary>
        public static string InWords(double seconds)
        {
            int min = System.Math.Max(1, (int)System.Math.Floor(seconds / 60.0 + 1e-6));
            return min + (min == 1 ? " minute" : " minutes");
        }
    }
}
