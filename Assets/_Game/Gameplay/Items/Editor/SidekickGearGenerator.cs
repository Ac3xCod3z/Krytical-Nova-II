using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Generates test gear from the Synty packs: Armor/Gloves/Boots/Cape per Sidekick SciFi Soldier outfit, weapons,
    /// rings, amulets and drone pets, plus the ItemCatalog and starter loadout. Existing item assets are never
    /// overwritten (IDs are stable), so hand-tuned stats/names survive re-runs.
    /// </summary>
    public static class SidekickGearGenerator
    {
        public const string ItemsRoot = "Assets/_Game/Gameplay/Items/Data/Assets";
        public const string CatalogPath = ItemsRoot + "/ItemCatalog.asset";
        public const string WeaponPrefabFolder = ItemsRoot + "/Prefabs/Weapons";
        public const string OutfitFolder = "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/ScifiSoldiers";
        public const string HumanFolder = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        private const string PlayerModelPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        // Sidekick demo weapons ship without materials; they are UV-mapped to the Sidekick colour palette.
        private const string FallbackWeaponMaterial = "Assets/Synty/SidekickCharacters/Resources/Materials/M_BaseMaterial.mat";

        // Stable ID ranges per slot: never renumber, only append.
        private const ushort WeaponBase = 1000, ArmorBase = 2000, GlovesBase = 3000, BootsBase = 4000;
        private const ushort RingBase = 5000, AmuletBase = 6000, CapeBase = 7000, PetBase = 8000;

        private static readonly Regex PartName = new Regex(@"^SK_(?<fam>[A-Z]+)_(?<set>[A-Z]+)_(?<vv>\d{2})_(?<nn>\d{2})(?<code>[A-Z]{4})_HU01$");

        private static readonly string[] OutfitNames =
            { "Recruit", "Ranger", "Sentinel", "Vanguard", "Warden", "Striker", "Phantom", "Bastion", "Raider", "Commander" };

        private struct WeaponSeed
        {
            public ushort id; public string asset; public string name; public string source; public ItemRarity rarity; public string description;
        }

        private static readonly WeaponSeed[] Weapons =
        {
            new WeaponSeed { id = WeaponBase + 1, asset = "Weapon_Katana", name = "Katana", source = "katana", rarity = ItemRarity.Common, description = "A balanced single-edged blade. Your starting weapon." },
            new WeaponSeed { id = WeaponBase + 2, asset = "Weapon_GreatSword", name = "Great Sword", source = "Assets/_ThirdParty/Grruzam Powerful Sword Animation(Great Sword, Katana)/Modeling/Modeling_Weapon_Big_Sword.FBX", rarity = ItemRarity.Rare, description = "A heavy two-handed blade." },
            new WeaponSeed { id = WeaponBase + 3, asset = "Weapon_PirateSword", name = "Corsair Saber", source = "Assets/Synty/SidekickCharacters/_Demos/Meshes/Weapons/Pirate_Sword/SK_Sword.fbx", rarity = ItemRarity.Uncommon, description = "A curved saber favoured by raiders." },
            new WeaponSeed { id = WeaponBase + 4, asset = "Weapon_GoblinAxe", name = "Goblin Cleaver", source = "Assets/Synty/SidekickCharacters/_Demos/Meshes/Weapons/Goblin_Axe/SK_Axe.fbx", rarity = ItemRarity.Uncommon, description = "Crude, heavy and surprisingly sharp." },
            new WeaponSeed { id = WeaponBase + 5, asset = "Weapon_Bat", name = "Scrap Bat", source = "Assets/Synty/SidekickCharacters/_Demos/Meshes/Weapons/Apocalypse_Bat/SK_Bat.fbx", rarity = ItemRarity.Common, description = "Nails driven through old wood." },
            new WeaponSeed { id = WeaponBase + 6, asset = "Weapon_SciFiSword", name = "Plasma Edge", source = "Assets/Synty/PolygonSciFiCity/Prefabs/Weapons/SM_Wep_Sword_01.prefab", rarity = ItemRarity.Epic, description = "A humming blade from the city foundries." },
            new WeaponSeed { id = WeaponBase + 7, asset = "Weapon_Knife", name = "Combat Knife", source = "Assets/Synty/PolygonSciFiCity/Prefabs/Weapons/SM_Wep_Knife_01.prefab", rarity = ItemRarity.Common, description = "Short, quick, reliable." },
        };

        private static readonly (string asset, string name, string prefab, ItemRarity rarity)[] Pets =
        {
            ("Pet_Drone", "Scout Drone", "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Drone_01.prefab", ItemRarity.Common),
            ("Pet_DroneHeavy", "Guardian Drone", "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Drone_02.prefab", ItemRarity.Rare),
            ("Pet_DroneBall", "Orb Companion", "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Drone_Ball_01.prefab", ItemRarity.Uncommon),
            ("Pet_DroneInsect", "Mantis Drone", "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Drone_Insect_01.prefab", ItemRarity.Epic),
            ("Pet_DroneQuad", "Quadcopter", "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Drone_Quad_01.prefab", ItemRarity.Legendary),
        };

        private static readonly (string name, ItemRarity rarity)[] Rings =
        {
            ("Iron Band", ItemRarity.Common), ("Silver Signet", ItemRarity.Uncommon), ("Ring of Focus", ItemRarity.Rare),
            ("Ember Loop", ItemRarity.Epic), ("Ring of the Sovereign", ItemRarity.Legendary)
        };

        private static readonly (string name, ItemRarity rarity)[] Amulets =
        {
            ("Bone Charm", ItemRarity.Common), ("Jade Pendant", ItemRarity.Uncommon), ("Amulet of Chi", ItemRarity.Rare),
            ("Heart of Embers", ItemRarity.Epic), ("Celestial Talisman", ItemRarity.Legendary)
        };

        [MenuItem("Krytical Nova/Items/1. Generate Test Gear From Synty")]
        public static void Generate()
        {
            var created = new List<string>();
            var all = new List<ItemDefinition>();

            AssetDatabase.StartAssetEditing();
            try { EnsureReadable(OutfitFolder); EnsureReadable(HumanFolder); }
            finally { AssetDatabase.StopAssetEditing(); }

            // Outfit parts grouped by outfit number.
            var outfits = new SortedDictionary<int, List<(BodyRegion region, string path)>>();
            foreach (var path in Directory.GetFiles(OutfitFolder, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                var m = PartName.Match(Path.GetFileNameWithoutExtension(path));
                if (!m.Success) continue;
                int vv = int.Parse(m.Groups["vv"].Value);
                var region = (BodyRegion)int.Parse(m.Groups["nn"].Value);
                if (!outfits.TryGetValue(vv, out var list)) outfits[vv] = list = new List<(BodyRegion, string)>();
                list.Add((region, path));
            }

            foreach (var kv in outfits)
            {
                int vv = kv.Key;
                string label = vv - 1 < OutfitNames.Length ? OutfitNames[vv - 1] : $"Outfit {vv:00}";
                var rarity = (ItemRarity)Mathf.Clamp((vv - 1) / 2, 0, 4);
                all.AddIfNotNull(OutfitItem(EquipSlot.Armor, ArmorBase, vv, $"Armor_SOLD_{vv:00}", $"{label} Armor", rarity, kv.Value,
                    "Body armour: torso, arms, hips and legs.", created));
                all.AddIfNotNull(OutfitItem(EquipSlot.Gloves, GlovesBase, vv, $"Gloves_SOLD_{vv:00}", $"{label} Gauntlets", rarity, kv.Value,
                    "Hand protection.", created));
                all.AddIfNotNull(OutfitItem(EquipSlot.Boots, BootsBase, vv, $"Boots_SOLD_{vv:00}", $"{label} Boots", rarity, kv.Value,
                    "Sturdy footwear.", created));
                all.AddIfNotNull(OutfitItem(EquipSlot.Cape, CapeBase, vv, $"Cape_SOLD_{vv:00}", $"{label} Cape", rarity, kv.Value,
                    "Worn across the back.", created));
            }

            foreach (var seed in Weapons) all.AddIfNotNull(WeaponItem(seed, created));

            for (int i = 0; i < Rings.Length; i++)
                all.Add(Simple(EquipSlot.Ring, (ushort)(RingBase + i + 1), $"Ring_{i + 1:00}", Rings[i].name, Rings[i].rarity,
                    "A ring. Stats coming soon.", created));
            for (int i = 0; i < Amulets.Length; i++)
                all.Add(Simple(EquipSlot.Amulet, (ushort)(AmuletBase + i + 1), $"Amulet_{i + 1:00}", Amulets[i].name, Amulets[i].rarity,
                    "An amulet. Stats coming soon.", created));
            for (int i = 0; i < Pets.Length; i++)
            {
                var pet = Simple(EquipSlot.Pet, (ushort)(PetBase + i + 1), Pets[i].asset, Pets[i].name, Pets[i].rarity,
                    "A loyal companion that follows you around.", created, out bool isNew);
                if (isNew)
                {
                    pet.petPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Pets[i].prefab);
                    pet.petScale = 1f; // multiplier on PetFollower's normalised size
                    EditorUtility.SetDirty(pet);
                }
                all.Add(pet);
            }

            BuildCatalog(all);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {all.Count} items in catalog ({created.Count} new). Next: Krytical Nova/Items/2. Capture Item Icons.");
        }

        private static void AddIfNotNull(this List<ItemDefinition> list, ItemDefinition def)
        {
            if (def != null) list.Add(def);
        }

        private static ItemDefinition OutfitItem(EquipSlot slot, ushort idBase, int vv, string asset, string displayName,
            ItemRarity rarity, List<(BodyRegion region, string path)> parts, string description, List<string> created)
        {
            var regions = EquipSlots.RegionsFor(slot);
            var mine = parts.Where(p => regions.Contains(p.region)).OrderBy(p => p.region).ToList();
            if (mine.Count == 0) return null;

            var def = Simple(slot, (ushort)(idBase + vv), asset, displayName, rarity, description, created, out bool isNew);
            // Part data is derived from the FBXs, so it is always refreshed (names/stats/rarity are left alone).
            def.appearanceParts = mine.Select(p => LoadPart(p.path, p.region)).Where(p => p.IsValid).ToList();
            EditorUtility.SetDirty(def);
            return def;
        }

        public static SidekickPartRef LoadPart(string fbxPath, BodyRegion region)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var smr = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (smr == null)
            {
                Debug.LogWarning($"[Items] No SkinnedMeshRenderer in {fbxPath}");
                return default;
            }
            // Record every dangle-bone chain so a pruned character skeleton can grow the bones on demand.
            var extras = new List<ExtraBone>();
            var seen = new HashSet<string>();
            foreach (var b in smr.bones)
            {
                var chain = new List<Transform>();
                for (var t = b; t != null && t.name.Contains("_dyn_"); t = t.parent) chain.Add(t);
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    var t = chain[i];
                    if (!seen.Add(t.name) || t.parent == null) continue;
                    extras.Add(new ExtraBone
                    {
                        name = t.name, parent = t.parent.name,
                        localPosition = t.localPosition, localRotation = t.localRotation, localScale = t.localScale
                    });
                }
            }
            return new SidekickPartRef
            {
                region = region,
                mesh = smr.sharedMesh,
                boneNames = smr.bones.Select(b => b != null ? b.name : "root").ToArray(),
                extraBones = extras.ToArray()
            };
        }

        public static bool TryParsePart(string partName, out BodyRegion region)
        {
            var m = PartName.Match(partName);
            region = m.Success ? (BodyRegion)int.Parse(m.Groups["nn"].Value) : BodyRegion.None;
            return m.Success;
        }

        private static ItemDefinition WeaponItem(WeaponSeed seed, List<string> created)
        {
            var def = Simple(EquipSlot.Weapon, seed.id, seed.asset, seed.name, seed.rarity, seed.description, created, out bool isNew);
            if (!isNew && def.weaponPrefab != null) return def; // delete the prefab (and clear the field) to rebuild

            string prefabPath = $"{WeaponPrefabFolder}/W_{seed.asset.Replace("Weapon_", "")}.prefab";
            var prefab = BuildWeaponPrefab(seed.source, prefabPath, out Bounds bounds);
            if (prefab == null) return def;
            def.weaponPrefab = prefab;
            def.weaponLocalPosition = Vector3.zero;
            def.weaponLocalEuler = Vector3.zero;
            def.weaponLocalScale = Vector3.one;
            float length = Mathf.Max(0.3f, bounds.max.z);
            def.hitboxCenter = new Vector3(0f, bounds.center.y, length * 0.5f + 0.05f);
            def.hitboxSize = new Vector3(Mathf.Max(0.18f, bounds.size.x), Mathf.Max(0.18f, bounds.size.y), length);
            EditorUtility.SetDirty(def);
            return def;
        }

        /// <summary>
        /// Wraps a weapon model in an identity root, rotated so the blade runs along +Z away from the grip (the mount's
        /// convention, inherited from the katana). The source pivot is treated as the grip.
        /// </summary>
        private static GameObject BuildWeaponPrefab(string source, string prefabPath, out Bounds bounds)
        {
            bounds = default;
            WeatherVfxBuilder.EnsureFolder(WeaponPrefabFolder);
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                GameObject model;
                if (source == "katana")
                {
                    // Reuse the exact mesh/material already on the player so the starter katana looks identical.
                    var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
                    var blade = player.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name == "KatanaBlade");
                    if (blade == null) { Debug.LogError("[Items] KatanaBlade not found on Player_01."); return null; }
                    model = new GameObject("Model");
                    model.AddComponent<MeshFilter>().sharedMesh = blade.sharedMesh;
                    model.AddComponent<MeshRenderer>().sharedMaterials = blade.GetComponent<MeshRenderer>().sharedMaterials;
                    model.transform.SetParent(root.transform, false);
                }
                else
                {
                    var src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                    if (src == null) { Debug.LogWarning($"[Items] Weapon source missing: {source}"); return null; }
                    model = (GameObject)Object.Instantiate(src, root.transform, false);
                    model.name = "Model";
                    model.transform.localPosition = Vector3.zero;
                    AlignLongestAxisToZ(root.transform, model.transform);
                }
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                var fallback = AssetDatabase.LoadAssetAtPath<Material>(FallbackWeaponMaterial);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == null) { mats[i] = fallback; changed = true; }
                    if (changed) r.sharedMaterials = mats;
                }
                bounds = LocalBounds(root.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AlignLongestAxisToZ(Transform root, Transform model)
        {
            var b = LocalBounds(root);
            Vector3 size = b.size;
            Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            // Point the end farthest from the pivot (the grip) along +Z.
            float toMax = Vector3.Dot(b.max, axis), toMin = -Vector3.Dot(b.min, axis);
            Vector3 bladeDir = toMax >= toMin ? axis : -axis;
            model.localRotation = Quaternion.FromToRotation(bladeDir, Vector3.forward) * model.localRotation;
        }

        public static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static ItemDefinition Simple(EquipSlot slot, ushort id, string asset, string displayName, ItemRarity rarity,
            string description, List<string> created) => Simple(slot, id, asset, displayName, rarity, description, created, out _);

        private static ItemDefinition Simple(EquipSlot slot, ushort id, string asset, string displayName, ItemRarity rarity,
            string description, List<string> created, out bool isNew)
        {
            string folder = $"{ItemsRoot}/{FolderFor(slot)}";
            WeatherVfxBuilder.EnsureFolder(folder);
            string path = $"{folder}/{asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            isNew = def == null;
            if (!isNew) return def;

            def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.description = description;
            def.rarity = rarity;
            def.equipSlot = slot;
            def.maxStack = 1;
            AssetDatabase.CreateAsset(def, path);
            created.Add(path);
            return def;
        }

        public static string FolderFor(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: return "Weapons";
                case EquipSlot.Armor: return "Armor";
                case EquipSlot.Gloves: return "Gloves";
                case EquipSlot.Boots: return "Boots";
                case EquipSlot.Ring: return "Rings";
                case EquipSlot.Amulet: return "Amulets";
                case EquipSlot.Cape: return "Capes";
                case EquipSlot.Pet: return "Pets";
                default: return "Misc";
            }
        }

        private static void BuildCatalog(List<ItemDefinition> items)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            // Keep anything already in the catalog (hand-made items), add generated ones, sort by ID.
            var merged = catalog.Items.Where(i => i != null).Concat(items).Distinct().OrderBy(i => i.itemId).ToList();

            ItemDefinition ById(ushort id) => merged.FirstOrDefault(i => i.itemId == id);
            var equipped = catalog.StarterEquipped.Count > 0
                ? catalog.StarterEquipped.ToList()
                : new[] { ById(WeaponBase + 1), ById(ArmorBase + 1), ById(GlovesBase + 1), ById(BootsBase + 1), ById(CapeBase + 1) }
                    .Where(i => i != null).ToList();
            var bag = catalog.StarterBag.Count > 0
                ? catalog.StarterBag.ToList()
                : new ushort[] { ArmorBase + 3, ArmorBase + 7, GlovesBase + 5, BootsBase + 8, CapeBase + 4, WeaponBase + 2,
                        WeaponBase + 6, RingBase + 3, AmuletBase + 3, PetBase + 1 }
                    .Select(ById).Where(i => i != null).Select(i => new StarterItem { item = i, count = 1 }).ToList();

            catalog.SetContents(merged, equipped, bag);
            EditorUtility.SetDirty(catalog);
        }

        private static void EnsureReadable(string folder)
        {
            foreach (var path in Directory.GetFiles(folder, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                }
            }
        }
    }
}
