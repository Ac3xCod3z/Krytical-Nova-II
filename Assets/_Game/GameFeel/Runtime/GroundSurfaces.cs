using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>What the ground is made of, as far as kicked-up dust is concerned.</summary>
    public enum GroundSurface { Paving, Road, Grass, Dirt, Rock }

    /// <summary>The colour of the dust each kind of ground gives off. Tuned on the GameFeel prefab.</summary>
    [Serializable]
    public struct DustPalette
    {
        [Tooltip("Sidewalks, plaza tiles, town stone: pale concrete dust.")]
        public Color paving;
        [Tooltip("Roads and streets: darker asphalt grit.")]
        public Color road;
        [Tooltip("Grass: the dry earth under it, never green.")]
        public Color grass;
        [Tooltip("Bare dirt: a deeper brown than the dust off grass.")]
        public Color dirt;
        public Color rock;

        public static DustPalette Default => new DustPalette
        {
            paving = new Color(0.90f, 0.88f, 0.84f),
            road = new Color(0.48f, 0.49f, 0.54f),
            grass = new Color(0.82f, 0.74f, 0.56f),
            dirt = new Color(0.56f, 0.37f, 0.23f),
            rock = new Color(0.72f, 0.72f, 0.70f),
        };

        public Color For(GroundSurface surface)
        {
            switch (surface)
            {
                case GroundSurface.Road: return road;
                case GroundSurface.Grass: return grass;
                case GroundSurface.Dirt: return dirt;
                case GroundSurface.Rock: return rock;
                default: return paving;
            }
        }
    }

    /// <summary>
    /// Tells what the visible ground under a point is, so run, jump and landing dust can take its colour. The terrain
    /// answers with its dominant painted layer; everything else (the town's road and paving meshes) by its name and
    /// material. Results are cached, so a running character costs one small terrain read per metre at most.
    /// </summary>
    public static class GroundSurfaces
    {
        public static DustPalette Palette = DustPalette.Default;

        private static readonly Dictionary<Collider, GroundSurface> ByMesh = new Dictionary<Collider, GroundSurface>();
        private static readonly Dictionary<TerrainLayer, GroundSurface> ByLayer = new Dictionary<TerrainLayer, GroundSurface>();
        private static TerrainData lastData;
        private static int lastX = -1, lastZ = -1;
        private static GroundSurface lastSurface;

        // Play Mode starts without a domain reload: colliders cached in the last run are gone.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Palette = DustPalette.Default;
            ByMesh.Clear();
            ByLayer.Clear();
            lastData = null;
            lastX = lastZ = -1;
        }

        public static Color DustAt(Vector3 point, Transform ignoreRoot = null) => Palette.For(At(point, ignoreRoot));

        public static GroundSurface At(Vector3 point, Transform ignoreRoot = null)
        {
            if (!GroundSnap.TryGetGround(point, out RaycastHit ground, ignoreRoot)) return GroundSurface.Paving;
            Collider collider = ground.collider;
            if (collider is TerrainCollider) return OnTerrain(collider.GetComponent<Terrain>(), ground.point);
            if (!ByMesh.TryGetValue(collider, out GroundSurface surface))
            {
                var renderer = collider.GetComponent<Renderer>();
                surface = Classify(collider.name) ?? Classify(renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : null)
                    ?? GroundSurface.Paving;
                ByMesh[collider] = surface;
            }
            return surface;
        }

        /// <summary>Gives every particle system of an effect instance the dust colour (newly emitted particles take it).</summary>
        public static void Tint(ParticleSystem[] systems, Color dust)
        {
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                var main = systems[i].main;
                main.startColor = dust;
            }
        }

        private static GroundSurface OnTerrain(Terrain terrain, Vector3 point)
        {
            if (terrain == null || terrain.terrainData == null) return GroundSurface.Paving;
            TerrainData data = terrain.terrainData;
            Vector3 local = point - terrain.transform.position;
            int x = Mathf.Clamp((int)(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp((int)(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            if (data == lastData && x == lastX && z == lastZ) return lastSurface;

            float[,,] weights = data.GetAlphamaps(x, z, 1, 1);
            TerrainLayer[] layers = data.terrainLayers;
            int dominant = 0;
            for (int i = 1; i < layers.Length && i < weights.GetLength(2); i++)
                if (weights[0, 0, i] > weights[0, 0, dominant]) dominant = i;

            GroundSurface surface = GroundSurface.Paving;
            TerrainLayer layer = dominant < layers.Length ? layers[dominant] : null;
            if (layer != null && !ByLayer.TryGetValue(layer, out surface))
            {
                surface = Classify(layer.name) ?? GroundSurface.Paving;
                ByLayer[layer] = surface;
            }
            lastData = data;
            lastX = x;
            lastZ = z;
            lastSurface = surface;
            return surface;
        }

        private static GroundSurface? Classify(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Has(name, "Road") || Has(name, "Asphalt") || Has(name, "Street")) return GroundSurface.Road;
            if (Has(name, "Grass") || Has(name, "Lawn")) return GroundSurface.Grass;
            if (Has(name, "Dirt") || Has(name, "Mud") || Has(name, "Soil") || Has(name, "Sand")) return GroundSurface.Dirt;
            if (Has(name, "Rock") || Has(name, "Cliff")) return GroundSurface.Rock;
            return null;
        }

        private static bool Has(string text, string word) => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
