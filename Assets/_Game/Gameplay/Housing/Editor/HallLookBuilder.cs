using System.Collections.Generic;
using System.Linq;
using KryticalNova.Factions;
using KryticalNova.Housing;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Housing
{
    /// <summary>
    /// Makes the looks a Hall can wear: Facades (the building on the Lot) and Finishes (every wall, the floor or
    /// the ceiling of the room inside), each an item with a picture, each its faction's own. A Facade is one of the
    /// vendor's buildings, or a few of its parts put together into a prefab of ours, stood so that its door faces
    /// the gate. A Finish is one panel or tile that the room repeats, in its own material or in one of the pack's
    /// tiling surfaces. The vendor's assets are only read.
    /// Ids are the tables' and are never renumbered: 29000 Finishes, 29500 Facades. Which of them a faction
    /// starts with, and what they are called, are first picks made from contact sheets for the owner to change.
    /// </summary>
    public static class HallLookBuilder
    {
        public const string LookFolder = HousingLotBuilder.DataFolder + "/Looks";
        public const string FacadeFolder = HousingLotBuilder.PrefabFolder + "/Facades";
        private const float FrontGap = 0.4f; // metres a Facade's front stands back from the front of its pad
        private const FactionId H = FactionId.Hegemony, N = FactionId.Null, C = FactionId.Covenant;
        private const string Space = FurnishingCatalogBuilder.Space, Worlds = FurnishingCatalogBuilder.Worlds, City = FurnishingCatalogBuilder.City,
            Cyber = FurnishingCatalogBuilder.Cyber, Fortress = FurnishingCatalogBuilder.Fortress, Dark = FurnishingCatalogBuilder.Dark,
            Kingdom = FurnishingCatalogBuilder.Kingdom, Elven = FurnishingCatalogBuilder.Elven, Vikings = FurnishingCatalogBuilder.Vikings,
            Realm = FurnishingCatalogBuilder.Realm;

        private struct Part
        {
            public string Pack, Prefab;
            public Vector3 Position;
        }

        private struct Facade
        {
            public ushort Id;
            public FactionId Faction;
            public string Name;
            public Part[] Parts;
            public float Scale, Yaw;
            public Vector2 Offset;
            public bool Standard, Darkened;
        }

        private struct Finish
        {
            public ushort Id;
            public FactionId Faction;
            public HomeLook Slot;
            public string Name, Pack, Prefab, MaterialPack, Material;
            public Vector3 Euler;
            public bool Standard;
        }

        private static Part P(string pack, string prefab, float x = 0f, float y = 0f, float z = 0f) =>
            new Part { Pack = pack, Prefab = prefab, Position = new Vector3(x, y, z) };

        private static Facade F(ushort id, FactionId faction, string name, float scale, float yaw, bool standard, params Part[] parts) =>
            new Facade { Id = id, Faction = faction, Name = name, Parts = parts, Scale = scale, Yaw = yaw, Standard = standard };

        private static Facade Dark_(Facade facade) { facade.Darkened = true; return facade; }
        private static Facade Moved(Facade facade, float x, float z) { facade.Offset = new Vector2(x, z); return facade; }

        private static Finish W(ushort id, FactionId faction, HomeLook slot, string name, string pack, string prefab,
            string materialPack = null, string material = null, bool standard = false, float flip = 0f, float yaw = 0f) =>
            new Finish { Id = id, Faction = faction, Slot = slot, Name = name, Pack = pack, Prefab = prefab, MaterialPack = materialPack,
                Material = material, Standard = standard, Euler = new Vector3(flip, yaw, 0f) };

        // A Hall is built to impress: a house-sized building of the packs is stood at up to one and a half times its
        // size (the world's measure calls that heroic), never more.
        private static readonly Facade[] Facades =
        {
            // ---- Hegemony
            F(29501, H, "Hab Block", 1f, 0f, false, P(Worlds, "SM_Bld_Pod_Hab_Small_02")),
            F(29502, H, "Long Hab", 1f, 0f, true, P(Worlds, "SM_Bld_Pod_Hab_Large_01")),
            F(29503, H, "Research Pod", 1f, 270f, false, P(Worlds, "SM_Bld_Pod_Research_02")),
            F(29504, H, "Barracks", 1.4f, 0f, false, P(Worlds, "SM_Bld_Corp_Barracks_01")),
            F(29505, H, "Depot", 1f, 180f, false, P(City, "SM_Bld_Warehouse_01")),
            F(29506, H, "Field Station", 1f, 180f, false, P(Worlds, "SM_Bld_Corp_MobileSite_01")),
            // ---- Null
            F(29521, N, "Black Gate", 1f, 0f, false, P(Fortress, "SM_Bld_Wall_L_Gate_02")),
            Dark_(F(29522, N, "Dark Chapel", 1.3f, 180f, false, P(Kingdom, "SM_Bld_Preset_Church_01_A_Optimized"))),
            Dark_(F(29523, N, "Watch Tower", 1f, 180f, false, P(Kingdom, "SM_Bld_Preset_Tower_01_Optimized"))),
            Dark_(F(29524, N, "Grim Manor", 1.2f, 180f, true, P(Kingdom, "SM_Bld_Preset_House_05_Optimized"))),
            Dark_(F(29525, N, "Gate Lodge", 1f, 180f, false, P(Kingdom, "SM_Bld_Preset_House_Archway_01_Optimized"))),
            // ---- Covenant
            F(29541, C, "Longhouse", 1.5f, 180f, true, P(Vikings, "SM_Bld_Base_Long_Door_01"), P(Vikings, "SM_Bld_Roof_Long_01")),
            F(29542, C, "Dragon Hall", 1.5f, 180f, false, P(Vikings, "SM_Bld_Base_Long_Door_01"), P(Vikings, "SM_Bld_Roof_02")),
            F(29543, C, "Star Court", 1f, 0f, false, P(Elven, "SM_Bld_Planetarium_01")),
            // The Boat House that was here did not fit or work (owner, 12 Oct 2026): an open frame on stilts is no Hall.
            F(29544, C, "Mead Hall", 1f, 180f, false, P(Kingdom, "SM_Bld_Preset_Tavern_01_Optimized")),
        };

        private const HomeLook Wall = HomeLook.Wall, Floor = HomeLook.Floor, Ceiling = HomeLook.Ceiling;
        private const string BaseWall = "SM_Bld_Base_Wall_01", BaseFloor = "SM_Bld_Base_Floor_01", BaseCeiling = "SM_Bld_Base_Ceiling_01";

        private static readonly Finish[] Finishes =
        {
            // ---- Hegemony
            W(29001, H, Wall, "Hull Plating", Space, "SM_Bld_Wall_01", standard: true),
            W(29002, H, Wall, "Service Panels", Space, "SM_Bld_Wall_03"),
            W(29003, H, Wall, "Clean Room", Cyber, BaseWall, Cyber, "Wall_01_A"),
            W(29004, H, Wall, "Neon District", Cyber, BaseWall, Cyber, "Wall_05_A"),
            W(29005, H, Wall, "Bare Concrete", Cyber, BaseWall, Cyber, "Wall_02_A"),
            W(29011, H, Floor, "Deck Plates", Space, "SM_Bld_Floor_01", standard: true),
            W(29012, H, Floor, "Grid Deck", Space, "SM_Bld_Floor_05"),
            W(29013, H, Floor, "Polished Tile", Cyber, BaseFloor, Cyber, "Floor_01"),
            W(29014, H, Floor, "Street Slab", Cyber, BaseFloor, Cyber, "Floor_02"),
            W(29021, H, Ceiling, "Deck Head", Space, "SM_Bld_Ceiling_01", standard: true),
            W(29022, H, Ceiling, "Light Wells", Space, "SM_Bld_Ceiling_02"),
            W(29023, H, Ceiling, "Plain Panel", Cyber, BaseCeiling, Cyber, "Wall_01_A"),
            // ---- Null
            W(29101, N, Wall, "Fortress Stone", Fortress, "SM_Bld_Wall_01", standard: true),
            W(29102, N, Wall, "Dark Brick", Dark, BaseWall, Dark, "Brick_Large_01"),
            W(29103, N, Wall, "Black Stone", Dark, BaseWall, Fortress, "SmoothStone_Dark_01"),
            W(29104, N, Wall, "Faded Wallpaper", Dark, BaseWall, Dark, "Wallpaper_03"),
            W(29111, N, Floor, "Flagstones", Fortress, "SM_Bld_Floor_Tile_01", standard: true),
            W(29112, N, Floor, "Crypt Tiles", Dark, BaseFloor, Dark, "Floor_Tiles_01"),
            W(29113, N, Floor, "Black Boards", Dark, BaseFloor, Dark, "Wood_Floor_Boards_01"),
            W(29121, N, Ceiling, "Stone Vault", Dark, BaseCeiling, Dark, "Stone_02", standard: true),
            W(29122, N, Ceiling, "Smoked Boards", Dark, BaseCeiling, Dark, "Wood_Floor_Boards_02"),
            W(29123, N, Ceiling, "Rib Vault", Dark, "SM_Bld_Ceiling_01"),
            // ---- Covenant
            W(29201, C, Wall, "Elven Hall", Elven, "SM_Bld_Wall_01", standard: true),
            W(29202, C, Wall, "White Marble", Elven, BaseWall, Elven, "Marble_White_01"),
            W(29203, C, Wall, "River Stone", Elven, BaseWall, Elven, "Brick_02"),
            W(29204, C, Wall, "Timber Frame", Kingdom, "SM_Bld_House_Wall_01"),
            W(29205, C, Wall, "Log Wall", Realm, "SM_Bld_Wall_Logs_01"),
            W(29206, C, Wall, "Plank Wall", Realm, "SM_Bld_Wall_01"),
            W(29211, C, Floor, "Elven Paving", Elven, "SM_Bld_Floor_01", standard: true),
            W(29212, C, Floor, "Marble Floor", Elven, BaseFloor, Elven, "Marble_White_03"),
            W(29213, C, Floor, "Oak Boards", Kingdom, "SM_Bld_House_Floor_Wood_01"),
            W(29214, C, Floor, "Black Marble", Elven, BaseFloor, Elven, "Marble_Black_01"),
            W(29215, C, Floor, "Hall Boards", Realm, "SM_Bld_Dock_Floor_01"),
            W(29221, C, Ceiling, "Oak Rafters", Kingdom, "SM_Bld_House_Floor_Wood_01", standard: true, flip: 180f),
            W(29222, C, Ceiling, "Marble Ceiling", Elven, BaseFloor, Elven, "Marble_White_01", flip: 180f),
        };

        [MenuItem("Krytical Nova/Housing/3. Build Facades and Finishes")]
        public static void Build() => Build(captureIcons: true);

        public static void Build(bool captureIcons)
        {
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            var catalog = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(FurnishingCatalogBuilder.CatalogPath);
            if (items == null || catalog == null)
            {
                Debug.LogError("[Housing] Build the furnishings first: there is no item catalog or no furnishing catalog.");
                return;
            }
            WeatherVfxBuilder.EnsureFolder(LookFolder);
            WeatherVfxBuilder.EnsureFolder(FacadeFolder);
            WeatherVfxBuilder.EnsureFolder(FurnishingCatalogBuilder.ItemFolder);
            WeatherVfxBuilder.EnsureFolder(FurnishingCatalogBuilder.IconFolder);

            var looks = new List<HallLookDefinition>();
            var made = new List<ItemDefinition>();
            var pictures = new List<(GameObject model, string path)>();
            var pictured = new List<ItemDefinition>();
            var kept = new HashSet<string>();
            int missing = 0;

            foreach (Facade row in Facades)
            {
                GameObject prefab = FacadePrefab(row);
                if (prefab == null) { missing++; continue; }
                HallLookDefinition look = Look(row.Id, row.Faction, HomeLook.Facade, row.Name, row.Standard, prefab, null,
                    new Vector3(0f, row.Yaw, 0f), row.Scale, "A Facade of the {0}, and theirs alone: the whole outside of a Hall. Click it in the Furnishing Crate at your Hall to have the Hall wear it.");
                // Stood at the front of its pad, so that its front is at the Lot's door whatever its depth; the
                // table's own offset is added to that.
                float room = HousingLotBuilder.PadSize.y - look.boundsSize.z;
                look.padOffset = new Vector2(row.Offset.x, row.Offset.y - Mathf.Max(0f, room) * 0.5f + FrontGap);
                Keep(look, prefab, looks, made, kept, pictures, pictured, captureIcons);
            }
            foreach (Finish row in Finishes)
            {
                string path = FurnishingCatalogBuilder.FindPrefab(row.Pack, row.Prefab);
                var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                Material material = row.Material != null ? FindMaterial(row.MaterialPack, row.Material) : null;
                if (prefab == null || (row.Material != null && material == null))
                {
                    Debug.LogWarning($"[Housing] {row.Id} {row.Name}: no prefab {row.Prefab} in {row.Pack}, or no material {row.Material}; skipped.");
                    missing++;
                    continue;
                }
                string what = row.Slot == HomeLook.Wall ? "every wall" : row.Slot == HomeLook.Floor ? "the floor" : "the ceiling";
                HallLookDefinition look = Look(row.Id, row.Faction, row.Slot, row.Name, row.Standard, prefab, material, row.Euler, 1f,
                    "A Finish in the {0} style: " + what + " of a Hall's rooms, changed at one stroke. Click it in the Furnishing Crate at your Hall.");
                Keep(look, prefab, looks, made, kept, pictures, pictured, captureIcons);
            }

            try
            {
                FurnishingCatalogBuilder.CaptureIcons(pictures, pictured);
            }
            finally
            {
                // A Finish's panel is a copy made for its picture (Pictured). Hidden and in no scene, it would stay in
                // the Editor at the world's origin, in front of every camera that looks there, until Unity is closed.
                foreach ((GameObject model, string _) in pictures)
                    if (model != null && !EditorUtility.IsPersistent(model)) Object.DestroyImmediate(model);
            }
            catalog.SetLooks(looks.OrderBy(l => l.itemId).ToList());
            EditorUtility.SetDirty(catalog);
            int removed = FurnishingCatalogBuilder.RemoveStale(items, made, kept,
                new[] { FurnishingCatalogBuilder.ItemFolder, LookFolder, FurnishingCatalogBuilder.IconFolder }, "Look_");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Housing] {looks.Count} looks built ({pictures.Count} icons taken, {missing} missing, {removed} old assets removed).");
        }

        private static void Keep(HallLookDefinition look, GameObject prefab, List<HallLookDefinition> looks, List<ItemDefinition> made,
            HashSet<string> kept, List<(GameObject model, string path)> pictures, List<ItemDefinition> pictured, bool captureIcons)
        {
            string asset = $"Look_{look.itemId}";
            string iconPath = $"{FurnishingCatalogBuilder.IconFolder}/{asset}_{prefab.name.Replace("SM_Bld_", string.Empty)}{(look.material != null ? "_" + look.material.name : string.Empty)}.png";
            looks.Add(look);
            made.Add(look.item);
            kept.Add($"{FurnishingCatalogBuilder.ItemFolder}/{asset}.asset");
            kept.Add($"{LookFolder}/{asset}.asset");
            kept.Add(iconPath);
            bool stale = look.item.icon == null || AssetDatabase.GetAssetPath(look.item.icon) != iconPath;
            if (!captureIcons || !stale) return;
            pictures.Add((Pictured(look, prefab), iconPath));
            pictured.Add(look.item);
        }

        // What an icon is a picture of: the building, or one panel in the material the Finish gives it.
        private static GameObject Pictured(HallLookDefinition look, GameObject prefab)
        {
            if (look.material == null) return prefab;
            GameObject copy = Object.Instantiate(prefab);
            copy.name = prefab.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            foreach (MeshRenderer renderer in copy.GetComponentsInChildren<MeshRenderer>())
                renderer.sharedMaterials = Enumerable.Repeat(look.material, renderer.sharedMaterials.Length).ToArray();
            return copy;
        }

        private static HallLookDefinition Look(ushort id, FactionId faction, HomeLook slot, string name, bool standard, GameObject prefab,
            Material material, Vector3 euler, float scale, string description)
        {
            string asset = $"Look_{id}";
            // Only a Facade is its faction's own, so that a Hall fits the Crew Quarter it stands in.
            ItemDefinition item = FurnishingCatalogBuilder.Item(id, slot == HomeLook.Facade ? faction : FactionId.None, name, asset,
                string.Format(description, faction), "HALL");
            item.maxStack = 9;
            string path = $"{LookFolder}/{asset}.asset";
            var look = AssetDatabase.LoadAssetAtPath<HallLookDefinition>(path);
            bool created = look == null;
            if (created) look = ScriptableObject.CreateInstance<HallLookDefinition>();
            Bounds box = FurnishingCatalogBuilder.Measure(prefab, euler, scale, float.NegativeInfinity, float.PositiveInfinity);
            look.item = item;
            look.itemId = id;
            look.faction = faction;
            look.slot = slot;
            look.standard = standard;
            look.prefab = prefab;
            look.material = material;
            look.modelScale = scale;
            look.modelEuler = euler;
            look.boundsCenter = box.center;
            look.boundsSize = box.size;
            if (created) AssetDatabase.CreateAsset(look, path);
            else EditorUtility.SetDirty(look);
            return look;
        }

        // One vendor building as it is; several parts, or a building in the Null's colours, as a prefab of ours.
        private static GameObject FacadePrefab(in Facade row)
        {
            var parts = new List<GameObject>();
            foreach (Part part in row.Parts)
            {
                string path = FurnishingCatalogBuilder.FindPrefab(part.Pack, part.Prefab);
                var prefab = path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (prefab == null)
                {
                    Debug.LogWarning($"[Housing] {row.Id} {row.Name}: no prefab {part.Prefab} in {part.Pack}; skipped.");
                    return null;
                }
                parts.Add(row.Darkened ? NullTreatment.Variant(prefab) : prefab);
            }
            if (parts.Count == 1) return parts[0];

            string made = $"{FacadeFolder}/Facade_{row.Id}.prefab";
            var root = new GameObject($"Facade_{row.Id}");
            try
            {
                for (int n = 0; n < parts.Count; n++)
                {
                    var piece = (GameObject)PrefabUtility.InstantiatePrefab(parts[n], root.transform);
                    piece.transform.localPosition = row.Parts[n].Position;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, made);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material FindMaterial(string pack, string name)
        {
            foreach (string guid in AssetDatabase.FindAssets($"{name} t:Material", new[] { $"{FurnishingCatalogBuilder.SyntyRoot}/{pack}" }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && material.name == name) return material;
            }
            return null;
        }
    }
}
