using KryticalNova.Brains;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Threat (M5.3b1, Q5): this tick's events on each Hostile become threat by player (a hit adds its damage, a heal half of it, a taunt
    /// lifts the taunter above the top), old threat decays, at most six players are kept; then the target: the top threat when it has
    /// none, and a challenger takes it only above 110 % of the target's threat in reach, 130 % out of reach.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(ThinkClockSystem))]
    [BurstCompile]
    public partial struct HostileThreatSystem : ISystem
    {
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
            foreach (var (threatBuffer, events, target, life, transform) in SystemAPI
                         .Query<DynamicBuffer<ThreatEntry>, DynamicBuffer<BrainEvent>, RefRW<HostileTarget>, RefRO<HostileLife>, RefRO<LocalTransform>>()
                         .WithAll<BrainTag, BrainActive>())
            {
                DynamicBuffer<ThreatEntry> threat = threatBuffer;   // a copy of the handle (the iteration variable cannot be written through)
                for (int i = 0; i < events.Length; i++)
                {
                    BrainEvent e = events[i];
                    if (e.Tick != now || e.GhostId == 0) continue;
                    if (e.Kind == BrainEventKind.Damaged) Add(threat, e.GhostId, math.max(0f, e.Amount), now);
                    else if (e.Kind == BrainEventKind.Healed) Add(threat, e.GhostId, math.max(0f, e.Amount) * c.HealThreatFactor, now);
                    else if (e.Kind == BrainEventKind.Taunt) Add(threat, e.GhostId, Top(threat) * (1f + c.TauntBonus) - Of(threat, e.GhostId), now);
                    // IHostileBrain.Aggro (M5.5c): fight this one now, whatever the threat so far (at least a point above the top)
                    else if (e.Kind == BrainEventKind.Aggro) Add(threat, e.GhostId, math.max(1f, Top(threat) * (1f + c.TauntBonus)) - Of(threat, e.GhostId), now);
                }
                for (int i = 0; i < threat.Length; i++)
                {
                    ThreatEntry t = threat[i];
                    if (now - t.LastTick <= c.ThreatDecayAfterTicks) continue;
                    t.Threat *= 1f - c.ThreatDecayPerTick;
                    threat[i] = t;
                }

                ref HostileTarget aim = ref target.ValueRW;
                int best = -1;
                for (int i = 0; i < threat.Length; i++)
                    if (threat[i].GhostId != aim.GhostId && (best < 0 || threat[i].Threat > threat[best].Threat)) best = i;
                if (best < 0) continue;
                ThreatEntry challenger = threat[best];
                if (aim.GhostId == 0)
                {
                    Aim(ref aim, challenger, sense);
                    continue;
                }
                bool inReach = sense.ByGhostId.TryGetValue(challenger.GhostId, out int p)
                    && HostileRules.InReach(transform.ValueRO.Position, sense.Players[p].Feet, life.ValueRO.AttackRange, c.ReachSlack, life.ValueRO.ReachAbove);
                if (HostileRules.ShouldSwitch(Of(threat, aim.GhostId), challenger.Threat, inReach, c.ThreatSwitchMelee, c.ThreatSwitchRanged))
                    Aim(ref aim, challenger, sense);
            }
        }

        static void Aim(ref HostileTarget aim, in ThreatEntry who, in SenseWorld sense)
        {
            aim.GhostId = who.GhostId;
            aim.Ghost = who.Ghost;
            if (sense.ByGhostId.TryGetValue(who.GhostId, out int p))
            {
                aim.Ghost = sense.Players[p].Ghost;
                aim.LastFeet = sense.Players[p].Feet;
            }
        }

        /// <summary>Adds threat for a player; a seventh player replaces the lowest only when it is higher.</summary>
        public static void Add(DynamicBuffer<ThreatEntry> threat, int ghostId, float amount, uint now)
        {
            for (int i = 0; i < threat.Length; i++)
            {
                if (threat[i].GhostId != ghostId) continue;
                ThreatEntry t = threat[i];
                t.Threat += amount;
                t.LastTick = now;
                threat[i] = t;
                return;
            }
            var entry = new ThreatEntry { GhostId = ghostId, Threat = amount, LastTick = now };
            if (threat.Length < HostileRules.ThreatCapacity) { threat.Add(entry); return; }
            int lowest = 0;
            for (int i = 1; i < threat.Length; i++) if (threat[i].Threat < threat[lowest].Threat) lowest = i;
            if (amount > threat[lowest].Threat) threat[lowest] = entry;
        }

        static float Of(DynamicBuffer<ThreatEntry> threat, int ghostId)
        {
            for (int i = 0; i < threat.Length; i++) if (threat[i].GhostId == ghostId) return threat[i].Threat;
            return 0f;
        }

        static float Top(DynamicBuffer<ThreatEntry> threat)
        {
            float top = 0f;
            for (int i = 0; i < threat.Length; i++) top = math.max(top, threat[i].Threat);
            return top;
        }
    }

    /// <summary>
    /// The AttacksOnSight scan (H02, M5.3b1): on its think ticks an idle or patrolling AttacksOnSight Hostile looks for the nearest
    /// living player in its aggro radius who is not on neutral ground or ignored (H17: ignore is sight only) and marks it seen; a FightsBack
    /// Hostile never picks anyone by sight. The decide step turns "seen" into Alert.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileThreatSystem))]
    [BurstCompile]
    public partial struct HostileSenseScanSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<SenseWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float cell = SystemAPI.GetSingleton<BrainConfig>().PlayerCell;
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            var found = new NativeList<int>(16, Allocator.Temp);
            // job later (perf phase): an IJobEntity with its own scratch list
            foreach (var (brain, target, life, clock, transform) in SystemAPI
                         .Query<RefRW<HostileBrain>, RefRW<HostileTarget>, RefRO<HostileLife>, RefRO<BrainClock>, RefRO<LocalTransform>>()
                         .WithAll<BrainTag, BrainActive>())
            {
                ref HostileBrain b = ref brain.ValueRW;
                if (clock.ValueRO.ThinkNow == 0) continue;
                b.Saw = 0;
                if (life.ValueRO.Behaviour != HostileBehaviourKind.AttacksOnSight) continue;
                if (b.Mode != HostileMode.Idle && b.Mode != HostileMode.Patrol) continue;
                float3 at = transform.ValueRO.Position;
                SenseQueries.Within(sense, at, life.ValueRO.AggroRadius, cell, found);
                int best = -1;
                float bestSq = float.MaxValue;
                for (int i = 0; i < found.Length; i++)
                {
                    SensedPlayer p = sense.Players[found[i]];
                    if ((p.Flags & SensedFlags.Alive) == 0 || (p.Flags & (SensedFlags.Neutral | SensedFlags.Ignored | SensedFlags.Dead)) != 0) continue;
                    float dSq = SenseQueries.FlatSq(p.Feet, at);
                    if (dSq < bestSq) { bestSq = dSq; best = found[i]; }
                }
                if (best < 0) continue;
                SensedPlayer seen = sense.Players[best];
                b.Saw = 1;
                target.ValueRW = new HostileTarget { Ghost = seen.Ghost, GhostId = seen.GhostId, LastFeet = seen.Feet, DistSq = bestSq };
            }
            found.Dispose();
        }
    }
}
