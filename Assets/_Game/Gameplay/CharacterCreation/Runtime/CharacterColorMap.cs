using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Builds a character's Sidekick colour map: a 32x32 texture where each cell of the 16x16 colour grid is a 2x2
    /// texel block (the layout Sidekick's own tool writes). Order: base palette, then skin tone, then the colours
    /// the player picked, then the set colours of what the character wears.
    /// </summary>
    public static class CharacterColorMap
    {
        public const int Size = CharacterPaletteLibrary.Grid * 2;
        public const int PixelCount = Size * Size;

        public static void Fill(in CharacterRecipe recipe, CharacterCreationDatabase database, Color32[] pixels)
        {
            var white = new Color32(255, 255, 255, 255);
            CharacterPaletteLibrary.Palette palette = default;
            bool hasPalette = database.Palettes != null && database.Palettes.TryGet(recipe.palette, out palette);
            for (int v = 0; v < CharacterPaletteLibrary.Grid; v++)
                for (int u = 0; u < CharacterPaletteLibrary.Grid; u++)
                    SetCell(pixels, u, v, hasPalette ? palette.cells[v * CharacterPaletteLibrary.Grid + u] : white);

            var lineage = database.GetLineage(recipe.lineage);
            var setup = lineage != null ? lineage.SetupFor(recipe.bodyType) : null;
            if (setup != null && recipe.skinTone > 0 && recipe.skinTone <= setup.skinTones.Count)
            {
                var cells = setup.skinTones[recipe.skinTone - 1].cells;
                if (cells != null)
                    foreach (var cell in cells) SetCell(pixels, cell.u, cell.v, cell.color);
            }

            // the right eye's own colour last, whatever order the picks were made in, so it is never painted over by Eyes
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < recipe.ColorCount; i++)
                {
                    var pick = recipe.colors[i];
                    if ((pick.channel == CharacterColorChannels.EyeRight) != (pass == 1)) continue;
                    if (!database.TryGetChannel(pick.channel, out var channel) || channel.targets == null) continue;
                    foreach (var target in channel.targets) SetCell(pixels, target.u, target.v, Shade(pick.color, target.shade));
                }
        }

        /// <summary>
        /// The glowing eyes' emission map (owner, 24 Oct 2026): black everywhere but the two eye-colour cells, which
        /// glow in the colour <paramref name="colours"/> (the filled colour map) gives them, <paramref name="strength"/>
        /// times over so bloom picks them up. Same 32x32 layout as the colour map.
        /// </summary>
        public static void FillEyeGlow(Color32[] colours, Color[] emission, float strength)
        {
            for (int i = 0; i < emission.Length; i++) emission[i] = Color.black;
            foreach (var (u, v) in EyeColourCells)
            {
                int row = v * 2 * Size + u * 2;
                Color c = (Color)colours[row] * strength;
                c.a = 1f;
                emission[row] = emission[row + 1] = emission[row + Size] = emission[row + Size + 1] = c;
            }
        }

        /// <summary>Sidekick's "Eye Color Left" and "Eye Color Right" cells: the iris of every lineage's eyes.</summary>
        // and (8,2), the whole eye of a skeleton's eye parts and a robot's eyes in its head (read from the parts, 24 Oct 2026)
        private static readonly (int u, int v)[] EyeColourCells = { (4, 2), (5, 2), (8, 2) };

        /// <summary>
        /// Paints a worn piece's set colours over the map: only the cells that piece's own meshes use, so skin, hair
        /// and whatever the piece does not cover keep the character's colours. A piece without a theme (the basic
        /// outfit) changes nothing, unless it is dyed: <paramref name="dye"/> is then the colour grid of the
        /// colourway it wears in place of its own (a theme's 256 cells).
        /// </summary>
        public static void OverlayGear(ItemDefinition item, Color32[] pixels, Color32[] dye = null)
        {
            if (item == null || item.colorCells == null) return;
            Color32[] colours = dye != null && dye.Length == GearThemeLibrary.CellCount ? dye : null;
            if (colours == null)
            {
                if (item.colorTheme == 0 || item.colorThemes == null || !item.colorThemes.TryGet(item.colorTheme, out var theme)) return;
                colours = theme.cells;
            }
            foreach (byte cell in item.colorCells)
                SetCell(pixels, cell % CharacterPaletteLibrary.Grid, cell / CharacterPaletteLibrary.Grid, colours[cell]);
        }

        public static Texture2D CreateTexture(string name)
        {
            // Same import settings as Sidekick's exported colour maps: sRGB, point filtered, clamped, no mips.
            return new Texture2D(Size, Size, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private static void SetCell(Color32[] pixels, int u, int v, Color32 color)
        {
            if (u < 0 || v < 0 || u >= CharacterPaletteLibrary.Grid || v >= CharacterPaletteLibrary.Grid) return;
            int x = u * 2, y = v * 2;
            int row = y * Size + x;
            pixels[row] = color;
            pixels[row + 1] = color;
            pixels[row + Size] = color;
            pixels[row + Size + 1] = color;
        }

        private static Color32 Shade(Color32 color, float shade)
        {
            if (shade <= 0f || Mathf.Approximately(shade, 1f)) return color;
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * shade), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * shade), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * shade), 0, 255),
                color.a);
        }
    }
}
