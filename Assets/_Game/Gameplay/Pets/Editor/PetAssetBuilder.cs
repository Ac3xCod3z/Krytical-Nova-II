using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the pets from one table (design: _Docs/Design/12_pet_evolution_system.md): the item that is each pet,
    /// its definition (stat, maximum, five stage looks), the pet catalog and the pet settings. Every pet is Common.
    /// Re-runnable. What the table owns is rewritten on every run (name, text, rarity, which model it has, whether it
    /// stands or bobs);
    /// numbers and stage looks are only filled in when missing, so values tuned in the Inspector survive.
    /// Also carries out the move from the old drone pets: assets and icons are renamed in place (ids and
    /// references kept) and retired pets are removed.
    /// </summary>
    public static class PetAssetBuilder
    {
        public const string PetsRoot = "Assets/_Game/Gameplay/Pets/Data/Assets";
        public const string SettingsPath = PetsRoot + "/PetSettings.asset";
        public const string CatalogPath = PetsRoot + "/PetCatalog.asset";
        private const string ItemFolder = SidekickGearGenerator.ItemsRoot + "/Pets";
        private const string Drones = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/";
        /// <summary>The pets' own models, one folder per pet.</summary>
        public const string PetModels = "Assets/_Game/Characters/Pets";

        private static readonly float[] StageScales = { 1f, 1.08f, 1.16f, 1.24f, 1.35f };
        private static readonly float[] StageGlow = { 0f, 0.3f, 0.5f, 0.75f, 1f };

        private struct Row
        {
            public ushort Id;
            /// <summary>Asset name of the item, its definition ("PetDef_" + Name) and its icon.</summary>
            public string Asset, Name;
            /// <summary>The model that stands in until the creature's own art exists.</summary>
            public string StandIn;
            /// <summary>Path of the creature's own model, once it has one. It replaces the stand-in.</summary>
            public string Model;
            /// <summary>It stands instead of bobbing like a drone, on a cloud under each of these bones of its model.</summary>
            public string[] Feet;
            public StatType Stat;
            public float Max;
            public string Description;
            /// <summary>Names of the five stages; empty = "Stage N".</summary>
            public string[] Stages;
            /// <summary>Glow colour of the five stages, from the art brief's palette.</summary>
            public string[] Glow;
            /// <summary>Asset and icon names this pet had before, renamed on the first run.</summary>
            public string OldAsset, OldIcon;
        }

        private static readonly Row[] Rows =
        {
            new Row { Id = 8001, Asset = "Pet_Emberwing", Name = "Emberwing", StandIn = "SM_Prop_Drone_Quad_01",
                Stat = StatType.MaxChi, Max = 2000f,
                Description = "A winged mystic beast in lacquer and gold. It grows your Flux as it matures.",
                Stages = new[] { "", "", "", "", "Ascendant" },
                Glow = new[] { "D4AF37", "D4AF37", "D4AF37", "D4AF37", "2E86AB" },
                OldAsset = "Pet_Drone", OldIcon = "Pet_DroneQuad" },
            new Row { Id = 8002, Asset = "Pet_DroneCore", Name = "Drone Core", StandIn = "SM_Prop_Drone_02",
                Stat = StatType.MaxHP, Max = 2200f,
                Description = "A boxy little robot buddy that bolts on plating and cannon pods. It grows your Health as it matures.",
                Stages = new[] { "", "", "", "", "Gunship" },
                Glow = new[] { "4FD1FF", "4FD1FF", "F2C230", "4FD1FF", "4FD1FF" },
                OldAsset = "Pet_DroneHeavy", OldIcon = "Pet_DroneHeavy" },
            new Row { Id = 8003, Asset = "Pet_Tidecaller", Name = "Tidecaller", StandIn = "SM_Prop_Drone_Ball_01",
                Stat = StatType.DefensePower, Max = 2200f,
                Description = "A pocket-sized kaiju that never outgrows its owner's shoulder. It grows your Defense as it matures.",
                Stages = new[] { "Spawnling", "", "", "", "Apex" },
                Glow = new[] { "5AD8C6", "5AD8C6", "5AD8C6", "3FA7D6", "3FA7D6" },
                OldAsset = "Pet_DroneBall", OldIcon = "Pet_DroneBall" },
            new Row { Id = 8004, Asset = "Pet_KitsuneSpirit", Name = "Kitsune Spirit", StandIn = "SM_Prop_Drone_Insect_01",
                Model = PetModels + "/KitsuneSpirit/Pet_KitsuneSpirit_S1.prefab",
                Feet = new[] { "DEF-front_toe.L", "DEF-front_toe.R", "DEF-toe.L", "DEF-toe.R" },
                Stat = StatType.AttackPower, Max = 1100f,
                Description = "A fox spirit wrapped in paper talismans, gaining tails as it grows. It grows your Attack as it matures.",
                Stages = new[] { "One Tail", "Three Tails", "Five Tails", "Seven Tails", "Nine Tails" },
                Glow = new[] { "FF8A3D", "FF8A3D", "FF8A3D", "FF8A3D", "FFD23F" },
                OldAsset = "Pet_DroneInsect", OldIcon = "Pet_DroneInsect" },
        };

        /// <summary>Pets that no longer exist: (item asset, icon). Their item ids (8005) are never handed out again.</summary>
        private static readonly (string Asset, string Icon)[] Retired = { ("Pet_DroneQuad", "Pet_Drone") };

        [MenuItem("Krytical Nova/Items/7. Build Pets")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(PetsRoot);
            WeatherVfxBuilder.EnsureFolder(ItemFolder);

            var settings = LoadOrCreate<PetSettings>(SettingsPath);
            if (settings.auraPrefab == null)
            {
                settings.auraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetGlowPath);
                if (settings.auraPrefab == null)
                    Debug.LogWarning("[Pets] No pet glow effect yet. Run Krytical Nova/GameFeel/Rebuild Pet Feel, then build the pets again.");
                EditorUtility.SetDirty(settings);
            }
            var items = new List<ItemDefinition>(Rows.Length);
            var definitions = new List<PetDefinition>(Rows.Length);
            foreach (Row row in Rows)
            {
                ItemDefinition item = Item(row);
                items.Add(item);
                definitions.Add(Definition(row));
            }

            var removed = new List<ItemDefinition>();
            foreach ((string asset, string icon) in Retired)
            {
                string itemPath = $"{ItemFolder}/{asset}.asset";
                var gone = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                if (gone != null)
                {
                    removed.Add(gone);
                    AssetDatabase.DeleteAsset(itemPath);
                }
                string iconPath = $"{ItemIconCapture.IconFolder}/{icon}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath) != null && !items.Any(i => AssetDatabase.GetAssetPath(i.icon) == iconPath))
                    AssetDatabase.DeleteAsset(iconPath);
            }

            var catalog = LoadOrCreate<PetCatalog>(CatalogPath);
            catalog.SetContents(definitions, settings);
            EditorUtility.SetDirty(catalog);

            RegisterItems(items, removed);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pets] {items.Count} pets built: {string.Join(", ", Rows.Select(r => r.Name))}. " +
                      "Run Krytical Nova/Setup/Player Prefab if the player has no PlayerPet yet.");
        }

        private static ItemDefinition Item(in Row row)
        {
            string path = $"{ItemFolder}/{row.Asset}.asset";
            string oldPath = $"{ItemFolder}/{row.OldAsset}.asset";
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>(path) == null && AssetDatabase.LoadAssetAtPath<ItemDefinition>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }

            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = row.Id;
                item.petScale = 1f; // multiplier on PetFollower's normalised size
                AssetDatabase.CreateAsset(item, path);
            }
            if (item.itemId != row.Id)
                Debug.LogError($"[Pets] {path} has item id {item.itemId}, the table says {row.Id}. Ids are never renumbered: fix the table or the asset.", item);

            item.displayName = row.Name;
            item.description = row.Description;
            item.rarity = ItemRarity.Common;
            item.equipSlot = EquipSlot.Pet;
            item.maxStack = 1;
            item.itemLevel = 0; // any level may use a pet
            string modelPath = string.IsNullOrEmpty(row.Model) ? Drones + row.StandIn + ".prefab" : row.Model;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null) item.petPrefab = model;
            else Debug.LogError($"[Pets] Model {modelPath} not found for {row.Name}.");

            Sprite icon = Icon(row);
            if (icon != null) item.icon = icon;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The pet's icon under its own name, renamed from the icon of the model that stands in for it.</summary>
        private static Sprite Icon(in Row row)
        {
            string path = $"{ItemIconCapture.IconFolder}/{row.Asset}.png";
            string oldPath = $"{ItemIconCapture.IconFolder}/{row.OldIcon}.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null && AssetDatabase.LoadAssetAtPath<Sprite>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static PetDefinition Definition(in Row row)
        {
            string path = $"{PetsRoot}/PetDef_{row.Asset.Substring("Pet_".Length)}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<PetDefinition>(path);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<PetDefinition>();
                definition.stat = row.Stat;
                definition.maxValue = row.Max;
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.itemId = row.Id;
            definition.displayName = row.Name;
            definition.description = row.Description;
            bool stands = row.Feet != null && row.Feet.Length > 0;
            definition.bobs = !stands;
            if (stands)
            {
                definition.footBones = row.Feet;
                definition.footEffect = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetCloudPath);
                if (definition.footEffect == null)
                    Debug.LogWarning($"[Pets] No pet cloud effect yet for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Pet Cloud, then build the pets again.");
            }

            if (definition.stages == null || definition.stages.Length != PetRules.StageCount)
                definition.stages = new PetStageLook[PetRules.StageCount];
            for (int stage = 0; stage < PetRules.StageCount; stage++)
            {
                PetStageLook look = definition.stages[stage];
                look.name = row.Stages[stage];
                if (look.scale <= 0f)
                {
                    // A stage that was never set up: the stand-in look of the art brief.
                    look.scale = StageScales[stage];
                    look.glow = ColorBible.Hex(row.Glow[stage]);
                    look.glowStrength = StageGlow[stage];
                }
                definition.stages[stage] = look;
            }
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>Pets are items a player owns: they belong in the item catalog, and retired ones leave it.</summary>
        private static void RegisterItems(List<ItemDefinition> pets, List<ItemDefinition> removed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[Pets] No item catalog yet: run Krytical Nova/Items/1 and 6, then build the pets again.");
                return;
            }
            var items = catalog.Items.Where(i => i != null && !removed.Contains(i)).Concat(pets).Distinct().OrderBy(i => i.itemId).ToList();
            var equipped = catalog.StarterEquipped.Where(i => i != null && !removed.Contains(i)).ToList();
            var bag = catalog.StarterBag.Where(s => s.item != null && !removed.Contains(s.item)).ToList();
            catalog.SetContents(items, equipped, bag);
            EditorUtility.SetDirty(catalog);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
