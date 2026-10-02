using KryticalNova.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.World
{
    /// <summary>
    /// Client-side presentation of the world clock + weather: drives the single directional light
    /// (sun by day, moon by night), ambient, fog, sky-shader globals, cloud shadows, post colour and the
    /// comic shadow style. One component, no per-frame allocations, never runs on a headless server.
    /// In edit mode it previews <see cref="previewHours"/> / <see cref="previewWeather"/> for art direction.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)]
    public sealed class EnvironmentPresenter : MonoBehaviour
    {
        private const string LowQualityKeyword = "_KN_SKY_LOW";

        [Header("Scene references")]
        [Tooltip("The scene's one directional light. It becomes the moon at night (never two shadowed lights).")]
        [SerializeField] private Light keyLight;
        [SerializeField] private Material skyMaterial;
        [Tooltip("Global volume whose runtime profile copy receives Color Adjustments.")]
        [SerializeField] private Volume postVolume;
        [SerializeField] private Texture2D cloudNoise;
        [Tooltip("Stylized sun art (PNG with alpha). Empty = procedural disc.")]
        [SerializeField] private Texture2D sunTexture;
        [Tooltip("Stylized moon art (PNG with alpha). Empty = procedural crescent.")]
        [SerializeField] private Texture2D moonTexture;
        [SerializeField] private LightningController lightning;
        [Tooltip("Comic lens flare on the key light while it is the sun (Krytical Nova > World > Build Comic Lens Flare).")]
        [SerializeField] private LensFlareDataSRP sunFlare;
        [Tooltip("Flare brightness multiplier on top of the day cycle's flare curve.")]
        [Range(0f, 4f)] [SerializeField] private float sunFlareStrength = 1f;

        [Header("Clouds")]
        [Tooltip("Height of the cloud plane in metres (parallax + shadow projection).")]
        [Min(100f)] [SerializeField] private float cloudHeight = 2000f;
        [Tooltip("World metres covered by one tile of the noise texture.")]
        [Min(100f)] [SerializeField] private float cloudTileSize = 7000f;
        [Tooltip("Detail noise tiling relative to the shape noise. Must be an integer to wrap seamlessly.")]
        [Range(1, 8)] [SerializeField] private int detailScale = 2;
        [Range(0f, 1f)] [SerializeField] private float detailErosion = 0.2f;
        [Tooltip("Detail noise scrolls this much faster than the shapes, so clouds slowly evolve.")]
        [Range(1f, 3f)] [SerializeField] private float detailWindMultiplier = 1.6f;

        [Header("Cloud shadows (ComicPost)")]
        [SerializeField] private bool cloudShadows = true;
        [Tooltip("Mip level sampled for ground shadows. Higher = softer shadow edges.")]
        [Range(0f, 6f)] [SerializeField] private float cloudShadowSoftness = 2.5f;

        [Header("Light")]
        [Tooltip("Seconds for the presented clock to absorb a small network time correction. Larger = smoother, slower to converge.")]
        [Range(0.1f, 10f)] [SerializeField] private float clockSmoothingSeconds = 2f;
        [Tooltip("A clock error larger than this (fraction of a day) snaps instead of easing, e.g. after time.set. 0.01 ≈ 15 game minutes.")]
        [Range(0.001f, 0.1f)] [SerializeField] private float clockSnapThreshold01 = 0.01f;
        [Tooltip("Shadows fade out as the key light's elevation (sin) drops from Fade End to Fade Start, so very long grazing shadows don't crawl across the map.")]
        [Range(0f, 0.3f)] [SerializeField] private float lowSunShadowFadeStart = 0.02f;
        [Range(0f, 0.5f)] [SerializeField] private float lowSunShadowFadeEnd = 0.08f;
        [Tooltip("Below this shadow amount the light stops casting shadows entirely (skips the shadow pass).")]
        [Range(0f, 0.3f)] [SerializeField] private float shadowsOffBelow = 0.04f;
        [Tooltip("Light.shadowStrength floor. Keeps ComicPost's re-thresholded shadow edges crisp; fading is done via comic opacity.")]
        [Range(0f, 1f)] [SerializeField] private float minLightShadowStrength = 0.65f;
        [SerializeField] private float lightningLightBoost = 2.5f;
        [SerializeField] private Color lightningColor = new Color(0.8f, 0.86f, 1f, 1f);

        [Header("Fog & ambient")]
        [Tooltip("Horizon fog amount in the sky = fog density × this.")]
        [SerializeField] private float horizonFogFromDensity = 90f;
        [Range(0f, 1f)] [SerializeField] private float minHorizonFog = 0.25f;
        [Tooltip("Seconds between ambient probe refreshes (cheap, but nothing gains from every frame).")]
        [Range(0f, 1f)] [SerializeField] private float ambientUpdateInterval = 0.1f;

        [Header("Quality")]
        [Tooltip("Quality level names that use the cheap 1-sample sky (level indices differ per platform, names don't).")]
        [SerializeField] private string[] lowQualityLevels = { "Mobile" };

        [Header("Rainbow (after rain)")]
        [Tooltip("Real seconds the air stays wet enough for a rainbow once the rain stops.")]
        [Min(1f)] [SerializeField] private float rainbowLingerSeconds = 150f;
        [Tooltip("Seconds for the rainbow to fade in / out.")]
        [Min(0.1f)] [SerializeField] private float rainbowFadeSeconds = 8f;
        [Tooltip("Peak rainbow strength; keep it subtle.")]
        [Range(0f, 1f)] [SerializeField] private float rainbowPeak = 0.25f;

        [Header("Edit-mode preview")]
        [SerializeField] private bool previewInEditMode = true;
        [SerializeField] private DayCycleProfile previewDayCycle;
        [SerializeField] private WeatherProfile previewWeather;
        [Range(0f, 24f)] [SerializeField] private float previewHours = 10f;
        [Tooltip("0 new moon, 0.25 first quarter, 0.5 full, 0.75 last quarter.")]
        [Range(0f, 1f)] [SerializeField] private float previewMoonPhase = 0.5f;
        [Tooltip("Forces the rainbow strength in edit mode (still needs a low sun to show).")]
        [Range(0f, 1f)] [SerializeField] private float previewRainbow;

        private ColorAdjustments colorAdjustments;
        private LightShadows authoredShadows = LightShadows.Soft;
        private bool shadowsOn = true;
        private float presentedTime01 = -1f;
        private float ambientTimer;
        private int lastQualityLevel = -1;
        private Vector2 baseScroll;
        private Vector2 detailScroll;
        private GlobalKeyword lowQualityKeyword;
        private float moonPhase01 = 0.5f;
        private float skyMoisture;
        private float rainbowStrength;
        private float rainbowOverride = -1f;
        private LensFlareComponentSRP flare;

        /// <summary>0 new .. 0.5 full .. 1 new, shared by every client (derived from the replicated world clock).</summary>
        public float MoonPhase01 => moonPhase01;
        /// <summary>Lit fraction of the moon's disc, 0-1.</summary>
        public static float MoonIllumination(float phase01) => 0.5f - 0.5f * Mathf.Cos(phase01 * Mathf.PI * 2f);

        private static bool IsHeadless
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Application.isBatchMode;
#endif
            }
        }

        private void OnEnable()
        {
            if (IsHeadless)
            {
                enabled = false;
                return;
            }

            lowQualityKeyword = GlobalKeyword.Create(LowQualityKeyword);
            lastQualityLevel = -1;
            ambientTimer = 0f;

            if (keyLight != null)
            {
                if (keyLight.shadows != LightShadows.None) authoredShadows = keyLight.shadows;
                shadowsOn = keyLight.shadows != LightShadows.None;
                RenderSettings.sun = keyLight;
            }
            if (skyMaterial != null) RenderSettings.skybox = skyMaterial;
            SetupFlare();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            if (Application.isPlaying && postVolume != null)
            {
                // .profile returns a runtime instance, so the profile asset is never modified.
                var profile = postVolume.profile;
                if (!profile.TryGet(out colorAdjustments))
                    colorAdjustments = profile.Add<ColorAdjustments>();
                colorAdjustments.saturation.overrideState = true;
                colorAdjustments.contrast.overrideState = true;
                colorAdjustments.colorFilter.overrideState = true;
            }

            if (!Application.isPlaying) ApplyPreview();
        }

        private void OnDisable()
        {
            ComicStyleRuntime.Reset();
            SunShaftRuntime.Reset();
            if (flare != null) flare.intensity = 0f;
            Shader.SetGlobalVector(Ids.CloudShadow, Vector4.zero);
            if (keyLight != null && !shadowsOn) keyLight.shadows = authoredShadows;
            shadowsOn = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Light / RenderSettings writes aren't allowed inside OnValidate; defer one editor tick.
            if (Application.isPlaying || IsHeadless) return;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && isActiveAndEnabled) ApplyPreview();
            };
        }
#endif

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;

            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || env.DayCycle == null) return;

            float flash = lightning != null ? lightning.Flash : 0f;
            float dt = Time.deltaTime;
            float time01 = SmoothClock(env, dt);
            // Continuous phase: whole days on the world clock plus today's fraction, over one lunar cycle.
            moonPhase01 = Mathf.Repeat((env.State.DayCount(env.Now) + time01) / Mathf.Max(1f, env.DayCycle.lunarCycleDays), 1f);
            rainbowOverride = -1f;
            Apply(env.DayCycle, time01, env.Weather, flash, dt);
        }

        /// <summary>
        /// Presented time of day: advances at the clock's nominal rate every frame and eases out small corrections
        /// (client ServerTime adjustments, frame hitches), so the sun/moon — and every shadow — glide instead of
        /// stepping. Large jumps (time.set, joining a session) snap.
        /// </summary>
        private float SmoothClock(WorldEnvironmentNetwork env, float dt)
        {
            float target = env.TimeOfDay01;
            var s = env.State;
            if (presentedTime01 < 0f || dt <= 0f || s.DayLengthSeconds <= 0f)
            {
                presentedTime01 = target;
                return presentedTime01;
            }

            float rate = s.TimeScale / s.DayLengthSeconds;
            float predicted = Mathf.Repeat(presentedTime01 + rate * dt, 1f);
            float error = Mathf.DeltaAngle(predicted * 360f, target * 360f) / 360f; // wrap-aware, [-0.5, 0.5)
            if (Mathf.Abs(error) > clockSnapThreshold01)
            {
                presentedTime01 = target;
            }
            else
            {
                float k = 1f - Mathf.Exp(-dt / clockSmoothingSeconds);
                presentedTime01 = Mathf.Repeat(predicted + error * k, 1f);
            }
            return presentedTime01;
        }

        private void ApplyPreview()
        {
            if (!previewInEditMode || previewDayCycle == null) return;
            var weather = previewWeather != null ? WeatherBlend.Evaluate(previewWeather, previewWeather, 1f, 0.5f) : WeatherBlend.Clear();
            ambientTimer = 0f;
            moonPhase01 = previewMoonPhase;
            rainbowOverride = previewRainbow;
            Apply(previewDayCycle, previewHours / 24f, weather, 0f, 0f);
        }

        // ---------------------------------------------------------------- core

        private void Apply(DayCycleProfile dc, float t, WeatherBlend w, float flash, float dt)
        {
            Vector3 sunDir = dc.SunDirection(t);
            Vector3 moonDir = dc.MoonDirection(t);
            bool useMoon = sunDir.y < dc.lightSwapElevation;
            Vector3 keyDir = useMoon ? moonDir : sunDir;
            float sunVisible = Smooth(sunDir.y, -0.02f, 0.06f);
            float moonVisible = Smooth(moonDir.y, -0.02f, 0.06f);

            // ---- key light (intensity crosses ~0 at the swap, so the swap never pops)
            float moonIllum = MoonIllumination(moonPhase01);
            // A thin crescent lights the world far less than a full moon.
            float intensity = useMoon ? dc.moonIntensity.Evaluate(t) * moonVisible * Mathf.Lerp(0.25f, 1f, moonIllum)
                                      : dc.sunIntensity.Evaluate(t) * sunVisible;
            intensity *= w.sunMultiplier;
            float shadowAmount = Mathf.Clamp01(dc.shadowStrength.Evaluate(t) * w.shadowMultiplier) * Smooth(intensity, 0.01f, 0.15f)
                * Smooth(keyDir.y, lowSunShadowFadeStart, Mathf.Max(lowSunShadowFadeStart + 0.001f, lowSunShadowFadeEnd));

            if (keyLight != null)
            {
                Color lightColor = Desaturate(useMoon ? dc.moonColor.Evaluate(t) : dc.sunColor.Evaluate(t), w.skyDesaturation * 0.5f);
                if (flash > 0f) lightColor = Color.Lerp(lightColor, lightningColor, Mathf.Clamp01(flash));
                keyLight.color = lightColor;
                keyLight.intensity = intensity + flash * lightningLightBoost;

                // Rotate every frame from the smoothed clock. The old "only after drifting 0.05°" gate made shadows
                // step a few times a second — very visible on long dawn/dusk shadows. Per-frame motion is sub-texel.
                // The sun↔moon swap happens while both are below the low-elevation shadow fade, so it never pops.
                keyLight.transform.rotation = Quaternion.LookRotation(-keyDir);

                // Hysteresis so the shadow pass doesn't flicker on/off at the threshold.
                // Play mode only, so edit-mode previews never save a shadowless light into the scene.
                if (Application.isPlaying)
                {
                    if (shadowsOn && shadowAmount < shadowsOffBelow) shadowsOn = false;
                    else if (!shadowsOn && shadowAmount > shadowsOffBelow * 2f) shadowsOn = true;
                    var wanted = shadowsOn ? authoredShadows : LightShadows.None;
                    if (keyLight.shadows != wanted) keyLight.shadows = wanted;
                }
                keyLight.shadowStrength = Mathf.Lerp(minLightShadowStrength, 1f, shadowAmount);
            }

            // ---- comic style
            Color tint = dc.comicShadowTint.Evaluate(t);
            ComicStyleRuntime.ShadowTint = tint;
            ComicStyleRuntime.ShadowTintWeight = tint.a;
            ComicStyleRuntime.ShadowOpacityMultiplier = shadowAmount;
            ComicStyleRuntime.DotStartOffset = dc.comicDotStartOffset.Evaluate(t);

            // ---- sky colours (weather desaturates / darkens)
            float desat = w.skyDesaturation;
            float bright = 1f - w.skyDarkening;
            Color zenith = Weatherize(dc.skyZenith.Evaluate(t), desat, bright);
            Color horizon = Weatherize(dc.skyHorizon.Evaluate(t), desat, bright);
            Color ground = Weatherize(dc.skyGround.Evaluate(t), desat, bright);
            Color fog = Weatherize(Color.Lerp(dc.fogColor.Evaluate(t), w.fogTint, w.fogTint.a), desat, bright);
            if (flash > 0f) fog = Color.Lerp(fog, lightningColor, Mathf.Clamp01(flash) * 0.25f);

            Color cloudLit = Weatherize(dc.cloudLit.Evaluate(t), desat, bright) * w.cloudTint;
            Color cloudShade = Weatherize(dc.cloudShade.Evaluate(t), desat, bright) * w.cloudTint;
            // Under heavy cover the sun no longer reaches cloud tops we can see.
            cloudLit = Color.Lerp(cloudLit, cloudShade, w.coverage * w.coverage * 0.6f);

            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = w.fogDensity;

            // ---- ambient (trilight → SH probe, rebuilt by Unity when the colours change)
            ambientTimer -= dt;
            if (ambientTimer <= 0f)
            {
                ambientTimer = ambientUpdateInterval;
                float a = w.ambientMultiplier;
                RenderSettings.ambientSkyColor = Weatherize(dc.ambientSky.Evaluate(t), desat, a) + lightningColor * flash * 0.3f;
                RenderSettings.ambientEquatorColor = Weatherize(dc.ambientEquator.Evaluate(t), desat, a);
                RenderSettings.ambientGroundColor = Weatherize(dc.ambientGround.Evaluate(t), desat, a);
            }

            // ---- sky shader globals
            Shader.SetGlobalVector(Ids.SunDir, sunDir);
            Shader.SetGlobalVector(Ids.MoonDir, moonDir);
            Shader.SetGlobalVector(Ids.SkyZenith, ToShader(zenith));
            Shader.SetGlobalVector(Ids.SkyHorizon, ToShader(horizon));
            Shader.SetGlobalVector(Ids.SkyGround, ToShader(ground));

            float starVisibility = dc.starVisibility.Evaluate(t) * (1f - w.skyDarkening * 0.5f);
            float horizonFog = Mathf.Clamp01(Mathf.Max(minHorizonFog, w.fogDensity * horizonFogFromDensity));
            Shader.SetGlobalVector(Ids.SkyParams, new Vector4(dc.horizonFalloff, starVisibility, flash, horizonFog));

            float sunGate = sunVisible * Mathf.Clamp01(w.sunMultiplier);
            Shader.SetGlobalVector(Ids.SunColor, ToShader(dc.sunColor.Evaluate(t)) * sunGate);
            Shader.SetGlobalVector(Ids.SunParams, new Vector4(Mathf.Cos(dc.sunDiscSize), dc.sunGlowPower,
                dc.sunGlowIntensity * (1f - w.coverage * 0.6f), dc.sunDiscIntensity));
            Shader.SetGlobalVector(Ids.MoonColor, ToShader(dc.moonColor.Evaluate(t)) * moonVisible * (1f - w.skyDarkening * 0.5f));
            Shader.SetGlobalVector(Ids.MoonParams, new Vector4(Mathf.Cos(dc.moonDiscSize), 0f, 0f, 0f));

            // ---- textured sun / moon (replace the procedural discs when art is assigned)
            if (sunTexture != null) Shader.SetGlobalTexture(Ids.SunTex, sunTexture);
            if (moonTexture != null) Shader.SetGlobalTexture(Ids.MoonTex, moonTexture);
            Shader.SetGlobalVector(Ids.BodyTexParams, new Vector4(
                Mathf.Tan(dc.sunTextureSize), Mathf.Tan(dc.moonTextureSize),
                sunTexture != null ? 1f : 0f, moonTexture != null ? 1f : 0f));
            Shader.SetGlobalVector(Ids.BodyTexBrightness, new Vector4(
                sunGate * dc.sunTextureIntensity,
                moonVisible * (1f - w.skyDarkening * 0.5f) * dc.moonTextureIntensity,
                Mathf.Clamp01(w.celestialDiffusion), 0f));
            Shader.SetGlobalVector(Ids.BodyTexAspect, new Vector4(Aspect(sunTexture), Aspect(moonTexture), 0f, 0f));
            Shader.SetGlobalVector(Ids.MoonPhase, new Vector4(moonPhase01, moonIllum, 0f, 0f));

            // ---- rainbow: wet air (recent rain) + clearing sky + a low sun (the bow sits 42° around the antisolar
            // point, so a sun higher than 42° puts it below the horizon).
            if (rainbowOverride >= 0f)
            {
                rainbowStrength = rainbowOverride;
            }
            else
            {
                if (w.rain > 0.05f) skyMoisture = 1f;
                else skyMoisture = Mathf.MoveTowards(skyMoisture, 0f, dt / rainbowLingerSeconds);
                float clearing = (1f - Mathf.Clamp01(w.rain * 10f)) * (1f - Smooth(w.celestialDiffusion, 0.1f, 0.4f));
                float target = skyMoisture * clearing * RainbowSunFactor(sunDir.y);
                rainbowStrength = Mathf.MoveTowards(rainbowStrength, target, dt / rainbowFadeSeconds);
            }
            Shader.SetGlobalVector(Ids.Rainbow, new Vector4(rainbowStrength * rainbowPeak, 0f, 0f, 0f));

            // ---- clouds (wind scroll is wrapped to [0,1) so precision never degrades in long sessions)
            float invTile = 1f / cloudTileSize;
            Vector2 windUv = w.wind * (dt * invTile);
            baseScroll = Wrap(baseScroll - windUv);
            Vector2 detailWind = Rotate(windUv, 20f) * (detailScale * (detailWindMultiplier - 1f));
            detailScroll = Wrap(detailScroll - detailWind);

            if (cloudNoise != null) Shader.SetGlobalTexture(Ids.CloudNoise, cloudNoise);
            Shader.SetGlobalVector(Ids.CloudParams, new Vector4(w.coverage, w.cloudDensity, w.cloudSoftness, w.cloudDarkness));
            Shader.SetGlobalVector(Ids.CloudShape, new Vector4(cloudHeight, invTile, detailScale, detailErosion));
            Shader.SetGlobalVector(Ids.CloudWind, new Vector4(baseScroll.x, baseScroll.y, detailScroll.x, detailScroll.y));
            Shader.SetGlobalVector(Ids.CloudLightDir, keyDir);
            Shader.SetGlobalVector(Ids.CloudLit, ToShader(cloudLit));
            Shader.SetGlobalVector(Ids.CloudShade, ToShader(cloudShade));
            Shader.SetGlobalVector(Ids.CloudExtra, new Vector4(w.cirrus, 0f, 0f, 0f));

            // ---- god rays + lens flare: sun only, thinned by cloud diffusion and by a cloud right in front of the sun
            float sunClear = (1f - Mathf.Clamp01(w.celestialDiffusion)) * sunGate;
            SunShaftRuntime.Intensity = useMoon ? 0f : dc.sunShaftIntensity.Evaluate(t) * sunClear;
            if (flare != null)
            {
                float cloudAtSun = CloudDensityToward(sunDir, w);
                flare.intensity = useMoon ? 0f : dc.sunFlareIntensity.Evaluate(t) * sunClear * (1f - cloudAtSun) * sunFlareStrength;
            }

            float groundShadow = cloudShadows && cloudNoise != null ? w.cloudShadow * Smooth(intensity, 0.05f, 0.6f) : 0f;
            Shader.SetGlobalVector(Ids.CloudShadow, new Vector4(groundShadow, cloudShadowSoftness, 0f, 0f));

            // ---- quality tier
            int quality = QualitySettings.GetQualityLevel();
            if (quality != lastQualityLevel)
            {
                lastQualityLevel = quality;
                Shader.SetKeyword(lowQualityKeyword, IsLowQuality(quality));
            }

            // ---- post
            if (colorAdjustments != null)
            {
                colorAdjustments.saturation.value = w.saturation;
                colorAdjustments.contrast.value = w.contrast;
                colorAdjustments.colorFilter.value = dc.postColorFilter.Evaluate(t);
            }
        }

        // ---------------------------------------------------------------- helpers

        private bool IsLowQuality(int level)
        {
            var names = QualitySettings.names;
            if (level < 0 || level >= names.Length) return false;
            foreach (var n in lowQualityLevels)
                if (string.Equals(n, names[level], System.StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static float Aspect(Texture tex) => tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;

        private void SetupFlare()
        {
            if (keyLight == null || sunFlare == null) return;
            if (!keyLight.TryGetComponent(out flare)) flare = keyLight.gameObject.AddComponent<LensFlareComponentSRP>();
            flare.lensFlareData = sunFlare;
            flare.useOcclusion = true;      // buildings and terrain hide it (depth test)
            flare.allowOffScreen = false;
            flare.intensity = 0f;
        }

        /// <summary>
        /// CPU copy of SkyCommon's KN_CloudDensityLod along a sky direction from the camera, so the flare hides when
        /// a cloud drifts across the sun. Needs Read/Write on the cloud noise; otherwise falls back to coverage.
        /// </summary>
        private float CloudDensityToward(Vector3 dir, in WeatherBlend w)
        {
            var cam = Camera.main;
            if (cloudNoise == null || !cloudNoise.isReadable || cam == null)
                return Mathf.Clamp01(w.coverage * w.cloudDensity * 0.5f);
            Vector3 origin = cam.transform.position;
            float dist = cloudHeight / Mathf.Max(dir.y, 0.02f);
            Vector2 planeXZ = new Vector2(origin.x + dir.x * dist, origin.z + dir.z * dist);
            Vector2 uv = planeXZ / cloudTileSize + baseScroll;
            Color n = cloudNoise.GetPixelBilinear(uv.x, uv.y);
            float shape = n.r - 0.5f * detailErosion;
            float soft = w.cloudSoftness;
            float coverage = Mathf.Clamp01(w.coverage + (n.a - 0.5f) * 0.3f * (1f - w.coverage));
            float threshold = Mathf.Lerp(1f + soft, -0.3f - soft, coverage);
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(threshold - soft, threshold + soft, shape));
            return Mathf.Clamp01(mask * w.cloudDensity);
        }

        /// <summary>Sun above the horizon but below ~42° elevation (sin 42° ≈ 0.67).</summary>
        private static float RainbowSunFactor(float sunElevationSin) =>
            Smooth(sunElevationSin, 0f, 0.06f) * (1f - Smooth(sunElevationSin, 0.55f, 0.67f));

        private static float Smooth(float x, float edge0, float edge1)
        {
            float k = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return k * k * (3f - 2f * k);
        }

        private static Color Desaturate(Color c, float amount)
        {
            float l = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return Color.Lerp(c, new Color(l, l, l, c.a), amount);
        }

        private static Color Weatherize(Color c, float desaturation, float brightness)
        {
            c = Desaturate(c, desaturation);
            return new Color(c.r * brightness, c.g * brightness, c.b * brightness, c.a);
        }

        private static Vector4 ToShader(Color c)
        {
            return QualitySettings.activeColorSpace == ColorSpace.Linear ? (Vector4)c.linear : (Vector4)c;
        }

        private static Vector2 Wrap(Vector2 v) => new Vector2(v.x - Mathf.Floor(v.x), v.y - Mathf.Floor(v.y));

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            float s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        private static class Ids
        {
            public static readonly int SunDir = Shader.PropertyToID("_KN_SunDir");
            public static readonly int MoonDir = Shader.PropertyToID("_KN_MoonDir");
            public static readonly int SkyZenith = Shader.PropertyToID("_KN_SkyZenith");
            public static readonly int SkyHorizon = Shader.PropertyToID("_KN_SkyHorizon");
            public static readonly int SkyGround = Shader.PropertyToID("_KN_SkyGround");
            public static readonly int SkyParams = Shader.PropertyToID("_KN_SkyParams");
            public static readonly int SunColor = Shader.PropertyToID("_KN_SunColor");
            public static readonly int SunParams = Shader.PropertyToID("_KN_SunParams");
            public static readonly int MoonColor = Shader.PropertyToID("_KN_MoonColor");
            public static readonly int MoonParams = Shader.PropertyToID("_KN_MoonParams");
            public static readonly int SunTex = Shader.PropertyToID("_KN_SunTex");
            public static readonly int MoonTex = Shader.PropertyToID("_KN_MoonTex");
            public static readonly int BodyTexParams = Shader.PropertyToID("_KN_BodyTexParams");
            public static readonly int BodyTexBrightness = Shader.PropertyToID("_KN_BodyTexBrightness");
            public static readonly int BodyTexAspect = Shader.PropertyToID("_KN_BodyTexAspect");
            public static readonly int MoonPhase = Shader.PropertyToID("_KN_MoonPhase");
            public static readonly int Rainbow = Shader.PropertyToID("_KN_Rainbow");
            public static readonly int CloudNoise = Shader.PropertyToID("_KN_CloudNoise");
            public static readonly int CloudParams = Shader.PropertyToID("_KN_CloudParams");
            public static readonly int CloudShape = Shader.PropertyToID("_KN_CloudShape");
            public static readonly int CloudWind = Shader.PropertyToID("_KN_CloudWind");
            public static readonly int CloudLightDir = Shader.PropertyToID("_KN_CloudLightDir");
            public static readonly int CloudShadow = Shader.PropertyToID("_KN_CloudShadow");
            public static readonly int CloudLit = Shader.PropertyToID("_KN_CloudLit");
            public static readonly int CloudShade = Shader.PropertyToID("_KN_CloudShade");
            public static readonly int CloudExtra = Shader.PropertyToID("_KN_CloudExtra");
        }
    }
}
