using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Server, while relevancy is on (perf round 3, R3-3b; Master: "keep teleports showing players at once"): a teleport about to be
    /// applied this tick nudges the relevancy fill, so the players and monsters around where the body lands are sent in this same
    /// tick instead of at the next timed fill (up to 0.2 s later), and the ones it left are taken away. Here, above both, because the
    /// player's teleport (Player.Entities) and the fill (Networking.Entities) do not know each other.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateBefore(typeof(PlayerTeleportSystem))]
    public partial struct RelevancyTeleportNudgeSystem : ISystem
    {
        EntityQuery m_Teleporting;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RelevancyConfig>();
            m_Teleporting = SystemAPI.QueryBuilder().WithAll<PlayerGhostTag, TeleportRequest, Simulate>().Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (m_Teleporting.IsEmpty) return;   // an enableable request: only the ones switched on this tick count
            SystemAPI.GetSingletonRW<RelevancyConfig>().ValueRW.Nudge++;
        }
    }
}
