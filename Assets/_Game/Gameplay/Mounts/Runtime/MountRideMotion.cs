using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>The arithmetic of the rider's summon hop (owner, 6 Nov 2026), kept apart from the scene so it can be tested.</summary>
    public static class MountRideMotion
    {
        /// <summary>
        /// How high the rider is, as a share of the seat's height, <paramref name="seconds"/> into a hop lasting
        /// <paramref name="length"/> seconds, read off a Mount Settings curve whose time runs 0 to 1. Before the start
        /// it reads the curve's start, after the end its end. With no curve the rider is on the seat (1).
        /// </summary>
        public static float Lift(AnimationCurve curve, float seconds, float length)
        {
            if (curve == null || curve.length == 0) return 1f;
            float t = length > 0f ? Mathf.Clamp01(seconds / length) : 1f;
            return curve.Evaluate(t);
        }

        /// <summary>
        /// How far ahead of the player the rider is drawn, in metres, <paramref name="seconds"/> into a getting off lasting
        /// <paramref name="length"/> seconds (the running dismount's launch arc, owner 6 Nov 2026). A visual arc only:
        /// the player's real position never changes. With no curve the rider is on the player (0).
        /// </summary>
        public static float Ahead(AnimationCurve curve, float seconds, float length)
        {
            if (curve == null || curve.length == 0) return 0f;
            float t = length > 0f ? Mathf.Clamp01(seconds / length) : 1f;
            return curve.Evaluate(t);
        }
    }
}
