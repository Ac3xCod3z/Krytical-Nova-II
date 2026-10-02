using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Camera-following precipitation rigs (rain / snow / ground mist). Particles simulate in world space
    /// inside a small radius around the camera, with no collision, shadows or lights. A rig at zero
    /// intensity stops emitting and is deactivated once its particles die, so clear weather costs nothing.
    /// Wind is applied through one global <see cref="WindZone"/> (External Forces module), which future
    /// foliage can share.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    public sealed class PrecipitationController : MonoBehaviour
    {
        [Serializable]
        private sealed class Rig
        {
            public ParticleSystem system;
            [Tooltip("Rig height above the camera (or above the ground when Ground Anchored).")]
            public float heightOffset = 12f;
            [Tooltip("Place at ground height under the camera instead of relative to the camera (splashes, mist).")]
            public bool groundAnchored;
            [Tooltip("Metres the rig is pushed along the camera's flattened forward, so particles fill what the camera sees.")]
            public float lookAhead = 6f;
            [Tooltip("WindZone influence on these particles.")]
            [Range(0f, 4f)] public float windInfluence = 1f;
            [Tooltip("Fade out under roofs (rain/snow yes, mist no).")]
            public bool useShelter = true;

            [NonSerialized] public ParticleSystem[] all;
            [NonSerialized] public float[] baseRates;
            [NonSerialized] public float appliedIntensity = -1f;
            [NonSerialized] public float nextAliveCheck;
            /// <summary>Seconds a particle needs to reach the ground (≈ average start lifetime); drives the velocity lead.</summary>
            [NonSerialized] public float fallSeconds;
            /// <summary>Sideways wind velocity currently applied to the particles (world XZ, m/s).</summary>
            [NonSerialized] public Vector3 drift;
            /// <summary>Constant downward speed applied through Velocity over Lifetime (0 = gravity only).</summary>
            [NonSerialized] public float fallSpeed;
            [NonSerialized] public bool driftApplied;
        }

        private static readonly int WindVectorId = Shader.PropertyToID("_KN_WindVector");
        private static readonly int WindParamsId = Shader.PropertyToID("_KN_WindParams");
        private static readonly int WindScrollId = Shader.PropertyToID("_KN_WindScroll");
        private Vector2 windScroll;

        [SerializeField] private Rig rain = new Rig();
        [SerializeField] private Rig rainSplash = new Rig { heightOffset = 0.05f, windInfluence = 0f, groundAnchored = true };
        [SerializeField] private Rig snow = new Rig();
        [Tooltip("Extra heavy snow layered on top once snow intensity passes Blizzard Threshold.")]
        [SerializeField] private Rig blizzard = new Rig();
        [SerializeField] private Rig mist = new Rig { heightOffset = 0f, windInfluence = 0.3f, useShelter = false, groundAnchored = true, lookAhead = 0f };
        [Range(0f, 1f)] [SerializeField] private float blizzardThreshold = 0.7f;

        [Header("Wind")]
        [SerializeField] private WindZone windZone;
        [Tooltip("Wind gust streaks (Krytical Nova > World > Build Wind Gust VFX Prefab). Spawned under this object at runtime.")]
        [SerializeField] private GameObject windGustsPrefab;
        [Tooltip("Wind speed (m/s) where gust streaks start to appear, and where they reach full strength.")]
        [SerializeField] private Vector2 windStreakSpeedRange = new Vector2(8f, 18f);
        [Tooltip("Fraction of the wind speed pushed directly into falling rain (slant). 0.6 at 18 m/s ≈ 30° storm slant.")]
        [Range(0f, 1.5f)] [SerializeField] private float rainWindDrift = 0.6f;
        [Tooltip("Same for snow / blizzard, which fall slower and so slant much more.")]
        [Range(0f, 1.5f)] [SerializeField] private float snowWindDrift = 0.35f;
        [Tooltip("WindZone.windMain per m/s of weather wind.")]
        [SerializeField] private float windZoneScale = 0.06f;
        [Tooltip("Wind speed (m/s) at which foliage and water reach full storm motion (_KN_WindParams.z = 1).")]
        [Min(1f)] [SerializeField] private float foliageFullWindSpeed = 20f;
        [Tooltip("Water ripple drift (texture tiles per second) per m/s of wind.")]
        [SerializeField] private float waterWindScroll = 0.004f;

        [Header("Following")]
        [SerializeField] private float snapSize = 1f;
        [Tooltip("Falling particles spawn ahead of the camera by velocity × fall time, so you can't outrun the rain. Covers rank-20 run speed (~29 m/s).")]
        [SerializeField] private float maxVelocityLead = 40f;
        [Tooltip("Seconds to smooth the camera velocity used for the lead.")]
        [SerializeField] private float velocitySmoothing = 0.1f;
        [Tooltip("Rain/snow ride vertically with the local player's jumps, so drops keep falling past you at the apex and on the way down.")]
        [SerializeField] private bool followPlayerJumps = true;

        [Header("Rain coverage + ground")]
        [Tooltip("Rain emitter radius (prefab is 14 m). Emission and max particles scale with area so density is unchanged.")]
        [SerializeField] private float rainRadius = 20f;
        [Tooltip("Rain/snow die on contact with the ground, roofs and props instead of passing through.")]
        [SerializeField] private bool precipitationCollides = true;
        [Tooltip("Splashes are emitted at raycast hits around the camera, so they sit on slopes, steps and roofs.")]
        [SerializeField] private float splashRadius = 12f;
        [SerializeField] private float splashRayHeight = 40f;
        [Tooltip("Splash raycasts per second at full rain (each emits one splash).")]
        [SerializeField] private float splashesPerSecond = 140f;

        [Header("Rain look")]
        [Tooltip("Drop width range in metres (thin streaks read as rain, fat ones read as snow/lasers).")]
        [SerializeField] private Vector2 rainDropWidth = new Vector2(0.012f, 0.02f);
        [Tooltip("Streak length = speed × this. ~0.03 gives 0.4–0.8 m streaks at fall speed.")]
        [SerializeField] private float rainStreakLength = 0.03f;
        [Tooltip("Gravity modifier for drops. Kept low: drops fall at Rain Fall Speed from the moment they spawn instead of hanging near the emitter.")]
        [SerializeField] private float rainGravity = 0.3f;
        [Tooltip("Constant downward speed of drops (real rain falls at ~7-9 m/s terminal velocity).")]
        [SerializeField] private float rainFallSpeed = 10f;
        [Range(0f, 1f)] [SerializeField] private float rainOpacity = 0.38f;
        [Tooltip("Splash droplet width range in metres.")]
        [SerializeField] private Vector2 splashDropWidth = new Vector2(0.006f, 0.012f);
        [Tooltip("Splash droplet stretch along its velocity (Synty's 5 made tall ovals).")]
        [SerializeField] private float splashStretch = 2.5f;
        [Tooltip("Max drop width as a fraction of screen height, so drops right next to the camera stay thin.")]
        [Range(0.001f, 0.05f)] [SerializeField] private float rainMaxScreenSize = 0.004f;

        [Header("Lighting (precipitation is unlit, so it is tinted by the scene light instead)")]
        [Tooltip("Colour at full daylight.")]
        [SerializeField] private Color litTint = new Color(0.82f, 0.88f, 1f);
        [Tooltip("Colour on a moonless night / under storm darkness: rain stays faintly visible but stops glowing.")]
        [SerializeField] private Color darkTint = new Color(0.16f, 0.18f, 0.24f);
        [Tooltip("How strongly the key light (sun/moon) brightens precipitation, on top of ambient.")]
        [SerializeField] private float keyLightInfluence = 0.3f;

        [Header("Ripples (Polygon FX ring, on flat ground)")]
        [Tooltip("Ring texture for ground ripples, e.g. Synty PolygonParticles_Ring_01. Leave empty to disable ripples.")]
        [SerializeField] private Texture2D rippleTexture;
        [Range(0f, 1f)] [SerializeField] private float rippleChance = 0.55f;
        [SerializeField] private Vector2 rippleSize = new Vector2(0.25f, 0.5f);

        [Header("Shelter + ground (terrain sample / one raycast every interval)")]
        [SerializeField] private LayerMask shelterMask = ~0;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float shelterCheckInterval = 0.25f;
        [SerializeField] private float shelterRayLength = 40f;
        [SerializeField] private float shelterFadeSpeed = 3f;

        private Transform cam;
        private float shelter = 1f;
        private float shelterTarget = 1f;
        private float nextShelterCheck;
        private float groundY;
        private Vector3 camForwardXZ;
        private Vector3 lastCamPos;
        private bool hasLastCamPos;
        private Vector3 camVelocityXZ;
        private float splashAccumulator;
        private ParticleSystem ripples;
        private Material rippleMaterial;
        private Color appliedTint = new Color(-1f, 0f, 0f, 0f);
        // Ground-hugging gust curtain; rotated to the wind each frame, so it isn't serialized with the other rigs.
        private readonly Rig windGusts = new Rig { heightOffset = 0f, groundAnchored = true, lookAhead = 4f, windInfluence = 0f, useShelter = true };
        private float[] windBaseSpeeds;
        // Custom simulation space for rain/snow: identity in XZ, lifted by the local player's jump height.
        private Transform jumpSpace;
        private Transform player;
        private KryticalNova.Player.PlayerJumpMotor playerJump;
        private float playerRestHeight = float.NaN;

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            if (windZone == null)
            {
                windZone = new GameObject("Weather WindZone").AddComponent<WindZone>();
                windZone.transform.SetParent(transform, false);
            }
            windZone.mode = WindZoneMode.Directional;

            if (windGustsPrefab != null)
            {
                var gusts = Instantiate(windGustsPrefab, transform);
                gusts.name = windGustsPrefab.name;
                windGusts.system = gusts.GetComponent<ParticleSystem>();
            }

            ConfigureRain();
            rain.fallSpeed = rainFallSpeed;
            Init(rain);
            Init(rainSplash);
            Init(snow);
            Init(blizzard);
            Init(mist);
            Init(windGusts);
            if (windGusts.all != null)
            {
                windBaseSpeeds = new float[windGusts.all.Length];
                for (int i = 0; i < windGusts.all.Length; i++) windBaseSpeeds[i] = windGusts.all[i].main.startSpeedMultiplier;
            }

            // Splashes are placed by script at real ground hits (see EmitSplashes), not by the emitter shape.
            if (rainSplash.baseRates != null)
                for (int i = 0; i < rainSplash.baseRates.Length; i++) rainSplash.baseRates[i] = 0f;

            if (precipitationCollides)
            {
                EnableGroundCollision(rain.system);
                EnableGroundCollision(snow.system);
                EnableGroundCollision(blizzard.system);
            }
            CreateRipples();

            if (followPlayerJumps)
            {
                jumpSpace = new GameObject("Precipitation Jump Space").transform;
                UseJumpSpace(rain);
                UseJumpSpace(snow);
                UseJumpSpace(blizzard);
            }
        }

        private void OnDestroy()
        {
            if (rippleMaterial != null) Destroy(rippleMaterial);
            if (jumpSpace != null) Destroy(jumpSpace.gameObject);
        }

        private void UseJumpSpace(Rig rig)
        {
            if (rig.all == null) return;
            foreach (var ps in rig.all)
            {
                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = jumpSpace;
            }
        }

        /// <summary>
        /// Lifts the precipitation space by how far the local player is above their normal standing height while a
        /// jump is in flight. On the ground it is exactly zero, so the rain is ordinary world-space rain.
        /// </summary>
        private void UpdateJumpSpace()
        {
            if (jumpSpace == null) return;
            if (player == null || !player.gameObject.activeInHierarchy)
            {
                var inventory = KryticalNova.Items.PlayerInventory.Local;
                player = inventory != null ? inventory.transform : null;
                playerJump = player != null ? player.GetComponent<KryticalNova.Player.PlayerJumpMotor>() : null;
                playerRestHeight = float.NaN;
            }

            float lift = 0f;
            if (player != null)
            {
                float aboveGround = player.position.y - SampleGround(player.position);
                if (playerJump == null || !playerJump.IsBusy) playerRestHeight = aboveGround;
                else if (!float.IsNaN(playerRestHeight)) lift = Mathf.Max(0f, aboveGround - playerRestHeight);
            }
            jumpSpace.SetPositionAndRotation(new Vector3(0f, lift, 0f), Quaternion.identity);
        }

        /// <summary>Horizontal ring particles placed with the splashes on flat ground (puddle-style ripples).</summary>
        private void CreateRipples()
        {
            if (rippleTexture == null || rainSplash.system == null) return;
            var sourceMaterial = rainSplash.system.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            if (sourceMaterial == null) return;

            var go = new GameObject("PFX_RainRipples");
            go.transform.SetParent(transform, false);
            ripples = go.AddComponent<ParticleSystem>();
            ripples.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ripples.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(rippleSize.x, rippleSize.y);
            main.gravityModifier = 0f;
            main.maxParticles = 250;
            var emission = ripples.emission;
            emission.enabled = false;
            var shape = ripples.shape;
            shape.enabled = false;

            var size = ripples.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.15f, 1f, 1f));
            var color = ripples.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            rippleMaterial = new Material(sourceMaterial) { name = "M_FX_RainRipple (runtime)", mainTexture = rippleTexture };
            renderer.sharedMaterial = rippleMaterial;
            ripples.Play();
        }

        /// <summary>
        /// Precipitation uses an unlit shader, so without this it glows white at midnight. Tint new particles by the
        /// ambient sky + key light (lightning already brightens ambient, so flashes light the rain too).
        /// </summary>
        private void ApplyLightingTint()
        {
            float ambient = Luminance(RenderSettings.ambientSkyColor);
            var key = RenderSettings.sun;
            float keyLevel = key != null && key.isActiveAndEnabled ? key.intensity * Luminance(key.color) * keyLightInfluence : 0f;
            float brightness = Mathf.Clamp01(ambient * 1.6f + keyLevel);
            Color tint = Color.Lerp(darkTint, litTint, brightness);
            if (Mathf.Abs(tint.r - appliedTint.r) + Mathf.Abs(tint.g - appliedTint.g) + Mathf.Abs(tint.b - appliedTint.b) < 0.01f) return;
            appliedTint = tint;

            SetStartColor(rain.system, tint, rainOpacity);
            SetStartColor(rainSplash.system, tint, Mathf.Min(1f, rainOpacity * 1.4f));
            SetStartColor(ripples, tint, Mathf.Min(1f, rainOpacity * 1.2f));
            SetStartColor(windGusts.system, tint, -1f);
            // Snow keeps its own alpha; only the colour follows the light (white snow glows at night too).
            SetStartColor(snow.system, Color.Lerp(tint, Color.white, 0.35f), -1f);
            SetStartColor(blizzard.system, Color.Lerp(tint, Color.white, 0.35f), -1f);
        }

        private static void SetStartColor(ParticleSystem ps, Color tint, float alpha)
        {
            if (ps == null) return;
            var main = ps.main;
            if (alpha < 0f) alpha = main.startColor.color.a;
            tint.a = alpha;
            main.startColor = tint;
        }

        private static float Luminance(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        /// <summary>Widens the rain column (area-scaled rate + cap) so the lead never exposes its edge.</summary>
        private void ConfigureRain()
        {
            if (rain.system == null) return;

            // Look: thin, short, believable-speed streaks (overrides the Synty-derived prefab values).
            var look = rain.system.main;
            look.startSize = new ParticleSystem.MinMaxCurve(rainDropWidth.x, rainDropWidth.y);
            look.gravityModifierMultiplier = rainGravity;
            look.startLifetime = new ParticleSystem.MinMaxCurve(2f, 2.4f); // collision ends them at the ground
            look.startSpeed = 0f; // fall speed is applied straight down via Velocity over Lifetime (ApplyWindDrift)
            var streaks = rain.system.GetComponent<ParticleSystemRenderer>();
            if (streaks != null)
            {
                streaks.velocityScale = rainStreakLength;
                // Drops passing right in front of the lens otherwise balloon into fat ovals.
                streaks.maxParticleSize = rainMaxScreenSize;
            }
            if (rainSplash.system != null)
            {
                var splashMain = rainSplash.system.main;
                splashMain.startSize = new ParticleSystem.MinMaxCurve(splashDropWidth.x, splashDropWidth.y);
                splashMain.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                var splashRenderer = rainSplash.system.GetComponent<ParticleSystemRenderer>();
                if (splashRenderer != null)
                {
                    splashRenderer.lengthScale = splashStretch;
                    splashRenderer.maxParticleSize = rainMaxScreenSize;
                }
            }

            var shape = rain.system.shape;
            if (!shape.enabled || shape.shapeType != ParticleSystemShapeType.Circle || shape.radius <= 0f || rainRadius <= shape.radius) return;
            float areaScale = (rainRadius * rainRadius) / (shape.radius * shape.radius);
            shape.radius = rainRadius;
            var emission = rain.system.emission;
            emission.rateOverTimeMultiplier *= areaScale;
            var main = rain.system.main;
            main.maxParticles = Mathf.CeilToInt(main.maxParticles * areaScale);
        }

        /// <summary>Cheap cached (voxel) world collision: particles die where they touch terrain, roofs or props.</summary>
        private void EnableGroundCollision(ParticleSystem ps)
        {
            if (ps == null) return;
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.voxelSize = 0.5f;
            collision.maxCollisionShapes = 64;
            collision.collidesWith = groundMask;
            collision.enableDynamicColliders = false;
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            collision.dampen = 1f;
            collision.radiusScale = 0.1f;
            collision.sendCollisionMessages = false;
        }

        private static void Init(Rig rig)
        {
            if (rig.system == null) return;
            rig.all = rig.system.GetComponentsInChildren<ParticleSystem>(true);
            rig.baseRates = new float[rig.all.Length];
            for (int i = 0; i < rig.all.Length; i++)
            {
                var ps = rig.all[i];
                rig.baseRates[i] = ps.emission.rateOverTimeMultiplier;
                var forces = ps.externalForces;
                forces.enabled = rig.windInfluence > 0f;
                forces.multiplier = rig.windInfluence;
            }
            var main = rig.system.main;
            var lifetime = main.startLifetime;
            rig.fallSeconds = lifetime.mode == ParticleSystemCurveMode.TwoConstants
                ? (lifetime.constantMin + lifetime.constantMax) * 0.5f
                : lifetime.constant;
            // Gravity-driven drops: time to fall from the emitter (+ a typical camera height) to the ground.
            // Lifetime is only a safety cap now that collision ends particles at the surface.
            float gravity = main.gravityModifierMultiplier * -Physics.gravity.y;
            if (rig.fallSpeed > 0f)
                rig.fallSeconds = Mathf.Min(rig.fallSeconds, (rig.heightOffset + 4f) / rig.fallSpeed);
            else if (gravity > 1f)
                rig.fallSeconds = Mathf.Min(rig.fallSeconds, Mathf.Sqrt(2f * (rig.heightOffset + 4f) / gravity));
            rig.system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rig.system.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return;

            if (cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                cam = main.transform;
            }

            var w = env.Weather;
            Vector3 camPos = cam.position;
            Vector3 forward = cam.forward;
            forward.y = 0f;
            camForwardXZ = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.zero;
            TrackCameraVelocity(camPos, Time.deltaTime);
            UpdateJumpSpace();

            // Wind (one zone for particles; global vectors for foliage and water shaders).
            float speed = w.wind.magnitude;
            if (speed > 0.01f) windZone.transform.rotation = Quaternion.LookRotation(new Vector3(w.wind.x, 0f, w.wind.y));
            windZone.windMain = speed * windZoneScale;
            windZone.windTurbulence = speed * windZoneScale * (0.3f + w.gust);
            windZone.windPulseMagnitude = w.gustiness;
            windZone.windPulseFrequency = 0.15f + w.gustiness * 0.25f;
            Shader.SetGlobalVector(WindVectorId, new Vector4(w.wind.x, w.wind.y, speed, 0f));
            // Foliage/water: x = gust now (0-1), y = gustiness, z = strength (0 calm .. 1 at foliageFullWindSpeed), w unused.
            Shader.SetGlobalVector(WindParamsId, new Vector4(w.gust, w.gustiness, Mathf.Clamp01(speed / foliageFullWindSpeed), 0f));
            // Integrated wind drift for water ripples: continuous even as wind speed/heading change. Wrapped at a
            // whole number of texture tiles so the wrap is seamless.
            windScroll += w.wind * (Time.deltaTime * waterWindScroll);
            windScroll.x = Mathf.Repeat(windScroll.x, 64f);
            windScroll.y = Mathf.Repeat(windScroll.y, 64f);
            Shader.SetGlobalVector(WindScrollId, new Vector4(windScroll.x, windScroll.y, 0f, 0f));

            if ((w.rain > 0f || w.snow > 0f || w.mist > 0f) && Time.time >= nextShelterCheck)
            {
                nextShelterCheck = Time.time + shelterCheckInterval;
                shelterTarget = Physics.Raycast(camPos + Vector3.up * 0.5f, Vector3.up, shelterRayLength, shelterMask, QueryTriggerInteraction.Ignore) ? 0f : 1f;
                groundY = SampleGround(camPos);
            }
            shelter = Mathf.MoveTowards(shelter, shelterTarget, shelterFadeSpeed * Time.deltaTime);

            float snowLayer = blizzardThreshold > 0f ? Mathf.Clamp01(w.snow / blizzardThreshold) : w.snow;
            float blizzardLayer = Mathf.Clamp01((w.snow - blizzardThreshold) / Mathf.Max(1f - blizzardThreshold, 0.01f));

            Vector3 wind3 = new Vector3(w.wind.x, 0f, w.wind.y);
            ApplyWindDrift(rain, wind3 * rainWindDrift);
            ApplyWindDrift(snow, wind3 * snowWindDrift);
            ApplyWindDrift(blizzard, wind3 * snowWindDrift);
            UpdateRig(rain, w.rain, camPos);
            UpdateRig(rainSplash, w.rain, camPos);
            UpdateRig(snow, snowLayer, camPos);
            UpdateRig(blizzard, blizzardLayer, camPos);
            UpdateRig(mist, w.mist, camPos);
            UpdateWindGusts(w, camPos);
            EmitSplashes(w.rain * shelter, camPos, Time.deltaTime);
            if (w.rain > 0f || w.snow > 0f || windGusts.system != null) ApplyLightingTint();
        }

        /// <summary>
        /// Streak curtain upwind of the camera, blowing along the storm's (per-cycle) heading. Strength comes from wind
        /// speed above <see cref="windStreakSpeedRange"/>.x, and gusts surge it, so storms visibly punch.
        /// </summary>
        private void UpdateWindGusts(WeatherBlend w, Vector3 camPos)
        {
            if (windGusts.system == null) return;
            float speed = w.wind.magnitude;
            float strength = Mathf.Clamp01(Mathf.InverseLerp(windStreakSpeedRange.x, windStreakSpeedRange.y, speed));
            float intensity = strength * (0.35f + 0.65f * Mathf.Max(w.gust * 2f, 0.3f));
            UpdateRig(windGusts, Mathf.Clamp01(intensity), camPos);
            if (!windGusts.system.gameObject.activeSelf || speed < 0.01f) return;

            windGusts.system.transform.rotation = Quaternion.LookRotation(new Vector3(w.wind.x, 0f, w.wind.y));
            if (windBaseSpeeds == null) return;
            float speedScale = Mathf.Max(0.5f, speed / Mathf.Max(1f, windStreakSpeedRange.y));
            for (int i = 0; i < windGusts.all.Length; i++)
            {
                var main = windGusts.all[i].main;
                main.startSpeedMultiplier = windBaseSpeeds[i] * speedScale;
            }
        }

        /// <summary>Constant world-space sideways velocity so precipitation visibly slants along the storm's heading.</summary>
        private static void ApplyWindDrift(Rig rig, Vector3 drift)
        {
            if (rig.system == null || rig.all == null || (rig.driftApplied && (drift - rig.drift).sqrMagnitude < 0.04f)) return;
            rig.drift = drift;
            rig.driftApplied = true;
            foreach (var ps in rig.all)
            {
                var vol = ps.velocityOverLifetime;
                vol.enabled = true;
                vol.space = ParticleSystemSimulationSpace.World;
                vol.x = new ParticleSystem.MinMaxCurve(drift.x);
                vol.y = new ParticleSystem.MinMaxCurve(-rig.fallSpeed);
                vol.z = new ParticleSystem.MinMaxCurve(drift.z);
            }
        }

        private void TrackCameraVelocity(Vector3 camPos, float dt)
        {
            if (hasLastCamPos && dt > 0f)
            {
                Vector3 v = (camPos - lastCamPos) / dt;
                v.y = 0f;
                // A teleport / respawn / camera cut is not movement: drop the lead instead of flinging the rain.
                if (v.sqrMagnitude > 60f * 60f) camVelocityXZ = Vector3.zero;
                else camVelocityXZ = Vector3.Lerp(camVelocityXZ, v, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, velocitySmoothing)));
            }
            lastCamPos = camPos;
            hasLastCamPos = true;
        }

        /// <summary>One downward raycast per splash at a random point near the camera; the splash sits on whatever it hits.</summary>
        private void EmitSplashes(float intensity, Vector3 camPos, float dt)
        {
            var ps = rainSplash.system;
            if (ps == null || !ps.gameObject.activeSelf || intensity <= 0.001f || dt <= 0f)
            {
                splashAccumulator = 0f;
                return;
            }

            splashAccumulator += splashesPerSecond * intensity * dt;
            int count = Mathf.Min((int)splashAccumulator, 16);
            splashAccumulator -= (int)splashAccumulator;
            if (count <= 0) return;

            Vector3 centre = camPos + camForwardXZ * rainSplash.lookAhead + camVelocityXZ * 0.25f;
            float top = Mathf.Max(camPos.y, groundY) + splashRayHeight;
            var emitParams = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * splashRadius;
                var origin = new Vector3(centre.x + offset.x, top, centre.z + offset.y);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, splashRayHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
                    continue;
                emitParams.position = hit.point + hit.normal * rainSplash.heightOffset;
                ps.Emit(emitParams, 1);
                if (ripples != null && hit.normal.y > 0.85f && UnityEngine.Random.value < rippleChance)
                {
                    emitParams.position = hit.point + hit.normal * 0.02f;
                    ripples.Emit(emitParams, 1);
                }
            }
        }

        private float SampleGround(Vector3 camPos)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null) return terrain.SampleHeight(camPos) + terrain.transform.position.y;
            return Physics.Raycast(camPos, Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : camPos.y - 2f;
        }

        private void UpdateRig(Rig rig, float intensity, Vector3 camPos)
        {
            if (rig.system == null || rig.all == null) return;
            if (rig.useShelter) intensity *= shelter;

            var go = rig.system.gameObject;
            bool want = intensity > 0.001f;

            if (want && !go.activeSelf)
            {
                go.SetActive(true);
                rig.appliedIntensity = -1f;
                rig.system.Play(true);
            }
            if (!go.activeSelf) return;

            Vector3 centre = camPos + camForwardXZ * rig.lookAhead;
            if (rig.groundAnchored)
            {
                float s = snapSize;
                rig.system.transform.position = new Vector3(Mathf.Round(centre.x / s) * s, groundY + rig.heightOffset, Mathf.Round(centre.z / s) * s);
            }
            else
            {
                // Falling particles are world-space: spawn them where the camera will be when they land.
                // ...and upwind by however far the wind carries them while falling, so slanted rain still lands on you.
                float emitterY = Mathf.Max(camPos.y, groundY) + rig.heightOffset;
                // Constant-speed drops: fall time is the actual drop to the ground, not a guess.
                if (rig.fallSpeed > 0f)
                    rig.fallSeconds = Mathf.Max(0.1f, (emitterY - groundY) / rig.fallSpeed);
                Vector3 lead = Vector3.ClampMagnitude((camVelocityXZ - rig.drift) * rig.fallSeconds, maxVelocityLead);
                centre += lead;
                rig.system.transform.position = new Vector3(centre.x, emitterY, centre.z);
            }

            if (Mathf.Abs(intensity - rig.appliedIntensity) > 0.005f)
            {
                rig.appliedIntensity = intensity;
                for (int i = 0; i < rig.all.Length; i++)
                {
                    var emission = rig.all[i].emission;
                    emission.rateOverTimeMultiplier = rig.baseRates[i] * intensity;
                }
            }

            if (!want && Time.time >= rig.nextAliveCheck)
            {
                rig.nextAliveCheck = Time.time + 0.5f;
                if (!rig.system.IsAlive(true)) go.SetActive(false);
            }
        }
    }
}
