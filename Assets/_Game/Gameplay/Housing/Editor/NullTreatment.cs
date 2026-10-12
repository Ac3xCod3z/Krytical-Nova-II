using System.IO;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// The Null's own colours for things from the Fantasy Kingdom pack (owner, 12 Oct 2026: the Null may draw on it,
    /// made darker and grittier, as prefab variants). The pack colours its models from one palette picture; a
    /// darkened copy of that picture, in a copy of the pack's material, is all a variant needs to wear. The
    /// vendor's prefab, material and picture are only read.
    /// </summary>
    public static class NullTreatment
    {
        public const string VariantFolder = HousingLotBuilder.PrefabFolder + "/Null";
        public const string TextureFolder = HousingLotBuilder.Root + "/Textures";
        // The properties the pack's shaders keep their colour picture under, in the order they are tried.
        private static readonly string[] PictureProperties = { "_Albedo_Map", "_BaseMap", "_MainTex" };
        private const int OpaqueUpTo = 2500; // glass and other see-through materials are left as they are

        /// <summary>Which of the looks the Null wear: 0 Ashen, 1 Blood and Iron, 2 Void. The owner's pick.</summary>
        public const int Style = 0;
        public static readonly string[] StyleNames = { "Ashen", "Blood and Iron", "Void" };

        /// <summary>One colour of the palette, as the Null wear it.</summary>
        public static Color Treat(Color colour, int style)
        {
            Color.RGBToHSV(colour, out float h, out float s, out float v);
            switch (style)
            {
                case 1: // Blood and Iron: everything goes to cold dark iron, and what was red stays red, deeper
                    bool red = (h < 0.07f || h > 0.93f) && s > 0.3f;
                    s = red ? Mathf.Min(1f, s * 1.15f) : s * 0.2f;
                    v *= red ? 0.62f : 0.5f;
                    break;
                case 2: // Void: every colour is drawn towards violet, and dimmed
                    h = Mathf.Repeat(Mathf.LerpAngle(h * 360f, 270f, 0.65f) / 360f, 1f);
                    s = Mathf.Clamp01(s * 0.7f + 0.12f);
                    v *= 0.5f;
                    break;
                default: // Ashen: the colour is burnt out of it
                    s *= 0.35f;
                    v *= 0.55f;
                    break;
            }
            Color treated = Color.HSVToRGB(h, s, v);
            if (style == 0) treated = new Color(treated.r * 0.94f, treated.g * 0.97f, treated.b * 1.06f); // a cold cast
            treated.a = colour.a;
            return treated;
        }

        // The palette picture with every colour treated. Read through the graphics card, so the vendor's file is
        // read whatever its format and whether or not it may be read from scripts.
        private static Texture2D TreatedPalette(Texture source, int style)
        {
            RenderTexture scratch = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, scratch);
            RenderTexture.active = scratch;
            var picture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(scratch);
            Color[] colours = picture.GetPixels();
            for (int n = 0; n < colours.Length; n++) colours[n] = Treat(colours[n], style);
            picture.SetPixels(colours);
            picture.Apply();
            return picture;
        }

        private static string PictureProperty(Material material)
        {
            foreach (string property in PictureProperties)
                if (material.HasProperty(property) && material.GetTexture(property) != null) return property;
            return null;
        }

        /// <summary>Whether a material is one the Null wear darkened: an opaque one of the Fantasy Kingdom pack that has a colour picture.</summary>
        public static bool Wears(Material material)
        {
            if (material == null || material.renderQueue >= OpaqueUpTo || PictureProperty(material) == null) return false;
            return AssetDatabase.GetAssetPath(material).StartsWith($"{FurnishingCatalogBuilder.SyntyRoot}/{FurnishingCatalogBuilder.Kingdom}/");
        }

        /// <summary>A copy of a material in one of the looks, not saved anywhere: for a picture to choose by.</summary>
        public static Material Preview(Material source, int style)
        {
            string property = PictureProperty(source);
            var material = new Material(source) { name = "Null Preview " + style, hideFlags = HideFlags.HideAndDontSave };
            if (property == null) return material;
            Texture picture = source.GetTexture(property);
            Texture2D treated = TreatedPalette(picture, style);
            treated.filterMode = picture.filterMode;
            treated.wrapMode = picture.wrapMode;
            treated.hideFlags = HideFlags.HideAndDontSave;
            material.SetTexture(property, treated);
            return material;
        }

        /// <summary>
        /// The Null's copy of one of the pack's materials, made (or made again, when the look has changed) on
        /// demand: the same material with its colour picture darkened.
        /// </summary>
        public static Material Material(Material source)
        {
            string property = PictureProperty(source);
            if (property == null) return source;
            WeatherVfxBuilder.EnsureFolder(TextureFolder);
            WeatherVfxBuilder.EnsureFolder(HousingLotBuilder.MaterialFolder);
            Texture picture = source.GetTexture(property);
            string texturePath = $"{TextureFolder}/T_Null_{picture.name}.png";
            string materialPath = $"{HousingLotBuilder.MaterialFolder}/M_Null_{source.name}.mat";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            string mark = "null-style-" + Style;
            if (importer == null || importer.userData != mark)
            {
                Texture2D treated = TreatedPalette(picture, Style);
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), texturePath), treated.EncodeToPNG());
                Object.DestroyImmediate(treated);
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                // As the vendor imports its own: a palette is a grid of flat colours, and must stay one.
                var from = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(picture)) as TextureImporter;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = from != null && from.mipmapEnabled;
                importer.filterMode = from != null ? from.filterMode : FilterMode.Point;
                importer.wrapMode = from != null ? from.wrapMode : TextureWrapMode.Repeat;
                importer.textureCompression = from != null ? from.textureCompression : TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = from != null ? from.maxTextureSize : 2048;
                importer.userData = mark;
                importer.SaveAndReimport();
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(source) { name = Path.GetFileNameWithoutExtension(materialPath) };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var own = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (own != null && material.GetTexture(property) != own)
            {
                material.SetTexture(property, own);
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>
        /// A prefab variant of a Fantasy Kingdom model that wears the Null's colours wherever the model wore the
        /// pack's own. Its glass, flames and other see-through materials are left as they are.
        /// </summary>
        public static GameObject Variant(GameObject vendorPrefab)
        {
            if (vendorPrefab == null) return null;
            WeatherVfxBuilder.EnsureFolder(VariantFolder);
            string path = $"{VariantFolder}/{vendorPrefab.name}_Null.prefab";
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(vendorPrefab);
            try
            {
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] worn = renderer.sharedMaterials;
                    bool changed = false;
                    for (int n = 0; n < worn.Length; n++)
                    {
                        if (!Wears(worn[n])) continue;
                        worn[n] = Material(worn[n]);
                        changed = true;
                    }
                    if (changed) renderer.sharedMaterials = worn;
                }
                return PrefabUtility.SaveAsPrefabAsset(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
