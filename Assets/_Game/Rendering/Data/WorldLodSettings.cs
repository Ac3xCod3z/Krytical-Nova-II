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
    /// meshes at a distance"). The Synty packs ship almost no LODs, so <c>EnvironmentLodBuilder</c> gives every world
    /// mesh lighter levels inside it (Unity 6 Mesh LOD, picked by its size on screen) and a distance past which it is
    /// not drawn, by kind of thing. The game's graphics preset then moves all of it nearer or farther
    /// (<see cref="tiers"/>). Characters are not remeshed: past set distances they skin with fewer bones, cast no
    /// shadow, animate only when seen, and are not drawn at all (<see cref="CharacterLod"/>). See
    /// Design/Rendering-Bible.md, "Level of detail".
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Rendering/World LOD Settings", fileName = "WorldLodSettings")]
    public sealed class WorldLodSettings : ScriptableObject
    {
        [Serializable]
        public struct Band
        {
            public LodCategory category;
            [Tooltip("Share of the screen's height below which it is not drawn at all (an LODGroup of one level). Its lighter levels in between are Mesh LOD's.")]
            [Range(0.0005f, 0.5f)] public float hiddenBelow;
        }

        /// <summary>How far detail reaches at one of the game's graphics presets (Settings ▸ Graphics).</summary>
        [Serializable]
        public struct Tier
        {
            [Tooltip("The preset it is for: Low, Medium, High, Ultra (Custom uses High).")]
            public string preset;
            [Tooltip("Unity's LOD bias: above 1 keeps detail farther away. Also how far character detail reaches.")]
            [Range(0.25f, 4f)] public float lodBias;
            [Tooltip("Mesh LOD threshold: higher switches to the lighter levels sooner. Measured 17 Oct 2026: from 4 up the far towers lose their lit windows; 2 still looks like 1.")]
            [Range(0.1f, 4f)] public float meshLodThreshold;
            [Tooltip("Things smaller than this share of the screen (percent) are not drawn (GPU Resident Drawer). 0 draws everything.")]
            [Range(0f, 5f)] public float smallMeshCullPercent;
            [Tooltip("World meshes smaller than this (metres, their largest side) cast no shadow (ShadowBands). 0 keeps every shadow.")]
            [Range(0f, 5f)] public float noShadowBelowSize;
        }

        [Header("World meshes, by kind")]
        public Band[] bands =
        {
            new Band { category = LodCategory.Tree, hiddenBelow = 0.01f },
            new Band { category = LodCategory.Bush, hiddenBelow = 0.015f },
            new Band { category = LodCategory.Rock, hiddenBelow = 0.008f },
            new Band { category = LodCategory.Building, hiddenBelow = 0.004f },
            new Band { category = LodCategory.Prop, hiddenBelow = 0.012f },
            new Band { category = LodCategory.SmallProp, hiddenBelow = 0.02f },
        };

        [Header("Sorting a mesh into a kind (by its name, then its size)")]
        [Tooltip("Things at least this tall or wide (metres) are buildings.")]
        [Min(1f)] public float buildingFromSize = 8f;
        [Tooltip("Things smaller than this (metres) are small props.")]
        [Min(0.1f)] public float smallPropBelowSize = 1f;
        [Tooltip("A mesh with fewer triangles than this gets no lighter levels, only the distance past which it is hidden.")]
        [Min(4)] public int minTrianglesToSimplify = 96;

        [Header("Graphics presets (the game's Low, Medium, High, Ultra)")]
        public Tier[] tiers =
        {
            new Tier { preset = "Low", lodBias = 0.7f, meshLodThreshold = 3f, smallMeshCullPercent = 0.6f, noShadowBelowSize = 2f },
            new Tier { preset = "Medium", lodBias = 1.2f, meshLodThreshold = 2f, smallMeshCullPercent = 0.35f, noShadowBelowSize = 1f },
            new Tier { preset = "High", lodBias = 2f, meshLodThreshold = 1.2f, smallMeshCullPercent = 0.15f },
            new Tier { preset = "Ultra", lodBias = 3f, meshLodThreshold = 0.8f, smallMeshCullPercent = 0f },
        };

        [Header("Mesh LOD (Unity 6: lighter levels made inside each mesh, picked by screen size)")]
        [Tooltip("Most levels a mesh is given. -1 lets Unity decide.")]
        [Range(-1, 8)] public int maxMeshLods = -1;

        [Header("Characters (players, Hostiles, NPCs, pets)")]
        [Tooltip("Beyond this many metres from the camera a character skins with two bones a vertex.")]
        [Min(1f)] public float characterMidDistance = 25f;
        [Tooltip("Beyond this, with one bone, casts no shadow, and animates only while it is on screen.")]
        [Min(1f)] public float characterFarDistance = 60f;
        [Tooltip("Beyond this it is not drawn and not animated.")]
        [Min(1f)] public float characterHiddenDistance = 150f;
        [Tooltip("Seconds between two looks at how far a character is.")]
        [Range(0.05f, 2f)] public float characterRefresh = 0.4f;

        // ------------------------------------------------------------------ the preset in force

        private static int wantedTier = 2; // High, what the game ships with
        private static WorldLodSettings active;

        /// <summary>The settings in use (given by the first character's LOD that has them).</summary>
        public static WorldLodSettings Active => active;

        /// <summary>The graphics preset asks for its tier (0 Low ... 3 Ultra); applied now, or once the settings are known.</summary>
        public static void SetTier(int tier)
        {
            wantedTier = Mathf.Clamp(tier, 0, 3);
            if (active != null) active.ApplyTier();
        }

        /// <summary>A character's LOD brings the settings in: the preset's tier is applied from them.</summary>
        public static void Use(WorldLodSettings settings)
        {
            if (settings == null || settings == active) return;
            active = settings;
            active.ApplyTier();
        }

        private void ApplyTier()
        {
            if (tiers == null || tiers.Length == 0) return;
            Tier tier = tiers[Mathf.Clamp(wantedTier, 0, tiers.Length - 1)];
            QualitySettings.lodBias = tier.lodBias;
            QualitySettings.meshLodThreshold = tier.meshLodThreshold;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
#if UNITY_EDITOR
                if (authoredSmallMesh == null) authoredSmallMesh = urp.smallMeshScreenPercentage;
#endif
                urp.smallMeshScreenPercentage = tier.smallMeshCullPercent;
            }
            ShadowBands.SetBelowSize(tier.noShadowBelowSize);
        }

#if UNITY_EDITOR
        // The pipeline is an asset: in the Editor a change made in Play would stay in it (and show as a changed file).
        // The value it had before the first change is put back when Play stops.
        private static float? authoredSmallMesh;

        [UnityEditor.InitializeOnLoadMethod]
        private static void PutBackWhenPlayStops() => UnityEditor.EditorApplication.playModeStateChanged += state =>
        {
            if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode || authoredSmallMesh == null) return;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
                urp.smallMeshScreenPercentage = authoredSmallMesh.Value;
            authoredSmallMesh = null;
        };
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            active = null;
            wantedTier = 2;
        }

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
            characterFarDistance = Mathf.Max(characterFarDistance, characterMidDistance);
            characterHiddenDistance = Mathf.Max(characterHiddenDistance, characterFarDistance);
        }
    }
}
