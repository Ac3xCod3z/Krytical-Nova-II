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
        // Characters hidden by something else (a de-rezzed body and its pet): never shown again by the LOD.
        private static readonly System.Collections.Generic.HashSet<Transform> held = new System.Collections.Generic.HashSet<Transform>();
        private static int holdStamp;
        private int seenHoldStamp = -1;
        private NetworkObject net;
        // Reused lists, refilled every RecollectSeconds (perf clean-ups round 2, chunk 14: four new arrays per character every 5 s).
        private readonly System.Collections.Generic.List<SkinnedMeshRenderer> skins = new System.Collections.Generic.List<SkinnedMeshRenderer>();
        private readonly System.Collections.Generic.List<SkinQuality> skinQuality = new System.Collections.Generic.List<SkinQuality>();
        private readonly System.Collections.Generic.List<ShadowCastingMode> skinShadows = new System.Collections.Generic.List<ShadowCastingMode>();
        private readonly System.Collections.Generic.List<Animator> animators = new System.Collections.Generic.List<Animator>();
        private readonly System.Collections.Generic.List<AnimatorCullingMode> animatorCulling = new System.Collections.Generic.List<AnimatorCullingMode>();
        private int tier = -1;
        private float next, nextCollect;
        private static Camera eye;

        /// <summary>The detail level in use: 0 whole, 1 lighter skinning, 2 no shadow, 3 not drawn (-1 before the first look).</summary>
        public int Tier => tier;

        /// <summary>Whether this character is always drawn whole (set for the local player).</summary>
        public bool AlwaysWhole { get => alwaysWhole; set { alwaysWhole = value; next = 0f; } }

        /// <summary>
        /// Something else decides this character's level (the townsfolk view, by the player's Crowd Detail): its own distance check is
        /// off and <see cref="Drive"/> sets the level.
        /// </summary>
        public bool DrivenExternally { get; set; }

        private bool shadowsOff;

        /// <summary>
        /// Sets the level from outside (<see cref="DrivenExternally"/>): 0 whole, 1 lighter skinning, 2 lightest, 3 not drawn; with
        /// <paramref name="noShadow"/> it casts no shadow at any level. Does nothing when nothing changed.
        /// </summary>
        public void Drive(int level, bool noShadow)
        {
            DrivenExternally = true;
            if (level == tier && noShadow == shadowsOff && seenHoldStamp == holdStamp) return;
            if (skins.Count == 0) Collect();
            shadowsOff = noShadow;
            seenHoldStamp = holdStamp;
            Apply(level);
        }

        /// <summary>
        /// Something else hides this character for a while (the de-rez until the respawn): its LOD leaves it hidden
        /// instead of showing it when it comes nearer.
        /// </summary>
        public static void Hold(Transform character, bool hold)
        {
            if (character == null) return;
            if (hold ? held.Add(character) : held.Remove(character)) holdStamp++; // every LOD looks again
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            held.Clear();
            shared = null;
            eye = null;
        }

        /// <summary>Gives a character its level of detail if it has none (a Hostile, a pet).</summary>
        public static CharacterLod Ensure(GameObject character)
        {
            if (character == null) return null;
            return character.TryGetComponent(out CharacterLod lod) ? lod : character.AddComponent<CharacterLod>();
        }

        private void OnEnable()
        {
            if (settings != null) shared = settings;
            if (shared != null) WorldLodSettings.Use(shared);
            net = GetComponentInParent<NetworkObject>();
            tier = -1;
            next = 0f;
            nextCollect = 0f;
        }

        private void OnDisable() => Apply(0); // whole again, as it was

        private void Update()
        {
            if (DrivenExternally || Application.isBatchMode || Time.time < next) return;
            WorldLodSettings s = settings != null ? settings : shared;
            next = Time.time + (s != null ? s.characterRefresh : DefaultRefresh);
            if (Time.time >= nextCollect) { Collect(); nextCollect = Time.time + RecollectSeconds; tier = -1; }
            if (eye == null || !eye.isActiveAndEnabled) eye = Camera.main;
            if (eye == null) return;

            float bias = Mathf.Max(0.25f, QualitySettings.lodBias);
            float distance = Vector3.Distance(eye.transform.position, transform.position) / bias;
            // The player this machine plays is always whole.
            bool whole = alwaysWhole || (net != null && net.IsSpawned && net.IsOwner && net.IsPlayerObject);
            int wanted = whole ? 0
                : distance >= (s != null ? s.characterHiddenDistance : DefaultHidden) ? 3
                : distance >= (s != null ? s.characterFarDistance : DefaultFar) ? 2
                : distance >= (s != null ? s.characterMidDistance : DefaultMid) ? 1 : 0;
            if (wanted != tier || seenHoldStamp != holdStamp) { seenHoldStamp = holdStamp; Apply(wanted); }
        }

        /// <summary>Looks for the character's parts again now (as every few seconds), whole first; allocates nothing once the lists are big enough.</summary>
        public void Recollect() => Collect();

        private void Collect()
        {
            Apply(0); // the old parts back to how they were before they are forgotten
            GetComponentsInChildren(true, skins);
            skinQuality.Clear();
            skinShadows.Clear();
            for (int i = 0; i < skins.Count; i++) { skinQuality.Add(skins[i].quality); skinShadows.Add(skins[i].shadowCastingMode); }
            GetComponentsInChildren(true, animators);
            animatorCulling.Clear();
            for (int i = 0; i < animators.Count; i++) animatorCulling.Add(animators[i].cullingMode);
        }

        private static bool IsHeld(Transform part)
        {
            if (held.Count == 0) return false;
            for (Transform t = part; t != null; t = t.parent)
                if (held.Contains(t)) return true;
            return false;
        }

        private void Apply(int level)
        {
            tier = level;
            for (int i = 0; i < skins.Count; i++)
            {
                SkinnedMeshRenderer skin = skins[i];
                if (skin == null) continue;
                skin.quality = level == 0 ? skinQuality[i] : level == 1 ? SkinQuality.Bone2 : SkinQuality.Bone1;
                skin.shadowCastingMode = level >= 2 || shadowsOff ? ShadowCastingMode.Off : skinShadows[i];
                skin.forceRenderingOff = level >= 3 || IsHeld(skin.transform);
            }
            bool decidesHere = NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.IsServer;
            for (int i = 0; i < animators.Count; i++)
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
