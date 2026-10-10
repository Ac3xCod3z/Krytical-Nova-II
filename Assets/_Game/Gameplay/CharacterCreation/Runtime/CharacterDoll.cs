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
        private FaceBlinker blinker;

        // Expressions: the face eases towards a target set of blend-shape weights; a stance is entered and then held
        // in its loop; an emote plays once, then the stance returns.
        private const float FaceBlendPerSecond = 500f;
        private const float MotionFade = 0.25f;
        private const string EyelidShapePrefix = "eyeBlink";
        private CharacterExpressionLibrary expressions;
        private float[] faceTarget, faceCurrent;
        private int[] shapeIndex;
        private Mesh indexedMesh;
        private AnimancerState idleState;   // the stance's loop, while it is what plays
        private AnimancerState onceState;   // a stance's way in, or an emote: played once, then the loop takes over

        // Props: some motions are performed with something in the right hand. The clips animate the rig's prop bone
        // themselves, so the prop only has to ride on it.
        private const string PropBoneName = "prop_r";
        private const float PropGrowSeconds = 0.15f;
        private Transform propBone;
        private GameObject stanceProp;      // what the current stance holds (null for most)
        private GameObject shownProp;       // the instance in the hand now
        private GameObject shownPropSource; // the prefab it came from
        private Vector3 shownPropScale;
        private float propGrown = 1f;

        public CharacterLook Look => look;
        public CharacterAppearance Appearance => appearance;
        /// <summary>Where this doll carries a weapon. Only a doll made to keep it has one.</summary>
        public WeaponVisualMount Weapon { get; private set; }
        /// <summary>A stance's way in or an emote is playing; the doll is not yet back in its loop.</summary>
        public bool IsBusy => onceState != null;

        /// <summary>Clone <paramref name="modelPrefab"/> (the player's Sidekick model) under <paramref name="parent"/>.</summary>
        /// <param name="keepWeapon">The doll keeps the model's weapon mount, empty: it can be given a weapon to show (the faction showcase).</param>
        public static CharacterDoll Create(GameObject modelPrefab, Transform parent, AnimationClip idleClip, bool keepWeapon = false)
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
                if (keepWeapon && behaviour is WeaponVisualMount) continue;
                DestroyImmediate(behaviour);
            }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            // Weapons and other rigid props are not part of the look being edited.
            foreach (var mr in clone.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = false;

            WeaponVisualMount mount = keepWeapon ? clone.GetComponentInChildren<WeaponVisualMount>(true) : null;
            if (mount != null) mount.ClearInherited(); // the model's own weapon: the mount shows what it is given
            var doll = clone.AddComponent<CharacterDoll>();
            doll.idle = idleClip;
            doll.Weapon = mount;
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
            blinker = GetComponent<FaceBlinker>();
            foreach (var bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone.name != PropBoneName) continue;
                propBone = bone;
                break;
            }
            if (TryGetComponent<Animator>(out var animator))
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        private void OnEnable()
        {
            // Whatever was playing when the doll was hidden is gone: it stands in its stance again.
            onceState = null;
            idleState = idle != null && animancer != null ? animancer.Play(idle) : null;
            HoldProp(stanceProp);
        }

        private void OnDisable()
        {
            onceState = null;
            idleState = null;
            HoldProp(null);
        }

        /// <summary>Puts a prop in the right hand (growing in rather than popping), or empties the hand.</summary>
        private void HoldProp(GameObject prefab)
        {
            if (prefab == shownPropSource && (shownProp != null || prefab == null)) return;
            if (shownProp != null) Destroy(shownProp);
            shownProp = null;
            shownPropSource = prefab;
            if (prefab == null || propBone == null) return;

            shownProp = Instantiate(prefab, propBone);
            shownProp.name = prefab.name;
            shownProp.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var part in shownProp.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = gameObject.layer;
            shownPropScale = prefab.transform.localScale;
            shownProp.transform.localScale = Vector3.zero;
            propGrown = 0f;
        }

        /// <param name="dressed">
        /// Stand in the lineage's basic outfit where the doll is given no gear (the creator, a kiosk's keeper). False
        /// for a doll that shows what a character really wears: its empty slots are the bare body.
        /// </param>
        public bool Apply(in CharacterRecipe recipe, CharacterCreationDatabase database, bool dressed = true)
        {
            if (look == null) return false;
            look.Dressed = dressed;
            return look.Apply(recipe, database);
        }

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
            bool usesEyelids = false;
            if (index >= 0 && index < expressions.Faces.Count)
            {
                foreach (var weight in expressions.Faces[index].weights)
                {
                    if (weight.shape >= faceTarget.Length) continue;
                    faceTarget[weight.shape] = weight.weight;
                    if (weight.weight > 0f && expressions.Shapes[weight.shape].StartsWith(EyelidShapePrefix)) usesEyelids = true;
                }
            }
            // The blinker writes the eyelid shapes too and leaves them open after a blink: a face that holds the lids
            // (a wink, a squint) would lose them at the first blink. Such a face does its own lids.
            if (blinker != null && blinker.enabled == usesEyelids)
            {
                blinker.enabled = !usesEyelids;
                indexedMesh = null; // put every shape back, whatever the blinker left behind
            }
        }

        /// <summary>
        /// Change the looping stance, going through its way in when it has one. <paramref name="prop"/> is held in
        /// the right hand for as long as the stance lasts.
        /// </summary>
        public void PlayIdle(AnimationClip clip, AnimationClip enter = null, GameObject prop = null)
        {
            if (clip == null) return;
            idle = clip;
            stanceProp = prop;
            if (animancer == null || !isActiveAndEnabled) return;
            if (enter != null)
            {
                idleState = null;
                onceState = animancer.Play(enter, MotionFade, FadeMode.FromStart);
                HoldProp(prop); // the way in is where the prop comes out
            }
            else HoldIdle();
        }

        /// <summary>
        /// Play a gesture once, then go back to the stance. Playing it again starts it over. <paramref name="prop"/>
        /// is held in the right hand until the gesture ends; a gesture without one is done empty-handed.
        /// </summary>
        public void PlayEmote(AnimationClip clip, GameObject prop = null)
        {
            if (clip == null || animancer == null || !isActiveAndEnabled) return;
            idleState = null;
            onceState = animancer.Play(clip, MotionFade, FadeMode.FromStart);
            HoldProp(prop);
        }

        private void HoldIdle()
        {
            onceState = null;
            idleState = idle != null ? animancer.Play(idle, MotionFade) : null;
            HoldProp(stanceProp);
        }

        private void LateUpdate()
        {
            if (animancer != null)
            {
                // Follow the clip's own time, so this holds whatever the time scale is.
                if (onceState != null && (onceState.Time >= onceState.Length - MotionFade || onceState.Weight <= 0f && !onceState.IsPlaying))
                    HoldIdle();
                // Many of the pack's stance loops are not marked as looping: the doll wraps them itself.
                else if (idleState != null && !idleState.IsLooping && idleState.Time >= idleState.Length)
                    idleState.Time -= idleState.Length;
            }
            if (shownProp != null && propGrown < 1f)
            {
                propGrown = Mathf.Min(1f, propGrown + Time.unscaledDeltaTime / PropGrowSeconds);
                shownProp.transform.localScale = shownPropScale * Mathf.SmoothStep(0f, 1f, propGrown);
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
