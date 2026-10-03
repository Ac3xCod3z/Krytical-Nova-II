using System.IO;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Turns a flat white UI glyph (Synty interface packs) into an icon in the game's comic style, to sit next to the
    /// gear icons that ItemIconCapture renders through the comic pass: a crisp silhouette in one flat colour, an ink
    /// outline with a paper keyline, a cel highlight on the lit side and Ben-Day dots on the shaded side. The glyph only supplies the
    /// shape (and, where it has grey shading, where the shade falls); its soft airbrush edges are thresholded away.
    /// Works at 2x and box-filters down, like the item capture. Source textures are read through a blit, so vendor
    /// import settings are never touched.
    /// </summary>
    public static class ComicIconBaker
    {
        public const int Size = 256;
        private const int Supersample = 2;
        private const int Work = Size * Supersample;

        // Sizes in output (256 px) pixels.
        private const float Margin = 18f;          // room for the outline, keyline and drop shadow
        private const float OutlineWidth = 7f;     // reads as a ~1 px ink line at hotbar size
        private const float KeylineWidth = 3.5f;
        private const float HighlightWidth = 5f;
        private const float ShadowOffset = 5f;
        private const float DotCell = 9f;
        private const float AlphaThreshold = 0.5f;
        private const float InkLuminance = 0.55f;   // glyph pixels darker than this become ink lines
        private const float ShadeLuminance = 0.86f; // ... and darker than this, the shaded cel band

        public static readonly Color Ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        private static readonly Color ShadowIndigo = new Color32(0x4C, 0x52, 0xBF, 0xFF);
        private static readonly Color Paper = new Color32(0xF4, 0xEE, 0xDF, 0xFF);

        /// <summary>
        /// Bakes <paramref name="sourcePath"/> into a comic icon at <paramref name="outputPath"/> and returns it as a sprite.
        /// </summary>
        /// <param name="rotation">Degrees, counter-clockwise. Long weapon silhouettes are turned onto the diagonal.</param>
        public static Sprite Bake(string sourcePath, Color fill, string outputPath, float rotation = 0f)
        {
            var source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            if (source == null)
            {
                Debug.LogWarning($"[Icons] Glyph not found: {sourcePath}");
                return null;
            }

            Color32[] glyph = ReadPixels(source, out int gw, out int gh);
            Shape shape = Rasterize(glyph, gw, gh, rotation);
            Color[] big = Paint(shape, fill);
            Color32[] small = Downsample(big);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(small);
            tex.Apply();
            string full = Path.Combine(Directory.GetCurrentDirectory(), outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            ImportAsSprite(outputPath);
            return AssetDatabase.LoadAssetAtPath<Sprite>(outputPath);
        }

        // ---------------------------------------------------------------- source

        private static Color32[] ReadPixels(Texture2D source, out int width, out int height)
        {
            width = source.width;
            height = source.height;
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            read.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = read.GetPixels32();
            Object.DestroyImmediate(read);
            return pixels;
        }

        /// <summary>The glyph fitted into the working square: what is solid, and how bright the glyph was there.</summary>
        private struct Shape
        {
            public bool[] Solid;
            public float[] Luminance;
            public bool HasOwnShading;
        }

        private static Shape Rasterize(Color32[] glyph, int gw, int gh, float rotation)
        {
            // Tight bounds of the solid glyph, in the rotated frame, so every icon fills the same box.
            float rad = rotation * Mathf.Deg2Rad;
            float cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
            Vector2 centre = new Vector2(gw, gh) * 0.5f;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int y = 0; y < gh; y++)
            {
                for (int x = 0; x < gw; x++)
                {
                    if (glyph[y * gw + x].a < AlphaThreshold * 255f) continue;
                    float dx = x + 0.5f - centre.x, dy = y + 0.5f - centre.y;
                    float rx = dx * cos - dy * sin, ry = dx * sin + dy * cos;
                    if (rx < minX) minX = rx;
                    if (rx > maxX) maxX = rx;
                    if (ry < minY) minY = ry;
                    if (ry > maxY) maxY = ry;
                }
            }

            var shape = new Shape { Solid = new bool[Work * Work], Luminance = new float[Work * Work] };
            if (minX > maxX) return shape; // empty glyph

            float box = Work - 2f * Margin * Supersample;
            float scale = Mathf.Max(maxX - minX, maxY - minY) / box; // glyph pixels per working pixel
            Vector2 rotatedCentre = new Vector2(minX + maxX, minY + maxY) * 0.5f;
            int solid = 0, shaded = 0;

            for (int y = 0; y < Work; y++)
            {
                for (int x = 0; x < Work; x++)
                {
                    // Working pixel → rotated glyph frame → glyph texel (inverse rotation).
                    float rx = (x + 0.5f - Work * 0.5f) * scale + rotatedCentre.x;
                    float ry = (y + 0.5f - Work * 0.5f) * scale + rotatedCentre.y;
                    float gx = rx * cos + ry * sin + centre.x - 0.5f;
                    float gy = -rx * sin + ry * cos + centre.y - 0.5f;
                    Color c = Bilinear(glyph, gw, gh, gx, gy);
                    if (c.a < AlphaThreshold) continue;

                    int i = y * Work + x;
                    shape.Solid[i] = true;
                    // The alpha edge darkens colour in a non-premultiplied glyph; judge shading on fully solid texels only.
                    shape.Luminance[i] = c.a > 0.9f ? c.grayscale : 1f;
                    solid++;
                    if (shape.Luminance[i] < ShadeLuminance) shaded++;
                }
            }
            shape.HasOwnShading = solid > 0 && shaded > solid * 0.03f;
            return shape;
        }

        private static Color Bilinear(Color32[] pixels, int w, int h, float x, float y)
        {
            if (x < -1f || y < -1f || x > w || y > h) return Color.clear;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            Color a = Texel(pixels, w, h, x0, y0), b = Texel(pixels, w, h, x0 + 1, y0);
            Color c = Texel(pixels, w, h, x0, y0 + 1), d = Texel(pixels, w, h, x0 + 1, y0 + 1);
            return Color.Lerp(Color.Lerp(a, b, tx), Color.Lerp(c, d, tx), ty);
        }

        private static Color Texel(Color32[] pixels, int w, int h, int x, int y) =>
            x < 0 || y < 0 || x >= w || y >= h ? Color.clear : (Color)pixels[y * w + x];

        // ---------------------------------------------------------------- comic treatment

        private static Color[] Paint(Shape shape, Color fill)
        {
            float outline = OutlineWidth * Supersample;
            float keyline = KeylineWidth * Supersample;
            int highlight = Mathf.RoundToInt(HighlightWidth * Supersample);
            int shadow = Mathf.RoundToInt(ShadowOffset * Supersample);
            float cell = DotCell * Supersample;

            float[] outside = Distance(shape.Solid, false);
            Color lit = Color.Lerp(fill, Color.white, 0.62f);
            Color dot = Color.Lerp(fill, ShadowIndigo, 0.6f) * 0.82f;
            dot.a = 1f;

            var pixels = new Color[Work * Work];
            for (int y = 0; y < Work; y++)
            {
                for (int x = 0; x < Work; x++)
                {
                    int i = y * Work + x;
                    if (shape.Solid[i])
                    {
                        float lum = shape.Luminance[i];
                        // Texture rows run bottom-up: "down-right" on screen is +x, -y.
                        float diagonal = ((float)x / Work + (1f - (float)y / Work)) * 0.5f; // 0 top-left .. 1 bottom-right
                        bool shade = shape.HasOwnShading ? lum < ShadeLuminance : diagonal > 0.56f;

                        Color c = fill;
                        if (shape.HasOwnShading && lum < InkLuminance) c = Ink;
                        else if (shade)
                        {
                            // Ben-Day dots on a 45° grid, growing toward the shaded corner.
                            float u = (x + y) / (cell * 1.41421f), v = (x - y) / (cell * 1.41421f);
                            float fu = u - Mathf.Floor(u) - 0.5f, fv = v - Mathf.Floor(v) - 0.5f;
                            float radius = Mathf.Lerp(0.26f, 0.44f, Mathf.InverseLerp(0.45f, 0.8f, diagonal));
                            if (fu * fu + fv * fv < radius * radius) c = dot;
                        }
                        // Rim facing the top-left light. Skipped where the shape is thinner than two rims (sword blades),
                        // or the highlight would replace the fill colour altogether.
                        else if (!IsSolid(shape.Solid, x - highlight, y + highlight)
                                 && IsSolid(shape.Solid, x + highlight * 2, y - highlight * 2)) c = lit;
                        pixels[i] = c;
                    }
                    else if (outside[i] <= outline)
                    {
                        pixels[i] = Ink;
                    }
                    else if (outside[i] <= outline + keyline + 0.5f)
                    {
                        // Paper keyline outside the ink, like a cut-out sticker: the HUD is dark, so the ink line
                        // alone would disappear into it.
                        Color c = Paper;
                        c.a = Mathf.Clamp01(outline + keyline + 0.5f - outside[i]);
                        pixels[i] = c;
                    }
                    else
                    {
                        // Hard drop shadow: the outlined shape again, pushed down-right.
                        int sx = x - shadow, sy = y + shadow;
                        if (sx >= 0 && sy < Work && outside[sy * Work + sx] <= outline + keyline)
                        {
                            Color c = Ink;
                            c.a = 0.55f;
                            pixels[i] = c;
                        }
                    }
                }
            }
            return pixels;
        }

        private static bool IsSolid(bool[] solid, int x, int y) =>
            x >= 0 && y >= 0 && x < Work && y < Work && solid[y * Work + x];

        /// <summary>Chamfer (3-4) distance in working pixels from each pixel to the nearest <paramref name="target"/> pixel.</summary>
        private static float[] Distance(bool[] solid, bool target)
        {
            const float Far = 1e6f, Straight = 1f, Diagonal = 1.41421f;
            var d = new float[Work * Work];
            for (int i = 0; i < d.Length; i++) d[i] = solid[i] != target ? 0f : Far;

            for (int y = 0; y < Work; y++)
            {
                for (int x = 0; x < Work; x++)
                {
                    int i = y * Work + x;
                    float v = d[i];
                    if (x > 0) v = Mathf.Min(v, d[i - 1] + Straight);
                    if (y > 0)
                    {
                        v = Mathf.Min(v, d[i - Work] + Straight);
                        if (x > 0) v = Mathf.Min(v, d[i - Work - 1] + Diagonal);
                        if (x < Work - 1) v = Mathf.Min(v, d[i - Work + 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            for (int y = Work - 1; y >= 0; y--)
            {
                for (int x = Work - 1; x >= 0; x--)
                {
                    int i = y * Work + x;
                    float v = d[i];
                    if (x < Work - 1) v = Mathf.Min(v, d[i + 1] + Straight);
                    if (y < Work - 1)
                    {
                        v = Mathf.Min(v, d[i + Work] + Straight);
                        if (x < Work - 1) v = Mathf.Min(v, d[i + Work + 1] + Diagonal);
                        if (x > 0) v = Mathf.Min(v, d[i + Work - 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            return d;
        }

        /// <summary>Box filter, alpha-weighted so transparent texels don't darken the edges.</summary>
        private static Color32[] Downsample(Color[] big)
        {
            var small = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < Supersample; sy++)
                    {
                        for (int sx = 0; sx < Supersample; sx++)
                        {
                            Color c = big[(y * Supersample + sy) * Work + x * Supersample + sx];
                            r += c.r * c.a;
                            g += c.g * c.a;
                            b += c.b * c.a;
                            a += c.a;
                        }
                    }
                    small[y * Size + x] = a > 0f
                        ? (Color32)new Color(r / a, g / a, b / a, a / (Supersample * Supersample))
                        : new Color32(0, 0, 0, 0);
                }
            }
            return small;
        }

        private static void ImportAsSprite(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
