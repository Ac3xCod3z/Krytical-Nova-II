using System.IO;
using UnityEditor;
using KryticalNova.World;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the sky's star map and nebula (owner's NIGHT list item 24; picks 14 Nov 2026: 1A glint stars, 2B faint constellation lines,
    /// 3A Soul Tide nebula, constellations spread apart). Krytical Nova ▸ World ▸ Sky ▸ Bake Star Map writes:
    /// <list type="bullet">
    /// <item><c>World/Sky/Textures/T_StarMap.png</c>: a lat-long map of the dome in its own frame (u = right ascension, v = declination).
    /// RGB = hand-placed shaped stars in a few sizes and colours, the constellations' stars and a painted galaxy band with dust lanes;
    /// A = the constellation lines, which the shader shows only at deep night.</item>
    /// <item><c>World/Sky/Textures/T_Nebula.png</c>: the Soul Tide, drawn by the shader around its own moving direction.</item>
    /// <item><c>Resources/KN/SkyEvents.asset</c> beside the presenter, pointing at both (made once; its numbers are kept on a re-bake).</item>
    /// </list>
    /// Fixed seeds: the same bake every time, so it is committed once (Git LFS rule).
    /// </summary>
    public static class StarMapBaker
    {
        public const string TextureFolder = "Assets/_Game/World/Sky/Textures";
        public const string StarMapPath = TextureFolder + "/T_StarMap.png";
        public const string NebulaPath = TextureFolder + "/T_Nebula.png";
        public const string SettingsPath = "Assets/_Game/World/Runtime/Presentation/Resources/KN/SkyEvents.asset";
        public const string OverlayShaderPath = "Assets/_Game/World/Sky/Shaders/SkyEvents.shader";

        const int W = 4096, H = 2048, NebulaSize = 512;
        const float PxPerDeg = H / 180f;

        static readonly Color[] Palette =
        {
            new Color(1f, 0.965f, 0.88f), new Color(0.81f, 0.89f, 1f), new Color(1f, 0.886f, 0.66f),
            new Color(1f, 0.816f, 0.847f), new Color(0.75f, 0.957f, 1f),
        };

        [MenuItem("Krytical Nova/World/Sky/Bake Star Map")]
        public static void BakeMenu()
        {
            var settings = Bake();
            Selection.activeObject = settings;
            Debug.Log($"[StarMapBaker] Star map and nebula baked; settings at {SettingsPath}. Smallest gap between constellations: " +
                      $"{StarCharts.SmallestGap(settings.poleElevation, out _, out _):0.0} deg (needs {StarCharts.MinGapDegrees}).");
        }

        /// <summary>Bakes both textures and makes sure the settings asset points at them. Returns the settings.</summary>
        public static SkyEventSettings Bake()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SkyEventSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                settings = ScriptableObject.CreateInstance<SkyEventSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            WritePng(StarMapPath, BakeStarMap(settings.poleElevation), W, H);
            WritePng(NebulaPath, BakeNebula(), NebulaSize, NebulaSize);
            AssetDatabase.ImportAsset(StarMapPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(NebulaPath, ImportAssetOptions.ForceSynchronousImport);
            SetImport(StarMapPath, false, TextureWrapMode.Repeat, TextureWrapMode.Clamp, 4096);
            SetImport(NebulaPath, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp, 512);

            settings.starMap = AssetDatabase.LoadAssetAtPath<Texture2D>(StarMapPath);
            settings.nebulaTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NebulaPath);
            // the overlay shader rides on the settings asset (Resources), so a build always includes it
            settings.overlayShader = AssetDatabase.LoadAssetAtPath<Shader>(OverlayShaderPath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        static void SetImport(string path, bool mips, TextureWrapMode u, TextureWrapMode v, int size)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = mips;
            imp.wrapModeU = u;
            imp.wrapModeV = v;
            imp.filterMode = FilterMode.Bilinear;
            imp.maxTextureSize = size;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.alphaIsTransparency = false;
            imp.SaveAndReimport();
        }

        static void WritePng(string path, Color[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.SetPixels(px);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }

        // ------------------------------------------------------------------ noise (seamless on the sphere: 3D value noise)

        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }

        static float Noise(Vector3 p, int seed)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
            float fx = p.x - x, fy = p.y - y, fz = p.z - z;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            float a = Mathf.Lerp(Hash(x, y, z, seed), Hash(x + 1, y, z, seed), fx);
            float b = Mathf.Lerp(Hash(x, y + 1, z, seed), Hash(x + 1, y + 1, z, seed), fx);
            float c = Mathf.Lerp(Hash(x, y, z + 1, seed), Hash(x + 1, y, z + 1, seed), fx);
            float d = Mathf.Lerp(Hash(x, y + 1, z + 1, seed), Hash(x + 1, y + 1, z + 1, seed), fx);
            return Mathf.Lerp(Mathf.Lerp(a, b, fy), Mathf.Lerp(c, d, fy), fz);
        }

        static float Fbm(Vector3 p, int seed, int octaves)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += Noise(p, seed + i * 31) * amp; norm += amp; p *= 2.03f; amp *= 0.5f; }
            return sum / norm;
        }

        // ------------------------------------------------------------------ star map

        static Vector3 DirOfPixel(int x, int y) =>
            StarCharts.DomeDir((x + 0.5f) / W * 360f, (y + 0.5f) / H * 180f - 90f);

        static Color[] BakeStarMap(float poleElevation)
        {
            var rgb = new Vector3[W * H];
            var lines = new float[W * H];
            var rng = new System.Random(240);

            // ---- the galaxy band: a great circle tilted across the dome, violet to cyan, with dark dust lanes
            Vector3 bandPole = StarCharts.DomeDir(200f, 28f);
            var band = new float[W * H];
            // worked out on a 2 px grid (the band is soft; 4x faster), each value written to its 2x2 block
            for (int y = 0; y < H; y += 2)
                for (int x = 0; x < W; x += 2)
                {
                    Vector3 d = DirOfPixel(x, y);
                    float dist = Mathf.Asin(Mathf.Clamp(Vector3.Dot(d, bandPole), -1f, 1f)) * Mathf.Rad2Deg;
                    if (Mathf.Abs(dist) > 40f) continue;
                    float n = Fbm(d * 3f, 11, 5);
                    float m = Mathf.Exp(-Mathf.Pow(dist / (16f * (0.75f + 0.6f * n)), 2f));
                    float n2 = Fbm(d * 9f, 17, 4);
                    float lane = Mathf.Exp(-Mathf.Pow(dist / 3.2f - (n2 - 0.5f) * 6f, 2f)) * (n2 > 0.35f ? 1f : 0f);
                    m *= 1f - 0.65f * lane;
                    m = Mathf.Clamp(m * (0.55f + 0.7f * n), 0f, 1.4f);
                    float tint = Fbm(d * 2.2f, 23, 4);
                    Vector3 col = Vector3.Lerp(new Vector3(0.42f, 0.36f, 0.79f), new Vector3(0.25f, 0.72f, 0.84f), tint);
                    float core = Mathf.Exp(-Mathf.Pow(dist / 5f, 2f)) * m;
                    Vector3 v = col * m * 0.30f + new Vector3(1f, 0.94f, 0.84f) * core * 0.10f;
                    for (int by = y; by < y + 2; by++)
                        for (int bx = x; bx < x + 2; bx++) { band[by * W + bx] = m; rgb[by * W + bx] = v; }
                }

            // ---- keep-out rings so loose bright stars never crowd a constellation's shape
            var centres = new Vector3[StarCharts.All.Length];
            var keepOut = new float[StarCharts.All.Length];
            for (int i = 0; i < centres.Length; i++)
            {
                var c = StarCharts.All[i];
                centres[i] = StarCharts.DomeDir(c.RightAscension, poleElevation + c.DeclinationOffset);
                keepOut[i] = c.Radius + 3f;
            }
            bool Crowds(Vector3 d)
            {
                for (int i = 0; i < centres.Length; i++) if (Vector3.Angle(d, centres[i]) < keepOut[i]) return true;
                return false;
            }

            // ---- dim fill: tiny points, gathered in the band
            for (int placed = 0, tries = 0; placed < 14000 && tries < 400000; tries++)
            {
                int x = rng.Next(W), y = rng.Next(H);
                float lat = ((y + 0.5f) / H * 180f - 90f) * Mathf.Deg2Rad;
                if (rng.NextDouble() > Mathf.Cos(lat)) continue;                     // even on the sphere, not piled at the poles
                if (rng.NextDouble() > 0.3f + 0.7f * band[y * W + x]) continue;
                Color c = Palette[rng.Next(Palette.Length)];
                float k = (0.25f + (float)rng.NextDouble() * 0.5f);
                Dot(rgb, x, y, new Vector3(c.r, c.g, c.b) * k);
                placed++;
            }

            // ---- mid stars: soft round glows in a few sizes
            for (int i = 0; i < 700; i++)
            {
                Vector3 d = RandomDir(rng);
                if (Crowds(d)) continue;
                Color c = Palette[rng.Next(Palette.Length)];
                StarSprite(rgb, d, 0.7f + (float)rng.NextDouble() * 0.8f, new Vector3(c.r, c.g, c.b), 0.75f, 0f);
            }

            // ---- bright stars (pick 1A): soft cores, the brightest with a four-point cross glint
            for (int i = 0; i < 110; i++)
            {
                Vector3 d = RandomDir(rng);
                if (Crowds(d)) continue;
                float s = (float)rng.NextDouble();
                Color c = Palette[rng.Next(Palette.Length)];
                StarSprite(rgb, d, 1.1f + s * 1.6f, new Vector3(c.r, c.g, c.b), 1f, s > 0.55f ? 5f + s * 12f : 0f);
            }

            // ---- the constellations: their stars bright and glinting, their lines in alpha
            var crng = new System.Random(24);
            foreach (var c in StarCharts.All)
            {
                foreach (var line in c.Lines)
                    for (int i = 0; i + 1 < line.Length; i++)
                    {
                        Vector2 a = StarCharts.RaDec(c, line[i], poleElevation), b = StarCharts.RaDec(c, line[i + 1], poleElevation);
                        LineAlpha(lines, StarCharts.DomeDir(a.x, a.y), StarCharts.DomeDir(b.x, b.y));
                    }
                var seen = new System.Collections.Generic.HashSet<Vector2>();
                foreach (var line in c.Lines)
                    foreach (var p in line)
                    {
                        Vector2 key = new Vector2(Mathf.Round(p.x * 100f), Mathf.Round(p.y * 100f));
                        if (!seen.Add(key)) continue;
                        Vector2 rd = StarCharts.RaDec(c, p, poleElevation);
                        float s = (float)crng.NextDouble();
                        Color col = Palette[crng.Next(Palette.Length)];
                        StarSprite(rgb, StarCharts.DomeDir(rd.x, rd.y), 1.6f + s * 1.2f, new Vector3(col.r, col.g, col.b), 1.1f,
                                   s > 0.45f ? 7f + s * 10f : 0f);
                    }
            }

            var outPx = new Color[W * H];
            for (int i = 0; i < outPx.Length; i++)
            {
                Vector3 v = rgb[i];
                // soft shoulder: bright cores stay white-hot without clipping the band's colours
                v = new Vector3(v.x / (1f + v.x * 0.35f), v.y / (1f + v.y * 0.35f), v.z / (1f + v.z * 0.35f)) * 1.25f;
                outPx[i] = new Color(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), Mathf.Clamp01(v.z), Mathf.Clamp01(lines[i]));
            }
            return outPx;
        }

        static Vector3 RandomDir(System.Random rng)
        {
            float z = (float)rng.NextDouble() * 2f - 1f, a = (float)rng.NextDouble() * Mathf.PI * 2f, r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        static void ToPixel(Vector3 d, out float px, out float py)
        {
            float ra = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg; if (ra < 0f) ra += 360f;
            float dec = Mathf.Asin(Mathf.Clamp(d.z, -1f, 1f)) * Mathf.Rad2Deg;
            px = ra / 360f * W - 0.5f;
            py = (dec + 90f) / 180f * H - 0.5f;
        }

        static void Dot(Vector3[] rgb, int x, int y, Vector3 c)
        {
            rgb[y * W + x] += c;
            Vector3 half = c * 0.25f;
            rgb[y * W + (x + 1) % W] += half; rgb[y * W + (x + W - 1) % W] += half;
            if (y + 1 < H) rgb[(y + 1) * W + x] += half;
            if (y > 0) rgb[(y - 1) * W + x] += half;
        }

        /// <summary>A star: soft core of radius <paramref name="r"/> px, a wide faint glow, and a cross glint of length <paramref name="glint"/> px (0 = none).</summary>
        static void StarSprite(Vector3[] rgb, Vector3 dir, float r, Vector3 col, float k, float glint)
        {
            ToPixel(dir, out float cx, out float cy);
            float stretch = 1f / Mathf.Max(0.15f, Mathf.Cos((cy + 0.5f) / H * Mathf.PI - Mathf.PI * 0.5f));
            float reach = Mathf.Max(r * 4f, glint * 1.1f) + 2f;
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - reach)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + reach));
            int xr = Mathf.CeilToInt(reach * stretch);
            for (int y = y0; y <= y1; y++)
                for (int xi = -xr; xi <= xr; xi++)
                {
                    int x = ((Mathf.FloorToInt(cx) + xi) % W + W) % W;
                    float dx = (Mathf.FloorToInt(cx) + xi - cx) / stretch, dy = y - cy;
                    float d2 = dx * dx + dy * dy;
                    float v = Mathf.Exp(-d2 / (r * r)) * k + Mathf.Exp(-d2 / (r * r * 12f)) * 0.16f * k;
                    if (glint > 0f)
                    {
                        float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                        float arm = Mathf.Exp(-ay * ay / 0.6f) * Mathf.Pow(Mathf.Clamp01(1f - ax / glint), 2.2f)
                                  + Mathf.Exp(-ax * ax / 0.6f) * Mathf.Pow(Mathf.Clamp01(1f - ay / glint), 2.2f);
                        v += arm * 0.85f * k;
                    }
                    if (v > 0.002f) rgb[y * W + x] += col * v;
                }
        }

        /// <summary>A thin soft line along the great circle from a to b, into the alpha map.</summary>
        static void LineAlpha(float[] a, Vector3 from, Vector3 to)
        {
            float deg = Vector3.Angle(from, to);
            int steps = Mathf.Max(2, Mathf.CeilToInt(deg * PxPerDeg * 3f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 d = Vector3.Slerp(from, to, i / (float)steps);
                ToPixel(d, out float px, out float py);
                float stretch = 1f / Mathf.Max(0.15f, Mathf.Cos((py + 0.5f) / H * Mathf.PI - Mathf.PI * 0.5f));
                int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
                for (int oy = -2; oy <= 2; oy++)
                    for (int ox = -3; ox <= 3; ox++)
                    {
                        int y = iy + oy; if (y < 0 || y >= H) continue;
                        int x = ((ix + ox) % W + W) % W;
                        float dx = (ix + ox - px) / stretch, dy = y - py;
                        float v = Mathf.Exp(-(dx * dx + dy * dy) / 0.9f);
                        int k = y * W + x;
                        if (v > a[k]) a[k] = v;
                    }
            }
        }

        // ------------------------------------------------------------------ nebula (pick 3A: Soul Tide, teal-violet wisps)

        static Color[] BakeNebula()
        {
            var px = new Color[NebulaSize * NebulaSize];
            for (int y = 0; y < NebulaSize; y++)
                for (int x = 0; x < NebulaSize; x++)
                {
                    float u = (x + 0.5f) / NebulaSize - 0.5f, v = (y + 0.5f) / NebulaSize - 0.5f;
                    var p = new Vector3(u * 4f, v * 4f, 0.37f);
                    float n1 = Fbm(p * 1.2f, 74, 6), n2 = Fbm(p * 2.2f, 75, 6), n3 = Fbm(p * 3.5f, 76, 5);
                    float flow = Mathf.Sin((u * 6f + v * 2f + n1 * 3f) * Mathf.PI) * 0.5f + 0.5f;
                    float env = Mathf.Exp(-((u / 0.30f) * (u / 0.30f) + (v / 0.24f) * (v / 0.24f)));
                    float dens = env * Mathf.Pow(n2, 1.6f) * (0.4f + 0.6f * flow * flow * flow) * 2.4f;
                    Color col = Color.Lerp(new Color(0.25f, 0.88f, 0.82f), new Color(0.61f, 0.42f, 1f), n3);
                    float core = env * Mathf.Pow(n2, 4f) * 0.6f;
                    Color c = col * dens + new Color(0.91f, 1f, 0.98f) * core;
                    float a = Mathf.Clamp01(dens * 0.9f + core);
                    px[y * NebulaSize + x] = new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), a);
                }
            return px;
        }
    }
}
