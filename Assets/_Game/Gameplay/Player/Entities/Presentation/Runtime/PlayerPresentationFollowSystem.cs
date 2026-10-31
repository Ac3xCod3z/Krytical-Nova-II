using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// Where the player bodies are made and placed each frame: the end of the simulation, in the player loop's Update phase, so
    /// every body is final before the LateUpdates that read it (the Cinemachine brain, foot IK). The PresentationSystemGroup runs
    /// after all LateUpdates (Entities appends it to PreLateUpdate): there the camera aimed at last frame's body and walking
    /// across the screen judders (owner's GUI retry 2, 30 Oct). The NGO game placed its own walk before the camera too
    /// (PlayerMoveAnticipation, execution order -200).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial class PlayerBodiesBeforeCameraGroup : ComponentSystemGroup { }

    /// <summary>
    /// Client only, every rendered frame (after the simulation and prediction have finished): puts each player body where its ghost is,
    /// with the easing of <see cref="FollowMath"/> (between full ticks of a predicted ghost, a decaying offset so a rollback correction
    /// slides, a snap when <see cref="TeleportSeq"/> changed). It reads the ghost and writes the body; it never writes the ghost.
    /// Also fills <see cref="PlayerPresentationState"/> for the animation. Managed, so not Burst.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PlayerBodiesBeforeCameraGroup))]
    public partial class PlayerPresentationFollowSystem : SystemBase
    {
        /// <summary>How far above or below the drawn point the ground is looked for (metres).</summary>
        const float GroundSampleM = 1.5f;

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
            int tickMicros = PlayerMovement.TickMicros(rate);
            var cfg = TryGetSingleton<FollowSettings>(out var custom) ? custom : FollowSettings.Default;
            var moveCfg = TryGetSingleton<PlayerMoveSettings>(out var setMove) ? setMove : PlayerMoveSettings.Default;
            // How far Netcode's clock is into the tick after the last full one (the predicted walk steps on full ticks only): the
            // owner's body is drawn that far along its way, so it is neither a tick late nor stepped by the frame rate.
            int elapsedMicros = 0;
            if (TryGetSingleton<NetworkTime>(out var nt) && nt.ServerTickFraction > 0f && nt.ServerTickFraction < 1f)
                elapsedMicros = (int)(nt.ServerTickFraction * tickMicros);

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

                float3 ghostPos = t.Position;
                float ghostYaw = FollowMath.YawDegrees(t.Rotation);
                bool smoothTicks = predicted;
                if (predicted && em.HasComponent<PathPlan>(e) && em.HasComponent<ClientPlanHeader>(e) && em.HasComponent<PlayerPose>(e)
                    && em.HasBuffer<PathCorner>(e) && em.HasBuffer<ClientPlanCorner>(e))
                {
                    var serverCorners = em.GetBuffer<PathCorner>(e, true).Reinterpret<int3>().AsNativeArray();
                    var clientCorners = em.GetBuffer<ClientPlanCorner>(e, true).Reinterpret<int3>().AsNativeArray();
                    if (PlayerMovement.PoseAhead(in move, em.GetComponentData<PlayerPose>(e), em.GetComponentData<PathPlan>(e), serverCorners,
                            em.GetComponentData<ClientPlanHeader>(e), clientCorners, in moveCfg, tickMicros, elapsedMicros,
                            out int3 aheadMm, out int aheadYaw))
                    {
                        ghostPos = new float3(aheadMm.x, aheadMm.y, aheadMm.z) * 0.001f;
                        ghostYaw = aheadYaw * 0.01f;
                    }
                    // the position is now right for this frame: the follow only slides out corrections, it no longer steps between ticks
                    smoothTicks = false;
                }

                bool snapped = FollowMath.Step(ref entry.Follow, ghostPos, ghostYaw, tele, smoothTicks,
                    (move.Flags & MoveFlags.Moving) != 0, maxSpeed, dt, tickSeconds, in cfg);

                var body = entry.Body.transform;
                float3 p = entry.Follow.Position;
                // The walk goes in straight lines between path corners, so on uneven ground it floats or sinks between them; the old
                // NavMeshAgent stood on the mesh every frame, and the feet's grounding IK expects that (owner's feel test 30 Oct:
                // "an issue with the ground on each step"). Drawn only: the ghost's X and Z stay the server's.
                var agent = entry.Body.GetComponent<UnityEngine.AI.NavMeshAgent>();
                float baseOffset = agent != null ? agent.baseOffset : 0f;
                if (UnityEngine.AI.NavMesh.SamplePosition(new Vector3(p.x, p.y - baseOffset, p.z), out var ground, GroundSampleM,
                        agent != null ? agent.areaMask : UnityEngine.AI.NavMesh.AllAreas))
                    p.y = ground.position.y + baseOffset;
                body.SetPositionAndRotation(new Vector3(p.x, p.y, p.z), Quaternion.Euler(0f, entry.Follow.Yaw, 0f));

                var state = entry.State;
                if (state != null)
                {
                    if (snapped) state.Snaps++;
                    state.HasPose = true;
                    state.PosePosition = body.position;
                    state.PoseYaw = entry.Follow.Yaw;
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
