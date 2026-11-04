using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    public enum PlayerCommandKind : byte { None, Click, Stop, Attack, StopAttack }

    /// <summary>What the player asked for this frame, before it becomes a <see cref="PlayerInput"/>.</summary>
    public struct PlayerCommand
    {
        public PlayerCommandKind Kind;
        public int3 GoalMm;
        public bool Run;
        /// <summary>The click asks to jump to <see cref="GoalMm"/> (M3b).</summary>
        public bool Jump;
        /// <summary>A click on a wall for Wallstride's wall walk (M3c), with the surface's normal in permille.</summary>
        public bool Wall;
        public int3 NormalPermille;
        /// <summary>The attacked ghost of an <see cref="PlayerCommandKind.Attack"/> (M4.4d).</summary>
        public int TargetGhostId;
    }

    /// <summary>
    /// Where commands come from: the mouse and key reader of the game (later wiring), a bot, or a test script. One command a
    /// frame at most. Implemented by the layer that knows the camera and the input devices; this assembly knows neither.
    /// </summary>
    public interface IPlayerCommandSource
    {
        bool TryTake(out PlayerCommand command);
    }

    /// <summary>A queue of commands filled in code (tests, dev bots). Plain C#.</summary>
    public sealed class ScriptedCommandSource : IPlayerCommandSource
    {
        readonly Queue<PlayerCommand> m_Queue = new Queue<PlayerCommand>();

        public int Pending => m_Queue.Count;

        public void Click(int3 goalMm, bool run = false) =>
            m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Click, GoalMm = goalMm, Run = run });

        /// <summary>A click on a wall for the wall walk (M3c), on a surface facing <paramref name="normalPermille"/>.</summary>
        public void Wall(int3 goalMm, int3 normalPermille, bool run = true, bool jump = false) =>
            m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Click, GoalMm = goalMm, Run = run, Jump = jump, Wall = true, NormalPermille = normalPermille });

        public void Jump(int3 goalMm) =>
            m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Click, GoalMm = goalMm, Jump = true });

        public void Stop() => m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Stop });

        /// <summary>The second click on a hostile (M4.4d): attack the ghost <paramref name="ghostId"/>.</summary>
        public void Attack(int ghostId) => m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Attack, TargetGhostId = ghostId });

        public void StopAttack() => m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.StopAttack });

        public void Clear() => m_Queue.Clear();

        public bool TryTake(out PlayerCommand command)
        {
            if (m_Queue.Count > 0) { command = m_Queue.Dequeue(); return true; }
            command = default;
            return false;
        }
    }

    /// <summary>
    /// Client only, in the input-gather group. Turns the owner's command into <see cref="PlayerInput"/> and, for a click, plans the
    /// path ONCE (outside rollback, with the installed <see cref="IPathPlanner"/>) into <see cref="ClientPlanCorner"/>. The plan is
    /// client-only data, so snapshot rollbacks leave it alone and the predicted loop only reads it. A click that cannot be
    /// planned is not sent (as the old local walk did).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(GhostInputSystemGroup))]
    public partial class PlayerInputGatherSystem : SystemBase
    {
        EntityQuery m_Query;
        bool m_WarnedPlanner;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>. Without it the system does nothing.</summary>
        public IPlayerCommandSource Source { get; set; }

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>. No silent default: a click without a planner is dropped with one error.</summary>
        public IPathPlanner Planner { get; set; }

        /// <summary>The owner's jump planner (the client's NavMesh and the rank's rules); null = a jump click is sent unpredicted.</summary>
        public IJumpPlanner JumpPlanner { get; set; }

        /// <summary>The owner's vault planner (M3c): its own walk goes over the obstacle the server's will; null = it walks round.</summary>
        public IVaultPlanner VaultPlanner { get; set; }

        /// <summary>The owner's Waterstride planner (M3c): its own run over the water is the one the server's will be; null = none.</summary>
        public IWaterPlanner WaterPlanner { get; set; }

        /// <summary>The owner's wall walk planner (M3c): its own way on the wall is the one the server's will be; null = no wall clicks.</summary>
        public IWallPlanner WallPlanner { get; set; }

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, ClientPlanHeader, ClientPlanCorner, GhostOwnerIsLocal>());
            RequireForUpdate(m_Query);
        }

        // the tick this frame's input is for, and how far into it this frame is
        NetworkTick m_Tick;
        ushort m_PressPermille;
        // the jump clip's clock (a jump click in the air or the landing cooldown is not predicted)
        PlayerMotionCurves m_Curves;
        int m_TickMicros;

        // The newest click. Netcode delivers an input EVENT by its count: the server sees it on the first tick whose count is higher,
        // which is a later tick when the click's own tick arrives late or is lost; and on later frames of a partial tick the plain
        // fields are the last frame's. Either way a click came with seq 0 and was refused (owner's GUI tests, 30-31 Oct: "noseq").
        // So every input carries the newest click's fields until the next click; the event itself is set only once. The server
        // plans only on a new event, and the walk ignores a seq it already took.
        readonly Dictionary<Entity, PlayerInput> m_LastClick = new Dictionary<Entity, PlayerInput>();
        // The newest attack's target, carried on every input for the same reason (M4.4d).
        readonly Dictionary<Entity, int> m_LastAttack = new Dictionary<Entity, int>();

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Tick = SystemAPI.TryGetSingleton<NetworkTime>(out var nt) ? nt.ServerTick : default;
            // how far into the tick being gathered the press is (the walk starts there; owner pick P1, 1 Nov). A frame that completes its
            // tick (fraction 1) predicts that whole tick at once: its press is at the tick's end, not its start (else the first frame ran a
            // whole tick, 0.93 m at 28 m/s: gui-tree-2's last "path jumped", PoseAheadContinuityTests at the GUI's frames)
            m_PressPermille = !nt.ServerTick.IsValid || nt.ServerTickFraction <= 0f ? (ushort)0
                : (ushort)math.clamp((int)(nt.ServerTickFraction * 1000f), 0, 999);
            m_Curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var curves) ? curves : PlayerMotionCurves.Default;
            m_TickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var input = default(PlayerInput);          // events are per frame: start from nothing
                if (Source != null && Source.TryTake(out var cmd))
                {
                    if (cmd.Kind == PlayerCommandKind.Stop)
                        input.Stop.Set();
                    else if (cmd.Kind == PlayerCommandKind.Attack)
                    {
                        input.Attack.Set();
                        m_LastAttack[e] = cmd.TargetGhostId;
                    }
                    else if (cmd.Kind == PlayerCommandKind.StopAttack)
                        input.StopAttack.Set();
                    else if (cmd.Kind == PlayerCommandKind.Click && WallPlanner != null && (cmd.Wall || OwnerOnWall(em, e)))
                        PlanWallClick(em, e, cmd, ref input);
                    else if (cmd.Kind == PlayerCommandKind.Click && cmd.Jump)
                        PlanJumpClick(em, e, cmd, ref input);
                    else if (cmd.Kind == PlayerCommandKind.Click)
                        PlanClick(em, e, cmd, ref input);
                }
                if (input.Click.IsSet) m_LastClick[e] = input;
                else if (m_LastClick.TryGetValue(e, out var last)) CarryClick(ref input, last);
                if (m_LastAttack.TryGetValue(e, out int attacked)) input.AttackGhostId = attacked;
                em.SetComponentData(e, input);
            }
        }

        /// <summary>The newest click's fields on a later input; the event itself is not set again (Netcode already counts it).</summary>
        public static void CarryClick(ref PlayerInput input, in PlayerInput click)
        {
            input.ClickSeq = click.ClickSeq;
            input.Run = click.Run;
            input.Jump = click.Jump;
            input.ClickTick = click.ClickTick;
            input.PressPermille = click.PressPermille;
            input.Wall = click.Wall;
            input.NormalX = click.NormalX; input.NormalY = click.NormalY; input.NormalZ = click.NormalZ;
            input.GoalXMm = click.GoalXMm;
            input.GoalYMm = click.GoalYMm;
            input.GoalZMm = click.GoalZMm;
        }

        void PlanClick(EntityManager em, Entity e, in PlayerCommand cmd, ref PlayerInput input)
        {
            if (Planner == null)
            {
                if (!m_WarnedPlanner)
                {
                    m_WarnedPlanner = true;
                    Debug.LogError("PlayerInputGatherSystem: no IPathPlanner installed (PlayerEntitiesSetup.Install); clicks are dropped.");
                }
                return;
            }

            // NGO: a click while the body is over an obstacle or in a leg of the wall walk is ignored, nothing is sent (the server refuses it too; M3c)
            if (OwnerInVault(em, e) || PlayerServerPlanSystem.InWallLeg(em, e)) return;
            var pose = em.GetComponentData<PlayerPose>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                // Waterstride's click (M3c): the owner plans it by the same rules as the server; a refused one is not sent (NGO)
                WaterPlan water = default;
                WaterAnswer waterAnswer = WaterAnswer.NotWater;
                if (WaterPlanner != null && em.HasComponent<ClientWaterPlan>(e) && em.HasComponent<MoveState>(e))
                {
                    var move = em.GetComponentData<MoveState>(e);
                    WaterPlan flown = WaterLeg.Of(em.HasComponent<WaterPlan>(e) ? em.GetComponentData<WaterPlan>(e) : default,
                        em.GetComponentData<ClientWaterPlan>(e).Plan, move.OrderSeq);
                    bool stranded = (move.Flags & MoveFlags.Moving) != 0 && WaterLeg.Stranded(flown, move.OrderSeq);
                    waterAnswer = WaterPlanner.Plan(e, pose.Mm, cmd.GoalMm, cmd.Run, move.Water != 0, stranded, corners, out water, out string why);
                    if (waterAnswer == WaterAnswer.Refused || (waterAnswer == WaterAnswer.Planned && corners.Length < 2))
                    {
                        if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWater] the owner refused a water click: " + why);
                        return;
                    }
                }
                // Wallstride by itself (part 5, NGO TryPlanRoute before the vault): the same question the server asks of the same click
                if (waterAnswer != WaterAnswer.Planned && PlanAutoWallClick(em, e, cmd, in pose, ref input)) return;
                int speed = 0;
                if (em.HasComponent<MoveStats>(e))
                {
                    var stats = em.GetComponentData<MoveStats>(e);
                    speed = cmd.Run ? stats.RunMmps : stats.WalkMmps;
                }
                IVaultPlanner vaults = em.HasComponent<ClientVaultPlan>(e) ? VaultPlanner : null;
                VaultPlan vault = default;
                if (waterAnswer != WaterAnswer.Planned
                    && (!VaultPath.Plan(Planner, vaults, e, pose.Mm, cmd.GoalMm, speed, corners, out vault) || corners.Length < 2)) return;

                var header = em.GetComponentData<ClientPlanHeader>(e);
                header.Seq = NextClientSeq(header.Seq);
                header.Count = (byte)math.min(corners.Length, PlayerLimits.MaxCorners);
                header.Writes++;
                if (em.HasComponent<ClientVaultPlan>(e))
                {
                    if (vault.LegLengthMm > 0) vault.Seq = header.Seq;
                    em.SetComponentData(e, new ClientVaultPlan { Plan = vault });
                }
                if (em.HasComponent<ClientWaterPlan>(e))
                {
                    water.Seq = waterAnswer == WaterAnswer.Planned ? header.Seq : (ushort)0;
                    em.SetComponentData(e, new ClientWaterPlan { Plan = water });
                }
                var buffer = em.GetBuffer<ClientPlanCorner>(e);
                buffer.Clear();
                for (int i = 0; i < header.Count; i++)
                    buffer.Add(new ClientPlanCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
                em.SetComponentData(e, header);

                input.Click.Set();
                input.ClickSeq = header.Seq;
                input.Run = (byte)(cmd.Run ? 1 : 0);
                input.PressPermille = m_PressPermille;
                input.GoalXMm = cmd.GoalMm.x;
                input.GoalYMm = cmd.GoalMm.y;
                input.GoalZMm = cmd.GoalMm.z;
                if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                    PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        header.Seq, m_Tick.TickIndexForValidTick);
            }
            finally { corners.Dispose(); }
        }

        /// <summary>
        /// A plain click Wallstride takes by itself (part 5): over a wall by the zone's graph, or up a face while running. The owner plans it
        /// as its own wall walk until the server's arrives; the input is a plain click (<see cref="PlayerInput.Wall"/> 0), as the server asks
        /// the same question itself. False: the vault or the walk plans it.
        /// </summary>
        bool PlanAutoWallClick(EntityManager em, Entity e, in PlayerCommand cmd, in PlayerPose pose, ref PlayerInput input)
        {
            if (WallPlanner == null || !em.HasComponent<ClientWallPlan>(e)) return false;
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                if (WallPlanner.Auto(e, pose.Mm, pose.YawCdeg, cmd.GoalMm, cmd.Run, corners, legs, out WallPlan wall) != WallAnswer.Planned
                    || corners.Length < 2) return false;
                var header = em.GetComponentData<ClientPlanHeader>(e);
                header.Seq = NextClientSeq(header.Seq);
                header.Count = (byte)WallWrite.Client(em, e, header.Seq, corners, legs, wall);
                header.Writes++;
                em.SetComponentData(e, header);
                if (em.HasComponent<ClientVaultPlan>(e)) em.SetComponentData(e, new ClientVaultPlan());
                if (em.HasComponent<ClientWaterPlan>(e)) em.SetComponentData(e, new ClientWaterPlan());

                input.Click.Set();
                input.ClickSeq = header.Seq;
                input.Run = (byte)(cmd.Run ? 1 : 0);
                input.PressPermille = m_PressPermille;
                input.GoalXMm = cmd.GoalMm.x;
                input.GoalYMm = cmd.GoalMm.y;
                input.GoalZMm = cmd.GoalMm.z;
                if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                    PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        header.Seq, m_Tick.TickIndexForValidTick);
                return true;
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>The owner's body stands or moves on a wall (NGO: every click on a wall is the wall's, PointAndClickPlayer 695).</summary>
        static bool OwnerOnWall(EntityManager em, Entity e) => em.HasComponent<MoveState>(e) && em.GetComponentData<MoveState>(e).Wall != 0;

        /// <summary>
        /// A click of Wallstride's wall walk (M3c): the owner plans it by the same rules as the server, with its own body; a refused one is
        /// not sent (shown on the body in NGO's words by the planner); a planned one is its own walk until the server's arrives. Not the
        /// wall's after all (no node near it and not on a wall): the ordinary click or jump.
        /// </summary>
        void PlanWallClick(EntityManager em, Entity e, in PlayerCommand cmd, ref PlayerInput input)
        {
            if (OwnerInVault(em, e) || PlayerServerPlanSystem.InWallLeg(em, e) || !em.HasComponent<ClientWallPlan>(e)) return;
            var pose = em.GetComponentData<PlayerPose>(e);
            var move = em.GetComponentData<MoveState>(e);
            var tilt = em.HasComponent<PoseTilt>(e) ? em.GetComponentData<PoseTilt>(e) : default;
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                WallAnswer answer = WallPlanner.Plan(e, pose.Mm, pose.YawCdeg, in tilt, cmd.GoalMm, cmd.NormalPermille, cmd.Run, cmd.Jump, move.Wall != 0,
                    corners, legs, out WallPlan wall, out string why);
                if (answer == WallAnswer.NotWall)
                {
                    // Jump selected at a wall the graph does not cover: Wallstride's catch (part 4), else the ordinary jump
                    if (cmd.Jump && PlanCatchClick(em, e, cmd, ref input)) return;
                    if (cmd.Jump) PlanJumpClick(em, e, cmd, ref input);
                    else PlanClick(em, e, cmd, ref input);
                    return;
                }
                if (answer == WallAnswer.Refused || corners.Length < 2)
                {
                    if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWall] the owner refused a wall click: " + why);
                    return;
                }
                var header = em.GetComponentData<ClientPlanHeader>(e);
                header.Seq = NextClientSeq(header.Seq);
                header.Count = (byte)WallWrite.Client(em, e, header.Seq, corners, legs, wall);
                header.Writes++;
                em.SetComponentData(e, header);
                if (em.HasComponent<ClientVaultPlan>(e)) em.SetComponentData(e, new ClientVaultPlan());
                if (em.HasComponent<ClientWaterPlan>(e)) em.SetComponentData(e, new ClientWaterPlan());

                input.Click.Set();
                input.ClickSeq = header.Seq;
                input.Run = (byte)(cmd.Run ? 1 : 0);
                input.Jump = 0;
                input.PressPermille = m_PressPermille;
                input.Wall = (byte)(WallClick.Wall | (cmd.Jump ? WallClick.Jump : 0));
                input.NormalX = (short)cmd.NormalPermille.x; input.NormalY = (short)cmd.NormalPermille.y; input.NormalZ = (short)cmd.NormalPermille.z;
                input.GoalXMm = cmd.GoalMm.x;
                input.GoalYMm = cmd.GoalMm.y;
                input.GoalZMm = cmd.GoalMm.z;
                if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                    PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        header.Seq, m_Tick.TickIndexForValidTick);
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>The owner's predicted walk is on its vault's leg (the server's vault for the walk's number, else the owner's own).</summary>
        static bool OwnerInVault(EntityManager em, Entity e)
        {
            if (!em.HasComponent<MoveState>(e)) return false;
            var m = em.GetComponentData<MoveState>(e);
            if ((m.Flags & MoveFlags.Moving) == 0) return false;
            if (em.HasComponent<VaultPlan>(e) && VaultLeg.On(em.GetComponentData<VaultPlan>(e), m.OrderSeq, m.ProgressMm)) return true;
            return em.HasComponent<ClientVaultPlan>(e) && VaultLeg.On(em.GetComponentData<ClientVaultPlan>(e).Plan, m.OrderSeq, m.ProgressMm);
        }

        /// <summary>
        /// A jump click (owner answer Q1 a: predicted on the press). It takes the next click number, with no walk corners (the walk
        /// never starts from it), and the owner plans the jump at once into <see cref="ClientMotionPlan"/> on the tick of the press;
        /// the server plans it again and its <see cref="MotionPlan"/> with the same number replaces the owner's.
        /// </summary>
        void PlanJumpClick(EntityManager em, Entity e, in PlayerCommand cmd, ref PlayerInput input)
        {
            if (OwnerInVault(em, e) || PlayerServerPlanSystem.InWallLeg(em, e)) return;   // NGO: ignored over an obstacle or in a wall leg, nothing sent (M3c)
            var header = em.GetComponentData<ClientPlanHeader>(e);
            header.Seq = NextClientSeq(header.Seq);
            header.Count = 0;
            header.Writes++;
            em.SetComponentData(e, header);

            input.Click.Set();
            input.ClickSeq = header.Seq;
            input.Run = 0;
            input.Jump = 1;
            input.ClickTick = m_Tick.IsValid ? m_Tick.SerializedData : 0u;   // the owner's plan starts on this tick
            input.GoalXMm = cmd.GoalMm.x;
            input.GoalYMm = cmd.GoalMm.y;
            input.GoalZMm = cmd.GoalMm.z;

            var pose = em.GetComponentData<PlayerPose>(e);
            string why = JumpPlanner == null ? "no jump planner on the client" : !m_Tick.IsValid ? "no tick yet"
                : !em.HasComponent<ClientMotionPlan>(e) ? "the ghost has no ClientMotionPlan" : Busy(em, e) ? "still in a jump or its cooldown" : null;
            if (why == null && JumpPlanner.TryPlan(e, pose.Mm, cmd.GoalMm, out int3 landing, out int apexY, out why))
            {
                em.SetComponentData(e, new ClientMotionPlan
                {
                    Plan = JumpFlight.NewJump(header.Seq, m_Tick, pose.Mm, landing, apexY),
                });
            }
            else if (PlayerClickLog.Sink != null)
                PlayerClickLog.Sink("[KnJump] the owner did not predict jump click " + header.Seq + ": " + why + " (it waits for the server's plan)");
            if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                    header.Seq, m_Tick.TickIndexForValidTick);
        }

        /// <summary>
        /// Wallstride's catch (part 4): a jump at a wall the graph does not cover, planned by the same rules as the server with the own body.
        /// A refusal is shown on the body in NGO's words and nothing is sent; a catch is sent as the jump it is (its climb under the same
        /// number) and flown from the press, as every jump. False: not a climbable surface (the ordinary jump takes the click).
        /// </summary>
        bool PlanCatchClick(EntityManager em, Entity e, in PlayerCommand cmd, ref PlayerInput input)
        {
            if (!em.HasComponent<ClientMotionPlan>(e) || !em.HasComponent<ClientWallPlan>(e)) return false;
            var pose = em.GetComponentData<PlayerPose>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                WallAnswer answer = WallPlanner.Catch(e, pose.Mm, pose.YawCdeg, cmd.GoalMm, cmd.NormalPermille, cmd.Run, corners, legs,
                    out WallPlan wall, out int3 catchMm, out int apexY, out int catchMicros, out string why);
                if (answer == WallAnswer.NotWall) return false;
                if (answer == WallAnswer.Refused || corners.Length < 2)
                {
                    if (PlayerClickLog.Sink != null) PlayerClickLog.Sink("[KnWall] the owner refused a jump at a wall: " + why);
                    return true;
                }
                var header = em.GetComponentData<ClientPlanHeader>(e);
                header.Seq = NextClientSeq(header.Seq);
                header.Count = (byte)WallWrite.Client(em, e, header.Seq, corners, legs, wall);
                header.Writes++;
                em.SetComponentData(e, header);
                if (em.HasComponent<ClientVaultPlan>(e)) em.SetComponentData(e, new ClientVaultPlan());
                if (em.HasComponent<ClientWaterPlan>(e)) em.SetComponentData(e, new ClientWaterPlan());

                input.Click.Set();
                input.ClickSeq = header.Seq;
                input.Run = (byte)(cmd.Run ? 1 : 0);
                input.Jump = 1;
                input.Wall = (byte)(WallClick.Wall | WallClick.Jump);
                input.NormalX = (short)cmd.NormalPermille.x; input.NormalY = (short)cmd.NormalPermille.y; input.NormalZ = (short)cmd.NormalPermille.z;
                input.ClickTick = m_Tick.IsValid ? m_Tick.SerializedData : 0u;   // the owner's plan starts on this tick
                input.PressPermille = m_PressPermille;
                input.GoalXMm = cmd.GoalMm.x;
                input.GoalYMm = cmd.GoalMm.y;
                input.GoalZMm = cmd.GoalMm.z;
                // flown from the press unless the server will refuse it for a jump still under way (sent all the same, as a jump click)
                if (m_Tick.IsValid && !Busy(em, e))
                {
                    MotionPlan jump = JumpFlight.NewJump(header.Seq, m_Tick, pose.Mm, catchMm, apexY);
                    jump.Flags |= MotionFlags.Catch;
                    jump.CatchMicros = catchMicros;
                    em.SetComponentData(e, new ClientMotionPlan { Plan = jump });
                }
                if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                    PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        header.Seq, m_Tick.TickIndexForValidTick);
                return true;
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>
        /// The owner is still in a jump or its landing cooldown: the server will refuse this jump click (NGO's rules), so it is sent
        /// but not flown; a new plan from the air would fly from mid-air until the refusal came back.
        /// </summary>
        bool Busy(EntityManager em, Entity e)
        {
            if (!em.HasComponent<MotionPlan>(e)) return false;
            var flown = em.GetComponentData<MotionPlan>(e);
            if (em.HasComponent<MoveState>(e))
                flown = PlayerMotionSystem.Choose(flown, em.GetComponentData<ClientMotionPlan>(e).Plan, em.GetComponentData<MoveState>(e).RefusedSeq);
            return JumpFlight.Active(flown, m_Curves, m_Tick, m_TickMicros) || JumpFlight.CoolingDown(flown, m_Curves, m_Tick, m_TickMicros);
        }

        /// <summary>Client order numbers run 1..0x7FFF and wrap; 0 means none.</summary>
        public static ushort NextClientSeq(ushort last) => (ushort)(last >= PlayerLimits.ClientSeqMax ? 1 : last + 1);
    }
}
