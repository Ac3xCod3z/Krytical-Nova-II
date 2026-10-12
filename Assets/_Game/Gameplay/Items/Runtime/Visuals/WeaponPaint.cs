using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The paints of a weapon model, so that it can be dyed in a colourway (a <see cref="GearThemeLibrary"/> theme):
    /// which hues of its pack's palette texture are its primary, its secondary, its leather and so on, and which of
    /// its greys is the blade and which a guard or a housing. Its lights take the colourway's glow. Sits on the
    /// weapon's model prefab (Items/Data/Assets/Prefabs/Weapons) beside <see cref="WeaponGlowZone"/>. Worked out from
    /// the mesh by Krytical Nova ▸ Items ▸ 12. Bake Weapon Paints; tick <see cref="keepAsSet"/> to keep paints set by
    /// hand. A dye never touches the pack's own texture or material: the model is given a small repainted copy,
    /// shared by every weapon of that model in that colourway (<see cref="WeaponPaintRules"/> has the rule).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponPaint : MonoBehaviour
    {
        public WeaponPaintBand[] bands = Array.Empty<WeaponPaintBand>();
        [Tooltip("The metals of the model, when it has more than one: the first is the blade or the barrel and keeps the colourway's metal, the others take a colour.")]
        public WeaponPaintShade[] shades = Array.Empty<WeaponPaintShade>();
        [Tooltip("The greys of the model take the colourway's metal. Off: only the paints change.")]
        public bool paintMetal = true;
        [Tooltip("Dark grips and barrels take a dark shade of the colourway. Off: they are left as they are. Black is left alone either way.")]
        public bool paintDark = true;
        [Tooltip("What the model lights up takes the colourway's glow colour. Off: its lights keep the pack's colours.")]
        public bool paintLights = true;
        [Tooltip("Set by hand: the baker leaves these paints as they are.")]
        public bool keepAsSet;

        /// <summary>The side of the repainted copy of a palette texture. The palettes are blocks of flat colour: this is plenty.</summary>
        public const int Size = 512;
        private static readonly string[] TextureProperties = { "_Albedo_Map", "_BaseMap", "_MainTex" };
        // The packs' shader and Unity's own name the same three things differently.
        private static readonly string[] LightTextureProperties = { "_Emission_Map", "_EmissionMap" };
        private static readonly string[] LightColourProperties = { "_Emission_Color", "_EmissionColor" };
        private const string LightSwitchProperty = "_Enable_Emission";
        private const string LightKeyword = "_EMISSION";

        // Shared by every weapon: the palettes read back once, the repainted copies and the materials that show them.
        private static readonly Dictionary<Texture, Color32[]> Palettes = new Dictionary<Texture, Color32[]>();
        private static readonly Dictionary<(Texture palette, int paint, ushort colourway), Texture2D> Painted =
            new Dictionary<(Texture, int, ushort), Texture2D>();
        private static readonly Dictionary<(Texture lights, ushort colourway), Texture2D> Lit =
            new Dictionary<(Texture, ushort), Texture2D>();
        private static readonly Dictionary<(Material material, Texture2D painted), Material> Materials =
            new Dictionary<(Material, Texture2D), Material>();
        private static readonly Dictionary<int, Color32> Memo = new Dictionary<int, Color32>(64);

        private Renderer[] renderers;
        private Material[][] originals;
        private ushort applied;
        private int paintKey;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Palettes.Clear();
            Painted.Clear();
            Lit.Clear();
            Materials.Clear();
        }

        /// <summary>True when a dye would change anything on this model.</summary>
        public bool HasPaint => paintMetal || paintDark || paintLights || (bands != null && bands.Length > 0) || (shades != null && shades.Length > 0);

        /// <summary>Shows the model in a colourway; one that is not valid gives it its own colours back.</summary>
        public void Apply(in GearThemeLibrary.Theme theme)
        {
            if (theme.id == 0 || !HasPaint) { Restore(); return; }
            if (applied == theme.id) return;
            Remember();
            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null) continue;
                Material[] own = originals[r];
                var shown = new Material[own.Length];
                for (int m = 0; m < own.Length; m++) shown[m] = Repainted(own[m], theme);
                renderers[r].sharedMaterials = shown;
            }
            applied = theme.id;
        }

        /// <summary>The model's own colours.</summary>
        public void Restore()
        {
            if (applied == 0 || renderers == null) return;
            for (int r = 0; r < renderers.Length; r++)
                if (renderers[r] != null) renderers[r].sharedMaterials = originals[r];
            applied = 0;
        }

        private void Remember()
        {
            if (renderers != null) return;
            renderers = GetComponentsInChildren<Renderer>(true);
            originals = new Material[renderers.Length][];
            for (int r = 0; r < renderers.Length; r++) originals[r] = renderers[r].sharedMaterials;

            var hash = new HashCode();
            hash.Add(paintMetal);
            hash.Add(paintDark);
            hash.Add(paintLights);
            if (shades != null)
                foreach (WeaponPaintShade shade in shades)
                {
                    hash.Add(shade.value);
                    hash.Add(shade.width);
                    hash.Add((byte)shade.role);
                }
            if (bands != null)
                foreach (WeaponPaintBand band in bands)
                {
                    hash.Add(band.hue);
                    hash.Add(band.width);
                    hash.Add((byte)band.role);
                    hash.Add(band.value);
                }
            paintKey = hash.ToHashCode();
        }

        private Material Repainted(Material material, in GearThemeLibrary.Theme theme)
        {
            if (material == null) return null;
            string property = null;
            Texture palette = null;
            foreach (string name in TextureProperties)
            {
                if (!material.HasProperty(name)) continue;
                palette = material.GetTexture(name);
                if (palette == null) continue;
                property = name;
                break;
            }
            if (property == null) return material;

            var key = (palette, paintKey, theme.id);
            if (!Painted.TryGetValue(key, out Texture2D painted) || painted == null)
            {
                painted = Paint(palette, theme);
                if (painted == null) return material;
                Painted[key] = painted;
            }
            var shownKey = (material, painted);
            if (!Materials.TryGetValue(shownKey, out Material shown) || shown == null)
            {
                shown = new Material(material) { name = material.name + " (dyed)" };
                shown.SetTexture(property, painted);
                if (paintLights) RepaintLights(material, shown, theme);
                Materials[shownKey] = shown;
            }
            return shown;
        }

        /// <summary>
        /// The lights of a material in the colourway's glow colour: its emission map repainted, and the colour the
        /// shader multiplies it by made white at the strength it had (a pack that tints its lights would tint ours).
        /// A material that lights nothing up is left as it is.
        /// </summary>
        private static void RepaintLights(Material material, Material shown, in GearThemeLibrary.Theme theme)
        {
            bool on = material.HasProperty(LightSwitchProperty) ? material.GetFloat(LightSwitchProperty) > 0.5f : material.IsKeywordEnabled(LightKeyword);
            if (!on) return;
            foreach (string name in LightTextureProperties)
            {
                if (!material.HasProperty(name)) continue;
                Texture lights = material.GetTexture(name);
                if (lights == null) continue;
                var key = (lights, theme.id);
                if (!Lit.TryGetValue(key, out Texture2D lit) || lit == null)
                {
                    lit = PaintLights(lights, theme);
                    if (lit == null) return;
                    Lit[key] = lit;
                }
                shown.SetTexture(name, lit);
                foreach (string colourName in LightColourProperties)
                {
                    if (!material.HasProperty(colourName)) continue;
                    Color tint = material.GetColor(colourName);
                    float strength = tint.maxColorComponent;
                    shown.SetColor(colourName, new Color(strength, strength, strength, tint.a));
                }
                return;
            }
        }

        private static Texture2D PaintLights(Texture lights, in GearThemeLibrary.Theme theme)
        {
            Color32[] source = PaletteOf(lights);
            if (source == null) return null;
            var pixels = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++) pixels[i] = WeaponPaintRules.RecolourLight(source[i], theme);
            var painted = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = $"{lights.name} ({theme.name})",
                filterMode = lights.filterMode,
                wrapMode = lights.wrapMode,
            };
            painted.SetPixels32(pixels);
            painted.Apply(false, true);
            return painted;
        }

        private Texture2D Paint(Texture palette, in GearThemeLibrary.Theme theme)
        {
            Color32[] source = PaletteOf(palette);
            if (source == null) return null;
            var pixels = new Color32[source.Length];
            Memo.Clear();
            for (int i = 0; i < source.Length; i++)
            {
                Color32 colour = source[i];
                int packed = colour.r << 16 | colour.g << 8 | colour.b;
                if (!Memo.TryGetValue(packed, out Color32 dyed))
                {
                    dyed = WeaponPaintRules.Recolour(colour, bands, shades, theme, paintMetal, paintDark);
                    Memo[packed] = dyed;
                }
                dyed.a = colour.a;
                pixels[i] = dyed;
            }
            var painted = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = $"{palette.name} ({name}, {theme.name})",
                filterMode = palette.filterMode,
                wrapMode = palette.wrapMode,
            };
            painted.SetPixels32(pixels);
            painted.Apply(false, true);
            return painted;
        }

        /// <summary>A palette texture read back small. The packs' textures are not readable, so it is drawn and read once.</summary>
        private static Color32[] PaletteOf(Texture palette)
        {
            if (Palettes.TryGetValue(palette, out Color32[] pixels)) return pixels;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return null; // nothing draws here
            RenderTexture target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Graphics.Blit(palette, target);
            RenderTexture.active = target;
            var copy = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            pixels = copy.GetPixels32();
            if (Application.isPlaying) Destroy(copy); else DestroyImmediate(copy);
            Palettes[palette] = pixels;
            return pixels;
        }
    }
}
