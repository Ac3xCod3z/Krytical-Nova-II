using System;
using UnityEngine;

namespace GI
{
    public enum DebugChoice
    {
        None,
        Diffuse,
        Probes,
        ProbeTexels,
        Depths,
        Surfels,
        GDF,
        SDF,
        FarProbeTexels,
        FarProbeTraces,
        ScreenTraces
    }

    public enum SearchType
    {
        None,
        InGame,
        Always
    }

    public enum RetraceChoice
    {
        All,
        Partial,
        OnlyNew
    }

    public enum ProbeChoice
    {
        SH2,
        SH3,
        Simple
    }

    public enum RendererChoice
    {
        Forward,
        Deferred,
        DeferredAccurateNormals
    }

    public enum TemporalChoice
    {
        None,
        Reprojection,
        MotionVectors
    }

    [Serializable]
    public struct GISettingsExperimental
    {
        /// <summary>
        /// The results of ray tracing are remembered and stored in a map of surfel IDs.
        /// Surfels are looked up when updating probes, avoiding the need to retrace.
        /// This allows the use of Partial or Only New retrace options to still have probes that dynamically react to lighting changes in the environment.
        /// </summary>
        [Tooltip("The results of ray tracing are remembered and stored in a map of surfel IDs. Surfels are looked up when updating probes, avoiding the need to retrace. This allows the use of Partial or Only New retrace options to still have probes that dynamically react to lighting changes in the environment.")]
        public bool SurfelMapping;

        /// <summary>
        /// Generates screen probes in screen-space. Allows for much greater accuracy of lighting. Comes with much greater performance cost and can be noisy. Implicitly enables temporal accumulation.
        /// </summary>
        [Tooltip("Generates screen probes in screen-space. Allows for much greater accuracy of lighting. Comes with much greater performance cost and can be noisy. Implicitly enables temporal accumulation.")]
        public bool ScreenProbes;

        /// <summary>
        /// Screen probes are placed on the grid in screen-space.
        /// Adaptive probes are additionaly placed in spots that normal screen probes can't shade.
        /// MaxAdaptiveProbes limits the number of such adaptive probes.
        /// </summary>
        [Tooltip("Screen probes are placed on the grid in screen-space. Adaptive probes are additionaly placed in spots that normal screen probes can't shade. MaxAdaptiveProbes limits the number of such adaptive probes.")]
        public int MaxAdaptiveProbes;

        public static GISettingsExperimental Default()
        {
            var settings = new GISettingsExperimental();
            settings.MaxAdaptiveProbes = 4096;

            return settings;
        }
    }

    [Serializable]
    public class GISettings
    {
        /// <summary>
        /// Multipler for the final result to be applied on the scene.
        /// </summary>
        [Tooltip("Multipler for the final result to be applied on the scene.")]
        [Range(0, 4)]
        public float DiffuseMultiplier = 1.0f;

        /// <summary>
        /// Spacing between each probe in the starting cascade.
        /// </summary>
        [Tooltip("Spacing between each probe in the starting cascade.")]
        [Range(0.8f, 4)]
        public float Cell = 1.0f;

        /// <summary>
        /// Number of probe cascades.
        /// </summary>
        [Tooltip("Number of probe cascades.")]
        [Range(1, 8)]
        public int Cascades = 8;

        /// <summary>
        /// Number of GDF (Global Distance Field) cascades. GDF is generated out of the SDFs present in the scene.
        /// </summary>
        [Tooltip("Number of GDF (Global Distance Field) cascades. GDF is generated out of the SDFs present in the scene.")]
        [Range(1, 4)]
        public int GDFCascades = 4;

        /// <summary>
        /// Enables applying the lighting to the final image. Useful to turn off when using custom shaders.
        /// </summary>
        [Tooltip("Enables applying the lighting to the final image. Useful to turn off when using custom shaders.")]
        public bool Apply = true;

        /// <summary>
        /// Select appropriate choice for the renderer setup in Unity. DeferredAccurateNormals refers to Deferred mode with G-buffer normals turned on.
        /// </summary>
        [Tooltip("Select appropriate choice for the renderer setup in Unity. DeferredAccurateNormals refers to Deferred mode with G-buffer normals turned on.")]
        public RendererChoice Renderer = RendererChoice.Forward;

        /// <summary>
        /// Uses the rendered results of previous frames to stabilize the lighting.
        /// Can introduce ghosting, normally not needed.
        /// </summary>
        [Tooltip("Uses the rendered results of previous frames to stabilize the lighting. Can introduce ghosting, normally not needed.")]
        public TemporalChoice TemporalAccumulation = TemporalChoice.MotionVectors;

        /// <summary>
        /// Enables detailed tracing of SDFs close to each probe. Normally only the global distance field is used which has less precision.
        /// </summary>
        [Tooltip("Enables detailed tracing of SDFs close to each probe. Normally only the global distance field is used which has less precision.")]
        public bool LocalTracing = true;

        /// <summary>
        /// Applies a filter on probes that stabilizes the lighting at some small performance cost.
        /// </summary>
        [Tooltip("Applies a filter on probes that stabilizes the lighting at some small performance cost.")]
        public bool ProbeFiltering = true;

        /// <summary>
        /// Calculates a shadow for each probe where the pixels not visible to the probe do not receive the light from it. Safe to turn off for all-outdoor environments, as the lack of occlusion is mostly visible when mixing indoor and outdoor areas.
        /// </summary>
        [Tooltip("Calculates a shadow for each probe where the pixels not visible to the probe do not receive the light from it. Safe to turn off for all-outdoor environments, as the lack of occlusion is mostly visible when mixing indoor and outdoor areas.")]
        public bool ProbeOcclusion = true;

        /// <summary>
        /// Normally only probes visible by the camera are maintained.
        /// This option maintains the probes around surfels visible from the probes on the screen.
        /// Light bouncing from sun or point lights does not require these probes.
        /// Comes with a performance hit as more probes are alive. In a bright environment the second bounce from off-screen probes is negligible.
        /// </summary>
        [Tooltip("Normally only probes visible by the camera are maintained. This option maintains the probes around surfels visible from the probes on the screen. Light bouncing from sun or point lights does not require these probes. Comes with a performance hit as more probes are alive. In a bright environment the second bounce from off-screen probes is negligible.")]
        public bool SurfelsMaintainProbes = false;

        /// <summary>
        /// Far probes are probes with wider spacing than normal probes.
        /// Enabling this option sets the normal probes to trace shorter rays, long rays are then handled by far probes.
        /// Results in much more stabler lighting.
        /// </summary>
        [Tooltip("Far probes are probes with wider spacing than normal probes. Enabling this option sets the normal probes to trace shorter rays, long rays are then handled by far probes. Results in much more stabler lighting.")]
        public bool FarProbes = true;

        /// <summary>
        /// Creates probes around pixels of transparent objects.
        /// Draws transparent objects into a separate render target with a custom shader and uses the results to generate probes.
        /// </summary>
        [Tooltip("Creates probes around pixels of transparent objects. Draws transparent objects into a separate render target with a custom shader and uses the results to generate probes.")]
        public bool TransparentQueries = true;

        /// <summary>
        /// Multiplier for the light bouncing off the surfaces.
        /// Multiplier is not applied to visible sky.
        /// Setting it to 0 turns the system to effectively just Ambient Occlusion.
        /// </summary>
        [Tooltip("Multiplier for the light bouncing off the surfaces. Multiplier is not applied to visible sky. Setting it to 0 turns the system to effectively just Ambient Occlusion.")]
        [Range(0, 1)]
        public float BounceStrength = 0.99f;

        /// <summary>
        /// Choice of probe quality. SH stands for spherical harmonics.
        /// SH2 takes less space and computing power than SH3 but loses some accuracy, does not handle well strong lights coming from one side.
        /// SH3 is more stable and accurate with some computing cost.
        /// Simple probes calculate a single most representative light value and apply without taking directions into account. It is the fastest solution.
        /// </summary>
        [Tooltip("Choice of probe quality. SH stands for spherical harmonics. SH2 takes less space and computing power than SH3 but loses some accuracy, does not handle well strong lights coming from one side. SH3 is more stable and accurate with some computing cost. Simple probes calculate a single most representative light value and apply without taking directions into account. It is the fastest solution.")]
        public ProbeChoice Probes = ProbeChoice.SH3;

        /// <summary>
        /// How often probe rays are retraced.
        /// All means every frame all rays.
        /// Partial means every frame only 1/4th of the frames.
        /// Only new means the probe finds out it's surroundings at the start and doesn't change afterwards.
        /// </summary>
        [Tooltip("How often probe rays are retraced. All means every frame all rays. Partial means every frame only 1/4th of the frames. Only new means the probe finds out it's surroundings at the start and doesn't change afterwards.")]
        public RetraceChoice Retrace;

        public GISettingsExperimental Experimental = GISettingsExperimental.Default();
    }
}