using Animancer;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Ground-probe foot placement for humanoid characters driven by Animancer.
    /// Feet keep their animated lift but are re-based onto the real ground under each foot, and the
    /// pelvis shifts so the lower foot can reach (or so the character doesn't sink when the NavMesh
    /// sits below the terrain). Planted feet also tilt to the slope. Each foot eases between ground
    /// heights and fades out when it finds no ground (over a ledge), so nothing pops; the first solve
    /// after enabling or teleporting starts settled. Purely cosmetic: runs locally on
    /// every peer. Must live on the same GameObject as the Animator so Unity sends OnAnimatorIK.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class FootGroundingIK : MonoBehaviour
    {
        private const float TeleportDistance = 1f; // further than a character moves in one frame
        private const float FlatGroundAngle = 3f;  // below this the ground counts as level (collider noise, tile lips)
        private const float SlopeSampleDistance = 0.12f; // how far from the foot the slope is measured

        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private PlayerAnimancerMotor motor;

        [Header("Ground probe")]
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [Tooltip("Tallest step up a foot is placed on. Anything higher under a foot (props, other characters, walls) is ignored.")]
        [SerializeField, Min(0.05f)] private float maxStepUp = 0.3f;
        [Tooltip("How far below the ground plane the probe searches, i.e. the deepest step down a foot reaches for. " +
                 "A foot over a deeper drop keeps its animated pose.")]
        [SerializeField, Min(0.05f)] private float maxStepDown = 0.35f;
        [Tooltip("Radius of the probe under each foot. Wider than a ray, so seams and edges give a steady answer.")]
        [SerializeField, Min(0.01f)] private float probeRadius = 0.06f;

        [Header("Feet")]
        [Tooltip("Animated sole height (m) at or below which a foot counts as planted and fully tilts to the slope.")]
        [SerializeField, Min(0f)] private float plantedHeight = 0.05f;
        [Tooltip("Animated sole height (m) at which a lifted foot stops tilting to the slope.")]
        [SerializeField, Min(0.01f)] private float liftedHeight = 0.2f;
        [SerializeField, Range(0f, 60f)] private float maxSlopeAngle = 40f;
        [Tooltip("How quickly a foot follows a change in the ground under it (per second).")]
        [SerializeField, Min(1f)] private float footSmoothing = 25f;

        [Header("Pelvis")]
        [Tooltip("Max the hips drop so a foot can reach lower ground.")]
        [SerializeField, Min(0f)] private float maxPelvisDrop = 0.2f;
        [Tooltip("Max the hips rise when both feet stand above the NavMesh height (prevents sinking into terrain).")]
        [SerializeField, Min(0f)] private float maxPelvisRaise = 0.15f;
        [SerializeField, Min(0.1f)] private float pelvisSmoothing = 12f;

        [Header("Blending")]
        [Tooltip("How quickly the whole effect, and each foot, fades in/out (leaving the ground for a jump, a foot over a ledge).")]
        [SerializeField, Min(0.1f)] private float weightBlendSpeed = 6f;

        private struct Foot
        {
            public float Weight;       // 0 = animated pose, 1 = on the probed ground
            public float GroundDelta;  // ground height under the foot, relative to the character's ground plane
            public Vector3 Normal;
        }

        private Animator animator;
        private float masterWeight;
        private float pelvisOffset;
        private Foot leftFoot, rightFoot;
        private Vector3 lastPosition;
        private bool settle = true; // next solve jumps to its targets instead of easing

        /// <summary>
        /// Metres this model currently stands above (or below) its resting height: a skill's leap or crouch, put on
        /// the body by whoever plays it. The ground is still where it was, so the feet are placed against that.
        /// </summary>
        public float BodyLift { get; set; }

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (animancer == null) animancer = GetComponent<AnimancerComponent>();
            if (animancer == null) animancer = GetComponentInParent<AnimancerComponent>();
            if (motor == null) motor = GetComponentInParent<PlayerAnimancerMotor>();
        }

        private void OnEnable()
        {
            // Tells Unity to run the IK pass (and call OnAnimatorIK) for every state on the base layer.
            if (animancer != null) animancer.Layers[0].ApplyAnimatorIK = true;
            settle = true;
        }

        private void OnDisable()
        {
            if (animancer != null && animancer.IsGraphInitialized) animancer.Layers[0].ApplyAnimatorIK = false;
            masterWeight = 0f;
            pelvisOffset = 0f;
            leftFoot = rightFoot = default;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman) return;

            // Arriving somewhere new (spawn, teleport) leaves nothing to ease from.
            Vector3 position = transform.position - Vector3.up * BodyLift;
            if ((position - lastPosition).sqrMagnitude > TeleportDistance * TeleportDistance) settle = true;
            lastPosition = position;

            float dt = Time.deltaTime;
            // In the air, or lying dead: there is nothing to plant the feet on.
            bool grounded = motor == null
                || (motor.CurrentState != CharacterAnimState.Jumping && motor.CurrentState != CharacterAnimState.Dead);
            masterWeight = settle ? (grounded ? 1f : 0f) : Mathf.MoveTowards(masterWeight, grounded ? 1f : 0f, weightBlendSpeed * dt);

            if (masterWeight <= 0f)
            {
                SetGoalWeights(AvatarIKGoal.LeftFoot, 0f, 0f);
                SetGoalWeights(AvatarIKGoal.RightFoot, 0f, 0f);
                pelvisOffset = 0f;
                leftFoot = rightFoot = default;
                settle = false;
                return;
            }

            // The Animator sits at the character's feet: its height is where the NavMesh says the ground is.
            float groundPlaneY = position.y;

            bool leftHit = SolveFoot(AvatarIKGoal.LeftFoot, ref leftFoot, animator.leftFeetBottomHeight, groundPlaneY, dt,
                out Vector3 leftTarget, out Quaternion leftRotation, out float leftPlant);
            bool rightHit = SolveFoot(AvatarIKGoal.RightFoot, ref rightFoot, animator.rightFeetBottomHeight, groundPlaneY, dt,
                out Vector3 rightTarget, out Quaternion rightRotation, out float rightPlant);

            // Shift the pelvis by the lower foot's ground offset so that leg can reach without over-stretching.
            // A foot with no ground under it (over a ledge) has no say: the body stays up on the other leg.
            float targetPelvis = 0f;
            if (leftHit || rightHit)
            {
                float lowest = Mathf.Min(leftHit ? leftFoot.GroundDelta : float.MaxValue, rightHit ? rightFoot.GroundDelta : float.MaxValue);
                targetPelvis = Mathf.Clamp(lowest, -maxPelvisDrop, maxPelvisRaise);
            }
            pelvisOffset = settle ? targetPelvis : Mathf.Lerp(pelvisOffset, targetPelvis, 1f - Mathf.Exp(-pelvisSmoothing * dt));
            animator.bodyPosition += Vector3.up * (pelvisOffset * masterWeight);

            ApplyFoot(AvatarIKGoal.LeftFoot, leftFoot, leftTarget, leftRotation, leftPlant);
            ApplyFoot(AvatarIKGoal.RightFoot, rightFoot, rightTarget, rightRotation, rightPlant);
            settle = false;
        }

        /// <summary>Probes the ground under one foot and eases the foot's state toward it. Returns whether ground was found.</summary>
        private bool SolveFoot(AvatarIKGoal goal, ref Foot foot, float footBottomHeight, float groundPlaneY, float dt,
            out Vector3 target, out Quaternion rotation, out float plant)
        {
            Vector3 animatedPosition = animator.GetIKPosition(goal);
            Quaternion animatedRotation = animator.GetIKRotation(goal);

            // Animated sole height above the ground plane: 0 while planted, > 0 mid-step.
            float soleHeight = animatedPosition.y - footBottomHeight - groundPlaneY;
            plant = 1f - Mathf.InverseLerp(plantedHeight, Mathf.Max(liftedHeight, plantedHeight + 0.01f), soleHeight);

            bool hit = ProbeGround(animatedPosition, groundPlaneY, out float delta);

            if (hit)
            {
                // The slope is measured from the ground's height a little ahead of and beside the foot, never read
                // from a surface normal: on a seam or an edge a normal belongs to whichever face (or the terrain in
                // the gap) the probe happens to touch, and flips from frame to frame.
                Vector3 forward = transform.forward, right = transform.right;
                float ahead = ProbeGround(animatedPosition + forward * SlopeSampleDistance, groundPlaneY, out float d) ? d : delta;
                float beside = ProbeGround(animatedPosition + right * SlopeSampleDistance, groundPlaneY, out d) ? d : delta;
                Vector3 normal = Vector3.Cross(forward * SlopeSampleDistance + Vector3.up * (ahead - delta),
                    right * SlopeSampleDistance + Vector3.up * (beside - delta)).normalized;
                if (Vector3.Angle(Vector3.up, normal) < FlatGroundAngle) normal = Vector3.up;
                // A foot that had no ground until now starts on it; otherwise it eases across the change.
                bool fresh = settle || foot.Weight <= 0f;
                float follow = fresh ? 1f : 1f - Mathf.Exp(-footSmoothing * dt);
                foot.GroundDelta = Mathf.Lerp(foot.GroundDelta, delta, follow);
                foot.Normal = fresh ? normal : Vector3.Slerp(foot.Normal, normal, follow).normalized;
            }
            // No ground: the foot keeps its last ground while its weight fades, then it is the animated pose again.
            foot.Weight = settle ? (hit ? 1f : 0f) : Mathf.MoveTowards(foot.Weight, hit ? 1f : 0f, weightBlendSpeed * dt);

            // Keep the animated lift, measured from the real ground instead of the NavMesh plane.
            // A sole the animation pushes below the ground is clamped back up onto it.
            target = animatedPosition;
            target.y = groundPlaneY + foot.GroundDelta + footBottomHeight + Mathf.Max(0f, soleHeight);

            Vector3 up = foot.Normal.sqrMagnitude > 0.5f ? foot.Normal : Vector3.up;
            if (Vector3.Angle(Vector3.up, up) > maxSlopeAngle)
                up = Vector3.RotateTowards(Vector3.up, up, maxSlopeAngle * Mathf.Deg2Rad, 0f);
            rotation = Quaternion.FromToRotation(Vector3.up, up) * animatedRotation;
            return hit;
        }

        /// <summary>
        /// Height of the ground under a point, relative to the character's ground plane. The probe starts at the
        /// tallest step a foot may take: anything taller is passed through, not stood on.
        /// </summary>
        private bool ProbeGround(Vector3 point, float groundPlaneY, out float delta)
        {
            var origin = new Vector3(point.x, groundPlaneY + maxStepUp + probeRadius, point.z);
            bool hit = Physics.SphereCast(origin, probeRadius, Vector3.down, out RaycastHit ground, maxStepUp + maxStepDown,
                groundLayers, QueryTriggerInteraction.Ignore);
            delta = hit ? ground.point.y - groundPlaneY : 0f;
            return hit;
        }

        private void ApplyFoot(AvatarIKGoal goal, Foot foot, Vector3 target, Quaternion rotation, float plant)
        {
            float weight = masterWeight * foot.Weight;
            if (weight <= 0f)
            {
                SetGoalWeights(goal, 0f, 0f);
                return;
            }

            animator.SetIKPosition(goal, target);
            animator.SetIKRotation(goal, rotation);
            SetGoalWeights(goal, weight, weight * plant);
        }

        private void SetGoalWeights(AvatarIKGoal goal, float position, float rotation)
        {
            animator.SetIKPositionWeight(goal, position);
            animator.SetIKRotationWeight(goal, rotation);
        }
    }
}
