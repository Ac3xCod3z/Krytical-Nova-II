using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// The basic attack's combo, identical on the server and on the owning client (M4.4d): the owner's Attack / StopAttack press starts
    /// or ends it (NGO StartAttackTarget / CancelAttack), and every full tick <see cref="MeleeRules.Tick"/> moves it on as NGO's
    /// ComboLoopRoutine does, against the target's place (NGO IsWithinStrikingRange / HasLeftReach). Runs after the walk, so the reach
    /// is measured where the attacker now stands. Nothing here lands a hit: that is the server's (M4.4e).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMoveSystem))]
    [BurstCompile]
    public partial struct PlayerMeleeStateSystem : ISystem
    {
        struct Body
        {
            public int3 FeetMm;
            public bool Alive;
        }

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerGhostTag>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;
            int simRate = 30;
            if (SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0)
                simRate = (int)rate.SimulationTickRate;

            // every player body by ghost id; no Max HP yet (the body has not reported it) counts as alive
            var bodies = new NativeHashMap<int, Body>(16, Allocator.Temp);
            foreach (var (ghost, pose, vitals) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<PlayerPose>, RefRO<PlayerVitals>>().WithAll<PlayerGhostTag>())
                bodies[ghost.ValueRO.ghostId] = new Body
                {
                    FeetMm = pose.ValueRO.Mm,
                    Alive = vitals.ValueRO.MaxHealthMilli == 0 || vitals.ValueRO.HealthMilli > 0,
                };
            // the attackable ghosts that are not players (M4.2b: the Strike Dummy, whose HP refills: always alive)
            foreach (var (ghost, target) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<CombatTarget>>())
                bodies[ghost.ValueRO.ghostId] = new Body { FeetMm = target.ValueRO.FeetMm, Alive = target.ValueRO.Alive };

            foreach (var (input, combat, timing, pose, ghost) in SystemAPI
                         .Query<RefRO<PlayerInput>, RefRW<CombatState>, RefRO<MeleeTimingData>, RefRO<PlayerPose>, RefRO<GhostInstance>>()
                         .WithAll<Simulate, PlayerGhostTag>())
            {
                ref CombatState s = ref combat.ValueRW;
                PlayerInput i = input.ValueRO;
                // a dead player swings no more (NGO PlayerDeathMotor.Begin: CancelAttack on every peer) and starts nothing until it is up
                if (bodies.TryGetValue(ghost.ValueRO.ghostId, out Body self) && !self.Alive) { MeleeRules.Cancel(ref s); continue; }
                // NGO PointAndClickPlayer: a walk click (ground, water, wall) or the stop calls the attack off (CancelAttack)
                if (i.StopAttack.IsSet || i.Click.IsSet || i.Stop.IsSet) MeleeRules.Cancel(ref s);
                if (i.ToggleWeapon.IsSet) MeleeRules.Toggle(ref s, nt.ServerTick);
                if (i.SheatheWeapon.IsSet) MeleeRules.Sheathe(ref s);
                if (i.Attack.IsSet && i.AttackGhostId != 0 && i.AttackGhostId != ghost.ValueRO.ghostId) MeleeRules.Begin(ref s, i.AttackGhostId);
                if (s.TargetGhostId == 0) continue;

                bool found = bodies.TryGetValue(s.TargetGhostId, out Body target);
                int3 me = pose.ValueRO.Mm;
                MeleeRules.Tick(timing.ValueRO.Value, ref s, nt.ServerTick, simRate, found && target.Alive,
                    found && MeleeRules.InStrikingRange(timing.ValueRO.Value, me, target.FeetMm),
                    found && MeleeRules.LeftReach(timing.ValueRO.Value, me, target.FeetMm));
            }
            bodies.Dispose();
        }
    }
}
