using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Shared jump timing, collision safety and visual feedback tuning; rank limits live in Jump skill data.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Jump Motion")]
    public sealed class JumpMotionData : ScriptableObject
    {
        private const float MinimumPositive = 0.01f;
        private const float DefaultGravity = 14f;
        private const float DefaultClearance = 1.4f;
        private const float DefaultHold = 0.16f;
        private const float DefaultAnticipation = 0.12f;
        private const float DefaultFlipDuration = 0.48f;
        private const float DefaultCooldown = 0.25f;
        private const float DefaultSnapDistance = 0.3f;
        private const float DefaultSkin = 0.04f;
        private const int DefaultSamples = 64;
        [Header("Flight")]
        [Min(MinimumPositive)] public float gravity = DefaultGravity;
        [Tooltip("Extra apex height above the higher endpoint, independent of the rank's landing-height limit.")]
        [Min(MinimumPositive)] public float apexClearance = DefaultClearance;
        [Min(0f)] public float apexHoldDuration = DefaultHold;
        [Min(0f)] public float anticipationDuration = DefaultAnticipation;
        [Min(0f)] public float landingCooldown = DefaultCooldown;
        [Header("Height by distance")]
        [Tooltip("Rise = rank height x (distance / rank distance)^exponent. 1 = linear; above 1 keeps short hops low and saves the full height for max-range jumps.")]
        [Range(0.5f, 3f)] public float heightByDistanceExponent = 1.5f;
        [Tooltip("Smallest rise above the takeoff point, so even tiny hops read as a jump.")]
        [Min(0f)] public float minimumRise = 0.8f;

        /// <summary>How high above the takeoff a jump of this distance should peak, before obstacle/ledge clearance.</summary>
        public float RiseForDistance(float horizontalDistance, float rankDistance, float rankHeight)
        {
            float t = rankDistance > 0f ? Mathf.Clamp01(horizontalDistance / rankDistance) : 1f;
            return Mathf.Max(minimumRise, rankHeight * Mathf.Pow(t, heightByDistanceExponent));
        }

        /// <summary>Shorter than this is no jump.</summary>
        public const float MinimumTravelDistance = 0.15f;

        /// <summary>
        /// The rank's limits for a jump from <paramref name="startFeet"/> to <paramref name="landingFeet"/>: why it is refused, or
        /// null. Down has no limit (owner, 22 Oct 2026: "jump off buildings to the ground at any distance"); up is the rank's.
        /// Shared by the NGO motor and the entity-netcode jump planner (one copy of the rules).
        /// </summary>
        public static string LimitRefusal(Vector3 startFeet, Vector3 landingFeet, float rankDistance, float rankHeight)
        {
            Vector3 displacement = landingFeet - startFeet;
            float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
            if (horizontalDistance < MinimumTravelDistance) return "Choose a landing point farther away.";
            if (horizontalDistance > rankDistance) return $"Too far: Jump rank reaches {rankDistance:0.0} m.";
            if (displacement.y > rankHeight) return $"Too high: Jump rank reaches +{rankHeight:0.0} m.";
            return null;
        }

        /// <summary>
        /// The apex height of the feet: the clearance over the higher end, or the rise this distance earns by the rank if that is
        /// higher (short hops stay low; only a max-range jump climbs to the rank's full height).
        /// </summary>
        public float PeakHeight(Vector3 startFeet, Vector3 landingFeet, float rankDistance, float rankHeight)
        {
            Vector3 displacement = landingFeet - startFeet;
            float horizontalDistance = new Vector2(displacement.x, displacement.z).magnitude;
            float clearanceApex = Mathf.Max(startFeet.y, landingFeet.y) + apexClearance;
            return Mathf.Max(clearanceApex, startFeet.y + RiseForDistance(horizontalDistance, rankDistance, rankHeight));
        }
        [Header("Safety")]
        [Min(MinimumPositive)] public float landingSampleRadius = DefaultSnapDistance;
        [Min(MinimumPositive)] public float collisionSkin = DefaultSkin;
        [Range(16, 256)] public int trajectorySamples = DefaultSamples;
        [Header("Animancer apex flip")]
        public AnimationClip frontFlip;
        public AnimationClip restPose;
        [Min(MinimumPositive)] public float flipDuration = DefaultFlipDuration;
        [Header("Boing visual scale spring")]
        [Min(MinimumPositive)] public float springFrequency = 5f;
        [Min(MinimumPositive)] public float springHalfLife = 0.12f;
        public Vector3 anticipationScale = new Vector3(1.15f, 0.75f, 1.15f);
        public Vector3 takeoffScale = new Vector3(0.9f, 1.2f, 0.9f);
        public Vector3 landingScale = new Vector3(1.22f, 0.65f, 1.22f);
        public Vector3 landingScaleVelocity = new Vector3(-1f, 4f, -1f);

        private void OnValidate()
        {
            gravity = Mathf.Max(MinimumPositive, gravity);
            apexClearance = Mathf.Max(MinimumPositive, apexClearance);
            apexHoldDuration = Mathf.Max(0f, apexHoldDuration);
            anticipationDuration = Mathf.Max(0f, anticipationDuration);
            landingCooldown = Mathf.Max(0f, landingCooldown);
            minimumRise = Mathf.Max(0f, minimumRise);
            landingSampleRadius = Mathf.Max(MinimumPositive, landingSampleRadius);
            collisionSkin = Mathf.Clamp(collisionSkin, MinimumPositive, 0.1f);
            trajectorySamples = Mathf.Clamp(trajectorySamples, 16, 256);
            flipDuration = Mathf.Max(MinimumPositive, flipDuration);
            springFrequency = Mathf.Max(MinimumPositive, springFrequency);
            springHalfLife = Mathf.Max(MinimumPositive, springHalfLife);
            anticipationScale = Vector3.Max(Vector3.one * MinimumPositive, anticipationScale);
            takeoffScale = Vector3.Max(Vector3.one * MinimumPositive, takeoffScale);
            landingScale = Vector3.Max(Vector3.one * MinimumPositive, landingScale);
        }
    }
}
