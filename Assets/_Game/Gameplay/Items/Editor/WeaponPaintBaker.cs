using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Gives every weapon model its <see cref="WeaponPaint"/>: the paints a dye changes. A weapon's mesh points into
    /// its pack's palette texture; the baker reads which colours of it the model uses and how much of its surface
    /// each covers, and <see cref="WeaponPaintRules.Bands"/> turns them into paints and
    /// <see cref="WeaponPaintRules.Shades"/> its greys into its metals. It also reads the pack's emission map at the
    /// same places, so what the model lights up is known as its glow. A model whose paints were set by hand
    /// (<see cref="WeaponPaint.keepAsSet"/>) is left alone.
    /// </summary>
    public static class WeaponPaintBaker
    {
        private const int ReadSize = 2048;
        private static readonly string[] TextureProperties = { "_Albedo_Map", "_BaseMap", "_MainTex" };
        private static readonly string[] LightTextureProperties = { "_Emission_Map", "_EmissionMap" };

        [MenuItem("Krytical Nova/Items/12. Bake Weapon Paints (for weapon dye)")]
        public static void Bake()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                Debug.LogError("[WeaponPaint] No item catalog: run Krytical Nova/Items/1 first.");
                return;
            }

            var models = new List<GameObject>();
            foreach (ItemDefinition item in catalog.Items)
                if (item != null && item.equipSlot == EquipSlot.Weapon && item.weaponPrefab != null && !models.Contains(item.weaponPrefab))
                    models.Add(item.weaponPrefab);

            var palettes = new Dictionary<Texture, Color32[]>();
            int written = 0, kept = 0;
            var report = new System.Text.StringBuilder();
            foreach (GameObject model in models)
            {
                string path = AssetDatabase.GetAssetPath(model);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/_Game/"))
                {
                    report.AppendLine($"  {model.name}: not one of our model prefabs ({path}), left alone");
                    continue;
                }
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!root.TryGetComponent(out WeaponPaint paint)) paint = root.AddComponent<WeaponPaint>();
                    if (paint.keepAsSet)
                    {
                        kept++;
                        report.AppendLine($"  {root.name}: paints kept as set by hand");
                        continue;
                    }
                    List<WeaponPaintRules.Sample> samples = Samples(root, palettes, out float grey);
                    paint.bands = WeaponPaintRules.Bands(samples);
                    paint.shades = WeaponPaintRules.Shades(samples, paint.bands);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    written++;
                    var roles = new System.Text.StringBuilder();
                    foreach (WeaponPaintBand band in paint.bands) roles.Append($" {band.role}@{Mathf.RoundToInt(band.hue * 360f)}°");
                    var metals = new System.Text.StringBuilder();
                    foreach (WeaponPaintShade shade in paint.shades)
                        metals.Append($" {(shade.role == WeaponPaintRole.Keep ? "Metal" : shade.role.ToString())}@{Mathf.RoundToInt(shade.value * 100f)}%");
                    report.AppendLine($"  {root.name}: {paint.bands.Length} paint(s){roles}; {paint.shades.Length} metal(s){metals}; {Mathf.RoundToInt(grey * 100f)}% metal or dark");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[WeaponPaint] {written} weapon models given their paints, {kept} kept as set by hand.\n{report}");
        }

        /// <summary>The colours a model's triangles take from its palette, weighted by their area.</summary>
        private static List<WeaponPaintRules.Sample> Samples(GameObject root, Dictionary<Texture, Color32[]> palettes, out float greyShare)
        {
            var byColour = new Dictionary<int, float>();
            var litByColour = new Dictionary<int, float>();
            float total = 0f, grey = 0f;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                    : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Vector3[] vertices = mesh.vertices;
                Vector2[] uvs = mesh.uv;
                if (uvs == null || uvs.Length != vertices.Length) continue;
                Material[] materials = renderer.sharedMaterials;
                Vector3 scale = renderer.transform.lossyScale;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material material = sub < materials.Length ? materials[sub] : null;
                    Color32[] pixels = PaletteOf(material, palettes);
                    if (pixels == null) continue;
                    Color32[] lights = LightsOf(material, palettes);
                    int[] triangles = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        Vector3 ab = Vector3.Scale(vertices[b] - vertices[a], scale), ac = Vector3.Scale(vertices[c] - vertices[a], scale);
                        float area = Vector3.Cross(ab, ac).magnitude * 0.5f;
                        if (area <= 0f) continue;
                        Vector2 uv = (uvs[a] + uvs[b] + uvs[c]) / 3f;
                        int x = Mathf.Clamp((int)(Mathf.Repeat(uv.x, 1f) * ReadSize), 0, ReadSize - 1);
                        int y = Mathf.Clamp((int)(Mathf.Repeat(uv.y, 1f) * ReadSize), 0, ReadSize - 1);
                        Color32 colour = pixels[y * ReadSize + x];
                        total += area;
                        Color.RGBToHSV(colour, out _, out float s, out float v);
                        if (s < WeaponPaintRules.GreySaturation || v < WeaponPaintRules.DarkValue) grey += area;
                        int key = colour.r << 16 | colour.g << 8 | colour.b;
                        byColour[key] = byColour.TryGetValue(key, out float had) ? had + area : area;
                        if (lights == null) continue;
                        Color32 light = lights[y * ReadSize + x];
                        if (Mathf.Max(light.r, Mathf.Max(light.g, light.b)) / 255f < WeaponPaintRules.LightFloor) continue;
                        litByColour[key] = litByColour.TryGetValue(key, out float lit) ? lit + area : area;
                    }
                }
            }
            greyShare = total > 0f ? grey / total : 0f;
            var samples = new List<WeaponPaintRules.Sample>(byColour.Count);
            foreach (KeyValuePair<int, float> pair in byColour)
                samples.Add(new WeaponPaintRules.Sample(new Color32((byte)(pair.Key >> 16), (byte)(pair.Key >> 8), (byte)pair.Key, 255), pair.Value,
                    litByColour.TryGetValue(pair.Key, out float lit) && lit * 2f > pair.Value));
            return samples;
        }

        /// <summary>A material's emission map, when the material lights anything up, read the same way as its palette. Null when it does not.</summary>
        private static Color32[] LightsOf(Material material, Dictionary<Texture, Color32[]> palettes)
        {
            if (material == null) return null;
            bool on = material.HasProperty("_Enable_Emission") ? material.GetFloat("_Enable_Emission") > 0.5f : material.IsKeywordEnabled("_EMISSION");
            if (!on) return null;
            foreach (string name in LightTextureProperties)
            {
                if (!material.HasProperty(name)) continue;
                Texture lights = material.GetTexture(name);
                if (lights != null) return PixelsOf(lights, palettes);
            }
            return null;
        }

        /// <summary>A material's palette texture, drawn and read back: the packs' textures are not readable.</summary>
        private static Color32[] PaletteOf(Material material, Dictionary<Texture, Color32[]> palettes)
        {
            if (material == null) return null;
            Texture palette = null;
            foreach (string name in TextureProperties)
            {
                if (!material.HasProperty(name)) continue;
                palette = material.GetTexture(name);
                if (palette != null) break;
            }
            return palette != null ? PixelsOf(palette, palettes) : null;
        }

        private static Color32[] PixelsOf(Texture palette, Dictionary<Texture, Color32[]> palettes)
        {
            if (palettes.TryGetValue(palette, out Color32[] pixels)) return pixels;

            RenderTexture target = RenderTexture.GetTemporary(ReadSize, ReadSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture before = RenderTexture.active;
            Graphics.Blit(palette, target);
            RenderTexture.active = target;
            var copy = new Texture2D(ReadSize, ReadSize, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, ReadSize, ReadSize), 0, 0);
            RenderTexture.active = before;
            RenderTexture.ReleaseTemporary(target);
            pixels = copy.GetPixels32();
            Object.DestroyImmediate(copy);
            palettes[palette] = pixels;
            return pixels;
        }
    }
}
