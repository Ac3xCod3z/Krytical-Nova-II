using Animancer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The HUD's live picture of the local player: head and shoulders, in what they wear now. A stripped clone of
    /// the player's model (the same kind of doll as the equipment page's, <see cref="CharacterPreviewStage"/>) stands
    /// in its idle far below the world, beside that page's stage, and one small camera draws it into a texture with
    /// a clear background, so the HUD's own portrait frame shows behind it. The doll shares the player's merged
    /// mesh, so a change of gear or look shows at once and costs no second merge.
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
        private Transform head;
        private AnimationClip idleClip;
        private MaterialPropertyBlock studioAmbient;
        private int previewLayer;
        private bool fogWasOn, shown;
        private Vector3 aim;
        private bool aimed;

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
            Apply();
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
            sourceAppearance = appearance;
            idleClip = idle;
            if (doll != null) Destroy(doll);
            doll = null;
            head = null;
            aimed = false;
            if (sourceAppearance != null) BuildDoll();
            Apply();
        }

        /// <summary>Draws the picture, or stops (nothing is animated or drawn while it is not looked at).</summary>
        public void Show(bool on)
        {
            if (shown == on) return;
            shown = on;
            Apply();
        }

        private void Apply()
        {
            bool live = shown && doll != null;
            if (live && texture == null)
            {
                texture = new RenderTexture(size, size, 24, GraphicsFormat.R8G8B8A8_SRGB) { name = "CharacterPortraitRT", antiAliasing = 4, useMipMap = false };
                texture.Create();
                portraitCamera.targetTexture = texture;
            }
            portraitCamera.enabled = live;
            if (doll != null) doll.SetActive(live);
            if (live)
            {
                var main = Camera.main;
                if (main != null) main.cullingMask &= ~(1 << previewLayer);
                if (idleClip != null && doll.TryGetComponent<AnimancerComponent>(out var animancer) && !animancer.IsPlaying(idleClip)) animancer.Play(idleClip);
            }
        }

        private void LateUpdate()
        {
            // An open equipment page lights every doll on the preview layer: two sets of lights would burn this one out.
            bool ownLights = shown && doll != null && CharacterPreviewStage.OpenCount == 0;
            if (lightRig.activeSelf != ownLights) lightRig.SetActive(ownLights);
            if (!shown || doll == null) return;

            // The camera keeps to where the head rests: the idle's sway is seen, a new body's height is followed.
            Vector3 target = head != null ? head.position : doll.transform.position + Vector3.up * 1.6f;
            aim = aimed ? Vector3.Lerp(aim, target, 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime)) : target;
            aimed = true;
            Vector3 lookAt = aim + Vector3.down * aimBelowHead;
            // The doll faces -Z.
            portraitCamera.transform.SetPositionAndRotation(lookAt + Vector3.back * distance, Quaternion.identity);
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
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
