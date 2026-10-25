using Animancer;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Keeps a rider's feet in the stirrups and the knees round the barrel, whatever the character's size (owner,
    /// 25 Oct 2026: the riding clips are keyed on one rider; played on another body their legs came out narrower and
    /// longer, so the feet hung below the stirrups and the legs cut into the saddle). The clips set the pose; this moves
    /// each foot onto its stirrup and points each knee forward and out round the mount, in the Animator's IK pass.
    /// Added by <see cref="MountRider"/> on the rider's Animator object, switched on while seated. Purely visual.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StirrupIK : MonoBehaviour
    {
        private Animator animator;
        private AnimancerComponent animancer;
        private Transform mount;
        private Vector3 left, right;
        private float weight;
        private float target;

        /// <summary>How quickly the feet go to or come off the stirrups, in weight a second.</summary>
        private const float BlendSpeed = 4f;
        /// <summary>The knees point this far ahead of the foot and this far out from the mount's centre line, in metres.</summary>
        private const float KneeAhead = 0.45f, KneeOut = 0.35f, KneeUp = 0.35f;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            animancer = GetComponent<AnimancerComponent>();
        }

        /// <summary>Feet onto the stirrups at these points of the mount model (its own space), or off them (null).</summary>
        public void SetStirrups(Transform mountModel, Vector3 leftFoot, Vector3 rightFoot)
        {
            mount = mountModel;
            left = leftFoot;
            right = rightFoot;
            target = mountModel != null ? 1f : 0f;
            if (target > 0f) enabled = true;
            if (animancer != null && target > 0f) animancer.Layers[0].ApplyAnimatorIK = true;
        }

        /// <summary>On getting on or off the feet come out of the stirrups (the clip places them).</summary>
        public void SetWeight(float value) => target = Mathf.Clamp01(value);

        private void Update()
        {
            weight = Mathf.MoveTowards(weight, mount != null ? target : 0f, BlendSpeed * Time.deltaTime);
            if (weight <= 0f && (mount == null || target <= 0f)) enabled = false;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || mount == null || weight <= 0f) return;
            Place(AvatarIKGoal.LeftFoot, AvatarIKHint.LeftKnee, mount.TransformPoint(left), -1f);
            Place(AvatarIKGoal.RightFoot, AvatarIKHint.RightKnee, mount.TransformPoint(right), 1f);
        }

        private void Place(AvatarIKGoal goal, AvatarIKHint hint, Vector3 foot, float side)
        {
            animator.SetIKPosition(goal, foot);
            animator.SetIKPositionWeight(goal, weight);
            // Out from the mount's centre line on the foot's own side, ahead of the foot, up toward the seat.
            Vector3 outward = mount.right * side;
            Vector3 knee = foot + mount.forward * KneeAhead + outward * KneeOut + mount.up * KneeUp;
            animator.SetIKHintPosition(hint, knee);
            animator.SetIKHintPositionWeight(hint, weight);
        }
    }
}
