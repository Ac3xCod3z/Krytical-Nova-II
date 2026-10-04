using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Builds a character's Sidekick colour map: a 32x32 texture where each cell of the 16x16 colour grid is a 2x2
    /// texel block (the layout Sidekick's own tool writes). Order: base palette, then skin tone, then the colours
    /// the player picked.
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

            for (int i = 0; i < recipe.ColorCount; i++)
            {
                var pick = recipe.colors[i];
                if (!database.TryGetChannel(pick.channel, out var channel) || channel.targets == null) continue;
                foreach (var target in channel.targets) SetCell(pixels, target.u, target.v, Shade(pick.color, target.shade));
            }
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
