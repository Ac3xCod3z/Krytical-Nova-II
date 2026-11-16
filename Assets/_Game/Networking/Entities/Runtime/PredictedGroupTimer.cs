using System.Diagnostics;
using KryticalNova.Diagnostics;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Perf round 4: the server's PredictedSimulationSystemGroup (every player's walk, jump, separation, melee state...) timed on the
    /// main thread from its first system to its last, jobs it waits for included: the server CSV's <c>predicted_ms</c> (mean per tick).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup), OrderFirst = true)]
    public partial class PredictedGroupTimerStart : SystemBase
    {
        internal static long StartedAt;
        protected override void OnUpdate() => StartedAt = PerformanceLog.GaugesOn ? Stopwatch.GetTimestamp() : 0;
    }

    /// <summary>The end of <see cref="PredictedGroupTimerStart"/>'s window.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup), OrderLast = true)]
    public partial class PredictedGroupTimerEnd : SystemBase
    {
        GaugeWindow m_Window;
        protected override void OnUpdate()
        {
            if (PredictedGroupTimerStart.StartedAt == 0) return;
            m_Window.Add(PerformanceLog.GaugeNames.PredictedMs, PredictedGroupTimerStart.StartedAt);
        }
    }
}
