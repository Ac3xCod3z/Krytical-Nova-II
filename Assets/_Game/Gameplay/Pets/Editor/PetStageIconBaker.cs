using System.Collections.Generic;
using System.IO;
using KryticalNova.Items;
using KryticalNova.Pets;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes an icon for every evolution of every pet (menu Krytical Nova ▸ Items ▸ Bake Pet Stage Icons; owner,
    /// 22 Oct 2026: "make sure all pets have evolution icons"). A stage with a model of its own is pictured from it; a
    /// stage whose model is empty wears the pet item's own model, and is pictured from that. The pictures are drawn as
    /// every item icon is (<see cref="ItemIconCapture"/>: the preview renderer's comic look, from the front-left), and
    /// each then gets its stage's pips along the bottom (one to five, in the stage's glow colour), so no two stages of
    /// a pet ever show the same picture even when they share a model. They go into <see cref="IconFolder"/> as
    /// Pet_&lt;Pet&gt;_S&lt;stage&gt;.png and onto the stage (<see cref="PetStageLook.icon"/>), where the interface
    /// finds the one for the pet as it is now. Run it again after a stage's model or glow changes, or for a new pet.
    /// </summary>
    public static class PetStageIconBaker
    {
        public const string IconFolder = "Assets/_Game/Gameplay/Pets/Data/Assets/Icons";
        private const string PetRoot = "Assets/_Game/Gameplay/Pets/Data/Assets";
        private const string ItemRoot = "Assets/_Game/Gameplay/Items/Data/Assets";

        [MenuItem("Krytical Nova/Items/Bake Pet Stage Icons")]
        public static void Bake()
        {
            var petModels = new Dictionary<ushort, GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemRoot }))
            {
                var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (item != null && item.petPrefab != null) petModels[item.itemId] = item.petPrefab;
            }

            var subjects = new List<(GameObject model, string path)>();
            var owners = new List<(PetDefinition pet, int stage)>();
            foreach (string guid in AssetDatabase.FindAssets("t:PetDefinition", new[] { PetRoot }))
            {
                var pet = AssetDatabase.LoadAssetAtPath<PetDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (pet == null || pet.stages == null) continue;
                petModels.TryGetValue(pet.itemId, out GameObject itemModel);
                for (int stage = 0; stage < pet.stages.Length; stage++)
                {
                    GameObject model = pet.stages[stage].model != null ? pet.stages[stage].model : itemModel;
                    if (model == null)
                    {
                        Debug.LogWarning($"[Pets] {pet.displayName} stage {stage + 1} has no model and its item {pet.itemId} has none: no icon.");
                        continue;
                    }
                    subjects.Add((model, StagePath(pet, stage)));
                    owners.Add((pet, stage));
                }
            }
            if (subjects.Count == 0)
            {
                Debug.Log("[Pets] No pet stage has a model to picture: nothing to bake.");
                return;
            }

            WeatherVfxBuilder.EnsureFolder(IconFolder);
            List<Sprite> icons = ItemIconCapture.CaptureModels(subjects);
            for (int i = 0; i < owners.Count && i < icons.Count; i++)
            {
                if (icons[i] == null) continue;
                (PetDefinition pet, int stage) = owners[i];
                AddPips(subjects[i].path, stage + 1, pet.stages[stage].glow);
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            int given = 0;
            for (int i = 0; i < owners.Count; i++)
            {
                (PetDefinition pet, int stage) = owners[i];
                var icon = AssetDatabase.LoadAssetAtPath<Sprite>(subjects[i].path);
                if (icon == null) continue;
                PetStageLook look = pet.stages[stage];
                look.icon = icon;
                pet.stages[stage] = look;
                EditorUtility.SetDirty(pet);
                given++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pets] {given} stage icon(s) baked into {IconFolder}, every stage of every pet.");
        }

        public static string StagePath(PetDefinition pet, int stage) => $"{IconFolder}/{pet.name.Replace("PetDef_", "Pet_")}_S{stage + 1}.png";

        /// <summary>
        /// Draws <paramref name="count"/> round pips in a row along the bottom of the picture, filled with the stage's
        /// glow and ringed in ink like the comic outline, so the stage reads at a glance.
        /// </summary>
        private static void AddPips(string path, int count, Color glow)
        {
            if (!File.Exists(path)) return;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(texture); return; }
            int size = Mathf.Min(texture.width, texture.height);
            float radius = size * 0.04f, ring = size * 0.016f, gap = size * 0.03f;
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
                    // Soft edges: fill inside the radius, ink ring round it, faded out over a pixel.
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
