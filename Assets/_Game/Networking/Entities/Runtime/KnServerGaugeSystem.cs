using KryticalNova.Diagnostics;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Wave 4 soak (12 Nov 2026): once a second, while the server CSV wants gauges (<see cref="PerformanceLog.GaugesOn"/>), publishes the
    /// server world's players in game, entities and ghosts downward into Core, where Networking's <c>ServerPerformanceLog</c> reads them.
    /// Nothing reads up: Networking never references this assembly. Off = one test a frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class KnServerGaugeSystem : SystemBase
    {
        EntityQuery m_Players, m_Ghosts, m_All;
        double m_Next;

        protected override void OnCreate()
        {
            m_Players = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<GhostInstance>());
            m_All = EntityManager.UniversalQuery;
        }

        protected override void OnUpdate()
        {
            if (!PerformanceLog.GaugesOn) return;
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + 1.0;
            PerformanceLog.SetGauge(PerformanceLog.GaugeNames.Players, m_Players.CalculateEntityCount());
            PerformanceLog.SetGauge(PerformanceLog.GaugeNames.Ghosts, m_Ghosts.CalculateEntityCount());
            PerformanceLog.SetGauge(PerformanceLog.GaugeNames.Entities, m_All.CalculateEntityCount());
        }
    }
}
