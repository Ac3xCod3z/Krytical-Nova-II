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
        /// <summary>
        /// The follow's speed limit on a jump's arc (metres a second): the arc is drawn exactly at this frame's time, so nothing of it
        /// is a correction to ease out (the walk's run-speed limit would trail a long jump behind the ghost).
        /// </summary>
        const float JumpFollowMaxSpeedMps = 1000f;
        /// <summary>The most of the walk's ground snap a jump carries from its start (metres); more is a placement, not a snap.</summary>
        const float JumpStartDeltaMaxM = 0.5f;

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
            var curves = TryGetSingleton<PlayerMotionCurves>(out var bakedCurves) ? bakedCurves : PlayerMotionCurves.Default;

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
                bool fromAhead = false;
                if (predicted && em.HasComponent<PathPlan>(e) && em.HasComponent<ClientPlanHeader>(e) && em.HasComponent<PlayerPose>(e)
                    && em.HasBuffer<PathCorner>(e) && em.HasBuffer<ClientPlanCorner>(e))
                {
                    var serverCorners = em.GetBuffer<PathCorner>(e, true).Reinterpret<int3>().AsNativeArray();
                    var clientCorners = em.GetBuffer<ClientPlanCorner>(e, true).Reinterpret<int3>().AsNativeArray();
                    VaultPlan serverVault = em.HasComponent<VaultPlan>(e) ? em.GetComponentData<VaultPlan>(e) : default;
                    VaultPlan clientVault = em.HasComponent<ClientVaultPlan>(e) ? em.GetComponentData<ClientVaultPlan>(e).Plan : default;
                    if (PlayerMovement.PoseAhead(in move, em.GetComponentData<PlayerPose>(e), em.GetComponentData<PathPlan>(e), serverCorners,
                            em.GetComponentData<ClientPlanHeader>(e), clientCorners, in serverVault, in clientVault, in moveCfg, tickMicros, elapsedMicros,
                            out int3 aheadMm, out int aheadYaw))
                    {
                        ghostPos = new float3(aheadMm.x, aheadMm.y, aheadMm.z) * 0.001f;
                        ghostYaw = aheadYaw * 0.01f;
                        fromAhead = true;
                    }
                    // the position is now right for this frame: the follow only slides out corrections, it no longer steps between ticks
                    smoothTicks = false;
                }

                // A jump: the root is on the plan's arc at this frame's time (between ticks too), facing the landing (M3b).
                MotionPlan jump = default;
                long jumpMicros = -1;
                if (em.HasComponent<MotionPlan>(e) && nt.ServerTick.IsValid)
                {
                    jump = em.GetComponentData<MotionPlan>(e);
                    if (predicted && em.HasComponent<ClientMotionPlan>(e))
                        jump = PlayerMotionSystem.Choose(jump, em.GetComponentData<ClientMotionPlan>(e).Plan, move.RefusedSeq);
                    jumpMicros = JumpClock(in jump, in nt, predicted, elapsedMicros, tickMicros);
                }
                bool inJump = jump.Kind == MotionKind.Jump && jumpMicros >= 0 && jumpMicros < curves.LandMicros;
                if (inJump)
                {
                    int3 feet = JumpFlight.FeetAt(in jump, in curves, jumpMicros);
                    feet.y += JumpFlight.CrouchMm(in curves, jumpMicros);   // the clip's crouch lowers the drawn body before take-off
                    ghostPos = new float3(feet.x, feet.y, feet.z) * 0.001f;
                    var shown = entry.State;
                    if (shown != null)
                    {
                        // the walk drew the body on the ground, the ghost may stand a little off it: no pop at the press
                        if (shown.JumpSeq != jump.PlanSeq)
                            shown.JumpStartDeltaM = shown.HasPose
                                ? math.clamp(shown.PosePosition.y - jump.FromYMm * 0.001f, -JumpStartDeltaMaxM, JumpStartDeltaMaxM) : 0f;
                        ghostPos.y += shown.JumpStartDeltaM * (1f - math.saturate(jumpMicros / (float)curves.ApexMicros));
                    }
                    ghostYaw = jump.YawCdeg * 0.01f;
                    smoothTicks = false;
                }

                // A vault: the drawn body is over the obstacle while the walk's progress is on the vault's leg (M3c); the height is in the
                // pose already (the owner's PoseAhead, an observer's interpolated ghost)
                VaultPlan vault = default;
                int drawnProgress = move.ProgressMm;
                if (em.HasComponent<VaultPlan>(e)) vault = em.GetComponentData<VaultPlan>(e);
                if (predicted)
                {
                    if (vault.Seq != move.OrderSeq && em.HasComponent<ClientVaultPlan>(e)) vault = em.GetComponentData<ClientVaultPlan>(e).Plan;
                    drawnProgress += (int)((long)move.SpeedMmps * math.clamp(elapsedMicros, 0, tickMicros) / 1000000);
                }
                bool inVault = (move.Flags & MoveFlags.Moving) != 0 && VaultLeg.On(in vault, move.OrderSeq, drawnProgress);
                bool airborne = inJump || inVault;
                bool onWater = move.Water != 0;   // Waterstride (M3c): the pose is on the water's surface, never snapped to the ground

                bool snapped = FollowMath.Step(ref entry.Follow, ghostPos, ghostYaw, tele, smoothTicks,
                    (move.Flags & MoveFlags.Moving) != 0 || airborne, airborne ? JumpFollowMaxSpeedMps : maxSpeed, dt, tickSeconds, in cfg);

                var body = entry.Body.transform;
                float3 p = entry.Follow.Position;
                // The walk goes in straight lines between path corners, so on uneven ground it floats or sinks between them; the old
                // NavMeshAgent stood on the mesh every frame, and the feet's grounding IK expects that (owner's feel test 30 Oct:
                // "an issue with the ground on each step"). Drawn only: the ghost's X and Z stay the server's.
                var agent = entry.Body.GetComponent<UnityEngine.AI.NavMeshAgent>();
                float baseOffset = agent != null ? agent.baseOffset : 0f;
                if (!airborne && !onWater && UnityEngine.AI.NavMesh.SamplePosition(new Vector3(p.x, p.y - baseOffset, p.z), out var ground, GroundSampleM,
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
                    state.InputYaw = ghostYaw;
                    state.InputFromPoseAhead = fromAhead;
                    state.InJump = inJump;
                    state.JumpArcErrorM = inJump ? math.distance(entry.Follow.Position, ghostPos) : 0f;
                    ShowJump(state, entry.Body.GetComponent<IGhostMotionView>(), in jump, jumpMicros, in curves);
                    ShowVault(state, entry.Body.GetComponent<IGhostMotionView>(), in vault, inVault, drawnProgress, move.SpeedMmps);
                    if (state.ShownOnWater != onWater)
                    {
                        // a change during or just after a jump is a jump off the water or onto it: the splash too
                        state.ShownOnWater = onWater;
                        bool byJump = jump.Kind == MotionKind.Jump && jumpMicros >= 0 && jumpMicros < curves.LandMicros + 1000000;
                        entry.Body.GetComponent<IGhostMotionView>()?.WaterStriding(onWater, byJump);
                    }
                    state.State = new PlayerAnimState
                    {
                        Speed = (move.Flags & MoveFlags.Moving) != 0 ? move.SpeedMmps * 0.001f : 0f,
                        Moving = (move.Flags & MoveFlags.Moving) != 0,
                        Running = (move.Flags & MoveFlags.Run) != 0,
                    };
                }
            }
        }

        /// <summary>
        /// Microseconds of a jump's clip at this frame: the owner's predicted ghost from its last full tick plus the part of the next
        /// one drawn ahead; an observer's at Netcode's interpolation time. -1 before the press or without a plan. The owner's own plan
        /// starts on the tick being predicted, so in the rest of the press's tick it reads 0: the clip starts on the press frame, as
        /// NGO's did, and is held at its start for less than a tick (GUI run predict-1, 31 Oct: it began 1-2 frames after the press).
        /// </summary>
        static long JumpClock(in MotionPlan plan, in NetworkTime nt, bool predicted, int elapsedMicros, int tickMicros)
        {
            if (plan.Kind == MotionKind.None || !plan.StartTick.IsValid) return -1;
            NetworkTick tick = predicted ? nt.ServerTick : nt.InterpolationTick;
            int partial = predicted ? elapsedMicros
                : nt.InterpolationTickFraction > 0f && nt.InterpolationTickFraction < 1f ? (int)(nt.InterpolationTickFraction * tickMicros) : 0;
            if (!tick.IsValid) return -1;
            if (partial > 0) tick.Decrement();   // a partial tick: count from the last full one
            int since = tick.TicksSince(plan.StartTick);
            if (since < 0) return predicted && partial > 0 && since == -1 ? 0 : -1;
            return JumpFlight.ElapsedMicros(in plan, tick, tickMicros) + partial;
        }

        /// <summary>
        /// Plays each phase of a jump once on the body (by the plan's number, so a rollback or the server's copy of the owner's own plan
        /// never restarts it): the clip from where it should be, the take-off, the landing; a jump taken back is cancelled.
        /// </summary>
        /// <summary>
        /// Plays a vault on the body once (by its walk's number, so a rollback or a late snapshot never restarts it): the clip from where
        /// the drawn body is along the leg when it gets there, the landing when it leaves the leg (M3c).
        /// </summary>
        static void ShowVault(PlayerPresentationState state, IGhostMotionView view, in VaultPlan vault, bool inVault, int progressMm, int speedMmps)
        {
            if (inVault && state.VaultSeq != vault.Seq)
            {
                state.VaultSeq = vault.Seq;
                state.InVault = true;
                state.VaultClipFrame = UnityEngine.Time.frameCount;
                float air = vault.AirMicros * 1e-6f;
                float elapsed = vault.LegLengthMm > 0 ? air * math.saturate((progressMm - vault.LegStartMm) / (float)vault.LegLengthMm) : 0f;
                view?.BeginVault(vault.Style, elapsed, air);
                return;
            }
            if (!inVault && state.InVault)
            {
                state.InVault = false;
                view?.EndVault(speedMmps * 0.001f);
            }
        }

        static void ShowJump(PlayerPresentationState state, IGhostMotionView view, in MotionPlan plan, long micros, in PlayerMotionCurves curves)
        {
            bool shown = state.JumpSeq != 0;
            bool same = shown && plan.Kind == MotionKind.Jump && plan.PlanSeq == state.JumpSeq;
            if (shown && !same && state.JumpPhase < 2)
            {
                view?.JumpCancelled();
                state.JumpSeq = 0;
                shown = false;
            }
            if (plan.Kind != MotionKind.Jump || micros < 0) return;
            if (!shown || !same)
            {
                if (micros >= curves.LandMicros) return;   // already over when first seen: nothing to play
                state.JumpSeq = plan.PlanSeq;
                state.JumpPhase = 0;
                view?.BeginJump(micros * 1e-6f);
            }
            if (state.JumpPhase < 1 && micros >= curves.TakeoffMicros) { state.JumpPhase = 1; view?.JumpTookOff(); }
            if (state.JumpPhase < 2 && micros >= curves.LandMicros) { state.JumpPhase = 2; view?.JumpLanded(); }
        }
    }
}
