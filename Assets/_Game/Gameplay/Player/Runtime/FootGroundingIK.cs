using Animancer;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Ground-raycast foot placement for humanoid characters driven by Animancer.
    /// Feet keep their animated lift but are re-based onto the real ground under each foot, and the
    /// pelvis shifts so the lower foot can reach (or so the character doesn't sink when the NavMesh
    /// sits below the terrain). Planted feet also tilt to the slope. Purely cosmetic: runs locally on
    /// every peer. Must live on the same GameObject as the Animator so Unity sends OnAnimatorIK.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    public sealed class FootGroundingIK : MonoBehaviour
    {
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private PlayerAnimancerMotor motor;

        [Header("Raycast")]
        [SerializeField] private LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [Tooltip("Ray starts this high above the character's ground plane, so feet find steps up to this height.")]
        [SerializeField, Min(0.05f)] private float rayStartHeight = 0.5f;
        [Tooltip("How far below the ground plane the ray searches, i.e. the deepest step down a foot reaches for.")]
        [SerializeField, Min(0.05f)] private float maxStepDown = 0.5f;

        [Header("Feet")]
        [Tooltip("Animated sole height (m) at or below which a foot counts as planted and fully tilts to the slope.")]
        [SerializeField, Min(0f)] private float plantedHeight = 0.05f;
        [Tooltip("Animated sole height (m) at which a lifted foot stops tilting to the slope.")]
        [SerializeField, Min(0.01f)] private float liftedHeight = 0.2f;
        [SerializeField, Range(0f, 60f)] private float maxSlopeAngle = 40f;

        [Header("Pelvis")]
        [Tooltip("Max the hips drop so a foot can reach lower ground.")]
        [SerializeField, Min(0f)] private float maxPelvisDrop = 0.35f;
        [Tooltip("Max the hips rise when both feet stand above the NavMesh height (prevents sinking into terrain).")]
        [SerializeField, Min(0f)] private float maxPelvisRaise = 0.15f;
        [SerializeField, Min(0.1f)] private float pelvisSmoothing = 12f;

        [Header("Blending")]
        [Tooltip("How quickly the whole effect fades in/out (e.g. leaving the ground for a jump).")]
        [SerializeField, Min(0.1f)] private float weightBlendSpeed = 6f;

        private Animator animator;
        private float masterWeight;
        private float pelvisOffset;

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
        }

        private void OnDisable()
        {
            if (animancer != null && animancer.IsGraphInitialized) animancer.Layers[0].ApplyAnimatorIK = false;
            masterWeight = 0f;
            pelvisOffset = 0f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || !animator.isHuman) return;

            bool grounded = motor == null || motor.CurrentState != CharacterAnimState.Jumping;
            masterWeight = Mathf.MoveTowards(masterWeight, grounded ? 1f : 0f, weightBlendSpeed * Time.deltaTime);

            if (masterWeight <= 0f)
            {
                SetGoalWeights(AvatarIKGoal.LeftFoot, 0f, 0f);
                SetGoalWeights(AvatarIKGoal.RightFoot, 0f, 0f);
                pelvisOffset = 0f;
                return;
            }

            // The Animator sits at the character's feet: its height is where the NavMesh says the ground is.
            float groundPlaneY = transform.position.y;

            bool leftHit = SolveFoot(AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight, groundPlaneY,
                out float leftDelta, out Vector3 leftTarget, out Quaternion leftRotation, out float leftPlant);
            bool rightHit = SolveFoot(AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight, groundPlaneY,
                out float rightDelta, out Vector3 rightTarget, out Quaternion rightRotation, out float rightPlant);

            // Shift the pelvis by the lower foot's ground offset so that leg can reach without over-stretching.
            float targetPelvis = 0f;
            if (leftHit || rightHit)
            {
                float lowest = Mathf.Min(leftHit ? leftDelta : float.MaxValue, rightHit ? rightDelta : float.MaxValue);
                targetPelvis = Mathf.Clamp(lowest, -maxPelvisDrop, maxPelvisRaise);
            }
            pelvisOffset = Mathf.Lerp(pelvisOffset, targetPelvis, 1f - Mathf.Exp(-pelvisSmoothing * Time.deltaTime));
            animator.bodyPosition += Vector3.up * (pelvisOffset * masterWeight);

            ApplyFoot(AvatarIKGoal.LeftFoot, leftHit, leftTarget, leftRotation, leftPlant);
            ApplyFoot(AvatarIKGoal.RightFoot, rightHit, rightTarget, rightRotation, rightPlant);
        }

        private bool SolveFoot(AvatarIKGoal goal, float footBottomHeight, float groundPlaneY,
            out float groundDelta, out Vector3 target, out Quaternion rotation, out float plant)
        {
            Vector3 animatedPosition = animator.GetIKPosition(goal);
            Quaternion animatedRotation = animator.GetIKRotation(goal);

            // Animated sole height above the ground plane: 0 while planted, > 0 mid-step.
            float soleHeight = animatedPosition.y - footBottomHeight - groundPlaneY;
            plant = 1f - Mathf.InverseLerp(plantedHeight, Mathf.Max(liftedHeight, plantedHeight + 0.01f), soleHeight);

            groundDelta = 0f;
            target = animatedPosition;
            rotation = animatedRotation;

            var origin = new Vector3(animatedPosition.x, groundPlaneY + rayStartHeight, animatedPosition.z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, rayStartHeight + maxStepDown,
                    groundLayers, QueryTriggerInteraction.Ignore))
                return false;

            groundDelta = hit.point.y - groundPlaneY;

            // Keep the animated lift, measured from the real ground instead of the NavMesh plane.
            // A sole the animation pushes below the ground is clamped back up onto it.
            target = animatedPosition;
            target.y = hit.point.y + footBottomHeight + Mathf.Max(0f, soleHeight);

            Vector3 normal = Vector3.Angle(Vector3.up, hit.normal) <= maxSlopeAngle
                ? hit.normal
                : Vector3.RotateTowards(Vector3.up, hit.normal, maxSlopeAngle * Mathf.Deg2Rad, 0f);
            rotation = Quaternion.FromToRotation(Vector3.up, normal) * animatedRotation;
            return true;
        }

        private void ApplyFoot(AvatarIKGoal goal, bool hit, Vector3 target, Quaternion rotation, float plant)
        {
            if (!hit)
            {
                SetGoalWeights(goal, 0f, 0f);
                return;
            }

            animator.SetIKPosition(goal, target);
            animator.SetIKRotation(goal, rotation);
            SetGoalWeights(goal, masterWeight, masterWeight * plant);
        }

        private void SetGoalWeights(AvatarIKGoal goal, float position, float rotation)
        {
            animator.SetIKPositionWeight(goal, position);
            animator.SetIKRotationWeight(goal, rotation);
        }
    }
}
