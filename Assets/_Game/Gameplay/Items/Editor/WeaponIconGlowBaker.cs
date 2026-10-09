using System.Collections.Generic;
using System.IO;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the glow rim of every weapon icon: a white picture, the same size as the icon, of the outline of the
    /// part of the weapon that glows when it is tempered, which the interface lays behind the icon and tints with
    /// the colour of the piece's Temper band (<see cref="ItemGlowIcons"/>). The part is the one the weapon itself
    /// glows on in the world (its <see cref="WeaponGlowZone"/>: the blade and not the hilt, an axe's head and not
    /// its shaft), found in the icon from the way the icon was framed. One rim an icon serves every band. The icons
    /// themselves are only read. Run it again after weapon icons are captured anew or glow zones are re-baked.
    /// </summary>
    public static class WeaponIconGlowBaker
    {
        public const string RimFolder = ItemIconCapture.IconFolder + "/Glow";
        public const string LibraryPath = SidekickGearGenerator.ItemsRoot + "/ItemGlowIcons.asset";

        // The rim as shares of the icon's width: how far it reaches out from the glowing part, and how soft its edge is.
        private const float Outside = 0.05f, Soften = 0.012f;
        private const float AlphaCut = 0.35f;

        [MenuItem("Krytical Nova/Items/6. Bake Weapon Glow Rims")]
        public static void Bake()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            var settings = AssetDatabase.LoadAssetAtPath<GearProgressionSettings>(PlayerSystemsSetup.GearProgressionPath);
            if (catalog == null || settings == null)
            {
                Debug.LogError("[Items] The item catalog or the gear progression settings are missing.");
                return;
            }

            // Items share icons (one look for a run of levels): the first weapon that shows an icon stands for it.
            var icons = new List<Sprite>();
            var weapons = new Dictionary<Sprite, ItemDefinition>();
            foreach (ItemDefinition item in catalog.Items)
            {
                if (item == null || item.equipSlot != EquipSlot.Weapon || item.icon == null || weapons.ContainsKey(item.icon)) continue;
                icons.Add(item.icon);
                weapons[item.icon] = item;
            }

            WeatherVfxBuilder.EnsureFolder(RimFolder);
            var written = new List<(Sprite icon, string path)>();
            foreach (Sprite icon in icons)
            {
                string source = AssetDatabase.GetAssetPath(icon);
                if (string.IsNullOrEmpty(source) || !File.Exists(source)) continue;
                var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!picture.LoadImage(File.ReadAllBytes(source))) { Object.DestroyImmediate(picture); continue; }
                Texture2D rim = Rim(picture, GlowingPart(weapons[icon], picture.width, picture.height));
                string path = $"{RimFolder}/{Path.GetFileNameWithoutExtension(source)}_Rim.png";
                File.WriteAllBytes(path, rim.EncodeToPNG());
                Object.DestroyImmediate(picture);
                Object.DestroyImmediate(rim);
                written.Add((icon, path));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var entries = new List<ItemGlowIcons.Entry>(written.Count);
            foreach ((Sprite icon, string path) in written)
            {
                ImportAsSprite(path);
                var rim = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (rim != null) entries.Add(new ItemGlowIcons.Entry { icon = icon, rim = rim });
            }

            var library = AssetDatabase.LoadAssetAtPath<ItemGlowIcons>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<ItemGlowIcons>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.SetContents(entries.ToArray());
            EditorUtility.SetDirty(library);
            if (settings.iconGlows != library)
            {
                settings.iconGlows = library;
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {entries.Count} weapon glow rims baked into {RimFolder} ({LibraryPath}).");
        }

        /// <summary>
        /// Which pixels of a weapon's icon show the part of it that glows, or null when all of it does. The icon
        /// capture looks straight down the world's z at the weapon turned onto the icon's diagonal, framed to its
        /// bounds (ItemIconCapture): the same placement is made here, and each pixel's line of sight is tried
        /// against the glow zone's box in the weapon's own space, as the world's glow tries each point of the mesh.
        /// </summary>
        private static bool[] GlowingPart(ItemDefinition item, int width, int height)
        {
            if (item == null || item.weaponPrefab == null) return null;
            if (!item.weaponPrefab.TryGetComponent(out WeaponGlowZone part)) return null;
            GlowZone zone = part.zone;
            if (zone.mode == GlowZoneMode.Whole && !zone.HasDark) return null;

            var root = new GameObject("Glow Rim Subject") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var weapon = (GameObject)Object.Instantiate(item.weaponPrefab, root.transform, false);
                weapon.transform.localRotation = Quaternion.LookRotation(new Vector3(1f, 1f, 0f).normalized, new Vector3(-1f, 1f, 0f).normalized);
                Bounds bounds = SidekickGearGenerator.LocalBounds(root.transform);
                float size = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f; // the capture's orthographic half-height
                Matrix4x4 toWeapon = weapon.transform.worldToLocalMatrix;
                Vector3 direction = toWeapon.MultiplyVector(Vector3.forward);

                var glowing = new bool[width * height];
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        var origin = new Vector3(bounds.center.x + ((x + 0.5f) / width - 0.5f) * 2f * size,
                            bounds.center.y + ((y + 0.5f) / height - 0.5f) * 2f * size, bounds.center.z - 100f);
                        var ray = new Ray(toWeapon.MultiplyPoint3x4(origin), direction);
                        bool lit = zone.mode == GlowZoneMode.Whole || (zone.mode == GlowZoneMode.Only && zone.box.IntersectRay(ray));
                        if (lit && zone.HasDark && zone.dark.IntersectRay(ray)) lit = false;
                        glowing[y * width + x] = lit;
                    }
                return glowing;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The outline of the glowing part of a picture's silhouette (all of it when <paramref name="glowing"/> is
        /// null) as a soft white band reaching outwards. Shown behind the icon, only what lies outside the icon's
        /// own shape is seen: a rim, never a tint over the weapon.
        /// </summary>
        public static Texture2D Rim(Texture2D picture, bool[] glowing = null)
        {
            int width = picture.width, height = picture.height;
            Color32[] pixels = picture.GetPixels32();
            var solid = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
                solid[i] = pixels[i].a / 255f >= AlphaCut && (glowing == null || glowing[i]) ? 1f : 0f;

            float[] band = Spread(solid, width, height, Mathf.Max(1, Mathf.RoundToInt(width * Outside)), grow: true);
            band = Blur(band, width, height, Mathf.Max(1, Mathf.RoundToInt(width * Soften)));

            var output = new Color32[pixels.Length];
            for (int i = 0; i < output.Length; i++) output[i] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(band[i]) * 255f));
            var rim = new Texture2D(width, height, TextureFormat.RGBA32, false);
            rim.SetPixels32(output);
            rim.Apply();
            return rim;
        }

        /// <summary>The silhouette grown (or shrunk) by a radius, with a soft falloff towards the far end of it.</summary>
        private static float[] Spread(float[] solid, int width, int height, int radius, bool grow)
        {
            var across = new float[solid.Length];
            var result = new float[solid.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                float[] from = pass == 0 ? solid : across, to = pass == 0 ? across : result;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float best = grow ? 0f : 1f;
                        for (int d = -radius; d <= radius; d++)
                        {
                            int sx = pass == 0 ? x + d : x, sy = pass == 0 ? y : y + d;
                            float value = sx < 0 || sy < 0 || sx >= width || sy >= height ? 0f : from[sy * width + sx];
                            if (grow)
                            {
                                // Fades with distance, so the rim is strongest at the outline.
                                value *= 1f - 0.6f * Mathf.Abs(d) / radius;
                                if (value > best) best = value;
                            }
                            else if (value < best) best = value;
                        }
                        to[y * width + x] = best;
                    }
            }
            return result;
        }

        private static float[] Blur(float[] source, int width, int height, int radius)
        {
            var across = new float[source.Length];
            var result = new float[source.Length];
            for (int pass = 0; pass < 2; pass++)
            {
                float[] from = pass == 0 ? source : across, to = pass == 0 ? across : result;
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int d = -radius; d <= radius; d++)
                        {
                            int sx = pass == 0 ? x + d : x, sy = pass == 0 ? y : y + d;
                            if (sx < 0 || sy < 0 || sx >= width || sy >= height) continue;
                            sum += from[sy * width + sx];
                            count++;
                        }
                        to[y * width + x] = count > 0 ? sum / count : 0f;
                    }
            }
            return result;
        }

        private static void ImportAsSprite(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) return;
            if (importer.textureType == TextureImporterType.Sprite && importer.alphaIsTransparency && !importer.mipmapEnabled) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
    }
}
