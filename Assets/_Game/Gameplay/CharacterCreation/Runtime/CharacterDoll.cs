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
    }
}
