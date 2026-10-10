using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Where the Ascension Novas in view are, for what is drawn over the frame afterwards and so cannot be hidden by
    /// a Nova's own depth: the glow halo. Each Nova reports itself every frame it is shown; the shaders get the list
    /// as <c>_KN_NovaDiscs</c> (xyz = its middle in the world, w = its radius in metres) and
    /// <c>_KN_NovaDiscCount</c>, and leave out what lies behind one. See Assets/_Game/_Docs/Design/Rendering-Bible.md.
    /// </summary>
    public static class NovaCull
    {
        /// <summary>The most Novas that hide the light behind them in one frame. Matches the shader's array.</summary>
        public const int MaxDiscs = 8;

        private static readonly int DiscsId = Shader.PropertyToID("_KN_NovaDiscs");
        private static readonly int CountId = Shader.PropertyToID("_KN_NovaDiscCount");
        private static readonly Vector4[] Discs = new Vector4[MaxDiscs];
        private static int count;
        private static int frame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Begin()
        {
            count = 0;
            frame = -1;
            RenderPipelineManager.beginContextRendering -= Push;
            RenderPipelineManager.beginContextRendering += Push;
        }

        /// <summary>A Nova that is shown this frame: its middle, and how far its furthest ring reaches (metres).</summary>
        public static void Report(Vector3 centre, float radius)
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                count = 0;
            }
            if (count < MaxDiscs && radius > 0f) Discs[count++] = new Vector4(centre.x, centre.y, centre.z, radius);
        }

        private static void Push(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (frame != Time.frameCount) count = 0; // none was shown this frame
            Shader.SetGlobalVectorArray(DiscsId, Discs);
            Shader.SetGlobalFloat(CountId, count);
        }
    }
}
