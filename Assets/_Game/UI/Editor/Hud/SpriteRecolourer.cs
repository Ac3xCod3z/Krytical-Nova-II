using System.IO;
using KryticalNova.Factions;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes a faction's copy of a piece of another pack's art, for the pieces a faction's own pack does not have.
    /// The copy keeps every pixel's light and shade and takes the faction's colours: steel and stone take the hue
    /// of the faction's panel tone, strong colour takes the accent's. (A tint on the image only multiplies: red art
    /// cannot become blue that way.) The copies live under Assets/_Game; nothing under Assets/Synty is changed.
    /// </summary>
    public static class SpriteRecolourer
    {
        /// <summary>One colour, remapped onto a faction's panel tone and accent. Used for pixels and for image tints alike.</summary>
        public static Color Remap(Color colour, Color tone, Color accent)
        {
            Color.RGBToHSV(colour, out float hue, out float saturation, out float value);
            Color.RGBToHSV(tone, out float toneHue, out float toneSaturation, out _);
            Color.RGBToHSV(accent, out float accentHue, out float accentSaturation, out _);

            // How far the colour is from grey, and whether it is one of the source's warm accents (reds, golds, bone).
            float coloured = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.22f, 0.5f, saturation));
            bool warm = hue < 0.14f || hue > 0.9f;
            float towardsAccent = warm ? coloured : 0f;

            float newHue = Mathf.Repeat(Mathf.LerpAngle(toneHue * 360f, accentHue * 360f, towardsAccent) / 360f, 1f);
            float steel = Mathf.Lerp(saturation, toneSaturation, 0.75f);
            float strong = Mathf.Min(1f, saturation * Mathf.Lerp(0.85f, accentSaturation / Mathf.Max(0.01f, saturation), 0.5f));
            float newSaturation = Mathf.Lerp(steel, strong, towardsAccent);
            Color mapped = Color.HSVToRGB(newHue, newSaturation, value);
            mapped.a = colour.a;
            return mapped;
        }

        /// <summary>
        /// The faction's copy of <paramref name="source"/> in <paramref name="folder"/> (made, or made again when
        /// the source is newer), with the source's borders, pivot and size.
        /// </summary>
        public static Sprite Copy(Sprite source, FactionId faction, string folder, Color tone, Color accent)
        {
            if (source == null) return null;
            WeatherVfxBuilder.EnsureFolder(folder);
            string path = $"{folder}/{source.name}_{faction}.png";

            Rect area = source.textureRect;
            int width = Mathf.RoundToInt(area.width), height = Mathf.RoundToInt(area.height);
            // The source need not be readable: it is drawn into a texture that is.
            RenderTexture target = RenderTexture.GetTemporary(source.texture.width, source.texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Graphics.Blit(source.texture, target);
            RenderTexture.active = target;
            var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            picture.ReadPixels(new Rect(area.x, source.texture.height - area.yMax, width, height), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);

            Color[] pixels = picture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i].a > 0f) pixels[i] = Remap(pixels[i], tone, accent);
            picture.SetPixels(pixels);
            File.WriteAllBytes(path, picture.EncodeToPNG());
            Object.DestroyImmediate(picture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureType = TextureImporterType.Sprite;
            settings.spriteMode = (int)SpriteImportMode.Single;
            settings.spritePixelsPerUnit = source.pixelsPerUnit;
            settings.spriteBorder = source.border;
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(source.pivot.x / Mathf.Max(1f, source.rect.width), source.pivot.y / Mathf.Max(1f, source.rect.height));
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.alphaIsTransparency = true;
            settings.mipmapEnabled = false;
            settings.sRGBTexture = true;
            importer.SetTextureSettings(settings);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
