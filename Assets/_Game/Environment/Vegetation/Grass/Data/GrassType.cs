using UnityEngine;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// How one kind of grass looks and moves: its mesh, size range, root→tip colour gradient, fake AO, wind and
    /// player response. Where it grows is decided by <see cref="GrassBiome"/> and the baked grass mask.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Grass/Grass Type")]
    public sealed class GrassType : ScriptableObject
    {
        [Header("Shape")]
        [Tooltip("Clump mesh, Y-up with its root at the origin. Any mesh works (procedural blade clumps or Synty grass); " +
                 "the shader colours it by height, so its UVs and material are ignored.")]
        public Mesh mesh;
        [Tooltip("Final clump height in metres. Each instance picks a height in this range.")]
        public Vector2 heightRange = new Vector2(0.3f, 0.6f);
        [Tooltip("Width multiplier relative to the height-scaled mesh.")]
        public Vector2 widthRange = new Vector2(0.85f, 1.15f);
        [Tooltip("Share of grid candidates this type keeps where it is picked (0-1). Lower = sparser.")]
        [Range(0f, 1f)] public float density = 0.5f;

        [Header("Colour (root → tip)")]
        public Color rootColor = new Color(0.1f, 0.21f, 0.09f);
        public Color midColor = new Color(0.27f, 0.44f, 0.17f);
        public Color tipColor = new Color(0.56f, 0.66f, 0.31f);
        [Tooltip("Height (0-1) where the mid colour sits.")]
        [Range(0.05f, 0.95f)] public float midPoint = 0.45f;
        [Tooltip("Per-clump brightness spread (0.1 = ±5%).")]
        [Range(0f, 0.4f)] public float brightnessVariation = 0.1f;
        [Tooltip("Broad field patches toward this hue (a = strength).")]
        public Color patchTint = new Color(0.92f, 0.95f, 0.62f, 0.25f);
        [Tooltip("Lusher tint near water (a = strength), from the mask's moisture channel.")]
        public Color wetTint = new Color(0.75f, 1f, 0.82f, 0.35f);
        [Tooltip("Fake ambient occlusion: brightness at the root (1 = none).")]
        [Range(0f, 1f)] public float rootOcclusion = 0.55f;

        [Header("Motion")]
        [Range(0f, 2f)] public float windStrength = 1f;
        [Range(0f, 1f)] public float flutter = 0.35f;
        [Range(0f, 2f)] public float playerBend = 1f;
        [Tooltip("Stylized lighting: 0 = real blade normals, 1 = all light as if facing up (even, no dark blades).")]
        [Range(0f, 1f)] public float normalUp = 0.8f;

        public float MeshHeight => mesh != null ? Mathf.Max(mesh.bounds.max.y, 0.01f) : 1f;
    }
}
