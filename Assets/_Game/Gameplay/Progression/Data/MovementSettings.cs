using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Shared movement tuning; never stores a player's current movement mode.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        private const float MinimumPositive = 0.01f;
        private const float DefaultWalkSpeed = 5f;
        private const float DefaultAcceleration = 25f;
        private const float DefaultTurnSpeed = 540f;
        private const float DefaultStoppingDistance = 0.2f;
        private const float DefaultAccelerationPerSpeed = 8f;
        private const float DefaultArrivalSlowdownTime = 0.3f;
        private const float DefaultArrivalMinRadius = 1.5f;
        private const float DefaultArrivalMaxRadius = 8f;
        private const float DefaultArrivalMinSpeedFactor = 0.55f;

        [Tooltip("Base walk speed in world units per second.")]
        [Min(MinimumPositive)] public float walkSpeed = DefaultWalkSpeed;
        [Min(MinimumPositive)] public float acceleration = DefaultAcceleration;
        [Tooltip("Degrees per second while turning toward the path.")]
        [Min(MinimumPositive)] public float turnSpeed = DefaultTurnSpeed;
        [Min(0f)] public float stoppingDistance = DefaultStoppingDistance;

        [Header("Responsiveness")]
        [Tooltip("Acceleration is at least speed x this, so fast run ranks change direction as crisply as walking.")]
        [Min(0f)] public float accelerationPerSpeed = DefaultAccelerationPerSpeed;

        [Header("Arrival")]
        [Tooltip("The slowdown zone is this many seconds of travel at the current speed (clamped below).")]
        [Min(0f)] public float arrivalSlowdownTime = DefaultArrivalSlowdownTime;
        [Min(0f)] public float arrivalMinRadius = DefaultArrivalMinRadius;
        [Min(0f)] public float arrivalMaxRadius = DefaultArrivalMaxRadius;
        [Tooltip("Fraction of full speed kept when reaching the destination.")]
        [Range(0.1f, 1f)] public float arrivalMinSpeedFactor = DefaultArrivalMinSpeedFactor;

        [Header("A move that makes no headway")]
        [Tooltip("A move order under way that gains less than the distance below in this many seconds has stalled: " +
                 "the way is planned again once, and if that stalls too the character stops where it is.")]
        [Min(0.1f)] public float stallSeconds = 0.75f;
        [Tooltip("Metres a move must gain in the time above to count as moving.")]
        [Min(0.01f)] public float stallDistance = 0.1f;
        [Tooltip("An agent that a jump or a vault failed to put back on the ground is put back after this many seconds. " +
                 "Without it the character takes no order again.")]
        [Min(0.1f)] public float agentRecoverySeconds = 1.5f;

        /// <summary>Distance before the destination where the agent starts easing off.</summary>
        public float ArrivalRadius(float speed) =>
            Mathf.Clamp(speed * arrivalSlowdownTime, arrivalMinRadius, Mathf.Max(arrivalMinRadius, arrivalMaxRadius));

        /// <summary>Speed multiplier for the given distance left: 1 outside the arrival zone, easing to the minimum at the end.</summary>
        public float ArrivalSpeedFactor(float remainingDistance, float speed)
        {
            float radius = ArrivalRadius(speed);
            if (radius <= 0f || remainingDistance >= radius) return 1f;
            float t = Mathf.Clamp01(remainingDistance / radius);
            return Mathf.Lerp(arrivalMinSpeedFactor, 1f, Mathf.Sqrt(t));
        }

        /// <summary>
        /// True when a body <paramref name="remainingDistance"/> from its destination and going at
        /// <paramref name="speed"/> is there by the end of this frame. A fast run covers more than the stopping
        /// distance in one frame: judged on the stopping distance alone it steps over the point, turns back and
        /// never arrives.
        /// </summary>
        public bool ArrivesThisFrame(float remainingDistance, float speed, float deltaTime) =>
            remainingDistance <= stoppingDistance + Mathf.Max(0f, speed) * Mathf.Max(0f, deltaTime);

        /// <summary>
        /// True when a move that has been watched for <paramref name="watchedSeconds"/> and gained
        /// <paramref name="gained"/> metres in that time has stalled: something holds the body where it is.
        /// </summary>
        public bool HasStalled(float gained, float watchedSeconds) =>
            watchedSeconds >= stallSeconds && gained < stallDistance;

        private void OnValidate()
        {
            walkSpeed = Mathf.Max(MinimumPositive, walkSpeed);
            acceleration = Mathf.Max(MinimumPositive, acceleration);
            turnSpeed = Mathf.Max(MinimumPositive, turnSpeed);
            stoppingDistance = Mathf.Max(0f, stoppingDistance);
            arrivalMaxRadius = Mathf.Max(arrivalMinRadius, arrivalMaxRadius);
        }
    }
}
