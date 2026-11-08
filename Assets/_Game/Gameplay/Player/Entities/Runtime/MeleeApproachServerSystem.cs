using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Server: the walk in of the basic attack (M4.4i; NGO ComboLoopRoutine step 1, PlayerMeleeCombat:258-286). While an attack waits at
    /// step 0 with its target out of striking range, the server walks the attacker to <see cref="MeleeRules.ApproachPointMm"/>, aimed again
    /// every 0.1 s when the target has moved (NGO re-sets the agent's destination at that interval). The walk goes through
    /// <see cref="ServerOrderRequest"/> like every server-issued walk, so the owner predicts it; the strike starts on its own once in range
    /// (<see cref="PlayerMeleeStateSystem"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial struct MeleeApproachServerSystem : ISystem
    {
        /// <summary>The last walk in an attacker was given: when and where to (server only, never sent).</summary>
        struct Aim
        {
            public NetworkTick At;
            public int3 GoalMm;
        }

        NativeHashMap<Entity, Aim> m_Aims;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            m_Aims = new NativeHashMap<Entity, Aim>(16, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state) => m_Aims.Dispose();

        public void OnUpdate(ref SystemState state)
        {
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid) return;
            int rate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            int repathTicks = math.max(1, MeleeRules.Ticks(MeleeRules.ApproachRepathMicros, rate));

            var places = new NativeHashMap<int, int3>(16, Allocator.Temp);
            foreach (var (ghost, pose) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<PlayerPose>>().WithAll<PlayerGhostTag>())
                places[ghost.ValueRO.ghostId] = pose.ValueRO.Mm;
            // the attackable ghosts that are not players (M4.2b: the Strike Dummy)
            foreach (var (ghost, target) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<CombatTarget>>())
                places[ghost.ValueRO.ghostId] = target.ValueRO.FeetMm;

            var orders = new NativeList<(Entity, int3, bool)>(Allocator.Temp);
            foreach (var (combat, timing, pose, entity) in SystemAPI.Query<RefRO<CombatState>, RefRO<MeleeTimingData>, RefRO<PlayerPose>>()
                         .WithAll<PlayerGhostTag>().WithEntityAccess())
            {
                CombatState s = combat.ValueRO;
                if (s.TargetGhostId == 0 || s.Step != 0 || !places.TryGetValue(s.TargetGhostId, out int3 target))
                {
                    m_Aims.Remove(entity);
                    continue;
                }
                MeleeTiming t = timing.ValueRO.Value;
                int3 me = pose.ValueRO.Mm;
                if (MeleeRules.InStrikingRange(t, me, target)) continue;
                int3 goal = MeleeRules.ApproachPointMm(t, me, target);
                if (m_Aims.TryGetValue(entity, out Aim aim))
                {
                    bool due = now.TicksSince(aim.At) >= repathTicks;
                    bool moved = math.lengthsq((float3)(goal - aim.GoalMm)) > MeleeRules.ApproachInsetMm * MeleeRules.ApproachInsetMm;
                    if (!due || !moved) continue;
                }
                m_Aims[entity] = new Aim { At = now, GoalMm = goal };
                orders.Add((entity, goal, RunSelected(ref state, entity)));
            }
            // NGO ComboLoopRoutine sets the agent's destination at the agent's speed, which PointAndClickPlayer keeps at
            // movementSkills.CurrentSpeed: the walk in runs when Run is selected, and no Chi is charged (no destination click)
            foreach (var (entity, goal, run) in orders) PlayerEntitiesSetup.IssueOrder(state.EntityManager, entity, goal, runPaid: run, chargeRun: false);
            orders.Dispose();
            places.Dispose();
        }

        /// <summary>MovementSkillId.Run (1) is the server's selection (<see cref="MoveRanks.Selected"/>).</summary>
        public const byte RunSkill = 1;

        static bool RunSelected(ref SystemState state, Entity entity) =>
            state.EntityManager.HasComponent<MoveRanks>(entity) && state.EntityManager.GetComponentData<MoveRanks>(entity).Selected == RunSkill;
    }
}
