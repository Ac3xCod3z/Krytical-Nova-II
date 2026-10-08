using System.Collections.Generic;
using System.IO;
using KryticalNova.Pets;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the emoji the animal pets speak in (<see cref="PetEmojiLibrary"/>). Each comes from one effect of the
    /// Cartoon Emotion Pack: the effect says which cell of the pack's sheet it shows, and the sheet keeps the
    /// drawing's lines, its shapes and its accents in its red, green and blue channels. The cell is cut out, drawn in
    /// ink with the emoji's two colours, trimmed and written as a sprite of its own. The pack is only read.
    /// </summary>
    public static class PetEmojiBaker
    {
        public const string LibraryPath = "Assets/_Game/Gameplay/Pets/Data/Assets/Chatter/PetEmojiLibrary.asset";
        public const string EmojiFolder = "Assets/_Game/Gameplay/Pets/Data/Assets/Chatter/Emoji";

        [MenuItem("Krytical Nova/Items/9. Bake Pet Emoji")]
        public static void BakeMenu()
        {
            var library = AssetDatabase.LoadAssetAtPath<PetEmojiLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogError($"[Pets] There is no emoji library at {LibraryPath}.");
                return;
            }
            Debug.Log($"[Pets] Baked {Bake(library)} of {library.emoji.Length} pet emoji into {EmojiFolder}.");
        }

        /// <summary>Bakes every emoji of the library and returns how many came out.</summary>
        public static int Bake(PetEmojiLibrary library)
        {
            WeatherVfxBuilder.EnsureFolder(EmojiFolder);
            int baked = 0;
            foreach (PetEmojiLibrary.Emoji emoji in library.emoji)
            {
                if (emoji == null || string.IsNullOrEmpty(emoji.name)) continue;
                Sprite sprite = Bake(library, emoji);
                if (sprite == null) continue;
                emoji.sprite = sprite;
                baked++;
            }
            // Only the library is saved: a save of everything would also write whatever else is waiting.
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            ReleasePackMaterials(library);
            return baked;
        }

        /// <summary>
        /// The pack's materials were only read, but Unity brings an older material up to date while it is in memory
        /// (two newer blend properties here) and marks it to be saved, again after every script reload. The next
        /// save of the project would then rewrite the pack's own files. So the materials the bake loaded are let go.
        /// </summary>
        private static void ReleasePackMaterials(PetEmojiLibrary library)
        {
            var loaded = new HashSet<Material>();
            foreach (PetEmojiLibrary.Emoji emoji in library.emoji)
            {
                if (emoji == null || emoji.source == null) continue;
                foreach (ParticleSystemRenderer renderer in emoji.source.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (Material material in renderer.sharedMaterials)
                        if (material != null && AssetDatabase.GetAssetPath(material).StartsWith("Assets/_ThirdParty/")) loaded.Add(material);
            }
            foreach (Material material in loaded)
            {
                EditorUtility.ClearDirty(material);
                Resources.UnloadAsset(material);
            }
        }

        private static Sprite Bake(PetEmojiLibrary library, PetEmojiLibrary.Emoji emoji)
        {
            if (!TryFindCell(emoji.source, out Texture sheet, out RectInt cell))
            {
                Debug.LogWarning($"[Pets] {emoji.name}: its source effect has no drawing on a sheet. Is the emotion pack in the project?");
                return null;
            }
            Color32[] pixels = ReadPixels(sheet, out int width, out int height);

            // Draw the cell: ink where there is anything, the fill where the green channel is, the accent where the
            // blue channel is alone, white where both are (a shine).
            var drawn = new Color[cell.width * cell.height];
            int minX = cell.width, minY = cell.height, maxX = -1, maxY = -1;
            for (int y = 0; y < cell.height; y++)
            {
                for (int x = 0; x < cell.width; x++)
                {
                    Color32 p = pixels[(cell.y + y) * width + cell.x + x];
                    if (p.a == 0) continue;
                    float g = p.g / 255f, b = p.b / 255f;
                    Color c = Color.Lerp(library.ink, emoji.fill, g);
                    c = Color.Lerp(c, emoji.accent, b * (1f - g));
                    c = Color.Lerp(c, Color.white, g * b);
                    c.a = p.a / 255f;
                    drawn[y * cell.width + x] = c;
                    if (p.a < 16) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX) return null;

            // Trimmed, then fitted into the square with clear room round it.
            int trimmedWidth = maxX - minX + 1, trimmedHeight = maxY - minY + 1;
            var trimmed = new Texture2D(trimmedWidth, trimmedHeight, TextureFormat.RGBA32, true);
            var block = new Color[trimmedWidth * trimmedHeight];
            for (int y = 0; y < trimmedHeight; y++)
                System.Array.Copy(drawn, (minY + y) * cell.width + minX, block, y * trimmedWidth, trimmedWidth);
            trimmed.SetPixels(block);
            trimmed.Apply(true);
            trimmed.filterMode = FilterMode.Trilinear;
            trimmed.wrapMode = TextureWrapMode.Clamp;

            int size = library.size, room = size - library.padding * 2;
            float scale = Mathf.Min((float)room / trimmedWidth, (float)room / trimmedHeight);
            int outWidth = Mathf.Max(1, Mathf.RoundToInt(trimmedWidth * scale)), outHeight = Mathf.Max(1, Mathf.RoundToInt(trimmedHeight * scale));
            var target = RenderTexture.GetTemporary(outWidth, outHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Graphics.Blit(trimmed, target);
            RenderTexture.active = target;
            var scaled = new Texture2D(outWidth, outHeight, TextureFormat.RGBA32, false);
            scaled.ReadPixels(new Rect(0, 0, outWidth, outHeight), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);

            var icon = new Texture2D(size, size, TextureFormat.RGBA32, false);
            icon.SetPixels32(new Color32[size * size]);
            icon.SetPixels((size - outWidth) / 2, (size - outHeight) / 2, outWidth, outHeight, scaled.GetPixels());
            icon.Apply();

            string path = $"{EmojiFolder}/PetEmoji_{emoji.name}.png";
            File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), path), icon.EncodeToPNG());
            Object.DestroyImmediate(trimmed);
            Object.DestroyImmediate(scaled);
            Object.DestroyImmediate(icon);
            ImportAsSprite(path, size);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// The sheet an effect draws from and the cell of it that it shows: the particle system named like the
        /// effect, its material's texture, and the frame its texture sheet stands on.
        /// </summary>
        public static bool TryFindCell(GameObject source, out Texture sheet, out RectInt cell)
        {
            sheet = null;
            cell = default;
            if (source == null) return false;
            foreach (ParticleSystem system in source.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.gameObject.name != source.name) continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                ParticleSystem.TextureSheetAnimationModule frames = system.textureSheetAnimation;
                if (renderer == null || renderer.sharedMaterial == null || !frames.enabled) continue;
                foreach (int id in renderer.sharedMaterial.GetTexturePropertyNameIDs())
                {
                    sheet = renderer.sharedMaterial.GetTexture(id);
                    if (sheet != null) break;
                }
                if (sheet == null) continue;
                int columns = frames.numTilesX, rows = frames.numTilesY;
                int frame = Mathf.Clamp(Mathf.FloorToInt(frames.frameOverTime.constant * columns * rows + 0.0005f), 0, columns * rows - 1);
                int cellWidth = sheet.width / columns, cellHeight = sheet.height / rows;
                // Frames count from the sheet's top row; a texture's rows count from the bottom.
                cell = new RectInt(frame % columns * cellWidth, sheet.height - (frame / columns + 1) * cellHeight, cellWidth, cellHeight);
                return true;
            }
            return false;
        }

        /// <summary>The texture's pixels as they are stored (channels as data), read through a blit: its import settings are not touched.</summary>
        private static Color32[] ReadPixels(Texture source, out int width, out int height)
        {
            width = source.width;
            height = source.height;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture before = RenderTexture.active;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            var read = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            read.Apply();
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            Color32[] pixels = read.GetPixels32();
            Object.DestroyImmediate(read);
            return pixels;
        }

        private static void ImportAsSprite(string assetPath, int size)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null) return;
            bool right = importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single
                         && importer.alphaIsTransparency && !importer.mipmapEnabled && importer.maxTextureSize == size;
            if (right) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
