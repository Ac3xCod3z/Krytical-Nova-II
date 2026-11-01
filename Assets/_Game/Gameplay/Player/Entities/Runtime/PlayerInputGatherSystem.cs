using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    public enum PlayerCommandKind : byte { None, Click, Stop }

    /// <summary>What the player asked for this frame, before it becomes a <see cref="PlayerInput"/>.</summary>
    public struct PlayerCommand
    {
        public PlayerCommandKind Kind;
        public int3 GoalMm;
        public bool Run;
        /// <summary>The click asks to jump to <see cref="GoalMm"/> (M3b).</summary>
        public bool Jump;
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

        public void Jump(int3 goalMm) =>
            m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Click, GoalMm = goalMm, Jump = true });

        public void Stop() => m_Queue.Enqueue(new PlayerCommand { Kind = PlayerCommandKind.Stop });

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

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, ClientPlanHeader, ClientPlanCorner, GhostOwnerIsLocal>());
            RequireForUpdate(m_Query);
        }

        // the tick this frame's input is for
        NetworkTick m_Tick;
        // the jump clip's clock (a jump click in the air or the landing cooldown is not predicted)
        PlayerMotionCurves m_Curves;
        int m_TickMicros;

        // The newest click. Netcode delivers an input EVENT by its count: the server sees it on the first tick whose count is higher,
        // which is a later tick when the click's own tick arrives late or is lost; and on later frames of a partial tick the plain
        // fields are the last frame's. Either way a click came with seq 0 and was refused (owner's GUI tests, 30-31 Oct: "noseq").
        // So every input carries the newest click's fields until the next click; the event itself is set only once. The server
        // plans only on a new event, and the walk ignores a seq it already took.
        readonly Dictionary<Entity, PlayerInput> m_LastClick = new Dictionary<Entity, PlayerInput>();

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Tick = SystemAPI.TryGetSingleton<NetworkTime>(out var nt) ? nt.ServerTick : default;
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
                    else if (cmd.Kind == PlayerCommandKind.Click && cmd.Jump)
                        PlanJumpClick(em, e, cmd, ref input);
                    else if (cmd.Kind == PlayerCommandKind.Click)
                        PlanClick(em, e, cmd, ref input);
                }
                if (input.Click.IsSet) m_LastClick[e] = input;
                else if (m_LastClick.TryGetValue(e, out var last)) CarryClick(ref input, last);
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

            // NGO: a click while the body is over an obstacle is ignored, nothing is sent (the server refuses it too; M3c)
            if (OwnerInVault(em, e)) return;
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
                input.GoalXMm = cmd.GoalMm.x;
                input.GoalYMm = cmd.GoalMm.y;
                input.GoalZMm = cmd.GoalMm.z;
                if (PlayerClickLog.Sink != null && m_Tick.IsValid)
                    PlayerClickLog.Client(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                        header.Seq, m_Tick.TickIndexForValidTick);
            }
            finally { corners.Dispose(); }
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
            if (OwnerInVault(em, e)) return;   // NGO: ignored over an obstacle, nothing sent (M3c)
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
