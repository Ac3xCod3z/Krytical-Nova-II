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

        [Header("Water")]
        public float waterLevel = -1.2f;
        public float riverWidth = 14f;
        public float riverBankWidth = 7f;
        public float riverBedHeight = -4f;
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

        [Header("Splat")]
        [Range(0f, 90f)] public float rockSlopeStart = 30f;
        [Range(0f, 90f)] public float rockSlopeFull = 42f;
    }
}
