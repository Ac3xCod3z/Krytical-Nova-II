using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: the basic attack's hits (M4.4e). On the tick a strike's window ends (<see cref="MeleeRules.HitDue"/>, NGO EnsureTargetStruck),
    /// the target is tested where the attacker saw it (the PositionHistory at the attacker's interpolation delay, M4.3) and within the
    /// server's check range now (<see cref="MeleeRules.Resolve"/>), then the body applies it as NGO does (<see cref="MeleeStrike"/>).
    /// One hit per swing, landed or not. Players only for now: hostiles and the Combat Dummy come with their own ghosts.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class MeleeResolveServerSystem : SystemBase
    {
        /// <summary>Strikes that landed (tests).</summary>
        public int Hits { get; private set; }

        protected override void OnCreate() => RequireForUpdate<NetworkTime>();

        protected override void OnUpdate()
        {
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid) return;
            int rate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            var empty = new NativeArray<PositionHistory>(0, Allocator.Temp);
            var collateral = new NativeList<int>(Allocator.Temp);
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(attacker.World, World) || attacker.Body == null || !em.Exists(attacker.Ghost)
                    || !em.HasComponent<CombatState>(attacker.Ghost) || !em.HasComponent<MeleeTimingData>(attacker.Ghost)) continue;
                CombatState s = em.GetComponentData<CombatState>(attacker.Ghost);
                MeleeTiming t = em.GetComponentData<MeleeTimingData>(attacker.Ghost).Value;
                if (!MeleeRules.HitDue(t, s, now, rate)) continue;
                s.SwingHit = 1;
                em.SetComponentData(attacker.Ghost, s);

                if (!TryFind(em, s.TargetGhostId, out GhostBodyEntry target) || !target.Body.TryGetComponent(out IDamageable damageable)) continue;
                NetworkTick seen = now;
                if (em.HasComponent<CommandDataInterpolationDelay>(attacker.Ghost))
                    seen.Subtract(em.GetComponentData<CommandDataInterpolationDelay>(attacker.Ghost).Delay);
                PlayerPose me = em.GetComponentData<PlayerPose>(attacker.Ghost);
                int3 them = em.GetComponentData<PlayerPose>(target.Ghost).Mm;
                collateral.Clear();
                if (!MeleeRules.Resolve(t, me.Mm, me.YawCdeg, now, seen, em.GetComponentData<PositionHistory>(target.Ghost), them, empty,
                        HitProxy.Player, out bool targetHit, ref collateral) || !targetHit) continue;
                if (MeleeStrike.Apply(attacker.Body, damageable, s.Step, MeleeRules.StepMultiplier(t, s.Step))) Hits++;
            }
            empty.Dispose();
            collateral.Dispose();
        }

        bool TryFind(EntityManager em, int ghostId, out GhostBodyEntry found)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.HasComponent<PositionHistory>(e.Ghost) && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                { found = e; return true; }
            found = null;
            return false;
        }
    }
}
