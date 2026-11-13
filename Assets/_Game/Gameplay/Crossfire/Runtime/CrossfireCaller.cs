namespace KryticalNova.Crossfire
{
    /// <summary>
    /// When the server announces Crossfire (owner, 13 Nov 2026, answers 3.4): through the game's one announcement system (the top-of-screen
    /// news that GM broadcasts and shutdowns use), never a HUD of its own. The call says when the war starts, is repeated every
    /// <c>CrossfireSettings.callRepeatSeconds</c> through the Call and the Muster, and stops when the gates open; when the gates close
    /// after a war, one gates-closed line. A pure rule, kept by the server's clock system; the words are in <see cref="CrossfireSettings"/>.
    /// </summary>
    public struct CrossfireCaller
    {
        double m_LastCallAt, m_CalledFor, m_OpenedFor, m_ClosedFor;
        bool m_Started;

        /// <summary>The line to announce now, or null. <paramref name="gatesOpenAt"/> is when this war's gates open (Muster's end).</summary>
        public string Next(in CrossfireState s, double now, double gatesOpenAt, CrossfireSettings settings)
        {
            if (!m_Started) { m_Started = true; m_CalledFor = m_OpenedFor = m_ClosedFor = double.NaN; m_LastCallAt = double.NegativeInfinity; }
            if (!s.Known || settings == null) return null;
            double war = s.Now.WarStart;
            switch (s.Now.Phase)
            {
                case CrossfirePhase.Call:
                case CrossfirePhase.Muster:
                    bool first = !m_CalledFor.Equals(war);
                    if (!first && now - m_LastCallAt < System.Math.Max(1f, settings.callRepeatSeconds)) return null;
                    m_CalledFor = war;
                    m_LastCallAt = now;
                    return Fill(settings.callText, gatesOpenAt - now, s);
                case CrossfirePhase.War:
                    m_OpenedFor = war;
                    return null;
                default:   // End or Gap: the gates are shut again
                    if (double.IsNaN(m_OpenedFor) || m_ClosedFor.Equals(m_OpenedFor)) return null;
                    m_ClosedFor = m_OpenedFor;
                    return Fill(settings.gatesClosedText, 0, s);
            }
        }

        /// <summary>{time}: how long until the gates open, in words; {level}: the lowest level called.</summary>
        public static string Fill(string text, double seconds, in CrossfireState s) =>
            (text ?? string.Empty).Replace("{time}", InWords(seconds)).Replace("{level}", s.MinLevel.ToString());

        /// <summary>"10 minutes", "1 minute", "40 seconds" (minutes rounded up from a minute on).</summary>
        public static string InWords(double seconds)
        {
            if (seconds < 60) { int sec = System.Math.Max(1, (int)System.Math.Ceiling(seconds)); return sec + (sec == 1 ? " second" : " seconds"); }
            int min = (int)System.Math.Ceiling(seconds / 60.0 - 1e-6);
            return min + (min == 1 ? " minute" : " minutes");
        }
    }
}
