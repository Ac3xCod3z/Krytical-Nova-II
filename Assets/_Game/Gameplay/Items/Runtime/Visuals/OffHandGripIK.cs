using Animancer;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The second hand of a two-handed weapon (owner 23 Oct 2026: "glaive needs a second hand"). While the weapon in
    /// the hand has a <see cref="WeaponGrip"/> that is two-handed, the left hand is pulled onto its shaft, to the
    /// point of the shaft nearest where the strike put it, so the hand slides along the shaft as the clip moves it.
    /// Presentation only, on every peer. Lives on the object with the Animator (Unity sends OnAnimatorIK there).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OffHandGripIK : MonoBehaviour
    {
        [SerializeField] private WeaponVisualMount mount;
        [Tooltip("Seconds for the hand to reach the shaft, or let go of it.")]
        [SerializeField] private float blend = 0.12f;
        [Tooltip("Furthest the hand is pulled from where the strike put it; beyond that it lets go. The Glaive strikes swing one-handed, the shaft mostly out of the left arm's reach: a stretched arm looked worse than a free hand.")]
        [SerializeField] private float reach = 0.35f;

        private Animator animator;
        private AnimancerComponent animancer;
        private float weight;
        private Transform leftHand, leftMiddle; // the palm's middle is between them: that, not the wrist, goes on the shaft

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (mount == null) mount = transform.root.GetComponentInChildren<WeaponVisualMount>(true);
            animancer = GetComponentInParent<AnimancerComponent>();
            if (animator != null && animator.isHuman)
            {
                leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            }
        }

        private void OnEnable()
        {
            if (animancer != null) animancer.Layers[0].ApplyAnimatorIK = true;
            weight = 0f;
        }

        // The strikes play on the upper-body layer: the IK pass must run there too, not only on the base layer.
        private void Update()
        {
            if (animancer == null || !animancer.IsGraphInitialized) return;
            for (int i = 0; i < animancer.Layers.Count; i++)
                if (!animancer.Layers[i].ApplyAnimatorIK) animancer.Layers[i].ApplyAnimatorIK = true;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || mount == null) return;
            WeaponGrip grip = mount.Grip;
            bool hold = grip != null && grip.twoHanded && mount.IsDrawn && grip.isActiveAndEnabled;
            Vector3 target = default;
            if (hold)
            {
                Vector3 wrist = animator.GetIKPosition(AvatarIKGoal.LeftHand);
                Vector3 palm = leftHand != null && leftMiddle != null ? (leftMiddle.position - leftHand.position) * 0.6f : Vector3.zero;
                Vector3 onShaft = grip.NearestOnShaft(wrist + palm);
                target = onShaft - palm;
                if ((target - wrist).sqrMagnitude > reach * reach) hold = false;
            }
            weight = Mathf.MoveTowards(weight, hold ? 1f : 0f, blend > 0f ? Time.deltaTime / blend : 1f);
            if (weight <= 0f) return;
            if (hold) animator.SetIKPosition(AvatarIKGoal.LeftHand, target);
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, hold ? weight : 0f);
        }
    }
}
