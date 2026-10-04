using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Idle face upkeep on a merged Sidekick character: eye blinks, and holding the jaw shut.
    /// <see cref="SkinnedMeshMerger"/> keeps only the <see cref="LiveShapes"/> as real blendshapes (named by suffix);
    /// everything else about the face is baked. The humanoid avatar maps the Sidekick "jaw" bone, and the sword clips
    /// have no jaw curves, so retargeting leaves the mouth ~10° open; the jaw is put back to its rest (closed) pose
    /// after animation each frame. Client-side cosmetic, re-resolves shape indices whenever the mesh is rebuilt.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FaceBlinker : MonoBehaviour
    {
        public static readonly string[] LiveShapes =
        {
            "eyeBlinkUpperLeft", "eyeBlinkLowerLeft", "eyeBlinkUpperRight", "eyeBlinkLowerRight",
        };

        [SerializeField] private SkinnedMeshRenderer target;
        [SerializeField] private Vector2 intervalRange = new Vector2(2.5f, 6f);
        [Tooltip("Chance a blink is immediately followed by a second one.")]
        [Range(0f, 1f)] [SerializeField] private float doubleBlinkChance = 0.15f;
        [SerializeField, Min(0.01f)] private float closeDuration = 0.08f;
        [SerializeField, Min(0f)] private float holdDuration = 0.1f;
        [SerializeField, Min(0.01f)] private float openDuration = 0.14f;
        [Tooltip("Blend the jaw back to its rest (closed) pose after animation. 1 = fully closed, 0 = leave the animated jaw.")]
        [Range(0f, 1f)] [SerializeField] private float jawRestBlend = 1f;

        private readonly int[] indices = new int[4];
        private Mesh resolvedMesh;
        private int resolvedShapeCount;
        private float nextBlink;
        private float blinkStart = -1f;
        private Transform jaw;
        private Quaternion jawRest;

        public bool HasTarget => target != null;

        public void Bind(SkinnedMeshRenderer renderer)
        {
            target = renderer;
            resolvedMesh = null;
        }

        private void Awake()
        {
            var animator = GetComponentInParent<Animator>();
            if (animator == null || !animator.isHuman || animator.avatar == null) return;
            jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
            if (jaw == null) return;
            jawRest = jaw.localRotation;
            // Prefer the avatar's recorded rest pose over whatever pose the bone happens to be in right now.
            foreach (var bone in animator.avatar.humanDescription.skeleton)
                if (bone.name == jaw.name) { jawRest = bone.rotation; break; }
        }

        private void OnEnable()
        {
            nextBlink = Time.time + Random.Range(intervalRange.x, intervalRange.y);
        }

        private void LateUpdate()
        {
            if (jaw != null && jawRestBlend > 0f)
                jaw.localRotation = Quaternion.Slerp(jaw.localRotation, jawRest, jawRestBlend);

            if (target == null || target.sharedMesh == null) return;
            // A rebuilt character gets a new mesh; one rebuilt in place keeps its mesh but may lose its shapes.
            if (target.sharedMesh != resolvedMesh || resolvedMesh.blendShapeCount != resolvedShapeCount) Resolve();
            if (indices[0] < 0 && indices[2] < 0) return;

            float now = Time.time;
            if (blinkStart < 0f && now >= nextBlink) blinkStart = now;
            if (blinkStart < 0f) return;

            float t = now - blinkStart;
            float openStart = closeDuration + holdDuration;
            float weight;
            if (t < closeDuration) weight = t / closeDuration;
            else if (t < openStart) weight = 1f;
            else weight = 1f - (t - openStart) / openDuration;

            // Done only once the lids have reopened (weight is also 0 on the blink's first frame).
            if (t >= openStart + openDuration)
            {
                weight = 0f;
                blinkStart = -1f;
                nextBlink = now + (Random.value < doubleBlinkChance ? 0.12f : Random.Range(intervalRange.x, intervalRange.y));
            }
            Apply(Mathf.Clamp01(weight) * 100f);
        }

        private void Resolve()
        {
            resolvedMesh = target.sharedMesh;
            resolvedShapeCount = resolvedMesh.blendShapeCount;
            for (int i = 0; i < LiveShapes.Length; i++) indices[i] = resolvedMesh.GetBlendShapeIndex(LiveShapes[i]);
        }

        private void Apply(float weight)
        {
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] >= 0 && indices[i] < resolvedShapeCount) target.SetBlendShapeWeight(indices[i], weight);
        }
    }
}
