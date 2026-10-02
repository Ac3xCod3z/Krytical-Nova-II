using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Inputs for the editor terrain generator (Krytical Nova → Zone). World origin is the town centre;
    /// +Z is map north. All heights are world Y metres; the town plateau sits at <see cref="townHeight"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Zone Terrain Profile")]
    public sealed class ZoneTerrainProfile : ScriptableObject
    {
        [Serializable]
        public sealed class Polyline
        {
            public string name = "";
            public List<Vector2> points = new List<Vector2>();
        }

        [Serializable]
        public sealed class DetailLayer
        {
            [Tooltip("Synty foliage prefab name (a wind-reactive variant is generated from it).")]
            public string prefab = "SM_Gen_Env_Grass_01";
            [Tooltip("Smallest and largest uniform scale. Each instance picks a size in between.")]
            public float minScale = 0.5f;
            public float maxScale = 1f;
            [Tooltip("Optional separate height range (0 = same as scale), so blade heights vary more than their width.")]
            public float minHeight = 0f;
            public float maxHeight = 0f;
            [Tooltip("Lawn layers grow only on the mowed town lawns (inside the walls); others only outside them.")]
            public bool lawn = false;
            [Tooltip("Average instances per detail cell (~1 m²) where this layer is fully on.")]
            [Range(0f, 8f)] public float density = 1f;
            [Tooltip("Perlin frequency of the patches this layer grows in (bigger = smaller patches).")]
            public float patchScale = 0.04f;
            [Tooltip("Perlin value above which the layer appears (0 = everywhere, 0.6 = sparse patches).")]
            [Range(0f, 1f)] public float patchThreshold = 0f;
            [Tooltip("Unity detail noise spread: clumps instance sizes so big and small grow in groups.")]
            public float noiseSpread = 0.3f;
        }

        [Serializable]
        public struct Lake
        {
            public Vector2 center;
            [Min(1f)] public float radius;
        }

        [Header("Size")]
        [Min(64f)] public float size = 600f;
        [Tooltip("World Y of the terrain's lowest possible point.")]
        public float baseHeight = -20f;
        [Min(10f)] public float heightRange = 80f;
        public int heightmapResolution = 513;
        public int alphamapResolution = 512;
        public int seed = 1337;

        [Header("Rolling hills")]
        public float hillAmplitude = 7f;
        public float hillScale = 0.012f;
        [Tooltip("Hills fade in between these distances from the town centre.")]
        public Vector2 hillFadeDistance = new Vector2(85f, 170f);

        [Header("Mountain rim")]
        public float rimWidth = 50f;
        public float rimHeight = 42f;

        [Header("Town plateau")]
        public float townHeight = 0f;
        [Tooltip("Half size of the flat square under the town.")]
        public float townHalfSize = 62f;
        public float townFalloff = 25f;
        [Tooltip("Half size of the paved (town stone) square: the walled city.")]
        public float pavedHalfSize = 54f;
        [Tooltip("Half size of the ruined outskirts band around the walls, painted as dirt and rubble. 0 = none.")]
        public float ruinsHalfSize = 0f;
        [Tooltip("Half size of the central lawn square painted grass inside the paving (the plaza). 0 = none.")]
        public float greenHalfSize = 0f;

        [Header("Water")]
        public float waterLevel = -1.2f;
        public float riverWidth = 14f;
        public float riverBankWidth = 7f;
        public float riverBedHeight = -4f;
        [Tooltip("Current speed (m/s-ish, shader units) of the river surface. Each river flows from its first point to its last.")]
        public float riverFlowSpeed = 1f;
        public List<Polyline> rivers = new List<Polyline>();
        public float lakeBedHeight = -5f;
        public float lakeShoreWidth = 12f;
        public List<Lake> lakes = new List<Lake>();

        [Header("Paths")]
        public float pathWidth = 8f;
        public float pathShoulder = 8f;
        public float pathHeight = 0.2f;
        public List<Polyline> paths = new List<Polyline>();

        [Header("Portal pad")]
        public Vector2 portalCenter = new Vector2(215f, 232f);
        public float portalPadRadius = 14f;
        public float portalPadHeight = 0.4f;

        [Header("Scatter")]
        public int treeCount = 450;
        public int rockCount = 140;
        public int grassCount = 700;
        [Tooltip("Trees and props stay this far from paths and the town.")]
        public float scatterClearance = 6f;
        [Range(0f, 60f)] public float maxScatterSlope = 32f;

        [Header("Grass & flowers (terrain details)")]
        [Tooltip("Detail cells across the terrain (1024 over 1 km ≈ 1 m cells).")]
        public int detailResolution = 1024;
        public int detailResolutionPerPatch = 32;
        [Tooltip("Metres from the camera that grass draws.")]
        public float detailDistance = 70f;
        [Tooltip("Splat grass weight required for details to grow (keeps them off dirt, rock and town stone).")]
        [Range(0f, 1f)] public float detailGrassThreshold = 0.75f;
        [Tooltip("Metres of bare ground kept around road / sidewalk / building meshes.")]
        public float detailMeshMargin = 0.4f;
        public List<DetailLayer> detailLayers = new List<DetailLayer>();

        [Header("Splat")]
        [Range(0f, 90f)] public float rockSlopeStart = 30f;
        [Range(0f, 90f)] public float rockSlopeFull = 42f;
    }
}
