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
                if (b.Phase == HostilePhase.Recover)
                {
                    if (now < b.PhaseEndTick) continue;
                    b.Phase = HostilePhase.Approach;
                }
                if (!inReach) continue;
                if (!facing) { Face(ref m, at, feet); continue; }
                int pick = Pick(abilities, now, ref dice.ValueRW.Rng);
                if (pick < 0) continue;
                HostileAbilityRow row = abilities[pick];
                b.Ability = (byte)pick;
                b.Phase = HostilePhase.Windup;
                b.AttackStartTick = now;
                b.PhaseEndTick = now + row.WindupTicks;
                b.CommitTick = HostileRules.CommitTick(now, row.WindupTicks, c.CommitFraction);
            }
        }

        static void Face(ref BrainMover m, float3 at, float3 feet)
        {
            float2 to = feet.xz - at.xz;
            if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
            m.Flags |= BrainMoverFlags.Face;
        }

        /// <summary>A ready attack by weight (NGO's 45/40/15 dice when the weights are those), or -1.</summary>
        static int Pick(DynamicBuffer<HostileAbilityRow> abilities, uint now, ref Random rng)
        {
            float total = 0f;
            for (int i = 0; i < abilities.Length; i++) if (abilities[i].ReadyTick <= now) total += math.max(0f, abilities[i].Weight);
            if (total <= 0f) return -1;
            float roll = rng.NextFloat(total);
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i].ReadyTick > now) continue;
                roll -= math.max(0f, abilities[i].Weight);
                if (roll < 0f) return i;
            }
            return abilities.Length - 1;
        }
    }
}
