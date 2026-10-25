using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Painted gear parts (<see cref="GearPaintLook"/>): finds a part's paint and dyes it. A dye never touches the paint:
    /// the paint is redrawn once per colourway on the GPU into a copy that every character wearing that piece in that
    /// colourway shares (the same rule as <see cref="WeaponPaint"/>). In each of the paint's four regions the
    /// colourway's colour takes the place of the paint's, at the paint's own brightness, so every painted fold,
    /// scratch and shadow stays. The set's own colourway is the paint as it was made.
    /// </summary>
    public static class GearPaint
    {
        private const string Folder = "Meshes/PartPaint/";
        private const string DyeShaderPath = Folder + "GearPaintDye";

        private static readonly int RegionsId = Shader.PropertyToID("_Regions");
        private static readonly int BrightnessId = Shader.PropertyToID("_RegionBrightness");
        private static readonly int ColourAId = Shader.PropertyToID("_ColourA");
        private static readonly int ColourBId = Shader.PropertyToID("_ColourB");
        private static readonly int ColourCId = Shader.PropertyToID("_ColourC");
        private static readonly int ColourDId = Shader.PropertyToID("_ColourD");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private static readonly Dictionary<Mesh, GearPaintLook> Looks = new Dictionary<Mesh, GearPaintLook>();
        private static readonly Dictionary<(GearPaintLook look, ushort colourway), Material> Dyed =
            new Dictionary<(GearPaintLook, ushort), Material>();
        private static readonly List<RenderTexture> Painted = new List<RenderTexture>();
        private static Material dyeMaterial;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Looks.Clear();
            Dyed.Clear();
            Painted.Clear();
            dyeMaterial = null;
        }

        /// <summary>Forget loaded paints (after painted gear is imported again in the Editor).</summary>
        public static void Clear()
        {
            Looks.Clear();
            foreach (var kv in Dyed) if (kv.Value != null) Object.Destroy(kv.Value);
            Dyed.Clear();
            foreach (var rt in Painted) if (rt != null) rt.Release();
            Painted.Clear();
        }

        /// <summary>The paint of <paramref name="part"/>; false for a part that reads the colour map like Synty's own.</summary>
        public static bool TryGetLook(SidekickPartRef part, out GearPaintLook look)
        {
            look = null;
            if (part.mesh == null) return false;
            if (!Looks.TryGetValue(part.mesh, out look))
            {
                string name = part.mesh.name;
                int lod = name.IndexOf(' ');
                if (lod > 0) name = name.Substring(0, lod);
                // Only our own parts are painted: no lookup (and no failed Resources.Load) for Synty's thousand.
                look = name.StartsWith("SK_KRYT_", System.StringComparison.Ordinal) ? Resources.Load<GearPaintLook>(Folder + name + "_Paint") : null;
                if (look != null && look.material == null) look = null;
                Looks[part.mesh] = look;
            }
            return look != null;
        }

        /// <summary>
        /// The material <paramref name="look"/> is drawn in when its piece is dyed in <paramref name="colourway"/>:
        /// the paint's own material for an invalid colourway (the piece's own colours).
        /// </summary>
        public static Material MaterialFor(GearPaintLook look, in GearThemeLibrary.Theme colourway)
        {
            if (look == null) return null;
            if (!colourway.IsValid || look.regions == null || !(look.Paint is Texture paint)) return look.material;
            var key = (look, colourway.id);
            if (Dyed.TryGetValue(key, out var made) && made != null) return made;

            if (dyeMaterial == null)
            {
                var shader = Resources.Load<Shader>(DyeShaderPath);
                if (shader == null) { Debug.LogWarning("[GearPaint] The dye shader is missing (Resources/" + DyeShaderPath + ")."); return look.material; }
                dyeMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            var metal = colourway.metal != null && colourway.metal.Length > 1 ? colourway.metal[1] : colourway.secondary;
            dyeMaterial.SetTexture(RegionsId, look.regions);
            dyeMaterial.SetVector(BrightnessId, look.regionBrightness);
            dyeMaterial.SetColor(ColourAId, colourway.primary);
            dyeMaterial.SetColor(ColourBId, colourway.secondary);
            dyeMaterial.SetColor(ColourCId, metal);
            dyeMaterial.SetColor(ColourDId, colourway.leather);

            var rt = new RenderTexture(paint.width, paint.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = paint.name + "_" + colourway.name,
                useMipMap = true,
                autoGenerateMips = true,
                filterMode = paint.filterMode,
                wrapMode = paint.wrapMode,
                anisoLevel = paint.anisoLevel,
                hideFlags = HideFlags.DontSave
            };
            rt.Create();
            Graphics.Blit(paint, rt, dyeMaterial);
            Painted.Add(rt);

            made = new Material(look.material) { name = look.material.name + " (" + colourway.name + ")", hideFlags = HideFlags.DontSave };
            made.SetTexture(BaseMapId, rt);
            made.mainTexture = rt;
            Dyed[key] = made;
            return made;
        }

        /// <summary>
        /// The colourway a piece's dye grid belongs to (a dye arrives as a theme's cells): found in the piece's own
        /// colourway library by the grid itself. False for no dye.
        /// </summary>
        public static bool TryFindColourway(ItemDefinition item, Color32[] dye, out GearThemeLibrary.Theme colourway)
        {
            colourway = default;
            if (dye == null || item == null || item.colorThemes == null) return false;
            var themes = item.colorThemes.Themes;
            for (int i = 0; i < themes.Count; i++)
                if (ReferenceEquals(themes[i].cells, dye)) { colourway = themes[i]; return colourway.IsValid; }
            return false;
        }
    }
}
