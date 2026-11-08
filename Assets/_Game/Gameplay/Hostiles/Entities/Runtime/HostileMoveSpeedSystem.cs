using KryticalNova.Brains;
using Unity.Burst;
using Unity.Entities;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// A Hostile's step speed from what its mind asked of the mover this tick, as Emerald moved NGO's Hostiles (HostileBrainLink.cs:102-103:
    /// the species' walk speed on patrol, its run speed in a chase or on the way home): Stop stands, Run runs, anything else walks.
    /// Until 8 Nov 2026 nothing set <see cref="BrainMover.Speed"/> in the game (only the tests did), so every brain stood where it spawned
    /// (probe-x6-12: Engage, Run, a goal 7 m away, speed 0.00). After every system that writes the mover, before the path service and the move.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileAttackSystem))]
    [BurstCompile]
    public partial struct HostileMoveSpeedSystem : ISystem
    {
        /// <summary>The speed for <paramref name="flags"/> (pure, tested).</summary>
        public static float SpeedFor(byte flags, float walk, float run) =>
            (flags & BrainMoverFlags.Stop) != 0 ? 0f : (flags & BrainMoverFlags.Run) != 0 ? run : walk;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var (mover, life) in SystemAPI.Query<RefRW<BrainMover>, RefRO<HostileLife>>().WithAll<BrainTag, BrainActive>())
                mover.ValueRW.Speed = SpeedFor(mover.ValueRO.Flags, life.ValueRO.Walk, life.ValueRO.Run);
        }
    }
}
