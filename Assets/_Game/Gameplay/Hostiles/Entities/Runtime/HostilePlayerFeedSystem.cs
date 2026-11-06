using KryticalNova.Brains;
using KryticalNova.Player.Entities;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// The Hostiles mind's feeder (M5.3b2a): every player ghost of the server world into <see cref="SenseWorld.Players"/> before the Brain
    /// builds its grids: its ghost id, its feet, alive or dead (no Max HP reported yet counts as alive, as the melee rules do), on a wall.
    /// Neutral ground and the ignore list are the game's managed rules: M5.3b2 adds them.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(BrainTickSystem))]
    [UpdateBefore(typeof(SenseBuildSystem))]
    [BurstCompile]
    public partial struct HostilePlayerFeedSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HostileBrainConfig>();
            state.RequireForUpdate<SenseWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            sense.Players.Clear();
            // job later (perf phase): a parallel writer per chunk
            foreach (var (ghost, pose, vitals, move, entity) in SystemAPI
                         .Query<RefRO<GhostInstance>, RefRO<PlayerPose>, RefRO<PlayerVitals>, RefRO<MoveState>>()
                         .WithAll<PlayerGhostTag>().WithEntityAccess())
            {
                bool alive = vitals.ValueRO.MaxHealthMilli == 0 || vitals.ValueRO.HealthMilli > 0;
                byte flags = alive ? SensedFlags.Alive : SensedFlags.Dead;
                if (move.ValueRO.Wall != 0) flags |= SensedFlags.OnWall;
                sense.Players.Add(new SensedPlayer
                {
                    Ghost = entity,
                    GhostId = ghost.ValueRO.ghostId,
                    Feet = (float3)pose.ValueRO.Mm * 0.001f,
                    Flags = flags,
                });
            }
        }
    }
}
