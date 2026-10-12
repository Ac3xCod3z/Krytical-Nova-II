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
    /// It also switches the upscaler (DLSS on an NVIDIA RTX card, FSR 2 on any): the pipeline has both behind its
    /// upscaler framework, each with an options asset inside the pipeline asset (Krytical Nova ▸ Lighting ▸ Set Up
    /// Upscalers puts them there). While one is on, it decides the render scale and the pipeline does the
    /// anti-aliasing it needs by itself.
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
        // The upscaler the asset names, and each options asset as it was: put back like the rest.
        private string authoredUpscaler, authoredDlssOptions, authoredFsr2Options;

        /// <summary>The upscaler that is on now: what the player chose, or what stands in for it on this card.</summary>
        public static UpscalerChoice UpscalerInForce { get; private set; }

        /// <summary>True on an NVIDIA RTX card with DLSS, when the pipeline asset has its options.</summary>
        public static bool DlssAvailable
        {
            get
            {
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && KN_NVIDIA_MODULE
                if (Options<DLSSOptions>() == null) return false;
                if (!SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia") || !UnityEngine.NVIDIA.NVUnityPlugin.IsLoaded()) return false;
                UnityEngine.NVIDIA.GraphicsDevice device = UnityEngine.NVIDIA.GraphicsDevice.device;
                return device != null && device.IsFeatureAvailable(UnityEngine.NVIDIA.GraphicsDeviceFeature.DLSS);
#else
                return false;
#endif
            }
        }

        /// <summary>True on any card the AMD plugin loads on, when the pipeline asset has its options.</summary>
        public static bool Fsr2Available
        {
            get
            {
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_AMD && KN_AMD_MODULE
                return Options<FSR2Options>() != null && UnityEngine.AMD.AMDUnityPlugin.IsLoaded();
#else
                return false;
#endif
            }
        }

#if ENABLE_UPSCALER_FRAMEWORK
        private static T Options<T>() where T : UpscalerOptions
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null || asset.upscalerOptions == null) return null;
            foreach (UpscalerOptions options in asset.upscalerOptions)
                if (options is T typed) return typed;
            return null;
        }
#endif

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
                RememberUpscalers();
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
                RestoreUpscalers();
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
            int cap = GraphicsPreferences.FrameCapInForce;
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
                // An upscaler draws at the size its quality mode asks for: the render scale option is the pipeline's
                // own again when none is on.
                bool upscaling = ApplyUpscaler();
                pipeline.renderScale = upscaling ? authoredRenderScale : authoredRenderScale * GraphicsPreferences.RenderScale;
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
            // Numbers before and after a change of options are not to be compared: the log says what is in force.
            if (KryticalNova.Diagnostics.PerformanceLog.IsRecording)
            {
                KryticalNova.Diagnostics.PerformanceLog.Change("graphics", System.FormattableString.Invariant(
                    $"{Screen.width}x{Screen.height} {GraphicsPreferences.Window} preset={GraphicsPreferences.Preset} upscaler={UpscalerInForce} quality={GraphicsPreferences.UpscalerQuality} scale={GraphicsPreferences.RenderScale:0.00} shadows={GraphicsPreferences.Shadows} aa={GraphicsPreferences.AntiAliasing} grass={GraphicsPreferences.GrassDensity:0.00} fog={GraphicsPreferences.VolumetricFog} shafts={GraphicsPreferences.SunShafts} outlines={GraphicsPreferences.ComicOutlines} vsync={GraphicsPreferences.VSync} cap={GraphicsPreferences.FrameCapInForce}"));
            }
        }

        // ------------------------------------------------------------------ upscalers

        private void RememberUpscalers()
        {
#if ENABLE_UPSCALER_FRAMEWORK
            authoredUpscaler = pipeline.upscalerName;
#if ENABLE_NVIDIA && KN_NVIDIA_MODULE
            DLSSOptions dlss = Options<DLSSOptions>();
            authoredDlssOptions = dlss != null ? JsonUtility.ToJson(dlss) : null;
#endif
#if ENABLE_AMD && KN_AMD_MODULE
            FSR2Options fsr2 = Options<FSR2Options>();
            authoredFsr2Options = fsr2 != null ? JsonUtility.ToJson(fsr2) : null;
#endif
#endif
        }

        private void RestoreUpscalers()
        {
            UpscalerInForce = UpscalerChoice.Off;
#if ENABLE_UPSCALER_FRAMEWORK
            if (authoredUpscaler != null) pipeline.upscalerName = authoredUpscaler;
#if ENABLE_NVIDIA && KN_NVIDIA_MODULE
            DLSSOptions dlss = Options<DLSSOptions>();
            if (dlss != null && authoredDlssOptions != null) JsonUtility.FromJsonOverwrite(authoredDlssOptions, dlss);
#endif
#if ENABLE_AMD && KN_AMD_MODULE
            FSR2Options fsr2 = Options<FSR2Options>();
            if (fsr2 != null && authoredFsr2Options != null) JsonUtility.FromJsonOverwrite(authoredFsr2Options, fsr2);
#endif
#endif
        }

        /// <summary>
        /// Puts the chosen upscaler in force and says whether one is. DLSS chosen on a card that cannot run it (a
        /// saved choice carried to another PC) becomes FSR 2; with neither to be had, none.
        /// </summary>
        private bool ApplyUpscaler()
        {
            UpscalerChoice choice = GraphicsPreferences.Upscaler;
            if (choice == UpscalerChoice.Dlss && !DlssAvailable) choice = UpscalerChoice.Fsr2;
            if (choice == UpscalerChoice.Fsr2 && !Fsr2Available) choice = UpscalerChoice.Off;
            UpscalerInForce = choice;
#if ENABLE_UPSCALER_FRAMEWORK
            UpscalerQuality quality = GraphicsPreferences.UpscalerQuality;
#if ENABLE_NVIDIA && KN_NVIDIA_MODULE
            if (choice == UpscalerChoice.Dlss)
            {
                DLSSOptions options = Options<DLSSOptions>();
                options.resolutionMode = UpscalerResolutionMode.QualityMode;
                options.dlssQualityMode = DlssMode(quality);
                // The model each mode runs on. NVIDIA's own pairing for DLSS 4.5: its second transformer model on
                // the two modes that gain from it (M for Performance, L for Ultra Performance), the DLSS 4 model
                // (K) on the rest. On RTX 20 and 30 cards the 4.5 models cost more than they give, so K throughout.
                bool olderCard = OlderRtx(SystemInfo.graphicsDeviceName);
                options.dlssRenderPresetDLAA = UnityEngine.NVIDIA.DLSSPreset.Preset_K;
                options.dlssRenderPresetQuality = UnityEngine.NVIDIA.DLSSPreset.Preset_K;
                options.dlssRenderPresetBalanced = UnityEngine.NVIDIA.DLSSPreset.Preset_K;
                options.dlssRenderPresetPerformance = olderCard ? UnityEngine.NVIDIA.DLSSPreset.Preset_K : UnityEngine.NVIDIA.DLSSPreset.Preset_M;
                options.dlssRenderPresetUltraPerformance = olderCard ? UnityEngine.NVIDIA.DLSSPreset.Preset_K : UnityEngine.NVIDIA.DLSSPreset.Preset_L;
                pipeline.upscalerName = options.upscalerName;
                return true;
            }
#endif
#if ENABLE_AMD && KN_AMD_MODULE
            if (choice == UpscalerChoice.Fsr2)
            {
                FSR2Options options = Options<FSR2Options>();
                // FSR 2 has no native mode of its own: at Native AA it runs on the pipeline's full-size picture.
                options.resolutionMode = quality == UpscalerQuality.NativeAA ? UpscalerResolutionMode.CustomScaling : UpscalerResolutionMode.QualityMode;
                options.fsr2QualityMode = Fsr2Mode(quality);
                pipeline.upscalerName = options.upscalerName;
                return true;
            }
#endif
            if (authoredUpscaler != null) pipeline.upscalerName = authoredUpscaler;
#endif
            return false;
        }

#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && KN_NVIDIA_MODULE
        private static UnityEngine.NVIDIA.DLSSQuality DlssMode(UpscalerQuality quality)
        {
            switch (quality)
            {
                case UpscalerQuality.NativeAA: return UnityEngine.NVIDIA.DLSSQuality.DLAA;
                case UpscalerQuality.Balanced: return UnityEngine.NVIDIA.DLSSQuality.Balanced;
                case UpscalerQuality.Performance: return UnityEngine.NVIDIA.DLSSQuality.MaximumPerformance;
                case UpscalerQuality.UltraPerformance: return UnityEngine.NVIDIA.DLSSQuality.UltraPerformance;
                default: return UnityEngine.NVIDIA.DLSSQuality.MaximumQuality;
            }
        }
#endif

#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_AMD && KN_AMD_MODULE
        private static UnityEngine.AMD.FSR2Quality Fsr2Mode(UpscalerQuality quality)
        {
            switch (quality)
            {
                case UpscalerQuality.Balanced: return UnityEngine.AMD.FSR2Quality.Balanced;
                case UpscalerQuality.Performance: return UnityEngine.AMD.FSR2Quality.Performance;
                case UpscalerQuality.UltraPerformance: return UnityEngine.AMD.FSR2Quality.UltraPerformance;
                default: return UnityEngine.AMD.FSR2Quality.Quality;
            }
        }
#endif

        /// <summary>An RTX 20 or 30 series card, by its name: the first two generations, which run the newest DLSS models slowly.</summary>
        public static bool OlderRtx(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName)) return false;
            string name = deviceName.ToUpperInvariant();
            return name.Contains("RTX 20") || name.Contains("RTX 30") || name.Contains("TITAN RTX");
        }

        /// <summary>Anti-aliasing belongs to the camera, and every scene brings its own.</summary>
        private static void ApplyCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || !camera.TryGetComponent(out UniversalAdditionalCameraData data)) return;
            // An upscaler smooths the edges itself, from the same frames it rebuilds the picture from: the pipeline
            // switches that on for it, and a second pass over its result would only blur it.
            if (UpscalerInForce != UpscalerChoice.Off)
            {
                data.antialiasing = AntialiasingMode.None;
                return;
            }
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
