using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Build 17, item 11.1 (owner pick C): players walk through each other, but nobody stays standing inside another. When two players
    /// both stand still closer than two personal circles, the one who stopped last slides straight out, a little each tick, until they
    /// touch. Only the server moves anyone (the owner's prediction takes the corrected pose from the next snapshot); a walk, a jump, a
    /// wall or the water is never touched. The rule itself is <see cref="PlayerSeparation"/> (pure, tested). Numbers: MovementSettings
    /// (personalSpaceRadius, slideOutSpeed) through <see cref="PlayerMoveSettings"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PredictedMoveSystem))]
    public partial class PlayerSeparationSystem : SystemBase
    {
        NativeHashMap<Entity, uint> m_StoodSince;
        /// <summary>The part of a millimetre each sliding player has not moved yet (the pose is whole mm; a step may not be).</summary>
        NativeHashMap<Entity, double2> m_Carry;
        PlayerServerPlanSystem m_Plans;

        /// <summary>Slides made; pairs the walkable surface kept from sliding either way (tests, diagnostics).</summary>
        public int Slides { get; private set; }
        public int Blocked { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            RequireForUpdate<PlayerMoveSettings>();
            m_StoodSince = new NativeHashMap<Entity, uint>(64, Allocator.Persistent);
            m_Carry = new NativeHashMap<Entity, double2>(16, Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            if (m_StoodSince.IsCreated) m_StoodSince.Dispose();
            if (m_Carry.IsCreated) m_Carry.Dispose();
        }

        /// <summary>The server's tick rate: the world's own, else NetCode's default (never a number of ours).</summary>
        public static int TickRateOf(bool found, ClientServerTickRate rate)
        {
            if (!found) rate = default;
            rate.ResolveDefaults();
            return rate.SimulationTickRate;
        }

        protected override void OnUpdate()
        {
            PlayerMoveSettings cfg = SystemAPI.GetSingleton<PlayerMoveSettings>();
            if (cfg.PersonalRadiusMm <= 0 || cfg.SlideOutMmps <= 0) return;
            NetworkTime time = SystemAPI.GetSingleton<NetworkTime>();
            if (!time.ServerTick.IsValid) return;
            uint tick = time.ServerTick.TickIndexForValidTick;
            int tickRate = TickRateOf(SystemAPI.TryGetSingleton(out ClientServerTickRate rate), rate);
            double stepMm = PlayerSeparation.StepMm(cfg.SlideOutMmps, tickRate);
            m_Plans ??= World.GetExistingSystemManaged<PlayerServerPlanSystem>();
            // the walkable surface the server's walk is planned on (NavMeshPathPlanner); none = a flat test world, nothing to clamp to
            IWalkableSurface surface = m_Plans != null ? m_Plans.Planner as IWalkableSurface : null;

            var standing = new NativeList<PlayerSeparation.Body>(16, Allocator.Temp);
            var seen = new NativeHashSet<Entity>(16, Allocator.Temp);
            foreach (var (move, pose, entity) in SystemAPI.Query<RefRO<MoveState>, RefRO<PlayerPose>>().WithAll<PlayerGhostTag>().WithEntityAccess())
            {
                seen.Add(entity);
                bool still = PlayerSeparation.IsStanding(in move.ValueRO);
                if (still && SystemAPI.HasComponent<MotionPlan>(entity)) still = SystemAPI.GetComponent<MotionPlan>(entity).Kind == MotionKind.None;
                if (still && SystemAPI.HasComponent<PlayerVitals>(entity))
                {
                    PlayerVitals v = SystemAPI.GetComponent<PlayerVitals>(entity);
                    still = v.MaxHealthMilli <= 0 || v.HealthMilli > 0; // a fallen body is left where it fell
                }
                if (!still) { m_StoodSince.Remove(entity); m_Carry.Remove(entity); continue; }
                if (!m_StoodSince.TryGetValue(entity, out uint since)) { since = tick; m_StoodSince[entity] = tick; }
                standing.Add(new PlayerSeparation.Body { Entity = entity, XMm = pose.ValueRO.XMm, YMm = pose.ValueRO.YMm, ZMm = pose.ValueRO.ZMm, StoodSince = since });
            }
            // players who left the world: forget them
            NativeArray<Entity> keys = m_StoodSince.GetKeyArray(Allocator.Temp);
            for (int k = 0; k < keys.Length; k++) if (!seen.Contains(keys[k])) { m_StoodSince.Remove(keys[k]); m_Carry.Remove(keys[k]); }

            // every standing pair (a town square holds tens of players; a grid comes with The Scar's crowds)
            for (int i = 0; i < standing.Length; i++)
            for (int j = i + 1; j < standing.Length; j++)
            {
                if (!PlayerSeparation.SlideOut(standing[i], standing[j], cfg.PersonalRadiusMm, stepMm, out int which, out double2 away)) continue;
                // the one who stopped last slides; when the edge of the walkable surface (a wall) stops it, the other steps aside instead
                if (TrySlide(standing, which == 0 ? i : j, away, surface)) continue;
                if (TrySlide(standing, which == 0 ? j : i, -away, surface)) continue;
                Blocked++;
            }
        }

        bool TrySlide(NativeList<PlayerSeparation.Body> standing, int at, double2 moveMm, IWalkableSurface surface)
        {
            PlayerSeparation.Body b = standing[at];
            m_Carry.TryGetValue(b.Entity, out double2 carry);
            int2 whole = PlayerSeparation.Carry(ref carry, moveMm);
            if (whole.x == 0 && whole.y == 0) { m_Carry[b.Entity] = carry; return true; }   // under a millimetre this tick: kept for the next
            var from = new int3(b.XMm, b.YMm, b.ZMm);
            var to = new int3(b.XMm + whole.x, b.YMm, b.ZMm + whole.y);
            if (surface != null)
            {
                if (!surface.TryClampStep(from, to, out int3 reached) || (reached.x == from.x && reached.z == from.z))
                {
                    m_Carry.Remove(b.Entity);
                    return false;
                }
                if (reached.x != to.x || reached.z != to.z) carry = double2.zero;   // stopped at the edge: no leftover pushes it on
                to = new int3(reached.x, b.YMm, reached.z);
            }
            m_Carry[b.Entity] = carry;
            b.XMm = to.x;
            b.ZMm = to.z;
            standing[at] = b;
            RefRW<PlayerPose> pose = SystemAPI.GetComponentRW<PlayerPose>(b.Entity);
            pose.ValueRW.XMm = b.XMm;
            pose.ValueRW.ZMm = b.ZMm;
            RefRW<LocalTransform> transform = SystemAPI.GetComponentRW<LocalTransform>(b.Entity);
            transform.ValueRW.Position = new float3(b.XMm, pose.ValueRO.YMm, b.ZMm) * 0.001f;
            Slides++;
            return true;
        }
    }

    /// <summary>The rule of <see cref="PlayerSeparationSystem"/>, in integers (mm): pure, so it is tested without a world.</summary>
    public static class PlayerSeparation
    {
        public struct Body
        {
            public Entity Entity;
            public int XMm, YMm, ZMm;
            public uint StoodSince;
        }

        /// <summary>Standing still on the ground: no walk, not on the water or a wall.</summary>
        public static bool IsStanding(in MoveState move) => (move.Flags & MoveFlags.Moving) == 0 && move.Water == 0 && move.Wall == 0;

        /// <summary>
        /// When <paramref name="a"/> and <paramref name="b"/> stand closer than two circles (and about level: within 1.5 m up or down),
        /// which of them moves (0 = a, 1 = b: the one who stopped last; the higher entity index on a tie) and by how much (x, z in mm,
        /// at most <paramref name="stepMm"/>, never past touching).
        /// </summary>
        public static bool SlideOut(in Body a, in Body b, int radiusMm, int stepMm, out int which, out int2 moveMm)
        {
            moveMm = int2.zero;
            if (!SlideOut(a, b, radiusMm, (double)stepMm, out which, out double2 move)) return false;
            moveMm = new int2((int)math.round(move.x), (int)math.round(move.y));
            return moveMm.x != 0 || moveMm.y != 0;
        }

        /// <summary>As above, in fractions of a millimetre: the system keeps what a tick cannot move yet (<see cref="Carry"/>).</summary>
        public static bool SlideOut(in Body a, in Body b, int radiusMm, double stepMm, out int which, out double2 moveMm)
        {
            which = 0;
            moveMm = double2.zero;
            if (math.abs(a.YMm - b.YMm) > 1500) return false;
            long dx = (long)b.XMm - a.XMm, dz = (long)b.ZMm - a.ZMm;
            long apart = 2L * radiusMm;
            long d2 = dx * dx + dz * dz;
            if (d2 >= apart * apart) return false;
            bool bMoves = b.StoodSince > a.StoodSince || (b.StoodSince == a.StoodSince && b.Entity.Index > a.Entity.Index);
            which = bMoves ? 1 : 0;
            double d = math.sqrt((double)d2);
            double2 away;
            if (d < 1.0)
            {
                // exactly on top of each other: a fixed way for the pair, the same every tick
                int lo = math.min(a.Entity.Index, b.Entity.Index), hi = math.max(a.Entity.Index, b.Entity.Index);
                double angle = (((lo * 73856093) ^ (hi * 19349663)) & 0x7FFFFFFF) % 360 * (math.PI_DBL / 180.0);
                away = new double2(math.cos(angle), math.sin(angle));
                if (!bMoves) away = -away;
            }
            else
            {
                away = new double2(dx / d, dz / d);     // from a toward b
                if (!bMoves) away = -away;             // a moves away from b
            }
            double step = math.min(stepMm, apart - d);
            if (step <= 0) return false;
            moveMm = away * step;
            return true;
        }

        /// <summary>One tick's slide in mm at <paramref name="slideMmps"/>, fractional (1500 mm/s at 45 ticks is 33.3 mm).</summary>
        public static double StepMm(int slideMmps, int tickRate) => tickRate > 0 ? (double)slideMmps / tickRate : 0;

        /// <summary>Adds <paramref name="moveMm"/> to what was left over; returns the whole millimetres to move now and keeps the rest.</summary>
        public static int2 Carry(ref double2 carry, double2 moveMm)
        {
            double2 total = carry + moveMm;
            var whole = new int2((int)math.trunc(total.x), (int)math.trunc(total.y));
            carry = total - new double2(whole.x, whole.y);
            return whole;
        }
    }
}
