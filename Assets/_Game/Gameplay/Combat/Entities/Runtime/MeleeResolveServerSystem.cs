using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;
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
    /// One hit per swing, landed or not. Every other hostile in the swing's arc where the attacker saw it is struck too (M4.4g: NGO's
    /// blade sweep strikes each hostile it touches). Players only for now: hostiles and the Combat Dummy come with their own ghosts.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedSimulationSystemGroup))]
    public partial class MeleeResolveServerSystem : SystemBase
    {
        /// <summary>Strikes that landed (tests).</summary>
        public int Hits { get; private set; }

        /// <summary>Attack orders dropped because the target is gone, dead or not hostile (tests).</summary>
        public int Refused { get; private set; }

        /// <summary>NGO RequestAttackEntityServerRpc's check (PointAndClickPlayer:1881-1895), on the bodies, with the same rule.</summary>
        public static bool AttackAllowed(UnityEngine.GameObject attacker, IDamageable target) =>
            attacker != null && target != null && target.IsAlive && CombatHostility.IsHostile(attacker.transform, target);

        bool AttackAllowed(EntityManager em, GhostBodyEntry attacker, int targetGhostId) =>
            TryFind(em, targetGhostId, out GhostBodyEntry target) && target.Body.TryGetComponent(out IDamageable damageable)
            && AttackAllowed(attacker.Body, damageable);

        protected override void OnCreate() => RequireForUpdate<NetworkTime>();

        protected override void OnUpdate()
        {
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid) return;
            int rate = SystemAPI.TryGetSingleton<ClientServerTickRate>(out var r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            var collateral = new NativeList<int>(Allocator.Temp);
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(attacker.World, World) || attacker.Body == null || !em.Exists(attacker.Ghost)
                    || !em.HasComponent<CombatState>(attacker.Ghost) || !em.HasComponent<MeleeTimingData>(attacker.Ghost)) continue;
                CombatState s = em.GetComponentData<CombatState>(attacker.Ghost);
                if (s.TargetGhostId == 0) continue;
                // NGO RequestAttackEntityServerRpc: the target must exist, be alive and hostile, else the order is silently dropped
                // (the owner's predicted combo is then rolled back by the server's state)
                if (!AttackAllowed(em, attacker, s.TargetGhostId))
                {
                    MeleeRules.Cancel(ref s);
                    em.SetComponentData(attacker.Ghost, s);
                    Refused++;
                    continue;
                }
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
                // M4.4g: everyone else who could be under the blade (NGO's sweep strikes each hostile it touches, once per swing)
                bystanders.Clear();
                foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(e.World, World) && e != attacker && e != target && e.Body != null && em.Exists(e.Ghost)
                        && em.HasComponent<PositionHistory>(e.Ghost) && e.Body.TryGetComponent(out IDamageable _))
                        bystanders.Add(e);
                var rings = new NativeArray<PositionHistory>(bystanders.Count, Allocator.Temp);
                for (int i = 0; i < bystanders.Count; i++) rings[i] = em.GetComponentData<PositionHistory>(bystanders[i].Ghost);
                collateral.Clear();
                bool resolved = MeleeRules.Resolve(t, me.Mm, me.YawCdeg, now, seen, em.GetComponentData<PositionHistory>(target.Ghost), them, rings,
                    HitProxy.Player, out bool targetHit, ref collateral);
                rings.Dispose();
                if (!resolved) continue;
                float multiplier = MeleeRules.StepMultiplier(t, s.Step);
                if (targetHit && MeleeStrike.Apply(attacker.Body, damageable, s.Step, multiplier)) Hits++;   // the ring is written by HitRecordServerSystem
                // a friend in the arc is left alone: MeleeStrike asks the same hostility rule
                for (int i = 0; i < collateral.Length; i++)
                {
                    GhostBodyEntry by = bystanders[collateral[i]];
                    if (by.Body.TryGetComponent(out IDamageable other) && MeleeStrike.Apply(attacker.Body, other, s.Step, multiplier)) Collateral++;
                }
            }
            collateral.Dispose();
        }

        readonly System.Collections.Generic.List<GhostBodyEntry> bystanders = new System.Collections.Generic.List<GhostBodyEntry>();

        /// <summary>Others struck by a swing at its target (tests).</summary>
        public int Collateral { get; private set; }

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
