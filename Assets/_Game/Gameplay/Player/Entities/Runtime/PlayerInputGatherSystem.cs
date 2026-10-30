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

        protected override void OnCreate()
        {
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, ClientPlanHeader, ClientPlanCorner, GhostOwnerIsLocal>());
            RequireForUpdate(m_Query);
        }

        // the tick this frame's input is for (the click log only)
        NetworkTick m_Tick;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Tick = SystemAPI.TryGetSingleton<NetworkTime>(out var nt) ? nt.ServerTick : default;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var input = default(PlayerInput);          // events are per frame: start from nothing
                if (Source != null && Source.TryTake(out var cmd))
                {
                    if (cmd.Kind == PlayerCommandKind.Stop)
                        input.Stop.Set();
                    else if (cmd.Kind == PlayerCommandKind.Click)
                        PlanClick(em, e, cmd, ref input);
                }
                em.SetComponentData(e, input);
            }
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

            var pose = em.GetComponentData<PlayerPose>(e);
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                if (!Planner.TryPlan(pose.Mm, cmd.GoalMm, corners) || corners.Length < 2) return;

                var header = em.GetComponentData<ClientPlanHeader>(e);
                header.Seq = NextClientSeq(header.Seq);
                header.Count = (byte)math.min(corners.Length, PlayerLimits.MaxCorners);
                header.Writes++;
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

        /// <summary>Client order numbers run 1..0x7FFF and wrap; 0 means none.</summary>
        public static ushort NextClientSeq(ushort last) => (ushort)(last >= PlayerLimits.ClientSeqMax ? 1 : last + 1);
    }
}
