using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Wires the gear systems onto the player prefabs. Idempotent: safe to re-run after regenerating items.
    ///  - Player_01 (model): CharacterAppearance seeded from the Sidekick .sk recipe (fixed head/face parts, base-body
    ///    defaults for empty slots, body-shape weights) and a WeaponVisualMount on the katana mount.
    ///  - Player (network prefab): PlayerEquipment, PlayerInventory, EquipmentVisualBinder, PetFollower + catalog.
    /// </summary>
    public static class PlayerAppearanceSetup
    {
        private const string ModelPrefabPath = "Assets/Player_01/Player_01.prefab";
        private const string RecipePath = "Assets/Player_01/Player_01.sk";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string WeaponMountName = "KatanaBlade";

        [MenuItem("Krytical Nova/Items/3. Set Up Player Gear Components")]
        public static void Run()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null) { Debug.LogError("[Items] No ItemCatalog. Run 'Generate Test Gear' first."); return; }
            if (!SetUpModel()) return;
            SetUpPlayer(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[Items] Player gear components set up. Next: Krytical Nova/Items/4. Set Up Equipment Preview + HUD.");
        }

        private static bool SetUpModel()
        {
            var recipe = ReadRecipe(RecipePath, out var shape);
            if (recipe.Count == 0) { Debug.LogError($"[Items] No parts found in {RecipePath}."); return false; }

            var slotRegions = new HashSet<BodyRegion>(EquipSlots.All.SelectMany(EquipSlots.RegionsFor));
            var fixedParts = new List<SidekickPartRef>();
            foreach (var partName in recipe)
            {
                if (!SidekickGearGenerator.TryParsePart(partName, out var region) || slotRegions.Contains(region)) continue;
                string path = FindPartFbx(partName);
                if (path == null) { Debug.LogWarning($"[Items] Recipe part '{partName}' not found."); continue; }
                fixedParts.Add(SidekickGearGenerator.LoadPart(path, region));
            }

            // Empty-slot fallbacks: the plain Sidekick base body for every slot region it has a part for.
            var defaults = new List<SidekickPartRef>();
            foreach (var path in Directory.GetFiles(SidekickGearGenerator.HumanFolder, "SK_HUMN_BASE_01_*.fbx").Select(p => p.Replace('\\', '/')))
            {
                if (!SidekickGearGenerator.TryParsePart(Path.GetFileNameWithoutExtension(path), out var region)) continue;
                if (slotRegions.Contains(region)) defaults.Add(SidekickGearGenerator.LoadPart(path, region));
            }

            var root = PrefabUtility.LoadPrefabContents(ModelPrefabPath);
            try
            {
                var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == "mesh");
                if (smr == null) smr = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
                var appearance = Ensure<CharacterAppearance>(root);
                appearance.EditorConfigure(smr, fixedParts.Where(p => p.IsValid).ToList(), defaults.Where(p => p.IsValid).ToList(), shape);

                var mount = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == WeaponMountName);
                if (mount != null)
                {
                    if (mount.GetComponent<WeaponVisualMount>() == null) mount.gameObject.AddComponent<WeaponVisualMount>();
                }
                else Debug.LogWarning($"[Items] '{WeaponMountName}' not found on {ModelPrefabPath}; weapons won't swap.");

                PrefabUtility.SaveAsPrefabAsset(root, ModelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[Items] Player_01: {fixedParts.Count} fixed parts, {defaults.Count} base-body defaults, " +
                      $"shape buff {shape.buff} heavy {shape.heavy} skinny {shape.skinny} fem {shape.masculineFeminine}.");
            return true;
        }

        private static void SetUpPlayer(ItemCatalog catalog)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var equipment = Ensure<PlayerEquipment>(root);
                var inventory = Ensure<PlayerInventory>(root);
                Ensure<PetFollower>(root);
                Ensure<EquipmentVisualBinder>(root);
                SetField(equipment, "catalog", catalog);
                SetField(inventory, "catalog", catalog);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // Explicit Unity null check: in the Editor a missing GetComponent result is a "fake null", which ?? doesn't catch.
        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string FindPartFbx(string partName)
        {
            foreach (var folder in new[] { SidekickGearGenerator.OutfitFolder, SidekickGearGenerator.HumanFolder })
            {
                string path = $"{folder}/{partName}.fbx";
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>Minimal reader for the Sidekick .sk YAML: part names plus the body-shape sliders.</summary>
        public static List<string> ReadRecipe(string path, out BodyShapeWeights shape)
        {
            var parts = new List<string>();
            float bodyType = 50f, bodySize = 0f, muscle = 50f;
            bool inParts = false;
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (raw.StartsWith("Parts:")) { inParts = true; continue; }
                if (!raw.StartsWith(" ") && !raw.StartsWith("-") && raw.Length > 0 && !raw.StartsWith("Parts:")) inParts = false;
                if (inParts && line.StartsWith("- Name:")) parts.Add(line.Substring("- Name:".Length).Trim());
                if (line.StartsWith("BodyTypeValue:")) bodyType = ParseValue(line);
                else if (line.StartsWith("BodySizeValue:")) bodySize = ParseValue(line);
                else if (line.StartsWith("MuscleValue:")) muscle = ParseValue(line);
            }
            // Same mapping as SidekickRuntime / ModularCharacterWindow.
            shape = new BodyShapeWeights
            {
                masculineFeminine = (bodyType + 100f) / 2f,
                heavy = bodySize > 0f ? bodySize : 0f,
                skinny = bodySize < 0f ? -bodySize : 0f,
                buff = (muscle + 100f) / 2f
            };
            return parts;
        }

        private static float ParseValue(string line) =>
            float.Parse(line.Substring(line.IndexOf(':') + 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
