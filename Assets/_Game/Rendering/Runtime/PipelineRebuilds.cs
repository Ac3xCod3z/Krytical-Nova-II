using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Some settings of the pipeline asset make URP throw the whole pipeline away and build it again when they change (the
    /// small-mesh cut-off, the GPU Resident Drawer's occlusion culling). In a built game a rebuild while a map's baked
    /// light probes (APV) are loaded fails ("An item with the same key has already been added" in ProbeVolumeBakingSet)
    /// and no pipeline is left: the screen stays black while the music plays (owner, 22 Oct 2026, build 10: every entry
    /// into Concord, since the character's LOD applied the graphics tier's small-mesh cut-off there). So such a change is
    /// made only while no map's probes are loaded; one asked for in a map waits for the next menu. The small-mesh cut-off
    /// is remembered, so the next launch puts it on at the title, before any map.
    /// </summary>
    public static class PipelineRebuilds
    {
        private const string SmallMeshKey = "kn.graphics.smallMeshCull";

        private static float? pendingSmallMesh;
        private static bool? pendingOcclusion;

        /// <summary>True while no map's baked probes are loaded: the pipeline may be built again now.</summary>
        public static bool Safe
        {
            get
            {
                ProbeReferenceVolume probes = ProbeReferenceVolume.instance;
                return probes == null || probes.currentBakingSet == null;
            }
        }

        /// <summary>The small-mesh cut-off (percent of the screen) for the graphics tier: now when safe, else at the next menu.</summary>
        public static void SetSmallMeshCull(float percent)
        {
            PlayerPrefs.SetFloat(SmallMeshKey, percent);
            pendingSmallMesh = percent;
            Apply();
        }

        /// <summary>The GPU Resident Drawer's occlusion culling (a GM switch): now when safe, else at the next menu.</summary>
        public static void SetOcclusion(bool on)
        {
            pendingOcclusion = on;
            Apply();
        }

        /// <summary>True when a change asked for is still waiting for a menu.</summary>
        public static bool Waiting => pendingSmallMesh != null || pendingOcclusion != null;

        private static void Apply()
        {
            if (!Waiting || !Safe || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            if (pendingSmallMesh is float small)
            {
#if UNITY_EDITOR
                if (authoredSmallMesh == null) authoredSmallMesh = urp.smallMeshScreenPercentage;
#endif
                if (!Mathf.Approximately(urp.smallMeshScreenPercentage, small)) urp.smallMeshScreenPercentage = small;
                pendingSmallMesh = null;
            }
            if (pendingOcclusion is bool occlusion)
            {
#if UNITY_EDITOR
                if (authoredOcclusion == null) authoredOcclusion = urp.gpuResidentDrawerEnableOcclusionCullingInCameras;
#endif
                if (urp.gpuResidentDrawerEnableOcclusionCullingInCameras != occlusion) urp.gpuResidentDrawerEnableOcclusionCullingInCameras = occlusion;
                pendingOcclusion = null;
            }
        }

        private static void OnSceneChanged(Scene from, Scene to) => Apply();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            pendingSmallMesh = null;
            pendingOcclusion = null;
            SceneManager.activeSceneChanged -= OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        // The title is the first scene: the cut-off this player's tier had last time goes on before any map is loaded.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (PlayerPrefs.HasKey(SmallMeshKey)) pendingSmallMesh = PlayerPrefs.GetFloat(SmallMeshKey);
            Apply();
        }

#if UNITY_EDITOR
        // The pipeline is an asset: in the Editor a change made in Play would stay in it (and show as a changed file).
        // The values it had before the first change go back when Play stops.
        private static float? authoredSmallMesh;
        private static bool? authoredOcclusion;

        [UnityEditor.InitializeOnLoadMethod]
        private static void PutBackWhenPlayStops() => UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                if (authoredSmallMesh is float small) urp.smallMeshScreenPercentage = small;
                if (authoredOcclusion is bool occlusion) urp.gpuResidentDrawerEnableOcclusionCullingInCameras = occlusion;
            }
            authoredSmallMesh = null;
            authoredOcclusion = null;
        };
#endif
    }
}
