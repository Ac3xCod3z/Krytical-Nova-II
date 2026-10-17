using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Level of detail for a character (players, Hostiles, NPCs, pets), on the machines that draw it. A character is
    /// assembled from parts at run time and animated, so it is not remeshed like the world: with distance from the
    /// camera it skins with fewer bones, then casts no shadow and animates only while seen, then is not drawn or
    /// animated at all (<see cref="WorldLodSettings"/>). The local player is always drawn whole. Does nothing where
    /// nothing is drawn (a dedicated server). Where the game is decided (a host, the offline game) only the drawing
    /// gets lighter: animation is never culled there, because weapon swings and a Hostile's strikes come from it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterLod : MonoBehaviour
    {
        private const float DefaultMid = 25f, DefaultFar = 60f, DefaultHidden = 150f, DefaultRefresh = 0.4f;
        // Parts are added and swapped at run time (gear, the creator): looked for again now and then.
        private const float RecollectSeconds = 5f;

        [SerializeField] private WorldLodSettings settings;
        [Tooltip("Always drawn whole (the local player's own character).")]
        [SerializeField] private bool alwaysWhole;

        private static WorldLodSettings shared;
        private SkinnedMeshRenderer[] skins = System.Array.Empty<SkinnedMeshRenderer>();
        private SkinQuality[] skinQuality = System.Array.Empty<SkinQuality>();
        private ShadowCastingMode[] skinShadows = System.Array.Empty<ShadowCastingMode>();
        private Animator[] animators = System.Array.Empty<Animator>();
        private AnimatorCullingMode[] animatorCulling = System.Array.Empty<AnimatorCullingMode>();
        private int tier = -1;
        private float next, nextCollect;
        private static Camera eye;

        /// <summary>Whether this character is always drawn whole (set for the local player).</summary>
        public bool AlwaysWhole { get => alwaysWhole; set { alwaysWhole = value; next = 0f; } }

        /// <summary>Gives a character its level of detail if it has none (a Hostile, a pet).</summary>
        public static CharacterLod Ensure(GameObject character)
        {
            if (character == null) return null;
            return character.TryGetComponent(out CharacterLod lod) ? lod : character.AddComponent<CharacterLod>();
        }

        private void OnEnable()
        {
            if (settings != null) shared = settings;
            tier = -1;
            next = 0f;
            nextCollect = 0f;
        }

        private void OnDisable() => Apply(0); // whole again, as it was

        private void Update()
        {
            if (Application.isBatchMode || Time.time < next) return;
            WorldLodSettings s = settings != null ? settings : shared;
            next = Time.time + (s != null ? s.characterRefresh : DefaultRefresh);
            if (Time.time >= nextCollect) { Collect(); nextCollect = Time.time + RecollectSeconds; tier = -1; }
            if (eye == null || !eye.isActiveAndEnabled) eye = Camera.main;
            if (eye == null) return;

            float bias = Mathf.Max(0.25f, QualitySettings.lodBias);
            float distance = Vector3.Distance(eye.transform.position, transform.position) / bias;
            int wanted = alwaysWhole ? 0
                : distance >= (s != null ? s.characterHiddenDistance : DefaultHidden) ? 3
                : distance >= (s != null ? s.characterFarDistance : DefaultFar) ? 2
                : distance >= (s != null ? s.characterMidDistance : DefaultMid) ? 1 : 0;
            if (wanted != tier) Apply(wanted);
        }

        private void Collect()
        {
            Apply(0); // the old parts back to how they were before they are forgotten
            skins = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            skinQuality = new SkinQuality[skins.Length];
            skinShadows = new ShadowCastingMode[skins.Length];
            for (int i = 0; i < skins.Length; i++) { skinQuality[i] = skins[i].quality; skinShadows[i] = skins[i].shadowCastingMode; }
            animators = GetComponentsInChildren<Animator>(true);
            animatorCulling = new AnimatorCullingMode[animators.Length];
            for (int i = 0; i < animators.Length; i++) animatorCulling[i] = animators[i].cullingMode;
        }

        private void Apply(int level)
        {
            tier = level;
            for (int i = 0; i < skins.Length; i++)
            {
                SkinnedMeshRenderer skin = skins[i];
                if (skin == null) continue;
                skin.quality = level == 0 ? skinQuality[i] : level == 1 ? SkinQuality.Bone2 : SkinQuality.Bone1;
                skin.shadowCastingMode = level >= 2 ? ShadowCastingMode.Off : skinShadows[i];
                skin.forceRenderingOff = level >= 3;
            }
            bool decidesHere = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
            for (int i = 0; i < animators.Length; i++)
            {
                Animator animator = animators[i];
                if (decidesHere) { if (animator != null) animator.cullingMode = animatorCulling[i]; continue; }
                if (animator == null) continue;
                animator.cullingMode = level >= 3 ? AnimatorCullingMode.CullCompletely
                    : level == 2 ? AnimatorCullingMode.CullUpdateTransforms : animatorCulling[i];
            }
        }
    }
}
