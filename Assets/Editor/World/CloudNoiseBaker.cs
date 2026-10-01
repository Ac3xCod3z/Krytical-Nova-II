using System.IO;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the tileable cloud noise used by the stylized sky and ComicPost cloud shadows.
    /// Generated once in the Editor (no runtime noise cost). Channels:
    /// R = cloud shapes (Perlin-Worley), G = detail (inverted Worley FBM), B = cirrus streaks, A = macro variation.
    /// </summary>
    public static class CloudNoiseBaker
    {
        public const string DefaultPath = "Assets/GameData/World/Textures/T_CloudNoise.png";
        private const int Size = 256;

        [MenuItem("Krytical Nova/World/Bake Cloud Noise Texture")]
        private static void BakeMenu()
        {
            var tex = Bake(DefaultPath);
            EditorGUIUtility.PingObject(tex);
        }

        public static Texture2D Bake(string assetPath)
        {
            var r = new float[Size * Size];
            var g = new float[Size * Size];
            var b = new float[Size * Size];
            var a = new float[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float u = (float)x / Size, v = (float)y / Size;
                    int i = y * Size + x;

                    float perlin = PerlinFbm(u, v, 4, 4, 0.5f, 11);
                    float worley = 1f - WorleyFbm(u, v, 5, 3, 23);
                    r[i] = Mathf.Lerp(perlin, worley, 0.45f) + (perlin - 0.5f) * worley * 0.6f;

                    g[i] = 1f - WorleyFbm(u, v, 14, 3, 37);
                    b[i] = PerlinFbmAniso(u, v, 2, 14, 4, 0.55f, 53);
                    a[i] = PerlinFbm(u, v, 2, 2, 0.5f, 71);
                }
            }

            Normalize(r); Normalize(g); Normalize(b); Normalize(a);

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(ToByte(r[i]), ToByte(g[i]), ToByte(b[i]), ToByte(a[i]));
            tex.SetPixels32(pixels);
            tex.Apply();

            string full = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = Size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            var standalone = importer.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.maxTextureSize = Size;
            standalone.format = TextureImporterFormat.BC7;
            importer.SetPlatformTextureSettings(standalone);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        // ---------------------------------------------------------------- noise

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

        private static void Normalize(float[] data)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (float d in data) { if (d < min) min = d; if (d > max) max = d; }
            float range = Mathf.Max(max - min, 1e-5f);
            for (int i = 0; i < data.Length; i++) data[i] = (data[i] - min) / range;
        }

        private static int Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        private static float Hash01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        private static int Wrap(int i, int period) => ((i % period) + period) % period;

        /// <summary>Tileable gradient noise in [-1,1] with integer periods per axis.</summary>
        private static float Perlin(float x, float y, int px, int py, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float Grad(int ix, int iy, float dx, float dy)
            {
                float angle = Hash01(Wrap(ix, px), Wrap(iy, py), seed) * Mathf.PI * 2f;
                return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
            }
            float n00 = Grad(x0, y0, fx, fy);
            float n10 = Grad(x0 + 1, y0, fx - 1f, fy);
            float n01 = Grad(x0, y0 + 1, fx, fy - 1f);
            float n11 = Grad(x0 + 1, y0 + 1, fx - 1f, fy - 1f);
            float ux = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float uy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, ux), Mathf.Lerp(n01, n11, ux), uy) * 1.414f;
        }

        private static float PerlinFbm(float u, float v, int period, int octaves, float gain, int seed)
        {
            return PerlinFbmAniso(u, v, period, period, octaves, gain, seed);
        }

        private static float PerlinFbmAniso(float u, float v, int periodX, int periodY, int octaves, float gain, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                int px = periodX << o, py = periodY << o;
                sum += Perlin(u * px, v * py, px, py, seed + o * 101) * amp;
                norm += amp;
                amp *= gain;
            }
            return sum / norm * 0.5f + 0.5f;
        }

        /// <summary>Tileable Worley F1 distance in ~[0,1].</summary>
        private static float Worley(float u, float v, int cells, int seed)
        {
            float x = u * cells, y = v * cells;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float best = 10f;
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int nx = cx + ox, ny = cy + oy;
                    int wx = Wrap(nx, cells), wy = Wrap(ny, cells);
                    float px = nx + Hash01(wx, wy, seed);
                    float py = ny + Hash01(wx, wy, seed + 7);
                    float dx = px - x, dy = py - y;
                    best = Mathf.Min(best, dx * dx + dy * dy);
                }
            }
            return Mathf.Clamp01(Mathf.Sqrt(best));
        }

        private static float WorleyFbm(float u, float v, int cells, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Worley(u, v, cells << o, seed + o * 31) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }
    }
}
