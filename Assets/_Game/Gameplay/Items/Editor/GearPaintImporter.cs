using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Brings in the paint of a painted gear set (owner, 24 Oct 2026: the Tripo paint is kept, the swatch "broke the
    /// gear"): the set folder's &lt;Set&gt;_paint.json names each part's paint PNG (written by the painted export). Each
    /// paint gets its import settings, a material (URP Lit, the paint as its base map) and its dye regions, a mask that
    /// says which areas a colourway recolours (R main cloth, G second cloth, B plate and metal, A leather and trim),
    /// grouped from the paint's own colours unless a hand-painted &lt;paint&gt;_Mask.png sits beside it. Each part then
    /// gets its <see cref="GearPaintLook"/> in Resources/Meshes/PartPaint, where the game finds it by the part's name.
    /// </summary>
    public static class GearPaintImporter
    {
        public const string PaintFolder = SidekickPartCatalogBaker.OwnPartsRoot + "/PartPaint";
        private const int PaintSize = 2048;
        private const int Groups = 5;        // colour groups the set's paint is sorted into before they are given a region
        private const float SameGroup = 0.07f; // groups closer than this are one paint

        // The set's colour groups and the region each is, worked out once over every paint of the set, so one material
        // (a cloth, a plate, a leather) takes the same dye on every piece (24 Oct 2026: per part, one cape cloth took three).
        private static Vector4[] setCentres;
        private static int[] setRegion;
        private const int SampleSide = 256;  // the paint is grouped from a picture this size

        /// <summary>Imports every paint the set folder's paint json names; returns how many parts got a paint.</summary>
        public static int Import(string setFolder)
        {
            string json = Directory.GetFiles(setFolder, "*_paint.json").Length > 0 ? Directory.GetFiles(setFolder, "*_paint.json")[0] : null;
            if (json == null) { Debug.LogError($"[Items] No *_paint.json in {setFolder}: is it a painted set?"); return 0; }
            var parts = ReadParts(File.ReadAllText(json));
            if (!AssetDatabase.IsValidFolder(PaintFolder)) AssetDatabase.CreateFolder(SidekickPartCatalogBaker.OwnPartsRoot, "PartPaint");

            var paths = new List<string>();
            foreach (var kv in parts) { string pp = $"{setFolder}/{kv.Value}"; if (File.Exists(pp) && !paths.Contains(pp)) paths.Add(pp); }
            GroupSet(paths);

            var materials = new Dictionary<string, (Material material, Texture2D regions, Vector4 brightness)>();
            int count = 0;
            foreach (var kv in parts)
            {
                string pngPath = $"{setFolder}/{kv.Value}";
                if (!File.Exists(pngPath)) { Debug.LogError($"[Items] {kv.Key}: its paint {pngPath} is missing."); continue; }
                if (!materials.TryGetValue(pngPath, out var made))
                {
                    made = PaintOf(pngPath);
                    materials[pngPath] = made;
                }
                string lookPath = $"{PaintFolder}/{kv.Key}_Paint.asset";
                var look = AssetDatabase.LoadAssetAtPath<GearPaintLook>(lookPath);
                if (look == null)
                {
                    look = ScriptableObject.CreateInstance<GearPaintLook>();
                    AssetDatabase.CreateAsset(look, lookPath);
                }
                look.material = made.material;
                look.regions = made.regions;
                look.regionBrightness = made.brightness;
                EditorUtility.SetDirty(look);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] Painted gear: {count} parts of {Path.GetFileName(setFolder)} have their paint ({materials.Count} paints).");
            return count;
        }

        /// <summary>{ "parts": { "part": "paint.png", ... } } read by hand: JsonUtility has no dictionaries.</summary>
        private static Dictionary<string, string> ReadParts(string text)
        {
            var result = new Dictionary<string, string>();
            int at = text.IndexOf("\"parts\"", StringComparison.Ordinal);
            if (at < 0) return result;
            int open = text.IndexOf('{', at), close = text.IndexOf('}', open);
            foreach (string entry in text.Substring(open + 1, close - open - 1).Split(','))
            {
                var bits = entry.Split(':');
                if (bits.Length != 2) continue;
                string key = bits[0].Trim().Trim('"'), value = bits[1].Trim().Trim('"');
                if (key.Length > 0 && value.Length > 0) result[key] = value;
            }
            return result;
        }

        private static (Material, Texture2D, Vector4) PaintOf(string pngPath)
        {
            // The paint: sRGB, mipmapped, 2048, high-quality compression.
            if (AssetImporter.GetAtPath(pngPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = PaintSize;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 4;
                importer.SaveAndReimport();
            }
            var paint = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);

            // The regions: hand-painted beside the paint, or grouped from it.
            string stem = Path.GetFileNameWithoutExtension(pngPath);
            string handMask = $"{Path.GetDirectoryName(pngPath).Replace('\\', '/')}/{stem}_Mask.png";
            string regionsPath = $"{PaintFolder}/{stem}_Regions.png";
            Vector4 brightness;
            var pixels = ReadPixels(pngPath, out int w, out int h);
            if (File.Exists(handMask))
            {
                var mask = ReadPixels(handMask, out int mw, out int mh);
                brightness = MeanBrightness(pixels, w, h, mask, mw, mh);
                File.Copy(handMask, regionsPath, true);
            }
            else
            {
                var regions = GroupRegions(pixels, w, h, out brightness);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
                tex.SetPixels32(regions);
                File.WriteAllBytes(regionsPath, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
            }
            AssetDatabase.ImportAsset(regionsPath);
            if (AssetImporter.GetAtPath(regionsPath) is TextureImporter mi)
            {
                mi.textureType = TextureImporterType.Default;
                mi.sRGBTexture = false;           // weights, not colours
                mi.mipmapEnabled = false;
                mi.maxTextureSize = PaintSize;
                mi.textureCompression = TextureImporterCompression.Uncompressed;
                mi.alphaSource = TextureImporterAlphaSource.FromInput;
                mi.alphaIsTransparency = false;
                mi.wrapMode = TextureWrapMode.Clamp;
                mi.SaveAndReimport();
            }
            var regionsTex = AssetDatabase.LoadAssetAtPath<Texture2D>(regionsPath);

            string matPath = $"{PaintFolder}/M_{stem}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, matPath);
            }
            material.SetTexture("_BaseMap", paint);
            material.mainTexture = paint;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            // A cape is one layer of cloth (Tripo's two skins drifted apart on the swing chains and showed through as
            // shards, owner 25 Oct 2026), so its inside is drawn too.
            bool cloth = stem.Contains("_24ABAC_");
            material.SetFloat("_Cull", cloth ? 0f : 2f);
            material.doubleSidedGI = cloth;
            EditorUtility.SetDirty(material);
            return (material, regionsTex, brightness);
        }

        private static Color32[] ReadPixels(string path, out int w, out int h)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            tex.LoadImage(File.ReadAllBytes(path));
            w = tex.width; h = tex.height;
            var px = tex.GetPixels32();
            UnityEngine.Object.DestroyImmediate(tex);
            return px;
        }

        private static float Linear(byte c) => Mathf.GammaToLinearSpace(c / 255f);
        private static float Brightness(Color32 c) => 0.2126f * Linear(c.r) + 0.7152f * Linear(c.g) + 0.0722f * Linear(c.b);

        // A paint's colour as the grouping sees it: hue as a point on a circle (weighted by how colourful it is), and a
        // little brightness, so a dark leather and a light bone fall apart while the light and shade of one paint stay
        // together (a heavier brightness split one painted cloth into three regions, 24 Oct 2026).
        private static Vector4 Feature(Color32 c)
        {
            Color.RGBToHSV(c, out float hue, out float sat, out float val);
            float a = hue * Mathf.PI * 2f;
            return new Vector4(Mathf.Cos(a) * sat, Mathf.Sin(a) * sat, val * 0.22f, sat * 0.5f);
        }

        /// <summary>The paint's dye regions, from the set's colour groups (<see cref="GroupSet"/>).</summary>
        private static Color32[] GroupRegions(Color32[] pixels, int w, int h, out Vector4 brightness)
        {
            var centres = setCentres; var region = setRegion;
            // Each pixel's region, then each island of the paint (the pieces the bake laid out, apart on a black ground)
            // takes the region most of it is in: one painted piece takes one dye, never a speckle of three.
            var pixelRegion = new sbyte[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                pixelRegion[i] = c.r + c.g + c.b < 24 ? (sbyte)-2 : (sbyte)region[Nearest(centres, Feature(c))];
            }
            IslandMajority(pixelRegion, w, h);

            var result = new Color32[pixels.Length];
            var lumSum = new double[4]; var lumN = new int[4];
            for (int i = 0; i < pixels.Length; i++)
            {
                int r = pixelRegion[i];
                byte R = 0, G = 0, B = 0, A = 0;
                if (r == 0) R = 255; else if (r == 1) G = 255; else if (r == 2) B = 255; else if (r == 3) A = 255;
                result[i] = new Color32(R, G, B, A);
                if (r >= 0) { lumSum[r] += Brightness(pixels[i]); lumN[r]++; }
            }
            brightness = new Vector4(Mean(0), Mean(1), Mean(2), Mean(3));
            return result;

            float Mean(int r) => lumN[r] > 0 ? (float)(lumSum[r] / lumN[r]) : 0.5f;
        }

        /// <summary>
        /// Sorts the colours of every paint of the set into groups (k-means over small copies, the ground left out, groups
        /// that are one paint merged), then gives the groups regions: the greys are plate and metal (B), the colourful ones
        /// by how much they cover, main cloth (R), second cloth (G), then leather and trim (A). Near-black stays as painted.
        /// </summary>
        private static void GroupSet(List<string> paths)
        {
            var samples = new List<Vector4>();
            foreach (string path in paths)
            {
                var pixels = ReadPixels(path, out int w, out int h);
                for (int y = 0; y < SampleSide; y++)
                    for (int x = 0; x < SampleSide; x++)
                    {
                        var c = pixels[(y * h / SampleSide) * w + x * w / SampleSide];
                        if (c.r + c.g + c.b < 24) continue; // the bake's empty background
                        samples.Add(Feature(c));
                    }
            }
            var centres = new List<Vector4>();
            var rng = new System.Random(7);
            for (int k = 0; k < Groups; k++) centres.Add(samples.Count > 0 ? samples[rng.Next(samples.Count)] : Vector4.zero);
            var assign = new int[samples.Count];
            for (int pass = 0; pass < 16; pass++)
            {
                var sum = new Vector4[centres.Count]; var n = new int[centres.Count];
                for (int i = 0; i < samples.Count; i++)
                {
                    int best = Nearest(centres.ToArray(), samples[i]);
                    assign[i] = best; sum[best] += samples[i]; n[best]++;
                }
                for (int k = 0; k < centres.Count; k++) if (n[k] > 0) centres[k] = sum[k] / n[k];
            }
            // groups that are one paint become one
            var area = new int[centres.Count];
            foreach (int a in assign) area[a]++;
            for (int a = 0; a < centres.Count; a++)
                for (int b = a + 1; b < centres.Count; b++)
                    if (area[a] > 0 && area[b] > 0 && (centres[a] - centres[b]).magnitude < SameGroup)
                    {
                        centres[a] = (centres[a] * area[a] + centres[b] * area[b]) / (area[a] + area[b]);
                        area[a] += area[b]; area[b] = 0;
                    }

            var region = new int[centres.Count];
            for (int k = 0; k < region.Length; k++) region[k] = -1;
            var colourful = new List<int>();
            for (int k = 0; k < centres.Count; k++)
            {
                float sat = new Vector2(centres[k].x, centres[k].y).magnitude;
                float val = centres[k].z / 0.22f;
                if (area[k] == 0 || val < 0.08f) continue;               // near-black or merged away: as painted
                if (sat < 0.16f) region[k] = 2;                           // greys: plate and metal
                else colourful.Add(k);
            }
            colourful.Sort((a, b) => area[b].CompareTo(area[a]));
            for (int i = 0; i < colourful.Count; i++) region[colourful[i]] = i == 0 ? 0 : i == 1 ? 1 : 3;
            // a merged-away group follows the one it joined (nearest live centre)
            for (int k = 0; k < centres.Count; k++)
                if (area[k] == 0)
                {
                    int best = -1; float bestD = float.MaxValue;
                    for (int j = 0; j < centres.Count; j++) if (area[j] > 0) { float d = (centres[j] - centres[k]).sqrMagnitude; if (d < bestD) { bestD = d; best = j; } }
                    if (best >= 0) region[k] = region[best];
                }
            setCentres = centres.ToArray();
            setRegion = region;
            var names = new[] { "main cloth", "second cloth", "plate and metal", "leather and trim" };
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < centres.Count; k++)
                if (area[k] > 0) sb.Append($" [{(region[k] >= 0 ? names[region[k]] : "as painted")}: {100f * area[k] / Math.Max(1, samples.Count):F0} %]");
            Debug.Log($"[Items] Painted gear: the set's colour groups:{sb}");
        }

        /// <summary>Every island of painted pixels (-2 is the ground) takes the region most of its pixels have (-1 = as painted).</summary>
        private static void IslandMajority(sbyte[] regionOf, int w, int h)
        {
            var seen = new bool[regionOf.Length];
            var island = new List<int>(4096);
            var stack = new Stack<int>();
            var votes = new int[5];
            for (int start = 0; start < regionOf.Length; start++)
            {
                if (seen[start] || regionOf[start] == -2) continue;
                island.Clear();
                stack.Push(start); seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    island.Add(i);
                    int x = i % w, y = i / w;
                    if (x > 0) Visit(i - 1); if (x < w - 1) Visit(i + 1);
                    if (y > 0) Visit(i - w); if (y < h - 1) Visit(i + w);
                }
                for (int v = 0; v < votes.Length; v++) votes[v] = 0;
                foreach (int i in island) votes[regionOf[i] + 1]++;
                int best = 0;
                for (int v = 1; v < votes.Length; v++) if (votes[v] > votes[best]) best = v;
                foreach (int i in island) regionOf[i] = (sbyte)(best - 1);
            }

            void Visit(int j)
            {
                if (seen[j] || regionOf[j] == -2) return;
                seen[j] = true;
                stack.Push(j);
            }
        }

        private static Vector4 MeanBrightness(Color32[] paint, int w, int h, Color32[] mask, int mw, int mh)
        {
            var sum = new double[4]; var n = new double[4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = paint[y * w + x];
                    var m = mask[(y * mh / h) * mw + x * mw / w];
                    float l = Brightness(c);
                    float[] wt = { m.r / 255f, m.g / 255f, m.b / 255f, m.a / 255f };
                    for (int r = 0; r < 4; r++) { sum[r] += l * wt[r]; n[r] += wt[r]; }
                }
            return new Vector4(M(0), M(1), M(2), M(3));
            float M(int r) => n[r] > 0 ? (float)(sum[r] / n[r]) : 0.5f;
        }

        private static int Nearest(Vector4[] centres, Vector4 f)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int k = 0; k < centres.Length; k++)
            {
                float d = (centres[k] - f).sqrMagnitude;
                if (d < bestD) { bestD = d; best = k; }
            }
            return best;
        }
    }
}
