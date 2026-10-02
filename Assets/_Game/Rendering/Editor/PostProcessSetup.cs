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
