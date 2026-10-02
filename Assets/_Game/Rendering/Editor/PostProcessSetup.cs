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

            var tone = Get<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.Neutral);

            // Only the HDR budget (sun glare, shafts, lightning, emissive VFX) blooms; a warm tint keeps glows sunny.
            var bloom = Get<Bloom>(profile);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(0.36f);
            bloom.scatter.Override(0.58f);
            bloom.tint.Override(ColorBible.Hex("FFF1E0"));
            bloom.highQualityFiltering.Override(true);

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
            vignette.color.Override(ColorBible.Ink);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            vignette.rounded.Override(false);

            // Faint print grain: reads as a printed page under the halftone shadows, not as noise.
            var grain = Get<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.2f);
            grain.response.Override(0.85f);

            var motion = Get<MotionBlur>(profile);
            motion.active = false;

            ApplyBeautify(profile);

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
        /// Beautify 3 runs after URP post (Rendering-Bible.md, "Post stack order"): it only adds what URP lacks —
        /// a depth-aware sharpen that crisps the ink lines and halftone dots, a vibrance lift, a whisper of anamorphic
        /// streak on HDR highlights and dithering against fog banding. URP keeps tonemapping, bloom, vignette and
        /// grain; Beautify's copies stay off so nothing runs twice. Depth of field is banned by the Bible.
        /// Chromatic aberration is left at 0 here; GameFeel punches it through a runtime volume on big hits.
        /// </summary>
        private static void ApplyBeautify(VolumeProfile profile)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (renderer != null) RendererFeatureUtil.Ensure<Beautify.Universal.BeautifyRendererFeature>(renderer, "Beautify", f => { });

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
            b.tonemap.Override(Beautify.Universal.Beautify.TonemapOperator.Linear); // URP Neutral owns tonemapping
            b.bloomIntensity.Override(0f);
            b.anamorphicFlaresIntensity.Override(0.12f);
            b.anamorphicFlaresThreshold.Override(0.9f);
            b.anamorphicFlaresSpread.Override(1.2f);
            b.anamorphicFlaresTint.Override(new Color(1f, 0.82f, 0.6f, 0f));
            b.sunFlaresIntensity.Override(0f);    // EnvironmentPresenter's LensFlare owns the sun
            b.depthOfField.Override(false);
            b.vignettingOuterRing.Override(0f);
            b.filmGrainEnabled.Override(false);
            b.chromaticAberrationIntensity.Override(0f);
            b.outline.Override(false);            // ComicStyleFeature owns the ink outlines
            b.ditherIntensity.Override(0.005f);
            b.eyeAdaptation.Override(false);
        }

        private const string PcRendererPath = "Assets/_Game/Rendering/Settings/PC_Renderer.asset";

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
