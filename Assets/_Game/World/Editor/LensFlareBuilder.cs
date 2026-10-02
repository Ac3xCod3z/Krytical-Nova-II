using System.IO;
using KryticalNova.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the comic sun lens flare: bakes two small flat-shaded textures (halftone dot disc, inked ring) and a
    /// LensFlareDataSRP that strings them along the flare axis with flat polygon/ring shapes. Nothing is drawn on
    /// the sun itself: the sky shader owns the sun's look, and a flare there reads as a star stamped over the art. Assigns it to the scene's EnvironmentPresenter, which drives intensity (sun only, cloud-aware).
    /// </summary>
    public static class LensFlareBuilder
    {
        private const string Folder = "Assets/_Game/World/Sky/LensFlare";
        private const string AssetPath = Folder + "/LF_ComicSun.asset";
        private const string CloudNoisePath = "Assets/_Game/World/Sky/Textures/T_CloudNoise.png";
        private const int Size = 128;

        [MenuItem("Krytical Nova/World/Build Comic Lens Flare")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(Folder);
            AssetDatabase.DeleteAsset($"{Folder}/T_Flare_Starburst.png"); // retired: read as a star stamped on the sun

            var halftone = BakeTexture("T_Flare_Halftone", Halftone);
            var ring = BakeTexture("T_Flare_InkRing", InkRing);

            var data = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(AssetPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
                AssetDatabase.CreateAsset(data, AssetPath);
            }

            data.elements = new[]
            {
                // Halftone and flat shapes marching across the screen, cool colours away from the sun.
                ImageElement(halftone, 0.32f, 0.16f, new Color(1f, 0.6f, 0.25f), 0.22f),
                Shape(SRPLensFlareType.Polygon, 0.58f, 0.11f, new Color(0.35f, 0.95f, 0.85f), 0.16f, sides: 6),
                ImageElement(ring, 1.0f, 0.3f, new Color(1f, 0.92f, 0.55f), 0.16f),
                Shape(SRPLensFlareType.Polygon, 1.3f, 0.06f, new Color(0.85f, 0.45f, 1f), 0.14f, sides: 6, count: 3),
                ImageElement(halftone, 1.7f, 0.24f, new Color(0.4f, 0.6f, 1f), 0.1f),
            };
            EditorUtility.SetDirty(data);

            // The presenter samples the cloud field on the CPU to hide the flare behind passing clouds.
            if (AssetImporter.GetAtPath(CloudNoisePath) is TextureImporter noise && !noise.isReadable)
            {
                noise.isReadable = true;
                noise.SaveAndReimport();
            }

            var presenter = Object.FindAnyObjectByType<EnvironmentPresenter>();
            if (presenter != null)
            {
                var so = new SerializedObject(presenter);
                so.FindProperty("sunFlare").objectReferenceValue = data;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(presenter);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[LensFlareBuilder] Built {AssetPath}" + (presenter != null ? " and assigned it to the Environment Presenter." : "."));
        }

        private static LensFlareDataElementSRP ImageElement(Texture tex, float position, float scale, Color tint, float intensity)
        {
            var e = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Image,
                lensFlareTexture = tex,
                position = position,
                uniformScale = scale,
                sizeXY = Vector2.one,
                tint = tint,
                localIntensity = intensity,
                blendMode = SRPLensFlareBlendMode.Additive,
                preserveAspectRatio = true,
                modulateByLightColor = true,
                visible = true,
            };
            return e;
        }

        private static LensFlareDataElementSRP Shape(SRPLensFlareType type, float position, float scale, Color tint, float intensity,
                                                     int sides = 6, int count = 1)
        {
            var e = new LensFlareDataElementSRP
            {
                flareType = type,
                position = position,
                uniformScale = scale,
                sizeXY = Vector2.one,
                tint = tint,
                localIntensity = intensity,
                blendMode = SRPLensFlareBlendMode.Additive,
                modulateByLightColor = true,
                visible = true,
                fallOff = 1f,          // hard edge: flat comic shapes, not soft photographic blobs
                edgeOffset = 0.05f,
                sideCount = sides,
            };
            if (count > 1)
            {
                e.allowMultipleElement = true;
                e.count = count;
                e.distribution = SRPLensFlareDistribution.Uniform;
                e.lengthSpread = 0.5f;
            }
            return e;
        }

        // ------------------------------------------------------------------ texture bakes (white, alpha-less additive)

        private delegate float Pixel(float x, float y); // x,y in [-1,1], returns brightness 0-1

        private static Texture2D BakeTexture(string name, Pixel pixel)
        {
            string path = $"{Folder}/{name}.png";
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            for (int j = 0; j < Size; j++)
            for (int i = 0; i < Size; i++)
            {
                float x = (i + 0.5f) / Size * 2f - 1f, y = (j + 0.5f) / Size * 2f - 1f;
                float v = Mathf.Clamp01(pixel(x, y));
                tex.SetPixel(i, j, new Color(v, v, v, v));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static float Halftone(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            if (r >= 1f) return 0f;
            const float cells = 9f;
            float gx = (x * 0.5f + 0.5f) * cells, gy = (y * 0.5f + 0.5f) * cells;
            float dx = gx - Mathf.Floor(gx) - 0.5f, dy = gy - Mathf.Floor(gy) - 0.5f;
            float dotRadius = 0.48f * (1f - r);                                          // dots shrink toward the rim
            return Step(dotRadius - Mathf.Sqrt(dx * dx + dy * dy), 0.03f);
        }

        private static float InkRing(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float ring = 1f - Mathf.Abs(r - 0.82f) / 0.07f;
            float inner = 0.12f * Mathf.Clamp01(1f - Mathf.Abs(r - 0.68f) / 0.03f);     // thin echo line
            return Mathf.Max(Step(ring, 0.15f), inner);
        }

        private static float Step(float v, float softness) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, softness, v));
    }
}
