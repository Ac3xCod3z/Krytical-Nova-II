using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace KryticalNova.Rendering
{
    /// <summary>What a shape in the shelter map does to the snow round it (owner, 14 Nov 2026).</summary>
    public enum SnowShelterKind : byte
    {
        /// <summary>A tree's canopy: thinner snow under it.</summary>
        Canopy = 0,
        /// <summary>A wall, building or rock: snow piles against its windward side.</summary>
        Wall = 1,
        /// <summary>A road or path: trodden, thin, slushy snow.</summary>
        Road = 2,
    }

    /// <summary>
    /// The shelter map (owner, 14 Nov 2026: drifts, not a flat sheet; thinner under trees; piles against walls; thinner, trodden roads):
    /// an RGBA8 picture of the ground round the camera, seen from above, rebuilt only when the camera has moved far, the wind has turned or
    /// the shapes changed (SnowTrack.compute, kernel Shelter). r = under a canopy, g = a windward pile, b = a road. The ground and grass
    /// read one texel (<c>KNSeason.hlsl</c> <c>KNSnowShelter</c>). Shapes are boxes from the renderers' bounds, gathered by the season
    /// presenter. Presentation only, per machine; released with no snow and off on Low.
    /// </summary>
    public sealed class SnowShelterMap
    {
        public const int MaxShapes = 512;
        static readonly int AfterId = Shader.PropertyToID("_Shelter");
        static readonly int MapId = Shader.PropertyToID("_Map");
        static readonly int ShapesId = Shader.PropertyToID("_Shapes");
        static readonly int KindsId = Shader.PropertyToID("_ShapeKinds");
        static readonly int CountId = Shader.PropertyToID("_ShapeCount");
        static readonly int WindId = Shader.PropertyToID("_ShelterWind");
        /// <summary>The map and its rectangle (xy corner, z 1 / width, w 1 while there is one), for the shaders.</summary>
        public static readonly int TextureId = Shader.PropertyToID("_KN_SnowShelter");
        public static readonly int RectId = Shader.PropertyToID("_KN_SnowShelterRect");

        readonly Vector4[] m_Shapes = new Vector4[MaxShapes];
        readonly Vector4[] m_Kinds = new Vector4[MaxShapes];
        RenderTexture m_Map;
        int m_Kernel = -1, m_Count;

        public bool Allocated => m_Map != null;

        /// <summary>Starts a new set of shapes (then <see cref="Add"/> each, then <see cref="Build"/>).</summary>
        public void Begin() => m_Count = 0;

        /// <summary>One box on the ground (world XZ centre and half size), of a kind, with a strength 0-1.</summary>
        public bool Add(Vector2 centre, Vector2 halfSize, SnowShelterKind kind, float strength = 1f)
        {
            if (m_Count >= MaxShapes) return false;
            m_Shapes[m_Count] = new Vector4(centre.x, centre.y, Mathf.Max(0.05f, halfSize.x), Mathf.Max(0.05f, halfSize.y));
            m_Kinds[m_Count] = new Vector4((float)kind, Mathf.Clamp01(strength), 0f, 0f);
            m_Count++;
            return true;
        }

        /// <summary>
        /// Draws the shapes into a map of <paramref name="metres"/> round <paramref name="centre"/>; <paramref name="windTo"/> is the
        /// direction the wind blows toward (world XZ), <paramref name="pileReach"/> how far a pile reaches from its wall (m).
        /// </summary>
        public void Build(ComputeShader shader, Vector3 centre, float metres, int texels, Vector2 windTo, float pileReach)
        {
            if (shader == null || texels <= 0) { Release(); return; }
            texels = Mathf.Clamp(Mathf.ClosestPowerOfTwo(texels), 64, 1024);
            if (m_Map == null || m_Map.width != texels) Allocate(texels);
            if (m_Kernel < 0) m_Kernel = shader.FindKernel("Shelter");
            float texel = metres / texels;
            Vector2 corner = new Vector2(centre.x - metres * 0.5f, centre.z - metres * 0.5f);
            Vector2 wind = windTo.sqrMagnitude > 1e-4f ? windTo.normalized : new Vector2(0.5f, 0.866f);
            shader.SetTexture(m_Kernel, AfterId, m_Map);
            shader.SetVector(MapId, new Vector4(corner.x, corner.y, texel, texels));
            shader.SetVectorArray(ShapesId, m_Shapes);
            shader.SetVectorArray(KindsId, m_Kinds);
            shader.SetInt(CountId, m_Count);
            shader.SetVector(WindId, new Vector4(wind.x, wind.y, Mathf.Max(0.1f, pileReach), 0f));
            int groups = Mathf.CeilToInt(texels / 8f);
            shader.Dispatch(m_Kernel, groups, groups, 1);
            Shader.SetGlobalTexture(TextureId, m_Map);
            Shader.SetGlobalVector(RectId, new Vector4(corner.x, corner.y, 1f / metres, 1f));
        }

        void Allocate(int texels)
        {
            Release();
            m_Map = new RenderTexture(texels, texels, 0, GraphicsFormat.R8G8B8A8_UNorm)
            {
                name = "KN Snow Shelter",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            m_Map.Create();
        }

        public void Release()
        {
            if (m_Map != null)
            {
                m_Map.Release();
                Object.Destroy(m_Map);
                m_Map = null;
            }
            Shader.SetGlobalVector(RectId, Vector4.zero);
        }
    }
}
