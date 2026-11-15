using KryticalNova.Brains;
using KryticalNova.Hostiles.Entities;
using Unity.Burst;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// Server (day list item 19, phase 4): every Mount Companion that is out and acts in the TANK stance joins the Hostiles' sense world after
    /// the players (<see cref="HostilePlayerFeedSystem"/>, <see cref="HostileSenseFlagsServerSystem"/>) and before the grids are built, so a
    /// Hostile whose threat its taunt lifted can aim at it, chase it and swing at it like at a player. It is marked
    /// <see cref="SensedFlags.Ignored"/>: no Hostile ever picks it by sight, only a taunt turns one on it. A companion in any other stance, going
    /// home or knocked out is not in the sense world, so DPS and HEALER are never targets and a Hostile it held falls back to its next threat.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileSenseFlagsServerSystem))]
    [UpdateBefore(typeof(SenseBuildSystem))]
    [BurstCompile]
    public partial struct CompanionSenseFeedSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SenseWorld>();
            state.RequireForUpdate<CompanionView>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            foreach (var (view, place, ghost, entity) in SystemAPI.Query<RefRO<CompanionView>, RefRO<LocalTransform>, RefRO<GhostInstance>>().WithEntityAccess())
            {
                if (!Sensed(view.ValueRO, ghost.ValueRO.ghostId)) continue;
                sense.Players.Add(new SensedPlayer
                {
                    Ghost = entity,
                    GhostId = ghost.ValueRO.ghostId,
                    Feet = place.ValueRO.Position,
                    Flags = SensedFlags.Alive | SensedFlags.Ignored,
                });
            }
        }

        /// <summary>A companion the Hostiles sense: out, acting as TANK, with a ghost id already.</summary>
        public static bool Sensed(in CompanionView view, int ghostId) =>
            ghostId != 0 && view.State == (byte)CompanionState.Out && view.ActingStance == (byte)CompanionStance.Tank;
    }
}
