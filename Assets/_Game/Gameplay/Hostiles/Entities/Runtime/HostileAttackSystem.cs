using KryticalNova.Brains;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// One attack of a Hostile's species in ticks (M5.3b2c: carried by each Hostile for now; the species bake fills it from its
    /// <see cref="HostileDefinition"/>).
    /// </summary>
    [InternalBufferCapacity(3)]
    public struct HostileAbilityRow : IBufferElementData
    {
        public uint WindupTicks;
        public uint RecoverTicks;
        public float Range;
        public float Weight;
        public byte Heavy;
        /// <summary>The tick it can start again (its recover plus the attack cooldown after its last swing).</summary>
        public uint ReadyTick;
    }

    /// <summary>The swings that resolved this tick on their targets (singleton queue; the bridge rolls them on the bodies, M5.6).</summary>
    public struct PendingStrikeQueue : IComponentData { }

    /// <summary>
    /// The swing (M5.3b2c, implementation plan M5.3): an engaged Hostile whose target is in reach (range + slack, not above what it
    /// reaches) and in front (facing) starts a ready attack that tick, picked by weight with its own dice; it turns with its target;
    /// before the commit tick a target beyond range + slack + 1 m cancels it, after it the swing can only miss; at the windup's end a
    /// target still in reach and in front is struck (<see cref="PendingStrike"/>), then it recovers and the attack waits its cooldown.
    /// Hits on the Hostile never cancel a windup (owner Q4: a flinch is a look only).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileDecideSystem))]
    [BurstCompile]
    public partial struct HostileAttackSystem : ISystem
    {
        const float CancelBeyond = 1f;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HostileBrainConfig>();
            state.RequireForUpdate<SenseWorld>();
            state.RequireForUpdate<BrainTick>();
            Entity queue = state.EntityManager.CreateEntity(typeof(PendingStrikeQueue));
            state.EntityManager.AddBuffer<PendingStrike>(queue);
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            HostileBrainConfig c = SystemAPI.GetSingleton<HostileBrainConfig>();
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            DynamicBuffer<PendingStrike> strikes = SystemAPI.GetSingletonBuffer<PendingStrike>();
            // the stage manager (M6.5): a Hostile with a HostileSlot starts a swing only holding a slot, and pays its attack cost
            bool slotted = SystemAPI.TryGetSingleton(out HostileSlotBook book);
            ComponentLookup<HostileSlot> slots = SystemAPI.GetComponentLookup<HostileSlot>();
            // job later (perf phase): an IJobEntity writing strikes through a parallel queue
            foreach (var (brain, target, mover, life, transform, dice, abilitiesBuffer, entity) in SystemAPI
                         .Query<RefRW<HostileBrain>, RefRW<HostileTarget>, RefRW<BrainMover>, RefRO<HostileLife>, RefRO<LocalTransform>, RefRW<BrainDice>,
                             DynamicBuffer<HostileAbilityRow>>()
                         .WithAll<BrainTag, BrainActive>().WithEntityAccess())
            {
                ref HostileBrain b = ref brain.ValueRW;
                ref HostileTarget aim = ref target.ValueRW;
                ref BrainMover m = ref mover.ValueRW;
                DynamicBuffer<HostileAbilityRow> abilities = abilitiesBuffer;
                if (b.Mode != HostileMode.Engage || abilities.Length == 0)
                {
                    b.Phase = HostilePhase.Approach;
                    continue;
                }
                float3 at = transform.ValueRO.Position;
                bool sensed = aim.GhostId != 0 && sense.ByGhostId.TryGetValue(aim.GhostId, out int p)
                              && (sense.Players[p].Flags & SensedFlags.Alive) != 0 && (sense.Players[p].Flags & SensedFlags.Dead) == 0;
                float3 feet = aim.LastFeet;
                if (sensed) feet = sense.Players[sense.ByGhostId[aim.GhostId]].Feet;
                float range = abilities[math.min(b.Ability, abilities.Length - 1)].Range;
                bool inReach = sensed && HostileRules.InReach(at, feet, range, c.ReachSlack, life.ValueRO.ReachAbove);
                bool facing = sensed && HostileRules.Facing(m.Yaw, at, feet, c.FacingDegrees);
                aim.Flags = (byte)((inReach ? HostileTargetFlags.InReach : 0) | (facing ? HostileTargetFlags.Facing : 0));

                if (b.Phase == HostilePhase.Windup)
                {
                    if (sensed && now < b.CommitTick)
                    {
                        // turns with its target until the commit; a target that ran clear calls the swing off
                        Face(ref m, at, feet);
                        float cancel = range + c.ReachSlack + CancelBeyond;
                        if (SenseQueries.FlatSq(feet, at) > cancel * cancel) { b.Phase = HostilePhase.Approach; continue; }
                    }
                    if (now < b.PhaseEndTick) continue;
                    HostileAbilityRow used = abilities[b.Ability];
                    if (inReach && facing)
                        strikes.Add(new PendingStrike { Hostile = entity, Target = aim.Ghost, Ability = b.Ability, From = at, Heavy = used.Heavy });
                    used.ReadyTick = now + used.RecoverTicks + c.AttackCooldownTicks;
                    abilities[b.Ability] = used;
                    b.Phase = HostilePhase.Recover;
                    b.PhaseEndTick = now + used.RecoverTicks;
                    continue;
                }
                if (b.Phase == HostilePhase.Tell)
                {
                    // the tell (Q3 A): it turns with its target; a target that ran clear calls it off (a feint), else the windup starts
                    if (sensed) Face(ref m, at, feet);
                    float cancelTell = range + c.ReachSlack + CancelBeyond;
                    if (!sensed || SenseQueries.FlatSq(feet, at) > cancelTell * cancelTell) { b.Phase = HostilePhase.Approach; continue; }
                    if (now < b.PhaseEndTick) continue;
                    StartWindup(ref b, abilities[b.Ability], now, c.CommitFraction);
                    continue;
                }
                if (b.Phase == HostilePhase.Recover)
                {
                    if (now < b.PhaseEndTick) continue;
                    b.Phase = HostilePhase.Approach;
                }
                if (!inReach) continue;
                if (!facing) { Face(ref m, at, feet); continue; }
                bool useSlot = slotted && slots.HasComponent(entity);
                if (useSlot && slots[entity].Held == 0) continue;   // no slot of the target's ring: it waits
                int pick = HostileRules.PickAbility(abilities, now, ref dice.ValueRW.Rng);
                if (pick < 0) continue;
                HostileAbilityRow row = abilities[pick];
                if (useSlot)
                {
                    int cost = row.Heavy != 0 ? c.AttackCostHeavy : c.AttackCostLight;
                    book.AttackUsed.TryGetValue(aim.GhostId, out int used);
                    if (used + cost > c.AttackCapacity) continue;   // the player's attack budget is spent: wait for one to recover
                    book.AttackUsed[aim.GhostId] = used + cost;
                    HostileSlot s = slots[entity];
                    s.Cost = (byte)cost;
                    s.WaitingSinceTick = 0;
                    slots[entity] = s;
                }
                b.Ability = (byte)pick;
                uint tell = HostileRules.TellTicks(row.WindupTicks, c.MinVisibleWarningTicks, c.WarningLatencyTicks);
                if (tell > 0)
                {
                    b.Phase = HostilePhase.Tell;
                    b.PhaseEndTick = now + tell;
                    continue;
                }
                StartWindup(ref b, row, now, c.CommitFraction);
            }
        }

        static void StartWindup(ref HostileBrain b, in HostileAbilityRow row, uint now, float commitFraction)
        {
            b.Phase = HostilePhase.Windup;
            b.AttackStartTick = now;
            b.PhaseEndTick = now + row.WindupTicks;
            b.CommitTick = HostileRules.CommitTick(now, row.WindupTicks, commitFraction);
        }

        static void Face(ref BrainMover m, float3 at, float3 feet)
        {
            float2 to = feet.xz - at.xz;
            if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
            m.Flags |= BrainMoverFlags.Face;
        }
    }
}
