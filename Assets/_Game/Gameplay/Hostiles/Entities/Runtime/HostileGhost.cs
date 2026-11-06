using KryticalNova.Brains;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// What every client sees of a Hostile (M5.5, plan s2.2; interpolated ghost). NGO parity (M5.0 H21): the lasting state NGO sent every
    /// 0.1 s as <c>HostileAnimState</c> (speed in 0.05 m/s steps, combat / stunned / dead bits, the death clip) and what its RPCs carried
    /// (the attack started and its index, a flinch), plus the species, level and tier the view is built from. The server writes it only
    /// when a value changes (<see cref="HostileGhostWriteSystem"/>), so an idle Hostile costs no snapshot bytes.
    /// </summary>
    public struct HostileGhost : IComponentData
    {
        [GhostField] public ushort Species;
        [GhostField] public short Level;
        [GhostField] public byte Tier;
        [GhostField] public byte Mode;
        /// <summary>NGO's <c>HostileAnimState.SpeedSteps</c>: ground speed in steps of 0.05 m/s.</summary>
        [GhostField] public byte SpeedSteps;
        /// <summary>The tick its current attack started (NGO's AttackClientRpc): a change starts the clip on the views.</summary>
        [GhostField] public uint AttackStartTick;
        [GhostField] public byte AttackIndex;
        [GhostField] public int AttackTargetGhostId;
        /// <summary>Counts the hits it took (NGO's HitClientRpc): a change plays a flinch.</summary>
        [GhostField] public byte HitCounter;
        /// <summary><see cref="HostileGhostFlags"/>.</summary>
        [GhostField] public byte Flags;
        /// <summary>Which death clip plays, counted from 1 (NGO's <c>HostileAnimState.DeathIndex</c>).</summary>
        [GhostField] public byte DeathIndex;
        [GhostField] public uint DeadTick;
        /// <summary>The player holding the kill (NGO's netTag, H10): filled by M5.6.</summary>
        [GhostField] public int TagHolderGhostId;
    }

    public static class HostileGhostFlags
    {
        /// <summary>The first three are NGO's <c>HostileAnimState</c> bits.</summary>
        public const byte InCombat = 1, Stunned = 2, Dead = 4, Asleep = 16, Heavy = 32;
    }

    /// <summary>Server only: what the ghost writer remembers between ticks.</summary>
    public struct HostileGhostSource : IComponentData
    {
        public float3 LastPosition;
        public byte HitCounter;
        public uint DeadTick;
    }

    public static class HostileGhostRules
    {
        public const float SpeedStep = 0.05f;

        /// <summary>A speed as NGO sends it: steps of 0.05 m/s, at most 255 steps (12.75 m/s).</summary>
        public static byte SpeedSteps(float metresPerSecond) => (byte)math.clamp((int)math.round(metresPerSecond / SpeedStep), 0, 255);

        /// <summary>The ghost a Hostile shows this tick (pure).</summary>
        public static HostileGhost Of(in HostileBrain brain, in HostileLife life, in HostileTarget target, float speed, byte hitCounter,
                                      uint deadTick, int tagHolder)
        {
            byte flags = 0;
            if (brain.Mode == HostileMode.Engage) flags |= HostileGhostFlags.InCombat;
            if (brain.Mode == HostileMode.Dead) flags |= HostileGhostFlags.Dead;
            bool attacking = brain.AttackStartTick != 0;
            return new HostileGhost
            {
                Species = life.Species,
                Level = life.Level,
                Tier = life.Tier,
                Mode = brain.Mode,
                SpeedSteps = brain.Mode == HostileMode.Dead ? (byte)0 : SpeedSteps(speed),
                AttackStartTick = brain.AttackStartTick,
                AttackIndex = attacking ? brain.Ability : (byte)0,
                AttackTargetGhostId = attacking ? target.GhostId : 0,
                HitCounter = hitCounter,
                Flags = flags,
                DeathIndex = brain.Mode == HostileMode.Dead ? (byte)1 : (byte)0,   // one death clip until the species bake gives more
                DeadTick = deadTick,
                TagHolderGhostId = tagHolder,
            };
        }

        public static bool Same(in HostileGhost a, in HostileGhost b) =>
            a.Species == b.Species && a.Level == b.Level && a.Tier == b.Tier && a.Mode == b.Mode && a.SpeedSteps == b.SpeedSteps
            && a.AttackStartTick == b.AttackStartTick && a.AttackIndex == b.AttackIndex && a.AttackTargetGhostId == b.AttackTargetGhostId
            && a.HitCounter == b.HitCounter && a.Flags == b.Flags && a.DeathIndex == b.DeathIndex && a.DeadTick == b.DeadTick
            && a.TagHolderGhostId == b.TagHolderGhostId;
    }

    /// <summary>
    /// Fills <see cref="HostileGhost"/> on the server, last in the brain tick (after the move): the speed from how far the body went this
    /// tick, a hit counted for every Damaged event of the tick, the dead tick on the way into Dead. Writes only when the ghost changes, so
    /// an unchanged Hostile's chunk keeps its change version and sends nothing.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderLast = true)]
    [UpdateAfter(typeof(BrainMoveSystem))]
    [BurstCompile]
    public partial struct HostileGhostWriteSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<BrainTick>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            BrainConfig c = SystemAPI.GetSingleton<BrainConfig>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            var ghostOf = SystemAPI.GetComponentLookup<HostileGhost>();
            // job later (perf phase): an IJobEntity with the same compare-then-write
            foreach (var (brain, life, target, transform, source, events, entity) in SystemAPI
                         .Query<RefRO<HostileBrain>, RefRO<HostileLife>, RefRO<HostileTarget>, RefRO<LocalTransform>, RefRW<HostileGhostSource>,
                             DynamicBuffer<BrainEvent>>()
                         .WithAll<HostileGhost>().WithEntityAccess())
            {
                ref HostileGhostSource s = ref source.ValueRW;
                float3 at = transform.ValueRO.Position;
                float speed = c.TickSeconds > 0f ? math.distance(at.xz, s.LastPosition.xz) / c.TickSeconds : 0f;
                s.LastPosition = at;
                for (int i = 0; i < events.Length; i++)
                    if (events[i].Tick == now && events[i].Kind == BrainEventKind.Damaged) s.HitCounter++;
                if (brain.ValueRO.Mode == HostileMode.Dead) { if (s.DeadTick == 0) s.DeadTick = now; }
                else s.DeadTick = 0;

                HostileGhost wanted = HostileGhostRules.Of(brain.ValueRO, life.ValueRO, target.ValueRO, speed, s.HitCounter, s.DeadTick,
                                                           ghostOf[entity].TagHolderGhostId);
                if (!HostileGhostRules.Same(ghostOf[entity], wanted)) ghostOf[entity] = wanted;
            }
        }
    }
}
