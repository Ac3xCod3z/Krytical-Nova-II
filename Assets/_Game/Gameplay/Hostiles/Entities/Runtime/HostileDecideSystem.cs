using KryticalNova.Brains;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Each Hostile's mode this tick (M5.3b2b) by <see cref="HostileRules.NextMode"/>: seen -> Alert, noticed after its notice ticks (Q8)
    /// -> Engage; a hit -> Engage the same tick; the target dead or on neutral ground -> the next threat or home; Emerald's give-up (the
    /// target beyond the detection radius for 5 s, H14) -> home without a heal; past the leash -> home and heal there (the heal itself is
    /// the server body's, M5.6); home -> Idle. Sets where the Hostile wants to go (<see cref="BrainMover"/>); moving is M5.4's.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileSenseScanSystem))]
    [BurstCompile]
    public partial struct HostileDecideSystem : ISystem
    {
        const float HomeReach = 0.5f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HostileBrainConfig>();
            state.RequireForUpdate<SenseWorld>();
            state.RequireForUpdate<BrainTick>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            HostileBrainConfig c = SystemAPI.GetSingleton<HostileBrainConfig>();
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            // job later (perf phase): an IJobEntity (the sense world is read-only here)
            BufferLookup<BrainEvent> eventsOf = SystemAPI.GetBufferLookup<BrainEvent>(true);
            ComponentLookup<BrainDice> diceOf = SystemAPI.GetComponentLookup<BrainDice>();
            foreach (var (brain, target, mover, life, home, transform, threatBuffer, entity) in SystemAPI
                         .Query<RefRW<HostileBrain>, RefRW<HostileTarget>, RefRW<BrainMover>, RefRO<HostileLife>, RefRO<HostileHome>, RefRO<LocalTransform>,
                             DynamicBuffer<ThreatEntry>>()
                         .WithAll<BrainTag, BrainActive>().WithEntityAccess())
            {
                if (!eventsOf.TryGetBuffer(entity, out DynamicBuffer<BrainEvent> events)) continue;
                ref HostileBrain b = ref brain.ValueRW;
                ref HostileTarget aim = ref target.ValueRW;
                DynamicBuffer<ThreatEntry> threat = threatBuffer;
                float3 at = transform.ValueRO.Position;

                bool damaged = false, killed = false;
                for (int i = 0; i < events.Length; i++)
                {
                    if (events[i].Tick != now) continue;
                    if (events[i].Kind == BrainEventKind.Damaged && events[i].GhostId != 0) damaged = true;
                    if (events[i].Kind == BrainEventKind.Killed) killed = true;
                }

                bool targetValid = Valid(sense, aim.GhostId, out float3 feet);
                if (targetValid) aim.LastFeet = feet;
                // H14: Emerald's chase limit: the target beyond the detection radius for the give-up time
                bool beyondSight = targetValid && SenseQueries.FlatSq(feet, at) > life.ValueRO.AggroRadius * life.ValueRO.AggroRadius;
                if (b.Mode != HostileMode.Engage || !beyondSight) b.UnreachableSinceTick = 0;
                else if (b.UnreachableSinceTick == 0) b.UnreachableSinceTick = now;
                bool gaveUp = b.UnreachableSinceTick != 0 && now - b.UnreachableSinceTick >= c.GiveUpTicks;

                int other = BestOther(sense, threat, aim.GhostId);
                HostileRules.ModeResult next = HostileRules.NextMode(new HostileRules.ModeInputs
                {
                    Mode = b.Mode,
                    Behaviour = life.ValueRO.Behaviour,
                    SawPlayer = b.Saw != 0,
                    NoticeDone = b.Mode == HostileMode.Alert && now >= b.NoticeUntilTick,
                    Damaged = damaged,
                    TargetValid = targetValid,
                    HasOtherThreat = other >= 0,
                    BeyondLeash = home.ValueRO.ZoneRadius > 0f
                                  && HostileRules.BeyondLeash(at, home.ValueRO.ZoneCentre, home.ValueRO.ZoneRadius, home.ValueRO.LeashRadius),
                    GaveUp = gaveUp,
                    AtHome = SenseQueries.FlatSq(at, home.ValueRO.Home) <= HomeReach * HomeReach,
                    Dead = killed,
                });

                if (next.Mode != b.Mode)
                {
                    b.PhaseEndTick = 0;   // a new mode starts its own clock (patrol pause, engage phases)
                    if (next.Mode == HostileMode.Alert) b.NoticeUntilTick = now + life.ValueRO.NoticeTicks;
                    if (next.Mode == HostileMode.Return) b.HealOnHome = (byte)(next.HealOnHome ? 1 : 0);
                    if (b.Mode == HostileMode.Return && next.Mode == HostileMode.Idle)
                    {
                        if (b.HealOnHome != 0) threat.Clear();   // the leash return: threat cleared (the body heals, M5.6)
                        b.HealOnHome = 0;
                        aim = default;
                    }
                    b.Mode = next.Mode;
                    b.ModeSinceTick = now;
                    b.UnreachableSinceTick = 0;
                }
                // the target gone but another threat left: that one
                if (b.Mode == HostileMode.Engage && !targetValid && other >= 0)
                {
                    ThreatEntry t = threat[other];
                    sense.ByGhostId.TryGetValue(t.GhostId, out int p);
                    aim = new HostileTarget { Ghost = sense.Players[p].Ghost, GhostId = t.GhostId, LastFeet = sense.Players[p].Feet };
                }

                ref BrainMover m = ref mover.ValueRW;
                // patrol: idle for a pause, then a walk to a point within the wander radius, then idle again on arrival
                if (b.Mode == HostileMode.Idle || b.Mode == HostileMode.Patrol)
                {
                    if (b.Mode == HostileMode.Patrol && SenseQueries.FlatSq(at, m.Dest) <= HomeReach * HomeReach)
                    {
                        b.Mode = HostileMode.Idle;
                        b.ModeSinceTick = now;
                        b.PhaseEndTick = 0;
                    }
                    if (b.Mode == HostileMode.Idle && diceOf.HasComponent(entity))
                    {
                        RefRW<BrainDice> dice = diceOf.GetRefRW(entity);
                        if (b.PhaseEndTick == 0)
                            b.PhaseEndTick = now + dice.ValueRW.Rng.NextUInt(c.PatrolPauseMinTicks, c.PatrolPauseMaxTicks + 1);
                        else if (now >= b.PhaseEndTick && home.ValueRO.WanderRadius > 0f)
                        {
                            float2 offset = dice.ValueRW.Rng.NextFloat2Direction() * math.sqrt(dice.ValueRW.Rng.NextFloat()) * home.ValueRO.WanderRadius;
                            m.Dest = home.ValueRO.Home + new float3(offset.x, 0f, offset.y);
                            m.Flags = 0;   // a walk
                            b.Mode = HostileMode.Patrol;
                            b.ModeSinceTick = now;
                            b.PhaseEndTick = 0;
                            continue;
                        }
                    }
                }
                if (b.Mode == HostileMode.Engage && aim.GhostId != 0) { m.Dest = aim.LastFeet; m.Flags = BrainMoverFlags.Run; }
                else if (b.Mode == HostileMode.Return) { m.Dest = home.ValueRO.Home; m.Flags = BrainMoverFlags.Run; }
                else if (b.Mode == HostileMode.Idle || b.Mode == HostileMode.Alert || b.Mode == HostileMode.Dead) m.Flags = BrainMoverFlags.Stop;
            }
        }

        /// <summary>A target is valid while its player is sensed, alive and not on neutral ground (a hit from an ignored player still counts, H17).</summary>
        static bool Valid(in SenseWorld sense, int ghostId, out float3 feet)
        {
            feet = default;
            if (ghostId == 0 || !sense.ByGhostId.TryGetValue(ghostId, out int p)) return false;
            SensedPlayer s = sense.Players[p];
            feet = s.Feet;
            return (s.Flags & SensedFlags.Alive) != 0 && (s.Flags & (SensedFlags.Neutral | SensedFlags.Dead)) == 0;
        }

        static int BestOther(in SenseWorld sense, DynamicBuffer<ThreatEntry> threat, int current)
        {
            int best = -1;
            for (int i = 0; i < threat.Length; i++)
            {
                if (threat[i].GhostId == current || !Valid(sense, threat[i].GhostId, out _)) continue;
                if (best < 0 || threat[i].Threat > threat[best].Threat) best = i;
            }
            return best;
        }
    }
}
