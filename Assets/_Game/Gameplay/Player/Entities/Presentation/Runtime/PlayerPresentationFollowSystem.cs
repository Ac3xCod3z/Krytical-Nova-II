using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// Client only, every rendered frame (after the simulation and prediction have finished): puts each player body where its ghost is,
    /// with the easing of <see cref="FollowMath"/> (between full ticks of a predicted ghost, a decaying offset so a rollback correction
    /// slides, a snap when <see cref="TeleportSeq"/> changed). It reads the ghost and writes the body; it never writes the ghost.
    /// Also fills <see cref="PlayerPresentationState"/> for the animation. Managed, so not Burst.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlayerPresentationFollowSystem : SystemBase
    {
        EntityQuery m_Query;

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, LocalTransform, TeleportSeq, MoveState, MoveStats>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = World.Time.DeltaTime;
            int rate = 30;
            if (TryGetSingleton<ClientServerTickRate>(out var tickRate) && tickRate.SimulationTickRate > 0)
                rate = (int)tickRate.SimulationTickRate;
            float tickSeconds = 1f / rate;
            var cfg = TryGetSingleton<FollowSettings>(out var custom) ? custom : FollowSettings.Default;

            using var ghosts = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in ghosts)
            {
                if (!GhostBodyRegistry.TryGetByGhost(World, e, out var entry)) continue;

                var t = em.GetComponentData<LocalTransform>(e);
                var move = em.GetComponentData<MoveState>(e);
                var stats = em.GetComponentData<MoveStats>(e);
                byte tele = em.GetComponentData<TeleportSeq>(e).Value;
                // an owner's ghost is predicted (changes on full ticks only); everyone else's is interpolated by Netcode every frame
                bool predicted = em.HasComponent<PredictedGhost>(e);
                float maxSpeed = math.max(math.max(stats.RunMmps, stats.WalkMmps), move.SpeedMmps) * 0.001f;

                bool snapped = FollowMath.Step(ref entry.Follow, t.Position, FollowMath.YawDegrees(t.Rotation), tele, predicted,
                    (move.Flags & MoveFlags.Moving) != 0, maxSpeed, dt, tickSeconds, in cfg);

                var body = entry.Body.transform;
                float3 p = entry.Follow.Position;
                body.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, entry.Follow.Yaw, 0f));

                var state = entry.State;
                if (state != null)
                {
                    if (snapped) state.Snaps++;
                    state.State = new PlayerAnimState
                    {
                        Speed = (move.Flags & MoveFlags.Moving) != 0 ? move.SpeedMmps * 0.001f : 0f,
                        Moving = (move.Flags & MoveFlags.Moving) != 0,
                        Running = (move.Flags & MoveFlags.Run) != 0,
                    };
                }
            }
        }
    }
}
