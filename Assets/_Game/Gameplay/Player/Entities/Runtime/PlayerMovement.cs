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
        }

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
            in MoveStats stats, in PlayerMoveSettings cfg, int tickMicros)
        {
            if (input.Stop.IsSet) CancelWalk(ref m);

            // ---- start of a walk -------------------------------------------------------------------------------------
            if (order.Seq != 0 && order.Seq != m.ServerSeq)
            {
                // A server order overrides input. It is consumed even when its corners are missing (nothing to walk).
                m.ServerSeq = order.Seq;
                m.OrderSeq = order.Seq;
                CancelWalk(ref m);
                if (plan.Seq == order.Seq && plan.Count >= 2)
                    BeginWalk(ref m, in pose, (order.Flags & OrderFlags.RunPaid) != 0);
            }
            else if (input.Click.IsSet && input.ClickSeq != 0 && input.ClickSeq != m.RefusedSeq && input.ClickSeq != m.OrderSeq)
            {
                bool havePath = (plan.Seq == input.ClickSeq && plan.Count >= 2) || (client.Seq == input.ClickSeq && client.Count >= 2);
                if (havePath)
                {
                    m.OrderSeq = input.ClickSeq;
                    BeginWalk(ref m, in pose, input.Run != 0);
                }
            }

            if ((m.Flags & MoveFlags.Moving) == 0) return;

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
            // and the agent's acceleration is max(acceleration, top speed x accelerationPerSpeed).
            if (starting) m.SpeedMmps = target;
            else
            {
                int accel = math.max(cfg.AccelMmps2, (int)math.min((long)top * cfg.AccelPerSpeed, int.MaxValue / 2));
                int dv = math.max(1, (int)((long)accel * tickMicros / MicrosPerSecond));
                m.SpeedMmps = m.SpeedMmps < target ? math.min(target, m.SpeedMmps + dv) : math.max(target, m.SpeedMmps - dv);
            }

            // ---- progress --------------------------------------------------------------------------------------------
            long acc = (long)m.SpeedMmps * tickMicros + m.RemMicro;
            long step = acc / MicrosPerSecond;
            m.RemMicro = (int)(acc % MicrosPerSecond);
            m.ProgressMm = (int)math.min((long)length, m.ProgressMm + step);

            bool arrived = m.ProgressMm + cfg.StopMm >= length;
            if (arrived) m.ProgressMm = length;   // the NGO game places the body ON the point when it stops (PlaceOn)

            // ---- pose ------------------------------------------------------------------------------------------------
            PlayerPath.PointAt(start, corners, count, m.ProgressMm, out int3 p, out int3 dir);
            pose.XMm = p.x; pose.YMm = p.y; pose.ZMm = p.z;
            pose.YawCdeg = TurnToward(pose.YawCdeg, PlayerPath.YawCdegOf(dir, pose.YawCdeg), TurnStepCdeg(cfg, tickMicros));

            if (arrived) CancelWalk(ref m);
        }

        /// <summary>Hundredths of a degree the body may turn in one tick; 0 = no limit (face the way at once).</summary>
        public static int TurnStepCdeg(in PlayerMoveSettings cfg, int tickMicros) =>
            cfg.TurnCdegPerSecond <= 0 ? 0 : math.max(1, (int)((long)cfg.TurnCdegPerSecond * tickMicros / MicrosPerSecond));

        /// <summary>
        /// Turns <paramref name="fromCdeg"/> toward <paramref name="toCdeg"/> the short way by at most <paramref name="maxStepCdeg"/>
        /// (0 = all the way). Integer, 0..35999 in and out.
        /// </summary>
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
