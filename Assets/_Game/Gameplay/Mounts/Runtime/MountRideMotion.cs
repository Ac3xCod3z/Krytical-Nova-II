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

        /// <summary>
        /// How much of the getting on or off still has (getting on) or already has (getting off) the rider's feet on the
        /// ground, 1 to 0, <paramref name="seconds"/> into the clip: getting on, 1 until the feet leave at
        /// <paramref name="leavesAt"/>, easing to 0 by the seat at <paramref name="arrivesAt"/>; getting off, 0 until the
        /// rider leaves the saddle, easing to 1 by the touchdown. No times (arrives not after leaves): 0, the seat throughout.
        /// </summary>
        public static float OnGround(bool gettingOn, float seconds, float leavesAt, float arrivesAt)
        {
            if (arrivesAt <= leavesAt) return 0f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((seconds - leavesAt) / (arrivesAt - leavesAt)));
            return gettingOn ? 1f - k : k;
        }

        /// <summary>
        /// The extra rise over the seat during a getting-on's air phase, 0 to 1: a sine over leaves-to-arrives, highest
        /// half way and back to 0 as the rider lands in the saddle (owner, 7 Nov 2026: clear the seat, then settle in).
        /// </summary>
        public static float AirBump(float seconds, float leavesAt, float arrivesAt)
        {
            if (arrivesAt <= leavesAt) return 0f;
            float k = Mathf.Clamp01((seconds - leavesAt) / (arrivesAt - leavesAt));
            return Mathf.Sin(k * Mathf.PI);
        }

        /// <summary>
        /// The share (1 to 0) of the seat offset the rider's origin still keeps <paramref name="seconds"/> into the hand-over
        /// out of a getting-off lasting <paramref name="fadeSeconds"/>: linear, as the motor's cross-fade is, so at fade
        /// weight w the origin is (1 - w) up and the body under both poses stays where the clip left it.
        /// </summary>
        public static float HandOverShare(float seconds, float fadeSeconds) =>
            fadeSeconds > 0f ? 1f - Mathf.Clamp01(seconds / fadeSeconds) : 0f;

        /// <summary>
        /// How far (metres) to lower the rider while their feet are on the ground: the stage's seat height less the seat
        /// height its getting-on/off clips were keyed on (0 when keyed on this seat, or when the stage says nothing).
        /// </summary>
        public static float GroundDrop(float seatHeight, float clipSeatHeight) =>
            clipSeatHeight > 0f ? seatHeight - clipSeatHeight : 0f;
    }
}
