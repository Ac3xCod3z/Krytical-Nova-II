using System.Collections.Generic;
using System.IO;
using KryticalNova.Items;
using KryticalNova.Mounts;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the mounts' icons the way every gear icon is drawn (<see cref="ItemIconCapture"/>: the preview renderer's
    /// comic pass, from the front-left; owner, 25 Oct 2026: "renders that are generated just like the icons are for
    /// gear"). Each growth stage of a built mount is pictured from its own model and gets its stage's pips along the
    /// bottom (one to three, in the stage's glow colour) as Mount_&lt;Mount&gt;_S&lt;n&gt;.png, written onto the stage
    /// (<see cref="MountStageLook.icon"/>): the Mount window shows the one for the mount as it is now. The mount's item
    /// gets a picture of its first stage without pips (Mount_&lt;Mount&gt;.png), so no icon is used twice.
    /// Menu Krytical Nova ▸ Items ▸ Bake Mount Icons. Run it again after a stage's model changes, or for a new mount.
    /// </summary>
    public static class MountStageIconBaker
    {
        public const string IconFolder = "Assets/_Game/Gameplay/Mounts/Data/Assets/Icons";

        public static string StagePath(MountDefinition mount, int stage) => $"{IconFolder}/Mount_{Key(mount)}_S{stage + 1}.png";
        public static string ItemIconPath(string key) => $"{ItemIconCapture.IconFolder}/Mount_{key}.png";
        private static string Key(MountDefinition mount) => mount.name.Replace("MountDef_", "");

        [MenuItem("Krytical Nova/Items/Bake Mount Icons")]
        public static void Bake()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MountCatalog>(MountAssetBuilder.CatalogPath);
            if (catalog == null) { Debug.LogWarning("[Mounts] No mount catalog: run Krytical Nova/Items/11. Build Mounts first."); return; }

            var subjects = new List<(GameObject model, string path)>();
            var owners = new List<(MountDefinition mount, int stage)>();
            foreach (MountDefinition mount in catalog.Mounts)
            {
                if (mount == null || mount.stages == null) continue;
                for (int stage = 0; stage < mount.stages.Length; stage++)
                {
                    if (mount.stages[stage].model == null) continue;
                    subjects.Add((mount.stages[stage].model, StagePath(mount, stage)));
                    owners.Add((mount, stage));
                    if (stage == 0 && mount.itemId != 0)
                    {
                        subjects.Add((mount.stages[0].model, ItemIconPath(Key(mount))));
                        owners.Add((mount, -1));
                    }
                }
            }
            if (subjects.Count == 0) { Debug.Log("[Mounts] No mount stage has a model to picture: nothing to bake."); return; }

            WeatherVfxBuilder.EnsureFolder(IconFolder);
            List<Sprite> icons = ItemIconCapture.CaptureModels(subjects);
            for (int i = 0; i < owners.Count && i < icons.Count; i++)
            {
                (MountDefinition mount, int stage) = owners[i];
                if (icons[i] != null && stage >= 0) AddPips(subjects[i].path, stage + 1, mount.stages[stage].glow);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            int given = 0;
            for (int i = 0; i < owners.Count; i++)
            {
                (MountDefinition mount, int stage) = owners[i];
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(subjects[i].path);
                if (icon == null) continue;
                if (stage < 0)
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { SidekickGearGenerator.ItemsRoot + "/Mounts" }))
                    {
                        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                        if (item == null || item.itemId != mount.itemId) continue;
                        item.icon = icon;
                        EditorUtility.SetDirty(item);
                    }
                }
                else
                {
                    MountStageLook look = mount.stages[stage];
                    look.icon = icon;
                    mount.stages[stage] = look;
                    EditorUtility.SetDirty(mount);
                }
                given++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Mounts] {given} mount icon(s) baked: a stage icon for every stage with a model, and each mount item's own.");
        }

        /// <summary>The stage's pips along the bottom, filled with its glow and ringed in the comic ink (as the pets' stages).</summary>
        private static void AddPips(string path, int count, Color glow)
        {
            if (!File.Exists(path)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(texture); return; }
            int size = Mathf.Min(texture.width, texture.height);
            float radius = size * 0.045f, ring = size * 0.016f, gap = size * 0.035f;
            float rowWidth = count * radius * 2f + (count - 1) * gap;
            float x0 = (texture.width - rowWidth) * 0.5f + radius;
            float y = size * 0.11f;
            Color fill = glow.maxColorComponent > 0.05f ? glow : Color.white;
            fill.a = 1f;
            var ink = new Color(0.08f, 0.07f, 0.17f, 1f);
            Color[] pixels = texture.GetPixels();
            for (int p = 0; p < count; p++)
            {
                float cx = x0 + p * (radius * 2f + gap);
                int left = Mathf.FloorToInt(cx - radius - ring - 1), right = Mathf.CeilToInt(cx + radius + ring + 1);
                int bottom = Mathf.FloorToInt(y - radius - ring - 1), top = Mathf.CeilToInt(y + radius + ring + 1);
                for (int py = Mathf.Max(0, bottom); py < Mathf.Min(texture.height, top); py++)
                for (int px = Mathf.Max(0, left); px < Mathf.Min(texture.width, right); px++)
                {
                    float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(cx, y));
                    float inFill = Mathf.Clamp01(radius - d + 0.5f);
                    float inAll = Mathf.Clamp01(radius + ring - d + 0.5f);
                    if (inAll <= 0f) continue;
                    int index = py * texture.width + px;
                    Color under = pixels[index];
                    Color over = Color.Lerp(ink, fill, inFill);
                    pixels[index] = Color.Lerp(under, over, inAll);
                    pixels[index].a = Mathf.Max(under.a, inAll);
                }
            }
            texture.SetPixels(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
