using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Player.Entities
{
    // =================================================================================================================
    // The death knock (M4 step 6b-2): NGO's PlayerDeathMotor moved the dead body's root with Motion Warping from where it fell to
    // its landing (FindLanding), along the knockdown clip's own travel, with a critical blow's arc and an airborne body's drop.
    // The ghost owns the root (M3b owner answer Q3 a), so the server plans the same travel and every machine places the body from
    // the plan and the tick, as the jump does. The jump's MotionPlan is left alone: the knock has its own plan.
    // =================================================================================================================

    /// <summary>A dead body's travel to its landing, planned by the server at the death (never predicted, owner answer Q4).</summary>
    public struct KnockPlan : IComponentData
    {
        /// <summary>Bumped on each knock; 0 = none yet.</summary>
        [GhostField] public byte Seq;
        /// <summary>The chain the blow picked: 0 front, 1 back, 2 critical front, 3 critical back (<see cref="KnockFlight.Chain"/>).</summary>
        [GhostField] public byte Chain;
        [GhostField] public NetworkTick StartTick;
        [GhostField] public int FromXMm, FromYMm, FromZMm;
        [GhostField] public int ToXMm, ToYMm, ToZMm;
        /// <summary>The way it faced when it was struck, and the way it ends (a blow from the front turns it to the blow).</summary>
        [GhostField] public int FromYawCdeg, YawCdeg;

        public int3 FromMm => new int3(FromXMm, FromYMm, FromZMm);
        public int3 ToMm => new int3(ToXMm, ToYMm, ToZMm);
    }

    /// <summary>The four knockdowns' numbers, baked from the body's PlayerDeathMotor (one per world, like PlayerMotionCurves).</summary>
    public struct DeathKnockCurves : IComponentData
    {
        public const int Samples = 32;
        public const int Chains = 4;

        /// <summary>Share of the way covered (thousandths) at <see cref="Samples"/> equal steps over each fall clip: the clip's own travel.</summary>
        public FixedList128Bytes<short> Travel0, Travel1, Travel2, Travel3;
        public int Length0, Length1, Length2, Length3;
        public int Impact0, Impact1, Impact2, Impact3;
        /// <summary>A critical blow's extra height (PlayerDeathMotor.critLift).</summary>
        public int CritLiftMm;
        /// <summary>The turn toward a blow from the front (PlayerDeathMotor.turnSeconds).</summary>
        public int TurnMicros;

        public int LengthMicros(int chain) => chain switch { 1 => Length1, 2 => Length2, 3 => Length3, _ => Length0 };
        public int ImpactMicros(int chain) => chain switch { 1 => Impact1, 2 => Impact2, 3 => Impact3, _ => Impact0 };
        public FixedList128Bytes<short> Travel(int chain) => chain switch { 1 => Travel1, 2 => Travel2, 3 => Travel3, _ => Travel0 };

        /// <summary>Straight travel over 1.2 s, impact at 0.5 s, NGO's lift 0.55 m and turn 0.12 s: only for worlds with no baked singleton (tests).</summary>
        public static DeathKnockCurves Default
        {
            get
            {
                var c = new DeathKnockCurves { CritLiftMm = 550, TurnMicros = 120000 };
                for (int chain = 0; chain < Chains; chain++) c.Set(chain, t => t, 1.2f, 0.5f);
                return c;
            }
        }

        /// <summary>One chain's numbers: <paramref name="travel"/> maps 0..1 of the clip to 0..1 of the way.</summary>
        public void Set(int chain, Func<float, float> travel, float lengthSeconds, float impactSeconds)
        {
            var list = new FixedList128Bytes<short>();
            for (int i = 0; i <= Samples; i++)
                list.Add((short)math.clamp(math.round(travel((float)i / Samples) * 1000f), -2000, 2000));
            int length = (int)math.round(math.max(0.05f, lengthSeconds) * 1e6f), impact = (int)math.round(math.max(0f, impactSeconds) * 1e6f);
            switch (chain)
            {
                case 1: Travel1 = list; Length1 = length; Impact1 = impact; break;
                case 2: Travel2 = list; Length2 = length; Impact2 = impact; break;
                case 3: Travel3 = list; Length3 = length; Impact3 = impact; break;
                default: Travel0 = list; Length0 = length; Impact0 = impact; break;
            }
        }
    }

    /// <summary>Where the dead body is at any tick of its knock: NGO's warp (travel profile, height curve, turn) as integers.</summary>
    public static class KnockFlight
    {
        /// <summary>Killed this far above where it lands, the body drops under gravity (PlayerDeathMotor.AirborneHeight 0.15 m).</summary>
        public const int AirborneMm = 150;

        /// <summary>NGO's Pick: critical or not, from the front half or behind.</summary>
        public static byte Chain(bool fromFront, bool crit) => (byte)(crit ? (fromFront ? 2 : 3) : (fromFront ? 0 : 1));

        public static bool IsCritical(byte chain) => chain >= 2;
        public static bool IsFromFront(byte chain) => chain == 0 || chain == 2;

        public static long ElapsedMicros(in KnockPlan plan, NetworkTick tick, int tickMicros)
        {
            if (!plan.StartTick.IsValid || !tick.IsValid) return -1;
            return (long)tick.TicksSince(plan.StartTick) * tickMicros;
        }

        /// <summary>True while the knock places the body: from its start tick until one tick past the clip's end (so it ends on the landing).</summary>
        public static bool Moves(in KnockPlan plan, in DeathKnockCurves curves, NetworkTick tick, int tickMicros)
        {
            if (plan.Seq == 0) return false;
            long elapsed = ElapsedMicros(in plan, tick, tickMicros);
            return elapsed >= 0 && elapsed <= curves.LengthMicros(plan.Chain) + tickMicros;
        }

        /// <summary>The body's root at <paramref name="elapsedMicros"/> into the knock.</summary>
        public static int3 RootAt(in KnockPlan plan, in DeathKnockCurves curves, long elapsedMicros)
        {
            int3 from = plan.FromMm, to = plan.ToMm;
            long length = curves.LengthMicros(plan.Chain);
            if (elapsedMicros <= 0) return from;
            if (elapsedMicros >= length) return to;
            int share = JumpFlight.Share(curves.Travel(plan.Chain), elapsedMicros, length);
            int3 root = new int3(
                from.x + (int)((long)(to.x - from.x) * share / 1000),
                from.y,
                from.z + (int)((long)(to.z - from.z) * share / 1000));

            double t = elapsedMicros / 1e6, lengthS = length / 1e6;
            int drop = from.y - to.y;
            if (drop >= AirborneMm)
            {
                // killed in the air: it falls to the landing under gravity, down before the clip ends (NGO HeightCurve)
                double dropM = drop / 1000.0, gravity = 9.81;
                double fall = math.sqrt(2.0 * dropM / gravity), latest = math.max(0.1, lengthS - 0.1);
                if (fall > latest) gravity = 2.0 * dropM / (latest * latest);
                root.y = from.y - (int)math.round(math.min(dropM, 0.5 * gravity * t * t) * 1000.0);
            }
            else
            {
                // on the ground: the small step to the landing's height goes with the travel (NGO snapped it at LieDown)
                root.y = from.y + (int)((long)(to.y - from.y) * share / 1000);
            }
            if (IsCritical(plan.Chain))
            {
                // a critical blow throws it up in an arc until it hits the ground
                double land = math.clamp(curves.ImpactMicros(plan.Chain) / 1e6, 0.1, lengthS);
                if (t < land)
                {
                    double u = t / land;
                    root.y += (int)math.round(curves.CritLiftMm * 4.0 * u * (1.0 - u));
                }
            }
            return root;
        }

        /// <summary>The body's facing: a blow from the front turns it to the blow over the turn time (smoothstep), one from behind never turns it.</summary>
        public static int YawAt(in KnockPlan plan, in DeathKnockCurves curves, long elapsedMicros)
        {
            if (!IsFromFront(plan.Chain) || curves.TurnMicros <= 0 || elapsedMicros >= curves.TurnMicros) return plan.YawCdeg;
            if (elapsedMicros <= 0) return plan.FromYawCdeg;
            double u = (double)elapsedMicros / curves.TurnMicros, s = u * u * (3.0 - 2.0 * u);
            int delta = ((plan.YawCdeg - plan.FromYawCdeg) % 36000 + 54000) % 36000 - 18000;   // the short way round
            return ((plan.FromYawCdeg + (int)math.round(delta * s)) % 36000 + 36000) % 36000;
        }
    }

    /// <summary>
    /// Places each dead body on its knock, on the server and on the owner (predicted from the server's plan, never ahead of it), after the
    /// walk and the jump: the walk is dropped while it moves. Observers' bodies follow the interpolated ghost.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerMotionSystem))]
    public partial struct DeathKnockSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerGhostTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid || nt.IsPartialTick) return;
            var curves = SystemAPI.TryGetSingleton<DeathKnockCurves>(out var c) ? c : DeathKnockCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            foreach (var (knock, move, pose, transform) in SystemAPI
                         .Query<RefRO<KnockPlan>, RefRW<MoveState>, RefRW<PlayerPose>, RefRW<LocalTransform>>()
                         .WithAll<PlayerGhostTag, Simulate>())
            {
                KnockPlan plan = knock.ValueRO;
                if (!KnockFlight.Moves(in plan, in curves, nt.ServerTick, tickMicros)) continue;
                long elapsed = KnockFlight.ElapsedMicros(in plan, nt.ServerTick, tickMicros);
                int3 root = KnockFlight.RootAt(in plan, in curves, elapsed);
                ref var p = ref pose.ValueRW;
                p.XMm = root.x; p.YMm = root.y; p.ZMm = root.z;
                p.YawCdeg = KnockFlight.YawAt(in plan, in curves, elapsed);
                PlayerMovement.CancelWalk(ref move.ValueRW);
                transform.ValueRW.Position = new float3(p.XMm, p.YMm, p.ZMm) * 0.001f;
                transform.ValueRW.Rotation = quaternion.RotateY(math.radians(p.YawCdeg * 0.01f));
            }
        }
    }
}
