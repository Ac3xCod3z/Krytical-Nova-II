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
    public partial struct PlayerSeparationSystem : ISystem
    {
        NativeHashMap<Entity, uint> m_StoodSince;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkTime>();
            state.RequireForUpdate<PlayerMoveSettings>();
            m_StoodSince = new NativeHashMap<Entity, uint>(64, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (m_StoodSince.IsCreated) m_StoodSince.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            PlayerMoveSettings cfg = SystemAPI.GetSingleton<PlayerMoveSettings>();
            if (cfg.PersonalRadiusMm <= 0 || cfg.SlideOutMmps <= 0) return;
            NetworkTime time = SystemAPI.GetSingleton<NetworkTime>();
            if (!time.ServerTick.IsValid) return;
            uint tick = time.ServerTick.TickIndexForValidTick;
            int tickRate = SystemAPI.TryGetSingleton(out ClientServerTickRate rate) && rate.SimulationTickRate > 0 ? rate.SimulationTickRate : 60;
            int stepMm = math.max(1, cfg.SlideOutMmps / tickRate);

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
                if (!still) { m_StoodSince.Remove(entity); continue; }
                if (!m_StoodSince.TryGetValue(entity, out uint since)) { since = tick; m_StoodSince[entity] = tick; }
                standing.Add(new PlayerSeparation.Body { Entity = entity, XMm = pose.ValueRO.XMm, YMm = pose.ValueRO.YMm, ZMm = pose.ValueRO.ZMm, StoodSince = since });
            }
            // players who left the world: forget them
            NativeArray<Entity> keys = m_StoodSince.GetKeyArray(Allocator.Temp);
            for (int k = 0; k < keys.Length; k++) if (!seen.Contains(keys[k])) m_StoodSince.Remove(keys[k]);

            // every standing pair (a town square holds tens of players; a grid comes with The Scar's crowds)
            for (int i = 0; i < standing.Length; i++)
            for (int j = i + 1; j < standing.Length; j++)
            {
                if (!PlayerSeparation.SlideOut(standing[i], standing[j], cfg.PersonalRadiusMm, stepMm, out int which, out int2 moveMm)) continue;
                int at = which == 0 ? i : j;
                PlayerSeparation.Body b = standing[at];
                b.XMm += moveMm.x;
                b.ZMm += moveMm.y;
                standing[at] = b;
                RefRW<PlayerPose> pose = SystemAPI.GetComponentRW<PlayerPose>(b.Entity);
                pose.ValueRW.XMm = b.XMm;
                pose.ValueRW.ZMm = b.ZMm;
                RefRW<LocalTransform> transform = SystemAPI.GetComponentRW<LocalTransform>(b.Entity);
                transform.ValueRW.Position = new float3(b.XMm, pose.ValueRO.YMm, b.ZMm) * 0.001f;
            }
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
            which = 0;
            moveMm = int2.zero;
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
            double step = math.min((double)stepMm, apart - d);
            moveMm = new int2((int)math.round(away.x * step), (int)math.round(away.y * step));
            return moveMm.x != 0 || moveMm.y != 0;
        }
    }
}
