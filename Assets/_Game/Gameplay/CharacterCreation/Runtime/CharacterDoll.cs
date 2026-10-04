using Animancer;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// A character model for display only (character creator, character select): the player's model with everything
    /// but its look and animation stripped, so it has no physics, IK, hitbox or networking. Restyle it with
    /// <see cref="Apply"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterDoll : MonoBehaviour
    {
        private CharacterLook look;
        private AnimancerComponent animancer;
        private AnimationClip idle;
        private CharacterAppearance appearance;

        // Expressions: the face eases towards a target set of blend-shape weights; an emote plays once, then the idle returns.
        private const float FaceBlendPerSecond = 500f;
        private const float MotionFade = 0.25f;
        private CharacterExpressionLibrary expressions;
        private float[] faceTarget, faceCurrent;
        private int[] shapeIndex;
        private Mesh indexedMesh;
        private float emoteEndsAt = -1f;

        public CharacterLook Look => look;

        /// <summary>Clone <paramref name="modelPrefab"/> (the player's Sidekick model) under <paramref name="parent"/>.</summary>
        public static CharacterDoll Create(GameObject modelPrefab, Transform parent, AnimationClip idleClip)
        {
            if (modelPrefab == null) return null;
            // Clone under an inactive holder so no Awake runs before the gameplay components are stripped.
            var holder = new GameObject("Doll Holder");
            holder.SetActive(false);
            var clone = Instantiate(modelPrefab, holder.transform);
            clone.name = "Character Doll";
            foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is CharacterAppearance || behaviour is CharacterLook || behaviour is AnimancerComponent
                    || behaviour is FaceBlinker) continue;
                DestroyImmediate(behaviour);
            }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            // Weapons and other rigid props are not part of the look being edited.
            foreach (var mr in clone.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;

            var doll = clone.AddComponent<CharacterDoll>();
            doll.idle = idleClip;
            clone.transform.SetParent(parent, false);
            clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            Destroy(holder);
            return doll;
        }

        private void Awake()
        {
            look = GetComponent<CharacterLook>();
            appearance = GetComponent<CharacterAppearance>();
            if (look == null && TryGetComponent<CharacterAppearance>(out _)) look = gameObject.AddComponent<CharacterLook>();
            animancer = GetComponent<AnimancerComponent>();
            if (TryGetComponent<Animator>(out var animator))
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private void OnEnable()
        {
            if (idle != null && animancer != null) animancer.Play(idle);
        }

        public bool Apply(in CharacterRecipe recipe, CharacterCreationDatabase database) =>
            look != null && look.Apply(recipe, database);

        // ---------------- Expressions (faces, idles, emotes) ----------------

        /// <summary>Lets this doll show the library's faces: their blend shapes stay live on its mesh from now on.</summary>
        public void EnableExpressions(CharacterExpressionLibrary library)
        {
            expressions = library;
            if (library == null || appearance == null) return;
            appearance.SetLiveFaceShapes(library.Shapes);
            faceTarget = new float[library.Shapes.Count];
            faceCurrent = new float[library.Shapes.Count];
            shapeIndex = new int[library.Shapes.Count];
            indexedMesh = null;
        }

        /// <summary>Ease into a face of the library (0 is neutral).</summary>
        public void SetFace(int index)
        {
            if (expressions == null || faceTarget == null) return;
            System.Array.Clear(faceTarget, 0, faceTarget.Length);
            if (index < 0 || index >= expressions.Faces.Count) return;
            foreach (var weight in expressions.Faces[index].weights)
                if (weight.shape < faceTarget.Length) faceTarget[weight.shape] = weight.weight;
        }

        /// <summary>Change the looping stance.</summary>
        public void PlayIdle(AnimationClip clip)
        {
            if (clip == null) return;
            idle = clip;
            emoteEndsAt = -1f;
            if (animancer != null && isActiveAndEnabled) animancer.Play(clip, MotionFade);
        }

        /// <summary>Play a gesture once, then go back to the idle.</summary>
        public void PlayEmote(AnimationClip clip)
        {
            if (clip == null || animancer == null || !isActiveAndEnabled) return;
            var state = animancer.Play(clip, MotionFade);
            state.Time = 0f;
            emoteEndsAt = Time.time + Mathf.Max(clip.length - MotionFade, 0.1f);
        }

        private void LateUpdate()
        {
            if (emoteEndsAt >= 0f && Time.time >= emoteEndsAt)
            {
                emoteEndsAt = -1f;
                if (idle != null && animancer != null) animancer.Play(idle, MotionFade);
            }
            BlendFace();
        }

        private void BlendFace()
        {
            if (faceTarget == null || appearance == null) return;
            var renderer = appearance.Renderer;
            var mesh = renderer != null ? renderer.sharedMesh : null;
            if (mesh == null) return;
            if (mesh != indexedMesh)
            {
                // A restyled character has a new mesh: look the shapes up again and put the face back on it.
                indexedMesh = mesh;
                for (int i = 0; i < shapeIndex.Length; i++)
                {
                    shapeIndex[i] = mesh.GetBlendShapeIndex(expressions.Shapes[i]);
                    if (shapeIndex[i] >= 0) renderer.SetBlendShapeWeight(shapeIndex[i], faceCurrent[i]);
                }
            }
            float step = FaceBlendPerSecond * Time.deltaTime;
            for (int i = 0; i < faceTarget.Length; i++)
            {
                if (shapeIndex[i] < 0 || Mathf.Approximately(faceCurrent[i], faceTarget[i])) continue;
                faceCurrent[i] = Mathf.MoveTowards(faceCurrent[i], faceTarget[i], step);
                renderer.SetBlendShapeWeight(shapeIndex[i], faceCurrent[i]);
            }
        }
    }
}
