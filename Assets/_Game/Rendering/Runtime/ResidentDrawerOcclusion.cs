using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The GPU Resident Drawer's own occlusion culling (things hidden behind others are not drawn), switched for a test
    /// (<c>gm.batching occlusion on|off</c>). Off as authored since 10 Oct 2026, because the camera looks down; the
    /// batching pass measures it again in the streets. The pipeline asset is shared project data, so the value it had
    /// is put back when the session ends.
    /// </summary>
    public static class ResidentDrawerOcclusion
    {
        private static bool? authored;

        public static bool On
        {
            get => GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.gpuResidentDrawerEnableOcclusionCullingInCameras;
            set
            {
                if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
                if (authored == null) authored = urp.gpuResidentDrawerEnableOcclusionCullingInCameras;
                PipelineRebuilds.SetOcclusion(value); // a rebuild: waits for a menu when a map is loaded
            }
        }

        /// <summary>The asset's own value back. The session's end calls it.</summary>
        public static void Restore()
        {
            if (authored != null) PipelineRebuilds.SetOcclusion(authored.Value);
            authored = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => authored = null;
    }
}
