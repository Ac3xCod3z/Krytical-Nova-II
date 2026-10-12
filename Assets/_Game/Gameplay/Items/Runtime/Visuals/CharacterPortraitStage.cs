using Animancer;
using KryticalNova.CharacterCreation;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The HUD's picture of the local player: head and shoulders, in the armor they wear. A stripped clone of the
    /// player's model (the same kind of doll as the equipment page's, <see cref="CharacterPreviewStage"/>) stands in
    /// its idle far below the world, beside that page's stage, and one small camera draws it into a texture with a
    /// clear background, so the HUD's own portrait frame shows behind it.
    /// <para>It is a still picture (owner, 11 Oct 2026): taken when the player arrives, and again only when the
    /// armor piece changes (the piece, its dye or its Holoweave look) or the character's look is changed at the
    /// Stylist or the Reshaper. Gloves, boots, cape and weapon do not retake it: from this angle they are not what
    /// the picture shows. Between pictures the doll is switched off, so nothing is animated and no camera draws.</para>
    /// </summary>
    public sealed class CharacterPortraitStage : MonoBehaviour
    {
        [Tooltip("Beside the equipment page's stage, out of its camera's sight.")]
        [SerializeField] private Vector3 stageOrigin = new Vector3(40f, -500f, 0f);
        [SerializeField, Min(64)] private int size = 256;
        [SerializeField] private float fieldOfView = 20f;
        [Tooltip("From the camera to the head, in metres: with the field of view, how much of the shoulders is in the picture.")]
        [SerializeField, Min(0.5f)] private float distance = 2.9f; // about a metre of the sitter: head, shoulders and chest, clear of the plate's diamond frame
        [Tooltip("The camera aims this far below the head bone, so the shoulders are in and the head is not cut.")]
        [SerializeField] private float aimBelowHead = 0.16f;
        [Tooltip("The sitter is turned this far from facing the camera.")]
        [SerializeField] private float turn = 16f;
        [Tooltip("Frames from a change to its picture: the new mesh and its colours are on the doll a frame after the " +
                 "player's, and the idle has to have posed it.")]
        [SerializeField, Range(2, 10)] private int framesToSettle = 3;
        [Tooltip("A second picture this many seconds after the first, for a texture that arrived late. 0 takes none.")]
        [SerializeField, Min(0f)] private float secondPictureAfter = 0.6f;
        [Header("Studio ambient (as the equipment page's)")]
        [SerializeField] private Color ambientSky = new Color(0.72f, 0.75f, 0.8f);
        [SerializeField] private Color ambientGround = new Color(0.4f, 0.38f, 0.36f);

        private Camera portraitCamera;
        private RenderTexture texture;
        private GameObject lightRig;
        private GameObject doll;
        private CharacterAppearance dollAppearance;
        private WeaponVisualMount dollWeapon;
        private CharacterAppearance sourceAppearance;
        private CharacterLook sourceLook;
        private ItemDefinition picturedArmor;
        private Color32[] picturedDye;
        private Transform head;
        private AnimationClip idleClip;
        private MaterialPropertyBlock studioAmbient;
        private int previewLayer;
        private bool fogWasOn, shown;
        // A picture is owed: the doll is on and settling, then the camera draws for one frame.
        private bool sitting, drawing, hasPicture, firstTaken;
        private int settleFrames;
        private float secondPictureAt;

        /// <summary>The picture. Its background is clear. Null until someone is drawn.</summary>
        public Texture Texture => texture;
        /// <summary>True once there is a player's model to draw.</summary>
        public bool HasSitter => doll != null;

        public static CharacterPortraitStage Create()
        {
            var go = new GameObject("Character Portrait Stage");
            DontDestroyOnLoad(go);
            return go.AddComponent<CharacterPortraitStage>();
        }

        private void Awake()
        {
            previewLayer = LayerMask.NameToLayer(CharacterPreviewStage.PreviewLayerName);
            if (previewLayer < 0) previewLayer = 31; // still isolated, just unnamed
            transform.position = stageOrigin;
            studioAmbient = CharacterPreviewStage.StudioAmbient(ambientSky, ambientGround);
            lightRig = CharacterPreviewStage.StudioLights(transform, previewLayer);
            BuildCamera();
            lightRig.SetActive(false);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        // The world's fog is global; switch it off only while the portrait camera draws.
        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != portraitCamera) return;
            fogWasOn = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != portraitCamera) return;
            RenderSettings.fog = fogWasOn;
        }

        /// <summary>Points the stage at a (new) local player, or at nobody. Rebuilds the doll only when the player changed.</summary>
        public void Bind(CharacterAppearance appearance, AnimationClip idle)
        {
            if (appearance == sourceAppearance && (doll != null || appearance == null)) return;
            Unwatch();
            sourceAppearance = appearance;
            idleClip = idle;
            if (doll != null) Destroy(doll);
            doll = null;
            head = null;
            sitting = drawing = hasPicture = false;
            portraitCamera.enabled = false;
            if (sourceAppearance != null)
            {
                BuildDoll();
                sourceAppearance.WornChanged += OnWornChanged;
                sourceLook = sourceAppearance.GetComponent<CharacterLook>();
                if (sourceLook != null) sourceLook.Applied += TakePicture;
                Remember();
                doll.SetActive(false);
                if (shown) TakePicture();
            }
        }

        /// <summary>Shows the picture, or stops (no picture is taken while nobody looks at it).</summary>
        public void Show(bool on)
        {
            if (shown == on) return;
            shown = on;
            if (shown && doll != null && !hasPicture) TakePicture();
        }

        // Only the armor piece is in the picture: a change of gloves, boots, cape or weapon is not a reason for a new one.
        private void OnWornChanged()
        {
            if (sourceAppearance == null) return;
            if (sourceAppearance.GetWorn(EquipSlot.Armor) == picturedArmor && ReferenceEquals(sourceAppearance.GetDye(EquipSlot.Armor), picturedDye)) return;
            TakePicture();
        }

        private void Remember()
        {
            picturedArmor = sourceAppearance.GetWorn(EquipSlot.Armor);
            picturedDye = sourceAppearance.GetDye(EquipSlot.Armor);
        }

        private void Unwatch()
        {
            if (sourceAppearance != null) sourceAppearance.WornChanged -= OnWornChanged;
            if (sourceLook != null) sourceLook.Applied -= TakePicture;
            sourceLook = null;
        }

        /// <summary>Has the picture taken again: the doll sits for a few frames, the camera draws once, and both go off.</summary>
        public void TakePicture()
        {
            if (doll == null || sourceAppearance == null) return;
            Remember();
            if (!shown) { hasPicture = false; return; } // taken when it is next looked at
            if (texture == null)
            {
                texture = new RenderTexture(size, size, 24, GraphicsFormat.R8G8B8A8_SRGB) { name = "CharacterPortraitRT", antiAliasing = 4, useMipMap = false };
                texture.Create();
                portraitCamera.targetTexture = texture;
            }
            var main = Camera.main;
            if (main != null) main.cullingMask &= ~(1 << previewLayer);
            if (!doll.activeSelf) doll.SetActive(true);
            if (idleClip != null && doll.TryGetComponent<AnimancerComponent>(out var animancer) && !animancer.IsPlaying(idleClip)) animancer.Play(idleClip);
            sitting = true;
            drawing = firstTaken = false;
            portraitCamera.enabled = false;
            settleFrames = framesToSettle;
        }

        private void Rise()
        {
            sitting = false;
            lightRig.SetActive(false);
            doll.SetActive(false);
        }

        private void LateUpdate()
        {
            if (doll == null) return;
            // The picture is gone with the graphics device (a change of screen mode can do it): take it again.
            if (hasPicture && !sitting && texture != null && !texture.IsCreated()) TakePicture();

            // An open equipment page lights every doll on the preview layer: two sets of lights would burn this one out.
            bool ownLights = sitting && CharacterPreviewStage.OpenCount == 0;
            if (lightRig.activeSelf != ownLights) lightRig.SetActive(ownLights);
            if (!sitting) return;

            if (drawing)
            {
                // The camera drew at the end of the last frame: the texture holds the picture.
                drawing = false;
                portraitCamera.enabled = false;
                hasPicture = true;
                if (firstTaken || secondPictureAfter <= 0f) Rise();
                else
                {
                    firstTaken = true;
                    secondPictureAt = Time.unscaledTime + secondPictureAfter;
                }
                return;
            }
            if (settleFrames > 0) { settleFrames--; return; }
            if (firstTaken && Time.unscaledTime < secondPictureAt) return;

            Vector3 target = head != null ? head.position : doll.transform.position + Vector3.up * 1.6f;
            Vector3 lookAt = target + Vector3.down * aimBelowHead;
            // The doll faces -Z.
            portraitCamera.transform.SetPositionAndRotation(lookAt + Vector3.back * distance, Quaternion.identity);
            portraitCamera.enabled = true;
            drawing = true;
        }

        private void BuildDoll()
        {
            doll = CharacterPreviewStage.CloneDoll(sourceAppearance, transform, out dollAppearance, out dollWeapon);
            // A bust: no weapon in the picture.
            if (dollWeapon != null) dollWeapon.SetWeapon(null);
            doll.transform.localRotation = Quaternion.Euler(0f, 180f + turn, 0f);
            dollAppearance.Rebuilt += Dress;
            dollAppearance.ShareFrom(sourceAppearance);
            Dress();
            Animator animator = doll.GetComponent<Animator>();
            head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null)
                foreach (Transform bone in doll.GetComponentsInChildren<Transform>(true))
                    if (bone.name.ToLowerInvariant().EndsWith("head")) { head = bone; break; }
        }

        private void Dress() => CharacterPreviewStage.ApplyPreviewLayers(doll, previewLayer, studioAmbient);

        private void BuildCamera()
        {
            var go = new GameObject("Portrait Camera");
            go.transform.SetParent(transform, false);
            portraitCamera = go.AddComponent<Camera>();
            portraitCamera.cullingMask = 1 << previewLayer;
            portraitCamera.clearFlags = CameraClearFlags.SolidColor;
            portraitCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            portraitCamera.fieldOfView = fieldOfView;
            portraitCamera.nearClipPlane = 0.1f;
            portraitCamera.farClipPlane = 8f;
            portraitCamera.allowHDR = false;
            portraitCamera.allowMSAA = true;
            portraitCamera.depth = -11f;
            portraitCamera.useOcclusionCulling = false;
            portraitCamera.enabled = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; // keeps the alpha channel and skips a full-screen pass
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.SetRenderer(CharacterPreviewStage.RendererIndex(CharacterPreviewStage.DefaultRendererName));
        }

        private void OnDestroy()
        {
            Unwatch();
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
