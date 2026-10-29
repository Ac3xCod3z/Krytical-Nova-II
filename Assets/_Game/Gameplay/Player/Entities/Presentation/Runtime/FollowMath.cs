using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// How the presentation body follows its ghost (ghosts-design 3.2.7). A singleton of this type in the client world overrides
    /// <see cref="Default"/>; the numbers stand in for the old PlayerMoveAnticipation feel and move into a settings asset later.
    /// </summary>
    public struct FollowSettings : IComponentData
    {
        /// <summary>Seconds for the visual offset to halve (about 0.2 s to fade away: the old ease of 0.2 s).</summary>
        public float HalfLifeSeconds;
        /// <summary>A jump of the ghost bigger than this (metres) snaps the body instead of sliding.</summary>
        public float SnapDistanceM;
        /// <summary>How much more than speed x tick the ghost may move between two samples before the excess counts as a correction.</summary>
        public float StepSlack;
        /// <summary>Flat allowance (metres) added to the step a sample may take before it counts as a correction.</summary>
        public float StepEpsilonM;

        public static FollowSettings Default => new FollowSettings
        {
            HalfLifeSeconds = 0.07f,
            SnapDistanceM = 6f,
            StepSlack = 1.5f,
            StepEpsilonM = 0.05f,
        };
    }

    /// <summary>The follow of one body: the last two ghost samples, the decaying visual offset and the yaw pair. Plain data; kept by the registry.</summary>
    public struct FollowState
    {
        public bool Init;
        public byte Teleport;
        public float3 Prev, Target, Offset;
        /// <summary>0 = the previous sample, 1 = the latest one; advances by frame time / tick length.</summary>
        public float T;
        public float PrevYaw, TargetYaw;
        /// <summary>The ghost was walking on the last sample: a stop may still move it one more step.</summary>
        public bool WasMoving;

        public float3 Position => math.lerp(Prev, Target, T) + Offset;
        public float Yaw => FollowMath.LerpAngle(PrevYaw, TargetYaw, T);
    }

    /// <summary>
    /// The arithmetic of the follow, with no world and no GameObject (so it is tested alone). Rendered position =
    /// lerp(previous sample, latest sample, T) + Offset. A predicted ghost moves once per full tick, so T eases the body between the
    /// samples; a correction (the ghost moved farther than speed x tick allows, i.e. a rollback) is taken out of the sample and put in
    /// Offset, which then halves every HalfLifeSeconds, so the body slides to the corrected spot instead of popping.
    /// </summary>
    public static class FollowMath
    {
        public static void Snap(ref FollowState s, float3 pos, float yawDeg, byte teleport)
        {
            s.Init = true; s.Teleport = teleport;
            s.Prev = pos; s.Target = pos; s.Offset = default; s.T = 1f;
            s.PrevYaw = yawDeg; s.TargetYaw = yawDeg;
        }

        /// <param name="smoothTicks">True for a predicted ghost (it only changes on full ticks); false for an interpolated one (already smooth every frame).</param>
        /// <param name="moving">The ghost is walking now (<see cref="MoveFlags.Moving"/>); a ghost that is not walking and did not just stop may not step, so any jump is a correction.</param>
        /// <param name="maxSpeedMps">The fastest this player may move (run speed), metres per second.</param>
        /// <returns>True when the body was set without easing (first sample, placement, huge jump).</returns>
        public static bool Step(ref FollowState s, float3 ghostPos, float ghostYawDeg, byte teleport, bool smoothTicks,
            bool moving, float maxSpeedMps, float dt, float tickSeconds, in FollowSettings cfg)
        {
            if (!s.Init || teleport != s.Teleport) { Snap(ref s, ghostPos, ghostYawDeg, teleport); return true; }

            // the offset fades first, so a correction found this frame starts to show from the next one
            float keep = cfg.HalfLifeSeconds > 0f ? math.exp2(-dt / cfg.HalfLifeSeconds) : 0f;
            s.Offset *= keep;
            if (math.lengthsq(s.Offset) < 1e-8f) s.Offset = default;

            bool mayStep = moving || s.WasMoving;
            s.WasMoving = moving;
            float3 d = ghostPos - s.Target;
            float len = math.length(d);
            bool moved = len > 1e-4f;
            bool turned = math.abs(DeltaAngle(s.TargetYaw, ghostYawDeg)) > 0.01f;
            if (moved || turned)
            {
                if (len > cfg.SnapDistanceM) { Snap(ref s, ghostPos, ghostYawDeg, teleport); return true; }

                float3 shown = math.lerp(s.Prev, s.Target, s.T);
                float shownYaw = LerpAngle(s.PrevYaw, s.TargetYaw, s.T);
                int span = tickSeconds > 0f ? math.max(1, (int)math.round(dt / tickSeconds)) : 1;
                float allowed = (mayStep ? maxSpeedMps : 0f) * tickSeconds * span * cfg.StepSlack + cfg.StepEpsilonM;
                float3 excess = len > allowed ? d * ((len - allowed) / len) : float3.zero;

                // The sample moves by `excess` too, so at T = 0 the body is exactly where it was; the offset pays the excess back.
                s.Prev = shown + excess;
                s.Target = ghostPos;
                s.Offset -= excess;
                s.T = 0f;
                s.PrevYaw = shownYaw;
                s.TargetYaw = ghostYawDeg;
            }

            s.T = smoothTicks && tickSeconds > 0f ? math.saturate(s.T + dt / tickSeconds) : 1f;

            if (math.length(s.Offset) > cfg.SnapDistanceM) s.Offset = default;
            return false;
        }

        /// <summary>Shortest signed turn from a to b in degrees, -180..180.</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            else if (d < -180f) d += 360f;
            return d;
        }

        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * math.saturate(t);

        /// <summary>Yaw in degrees of a rotation about Y (the player only ever turns about Y).</summary>
        public static float YawDegrees(quaternion q) => math.degrees(2f * math.atan2(q.value.y, q.value.w));
    }
}
