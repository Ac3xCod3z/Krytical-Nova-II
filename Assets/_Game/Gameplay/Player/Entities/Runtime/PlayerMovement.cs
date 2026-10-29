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

            // ---- speed -----------------------------------------------------------------------------------------------
            int target = (m.Flags & MoveFlags.Run) != 0 ? stats.RunMmps : stats.WalkMmps;
            target = math.max(0, target);
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
            int accel = math.max(cfg.AccelMmps2, (int)math.min((long)m.SpeedMmps * cfg.AccelPerSpeed, int.MaxValue / 2));
            int dv = math.max(1, (int)((long)accel * tickMicros / MicrosPerSecond));
            m.SpeedMmps = m.SpeedMmps < target ? math.min(target, m.SpeedMmps + dv) : math.max(target, m.SpeedMmps - dv);

            // ---- progress --------------------------------------------------------------------------------------------
            long acc = (long)m.SpeedMmps * tickMicros + m.RemMicro;
            long step = acc / MicrosPerSecond;
            m.RemMicro = (int)(acc % MicrosPerSecond);
            m.ProgressMm = (int)math.min((long)length, m.ProgressMm + step);

            bool arrived = m.ProgressMm + cfg.StopMm >= length;

            // ---- pose ------------------------------------------------------------------------------------------------
            PlayerPath.PointAt(start, corners, count, m.ProgressMm, out int3 p, out int3 dir);
            pose.XMm = p.x; pose.YMm = p.y; pose.ZMm = p.z;
            pose.YawCdeg = PlayerPath.YawCdegOf(dir, pose.YawCdeg);

            if (arrived) CancelWalk(ref m);
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
