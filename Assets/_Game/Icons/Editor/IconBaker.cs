using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes the icons the game shows out of the icon packs' pictures: reads a pack's file (never changing it, nor its
    /// import settings), redraws it in the comic style (<see cref="ComicIconTreatment"/>), writes the copy into the
    /// owning system's icon folder and hands it to the skill or item the library gave it to.
    /// </summary>
    public static class IconBaker
    {
        public const string SkillIconFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Icons";
        public const string ItemIconFolder = "Assets/_Game/Gameplay/Items/Data/Assets/Icons";

        private static readonly Regex NumberAtEnd = new Regex(@"_(\d+)\.png$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Dictionary<string, Dictionary<int, string>> Folders = new Dictionary<string, Dictionary<int, string>>();

        /// <summary>The settings asset, made with its defaults when the project has none.</summary>
        public static IconTreatmentSettings Settings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<IconTreatmentSettings>(IconTreatmentSettings.AssetPath);
            if (settings != null) return settings;
            WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(IconTreatmentSettings.AssetPath).Replace('\\', '/'));
            settings = ScriptableObject.CreateInstance<IconTreatmentSettings>();
            AssetDatabase.CreateAsset(settings, IconTreatmentSettings.AssetPath);
            return settings;
        }

        /// <summary>The library asset, made empty when the project has none.</summary>
        public static IconLibrary Library()
        {
            var library = AssetDatabase.LoadAssetAtPath<IconLibrary>(IconLibrary.AssetPath);
            if (library != null) return library;
            WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(IconLibrary.AssetPath).Replace('\\', '/'));
            library = ScriptableObject.CreateInstance<IconLibrary>();
            AssetDatabase.CreateAsset(library, IconLibrary.AssetPath);
            return library;
        }

        // ---------------------------------------------------------------- the packs' files

        /// <summary>Forgets which files the sets' folders hold (after a pack is added or changed).</summary>
        public static void ForgetFolders() => Folders.Clear();

        /// <summary>The numbered pictures of a folder: number → asset path. The number is the one that ends the file's name.</summary>
        public static Dictionary<int, string> FilesOf(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return new Dictionary<int, string>();
            if (Folders.TryGetValue(folder, out Dictionary<int, string> known)) return known;
            known = new Dictionary<int, string>();
            if (Directory.Exists(folder))
            {
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    Match match = NumberAtEnd.Match(file);
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int number)) known[number] = file.Replace('\\', '/');
                }
            }
            Folders[folder] = known;
            return known;
        }

        public static string FileOf(IconLibrary.Set set, int number) =>
            set != null && FilesOf(set.folder).TryGetValue(number, out string path) ? path : null;

        // ---------------------------------------------------------------- the treatment

        /// <summary>
        /// The treated icon as pixels (rows from the bottom up, as a texture holds them), or null when the pack's file
        /// is missing or cannot be read.
        /// </summary>
        public static Color32[] Treat(IconLibrary.Set set, int number, IconTreatmentSettings settings, out int size)
        {
            size = 0;
            string path = FileOf(set, number);
            if (path == null || settings == null) return null;
            // Read from the file itself: the pack's import settings are neither used nor touched.
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(path), false)) return null;
                Color32[] pixels = source.GetPixels32();
                int width = source.width, height = source.height;
                var rows = new byte[width * height * 4];
                for (int y = 0; y < height; y++)
                {
                    int from = (height - 1 - y) * width, to = y * width * 4;
                    for (int x = 0; x < width; x++)
                    {
                        Color32 c = pixels[from + x];
                        rows[to + x * 4] = c.r; rows[to + x * 4 + 1] = c.g; rows[to + x * 4 + 2] = c.b; rows[to + x * 4 + 3] = c.a;
                    }
                }
                ComicIconLook look = settings.ToLook(set);
                byte[] treated = ComicIconTreatment.Treat(rows, width, height, set.shape, look);
                size = look.size;
                var result = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    int from = (size - 1 - y) * size * 4, to = y * size;
                    for (int x = 0; x < size; x++)
                        result[to + x] = new Color32(treated[from + x * 4], treated[from + x * 4 + 1], treated[from + x * 4 + 2], treated[from + x * 4 + 3]);
                }
                return result;
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        /// <summary>The treated icon as a texture, for showing in a window. The caller destroys it.</summary>
        public static Texture2D Preview(IconLibrary.Set set, int number, IconTreatmentSettings settings)
        {
            Color32[] pixels = Treat(set, number, settings, out int size);
            if (pixels == null) return null;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "Icon preview" };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // ---------------------------------------------------------------- baking

        /// <summary>Where the treated copy of a skill's or an item's icon is written: the owning system's icon folder.</summary>
        public static string OutputPath(IconTargetKind kind, string target) =>
            $"{(kind == IconTargetKind.Item ? ItemIconFolder : SkillIconFolder)}/{target}.png";

        /// <summary>Bakes one assignment into its icon file and returns the sprite, or null (and a warning) when it cannot.</summary>
        public static Sprite Bake(IconLibrary library, IconTreatmentSettings settings, IconLibrary.Assignment assignment)
        {
            IconLibrary.Set set = library.FindSet(assignment.set);
            Color32[] pixels = Treat(set, assignment.number, settings, out int size);
            if (pixels == null)
            {
                Debug.LogWarning($"[Icons] {assignment.target}: {assignment.set} {assignment.number:00} could not be read. Is the icon pack in the project?");
                return null;
            }
            string output = OutputPath(assignment.kind, assignment.target);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            string full = Path.Combine(Directory.GetCurrentDirectory(), output);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            ImportAsSprite(output, size);
            return AssetDatabase.LoadAssetAtPath<Sprite>(output);
        }

        /// <summary>
        /// Bakes every assignment of the library and hands each icon to its skill or item. Returns how many were
        /// baked; assignments whose skill or item no longer exists are reported and left alone.
        /// </summary>
        public static int BakeAll(IconLibrary library, IconTreatmentSettings settings)
        {
            var targets = new Dictionary<(IconTargetKind, string), IconTarget>();
            foreach (IconTarget target in IconTargets.Skills()) targets[(target.kind, target.id)] = target;
            foreach (IconTarget target in IconTargets.Items()) targets[(target.kind, target.id)] = target;

            int baked = 0;
            try
            {
                for (int i = 0; i < library.assignments.Count; i++)
                {
                    IconLibrary.Assignment assignment = library.assignments[i];
                    if (assignment == null) continue;
                    EditorUtility.DisplayProgressBar("Baking icons", assignment.target, (float)i / library.assignments.Count);
                    if (!targets.TryGetValue((assignment.kind, assignment.target), out IconTarget target))
                    {
                        Debug.LogWarning($"[Icons] The library has an icon for '{assignment.target}', but there is no such {assignment.kind} in the project.");
                        continue;
                    }
                    Sprite sprite = Bake(library, settings, assignment);
                    if (sprite == null) continue;
                    if (target.Icon != sprite) target.Icon = sprite;
                    baked++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            return baked;
        }

        /// <summary>Bakes the icon one target was given and hands it over. False when the library has none for it.</summary>
        public static bool BakeFor(IconLibrary library, IconTreatmentSettings settings, IconTarget target)
        {
            IconLibrary.Assignment assignment = library.AssignmentOf(target.kind, target.id);
            if (assignment == null) return false;
            Sprite sprite = Bake(library, settings, assignment);
            if (sprite == null) return false;
            target.Icon = sprite;
            AssetDatabase.SaveAssetIfDirty(target.asset);
            return true;
        }

        private static void ImportAsSprite(string assetPath, int size)
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            if (importer == null) return;
            bool right = importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single
                         && importer.alphaIsTransparency && !importer.mipmapEnabled && importer.maxTextureSize == size
                         && importer.wrapMode == TextureWrapMode.Clamp && importer.textureCompression == TextureImporterCompression.CompressedHQ;
            if (right) return; // re-baked over an icon already set up: nothing to change
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
