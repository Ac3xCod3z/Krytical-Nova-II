using System.Diagnostics;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// Perf round 4: the mean time of one piece of work over the last second, published as a <see cref="PerformanceLog"/> gauge once a
    /// second (the server CSV's column of that name). Start with <see cref="Stopwatch.GetTimestamp"/>, then <see cref="Add"/> when done.
    /// While no CSV is written (<see cref="PerformanceLog.GaugesOn"/> off) it is one test. Main thread only.
    /// </summary>
    public struct GaugeWindow
    {
        long m_WindowStart;
        int m_Count;
        double m_SumMs;

        public void Add(string gauge, long startedAt)
        {
            if (!PerformanceLog.GaugesOn) return;
            long end = Stopwatch.GetTimestamp();
            if (m_Count == 0) m_WindowStart = startedAt;
            m_Count++;
            m_SumMs += (end - startedAt) * 1000.0 / Stopwatch.Frequency;
            if (end - m_WindowStart < Stopwatch.Frequency) return;
            PerformanceLog.SetGauge(gauge, m_SumMs / m_Count);
            m_Count = 0;
            m_SumMs = 0;
        }
    }
}
