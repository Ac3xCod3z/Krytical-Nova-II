using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Brings a gear set made in Blender (Tripo models fitted by the synty-blender skill) into the gear ladder: the
    /// part FBXs get Synty's import settings and join the part catalog, each piece gets a look template, the set's
    /// own cell colours become a theme (from the swatch file the Blender passes write), and the ladder lines of the
    /// set's band wear it. Then the ladder is baked, so every tier of those lines is the new set, dyes included.
    /// The helmet rides the armor's look in the head attachment (helmets are a vanity toggle, not a slot).
    /// </summary>
    public static class TripoGearSetBuilder
    {
        /// <summary>Themes of Blender-made sets: their cells are the set's own, never re-baked from roles.</summary>
        public const string ThemePrefix = "KRYT_";

        private const string SwatchPath = "Assets/_ArtSource/Blender~/Swatch/KN_GearSwatch.json";
        private const string PartsRoot = SidekickPartCatalogBaker.OwnPartsRoot + "/Gear";
        // Synty's own Sidekick part import settings (normals imported, so a faceted part stays faceted).
        private const string ImportReference = "SK_SCFI_CIVL_01_10TORS_HU01";

        private struct Piece
        {
            public EquipSlot slot;
            public ushort lineId;
            public string displayName;
            public string[] parts;
            public string icon; // its painted icon in IconArtRoot/<set folder>, made from the piece's in-game capture
        }

        private struct Set
        {
            public string code, folder, name, palettePrimary;
            public string species; // the part names' body suffix: HU01 (default, humans), RO01 robots, SN01 skeletons
            public string[] roles; // primary, secondary, under, accent, trim, leather, glow, metal x4
            public Piece[] pieces;
            public (ushort lineId, string displayName, string icon)[] renamed; // ring and amulet of the same band: the set's name
            public Weapon[] weapons;
            // Painted (owner, 24 Oct 2026): the parts keep their Tripo paint, one PNG each, named in <folder>_paint.json;
            // a dye recolours the paint (GearPaintImporter, GearPaint) instead of the swatch cells.
            public bool painted;
        }

        /// <summary>
        /// A weapon of the set, from synty-blender weapon_pass.py: an FBX with its grip at the origin, its palette
        /// texture and its grip json (where a two-handed weapon's other hand goes). It takes the hold, carry and
        /// strikes of <see cref="like"/>, the class's look of the same kind already in the game.
        /// </summary>
        private struct Weapon
        {
            public ushort lineId;
            public string name, displayName, like, icon;
            public Vector3? holdEuler; // the hand's hold, when the class look's is wrong for this model
        }

        private static readonly Set DockRunner = new Set
        {
            code = "DOCK", folder = "DockRunner", name = "Dock Runner",
            roles = new[] { "B8935C", "43656A", "6C442E", "DB8040", "BD6334", "6C442E", "F0994B", "4B4546", "453937", "2B2D30", "27211F" },
            pieces = new[]
            {
                // Civilian Rare, levels 45-75 (band 2): the Analyst lines become the Dock Runner set.
                new Piece { slot = EquipSlot.Armor, lineId = 22, displayName = "Dock Runner Outfit", parts = new[] { "10TORS", "11AUPL", "12AUPR", "17HIPS", "22AHED" }, icon = "DockRunner_Outfit_01" },
                new Piece { slot = EquipSlot.Gloves, lineId = 27, displayName = "Dock Runner Gloves", parts = new[] { "15HNDL", "16HNDR" }, icon = "DockRunner_Gloves_02" },
                new Piece { slot = EquipSlot.Boots, lineId = 32, displayName = "Dock Runner Work Boots", parts = new[] { "20FOTL", "21FOTR" }, icon = "DockRunner_WorkBoots_03" },
                new Piece { slot = EquipSlot.Cape, lineId = 36, displayName = "Dock Runner Rain Cape", parts = new[] { "24ABAC" }, icon = "DockRunner_RainCape_04" },
            },
            renamed = new (ushort, string, string)[] { (174, "Dock Runner Cargo Ring", "DockRunner_CargoRing_08"), (175, "Dock Runner Cargo Token", "DockRunner_CargoToken_09") },
            weapons = new[]
            {
                // The Rare band-2 lines of the three Hegemony classes. The Carbine is a pistol (owner, 23 Oct): a pair at the hips.
                new Weapon { lineId = 117, name = "Monoblade", displayName = "Dock Runner Monoblade", like = "Weapon_Monoblade_03", icon = "DockRunner_Monoblade_05" },
                new Weapon { lineId = 122, name = "Glaive", displayName = "Dock Runner Glaive", like = "Weapon_Glaive_03", icon = "DockRunner_Glaive_06" },
                // The pistol hold (SidekickGearGenerator.PistolHoldEuler), not the class look's rifle hold: it points the barrel ahead,
                // upright, through the Carbine strikes (measured in Play, 24 Oct 2026; owner: "gun placement is way off").
                new Weapon { lineId = 127, name = "Carbine", displayName = "Dock Runner Carbine", like = "Weapon_Carbine_01", holdEuler = SidekickGearGenerator.PistolHoldEuler, icon = "DockRunner_Carbine_07" },
            },
        };

        private static readonly Set ScrapSentinel = new Set
        {
            code = "SCRP", folder = "ScrapSentinel", name = "Scrap Sentinel", species = "RO01",
            // primary yellow, secondary dark, under, accent cream, trim, leather, glow green, metals
            roles = new[] { "F4B741", "3B383C", "1C191A", "D8C6B0", "C7892B", "44322A", "3CE35A", "776D6D", "4F4545", "363135", "242124" },
            pieces = new[]
            {
                // Robot Rare, levels 45-75 (band 2): the Ravager lines become the Scrap Sentinel set. On a robot the head
                // piece is the head itself (owner, 24 Oct 2026); the bare head hides while it is shown (CharacterAppearance).
                new Piece { slot = EquipSlot.Armor, lineId = 41, displayName = "Scrap Sentinel Plating", parts = new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR", "22AHED" }, icon = "ScrapSentinel_Plating_01" },
                new Piece { slot = EquipSlot.Gloves, lineId = 46, displayName = "Scrap Sentinel Servos", parts = new[] { "15HNDL", "16HNDR" }, icon = "ScrapSentinel_Servos_02" },
                new Piece { slot = EquipSlot.Boots, lineId = 51, displayName = "Scrap Sentinel Stabilizers", parts = new[] { "20FOTL", "21FOTR" }, icon = "ScrapSentinel_Stabilizers_03" },
                new Piece { slot = EquipSlot.Cape, lineId = 55, displayName = "Scrap Sentinel Back Unit", parts = new[] { "24ABAC" }, icon = "ScrapSentinel_BackUnit_04" },
            },
            renamed = new (ushort, string, string)[] { (184, "Scrap Sentinel Coupling", "ScrapSentinel_Coupling_08"), (185, "Scrap Sentinel Capacitor", "ScrapSentinel_Capacitor_09") },
            weapons = new[]
            {
                // The Rare band-2 lines of the three Null classes. The Harvester is a one-handed sickle-pick (0.84 m, like
                // the cleaver); the Servo Claws a pair over the fists, along the forearm; the Arc Caster a twin-barrel gun.
                new Weapon { lineId = 132, name = "Harvester", displayName = "Scrap Sentinel Harvester", like = "Weapon_Harvester_03", icon = "ScrapSentinel_Harvester_05" },
                new Weapon { lineId = 137, name = "ServoClaws", displayName = "Scrap Sentinel Servo Claws", like = "Weapon_ServoClaws_03", holdEuler = new Vector3(270f, 0f, 180f), icon = "ScrapSentinel_ServoClaws_07" },
                new Weapon { lineId = 142, name = "ArcCaster", displayName = "Scrap Sentinel Arc Caster", like = "Weapon_ArcCaster_03", icon = "ScrapSentinel_ArcCaster_06" },
            },
        };

        private static readonly Set HollowWarden = new Set
        {
            code = "HOLW", folder = "HollowWarden", name = "Hollow Warden", species = "SN01", painted = true,
            // The paint is its own colours; these name the set's colourway (bone, iron, cloth, bronze trim, ghost-blue glow).
            roles = new[] { "4A4744", "2F3538", "26221F", "C9B98F", "A0773A", "4A3628", "7FE6F0", "8E9294", "5F6467", "41464A", "2A2D30" },
            pieces = new[]
            {
                // Skeleton Rare, levels 45-75 (band 2): the Deathsworn lines become the Hollow Warden set. The bracers are
                // in the upper arms and the knee guards in the waist (the fitted scene has no 13/14 or 18/19 parts).
                new Piece { slot = EquipSlot.Armor, lineId = 60, displayName = "Hollow Warden Armor", parts = new[] { "10TORS", "11AUPL", "12AUPR", "17HIPS", "22AHED" }, icon = "HollowWarden_Armor_01" },
                new Piece { slot = EquipSlot.Gloves, lineId = 65, displayName = "Hollow Warden Gauntlets", parts = new[] { "15HNDL", "16HNDR" }, icon = "HollowWarden_Gloves_02" },
                new Piece { slot = EquipSlot.Boots, lineId = 70, displayName = "Hollow Warden Greaves", parts = new[] { "20FOTL", "21FOTR" }, icon = "HollowWarden_Boots_03" },
                new Piece { slot = EquipSlot.Cape, lineId = 74, displayName = "Hollow Warden Shroud", parts = new[] { "24ABAC" }, icon = "HollowWarden_Cape_04" },
            },
            renamed = new (ushort, string, string)[] { (194, "Hollow Warden Ring", "HollowWarden_Ring_09"), (195, "Hollow Warden Amulet", "HollowWarden_Amulet_10") },
            // Its weapons wait: the Null Rare weapon lines (132, 137, 142) wear Scrap Sentinel's.
        };

        [MenuItem("Krytical Nova/Items/Blender Sets/Add Hollow Warden (Skeleton Rare 45-75, painted)")]
        public static void AddHollowWarden() => Add(HollowWarden);

        [MenuItem("Krytical Nova/Items/Blender Sets/Add Scrap Sentinel (Robot Rare 45-75)")]
        public static void AddScrapSentinel() => Add(ScrapSentinel);

        // Painted icons of the set's pieces (owner, 24 Oct 2026: AI-painted from each piece's in-game capture, in the
        // Cyberpunk pack's style), given the comic treatment like every pack icon.
        private const string IconArtRoot = "Assets/_Game/Icons/Art/Generated";
        private const string WeaponModelsRoot = SidekickGearGenerator.WeaponPrefabFolder + "/Models";
        private const string PlayerPrefabPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";

        [MenuItem("Krytical Nova/Items/Blender Sets/Add Dock Runner (Civilian Rare 45-75)")]
        public static void AddDockRunner() => Add(DockRunner);

        private static void Add(Set set)
        {
            string folder = $"{PartsRoot}/{set.folder}";
            int imported = ApplyImportSettings(folder);
            if (set.painted) GearPaintImporter.Import(folder);
            var catalog = SidekickPartCatalogBaker.Bake();

            var themes = AssetDatabase.LoadAssetAtPath<GearThemeLibrary>(GearThemeBuilder.LibraryPath);
            string themeName = ThemePrefix + set.name;
            WriteTheme(themes, themeName, set);

            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            var lines = dictionary.Lines.ToList();
            foreach (var piece in set.pieces)
            {
                var ids = new List<ushort>();
                foreach (string slot in piece.parts)
                {
                    string partName = $"SK_KRYT_{set.code}_01_{slot}_{set.species ?? "HU01"}";
                    ushort id = catalog.IdOf(partName);
                    if (id == 0) { Debug.LogError($"[Items] {partName} is not in the part catalog: is it in {folder}?"); return; }
                    ids.Add(id);
                }
                var look = LookTemplate(piece, set, catalog, ids.ToArray());
                int at = lines.FindIndex(l => l.lineId == piece.lineId);
                if (at < 0) { Debug.LogError($"[Items] Gear line {piece.lineId} is missing."); return; }
                var line = lines[at];
                line.look = look;
                line.set = set.name;
                line.theme = themeName;
                line.displayName = piece.displayName;
                line.icon = PaintedIcon(set, piece.icon); // null: captured again from the new look
                lines[at] = line;
            }
            foreach (var weapon in set.weapons ?? Array.Empty<Weapon>())
            {
                var look = WeaponLook(weapon, set);
                if (look == null) return;
                int at = lines.FindIndex(l => l.lineId == weapon.lineId);
                if (at < 0) { Debug.LogError($"[Items] Gear line {weapon.lineId} is missing."); return; }
                var line = lines[at];
                line.look = look;
                line.set = set.name;
                line.displayName = weapon.displayName;
                line.icon = PaintedIcon(set, weapon.icon);
                lines[at] = line;
            }
            foreach (var (lineId, displayName, icon) in set.renamed)
            {
                int at = lines.FindIndex(l => l.lineId == lineId);
                if (at < 0) continue;
                var line = lines[at];
                line.set = set.name;
                line.displayName = displayName;
                var painted = PaintedIcon(set, icon);
                if (painted != null) line.icon = painted; // jewellery has no model: without one it keeps the library's icon
                lines[at] = line;
            }
            dictionary.SetLines(dictionary.Parts, lines);
            EditorUtility.SetDirty(dictionary);
            AssetDatabase.SaveAssets();

            GearStatBaker.Bake();
            if (set.weapons != null && set.weapons.Length > 0)
            {
                WeaponGlowZoneBaker.Bake();
                WeaponPaintBaker.Bake();
                EnsureOffHandIK();
            }
            Debug.Log($"[Items] {set.name}: {imported} parts imported, theme {themeName}, {set.pieces.Length} lines wear it. " +
                      "Next: capture the new pieces' icons (Krytical Nova/Items/Capture Icons (Missing Only)).");
        }

        /// <summary>The piece's painted icon, given the comic treatment; null when it has none (it is then captured).</summary>
        private static Sprite PaintedIcon(Set set, string icon)
        {
            if (string.IsNullOrEmpty(icon)) return null;
            string source = $"{IconArtRoot}/{set.folder}/{icon}.png";
            if (!File.Exists(source)) { Debug.LogWarning($"[Items] {source} is missing: that piece's icon will be captured from its model."); return null; }
            string target = icon.Substring(0, icon.LastIndexOf('_')); // the number at the end only orders the folder
            return IconBaker.BakePicture(source, target, IconBaker.Settings());
        }
            GearLookIconFiller.Fill();

        /// <summary>
        /// One weapon of the set: its palette material, its model prefab (grip at the origin, the WeaponGrip from its
        /// grip json) and its look template, which holds and carries it like <see cref="Weapon.like"/>.
        /// </summary>
        private static ItemDefinition WeaponLook(Weapon weapon, Set set)
        {
            string folder = $"{WeaponModelsRoot}/{set.folder}";
            string asset = $"W_KRYT_{set.code}_{weapon.name}";
            string fbx = $"{folder}/{asset}.fbx", png = $"{folder}/T_{asset}.png", json = $"{folder}/{asset}_grip.json";
            var like = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{SidekickGearGenerator.ItemsRoot}/Weapons/{weapon.like}.asset");
            if (like == null || !File.Exists(fbx) || !File.Exists(png) || !File.Exists(json))
            {
                Debug.LogError($"[Items] {asset}: needs {fbx}, its palette and grip json from weapon_pass.py, and {weapon.like}.");
                return null;
            }

            // The palette is blocks of flat colour: point filtered, uncompressed, no mips, readable for the dye baker.
            if (AssetImporter.GetAtPath(png) is TextureImporter tex)
            {
                tex.textureType = TextureImporterType.Default;
                tex.sRGBTexture = true;
                tex.mipmapEnabled = false;
                tex.filterMode = FilterMode.Point;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.textureCompression = TextureImporterCompression.Uncompressed;
                tex.isReadable = true;
                tex.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath(fbx) is ModelImporter model)
            {
                model.importNormals = ModelImporterNormals.Import; // faceted stays faceted
                model.materialImportMode = ModelImporterMaterialImportMode.None;
                model.importAnimation = false;
                model.animationType = ModelImporterAnimationType.None;
                model.globalScale = 1f;
                model.SaveAndReimport();
            }
            string matPath = $"{folder}/M_{asset}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, matPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(png));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.2f);
            material.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(material);

            string prefabPath = $"{SidekickGearGenerator.WeaponPrefabFolder}/{asset}.prefab";
            if (SidekickGearGenerator.BuildWeaponPrefab(fbx, prefabPath, out Bounds bounds) == null) return null;
            var grip = JsonUtility.FromJson<GripJson>(File.ReadAllText(json));
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = new[] { material };
                MatchSide(root, like.weaponPrefab);
                bounds = SidekickGearGenerator.LocalBounds(root.transform);
                if (!root.TryGetComponent(out WeaponGrip held)) held = root.AddComponent<WeaponGrip>();
                held.twoHanded = grip.off_hand != null && grip.off_hand.point != null && grip.off_hand.point.Length == 3;
                if (held.twoHanded)
                {
                    // Along the shaft (+Z here, the grip at 0): from a hand's width past the main hand to where the blade starts.
                    float butt = bounds.min.z, length = bounds.size.z;
                    float bladeAt = butt + (grip.blade_starts_at > 0f ? grip.blade_starts_at : 0.55f) * length;
                    held.offHandAt = new Vector3(0f, 0f, grip.off_hand.point[1]);
                    held.offHandFrom = new Vector3(0f, 0f, 0.14f);
                    held.offHandTo = new Vector3(0f, 0f, Mathf.Max(0.2f, bladeAt - 0.04f));
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            string lookPath = $"{SidekickGearGenerator.ItemsRoot}/Weapons/Weapon_KRYT_{set.code}_{weapon.name}.asset";
            var look = AssetDatabase.LoadAssetAtPath<ItemDefinition>(lookPath);
            if (look == null)
            {
                look = UnityEngine.Object.Instantiate(like);
                AssetDatabase.CreateAsset(look, lookPath);
            }
            else EditorUtility.CopySerialized(like, look);
            look.name = Path.GetFileNameWithoutExtension(lookPath);
            look.itemId = 0;
            look.icon = null;
            look.displayName = weapon.displayName;
            look.rarity = ItemRarity.Rare;
            look.weaponPrefab = prefab;
            look.weaponLocalPosition = Vector3.zero;
            look.weaponLocalScale = Vector3.one;
            if (weapon.holdEuler.HasValue) look.weaponLocalEuler = weapon.holdEuler.Value;
            look.offHandLocalPosition = Vector3.zero;
            look.offHandLocalEuler = Vector3.zero;
            if (look.carry == WeaponCarry.Back)
            {
                // Slid along the back as far as the class's own model is, for this one's length.
                Bounds theirs = like.weaponPrefab != null ? SidekickGearGenerator.LocalBounds(like.weaponPrefab.transform) : bounds;
                float tuned = like.carryLocalPosition.z - SidekickGearGenerator.BackCarryShift(theirs.min.z, theirs.max.z);
                look.carryLocalPosition = new Vector3(0f, 0f, SidekickGearGenerator.BackCarryShift(bounds.min.z, bounds.max.z) + tuned);
            }
            float reach = Mathf.Max(0.3f, bounds.max.z);
            look.hitboxCenter = new Vector3(0f, bounds.center.y, reach * 0.5f + 0.05f);
            look.hitboxSize = new Vector3(Mathf.Max(0.18f, bounds.size.x), Mathf.Max(0.18f, bounds.size.y), reach);
            EditorUtility.SetDirty(look);
            return look;
        }

        /// <summary>
        /// Rolls the model half a turn about its length when its bulk is on the other side from the class's own model:
        /// a pistol's handle (and drum) hang the way the hold expects, a glaive's blade sweeps the same way.
        /// </summary>
        private static void MatchSide(GameObject root, GameObject like)
        {
            if (like == null) return;
            float own = MeanY(root.transform), theirs = MeanY(like.transform);
            if (Mathf.Abs(own) < 0.004f || Mathf.Abs(theirs) < 0.004f || Mathf.Sign(own) == Mathf.Sign(theirs)) return;
            Transform model = root.transform.Find("Model");
            if (model != null) model.localRotation = Quaternion.AngleAxis(180f, Vector3.forward) * model.localRotation;
        }

        private static float MeanY(Transform root)
        {
            double sum = 0;
            int n = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                var m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                foreach (var v in f.sharedMesh.vertices) { sum += m.MultiplyPoint3x4(v).y; n++; }
            }
            return n > 0 ? (float)(sum / n) : 0f;
        }

        [Serializable]
        private class GripJson
        {
            public float blade_starts_at;
            public Hand off_hand;
            [Serializable] public class Hand { public float[] point; }
        }

        /// <summary>The player's model puts a two-handed weapon's second hand on its shaft.</summary>
        private static void EnsureOffHandIK()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var animator = root.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a.GetComponent("FootGroundingIK") != null);
                if (animator == null) { Debug.LogError("[Items] Player_01 has no Animator beside FootGroundingIK: OffHandGripIK not added."); return; }
                if (animator.TryGetComponent(out OffHandGripIK _)) return;
                animator.gameObject.AddComponent<OffHandGripIK>();
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Synty's Sidekick part import settings on every part FBX of the set.</summary>
        private static int ApplyImportSettings(string folder)
        {
            string refGuid = AssetDatabase.FindAssets(ImportReference).FirstOrDefault(g =>
                AssetDatabase.GUIDToAssetPath(g).EndsWith(ImportReference + ".fbx", StringComparison.Ordinal));
            var reference = refGuid != null ? AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(refGuid)) as ModelImporter : null;
            if (reference == null) { Debug.LogError($"[Items] {ImportReference}.fbx not found: no import settings to copy."); return 0; }
            var preset = new Preset(reference);
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                preset.ApplyTo(importer);
                importer.SaveAndReimport();
                count++;
            }
            return count;
        }

        private static ItemDefinition LookTemplate(Piece piece, Set set, SidekickPartCatalog catalog, ushort[] partIds)
        {
            string path = $"{SidekickGearGenerator.ItemsRoot}/{Folder(piece.slot)}/{SidekickGearGenerator.AssetPrefix(piece.slot)}_{set.code}_01.asset";
            var look = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (look == null)
            {
                look = ScriptableObject.CreateInstance<ItemDefinition>();
                AssetDatabase.CreateAsset(look, path);
            }
            look.displayName = piece.displayName;
            look.equipSlot = piece.slot;
            look.rarity = ItemRarity.Rare;
            look.maxStack = 1;
            look.partCatalog = catalog;
            look.appearancePartIds = partIds;
            look.appearanceParts = new List<SidekickPartRef>();
            // A painted set reads no colour cells (its paint is its own): it must not repaint cells other parts use.
            look.colorCells = set.painted ? Array.Empty<byte>() : GearThemeBuilder.PaintedCells(look);
            EditorUtility.SetDirty(look);
            return look;
        }

        private static string Folder(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Gloves: return "Gloves";
                case EquipSlot.Boots: return "Boots";
                case EquipSlot.Cape: return "Capes";
                default: return "Armor";
            }
        }

        /// <summary>The set's theme: its own colour in every cell its pieces paint (read from the swatch file).</summary>
        private static void WriteTheme(GearThemeLibrary library, string themeName, Set set)
        {
            var cells = new Color32[GearThemeLibrary.CellCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Color32(255, 255, 255, 255);
            var byIndex = ReadSwatchCells(set.code);
            foreach (var kv in byIndex) cells[kv.Key] = Hex(kv.Value);
            var r = set.roles;
            var theme = new GearThemeLibrary.Theme
            {
                name = themeName, style = GearThemeStyle.TwoPiece,
                primary = Hex(r[0]), secondary = Hex(r[1]), under = Hex(r[2]), accent = Hex(r[3]), trim = Hex(r[4]),
                leather = Hex(r[5]), glow = Hex(r[6]), metal = new[] { Hex(r[7]), Hex(r[8]), Hex(r[9]), Hex(r[10]) },
                cells = cells
            };
            ushort id = library.IdOf(themeName);
            if (id == 0) library.Add(theme);
            else
            {
                int index = library.Themes.ToList().FindIndex(t => t.id == id);
                theme.id = id;
                library.Set(index, theme);
            }
            EditorUtility.SetDirty(library);
            Debug.Log($"[Items] Theme {themeName}: {byIndex.Count} cells from {SwatchPath}.");
        }

        /// <summary>sets.&lt;code&gt;.cells_by_index of the swatch file: cell index (v * 16 + u) to "#RRGGBB".</summary>
        private static Dictionary<int, string> ReadSwatchCells(string code)
        {
            var result = new Dictionary<int, string>();
            string text = File.ReadAllText(SwatchPath);
            int set = text.IndexOf($"\"{code}\"", StringComparison.Ordinal);
            int block = set >= 0 ? text.IndexOf("\"cells_by_index\"", set, StringComparison.Ordinal) : -1;
            if (block < 0) { Debug.LogError($"[Items] {SwatchPath} has no cells_by_index for {code}: run the Blender cell read first."); return result; }
            int open = text.IndexOf('{', block), close = text.IndexOf('}', open);
            foreach (string pair in text.Substring(open + 1, close - open - 1).Split(','))
            {
                var kv = pair.Split(':');
                if (kv.Length != 2) continue;
                if (int.TryParse(kv[0].Trim().Trim('"'), out int index) && index >= 0 && index < GearThemeLibrary.CellCount)
                    result[index] = kv[1].Trim().Trim('"');
            }
            return result;
        }

        private static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out Color c);
            return c;
        }
    }
}
