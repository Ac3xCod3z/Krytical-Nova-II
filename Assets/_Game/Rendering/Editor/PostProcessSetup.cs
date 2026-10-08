using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Writes the comic-grade post look into the scene's global volume profile (see Rendering-Bible.md, "Post volume
    /// rules"): neutral tonemapping, a warm restrained bloom, indigo/warm split toning, an ink-coloured vignette and a
    /// light print grain, plus SMAA on the main camera. Colour Adjustments stay owned by EnvironmentPresenter at runtime.
    /// Re-runnable: it only sets the overrides it owns.
    /// </summary>
    public static class PostProcessSetup
    {
        private const string ProfilePath = "Assets/_Game/Rendering/Settings/SampleSceneProfile.asset";

        [MenuItem("Krytical Nova/Lighting/Apply Post Processing Look")]
        public static void Apply()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null) { Debug.LogError($"[PostProcess] Missing {ProfilePath}"); return; }
            Undo.RecordObject(profile, "Apply post look");

            // Tonemapping, bloom and vignette belong to Beautify (ApplyBeautify). URP's copies are switched off so
            // nothing runs twice; Beautify sees the HDR frame first, so its tonemap/bloom get the right input.
            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.None);
            var bloom = Get<Bloom>(profile);
            bloom.intensity.Override(0f);
            bloom.active = false;

            // Split toning: shadows lean to the comic indigo, highlights to warm paper. Kept subtle (near-grey inputs).
            var split = Get<SplitToning>(profile);
            split.shadows.Override(ColorBible.Hex("6E74A4"));
            split.highlights.Override(ColorBible.Hex("B8A68A"));
            split.balance.Override(0f);

            // A gentle lift of the darks toward ink-indigo instead of pure black.
            var lgg = Get<LiftGammaGain>(profile);
            lgg.lift.Override(new Vector4(1f, 0.99f, 1.01f, 0.005f));
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, 0f));
            lgg.gain.Override(new Vector4(1.02f, 1.005f, 0.98f, 0.02f));

            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0f);
            vignette.active = false;

            // Faint print grain: reads as a printed page under the halftone shadows, not as noise.
            var grain = Get<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.2f);
            grain.response.Override(0.85f);

            var motion = Get<MotionBlur>(profile);
            motion.active = false;

            ApplyBeautify(profile);
            EnsureGlowHalo();

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // Anti-aliasing: SMAA keeps the ink outlines crisp (FXAA smears them; TAA ghosts the halftone dots).
            var cam = Camera.main;
            if (cam != null && cam.TryGetComponent<UniversalAdditionalCameraData>(out var data))
            {
                Undo.RecordObject(data, "Camera AA");
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            }
            Debug.Log("[PostProcess] Post look applied to " + ProfilePath + (cam != null ? " and SMAA set on the main camera." : "."));
        }

        /// <summary>
        /// The halo of light round tempered weapons and evolved pets (see Rendering-Bible.md, "Glow halo"): makes sure
        /// the PC renderer carries the feature. Its numbers are left as they are, so tuning on the renderer asset survives.
        /// </summary>
        public static void EnsureGlowHalo()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer == null) { Debug.LogError($"[PostProcess] Missing {PcRendererPath}"); return; }
            RendererFeatureUtil.Ensure<KryticalNova.Rendering.GlowHaloFeature>(renderer, "GlowHalo", f =>
            {
                // Referenced, not found by name: a hidden shader nothing points at is left out of a build.
                var feature = new SerializedObject(f);
                SerializedProperty shader = feature.FindProperty("shader");
                if (shader.objectReferenceValue != null) return;
                shader.objectReferenceValue = AssetDatabase.LoadAssetAtPath<Shader>(GlowHaloShaderPath);
                feature.ApplyModifiedPropertiesWithoutUndo();
            });
        }

        /// <summary>
        /// Beautify 3 runs BEFORE URP post here (its pass is AfterRenderingTransparents, on the HDR frame; see
        /// Rendering-Bible.md, "Frame order"). It owns tonemapping (PBR Neutral, the closest match to the old URP
        /// Neutral look), the warm restrained bloom and the ink vignette, plus a depth-aware sharpen, a vibrance lift,
        /// a faint anamorphic streak and dithering. URP keeps Split Toning, Lift Gamma Gain, Film Grain and Chromatic
        /// Aberration (GameFeel punches CA on heavy hits; the hurt pulse drives Beautify's vignette at runtime).
        /// Depth of field is banned by the Bible.
        /// </summary>
        private static void ApplyBeautify(VolumeProfile profile)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer != null) RendererFeatureUtil.Ensure<Beautify.Universal.BeautifyRendererFeature>(renderer, "Beautify",
                f => f.stripBeautifyTonemappingPBRNeutral = false); // stripped by default

            var b = Get<Beautify.Universal.Beautify>(profile);
            b.disabled.Override(false);
            b.sharpenIntensity.Override(1.6f);
            b.sharpenDepthThreshold.Override(0.035f);
            b.sharpenClamp.Override(0.3f);
            b.sharpenRelaxation.Override(0.1f);
            b.sharpenMotionSensibility.Override(0.6f);
            b.saturate.Override(0.35f);           // vibrance: lifts muted tones, leaves saturated ones
            b.contrast.Override(1.03f);
            b.brightness.Override(1f);
            b.tonemap.Override(Beautify.Universal.Beautify.TonemapOperator.PBRNeutral);
            b.tonemapExposurePre.Override(1f);
            b.tonemapBrightnessPost.Override(1f);
            // Only the HDR budget (sun glare, shafts, lightning, emissive signs/VFX) blooms; a warm tint keeps it sunny.
            b.bloomIntensity.Override(0.4f);
            b.bloomThreshold.Override(1f);
            b.bloomSpread.Override(0.58f);
            b.bloomAntiflicker.Override(true);
            var warm = ColorBible.Hex("FFF1E0");
            b.bloomTint.Override(new Color(warm.r, warm.g, warm.b, 0.35f));
            b.anamorphicFlaresIntensity.Override(0.12f);
            b.anamorphicFlaresThreshold.Override(0.9f);
            b.anamorphicFlaresSpread.Override(1.2f);
            b.anamorphicFlaresTint.Override(new Color(1f, 0.82f, 0.6f, 0f));
            b.sunFlaresIntensity.Override(0f);    // EnvironmentPresenter's LensFlare owns the sun
            b.depthOfField.Override(false);
            // Comic-panel framing in ink, not black. Rings are squared distances from the centre: clear inside the inner
            // ring, full ink past the outer. Alpha is the strength. vignettingFade is NOT edge softness: it fades the
            // whole frame to the vignette colour (blink / fade-out), so it stays 0. At 0.45 it laid a flat ink veil over
            // every pixel, which read as purple haze at night when the scene is darker than the ink.
            b.vignettingOuterRing.Override(0.5f);
            b.vignettingInnerRing.Override(0.1f);
            b.vignettingFade.Override(0f);
            b.vignettingCircularShape.Override(false);
            var ink = ColorBible.Ink;
            b.vignettingColor.Override(new Color(ink.r, ink.g, ink.b, 0.6f));
            b.filmGrainEnabled.Override(false);
            b.chromaticAberrationIntensity.Override(0f);
            b.outline.Override(false);            // ComicStyleFeature owns the ink outlines
            b.ditherIntensity.Override(0.005f);
            b.eyeAdaptation.Override(false);
        }

        private const string PcRendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";
        private const string GlowHaloShaderPath = "Assets/_Game/Rendering/Shaders/Comic/GlowHalo.shader";

        private static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(false);
                // Sub-assets keep the profile self-contained (Volume components are ScriptableObjects).
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }
    }
}
