using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// Where grass grows (baked into the grass mask in the editor) and how much of it is drawn (runtime LOD).
    /// Final density = biome density × ground × slope × altitude × road/building exclusion × artist mask
    /// × runtime exclusion volumes × distance LOD.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Grass/Grass Rule Set")]
    public sealed class GrassRuleSet : ScriptableObject
    {
        public const int MaxBiomes = 4;

        [Header("Biomes (mask G channel picks one)")]
        [Tooltip("Index 0 = default (wild) biome. Others are assigned by region rules at bake time.")]
        public List<GrassBiome> biomes = new List<GrassBiome>();
        [Tooltip("Inside this half size around the zone origin (the walled city) the lawn biome is used.")]
        public float lawnRegionHalfSize = 112f;
        [Tooltip("Biome index used inside the lawn region.")]
        public int lawnBiome = 1;

        [Header("Placement rules (baked)")]
        [Tooltip("Terrain splat layer index that counts as grass ground.")]
        public int grassSplatLayer = 0;
        [Tooltip("Splat grass weight → density. Below x nothing grows, above y full density.")]
        public Vector2 groundWeightRange = new Vector2(0.45f, 0.85f);
        [Tooltip("Density by slope in degrees. Grass thins on hills and never grows on cliffs.")]
        public AnimationCurve slopeDensity = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(20f, 1f), new Keyframe(28f, 0.85f), new Keyframe(36f, 0.45f),
            new Keyframe(45f, 0.08f), new Keyframe(52f, 0f));
        [Tooltip("Density by height above the waterline (m). Thins near water's edge and high on the rim.")]
        public AnimationCurve altitudeDensity = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.4f, 0.6f), new Keyframe(1.2f, 1f), new Keyframe(25f, 1f), new Keyframe(45f, 0.35f));
        [Tooltip("Metres over which grass fades out next to roads, sidewalks, buildings and props (no hard edges).")]
        [Min(0f)] public float meshEdgeFalloff = 1.5f;
        [Tooltip("Lawn biome: grass runs right up to paving (tight edge) and fills the whole lawn splat.")]
        [Min(0f)] public float lawnEdgeFalloff = 0.2f;
        public Vector2 lawnGroundWeightRange = new Vector2(0.12f, 0.35f);
        [Tooltip("Metres of bare ground kept around blocking meshes before the falloff starts.")]
        [Min(0f)] public float meshMargin = 0.15f;
        [Tooltip("Metres from water over which the ground counts as wet (lusher tint, taller grass).")]
        [Min(1f)] public float moistureDistance = 22f;
        [Tooltip("Optional painted designer mask over the terrain (R multiplies density).")]
        public Texture2D artistMask;
        public int maskResolution = 2048;

        [Header("Baked mask (R density, G biome, B moisture)")]
        public Texture2D mask;

        [Header("Rendering")]
        [Tooltip("Grid spacing of candidate positions (m). 0.25 = up to 16 clumps per m².")]
        [Range(0.15f, 1f)] public float cellSize = 0.25f;
        [Tooltip("Metres from the camera where real grass ends (far tufts take over; see Far grass).")]
        [Range(20f, 150f)] public float maxDistance = 60f;
        [Tooltip("Full density out to x m; y m is the middle band.")]
        public Vector2 lodDistances = new Vector2(15f, 35f);
        [Tooltip("Share of grass kept in the middle and far bands. Kept clumps widen to fill the gaps.")]
        public Vector2 lodKeep = new Vector2(0.45f, 0.15f);
        [Tooltip("Global density multiplier (quality setting).")]
        [Range(0f, 2f)] public float densityScale = 1f;
        [Tooltip("Max real clumps per grass type per camera per frame.")]
        public int capacityPerType = 64000;

        [Header("Far grass (smoke and mirrors)")]
        [Tooltip("Cheap tufts that carry the field past the real grass, following the same mask, biomes and types.")]
        public bool farGrass = true;
        [Tooltip("Low-poly tuft drawn for every type in the far band (root at origin, Y up).")]
        public Mesh farMesh;
        [Tooltip("Real grass fades out and far tufts fade in between these camera distances (m). y also ends the real grass.")]
        public Vector2 handOffBand = new Vector2(42f, 60f);
        [Tooltip("Metres from the camera where far tufts end.")]
        [Range(60f, 400f)] public float farDistance = 170f;
        [Tooltip("Grid spacing of far tufts (m).")]
        [Range(0.5f, 3f)] public float farCellSize = 0.9f;
        [Tooltip("Far tufts are this much wider than the clump they stand in for.")]
        [Range(0.5f, 4f)] public float farWidthScale = 1.7f;
        [Tooltip("Share of far tufts kept at the far edge.")]
        [Range(0f, 1f)] public float farKeepAtEnd = 0.45f;
        [Tooltip("Max far tufts per grass type per camera per frame.")]
        public int farCapacityPerType = 48000;
    }
}
