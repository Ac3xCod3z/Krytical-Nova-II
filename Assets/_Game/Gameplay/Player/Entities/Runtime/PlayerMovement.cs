using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// One tick of a player's walk, as pure integer code. The predicted system calls it on the server and on the owning
    /// client with the same inputs, so the two cannot differ. Burst-compatible: no managed types, no statics, no exceptions.
    ///
    /// Order inside a tick: Stop, then a server order (wins over a click of the same tick), else a click; then, if a walk is
    /// under way, speed (accelerate toward the target, eased near the end), progress (+= speed x tick length, remainder kept),
    /// arrival, and the pose from the polyline.
    /// </summary>
    public static class PlayerMovement
    {
        public const int MicrosPerSecond = 1_000_000;

        /// <summary>Tick length in microseconds for a simulation rate (30 Hz = 33333).</summary>
        public static int TickMicros(int simulationTickRate) => MicrosPerSecond / math.max(1, simulationTickRate);

        /// <summary>Drops the walk but keeps where the player stands (the pose is the truth).</summary>
        public static void CancelWalk(ref MoveState m)
        {
            m.Flags = 0;
            m.SpeedMmps = 0;
            m.ProgressMm = 0;
            m.RemMicro = 0;
            m.DelayMicros = 0;
        }

        /// <summary>The part of a tick before a press at <paramref name="permille"/> of it (clamped to the tick).</summary>
        public static int PressDelayMicros(ushort permille, int tickMicros) => (int)((long)tickMicros * math.min((int)permille, 999) / 1000);

        /// <summary>
        /// Advances the walk by one full tick. <paramref name="serverCorners"/> and <paramref name="clientCorners"/> are the two
        /// corner sources: the server's (valid for <paramref name="plan"/>.Seq) and the owner's own plan (valid for
        /// <paramref name="client"/>.Seq). The walk uses the server's when it carries the walk's order number, else the owner's.
        /// </summary>
        public static void Step(
            ref MoveState m, ref PlayerPose pose,
            in PlayerInput input, in ServerOrder order,
            in PathPlan plan, NativeArray<int3> serverCorners,
            in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros) =>
            Step(ref m, ref pose, in input, in order, in plan, serverCorners, in client, clientCorners, default, default, in stats, in cfg, tickMicros);

        /// <summary>
        /// The walk with its vault (M3c): <paramref name="serverVault"/> belongs to the server's corners, <paramref name="clientVault"/> to
        /// the owner's (each used for its own walk's number). While the progress is on the vault's leg the body moves at the leg's own
        /// speed (no acceleration, no slowdown) and rises on NGO's curve (<see cref="VaultLeg"/>); a click on the leg starts nothing
        /// (NGO ignores a click during a vault; the walk goes on to its destination). Stateless on the leg: the pose is a function of
        /// the progress, so a rollback or a skipped tick never leaves it wrong.
        /// </summary>
        public static void Step(
            ref MoveState m, ref PlayerPose pose,
            in PlayerInput input, in ServerOrder order,
            in PathPlan plan, NativeArray<int3> serverCorners,
            in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in VaultPlan serverVault, in VaultPlan clientVault,
            in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros) =>
            Step(ref m, ref pose, in input, in order, in plan, serverCorners, in client, clientCorners, in serverVault, in clientVault,
                default, default, in stats, in cfg, tickMicros);

        /// <summary>
        /// The walk with its vault and its Waterstride leg (M3c): each plan belongs to its own corners (the server's or the owner's). On
        /// the water leg the body runs at the leg's own speed (no acceleration, no slowing at the end) and turns at the leg's rate;
        /// <see cref="MoveState.Water"/> follows the leg, and stays 1 when the walk ends standing on the water. NGO: on the water a stop
        /// does nothing (the run goes on), and while stranded (out of Flux, carried ashore) a click starts nothing.
        /// </summary>
        public static void Step(
            ref MoveState m, ref PlayerPose pose,
            in PlayerInput input, in ServerOrder order,
            in PathPlan plan, NativeArray<int3> serverCorners,
            in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in VaultPlan serverVault, in VaultPlan clientVault,
            in WaterPlan serverWater, in WaterPlan clientWater,
            in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros)
        {
            PoseTilt tilt = default;
            Step(ref m, ref pose, ref tilt, in input, in order, in plan, serverCorners, in client, clientCorners, in serverVault, in clientVault,
                in serverWater, in clientWater, default, default, default, default, in stats, in cfg, tickMicros);
        }

        /// <summary>
        /// The walk with its vault, its Waterstride leg and its wall walk (M3c, `m3c-wallstride-design.md` section 3). A walk whose order
        /// carries a <see cref="WallPlan"/> moves segment by segment as its <see cref="WallCorner"/>s say: a timed leg takes exactly its time
        /// on NGO's eases, a wall segment runs at the wall's speed, a ground segment by the walk's own rules; the progress is spent across
        /// segments within the tick, so a leg never takes a tick more or less than its time. NGO: a stop does nothing on the wall (the agent
        /// is off), and nothing starts during a timed leg (clicks are ignored while the motor is busy off the wall; a server order waits).
        /// </summary>
        public static void Step(
            ref MoveState m, ref PlayerPose pose, ref PoseTilt tilt,
            in PlayerInput input, in ServerOrder order,
            in PathPlan plan, NativeArray<int3> serverCorners,
            in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in VaultPlan serverVault, in VaultPlan clientVault,
            in WaterPlan serverWater, in WaterPlan clientWater,
            in WallPlan serverWall, NativeArray<WallCorner> serverLegs,
            in WallPlan clientWall, NativeArray<WallCorner> clientLegs,
            in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros)
        {
            // the vault of the walk under way (the server's for its corners, else the owner's own)
            VaultPlan vault = serverVault.Seq != 0 && serverVault.Seq == m.OrderSeq ? serverVault : clientVault;
            bool inVault = (m.Flags & MoveFlags.Moving) != 0 && VaultLeg.On(in vault, m.OrderSeq, m.ProgressMm);
            bool stranded = (m.Flags & MoveFlags.Moving) != 0 && WaterLeg.Stranded(WaterLeg.Of(in serverWater, in clientWater, m.OrderSeq), m.OrderSeq);
            bool inTimedLeg = InTimedWallLeg(in m, in plan, serverCorners, in client, clientCorners, in serverWall, serverLegs, in clientWall, clientLegs);
            if (input.Stop.IsSet && !inVault && !inTimedLeg && m.Water == 0 && m.Wall == 0) CancelWalk(ref m);

            // ---- start of a walk -------------------------------------------------------------------------------------
            if (inVault || inTimedLeg)
            {
                // over the obstacle or in a leg of the wall walk: no new walk until it ends (NGO); a server order waits for it
            }
            else if (order.Seq != 0 && order.Seq != m.ServerSeq)
            {
                // A server order overrides input. It is consumed even when its corners are missing (nothing to walk).
                m.ServerSeq = order.Seq;
                m.OrderSeq = order.Seq;
                CancelWalk(ref m);
                if (plan.Seq == order.Seq && plan.Count >= 2)
                    BeginWalk(ref m, in pose, (order.Flags & OrderFlags.RunPaid) != 0);
            }
            // a jump click is never a walk (a catch's climb corners carry its number, and begin only where the jump meets the wall)
            else if (input.Click.IsSet && input.Jump == 0 && input.ClickSeq != 0 && input.ClickSeq != m.RefusedSeq && input.ClickSeq != m.OrderSeq && !stranded)
            {
                bool havePath = (plan.Seq == input.ClickSeq && plan.Count >= 2) || (client.Seq == input.ClickSeq && client.Count >= 2);
                if (havePath)
                {
                    m.OrderSeq = input.ClickSeq;
                    BeginWalk(ref m, in pose, input.Run != 0);
                    m.DelayMicros = PressDelayMicros(input.PressPermille, tickMicros);   // the walk starts at the press, inside this tick
                }
            }

            if ((m.Flags & MoveFlags.Moving) == 0) return;
            if (WallWalkOf(in m, in plan, serverCorners, in client, clientCorners, in serverWall, serverLegs, in clientWall, clientLegs,
                    out var wallCorners, out int wallCount, out WallPlan wall, out var legs))
            {
                StepWallWalk(ref m, ref pose, ref tilt, wallCorners, wallCount, in wall, legs, in stats, in cfg, tickMicros);
                return;
            }
            // a walk with no wall walk is on the ground, upright
            m.Wall = 0;
            tilt = default;
            if (vault.Seq != m.OrderSeq) vault = serverVault.Seq != 0 && serverVault.Seq == m.OrderSeq ? serverVault : clientVault;
            WaterPlan water = WaterLeg.Of(in serverWater, in clientWater, m.OrderSeq);
            bool hasWater = water.Seq != 0 && water.Seq == m.OrderSeq;

            // ---- the corners this walk follows -----------------------------------------------------------------------
            NativeArray<int3> corners;
            int count;
            if (plan.Seq == m.OrderSeq && plan.Count >= 2) { corners = serverCorners; count = plan.Count; }
            else if (client.Seq == m.OrderSeq && client.Count >= 2) { corners = clientCorners; count = client.Count; }
            else { CancelWalk(ref m); return; }

            int3 start = new int3(m.StartXMm, m.StartYMm, m.StartZMm);
            int length = PlayerPath.LengthMm(start, corners, count);
            if (length <= 0) { CancelWalk(ref m); return; }
            bool starting = m.ProgressMm == 0 && m.RemMicro == 0;
            // a click within the stopping distance starts nothing (the NGO game: the agent is not started for it)
            if (starting && length <= cfg.StopMm) { CancelWalk(ref m); return; }

            // ---- speed -----------------------------------------------------------------------------------------------
            int target = (m.Flags & MoveFlags.Run) != 0 ? stats.RunMmps : stats.WalkMmps;
            target = math.max(0, target);
            int top = target;
            int remaining = math.max(0, length - m.ProgressMm);
            int radius = math.clamp(
                (int)((long)target * cfg.ArrivalSlowdownMs / 1000),
                cfg.ArrivalMinRadiusMm,
                math.max(cfg.ArrivalMinRadiusMm, cfg.ArrivalMaxRadiusMm));
            if (radius > 0 && remaining < radius)
            {
                int tPermille = (int)((long)remaining * 1000 / radius);                  // 0..999
                int ease = (int)PlayerPath.ISqrt((long)tPermille * 1000);               // sqrt(t) in permille
                int factor = cfg.ArrivalMinPermille + (int)((long)(1000 - cfg.ArrivalMinPermille) * ease / 1000);
                target = (int)((long)target * factor / 1000);
            }
            // The NGO game: a new walk (first click or re-click) starts at full speed along the new way (SnapVelocityToPath),
            // and the agent's acceleration is max(acceleration, top speed x accelerationPerSpeed). On a vault leg: the leg's speed.
            bool onLeg = VaultLeg.On(in vault, m.OrderSeq, m.ProgressMm);
            bool onWater = hasWater && WaterLeg.On(in water, m.OrderSeq, m.ProgressMm);
            if (onLeg) m.SpeedMmps = VaultLeg.SpeedMmps(in vault);
            else if (onWater) m.SpeedMmps = water.SpeedMmps;
            else if (starting) m.SpeedMmps = target;
            else
            {
                int accel = math.max(cfg.AccelMmps2, (int)math.min((long)top * cfg.AccelPerSpeed, int.MaxValue / 2));
                int dv = math.max(1, (int)((long)accel * tickMicros / MicrosPerSecond));
                m.SpeedMmps = m.SpeedMmps < target ? math.min(target, m.SpeedMmps + dv) : math.max(target, m.SpeedMmps - dv);
            }

            // ---- progress --------------------------------------------------------------------------------------------
            int spend = math.max(0, tickMicros - m.DelayMicros);   // the first tick of a click: only the part after the press
            m.DelayMicros = 0;
            long acc = (long)m.SpeedMmps * spend + m.RemMicro;
            long step = acc / MicrosPerSecond;
            m.RemMicro = (int)(acc % MicrosPerSecond);
            m.ProgressMm = (int)math.min((long)length, m.ProgressMm + step);

            bool arrived = m.ProgressMm + cfg.StopMm >= length;
            if (arrived) m.ProgressMm = length;   // the NGO game places the body ON the point when it stops (PlaceOn)

            // ---- pose ------------------------------------------------------------------------------------------------
            PlayerPath.PointAt(start, corners, count, m.ProgressMm, out int3 p, out int3 dir);
            int rise = VaultLeg.On(in vault, m.OrderSeq, m.ProgressMm) ? VaultLeg.HeightMm(in vault, m.ProgressMm) : 0;
            pose.XMm = p.x; pose.YMm = p.y + rise; pose.ZMm = p.z;
            int turn = onWater ? TurnStepCdeg(water.TurnCdegPerSecond, tickMicros) : TurnStepCdeg(cfg, tickMicros);
            pose.YawCdeg = TurnToward(pose.YawCdeg, PlayerPath.YawCdegOf(dir, pose.YawCdeg), turn);
            // on the water from the leg's start, ashore at its end; a walk with no water leg is on land
            m.Water = (byte)(hasWater && WaterLeg.On(in water, m.OrderSeq, m.ProgressMm) ? 1 : 0);

            if (arrived) CancelWalk(ref m);
        }

        /// <summary>
        /// The walk under way is in a timed leg of its wall walk (the plant, the top-out, a jump onto or off a wall): nothing starts until it
        /// ends (NGO ignores clicks while the motor is busy off the wall). The server's corners when they carry the walk's number, else the owner's.
        /// </summary>
        public static bool InTimedWallLeg(in MoveState m, in PathPlan plan, NativeArray<int3> serverCorners, in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in WallPlan serverWall, NativeArray<WallCorner> serverLegs, in WallPlan clientWall, NativeArray<WallCorner> clientLegs) =>
            (m.Flags & MoveFlags.Moving) != 0
            && WallWalkOf(in m, in plan, serverCorners, in client, clientCorners, in serverWall, serverLegs, in clientWall, clientLegs,
                out var corners, out int count, out _, out var legs)
            && WallLeg.SegmentAt(new int3(m.StartXMm, m.StartYMm, m.StartZMm), corners, count, m.ProgressMm, out int i, out _, out _, out _, out _)
            && i < legs.Length && legs[i].Micros > 0;

        /// <summary>
        /// The corners and the wall walk of the walk under way: the server's when its corners carry the walk's number, else the owner's own.
        /// False when that walk has no wall walk (or not a leg for every corner).
        /// </summary>
        static bool WallWalkOf(in MoveState m, in PathPlan plan, NativeArray<int3> serverCorners, in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in WallPlan serverWall, NativeArray<WallCorner> serverLegs, in WallPlan clientWall, NativeArray<WallCorner> clientLegs,
            out NativeArray<int3> corners, out int count, out WallPlan wall, out NativeArray<WallCorner> legs)
        {
            corners = default; count = 0; wall = default; legs = default;
            if (plan.Seq == m.OrderSeq && plan.Count >= 2)
            {
                if (!WallLeg.Of(in serverWall, m.OrderSeq) || !serverLegs.IsCreated) return false;
                corners = serverCorners; count = plan.Count; wall = serverWall; legs = serverLegs;
            }
            else if (client.Seq == m.OrderSeq && client.Count >= 2)
            {
                if (!WallLeg.Of(in clientWall, m.OrderSeq) || !clientLegs.IsCreated) return false;
                corners = clientCorners; count = client.Count; wall = clientWall; legs = clientLegs;
            }
            else return false;
            if (!corners.IsCreated) return false;
            count = math.min(count, corners.Length);
            return count >= 2 && legs.Length >= count;
        }

        /// <summary>
        /// One tick of a wall walk (see the full Step with the wall plans):
        /// the tick's time is spent segment by segment at each one's speed, then the pose, the tilt and the wall flag come from the progress.
        /// </summary>
        static void StepWallWalk(ref MoveState m, ref PlayerPose pose, ref PoseTilt tilt, NativeArray<int3> corners, int count,
            in WallPlan wall, NativeArray<WallCorner> legs, in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros)
        {
            m.Water = 0;
            int3 start = new int3(m.StartXMm, m.StartYMm, m.StartZMm);
            int length = PlayerPath.LengthMm(start, corners, count);
            if (length <= 0) { CancelWalk(ref m); return; }
            bool starting = m.ProgressMm == 0 && m.RemMicro == 0;
            int groundTop = math.max(0, (m.Flags & MoveFlags.Run) != 0 ? stats.RunMmps : stats.WalkMmps);
            bool accelerated = false;

            // ---- spend the tick, segment by segment (the first tick of a click: only the part after the press) -------
            long budget = math.max(0, tickMicros - m.DelayMicros);
            m.DelayMicros = 0;
            for (int guard = 0; guard < 2 * PlayerLimits.MaxWallCorners && budget > 0 && m.ProgressMm < length; guard++)
            {
                if (!WallLeg.SegmentAt(start, corners, count, m.ProgressMm, out int idx, out int segStart, out int segLen, out _, out _)) break;
                WallCorner leg = legs[idx];
                int speed;
                if (leg.Micros > 0) speed = WallLeg.TimedSpeedMmps(segLen, leg.Micros);
                else if (WallLeg.OnWall(in wall, idx)) speed = math.max(1, wall.SpeedMmps);
                else
                {
                    // the ground: the walk's own rules up to the end of this stretch of ground (NGO: the agent walks to the wall's foot)
                    int groundEnd = GroundStretchEnd(start, corners, count, in wall, legs, idx, segStart + segLen);
                    int target = ArrivalTarget(groundTop, groundEnd - m.ProgressMm, in cfg);
                    bool entering = starting || (m.ProgressMm == segStart && idx > 1 && (legs[idx - 1].Micros > 0 || WallLeg.OnWall(in wall, idx - 1)));
                    if (entering) speed = target;
                    else if (accelerated) speed = m.SpeedMmps;
                    else
                    {
                        int accel = math.max(cfg.AccelMmps2, (int)math.min((long)groundTop * cfg.AccelPerSpeed, int.MaxValue / 2));
                        int dv = math.max(1, (int)((long)accel * tickMicros / MicrosPerSecond));
                        speed = m.SpeedMmps < target ? math.min(target, m.SpeedMmps + dv) : math.max(target, m.SpeedMmps - dv);
                    }
                    accelerated = true;
                    speed = math.max(1, speed);
                }
                starting = false;
                m.SpeedMmps = speed;
                int segEnd = segStart + segLen;
                long need = (long)(segEnd - m.ProgressMm) * MicrosPerSecond - m.RemMicro;   // micro-millimetres to the segment's end
                long can = (long)speed * budget;
                if (can >= need)
                {
                    budget -= (need + speed - 1) / speed;
                    m.ProgressMm = segEnd;
                    m.RemMicro = 0;
                    continue;
                }
                long acc = can + m.RemMicro;
                m.ProgressMm = (int)math.min((long)length, m.ProgressMm + acc / MicrosPerSecond);
                m.RemMicro = (int)(acc % MicrosPerSecond);
                budget = 0;
            }

            // ---- arrival ---------------------------------------------------------------------------------------------
            WallLeg.SegmentAt(start, corners, count, length, out int lastIdx, out _, out _, out _, out _);
            bool lastOnGround = legs[lastIdx].Micros == 0 && !WallLeg.OnWall(in wall, lastIdx);
            bool arrived = m.ProgressMm >= length || (lastOnGround && m.ProgressMm + cfg.StopMm >= length);
            if (arrived) m.ProgressMm = length;   // on the point, as the walk does

            // ---- pose, tilt, on the wall -----------------------------------------------------------------------------
            WallLeg.SegmentAt(start, corners, count, m.ProgressMm, out int i, out int s0, out int len, out int3 a, out int3 b);
            WallCorner here = legs[i];
            int share = len > 0 ? (int)math.clamp((long)(m.ProgressMm - s0) * 1000 / len, 0, 1000) : 1000;
            bool onWall = WallLeg.OnWall(in wall, i);
            int3 p;
            if (here.Micros > 0) p = WallLeg.TimedPoint(a, b, in here, share);
            else PlayerPath.PointAt(start, corners, count, m.ProgressMm, out p, out _);
            pose.XMm = p.x; pose.YMm = p.y; pose.ZMm = p.z;
            if (here.Micros > 0 || onWall)
            {
                byte phase = here.Phase != WallPhase.None ? here.Phase : (onWall ? WallPhase.WallMove : WallPhase.None);
                tilt = WallLeg.TiltOf(legs[i - 1], here, a, b, onWall, share, phase, pose.YawCdeg);
                tilt.PhaseMillis = PhaseMillis(legs, count, i);
                pose.YawCdeg = WallLeg.YawOf(in tilt, pose.YawCdeg);
            }
            else
            {
                tilt = default;
                pose.YawCdeg = TurnToward(pose.YawCdeg, PlayerPath.YawCdegOf(b - a, pose.YawCdeg), TurnStepCdeg(cfg, tickMicros));
            }
            m.Wall = (byte)(onWall ? 1 : 0);

            if (!arrived) return;
            if ((wall.Flags & WallFlags.EndsOnWall) != 0)
            {
                // standing on the wall at the route's end (NGO WallHold): the tilt stays, Grip goes on
                m.Wall = 1;
                tilt.Phase = WallPhase.WallHold;
            }
            else if ((wall.Flags & WallFlags.EndsPerched) != 0)
            {
                // perched in a tree (NGO EndRoute: upright, facing as the last leg; the perch's crouch plays): off the ground, no Grip
                m.Wall = 2;
                float yaw = math.radians(pose.YawCdeg * 0.01f);
                tilt = new PoseTilt { UpY = 1000, FwdX = (short)math.round(math.sin(yaw) * 1000f), FwdZ = (short)math.round(math.cos(yaw) * 1000f), Phase = WallPhase.Perched };
            }
            else
            {
                m.Wall = 0;
                tilt = default;
            }
            CancelWalk(ref m);
        }

        /// <summary>The time of the run of timed legs with segment <paramref name="i"/>'s phase around it, milliseconds (0 for an untimed segment).</summary>
        static ushort PhaseMillis(NativeArray<WallCorner> legs, int count, int i)
        {
            if (legs[i].Micros <= 0) return 0;
            byte phase = legs[i].Phase;
            long micros = 0;
            for (int j = i; j >= 1 && legs[j].Micros > 0 && legs[j].Phase == phase; j--) micros += legs[j].Micros;
            int next = i + 1;
            for (; next < count && legs[next].Micros > 0 && legs[next].Phase == phase; next++) micros += legs[next].Micros;
            // a route's jump is shown over its whole air time, the fall after its apex included (design 9 item 7: the flip, then the fall loop)
            if (WallPhase.IsRouteJump(phase))
                for (; next < count && legs[next].Micros > 0 && legs[next].Phase == WallPhase.RouteFall; next++) micros += legs[next].Micros;
            return (ushort)math.min(micros / 1000, ushort.MaxValue);
        }

        /// <summary>
        /// The owner's pose drawn <paramref name="elapsedMicros"/> into the next tick on a wall walk (M3c): the same step on copies, so the
        /// eased legs and the tilt are right between ticks too. False when the walk has no wall walk (the ordinary <c>PoseAhead</c> draws it).
        /// </summary>
        public static bool PoseAheadOnWall(in MoveState m, in PlayerPose pose, in PathPlan plan, NativeArray<int3> serverCorners,
            in ClientPlanHeader client, NativeArray<int3> clientCorners, in WallPlan serverWall, NativeArray<WallCorner> serverLegs,
            in WallPlan clientWall, NativeArray<WallCorner> clientLegs, in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros, int elapsedMicros,
            out int3 posMm, out int yawCdeg, out PoseTilt tilt)
        {
            posMm = pose.Mm;
            yawCdeg = pose.YawCdeg;
            tilt = default;
            if ((m.Flags & MoveFlags.Moving) == 0) return false;
            if (!WallWalkOf(in m, in plan, serverCorners, in client, clientCorners, in serverWall, serverLegs, in clientWall, clientLegs,
                    out var corners, out int count, out WallPlan wall, out var legs)) return false;
            var ahead = m;
            var p = pose;
            StepWallWalk(ref ahead, ref p, ref tilt, corners, count, in wall, legs, in stats, in cfg, math.clamp(elapsedMicros, 0, tickMicros));
            posMm = p.Mm;
            yawCdeg = p.YawCdeg;
            return true;
        }

        /// <summary>Where the stretch of ground that segment <paramref name="idx"/> is on ends: the start of the next leg or wall segment, else the path's end.</summary>
        static int GroundStretchEnd(int3 start, NativeArray<int3> corners, int count, in WallPlan wall, NativeArray<WallCorner> legs, int idx, int segEnd)
        {
            int along = segEnd;
            int3 prev = corners[idx];
            for (int j = idx + 1; j < count; j++)
            {
                if (legs[j].Micros > 0 || WallLeg.OnWall(in wall, j)) return along;
                along += PlayerPath.DistanceMm(prev, corners[j]);
                prev = corners[j];
            }
            return along;
        }

        /// <summary>The walk's target speed <paramref name="remainingMm"/> before it ends (NGO's arrival slowing, as the ground walk does).</summary>
        static int ArrivalTarget(int top, int remainingMm, in PlayerMoveSettings cfg)
        {
            int radius = math.clamp(
                (int)((long)top * cfg.ArrivalSlowdownMs / 1000),
                cfg.ArrivalMinRadiusMm,
                math.max(cfg.ArrivalMinRadiusMm, cfg.ArrivalMaxRadiusMm));
            if (radius <= 0 || remainingMm >= radius) return top;
            int tPermille = (int)((long)math.max(0, remainingMm) * 1000 / radius);
            int ease = (int)PlayerPath.ISqrt((long)tPermille * 1000);
            int factor = cfg.ArrivalMinPermille + (int)((long)(1000 - cfg.ArrivalMinPermille) * ease / 1000);
            return (int)((long)top * factor / 1000);
        }

        /// <summary>Hundredths of a degree the body may turn in one tick; 0 = no limit (face the way at once).</summary>
        public static int TurnStepCdeg(in PlayerMoveSettings cfg, int tickMicros) => TurnStepCdeg(cfg.TurnCdegPerSecond, tickMicros);

        /// <summary>Hundredths of a degree a turn rate allows in one tick; 0 = no limit.</summary>
        public static int TurnStepCdeg(int cdegPerSecond, int tickMicros) =>
            cdegPerSecond <= 0 ? 0 : math.max(1, (int)((long)cdegPerSecond * tickMicros / MicrosPerSecond));

        /// <summary>
        /// Turns <paramref name="fromCdeg"/> toward <paramref name="toCdeg"/> the short way by at most <paramref name="maxStepCdeg"/>
        /// (0 = all the way). Integer, 0..35999 in and out.
        /// </summary>
        /// <summary>
        /// A walk click is drawn ahead (<see cref="PoseAheadWithClick"/>) only in the tick it was pressed in (<paramref name="clickTick"/>,
        /// the tick's serialized data; 0 = not stamped: as before). After that tick the prediction has taken it, or a server order has
        /// overridden it (the attack's walk-in, a travel). Drawing it ahead again pulled the body toward the click on every frame and
        /// snapped it back on every tick (owner, 8 Nov 2026: the body and the camera jittered on every attack click).
        /// </summary>
        public static bool ClickPendingAt(uint clickTick, uint nowTick) => clickTick == 0 || nowTick == 0 || clickTick == nowTick;

        public static int TurnToward(int fromCdeg, int toCdeg, int maxStepCdeg)
        {
            int delta = ((toCdeg - fromCdeg) % 36000 + 54000) % 36000 - 18000; // -18000..17999, the short way
            if (maxStepCdeg > 0) delta = math.clamp(delta, -maxStepCdeg, maxStepCdeg);
            int to = (fromCdeg + delta) % 36000;
            return to < 0 ? to + 36000 : to;
        }

        /// <summary>
        /// Where the walk of this tick has got to <paramref name="elapsedMicros"/> into the next tick (0..one tick), for the presentation
        /// of the owner's predicted body between full ticks: the same corners and speed the next <see cref="Step"/> would use, the
        /// progress capped at the stop point, the turn carried on by the same rate. Never compared, never written to the ghost.
        /// False when no walk is under way (the pose is the answer).
        /// </summary>
        public static bool PoseAhead(in MoveState m, in PlayerPose pose,
            in PathPlan plan, NativeArray<int3> serverCorners, in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in PlayerMoveSettings cfg, int tickMicros, int elapsedMicros, out int3 posMm, out int yawCdeg) =>
            PoseAhead(in m, in pose, in plan, serverCorners, in client, clientCorners, default, default, in cfg, tickMicros, elapsedMicros,
                out posMm, out yawCdeg);

        /// <summary>The pose drawn ahead with the walk's vault (M3c): over the leg the body is on NGO's curve at the frame's own progress.</summary>
        public static bool PoseAhead(in MoveState m, in PlayerPose pose,
            in PathPlan plan, NativeArray<int3> serverCorners, in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in VaultPlan serverVault, in VaultPlan clientVault,
            in PlayerMoveSettings cfg, int tickMicros, int elapsedMicros, out int3 posMm, out int yawCdeg)
        {
            posMm = pose.Mm;
            yawCdeg = pose.YawCdeg;
            if ((m.Flags & MoveFlags.Moving) == 0) return false;
            NativeArray<int3> corners;
            int count;
            if (plan.Seq == m.OrderSeq && plan.Count >= 2) { corners = serverCorners; count = plan.Count; }
            else if (client.Seq == m.OrderSeq && client.Count >= 2) { corners = clientCorners; count = client.Count; }
            else return false;
            int3 start = new int3(m.StartXMm, m.StartYMm, m.StartZMm);
            int length = PlayerPath.LengthMm(start, corners, count);
            if (length <= 0) return false;
            int elapsed = math.clamp(elapsedMicros, 0, tickMicros);
            long ahead = (long)m.SpeedMmps * elapsed / MicrosPerSecond;
            int progress = (int)math.min((long)length, m.ProgressMm + ahead);
            if (progress + cfg.StopMm >= length) progress = length;   // the next step stops ON the point (as Step does)
            PlayerPath.PointAt(start, corners, count, progress, out posMm, out int3 dir);
            VaultPlan vault = serverVault.Seq != 0 && serverVault.Seq == m.OrderSeq ? serverVault : clientVault;
            if (VaultLeg.On(in vault, m.OrderSeq, progress)) posMm.y += VaultLeg.HeightMm(in vault, progress);
            int way = PlayerPath.YawCdegOf(dir, pose.YawCdeg);
            int step = TurnStepCdeg(cfg, tickMicros);
            if (step <= 0) yawCdeg = way;
            else
            {
                int turnNow = (int)((long)step * elapsed / math.max(1, tickMicros));
                yawCdeg = turnNow <= 0 ? pose.YawCdeg : TurnToward(pose.YawCdeg, way, turnNow);
            }
            return true;
        }

        /// <summary>
        /// The owner's pose drawn ahead (as <see cref="PoseAhead(in MoveState, in PlayerPose, in PathPlan, NativeArray{int3}, in ClientPlanHeader, NativeArray{int3}, in VaultPlan, in VaultPlan, in PlayerMoveSettings, int, int, out int3, out int)"/>),
        /// and in the tick of a new click that the walk has not taken yet, the new walk as the next step will start it: from the pose along
        /// the owner's own corners at the click's speed, from the press (owner pick P1, 1 Nov: else the drawn target stood still for the
        /// rest of that tick and then jumped a whole tick's run, ~1.5 m at 28 m/s in one frame).
        /// </summary>
        public static bool PoseAheadWithClick(in MoveState m, in PlayerPose pose, in PlayerInput input, in MoveStats stats,
            in PathPlan plan, NativeArray<int3> serverCorners, in ClientPlanHeader client, NativeArray<int3> clientCorners,
            in VaultPlan serverVault, in VaultPlan clientVault,
            in PlayerMoveSettings cfg, int tickMicros, int elapsedMicros, out int3 posMm, out int yawCdeg)
        {
            VaultPlan vault = serverVault.Seq != 0 && serverVault.Seq == m.OrderSeq ? serverVault : clientVault;
            bool busy = (m.Flags & MoveFlags.Moving) != 0 && VaultLeg.On(in vault, m.OrderSeq, m.ProgressMm);
            bool pending = !busy && input.ClickSeq != 0 && input.Jump == 0 && input.Wall == 0 && input.ClickSeq == client.Seq && client.Count >= 2
                && input.ClickSeq != m.OrderSeq && input.ClickSeq != m.RefusedSeq && clientCorners.IsCreated;
            if (!pending)
                return PoseAhead(in m, in pose, in plan, serverCorners, in client, clientCorners, in serverVault, in clientVault, in cfg, tickMicros,
                    elapsedMicros, out posMm, out yawCdeg);
            int speed = math.max(0, input.Run != 0 ? stats.RunMmps : stats.WalkMmps);
            int moving = math.max(0, math.clamp(elapsedMicros, 0, tickMicros) - PressDelayMicros(input.PressPermille, tickMicros));
            int count = math.min((int)client.Count, clientCorners.Length);
            int length = PlayerPath.LengthMm(pose.Mm, clientCorners, count);
            int progress = (int)math.min((long)length, (long)speed * moving / MicrosPerSecond);
            PlayerPath.PointAt(pose.Mm, clientCorners, count, progress, out posMm, out int3 dir);
            yawCdeg = pose.YawCdeg;
            if (progress > 0)
            {
                int way = PlayerPath.YawCdegOf(dir, pose.YawCdeg);
                int step = TurnStepCdeg(cfg, tickMicros);
                yawCdeg = step <= 0 ? way : TurnToward(pose.YawCdeg, way, (int)((long)step * moving / math.max(1, tickMicros)));
            }
            return true;
        }

        /// <summary>
        /// Wallstride's catch (part 4): the climb begins where the jump met the wall, as the walk of the jump's own number from the catch point
        /// (its corners and legs are the climb's, written with the jump). NGO <c>BeginCaught</c>.
        /// </summary>
        public static void BeginCatchClimb(ref MoveState m, int3 catchMm, ushort seq)
        {
            CancelWalk(ref m);
            m.OrderSeq = seq;
            m.Flags = MoveFlags.Moving;
            m.StartXMm = catchMm.x;
            m.StartYMm = catchMm.y;
            m.StartZMm = catchMm.z;
        }

        static void BeginWalk(ref MoveState m, in PlayerPose pose, bool run)
        {
            m.Flags = (byte)(MoveFlags.Moving | (run ? MoveFlags.Run : 0));
            m.ProgressMm = 0;
            m.RemMicro = 0;
            m.StartXMm = pose.XMm;
            m.StartYMm = pose.YMm;
            m.StartZMm = pose.ZMm;
        }
    }
}
