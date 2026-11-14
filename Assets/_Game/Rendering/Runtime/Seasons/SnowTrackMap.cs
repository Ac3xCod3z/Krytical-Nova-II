using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// The snow track map (owner's DAY list item 21): footprints and trails in the season's snow. A small R16 picture of the ground round
    /// the game camera (SnowTrack.compute), pressed by the same <c>_KN_TrampleSources</c> that bend the grass, filling back in over
    /// minutes. The ground and grass shaders read one texel of it (<c>KNSeason.hlsl</c>). Nothing new is networked: remote characters
    /// already move on every client. Presentation only, per machine; it knows no gameplay. Released when there is no snow.
    /// </summary>
    public sealed class SnowTrackMap
    {
        static readonly int BeforeId = Shader.PropertyToID("_Before");
        static readonly int AfterId = Shader.PropertyToID("_After");
        static readonly int ShiftId = Shader.PropertyToID("_Shift");
        static readonly int MapId = Shader.PropertyToID("_Map");
        static readonly int FillId = Shader.PropertyToID("_Fill");
        static readonly int SourcesId = Shader.PropertyToID("_KN_TrampleSources");
        static readonly int SourceCountId = Shader.PropertyToID("_KN_TrampleSourceCount");
        /// <summary>The map, for the ground and grass shaders.</summary>
        public static readonly int TextureId = Shader.PropertyToID("_KN_SnowTrack");
        /// <summary>xy = world XZ of the map's corner, z = 1 / its width in metres, w = 1 while there is a map.</summary>
        public static readonly int RectId = Shader.PropertyToID("_KN_SnowTrackRect");
        const int MaxSources = 32; // SnowTrack.compute KN_MAX_TRAMPLERS

        readonly List<Vector4> m_Sources = new List<Vector4>(MaxSources);
        readonly Vector4[] m_Sent = new Vector4[MaxSources];
        readonly int[] m_Shift = new int[2];
        RenderTexture m_Before, m_After;
        int m_Kernel = -1;
        Vector2Int m_Corner;
        bool m_HasCorner;
        int m_LastFrame = -1;

        /// <summary>True while the map exists.</summary>
        public bool Allocated => m_Before != null;

        /// <summary>
        /// Once a frame: moves the map round <paramref name="centre"/>, lets old marks fill in (<paramref name="fillSeconds"/> to about 5 %)
        /// and presses this frame's feet in. <paramref name="footScale"/> scales each trampler's radius (a footprint is narrower than a
        /// grass push).
        /// </summary>
        public void Update(ComputeShader shader, Vector3 centre, float metres, int texels, float fillSeconds, float footScale)
        {
            if (shader == null || Time.frameCount == m_LastFrame) return;
            m_LastFrame = Time.frameCount;
            texels = Mathf.Clamp(Mathf.ClosestPowerOfTwo(texels), 64, 1024);
            metres = Mathf.Max(8f, metres);
            if (m_Before == null || m_Before.width != texels) Allocate(texels);
            if (m_Kernel < 0) m_Kernel = shader.FindKernel("Track");

            float texel = metres / texels;
            var cornerNow = new Vector2Int(Mathf.FloorToInt((centre.x - metres * 0.5f) / texel), Mathf.FloorToInt((centre.z - metres * 0.5f) / texel));
            Vector2Int shift = m_HasCorner ? cornerNow - m_Corner : new Vector2Int(texels, texels);
            m_Corner = cornerNow;
            m_HasCorner = true;

            m_Sources.Clear();
            Shader.GetGlobalVectorArray(SourcesId, m_Sources);
            int count = Mathf.Min(Mathf.Min(Shader.GetGlobalInt(SourceCountId), m_Sources.Count), MaxSources);
            for (int i = 0; i < MaxSources; i++) m_Sent[i] = i < count ? m_Sources[i] : Vector4.zero;

            // e^-3 ~ 5 % left after fillSeconds
            float keep = fillSeconds > 0.01f ? Mathf.Exp(-3f * Time.deltaTime / fillSeconds) : 0f;
            shader.SetTexture(m_Kernel, BeforeId, m_Before);
            shader.SetTexture(m_Kernel, AfterId, m_After);
            m_Shift[0] = shift.x; m_Shift[1] = shift.y;
            shader.SetInts(ShiftId, m_Shift);
            shader.SetVector(MapId, new Vector4(m_Corner.x * texel, m_Corner.y * texel, texel, texels));
            shader.SetVector(FillId, new Vector4(keep, footScale, 0f, 0f));
            shader.SetVectorArray(SourcesId, m_Sent);
            shader.SetInt(SourceCountId, count);
            int groups = Mathf.CeilToInt(texels / 8f);
            shader.Dispatch(m_Kernel, groups, groups, 1);

            (m_Before, m_After) = (m_After, m_Before);
            Shader.SetGlobalTexture(TextureId, m_Before);
            Shader.SetGlobalVector(RectId, new Vector4(m_Corner.x * texel, m_Corner.y * texel, 1f / metres, 1f));
        }

        void Allocate(int texels)
        {
            Release();
            m_Before = Create(texels);
            m_After = Create(texels);
            m_HasCorner = false;
        }

        static RenderTexture Create(int texels)
        {
            var texture = new RenderTexture(texels, texels, 0, GraphicsFormat.R16_SFloat)
            {
                name = "KN Snow Tracks",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            return texture;
        }

        static void Kill(RenderTexture texture)
        {
            if (texture == null) return;
            texture.Release();
            if (Application.isPlaying) Object.Destroy(texture); else Object.DestroyImmediate(texture);
        }

        /// <summary>Lets go of the map: the shaders see no tracks.</summary>
        public void Release()
        {
            Shader.SetGlobalVector(RectId, Vector4.zero);
            Kill(m_Before);
            Kill(m_After);
            m_Before = m_After = null;
            m_HasCorner = false;
            m_LastFrame = -1;
        }
    }
}
