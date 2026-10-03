using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.UI
{
    /// <summary>
    /// Live top-down view for the minimap: an orthographic camera (north-up, world +Z = texture up) centred on the
    /// local player, rendered into a small RenderTexture a few times a second through the lean "Minimap_Renderer"
    /// (forward, opaque only, no features / post / shadows). The camera is enabled for exactly one frame per capture,
    /// so it costs nothing in between. Unavailable (null) when the active pipeline has no Minimap_Renderer, e.g. the
    /// Mobile tier; the minimap then keeps the baked zone image.
    /// </summary>
    public sealed class MinimapLiveCamera
    {
        public const string RendererName = "Minimap_Renderer";
        private const int Resolution = 512;
        private const float Height = 180f;

        private readonly Camera cam;
        private readonly RenderTexture rt;
        private readonly float interval;
        private float nextCapture;
        private bool capturing;
        private Vector3 pendingCenter;

        public RenderTexture Texture => rt;
        /// <summary>World centre of the frame currently in <see cref="Texture"/>.</summary>
        public Vector3 Center { get; private set; }
        public float Radius { get; }
        public bool HasFrame { get; private set; }

        private MinimapLiveCamera(Camera cam, RenderTexture rt, float radius, float interval)
        {
            this.cam = cam;
            this.rt = rt;
            Radius = radius;
            this.interval = interval;
        }

        public static MinimapLiveCamera TryCreate(float radius, float capturesPerSecond, Transform parent)
        {
            int rendererIndex = FindRenderer();
            if (rendererIndex < 0) return null;

            var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
            {
                name = "RT_MinimapLive",
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();

            var go = new GameObject("Minimap Live Camera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = radius;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = Height + 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.07f, 0.12f, 1f);
            cam.cullingMask = ~LayerMask.GetMask("UI", "UIPreview", "Ignore Raycast", "TransparentFX");
            cam.targetTexture = rt;
            cam.depth = -50f;
            cam.useOcclusionCulling = false;
            cam.enabled = false;

            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.SetRenderer(rendererIndex);
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.antialiasing = AntialiasingMode.None;

            var live = new MinimapLiveCamera(cam, rt, radius, 1f / Mathf.Max(0.5f, capturesPerSecond));
            RenderPipelineManager.beginCameraRendering += live.OnBeginCamera;
            RenderPipelineManager.endCameraRendering += live.OnEndCamera;
            return live;
        }

        // The map camera sits ~180 m above the player: weather distance fog (dense in WP_Fog) would wash the whole
        // map out, so fog is switched off for this camera's render only.
        private bool fogWas;

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam) return;
            fogWas = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        private void OnEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c == cam) RenderSettings.fog = fogWas;
        }

        /// <summary>
        /// Call once per frame. Returns true on the frame a new capture has landed in <see cref="Texture"/>
        /// (the camera renders at the end of the frame it was enabled, so results are read one frame later).
        /// </summary>
        public bool Tick(Vector3 playerPosition)
        {
            if (cam == null) return false;
            if (capturing)
            {
                capturing = false;
                cam.enabled = false;
                Center = pendingCenter;
                HasFrame = true;
                return true;
            }
            if (Time.unscaledTime < nextCapture) return false;
            nextCapture = Time.unscaledTime + interval;
            pendingCenter = playerPosition;
            cam.transform.SetPositionAndRotation(playerPosition + Vector3.up * Height, Quaternion.Euler(90f, 0f, 0f));
            cam.enabled = true;
            capturing = true;
            return false;
        }

        public void Dispose()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
            if (cam != null) Object.Destroy(cam.gameObject);
            if (rt != null)
            {
                rt.Release();
                Object.Destroy(rt);
            }
        }

        private static int FindRenderer()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return -1;
            var list = urp.rendererDataList;
            for (int i = 0; i < list.Length; i++)
                if (list[i] != null && list[i].name == RendererName) return i;
            return -1;
        }
    }
}
