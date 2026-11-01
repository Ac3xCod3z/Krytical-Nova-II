using Kinemation.MotionWarping.Runtime.Core;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// The warped jump's planning rules without a body (M3b; one copy of the rules): the landing spot, the apex and the collision
    /// sweep of the arc, for a jump from given feet to a clicked point. <see cref="PlayerJumpMotor"/> plans its own jumps with it;
    /// the entity-netcode jump planner calls it with the ghost's feet, because on a body of the entity netcode the motor and the
    /// agent are switched off. Wallstride catches and the parabola fallback stay in the motor.
    /// </summary>
    public static class JumpPlanRules
    {
        const float MinimumTime = 0.01f;
        /// <summary>A NavMesh landing further than this above or below the clicked point is not where the player clicked.</summary>
        public const float LandingHeightTolerance = 0.2f;

        /// <summary>The capsule the jump sweeps: the player's NavMeshAgent radius and height, minus the motion data's skin.</summary>
        public struct Capsule
        {
            public float Radius, Height, Skin;
            public int Layers;

            public void At(Vector3 feet, out Vector3 bottom, out Vector3 top, out float radius)
            {
                radius = Mathf.Max(MinimumTime, Radius - Skin);
                bottom = feet + Vector3.up * Radius;
                top = feet + Vector3.up * Mathf.Max(Radius, Height - Radius);
            }

            public bool IsClear(Vector3 feet)
            {
                At(feet, out var bottom, out var top, out float radius);
                return !Physics.CheckCapsule(bottom, top, radius, Layers, QueryTriggerInteraction.Ignore);
            }

            public bool IsSegmentClear(Vector3 from, Vector3 to)
            {
                At(from, out var bottom, out var top, out float radius);
                Vector3 delta = to - from;
                float distance = delta.magnitude;
                if (Physics.CheckCapsule(bottom, top, radius, Layers, QueryTriggerInteraction.Ignore)) return false;
                return distance <= Mathf.Epsilon || !Physics.CapsuleCast(bottom, top, radius, delta / distance,
                    distance, Layers, QueryTriggerInteraction.Ignore);
            }
        }

        /// <summary>The warp asset's clip times: take-off and apex (phase 0), touchdown (end of phase 1).</summary>
        public struct Phases
        {
            public float Takeoff, Apex, Land;

            public static bool TryRead(MotionWarpingAsset warp, out Phases phases)
            {
                phases = default;
                if (warp == null || warp.warpPhases == null || warp.warpPhases.Count != 2 || warp.rootY == null) return false;
                phases = new Phases { Takeoff = warp.warpPhases[0].startTime, Apex = warp.warpPhases[0].endTime, Land = warp.warpPhases[1].endTime };
                return phases.Apex > phases.Takeoff && phases.Land > phases.Apex;
            }
        }

        /// <summary>
        /// Where the jump comes down for a click: on the water (Waterstride's map), else the NavMesh within the motion data's landing
        /// radius and close to the clicked height. Null when the click has no landing.
        /// </summary>
        public static string Landing(Vector3 clicked, JumpMotionData motion, NavMeshQueryFilter filter, out Vector3 landingFeet, out bool onWater)
        {
            onWater = PlayerWaterstrideMotor.TryWaterLandingAt(clicked, out landingFeet);
            if (onWater) return null;
            if (!NavMesh.SamplePosition(clicked, out var hit, motion.landingSampleRadius, filter)
                || Mathf.Abs(hit.position.y - clicked.y) > LandingHeightTolerance)
            {
                landingFeet = clicked;
                return "Choose a walkable landing surface.";
            }
            landingFeet = hit.position;
            return null;
        }

        /// <summary>The apex across the ground: travel is even over the air time, so it sits where the clip's apex falls in time.</summary>
        public static Vector3 ApexFeet(Vector3 startFeet, Vector3 landingFeet, float peakHeight, in Phases p)
        {
            Vector3 apex = Vector3.Lerp(startFeet, landingFeet, (p.Apex - p.Takeoff) / (p.Land - p.Takeoff));
            apex.y = peakHeight;
            return apex;
        }

        /// <summary>
        /// Where the warp will put the feet at a clip time. Mirrors the warp component's maths for this asset setup
        /// (horizontal travel linear in time; height follows the clip's own rise and fall, rescaled to reach the
        /// apex and the landing) so the route can be collision-checked before committing.
        /// </summary>
        public static Vector3 WarpedFeet(Vector3 startFeet, Vector3 apexFeet, Vector3 landingFeet, float peakHeight, in Phases p,
            AnimationCurve height, float clipTime)
        {
            if (clipTime <= p.Takeoff) return startFeet;
            if (clipTime <= p.Apex)
            {
                Vector3 feet = Vector3.Lerp(startFeet, apexFeet, Mathf.InverseLerp(p.Takeoff, p.Apex, clipTime));
                float clipTotal = height.Evaluate(p.Apex) - height.Evaluate(p.Takeoff);
                float share = Mathf.Abs(clipTotal) > MinimumTime
                    ? Mathf.Clamp01((height.Evaluate(clipTime) - height.Evaluate(p.Takeoff)) / clipTotal) : 1f;
                feet.y = Mathf.Lerp(startFeet.y, peakHeight, share);
                return feet;
            }
            else
            {
                clipTime = Mathf.Min(clipTime, p.Land);
                Vector3 feet = Vector3.Lerp(apexFeet, landingFeet, Mathf.InverseLerp(p.Apex, p.Land, clipTime));
                float clipTotal = height.Evaluate(p.Land) - height.Evaluate(p.Apex);
                float share = Mathf.Abs(clipTotal) > MinimumTime
                    ? Mathf.Clamp01((height.Evaluate(clipTime) - height.Evaluate(p.Apex)) / clipTotal) : 1f;
                feet.y = Mathf.Lerp(peakHeight, landingFeet.y, share);
                return feet;
            }
        }

        /// <summary>The landing is free and every step of the arc (motion data's trajectory samples) is clear of the capsule's layers.</summary>
        public static bool IsWarpedRouteClear(Vector3 startFeet, Vector3 apexFeet, Vector3 landingFeet, float peakHeight, in Phases p,
            AnimationCurve height, int samples, in Capsule capsule)
        {
            if (!capsule.IsClear(landingFeet)) return false;
            Vector3 previous = startFeet;
            for (int step = 1; step <= samples; step++)
            {
                float clipTime = Mathf.Lerp(p.Takeoff, p.Land, (float)step / samples);
                Vector3 next = WarpedFeet(startFeet, apexFeet, landingFeet, peakHeight, p, height, clipTime);
                if (!capsule.IsSegmentClear(previous, next)) return false;
                previous = next;
            }
            return true;
        }

        /// <summary>
        /// A whole warped jump from <paramref name="startFeet"/> to the clicked point by the rank's limits: the landing, the limits
        /// (<see cref="JumpMotionData.LimitRefusal"/>), the apex (<see cref="JumpMotionData.PeakHeight"/>) and the clear arc. Null
        /// when it stands; else the player's message (the motor's words).
        /// </summary>
        public static string PlanWarped(Vector3 startFeet, Vector3 clicked, float rankDistance, float rankHeight, JumpMotionData motion,
            MotionWarpingAsset warp, NavMeshQueryFilter filter, in Capsule capsule, out Vector3 landingFeet, out float peakHeight, out bool onWater)
        {
            peakHeight = 0f;
            string why = Landing(clicked, motion, filter, out landingFeet, out onWater);
            if (why != null) return why;
            why = JumpMotionData.LimitRefusal(startFeet, landingFeet, rankDistance, rankHeight);
            if (why != null) return why;
            if (!Phases.TryRead(warp, out Phases p)) return "Jump warp asset has no flight phases. Rebuild it from the Krytical Nova menu.";
            peakHeight = motion.PeakHeight(startFeet, landingFeet, rankDistance, rankHeight);
            Vector3 apex = ApexFeet(startFeet, landingFeet, peakHeight, p);
            if (!IsWarpedRouteClear(startFeet, apex, landingFeet, peakHeight, p, warp.rootY, motion.trajectorySamples, capsule))
                return "The jump arc or landing area is blocked.";
            return null;
        }

        /// <summary>
        /// The warped flight to a Wallstride catch point (`PlayerJumpMotor.PlanCatchFlight`): no NavMesh landing (the point is against the
        /// surface), refused past the rank's height, the apex the higher end plus the motion's clearance, the arc clear. Null when it stands.
        /// </summary>
        public static string PlanCatchWarped(Vector3 startFeet, Vector3 catchFeet, float rankHeight, JumpMotionData motion, MotionWarpingAsset warp,
            in Capsule capsule, out float peakHeight)
        {
            peakHeight = Mathf.Max(startFeet.y, catchFeet.y) + motion.apexClearance;
            if (catchFeet.y - startFeet.y > rankHeight) return $"Too high to jump at: Jump rank reaches {rankHeight:0.0} m.";
            if (!Phases.TryRead(warp, out Phases p)) return "Jump warp asset has no flight phases. Rebuild it from the Krytical Nova menu.";
            Vector3 apex = ApexFeet(startFeet, catchFeet, peakHeight, p);
            if (!IsWarpedRouteClear(startFeet, apex, catchFeet, peakHeight, p, warp.rootY, motion.trajectorySamples, capsule))
                return "The jump arc or landing area is blocked.";
            return null;
        }
    }
}
