using System;
using UnityEngine;
using UnityEngine.Rendering;
using KryticalNova.Rendering;

namespace KryticalNova.World
{
    /// <summary>
    /// A Unique Sky (owner's NIGHT list item 24): the client's half. It asks <see cref="SkyCalendar"/> what is in the sky (the world's
    /// seed, the calendar day, the presented time and any GM force) and draws it on a far overlay dome over the custom sky: the baked
    /// star map (stars only), the night's nebula (Soul Tide, Ember Rose or Veil Ribbon), shooting stars, the comet, the amber second sun (the custom sun's own art, a copy of its drawing
    /// code, no light of any kind), the eclipse (moon disc, pearl corona, deep-blue wash) and the aurora.
    /// <para>The custom sun's files are never touched (owner, 14 Nov: "My custom sun better not change"). Everything here runs after
    /// <see cref="EnvironmentPresenter"/> (execution order 70 &gt; 50) and only on top of what it set this frame:</para>
    /// <list type="bullet">
    /// <item>the old dot stars: the sky uses a runtime COPY of the sky material with only <c>_StarBrightness</c> at 0 (the asset is unchanged);</item>
    /// <item>the eclipse dims the key light, its colour, the ambient and the fog colour toward the day cycle's midnight values by
    /// <see cref="SkyEventSettings.eclipseDarkness"/>, eased with the cover; with no eclipse nothing is written, so the world is exactly
    /// what the presenter made it.</item>
    /// </list>
    /// Client presentation only: created at runtime on clients (never saved into a scene or a bundle), and a headless server never makes
    /// it (black-sky rule: nothing here switches a scene component off or strips it for the server).
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class SkyEventPresenter : MonoBehaviour
    {
        const string OverlayShader = "Krytical Nova/Sky Events";

        /// <summary>
        /// Whether the aurora may show here and now: winter, or an always-winter map (owner 24.2 A). Item 21's seasons fill it; until
        /// they do it stays null and only a GM-forced aurora (sky.aurora) shows.
        /// </summary>
        public static Func<bool> AuroraAllowed;

        /// <summary>Shows this nebula look instead of the calendar's (-1 = the calendar's). This client only: for proof shots
        /// (<c>sky.nebula.look</c>); every player still agrees on the calendar's pick.</summary>
        public static int NebulaLookOverride = -1;

        /// <summary>The running presenter (null on a server or before the first map).</summary>
        public static SkyEventPresenter Instance { get; private set; }

        /// <summary>What the sky shows this frame (sky.events on a client, a future migration layer).</summary>
        public SkyNow LastSky { get; private set; }

        /// <summary>How far the eclipse dims the world right now (0 none .. <see cref="SkyEventSettings.eclipseDarkness"/>).</summary>
        public float EclipseDim { get; private set; }

        static readonly Vector4[] MeteorA = new Vector4[SkyNow.MaxStreaks];
        static readonly Vector4[] MeteorB = new Vector4[SkyNow.MaxStreaks];

        Material overlay;
        Mesh dome;
        Material skyCopy, skySource;

        // eclipse dim bookkeeping: what the presenter set (the base) and what we wrote on top, so the base is never lost
        bool ambientDimmed;
        Color baseAmbSky, baseAmbEq, baseAmbGround, wroteAmbSky, wroteAmbEq, wroteAmbGround;
        float wroteAmbientDim;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { AuroraAllowed = null; Instance = null; NebulaLookOverride = -1; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (IsHeadlessRuntime || Instance != null) return;
            var go = new GameObject("KN Sky Events") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<SkyEventPresenter>();
        }

        /// <summary>A running headless server (or batch run): no sky to draw. Checked at run time only; nothing is stripped from builds.</summary>
        static bool IsHeadlessRuntime => Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreSkyMaterial();
            if (overlay != null) Destroy(overlay);
            if (dome != null) Destroy(dome);
        }

        void LateUpdate()
        {
            var env = WorldEnvironmentNetwork.Instance;
            var s = SkyEventSettings.Default;
            var dc = env != null ? env.DayCycle : null;
            var sky = RenderSettings.skybox;
            if (s == null || dc == null || sky == null || !HasPresenter())
            {
                EclipseDim = 0f;
                ambientDimmed = false;
                return;
            }

            float t = EnvironmentPresenter.PresentedTimeOfDay01;
            var now = Evaluate(env, dc, s, t);
            LastSky = now;

            UseStarlessSkyCopy(sky);
            float starVisibility = Shader.GetGlobalVector(Ids.SkyParams).y;
            Publish(now, s, env.Weather, starVisibility, WorldEffectsRuntime.Tier);
            ScaleNightMoon(s, dc);
            DimForEclipse(now, s, dc);
            DrawOverlay();
        }

        static bool HasPresenter()
        {
            if (presenterCheck > Time.unscaledTime) return cachedPresenter != null && cachedPresenter.isActiveAndEnabled;
            presenterCheck = Time.unscaledTime + 1f;
            if (cachedPresenter == null) cachedPresenter = FindFirstObjectByType<EnvironmentPresenter>();
            return cachedPresenter != null && cachedPresenter.isActiveAndEnabled;
        }
        static EnvironmentPresenter cachedPresenter;
        static float presenterCheck;

        // ------------------------------------------------------------------ calendar

        static SkyNow Evaluate(WorldEnvironmentNetwork env, DayCycleProfile dc, SkyEventSettings s, float t)
        {
            var st = env.State;
            long day = st.CalendarDay(env.Now);
            // the presented clock glides; keep the day in step with it around midnight so the dome never jumps
            float raw = env.TimeOfDay01;
            if (raw < 0.25f && t > 0.75f) day--;
            else if (raw > 0.75f && t < 0.25f) day++;
            SkyEventKind forced = env.ForcedSkyEvent(out double age);
            // the sun's visible disc, for the eclipse's real overlap: the sun art's half-size share when the art is on, else the plain disc
            bool sunArt = Shader.GetGlobalVector(Ids.BodyTexParams).z > 0.5f;
            float sunDisc = sunArt ? Mathf.Atan(Mathf.Tan(dc.sunTextureSize) * s.eclipseMoonSize) : dc.sunDiscSize;
            var c = new SkyClock
            {
                Seed = st.Seed, Day = day, Time01 = t, DayLengthSeconds = st.DayLengthSeconds, MonthDays = dc.lunarCycleDays,
                Forced = forced, ForcedAge = age, SunDiscRadians = sunDisc,
            };
            var now = SkyCalendar.Evaluate(c, s);
            if (now.Aurora > 0f && c.Forced != SkyEventKind.Aurora && !(AuroraAllowed?.Invoke() ?? false)) now.Aurora = 0f;
            return now;
        }

        // ------------------------------------------------------------------ old dot stars (pick Q2 A)

        void UseStarlessSkyCopy(Material sky)
        {
            if (sky == skyCopy) return;
            // a new map's presenter set its own sky material: copy that one
            if (skyCopy != null) Destroy(skyCopy);
            skySource = sky;
            skyCopy = new Material(sky) { name = sky.name + " (sky events)", hideFlags = HideFlags.DontSave };
            if (skyCopy.HasProperty(Ids.StarBrightness)) skyCopy.SetFloat(Ids.StarBrightness, 0f);
            RenderSettings.skybox = skyCopy;
            CopyBodyLook(sky);
        }

        void RestoreSkyMaterial()
        {
            if (skyCopy == null) return;
            if (RenderSettings.skybox == skyCopy && skySource != null) RenderSettings.skybox = skySource;
            Destroy(skyCopy);
            skyCopy = null;
        }

        // The second sun is drawn with the custom sun's own look (and the stars sit behind the same clouds): numbers the sky material holds.
        static readonly string[] BodyLook =
        {
            "_BodySaturation", "_BodyContrast", "_BodyInkLift", "_BodyHazeHeight", "_BodyHaze", "_BodyHorizonHaze",
            "_SunGlare", "_SunGlareRadius", "_SunGlareHDR", "_CloudHorizonFade", "_ToonEdge", "_HorizonFogHeight",
        };

        void CopyBodyLook(Material sky)
        {
            if (!EnsureOverlay()) return;
            foreach (var p in BodyLook)
                if (sky.HasProperty(p) && overlay.HasProperty(p)) overlay.SetFloat(p, sky.GetFloat(p));
            // where the moon art's disc sits, so the stars hide behind exactly that disc (owner 14 Nov, part 3)
            if (sky.HasProperty(Ids.MoonDisc) && overlay.HasProperty(Ids.MoonDisc)) overlay.SetVector(Ids.MoonDisc, sky.GetVector(Ids.MoonDisc));
        }

        // ------------------------------------------------------------------ the night moon, bigger and closer (owner 14 Nov, part 3)

        /// <summary>
        /// Draws the night moon <see cref="SkyEventSettings.moonScale"/> times its day-cycle size. Only the y of <c>_KN_BodyTexParams</c> (tan of
        /// the moon art's radius) changes, rebuilt from the day cycle every frame (so it never compounds); x, z and w are written back exactly as
        /// <see cref="EnvironmentPresenter"/> set them this frame. In the custom sky that y is read only by the moon (its art's billboard and the
        /// rim clouds catch around it); the custom sun reads x. The moon's light, direction and phase are not touched.
        /// </summary>
        static void ScaleNightMoon(SkyEventSettings s, DayCycleProfile dc)
        {
            if (Mathf.Approximately(s.moonScale, 1f)) return;
            Vector4 body = Shader.GetGlobalVector(Ids.BodyTexParams);
            body.y = Mathf.Tan(Mathf.Clamp(dc.moonTextureSize * s.moonScale, 0.001f, 1.2f));
            Shader.SetGlobalVector(Ids.BodyTexParams, body);
        }

        // ------------------------------------------------------------------ globals for the overlay

        static void Publish(in SkyNow sky, SkyEventSettings s, WeatherBlend w, float starVisibility, int effectsTier)
        {
            bool on = s.starMap != null;
            if (on) Shader.SetGlobalTexture(Ids.StarMap, s.starMap);

            // stars: the baked map turned by the calendar (owner 14 Nov: stars only, no constellation lines)
            float clear = 1f - Mathf.Clamp01(w.coverage);
            Shader.SetGlobalVector(Ids.StarParams, new Vector4(on ? 1f : 0f, s.starBrightness, 0f, 0f));
            Shader.SetGlobalVector(Ids.StarBasisX, sky.StarBasisX);
            Shader.SetGlobalVector(Ids.StarBasisY, sky.StarBasisY);
            Shader.SetGlobalVector(Ids.StarBasisZ, sky.StarBasisZ);

            Vector3 n = sky.NebulaDir.sqrMagnitude > 0f ? sky.NebulaDir : Vector3.up;
            // the night's nebula look (Soul Tide, Ember Rose or Veil Ribbon), picked by the calendar
            int look = NebulaLookOverride >= 0 ? NebulaLookOverride : sky.NebulaLook;
            var neb = s.nebulas != null && look >= 0 && look < s.nebulas.Length ? s.nebulas[look] : null;
            bool nebOn = neb != null && neb.texture != null;
            if (nebOn) Shader.SetGlobalTexture(Ids.NebulaTex, neb.texture);
            Shader.SetGlobalVector(Ids.Nebula, new Vector4(n.x, n.y, n.z, nebOn ? sky.Nebula * neb.brightness : 0f));
            Shader.SetGlobalVector(Ids.NebulaParams, new Vector4(Mathf.Tan((nebOn ? neb.degrees : 38f) * 0.5f * Mathf.Deg2Rad), 0f, 0f, 0f));

            // World Effects: fewer streaks at once on Low (the calendar still decides which ones, so clients agree on those shown).
            // The motion and fades are worked out here, once per streak, so the shader only draws a tapered line and a head.
            int streaks = Mathf.Min(sky.StreakCount, s.StreaksFor(effectsTier));
            for (int i = 0; i < SkyNow.MaxStreaks; i++)
            {
                if (i < streaks) StreakShape(sky.Streak(i), s, out MeteorA[i], out MeteorB[i]);
                else { MeteorA[i] = Vector4.zero; MeteorB[i] = Vector4.zero; }
            }
            Shader.SetGlobalVector(Ids.StreakParams, new Vector4(InvTwoSigmaSq(s.streakWidthDegrees), InvTwoSigmaSq(s.streakHeadDegrees), streaks, 0f));
            Shader.SetGlobalVector(Ids.ShowerParams, new Vector4(InvTwoSigmaSq(s.showerWidthDegrees), InvTwoSigmaSq(s.showerHeadDegrees), 0f, 0f));
            // HDR colours are the shader's own (linear) values: above 1 they bloom (the lone streak's 1.6 is the old look)
            Shader.SetGlobalVector(Ids.StreakColor, s.streakColor);
            Shader.SetGlobalVector(Ids.ShowerHead, s.showerHeadColor);
            Shader.SetGlobalVector(Ids.ShowerTrail, s.showerTrailColor);
            Shader.SetGlobalVectorArray(Ids.MeteorA, MeteorA);
            Shader.SetGlobalVectorArray(Ids.MeteorB, MeteorB);

            Shader.SetGlobalVector(Ids.Comet, new Vector4(sky.CometHead.x, sky.CometHead.y, sky.CometHead.z, sky.Comet * s.cometBrightness));
            Shader.SetGlobalVector(Ids.CometParams, new Vector4(s.cometTailDegrees * Mathf.Deg2Rad, 0f, 0f, 0f));

            // the second sun: art size and tint of its own; it never touches a light, the ambient, fog or reflections
            // owner 14 Nov, part 3: never at night. The calendar already gates it to daytime; this also follows our sun's real height, so the
            // second sun goes as our sun sets whatever the day cycle's sunset time is.
            float ourSunUp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.03f, 0.08f, Shader.GetGlobalVector(Ids.SunDir).y));
            Shader.SetGlobalVector(Ids.Sun2, new Vector4(sky.Sun2Dir.x, sky.Sun2Dir.y, sky.Sun2Dir.z, sky.Sun2 * ourSunUp));
            Color c2 = s.sun2Color.linear;
            Shader.SetGlobalVector(Ids.Sun2Color, new Vector4(c2.r, c2.g, c2.b, Mathf.Tan(s.sun2Degrees * Mathf.Deg2Rad)));
            Shader.SetGlobalVector(Ids.Sun2Params, new Vector4(s.sun2Brightness, 0f, 0f, 0f));

            Color corona = s.coronaColor.linear, wash = s.eclipseSkyColor.linear;
            Shader.SetGlobalVector(Ids.Eclipse, new Vector4(sky.Eclipse, Mathf.Clamp01((sky.Eclipse - 0.85f) / 0.15f), s.eclipseDarkness, sky.EclipseMoon ? 1f : 0f));
            Shader.SetGlobalVector(Ids.EclipseMoon, new Vector4(sky.EclipseMoonAngle, s.eclipsePathDegrees * Mathf.Deg2Rad, sky.EclipseMoonRadius, sky.EclipseSunRadius));
            Shader.SetGlobalVector(Ids.CoronaColor, new Vector4(corona.r, corona.g, corona.b, 1f));
            Shader.SetGlobalVector(Ids.EclipseSky, new Vector4(wash.r, wash.g, wash.b, 1f));

            Color lo = s.auroraLow.linear, hi = s.auroraHigh.linear;
            bool auroraOn = effectsTier > (int)WorldEffects.Low || s.auroraOnLow;
            Shader.SetGlobalVector(Ids.Aurora, new Vector4(auroraOn ? sky.Aurora * s.auroraBrightness * clear : 0f, 0f, 0f, 0f));
            Shader.SetGlobalVector(Ids.AuroraLow, new Vector4(lo.r, lo.g, lo.b, 1f));
            Shader.SetGlobalVector(Ids.AuroraHigh, new Vector4(hi.r, hi.g, hi.b, 1f));
        }

        static float InvTwoSigmaSq(float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return 1f / Mathf.Max(1e-8f, 2f * r * r);
        }

        /// <summary>
        /// A streak's look at its point in life (owner 14 Nov, part 3: longer trails, a smooth fade): the head flies the first 80 % of its life
        /// and slows as it burns out; the trail covers <see cref="SkyEventSettings.streakTrail"/> of the path behind it and fades out smoothly
        /// after the head goes. a = (tail end, trail amount), b = (head, head amount, +2 for an Emberfall streak).
        /// </summary>
        public static void StreakShape(in SkyStreak st, SkyEventSettings s, out Vector4 a, out Vector4 b)
        {
            float p = Mathf.Clamp01(st.Progress);
            float fly = Mathf.Clamp01(p / 0.8f);
            float h = 1f - (1f - fly) * (1f - fly);
            float trail = Mathf.Clamp01(s.streakTrail);
            Vector3 head = Vector3.Slerp(st.Start, st.End, h);
            Vector3 tail = Vector3.Slerp(st.Start, st.End, Mathf.Max(0f, h - trail));
            float born = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(p / 0.06f));
            float headAmt = born * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 0.85f, p))) * st.Brightness;
            float trailAmt = born * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, p))) * st.Brightness;
            a = new Vector4(tail.x, tail.y, tail.z, trailAmt);
            b = new Vector4(head.x, head.y, head.z, headAmt + (st.Shower ? 2f : 0f));
        }

        // ------------------------------------------------------------------ eclipse: the world dims toward night (owner 14 Nov)

        void DimForEclipse(in SkyNow sky, SkyEventSettings s, DayCycleProfile dc)
        {
            // smooth in and out with the cover (the cover itself already eases from first bite to the end)
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(sky.Eclipse)) * Mathf.Clamp01(s.eclipseDarkness);
            EclipseDim = k;

            // ambient: the presenter writes it on a timer, not every frame, so keep its own value as the base and never stack
            Color curSky = RenderSettings.ambientSkyColor, curEq = RenderSettings.ambientEquatorColor, curGround = RenderSettings.ambientGroundColor;
            if (!ambientDimmed || curSky != wroteAmbSky || curEq != wroteAmbEq || curGround != wroteAmbGround)
            {
                baseAmbSky = curSky; baseAmbEq = curEq; baseAmbGround = curGround;
                ambientDimmed = false;
            }

            if (k <= 0f)
            {
                // nothing written to the light or fog this frame; put back the presenter's exact ambient if we had moved it
                if (ambientDimmed)
                {
                    RenderSettings.ambientSkyColor = baseAmbSky;
                    RenderSettings.ambientEquatorColor = baseAmbEq;
                    RenderSettings.ambientGroundColor = baseAmbGround;
                    ambientDimmed = false;
                }
                wroteAmbientDim = 0f;
                return;
            }

            // the day cycle's midnight values, read only
            const float Midnight = 0f;
            var key = RenderSettings.sun;
            if (key != null)
            {
                key.intensity = Mathf.Lerp(key.intensity, dc.moonIntensity.Evaluate(Midnight), k);
                key.color = Color.Lerp(key.color, dc.moonColor.Evaluate(Midnight), k);
            }
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, dc.fogColor.Evaluate(Midnight), k);

            // the SH probe is rebuilt when the ambient changes: write it only when the presenter did or the dim moved enough
            if (!ambientDimmed || Mathf.Abs(k - wroteAmbientDim) > 0.01f)
            {
                wroteAmbSky = Color.Lerp(baseAmbSky, dc.ambientSky.Evaluate(Midnight), k);
                wroteAmbEq = Color.Lerp(baseAmbEq, dc.ambientEquator.Evaluate(Midnight), k);
                wroteAmbGround = Color.Lerp(baseAmbGround, dc.ambientGround.Evaluate(Midnight), k);
                RenderSettings.ambientSkyColor = wroteAmbSky;
                RenderSettings.ambientEquatorColor = wroteAmbEq;
                RenderSettings.ambientGroundColor = wroteAmbGround;
                // read back: Unity may store the colour with less precision than we passed
                wroteAmbSky = RenderSettings.ambientSkyColor;
                wroteAmbEq = RenderSettings.ambientEquatorColor;
                wroteAmbGround = RenderSettings.ambientGroundColor;
                wroteAmbientDim = k;
                ambientDimmed = true;
            }
        }

        // ------------------------------------------------------------------ the overlay dome

        bool EnsureOverlay()
        {
            if (overlay != null) return true;
            var s = SkyEventSettings.Default;
            var shader = s != null && s.overlayShader != null ? s.overlayShader : Shader.Find(OverlayShader);
            if (shader == null || !shader.isSupported) return false;
            overlay = new Material(shader) { name = "Sky Events (runtime)", hideFlags = HideFlags.DontSave };
            return true;
        }

        void DrawOverlay()
        {
            var cam = Camera.main;
            if (cam == null || !EnsureOverlay()) return;
            if (dome == null) dome = BuildDome();
            var rp = new RenderParams(overlay)
            {
                camera = cam,   // the game camera only: never the minimap, portraits, reflection or probe cameras
                worldBounds = new Bounds(cam.transform.position, Vector3.one * 100000f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                layer = cam.gameObject.layer,
            };
            Graphics.RenderMesh(rp, dome, 0, Matrix4x4.identity);
        }

        /// <summary>A unit sphere seen from inside (the shader puts it on the far plane around the camera).</summary>
        static Mesh BuildDome()
        {
            const int Rings = 24, Segments = 48;
            var v = new Vector3[(Rings + 1) * (Segments + 1)];
            var tri = new int[Rings * Segments * 6];
            int vi = 0;
            for (int r = 0; r <= Rings; r++)
            {
                float phi = Mathf.PI * r / Rings;
                for (int sgm = 0; sgm <= Segments; sgm++)
                {
                    float th = 2f * Mathf.PI * sgm / Segments;
                    v[vi++] = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                }
            }
            int ti = 0;
            for (int r = 0; r < Rings; r++)
                for (int sgm = 0; sgm < Segments; sgm++)
                {
                    int a = r * (Segments + 1) + sgm, b = a + Segments + 1;
                    tri[ti++] = a; tri[ti++] = a + 1; tri[ti++] = b;
                    tri[ti++] = a + 1; tri[ti++] = b + 1; tri[ti++] = b;
                }
            var m = new Mesh { name = "Sky Events Dome", hideFlags = HideFlags.DontSave, vertices = v, triangles = tri };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            return m;
        }

        static class Ids
        {
            public static readonly int StarBrightness = Shader.PropertyToID("_StarBrightness");
            public static readonly int SkyParams = Shader.PropertyToID("_KN_SkyParams");
            public static readonly int StarMap = Shader.PropertyToID("_KN_StarMap");
            public static readonly int NebulaTex = Shader.PropertyToID("_KN_NebulaTex");
            public static readonly int StarParams = Shader.PropertyToID("_KN_StarParams");
            public static readonly int StarBasisX = Shader.PropertyToID("_KN_StarBasisX");
            public static readonly int StarBasisY = Shader.PropertyToID("_KN_StarBasisY");
            public static readonly int StarBasisZ = Shader.PropertyToID("_KN_StarBasisZ");
            public static readonly int Nebula = Shader.PropertyToID("_KN_Nebula");
            public static readonly int NebulaParams = Shader.PropertyToID("_KN_NebulaParams");
            public static readonly int MeteorA = Shader.PropertyToID("_KN_StreakA");
            public static readonly int MeteorB = Shader.PropertyToID("_KN_StreakB");
            public static readonly int StreakParams = Shader.PropertyToID("_KN_StreakParams");
            public static readonly int ShowerParams = Shader.PropertyToID("_KN_ShowerParams");
            public static readonly int StreakColor = Shader.PropertyToID("_KN_StreakColor");
            public static readonly int ShowerHead = Shader.PropertyToID("_KN_ShowerHead");
            public static readonly int ShowerTrail = Shader.PropertyToID("_KN_ShowerTrail");
            public static readonly int EclipseMoon = Shader.PropertyToID("_KN_EclipseMoon");
            public static readonly int BodyTexParams = Shader.PropertyToID("_KN_BodyTexParams");
            public static readonly int SunDir = Shader.PropertyToID("_KN_SunDir");
            public static readonly int MoonDisc = Shader.PropertyToID("_MoonDisc");
            public static readonly int Comet = Shader.PropertyToID("_KN_Comet");
            public static readonly int CometParams = Shader.PropertyToID("_KN_CometParams");
            public static readonly int Sun2 = Shader.PropertyToID("_KN_Sun2");
            public static readonly int Sun2Color = Shader.PropertyToID("_KN_Sun2Color");
            public static readonly int Sun2Params = Shader.PropertyToID("_KN_Sun2Params");
            public static readonly int Eclipse = Shader.PropertyToID("_KN_Eclipse");
            public static readonly int CoronaColor = Shader.PropertyToID("_KN_CoronaColor");
            public static readonly int EclipseSky = Shader.PropertyToID("_KN_EclipseSky");
            public static readonly int Aurora = Shader.PropertyToID("_KN_Aurora");
            public static readonly int AuroraLow = Shader.PropertyToID("_KN_AuroraLow");
            public static readonly int AuroraHigh = Shader.PropertyToID("_KN_AuroraHigh");
        }
    }
}
