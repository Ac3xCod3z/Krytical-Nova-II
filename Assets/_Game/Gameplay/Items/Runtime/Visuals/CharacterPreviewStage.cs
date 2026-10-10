using System.Collections.Generic;
using Animancer;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The equipment page's 3D model. A stripped clone of the local player's model (animation + appearance only: no
    /// physics, IK, hitbox or networking) stands far below the world on its own layer, lit by its own lights on its own
    /// rendering layer (so the day/night sun never darkens it), and is drawn by one dedicated camera into a
    /// RenderTexture shown in UI Toolkit. Everything is disabled while the page is closed: zero cost.
    /// The doll mirrors the player's merged mesh (no second merge) until a try-on gives it its own.
    /// </summary>
    public sealed class CharacterPreviewStage : MonoBehaviour
    {
        public const string PreviewLayerName = "UIPreview";
        internal const int PreviewRenderingLayer = 7;
        internal const string DefaultRendererName = "UIPreview_Renderer";

        /// <summary>
        /// How many of these stages are lit right now. Their lights are directional, so they reach every doll on the
        /// preview layer: a stage of another kind (the HUD portrait) puts its own out while this is above zero.
        /// </summary>
        public static int OpenCount { get; private set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => OpenCount = 0;
        private const int MaxTextureHeight = 1024;

        [SerializeField] private Vector3 stageOrigin = new Vector3(0f, -500f, 0f);
        [Tooltip("URP renderer used by the preview camera, looked up by name in the active pipeline asset. A feature-free " +
                 "renderer (no SSAO / comic outlines) keeps the alpha channel and costs less. Falls back to the default renderer.")]
        [SerializeField] private string previewRendererName = "UIPreview_Renderer";
        [SerializeField] private float fieldOfView = 30f;
        [SerializeField, Min(0.01f)] private float smoothTime = 0.12f;
        [Header("Studio ambient (replaces the world's day/night ambient on the doll)")]
        [SerializeField] private Color ambientSky = new Color(0.72f, 0.75f, 0.8f);
        [SerializeField] private Color ambientGround = new Color(0.4f, 0.38f, 0.36f);

        private Camera previewCamera;
        private RenderTexture texture;
        private GameObject lightRig;
        private GameObject doll;
        private CharacterAppearance dollAppearance;
        private WeaponVisualMount dollWeapon;
        private CharacterAppearance sourceAppearance;
        private PlayerEquipment sourceEquipment;
        private AnimationClip idleClip;
        private int previewLayer;
        private MaterialPropertyBlock studioAmbient;
        private bool fogWasOn;

        // The view maths is shared with the character creator's showroom, so both feel the same.
        private readonly PreviewOrbit orbit = new PreviewOrbit();
        private ItemDefinition tryOnItem;

        public RenderTexture Texture => texture;
        public ItemDefinition TryOnItem => tryOnItem;
        public bool IsOpen => previewCamera != null && previewCamera.enabled;

        public static CharacterPreviewStage Create()
        {
            var go = new GameObject("Character Preview Stage");
            DontDestroyOnLoad(go);
            return go.AddComponent<CharacterPreviewStage>();
        }

        private void Awake()
        {
            previewLayer = LayerMask.NameToLayer(PreviewLayerName);
            if (previewLayer < 0) previewLayer = 31; // still isolated, just unnamed
            orbit.smoothTime = smoothTime;
            orbit.fieldOfView = fieldOfView;
            transform.position = stageOrigin;
            BuildCamera();
            BuildLights();
            BuildStudioAmbient();
            SetActiveParts(false);
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

        // The world's fog is global; switch it off only while the preview camera draws.
        private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != previewCamera) return;
            fogWasOn = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        private void OnEndCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam != previewCamera) return;
            RenderSettings.fog = fogWasOn;
        }

        /// <summary>
        /// Fixed two-tone ambient fed straight to the doll's renderers (CustomProvided light probes), so the
        /// day/night ambient the environment writes every frame never reaches the preview.
        /// </summary>
        private void BuildStudioAmbient() => studioAmbient = StudioAmbient(ambientSky, ambientGround);

        internal static MaterialPropertyBlock StudioAmbient(Color sky, Color ground)
        {
            var sh = new SphericalHarmonicsL2();
            Color mid = Color.Lerp(sky, ground, 0.5f);
            sh.AddAmbientLight(mid);
            sh.AddDirectionalLight(Vector3.up, (sky - mid) * 2f, 1f);
            var block = new MaterialPropertyBlock();
            block.CopySHCoefficientArraysFrom(new List<SphericalHarmonicsL2> { sh });
            return block;
        }

        /// <summary>Point the stage at a (new) local player. Rebuilds the doll only when the source changed.</summary>
        public void Bind(CharacterAppearance appearance, PlayerEquipment equipment, AnimationClip idle)
        {
            if (appearance == sourceAppearance && doll != null) return;
            if (sourceEquipment != null)
            {
                sourceEquipment.SlotChanged -= OnSourceSlotChanged;
                sourceEquipment.TemperChanged -= OnSourceTemperChanged;
            }
            sourceAppearance = appearance;
            sourceEquipment = equipment;
            idleClip = idle;
            if (doll != null) Destroy(doll);
            doll = null;
            tryOnItem = null;
            if (sourceEquipment != null)
            {
                sourceEquipment.SlotChanged += OnSourceSlotChanged;
                sourceEquipment.TemperChanged += OnSourceTemperChanged;
            }
            if (IsOpen)
            {
                BuildDoll();
                SyncFromSource();
                if (idleClip != null && doll != null && doll.TryGetComponent<AnimancerComponent>(out var animancer)) animancer.Play(idleClip);
            }
        }

        public void Open(int width, int height)
        {
            if (sourceAppearance == null) return;
            if (doll == null) BuildDoll();
            Resize(width, height);
            SetActiveParts(true);
            ExcludeFromMainCamera();
            SyncFromSource();
            if (idleClip != null && doll.TryGetComponent<AnimancerComponent>(out var animancer)) animancer.Play(idleClip);
            SnapView();
        }

        public void Close()
        {
            ClearTryOn();
            SetActiveParts(false);
            if (texture != null)
            {
                previewCamera.targetTexture = null;
                texture.Release();
                Destroy(texture);
                texture = null;
            }
        }

        public void Resize(int width, int height)
        {
            if (height > MaxTextureHeight)
            {
                width = Mathf.RoundToInt(width * (MaxTextureHeight / (float)height));
                height = MaxTextureHeight;
            }
            width = Mathf.Max(width, 16);
            height = Mathf.Max(height, 16);
            if (texture != null && texture.width == width && texture.height == height) return;
            if (texture != null)
            {
                previewCamera.targetTexture = null;
                texture.Release();
                Destroy(texture);
            }
            texture = new RenderTexture(width, height, 24, GraphicsFormat.R8G8B8A8_SRGB)
            {
                name = "CharacterPreviewRT",
                antiAliasing = 4,
                useMipMap = false
            };
            texture.Create();
            previewCamera.targetTexture = texture;
            previewCamera.aspect = width / (float)height;
        }

        // ---------------- View control (called by the UI manipulator) ----------------

        public void Rotate(float deltaYaw, float deltaPitch) => orbit.Rotate(deltaYaw, deltaPitch);

        public void Zoom(float delta) => orbit.Zoom(delta);

        /// <summary>
        /// Slide the view by a drag of <paramref name="pixelDelta"/> (UI pixels, y down) on a viewport
        /// <paramref name="viewportHeightPixels"/> tall, so the model tracks the cursor 1:1.
        /// </summary>
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels) => orbit.Pan(pixelDelta, viewportHeightPixels);

        public void ResetView() => orbit.ResetView();

        // ---------------- Try-on ----------------

        /// <summary>Show an item on the doll only. Returns false when the item has no visual to preview.</summary>
        public bool TryOn(ItemDefinition item)
        {
            if (doll == null || item == null) return false;
            switch (item.equipSlot)
            {
                case EquipSlot.Weapon:
                    if (dollWeapon == null) return false;
                    dollWeapon.SetWeapon(item);
                    dollWeapon.SetGlow(false, default, default); // a weapon being tried on is not the tempered one worn
                    ApplyPreviewLayers(doll); // a paired or hip-carried weapon is not under the mount
                    break;
                case EquipSlot.Armor:
                case EquipSlot.Gloves:
                case EquipSlot.Boots:
                case EquipSlot.Cape:
                    if (sourceEquipment != null) dollAppearance.ApplyLoadout(sourceEquipment.Loadout, sourceEquipment.Catalog);
                    dollAppearance.SetSlot(item.equipSlot, item);
                    break;
                default:
                    return false;
            }
            tryOnItem = item;
            return true;
        }

        public void ClearTryOn()
        {
            if (tryOnItem == null) return;
            tryOnItem = null;
            SyncFromSource();
        }

        // ---------------- Internals ----------------

        private void SyncFromSource()
        {
            if (doll == null || sourceAppearance == null) return;
            if (tryOnItem == null) dollAppearance.ShareFrom(sourceAppearance);
            if (dollWeapon != null && (tryOnItem == null || tryOnItem.equipSlot != EquipSlot.Weapon))
            {
                dollWeapon.SetWeapon(sourceEquipment != null ? sourceEquipment.GetDefinition(EquipSlot.Weapon) : null);
                SyncGlow();
                ApplyPreviewLayers(doll);
            }
        }

        /// <summary>
        /// The doll's weapon glows as the worn one does in the world: the colour of the glow band its Temper % is
        /// in, and the look of its rarity (the same call the player's own weapon gets from EquipmentVisualBinder).
        /// </summary>
        private void SyncGlow()
        {
            if (dollWeapon == null) return;
            ItemDefinition weapon = sourceEquipment != null ? sourceEquipment.GetDefinition(EquipSlot.Weapon) : null;
            GearProgressionSettings settings = sourceEquipment != null ? sourceEquipment.Progression : null;
            if (weapon != null && settings != null
                && GearProgressionRules.TryGetGlow(settings.temper, sourceEquipment.GetTemper(EquipSlot.Weapon), weapon.rarity,
                    out Color color, out TemperGlowLook look))
                dollWeapon.SetGlow(true, color, look);
            else
                dollWeapon.SetGlow(false, default, default);
        }

        private void OnSourceTemperChanged(EquipSlot slot)
        {
            if (!IsOpen || slot != EquipSlot.Weapon || doll == null) return;
            if (tryOnItem != null && tryOnItem.equipSlot == EquipSlot.Weapon) return;
            SyncGlow();
            ApplyPreviewLayers(doll); // the glow's own renderers are new
        }

        private void OnSourceSlotChanged(EquipSlot slot, ushort previous, ushort next)
        {
            if (!IsOpen) return;
            // A real equip replaces any try-on, like most games do.
            tryOnItem = null;
            SyncFromSource();
        }

        private void LateUpdate()
        {
            if (!IsOpen || doll == null) return;
            orbit.Tick(Time.deltaTime);
            PlaceCamera();
        }

        private void SnapView()
        {
            orbit.Snap();
            PlaceCamera();
        }

        private void PlaceCamera()
        {
            orbit.Evaluate(transform.position, Quaternion.identity, out var dollRotation, out var cameraPosition, out var cameraRotation);
            doll.transform.localRotation = dollRotation;
            previewCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        private void BuildCamera()
        {
            var go = new GameObject("Preview Camera");
            go.transform.SetParent(transform, false);
            previewCamera = go.AddComponent<Camera>();
            previewCamera.cullingMask = 1 << previewLayer;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            previewCamera.fieldOfView = fieldOfView;
            previewCamera.nearClipPlane = 0.1f;
            previewCamera.farClipPlane = 12f;
            previewCamera.allowHDR = false;
            previewCamera.allowMSAA = true;
            previewCamera.depth = -10f;
            previewCamera.useOcclusionCulling = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; // keeps the alpha channel and skips a full-screen pass
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.SetRenderer(RendererIndex(previewRendererName));
        }

        internal static int RendererIndex(string previewRendererName)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null && list[i].name == previewRendererName) return i;
            }
            return -1;
        }

        private void BuildLights() => lightRig = StudioLights(transform, previewLayer);

        /// <summary>The three studio lights every preview doll is lit by. They light the preview layer only.</summary>
        internal static GameObject StudioLights(Transform parent, int previewLayer)
        {
            var rig = new GameObject("Preview Lights");
            rig.transform.SetParent(parent, false);
            // The doll faces -Z (towards the camera), so key/fill travel +Z and the rim travels -Z from behind.
            AddLight(rig, previewLayer, "Key", new Vector3(35f, -30f, 0f), 2.2f, new Color(1f, 0.96f, 0.9f));
            AddLight(rig, previewLayer, "Fill", new Vector3(15f, 40f, 0f), 0.85f, new Color(0.7f, 0.82f, 1f));
            AddLight(rig, previewLayer, "Rim", new Vector3(20f, 165f, 0f), 1.0f, new Color(0.6f, 1f, 0.9f));
            return rig;
        }

        private static void AddLight(GameObject lightRig, int previewLayer, string name, Vector3 euler, float intensity, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(lightRig.transform, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.color = color;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << previewLayer;
            light.renderingLayerMask = 1 << PreviewRenderingLayer; // never lights the world
        }

        private void BuildDoll()
        {
            if (sourceAppearance == null) return;
            doll = CloneDoll(sourceAppearance, transform, out dollAppearance, out dollWeapon);
            dollAppearance.Rebuilt += () => ApplyPreviewLayers(doll);
            ApplyPreviewLayers(doll);
            doll.SetActive(IsOpen || previewCamera.enabled);
        }

        /// <summary>
        /// A stripped clone of a player's model under <paramref name="parent"/>: its look and animation only, no
        /// physics, IK, hitbox or networking.
        /// </summary>
        internal static GameObject CloneDoll(CharacterAppearance sourceAppearance, Transform parent, out CharacterAppearance dollAppearance, out WeaponVisualMount dollWeapon)
        {
            // Clone under an inactive holder so no Awake runs before the gameplay components are stripped.
            var holder = new GameObject("Doll Holder");
            holder.SetActive(false);
            var clone = Instantiate(sourceAppearance.gameObject, holder.transform);
            clone.name = "Preview Doll";
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb is CharacterAppearance || mb is WeaponVisualMount || mb is AnimancerComponent || mb is IPreviewDollPart) continue;
                DestroyImmediate(mb);
            }
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in clone.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);

            dollWeapon = clone.GetComponentInChildren<WeaponVisualMount>(true);
            if (dollWeapon != null) dollWeapon.ClearInherited(); // the original's weapon models, wherever they were riding

            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = Vector3.zero;
            Destroy(holder);
            if (clone.TryGetComponent<Animator>(out var animator))
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
            dollAppearance = clone.GetComponent<CharacterAppearance>();
            foreach (var part in clone.GetComponentsInChildren<IPreviewDollPart>(true)) part.BindToOriginal(sourceAppearance.gameObject);
            return clone;
        }

        private void ApplyPreviewLayers(GameObject root) => ApplyPreviewLayers(root, previewLayer, studioAmbient);

        internal static void ApplyPreviewLayers(GameObject root, int previewLayer, MaterialPropertyBlock studioAmbient)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = previewLayer;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.renderingLayerMask = 1u << PreviewRenderingLayer;
                r.lightProbeUsage = LightProbeUsage.CustomProvided;
                r.SetPropertyBlock(studioAmbient);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
        }

        private void ExcludeFromMainCamera()
        {
            var main = Camera.main;
            if (main != null) main.cullingMask &= ~(1 << previewLayer);
        }

        private void SetActiveParts(bool active)
        {
            if (previewCamera.enabled != active) OpenCount = Mathf.Max(0, OpenCount + (active ? 1 : -1));
            previewCamera.enabled = active;
            lightRig.SetActive(active);
            if (doll != null) doll.SetActive(active);
        }

        private void OnDestroy()
        {
            if (previewCamera != null && previewCamera.enabled) OpenCount = Mathf.Max(0, OpenCount - 1);
            if (sourceEquipment != null)
            {
                sourceEquipment.SlotChanged -= OnSourceSlotChanged;
                sourceEquipment.TemperChanged -= OnSourceTemperChanged;
            }
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
