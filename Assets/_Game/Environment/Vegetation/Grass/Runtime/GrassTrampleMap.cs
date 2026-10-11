using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// The trample map: a small picture of the ground in front of the game camera that remembers where grass has been
    /// pushed, kept on the GPU (GrassTrample.compute). The blade shader reads it instead of going through a list
    /// of characters for every vertex, so any number of characters bend grass and it stays pressed for a moment
    /// where someone walked. Who presses it is whatever publishes <c>_KN_TrampleSources</c> (the world's foliage
    /// interactors): this assembly knows no gameplay. Presentation only, per machine.
    /// </summary>
    public sealed class GrassTrampleMap
    {
        private static readonly int BeforeId = Shader.PropertyToID("_Before");
        private static readonly int AfterId = Shader.PropertyToID("_After");
        private static readonly int ShiftId = Shader.PropertyToID("_Shift");
        private static readonly int MapId = Shader.PropertyToID("_Map");
        private static readonly int SpringId = Shader.PropertyToID("_Spring");
        private static readonly int SourcesId = Shader.PropertyToID("_KN_TrampleSources");
        private static readonly int SourceCountId = Shader.PropertyToID("_KN_TrampleSourceCount");
        /// <summary>The map, for the blade shader.</summary>
        public static readonly int TextureId = Shader.PropertyToID("_KN_GrassTrample");
        /// <summary>xy = world XZ of the map's corner, z = 1 / its width in metres, w = 1 while there is a map.</summary>
        public static readonly int RectId = Shader.PropertyToID("_KN_GrassTrampleRect");
        private const int MaxSources = 32; // GrassTrample.compute KN_MAX_TRAMPLERS

        private readonly List<Vector4> sources = new List<Vector4>(MaxSources);
        private readonly Vector4[] sent = new Vector4[MaxSources];
        private RenderTexture before, after;
        private int kernel = -1;
        private Vector2Int corner;      // the map's corner, in texels of the world
        private bool hasCorner;
        private int lastFrame = -1;

        /// <summary>
        /// Once a frame, for the game camera: moves the map to lie round <paramref name="centre"/>, lets the marks
        /// spring back and presses this frame's in. Does nothing more when called again in the same frame.
        /// </summary>
        public void Update(ComputeShader shader, Vector3 centre, float metres, int texels, float springSeconds)
        {
            if (shader == null || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            texels = Mathf.Clamp(Mathf.ClosestPowerOfTwo(texels), 64, 1024);
            metres = Mathf.Max(8f, metres);
            if (before == null || before.width != texels) Allocate(texels);
            if (kernel < 0) kernel = shader.FindKernel("Trample");

            float texel = metres / texels;
            // Whole texels of the world, so a mark never has to be resampled as the camera moves.
            var cornerNow = new Vector2Int(Mathf.FloorToInt((centre.x - metres * 0.5f) / texel), Mathf.FloorToInt((centre.z - metres * 0.5f) / texel));
            Vector2Int shift = hasCorner ? cornerNow - corner : new Vector2Int(texels, texels); // the first frame keeps nothing
            corner = cornerNow;
            hasCorner = true;

            sources.Clear();
            Shader.GetGlobalVectorArray(SourcesId, sources);
            int count = Mathf.Min(Mathf.Min(Shader.GetGlobalInt(SourceCountId), sources.Count), MaxSources);
            for (int i = 0; i < MaxSources; i++) sent[i] = i < count ? sources[i] : Vector4.zero;

            float spring = springSeconds > 0.01f ? Mathf.Exp(-Time.deltaTime / springSeconds) : 0f;
            shader.SetTexture(kernel, BeforeId, before);
            shader.SetTexture(kernel, AfterId, after);
            shader.SetInts(ShiftId, shift.x, shift.y);
            shader.SetVector(MapId, new Vector4(corner.x * texel, corner.y * texel, texel, texels));
            shader.SetVector(SpringId, new Vector4(spring, 0f, 0f, 0f));
            shader.SetVectorArray(SourcesId, sent);
            shader.SetInt(SourceCountId, count);
            int groups = Mathf.CeilToInt(texels / 8f);
            shader.Dispatch(kernel, groups, groups, 1);

            (before, after) = (after, before); // what was just written is what the next frame springs back from
            Shader.SetGlobalTexture(TextureId, before);
            Shader.SetGlobalVector(RectId, new Vector4(corner.x * texel, corner.y * texel, 1f / metres, 1f));
        }

        private void Allocate(int texels)
        {
            Release();
            before = Create(texels);
            after = Create(texels);
            hasCorner = false;
        }

        private static RenderTexture Create(int texels)
        {
            var texture = new RenderTexture(texels, texels, 0, GraphicsFormat.R16G16B16A16_SFloat)
            {
                name = "KN Grass Trample",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            return texture;
        }

        /// <summary>Lets go of the map: the blade shader goes back to asking the nearest characters directly.</summary>
        public void Release()
        {
            Shader.SetGlobalVector(RectId, Vector4.zero);
            if (before != null) { before.Release(); Object.DestroyImmediate(before); }
            if (after != null) { after.Release(); Object.DestroyImmediate(after); }
            before = after = null;
            hasCorner = false;
            lastFrame = -1;
        }
    }
}
