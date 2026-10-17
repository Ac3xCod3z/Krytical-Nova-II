using System;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>What kind of thing a mesh in the world is, for how its detail falls away with distance.</summary>
    public enum LodCategory : byte
    {
        Tree,
        Bush,
        Rock,
        Building,
        Prop,
        SmallProp,
    }

    /// <summary>
    /// Level of detail for every mesh drawn at a distance (owner, 17 Oct 2026: "no matter what we need LOD", "for all
    /// meshes at a distance"). The Synty packs ship almost no LODs, so <c>EnvironmentLodBuilder</c> makes them: two
    /// lighter copies of every world mesh and a distance past which it is not drawn, by kind of thing. The numbers
    /// here say where each switch happens; the quality tier then moves all of them nearer or farther
    /// (<see cref="qualityBias"/>, written into the project's Quality levels by the builder). Characters are not
    /// remeshed: past set distances they skin with fewer bones, cast no shadow, animate only when seen, and are not
    /// drawn at all (<see cref="CharacterLod"/>). See Design/Rendering-Bible.md, "Level of detail".
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Rendering/World LOD Settings", fileName = "WorldLodSettings")]
    public sealed class WorldLodSettings : ScriptableObject
    {
        [Serializable]
        public struct Band
        {
            public LodCategory category;
            [Tooltip("Share of the screen's height the thing fills when its first lighter copy takes over (LODGroup threshold).")]
            [Range(0.005f, 1f)] public float lighterFrom;
            [Tooltip("...when its lightest copy takes over.")]
            [Range(0.002f, 1f)] public float lightestFrom;
            [Tooltip("...below which it is not drawn at all.")]
            [Range(0.0005f, 0.5f)] public float hiddenBelow;
            [Tooltip("Share of its triangles the lighter copy keeps.")]
            [Range(0.05f, 1f)] public float lighterKeeps;
            [Tooltip("Share of its triangles the lightest copy keeps.")]
            [Range(0.02f, 1f)] public float lightestKeeps;
            [Tooltip("The lightest copy casts no shadow.")]
            public bool lightestCastsNoShadow;
        }

        [Serializable]
        public struct QualityBias
        {
            [Tooltip("The Quality level's name (Project Settings ▸ Quality).")]
            public string level;
            [Tooltip("Above 1 keeps detail farther away; below 1 brings the switches nearer.")]
            [Range(0.25f, 4f)] public float lodBias;
        }

        [Header("World meshes, by kind")]
        public Band[] bands =
        {
            new Band { category = LodCategory.Tree, lighterFrom = 0.25f, lightestFrom = 0.08f, hiddenBelow = 0.01f, lighterKeeps = 0.5f, lightestKeeps = 0.15f, lightestCastsNoShadow = true },
            new Band { category = LodCategory.Bush, lighterFrom = 0.2f, lightestFrom = 0.06f, hiddenBelow = 0.015f, lighterKeeps = 0.5f, lightestKeeps = 0.2f, lightestCastsNoShadow = true },
            new Band { category = LodCategory.Rock, lighterFrom = 0.2f, lightestFrom = 0.06f, hiddenBelow = 0.008f, lighterKeeps = 0.5f, lightestKeeps = 0.2f, lightestCastsNoShadow = true },
            new Band { category = LodCategory.Building, lighterFrom = 0.35f, lightestFrom = 0.12f, hiddenBelow = 0.004f, lighterKeeps = 0.6f, lightestKeeps = 0.25f, lightestCastsNoShadow = false },
            new Band { category = LodCategory.Prop, lighterFrom = 0.15f, lightestFrom = 0.05f, hiddenBelow = 0.012f, lighterKeeps = 0.5f, lightestKeeps = 0.2f, lightestCastsNoShadow = true },
            new Band { category = LodCategory.SmallProp, lighterFrom = 0.1f, lightestFrom = 0.04f, hiddenBelow = 0.02f, lighterKeeps = 0.5f, lightestKeeps = 0.25f, lightestCastsNoShadow = true },
        };

        [Header("Sorting a mesh into a kind (by its name, then its size)")]
        [Tooltip("Things at least this tall or wide (metres) are buildings.")]
        [Min(1f)] public float buildingFromSize = 8f;
        [Tooltip("Things smaller than this (metres) are small props.")]
        [Min(0.1f)] public float smallPropBelowSize = 1f;
        [Tooltip("A mesh with fewer triangles than this gets no lighter copies, only the distance past which it is hidden.")]
        [Min(4)] public int minTrianglesToSimplify = 96;

        [Header("Quality tiers")]
        public QualityBias[] qualityBias =
        {
            new QualityBias { level = "Mobile", lodBias = 0.8f },
            new QualityBias { level = "PC", lodBias = 2f },
        };

        [Header("Characters (players, Hostiles, NPCs, pets)")]
        [Tooltip("Beyond this many metres from the camera a character skins with two bones a vertex.")]
        [Min(1f)] public float characterMidDistance = 25f;
        [Tooltip("Beyond this, with one bone, casts no shadow, and animates only while it is on screen.")]
        [Min(1f)] public float characterFarDistance = 60f;
        [Tooltip("Beyond this it is not drawn and not animated.")]
        [Min(1f)] public float characterHiddenDistance = 150f;
        [Tooltip("Seconds between two looks at how far a character is.")]
        [Range(0.05f, 2f)] public float characterRefresh = 0.4f;

        public bool TryGetBand(LodCategory category, out Band band)
        {
            foreach (Band each in bands)
                if (each.category == category) { band = each; return true; }
            band = default;
            return false;
        }

        /// <summary>The kind of thing a mesh is, from its name first and then its size.</summary>
        public LodCategory CategoryOf(string objectName, Vector3 size)
        {
            string n = objectName ?? string.Empty;
            if (n.Contains("Tree")) return LodCategory.Tree;
            if (n.Contains("Bush") || n.Contains("Fern") || n.Contains("Hedge") || n.Contains("Plant") || n.Contains("Shrub")) return LodCategory.Bush;
            if (n.Contains("Rock") || n.Contains("Cliff") || n.Contains("Boulder") || n.Contains("Stone")) return LodCategory.Rock;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest >= buildingFromSize || n.Contains("Bld") || n.Contains("Building")) return LodCategory.Building;
            return largest < smallPropBelowSize ? LodCategory.SmallProp : LodCategory.Prop;
        }

        /// <summary>
        /// How far away (metres) a thing <paramref name="height"/> metres tall stops filling <paramref name="screenShare"/>
        /// of a screen seen through <paramref name="verticalFov"/> degrees, with the quality tier's bias.
        /// </summary>
        public static float DistanceAt(float height, float screenShare, float verticalFov, float lodBias) =>
            height / (2f * Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(1e-4f, screenShare)) * lodBias;

        private void OnValidate()
        {
            for (int i = 0; i < bands.Length; i++)
            {
                bands[i].lightestFrom = Mathf.Min(bands[i].lightestFrom, bands[i].lighterFrom);
                bands[i].hiddenBelow = Mathf.Min(bands[i].hiddenBelow, bands[i].lightestFrom);
                bands[i].lightestKeeps = Mathf.Min(bands[i].lightestKeeps, bands[i].lighterKeeps);
            }
            characterFarDistance = Mathf.Max(characterFarDistance, characterMidDistance);
            characterHiddenDistance = Mathf.Max(characterHiddenDistance, characterFarDistance);
        }
    }
}
