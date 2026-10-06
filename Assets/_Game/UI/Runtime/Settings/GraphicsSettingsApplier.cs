using KryticalNova.Rendering;
using KryticalNova.Vegetation.Grass;
using KryticalNova.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KryticalNova.UI
{
    /// <summary>
    /// Puts <see cref="GraphicsPreferences"/> in force: at boot (so the front-end scenes get them too), whenever a
    /// scene loads (the camera is new) and whenever an option changes. Lives on its own object for the whole session.
    /// The render pipeline asset is shared project data, so the values it had at boot are put back when the session
    /// ends: in the Editor a Play session would otherwise leave the asset changed on disk.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GraphicsSettingsApplier : MonoBehaviour
    {
        /// <summary>Main-light shadows by quality: how far they reach, the map they are drawn into, and its cascades.</summary>
        private readonly struct ShadowRow
        {
            public readonly float Distance;
            public readonly int Resolution, Cascades;
            public ShadowRow(float distance, int resolution, int cascades) { Distance = distance; Resolution = resolution; Cascades = cascades; }
        }

        // High is the authored asset (50 m, 4096, 4 cascades): it is read from the asset at boot, not from here.
        private static readonly ShadowRow[] ShadowRows =
        {
            new ShadowRow(0f, 1024, 1),    // Off
            new ShadowRow(22f, 1024, 1),   // Low
            new ShadowRow(36f, 2048, 2),   // Medium
            new ShadowRow(50f, 4096, 4),   // High
            new ShadowRow(80f, 4096, 4),   // Ultra
        };

        private static GraphicsSettingsApplier instance;

        private UniversalRenderPipelineAsset pipeline;
        private float authoredRenderScale, authoredShadowDistance;
        private int authoredShadowResolution, authoredShadowCascades;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode) return;
            var host = new GameObject("Graphics Settings");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<GraphicsSettingsApplier>();
        }

        private void OnEnable()
        {
            pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                authoredRenderScale = pipeline.renderScale;
                authoredShadowDistance = pipeline.shadowDistance;
                authoredShadowResolution = pipeline.mainLightShadowmapResolution;
                authoredShadowCascades = pipeline.shadowCascadeCount;
            }
            GraphicsPreferences.Changed += Apply;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
            ApplyDisplay();
        }

        private void OnDisable()
        {
            GraphicsPreferences.Changed -= Apply;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (pipeline != null)
            {
                pipeline.renderScale = authoredRenderScale;
                pipeline.shadowDistance = authoredShadowDistance;
                pipeline.mainLightShadowmapResolution = authoredShadowResolution;
                pipeline.shadowCascadeCount = authoredShadowCascades;
            }
            SunShaftRuntime.UserEnabled = true;
            ComicStyleRuntime.OutlinesEnabled = true;
            VolumetricAtmosphereDriver.UserEnabled = true;
            GrassRenderer.DensityMultiplier = 1f;
            if (instance == this) instance = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyCamera();

        /// <summary>The resolution and window mode, which only a built player has. Asked for by the Settings window.</summary>
        public static void ApplyDisplay()
        {
            QualitySettings.vSyncCount = GraphicsPreferences.VSync ? 1 : 0;
            int cap = GraphicsPreferences.FrameCap;
            Application.targetFrameRate = GraphicsPreferences.VSync || cap <= 0 ? -1 : cap;
#if !UNITY_EDITOR
            Vector2Int size = GraphicsPreferences.Resolution;
            if (size.x <= 0 || size.y <= 0)
            {
                Resolution native = Screen.currentResolution;
                size = new Vector2Int(native.width, native.height);
            }
            FullScreenMode mode = GraphicsPreferences.Window == WindowChoice.Fullscreen ? FullScreenMode.ExclusiveFullScreen
                : GraphicsPreferences.Window == WindowChoice.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.width != size.x || Screen.height != size.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(size.x, size.y, mode);
#endif
        }

        private void Apply()
        {
            if (pipeline != null)
            {
                pipeline.renderScale = authoredRenderScale * GraphicsPreferences.RenderScale;
                ShadowQuality quality = GraphicsPreferences.Shadows;
                if (quality == ShadowQuality.High)
                {
                    pipeline.shadowDistance = authoredShadowDistance;
                    pipeline.mainLightShadowmapResolution = authoredShadowResolution;
                    pipeline.shadowCascadeCount = authoredShadowCascades;
                }
                else
                {
                    ShadowRow row = ShadowRows[(int)quality];
                    pipeline.shadowDistance = row.Distance;
                    pipeline.mainLightShadowmapResolution = row.Resolution;
                    pipeline.shadowCascadeCount = row.Cascades;
                }
            }
            SunShaftRuntime.UserEnabled = GraphicsPreferences.SunShafts;
            ComicStyleRuntime.OutlinesEnabled = GraphicsPreferences.ComicOutlines;
            VolumetricAtmosphereDriver.UserEnabled = GraphicsPreferences.VolumetricFog;
            GrassRenderer.DensityMultiplier = GraphicsPreferences.GrassDensity;
            ApplyCamera();
            ApplyDisplay();
        }

        /// <summary>Anti-aliasing belongs to the camera, and every scene brings its own.</summary>
        private static void ApplyCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
            switch (GraphicsPreferences.AntiAliasing)
            {
                case AntiAliasingChoice.Off: data.antialiasing = AntialiasingMode.None; break;
                case AntiAliasingChoice.Fxaa: data.antialiasing = AntialiasingMode.FastApproximateAntialiasing; break;
                default:
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
                    break;
            }
        }
    }
}
