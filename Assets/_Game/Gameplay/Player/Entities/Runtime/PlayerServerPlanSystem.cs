using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Server only. Inside the predicted group but before <see cref="PredictedMoveSystem"/>, so a path exists on the tick its
    /// order starts. It never runs on a re-simulation (the server does not re-simulate), so the NavMesh is safe here.
    ///
    /// For a click of this tick: check the range, plan the way again with the server's own planner (the server's corners
    /// override the owner's), fill <see cref="PathCorner"/> and <see cref="PathPlan"/>; or refuse: <see cref="MoveState.RefusedSeq"/>
    /// is set (replicated, so the owner's rollback does not start the order again) and the click is wiped from this tick's input.
    /// For a <see cref="ServerOrderRequest"/>: plan it and write <see cref="ServerOrder"/> with the next server number.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
    [UpdateAfter(typeof(PlayerTeleportSystem))]
    [UpdateBefore(typeof(PredictedMoveSystem))]
    public partial class PlayerServerPlanSystem : SystemBase
    {
        EntityQuery m_Query;
        bool m_WarnedPlanner;

        /// <summary>Set by <see cref="PlayerEntitiesSetup.Install"/>; no silent default.</summary>
        public IPathPlanner Planner { get; set; }

        /// <summary>The price of a running click; null = running is free (tests, a world without the game's skills).</summary>
        public IRunCost RunCost { get; set; }

        /// <summary>Vaults over obstacles on the way (M3c); null = the walk always goes round.</summary>
        public IVaultPlanner VaultPlanner { get; set; }

        /// <summary>Waterstride (M3c): runs out over the water, on it and ashore; null = no click is Waterstride's.</summary>
        public IWaterPlanner WaterPlanner { get; set; }

        /// <summary>Wallstride's wall walk (M3c): onto a wall, along it and off it; null = no click is the wall's.</summary>
        public IWallPlanner WallPlanner { get; set; }

        /// <summary>Clicks planned as the wall walk, and why the last one was refused (tests; the owner is told by its own planner).</summary>
        public int WallWalks { get; private set; }
        public string LastWallRefusal { get; private set; }

        /// <summary>Plain clicks Wallstride took by itself (part 5: over a wall, or up a face while running; tests, the scripted run).</summary>
        public int AutoWalls { get; private set; }

        /// <summary>Clicks planned as Waterstride, and why the last one was refused (tests; the message comes with M8's chat port).</summary>
        public int WaterRuns { get; private set; }
        public string LastWaterRefusal { get; private set; }

        /// <summary>Walks planned with a vault in them (tests, the scripted run).</summary>
        public int Vaults { get; private set; }

        /// <summary>The move-click rate limit; null = no limit (tests, a world without the game's request limits).</summary>
        public IClickBudget ClickBudget { get; set; }

        /// <summary>Told of every click taken (moving ends a channelled buff; feel test item 5); null = nobody.</summary>
        public IClickTaken ClickTaken { get; set; }

        /// <summary>Clicks refused for coming too fast (tests, the status page).</summary>
        public int OverBudget { get; private set; }

        /// <summary>Why the last click was refused for its price (tests; the message to the player comes with M8's chat port).</summary>
        public string LastCostRefusal { get; private set; }

        /// <summary>Clicks refused and requests dropped (tests and the status page read it).</summary>
        public int Refused { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_Query = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerInput, PlayerPose, MoveState>()
                .WithAll<PathPlan, ServerOrder, PathCorner, Simulate>());
            RequireForUpdate(m_Query);
        }

        protected override void OnUpdate()
        {
            var nt = SystemAPI.GetSingleton<NetworkTime>();
            if (!nt.ServerTick.IsValid) return;
            var cfg = SystemAPI.TryGetSingleton<PlayerMoveSettings>(out var set) ? set : PlayerMoveSettings.Default;
            var curves = SystemAPI.TryGetSingleton<PlayerMotionCurves>(out var c) ? c : PlayerMotionCurves.Default;
            int tickMicros = PlayerMovement.TickMicros(SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rate) && rate.SimulationTickRate > 0
                ? rate.SimulationTickRate : 30);
            var em = EntityManager;
            using var entities = m_Query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                var input = em.GetComponentData<PlayerInput>(e);
                ChainVault(em, e, in input);
                if (input.Jump != 0 && input.Click.IsSet && !em.IsComponentEnabled<ServerOrderRequest>(e)) continue;   // PlayerJumpServerSystem's
                bool click = input.Click.IsSet && input.ClickSeq != 0;
                bool request = em.IsComponentEnabled<ServerOrderRequest>(e);
                if (input.Click.IsSet && input.ClickSeq == 0) LogRefusal(em, e, input, nt, "noseq");
                if (!click && !request) continue;
                if (Planner == null)
                {
                    if (!m_WarnedPlanner)
                    {
                        m_WarnedPlanner = true;
                        Debug.LogError("PlayerServerPlanSystem: no IPathPlanner installed (PlayerEntitiesSetup.Install); orders are refused.");
                    }
                    if (click) { LogRefusal(em, e, input, nt, "planner"); Refuse(em, e, input); }
                    if (request) em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    Refused++;
                    continue;
                }

                var pose = em.GetComponentData<PlayerPose>(e);
                if (request && (InVault(em, e) || InWallLeg(em, e))) continue;   // a server order waits for the landing (its request stays on)
                if (request && em.GetComponentData<MoveState>(e).Wall != 0)
                {
                    // NGO: a server walk is not taken on a wall (the agent is off; PointAndClickPlayer.ServerMoveTo has no NavMesh there)
                    em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    Refused++;
                    continue;
                }
                if (request && em.GetComponentData<MoveState>(e).Water != 0)
                {
                    // NGO: a server walk is not taken on the water (PointAndClickPlayer.ServerMoveTo returns false)
                    em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    Refused++;
                    continue;
                }
                if (request)
                {
                    var r = em.GetComponentData<ServerOrderRequest>(e);
                    em.SetComponentEnabled<ServerOrderRequest>(e, false);
                    var order = em.GetComponentData<ServerOrder>(e);
                    ushort seq = NextServerSeq(order.Seq);
                    var goal = new int3(r.GoalXMm, r.GoalYMm, r.GoalZMm);
                    if (TryWritePlan(em, e, pose, goal, seq, (r.Flags & OrderFlags.RunPaid) != 0, out bool truncated))
                    {
                        order.Seq = seq;
                        order.StartTick = nt.ServerTick;
                        order.GoalXMm = goal.x; order.GoalYMm = goal.y; order.GoalZMm = goal.z;
                        order.Flags = (byte)(r.Flags | (truncated ? OrderFlags.Truncated : 0));
                        em.SetComponentData(e, order);
                    }
                    else Refused++;
                    continue;      // a server order wins over a click of the same tick (the move step does the same)
                }

                {
                    var goal = new int3(input.GoalXMm, input.GoalYMm, input.GoalZMm);
                    // too many clicks: refused before any planning, so a flood costs the server no NavMesh work
                    if (ClickBudget != null && !ClickBudget.Allow(e))
                    {
                        OverBudget++;
                        LogRefusal(em, e, input, nt, "budget");
                        Refuse(em, e, input);
                        Refused++;
                        continue;
                    }
                    // NGO: a click while a jump is in the air is refused ("Finish this jump before choosing another destination.")
                    if (em.HasComponent<MotionPlan>(e) && JumpFlight.Active(em.GetComponentData<MotionPlan>(e), curves, nt.ServerTick, tickMicros))
                    {
                        LogRefusal(em, e, input, nt, "airborne");
                        Refuse(em, e, input);
                        Refused++;
                        continue;
                    }
                    // NGO: a click while the body is over an obstacle is ignored; the walk goes on to its destination (M3c)
                    if (InVault(em, e))
                    {
                        LogRefusal(em, e, input, nt, "vaulting");
                        Refuse(em, e, input);
                        Refused++;
                        continue;
                    }
                    // NGO: a click in a leg of the wall walk (the plant, the top-out, a jump off) is ignored (PointAndClickPlayer 685)
                    if (InWallLeg(em, e))
                    {
                        LogRefusal(em, e, input, nt, "wall leg");
                        Refuse(em, e, input);
                        Refused++;
                        continue;
                    }
                    bool tooFar = cfg.MaxClickRangeMm > 0 && PlayerPath.DistanceMm(pose.Mm, goal) > cfg.MaxClickRangeMm;
                    // Wallstride's wall walk: a click on a wall, or any click while on one (NGO ServerWallClick); free of Run Flux
                    if (!tooFar && TryWall(em, e, pose, goal, input, out bool wallRefused))
                    {
                        if (wallRefused)
                        {
                            LogRefusal(em, e, input, nt, "wall: " + LastWallRefusal);
                            Refuse(em, e, input);
                            Refused++;
                        }
                        else
                        {
                            ClickTaken?.Taken(e);
                            if (PlayerClickLog.Sink != null)
                                PlayerClickLog.Server(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                                    input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
                        }
                        continue;
                    }
                    // Waterstride's clicks are planned by its own rules and cost no Run Flux (NGO: the water motor sets the path itself)
                    if (!tooFar && TryWater(em, e, pose, goal, input, out bool waterRefused))
                    {
                        if (waterRefused)
                        {
                            LogRefusal(em, e, input, nt, "water: " + LastWaterRefusal);
                            Refuse(em, e, input);
                            Refused++;
                        }
                        else
                        {
                            ClickTaken?.Taken(e);
                            if (PlayerClickLog.Sink != null)
                                PlayerClickLog.Server(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                                    input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
                        }
                        continue;
                    }
                    // a run is paid per destination, only for a click that will start (the Netcode for GameObjects rule)
                    bool run = input.Run != 0 && RunCost != null;
                    string costReason = null;
                    bool unpaid = run && !RunCost.CanPay(e, out costReason);
                    if (unpaid) LastCostRefusal = costReason;
                    // Wallstride by itself (part 5, NGO TryPlanRoute before the vault): over a wall, or up a face while running; the Run is
                    // paid as for any move (NGO ServerMoveTo), the wall's own Flux by the wall walk and the climb
                    if (!tooFar && !unpaid && TryAutoWall(em, e, pose, goal, input))
                    {
                        if (run) RunCost.Pay(e);
                        ClickTaken?.Taken(e);
                        if (PlayerClickLog.Sink != null)
                            PlayerClickLog.Server(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                                input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
                        continue;
                    }
                    if (tooFar || unpaid || !TryWritePlan(em, e, pose, goal, input.ClickSeq, input.Run != 0, out _))
                    {
                        LogRefusal(em, e, input, nt, tooFar ? "range" : unpaid ? "cost" : "path");
                        Refuse(em, e, input);
                        Refused++;
                        continue;
                    }
                    if (run) RunCost.Pay(e);
                    ClickTaken?.Taken(e);
                    if (PlayerClickLog.Sink != null)
                        PlayerClickLog.Server(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                            input.ClickSeq, nt.ServerTick.TickIndexForValidTick);
                }
            }
        }

        /// <summary>
        /// A click Waterstride takes (true): planned into the corners and <see cref="WaterPlan"/>, or refused (<paramref name="refused"/>).
        /// False when it is not Waterstride's: the ordinary walk plans it.
        /// </summary>
        bool TryWater(EntityManager em, Entity e, in PlayerPose pose, int3 goal, in PlayerInput input, out bool refused)
        {
            refused = false;
            if (WaterPlanner == null || !em.HasComponent<WaterPlan>(e)) return false;
            var move = em.GetComponentData<MoveState>(e);
            bool stranded = (move.Flags & MoveFlags.Moving) != 0 && WaterLeg.Stranded(em.GetComponentData<WaterPlan>(e), move.OrderSeq);
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                var answer = WaterPlanner.Plan(e, pose.Mm, goal, input.Run != 0, move.Water != 0, stranded, corners, out WaterPlan leg, out string reason);
                if (answer == WaterAnswer.NotWater) return false;
                if (answer == WaterAnswer.Refused || corners.Length < 2)
                {
                    LastWaterRefusal = reason ?? "no way";
                    refused = true;
                    return true;
                }
                int count = math.min(corners.Length, PlayerLimits.MaxCorners);
                var buffer = em.GetBuffer<PathCorner>(e);
                buffer.Clear();
                for (int i = 0; i < count; i++) buffer.Add(new PathCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
                em.SetComponentData(e, new PathPlan { Seq = input.ClickSeq, Count = (byte)count });
                leg.Seq = input.ClickSeq;
                em.SetComponentData(e, leg);
                WaterRuns++;
                return true;
            }
            finally { corners.Dispose(); }
        }

        /// <summary>
        /// A click the wall walk takes (true): planned into the corners, <see cref="WallCorner"/>s and <see cref="WallPlan"/>, or refused
        /// (<paramref name="refused"/>). False when it is not the wall's: the ordinary walk plans it.
        /// </summary>
        bool TryWall(EntityManager em, Entity e, in PlayerPose pose, int3 goal, in PlayerInput input, out bool refused)
        {
            refused = false;
            if (WallPlanner == null || !em.HasComponent<WallPlan>(e)) return false;
            var move = em.GetComponentData<MoveState>(e);
            if ((input.Wall & WallClick.Wall) == 0 && move.Wall == 0) return false;
            var tilt = em.HasComponent<PoseTilt>(e) ? em.GetComponentData<PoseTilt>(e) : default;
            var normal = new int3(input.NormalX, input.NormalY, input.NormalZ);
            bool jump = (input.Wall & WallClick.Jump) != 0;
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                var answer = WallPlanner.Plan(e, pose.Mm, pose.YawCdeg, in tilt, goal, normal, input.Run != 0, jump, move.Wall != 0, corners, legs,
                    out WallPlan plan, out string reason);
                if (answer == WallAnswer.NotWall) return false;
                if (answer == WallAnswer.Refused || corners.Length < 2)
                {
                    LastWallRefusal = reason ?? "no way";
                    refused = true;
                    return true;
                }
                WallWrite.Server(em, e, input.ClickSeq, corners, legs, plan);
                if (em.HasComponent<WallGoal>(e))
                    em.SetComponentData(e, (plan.Flags & WallFlags.Truncated) != 0
                        ? new WallGoal { Seq = input.ClickSeq, PointMm = goal, NormalPermille = normal, Run = input.Run != 0, Jump = jump }
                        : default);
                if (em.HasComponent<VaultPlan>(e)) em.SetComponentData(e, new VaultPlan());
                if (em.HasComponent<WaterPlan>(e)) em.SetComponentData(e, new WaterPlan());
                WallWalks++;
                return true;
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>
        /// A plain ground click Wallstride takes by itself (true, part 5): planned as a wall walk on the click's number. False: the vault or
        /// the walk plans it (NGO never refuses here, it tries the next way).
        /// </summary>
        bool TryAutoWall(EntityManager em, Entity e, in PlayerPose pose, int3 goal, in PlayerInput input)
        {
            if (WallPlanner == null || !em.HasComponent<WallPlan>(e)) return false;
            var corners = new NativeList<int3>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            var legs = new NativeList<WallCorner>(PlayerLimits.MaxWallCorners, Allocator.Temp);
            try
            {
                if (WallPlanner.Auto(e, pose.Mm, pose.YawCdeg, goal, input.Run != 0, corners, legs, out WallPlan plan) != WallAnswer.Planned
                    || corners.Length < 2) return false;
                WallWrite.Server(em, e, input.ClickSeq, corners, legs, plan);
                // a route cut at the corner budget goes on from where it stops: the rest is the place, off the wall (the wall walk's own way)
                if (em.HasComponent<WallGoal>(e))
                    em.SetComponentData(e, (plan.Flags & WallFlags.Truncated) != 0
                        ? new WallGoal { Seq = input.ClickSeq, PointMm = goal, Run = input.Run != 0 }
                        : default);
                if (em.HasComponent<VaultPlan>(e)) em.SetComponentData(e, new VaultPlan());
                if (em.HasComponent<WaterPlan>(e)) em.SetComponentData(e, new WaterPlan());
                AutoWalls++;
                return true;
            }
            finally { corners.Dispose(); legs.Dispose(); }
        }

        /// <summary>The walk under way is in a timed leg of its wall walk (the plant, the top-out, a jump onto or off a wall).</summary>
        public static bool InWallLeg(EntityManager em, Entity e)
        {
            if (!em.HasComponent<WallPlan>(e) || !em.HasBuffer<WallCorner>(e) || !em.HasComponent<MoveState>(e)) return false;
            var m = em.GetComponentData<MoveState>(e);
            if ((m.Flags & MoveFlags.Moving) == 0) return false;
            var client = em.HasComponent<ClientPlanHeader>(e) ? em.GetComponentData<ClientPlanHeader>(e) : default;
            var clientWall = em.HasComponent<ClientWallPlan>(e) ? em.GetComponentData<ClientWallPlan>(e).Plan : default;
            NativeArray<int3> clientCorners = em.HasBuffer<ClientPlanCorner>(e) ? em.GetBuffer<ClientPlanCorner>(e).Reinterpret<int3>().AsNativeArray() : default;
            NativeArray<WallCorner> clientLegs = em.HasBuffer<ClientWallCorner>(e) ? em.GetBuffer<ClientWallCorner>(e).Reinterpret<WallCorner>().AsNativeArray() : default;
            return PlayerMovement.InTimedWallLeg(in m, em.GetComponentData<PathPlan>(e), em.GetBuffer<PathCorner>(e).Reinterpret<int3>().AsNativeArray(),
                in client, clientCorners, em.GetComponentData<WallPlan>(e), em.GetBuffer<WallCorner>(e).AsNativeArray(), in clientWall, clientLegs);
        }

        /// <summary>A walk over its vault: where it was going and whether it ran (the way on is planned again at the landing).</summary>
        struct VaultWatch
        {
            public bool Over;
            public int3 Goal;
            public bool Run;
        }

        readonly Dictionary<Entity, VaultWatch> m_VaultWatch = new Dictionary<Entity, VaultWatch>();

        /// <summary>Vaults taken from a landing, on the way on to the same place (tests read it).</summary>
        public int ChainedVaults { get; private set; }

        /// <summary>
        /// Vaults in a row (owner, 2 Nov, item 4: "use those things as the fastest path"; NGO's <c>ResumeMoveAfterVault</c>): when a vault
        /// lands, the way on to the same place is planned again from the landing, and when that finds another vault the walk takes it,
        /// as a server order (the owner predicts it like a click). One vault per plan (one <see cref="VaultPlan"/> leg); the run already
        /// paid stays paid.
        /// </summary>
        void ChainVault(EntityManager em, Entity e, in PlayerInput input)
        {
            if (Planner == null || VaultPlanner == null || !em.HasComponent<VaultPlan>(e)) return;
            bool over = InVault(em, e);
            m_VaultWatch.TryGetValue(e, out VaultWatch watch);
            if (over == watch.Over) return;
            if (over)
            {
                var plan = em.GetComponentData<PathPlan>(e);
                var buffer = em.GetBuffer<PathCorner>(e);
                if (plan.Count < 1 || buffer.Length < plan.Count) return;
                PathCorner last = buffer[plan.Count - 1];
                var m = em.GetComponentData<MoveState>(e);
                bool ordered = (m.OrderSeq & PlayerLimits.ServerSeqBit) != 0;
                bool run = ordered ? (em.GetComponentData<ServerOrder>(e).Flags & OrderFlags.RunPaid) != 0 : input.Run != 0;
                m_VaultWatch[e] = new VaultWatch { Over = true, Goal = new int3(last.XMm, last.YMm, last.ZMm), Run = run };
                return;
            }
            m_VaultWatch.Remove(e);
            var move = em.GetComponentData<MoveState>(e);
            if ((move.Flags & MoveFlags.Moving) == 0 || move.Water != 0 || move.Wall != 0 || em.IsComponentEnabled<ServerOrderRequest>(e)) return;
            int speed = 0;
            if (em.HasComponent<MoveStats>(e))
            {
                var stats = em.GetComponentData<MoveStats>(e);
                speed = watch.Run ? stats.RunMmps : stats.WalkMmps;
            }
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                // only a way on with another vault is ordered: a plain walk on goes on as it is
                if (!VaultPath.Plan(Planner, VaultPlanner, e, em.GetComponentData<PlayerPose>(e).Mm, watch.Goal, speed, corners, out VaultPlan next)
                    || next.LegLengthMm <= 0) return;
            }
            finally { corners.Dispose(); }
            PlayerEntitiesSetup.IssueOrder(em, e, watch.Goal, runPaid: watch.Run);
            ChainedVaults++;
        }

        /// <summary>The walk under way is on its vault's leg (over the obstacle).</summary>
        public static bool InVault(EntityManager em, Entity e)
        {
            if (!em.HasComponent<VaultPlan>(e)) return false;
            var m = em.GetComponentData<MoveState>(e);
            return (m.Flags & MoveFlags.Moving) != 0 && VaultLeg.On(em.GetComponentData<VaultPlan>(e), m.OrderSeq, m.ProgressMm);
        }

        bool TryWritePlan(EntityManager em, Entity e, in PlayerPose pose, int3 goal, ushort seq, bool run, out bool truncated)
        {
            truncated = false;
            var corners = new NativeList<int3>(PlayerLimits.MaxCorners, Allocator.Temp);
            try
            {
                // the speed the walk reaches a take-off at sets a vault's air time (the click's own Run choice)
                int speed = 0;
                if (em.HasComponent<MoveStats>(e))
                {
                    var stats = em.GetComponentData<MoveStats>(e);
                    speed = run ? stats.RunMmps : stats.WalkMmps;
                }
                if (!VaultPath.Plan(Planner, em.HasComponent<VaultPlan>(e) ? VaultPlanner : null, e, pose.Mm, goal, speed, corners, out VaultPlan vault)
                    || corners.Length < 2) return false;
                if (em.HasComponent<VaultPlan>(e))
                {
                    if (vault.LegLengthMm > 0) { vault.Seq = seq; Vaults++; }
                    em.SetComponentData(e, vault);
                }
                if (em.HasComponent<WallPlan>(e)) em.SetComponentData(e, new WallPlan());
                int count = math.min(corners.Length, PlayerLimits.MaxCorners);
                truncated = corners.Length > PlayerLimits.MaxCorners;
                var buffer = em.GetBuffer<PathCorner>(e);
                buffer.Clear();
                for (int i = 0; i < count; i++)
                    buffer.Add(new PathCorner { XMm = corners[i].x, YMm = corners[i].y, ZMm = corners[i].z });
                em.SetComponentData(e, new PathPlan { Seq = seq, Count = (byte)count });
                return true;
            }
            finally { corners.Dispose(); }
        }

        static void LogRefusal(EntityManager em, Entity e, in PlayerInput input, in NetworkTime nt, string why)
        {
            if (PlayerClickLog.Sink == null) return;
            PlayerClickLog.Refused(em.HasComponent<GhostOwner>(e) ? em.GetComponentData<GhostOwner>(e).NetworkId : 0,
                input.ClickSeq, nt.ServerTick.TickIndexForValidTick, why);
        }

        static void Refuse(EntityManager em, Entity e, PlayerInput input)
        {
            var m = em.GetComponentData<MoveState>(e);
            m.RefusedSeq = input.ClickSeq;
            em.SetComponentData(e, m);
            input.Click = default;      // this tick only: the next tick's input is copied in again
            em.SetComponentData(e, input);
        }

        /// <summary>Server order numbers carry the high bit and never use 0x8000 (that would read as 0 in the low bits).</summary>
        public static ushort NextServerSeq(ushort last)
        {
            int low = (last & PlayerLimits.ClientSeqMax) + 1;
            if (low > PlayerLimits.ClientSeqMax) low = 1;
            return (ushort)(PlayerLimits.ServerSeqBit | low);
        }
    }
}
