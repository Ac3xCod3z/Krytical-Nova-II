using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Mounts;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the mounts from one table (owner, 25 Oct 2026): the import settings of every mount and rider file
    /// exported from Blender (Characters/Mounts), a prefab per growth stage, each mount's definition (box, two stats,
    /// three stage looks, rider clips), the item that registers it, the mount catalog and the mount settings.
    /// Four boxes: the Brambleback is built; the Grave Trot, Stone Horn and Circuit Strider have a box and their two
    /// stats but no art and no item yet. Re-runnable: what the table owns is rewritten; numbers tuned in the Inspector
    /// (stat maximums, settings) are only filled in when missing. Menu Krytical Nova ▸ Items ▸ 11. Build Mounts.
    /// </summary>
    public static class MountAssetBuilder
    {
        public const string MountsRoot = "Assets/_Game/Gameplay/Mounts/Data/Assets";
        public const string SettingsPath = MountsRoot + "/MountSettings.asset";
        public const string CatalogPath = MountsRoot + "/MountCatalog.asset";
        /// <summary>The mounts' own models and clips, one folder per stage (Brambleback_0, _50, _100) and Rider.</summary>
        public const string MountModels = "Assets/_Game/Characters/Mounts";
        private const string ItemFolder = SidekickGearGenerator.ItemsRoot + "/Mounts";
        private const string RiderFolder = MountModels + "/Rider/Animations";
        private static readonly int[] StagePercents = { 0, 50, 100 };
        private static readonly string[] MountClips = { "Walk", "Run", "IdleBreathe", "IdleSniff", "IdleLookAround" };
        private static readonly string[] RiderLoops = { "Rider_RideIdle", "Rider_RideWalk", "Rider_RideGallop", "Rider_RideLookAround" };

        private struct Row
        {
            public byte Box;
            public string Key, Name, Description, ItemDescription;
            public ushort ItemId;
            public StatType First, Second;
            public float FirstMax, SecondMax;
            public string[] StageNames;
            public Color[] Glows;
            /// <summary>Turn of the model about its up axis, so it faces the way its rider does.</summary>
            public float Yaw;
            /// <summary>Metres the rider sits further toward the head than the seat it was keyed on, per stage.</summary>
            public float[] SeatForward;
        }

        // Stats (owner, 25 Oct 2026): two of the four main stats per mount, every mount a different pair, so each main stat
        // is on two mounts. The maximums are a first pick, to tune.
        private static readonly Row[] Rows =
        {
            new Row { Box = 1, Key = "Brambleback", Name = "Brambleback", ItemId = 8101,
                Description = "A mossy, bramble-backed beast that carries its rider through the wilds; its armour grows as it does.",
                ItemDescription = "Use it to add the Brambleback to your Mount window. Select it there and press the Ride key to ride.",
                First = StatType.Vitality, FirstMax = 50f, Second = StatType.Strength, SecondMax = 50f,
                StageNames = new[] { "Sapling", "Thornhide", "Elder Bramble" },
                SeatForward = new[] { 0f, 0f, 0.07f }, // owner, 25 Oct 2026: on the 100 % the rider sat too far back
                Glows = new[] { new Color(0.55f, 0.85f, 0.35f), new Color(0.35f, 0.9f, 0.45f), new Color(0.95f, 0.8f, 0.3f) } },
            new Row { Box = 2, Key = "GraveTrot", Name = "Grave Trot",
                Description = "Not in the game yet.",
                First = StatType.Spirit, FirstMax = 50f, Second = StatType.Agility, SecondMax = 50f },
            new Row { Box = 3, Key = "StoneHorn", Name = "Stone Horn",
                Description = "Not in the game yet.",
                First = StatType.Strength, FirstMax = 50f, Second = StatType.Spirit, SecondMax = 50f },
            new Row { Box = 4, Key = "CircuitStrider", Name = "Circuit Strider",
                Description = "Not in the game yet.",
                First = StatType.Agility, FirstMax = 50f, Second = StatType.Vitality, SecondMax = 50f },
        };

        [MenuItem("Krytical Nova/Items/11. Build Mounts")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(MountsRoot);
            WeatherVfxBuilder.EnsureFolder(ItemFolder);
            ConfigureImports();

            var settings = LoadOrCreate<MountSettings>(SettingsPath);
            var definitions = new List<MountDefinition>(Rows.Length);
            var items = new List<ItemDefinition>();
            foreach (Row row in Rows)
            {
                MountDefinition definition = Definition(row);
                definitions.Add(definition);
                if (row.ItemId == 0) continue;
                ItemDefinition item = Item(row, definition);
                items.Add(item);
            }
            var catalog = LoadOrCreate<MountCatalog>(CatalogPath);
            catalog.SetContents(definitions, settings);
            EditorUtility.SetDirty(catalog);
            RegisterItems(items);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Mounts] {definitions.Count} mount boxes built ({string.Join(", ", definitions.Where(d => d.IsBuilt).Select(d => d.displayName))} rideable). " +
                      "Next: Krytical Nova/Items/Bake Mount Icons, then Krytical Nova/Setup/Player Prefab.");
        }

        // ------------------------------------------------------------------ import settings

        /// <summary>Sets up every mount model, mount clip and rider clip exported from Blender. Safe to run again.</summary>
        public static void ConfigureImports()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (Row row in Rows)
                foreach (int percent in StagePercents)
                {
                    string folder = StageFolder(row, percent);
                    string model = $"{folder}/SK_{row.Key}_{percent}.fbx";
                    if (!File.Exists(model)) continue;
                    ConfigureTextures(folder);
                    ConfigureModel(model, row.Key, percent);
                    Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Avatar>().FirstOrDefault();
                    foreach (string clip in MountClips)
                    {
                        string path = $"{folder}/Animations/A_{row.Key}_{percent}_{clip}.fbx";
                        if (File.Exists(path)) ConfigureMountClip(path, "BB_" + clip, avatar, true); // every mount clip is a loop
                    }
                }
            ConfigureRiderClips();
        }

        private static string StageFolder(in Row row, int percent) => $"{MountModels}/{row.Key}_{percent}";

        private static void ConfigureTextures(string folder)
        {
            foreach (string path in Directory.GetFiles(folder, "T_*.png"))
            {
                string assetPath = path.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(assetPath) is TextureImporter importer)) continue;
                if (importer.maxTextureSize == 2048 && importer.sRGBTexture && importer.mipmapEnabled) continue;
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }
        }

        private static void ConfigureModel(string path, string key, int percent)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.bakeAxisConversion = false; // the rig keeps Blender's turn on its nodes (see the export script)
            // Keep the rig's own node: folded into the root, its turn would be keyed onto the whole model by the clips.
            importer.preserveHierarchy = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            // Each mesh's material is a URP Lit one beside the model, on the stage's own 2K texture.
            string folder = Path.GetDirectoryName(path).Replace('\\', '/');
            foreach (string part in new[] { "Body", "Saddle", "Armor" })
            {
                string texturePath = $"{folder}/T_{key}_{percent}_{part}.png";
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                if (texture == null) continue;
                Material material = LitMaterial($"{folder}/M_{key}_{percent}_{part}.mat", texture);
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), $"M_{key}_{percent}_{part}"), material);
            }
            importer.SaveAndReimport();
        }

        private static Material LitMaterial(string path, Texture2D texture)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetFloat("_Smoothness", 0.15f);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ConfigureMountClip(string path, string clipName, Avatar avatar, bool loop)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            importer.animationType = ModelImporterAnimationType.Generic;
            if (avatar != null)
            {
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
            }
            importer.importAnimation = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.bakeAxisConversion = false;
            importer.preserveHierarchy = true; // the same paths as the model (BB<N>_GameRig/DEF-spine.004/...)
            ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
            if (defaults.Length == 0) { importer.SaveAndReimport(); return; }
            ModelImporterClipAnimation clip = defaults[0];
            clip.name = clipName;
            clip.loopTime = loop;
            clip.loopPose = false;
            importer.clipAnimations = new[] { clip };
            importer.SaveAndReimport();
        }

        private static void ConfigureRiderClips()
        {
            if (!Directory.Exists(RiderFolder)) return;
            foreach (string file in Directory.GetFiles(RiderFolder, "A_Rider_*.fbx"))
            {
                string path = file.Replace('\\', '/');
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                string take = Path.GetFileNameWithoutExtension(path).Substring("A_".Length);
                // A Humanoid of its own: the export keeps the armature's node (Rig_CivilianM) above the Sidekick skeleton,
                // so Sidekick's avatar cannot be copied; a Humanoid clip plays on the player's avatar all the same.
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.importAnimation = true;
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                ModelImporterClipAnimation[] defaults = importer.defaultClipAnimations;
                if (defaults.Length == 0) { importer.SaveAndReimport(); continue; }
                ModelImporterClipAnimation clip = defaults[0];
                clip.name = take;
                clip.loopTime = RiderLoops.Any(take.StartsWith); // Rider_RideIdle_50 and the like
                // Made on the seat in Blender: the body keeps exactly the height and place it was keyed at, so it sits
                // in the saddle when the rider's origin is put on the seat socket.
                clip.lockRootRotation = true;
                clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true;
                clip.keepOriginalPositionY = true;
                clip.heightFromFeet = false;
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                importer.clipAnimations = new[] { clip };
                importer.SaveAndReimport();
            }
        }

        // ------------------------------------------------------------------ prefabs and definitions

        /// <summary>The stage's prefab: the model under a root turned to face the rider's way. Null when the stage has no model.</summary>
        private static GameObject StagePrefab(in Row row, int percent)
        {
            string folder = StageFolder(row, percent);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/SK_{row.Key}_{percent}.fbx");
            if (model == null) return null;
            string path = $"{folder}/PF_Mount_{row.Key}_{percent}.prefab";
            var root = new GameObject($"PF_Mount_{row.Key}_{percent}");
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.Euler(0f, row.Yaw, 0f);
                foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                foreach (SkinnedMeshRenderer skinned in instance.GetComponentsInChildren<SkinnedMeshRenderer>())
                    skinned.updateWhenOffscreen = false;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AnimationClip ClipIn(string path)
        {
            if (!File.Exists(path)) return null;
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        /// <summary>The rider's origin on each stage's seat, read from the export (Blender metres: x right, y back, z up).</summary>
        private static Vector3 Seat(int percent)
        {
            string json = $"{RiderFolder}/seats_blender.json";
            if (!File.Exists(json)) return Vector3.zero;
            var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(json),
                $"\"{percent}\"\\s*:\\s*\\[\\s*([-0-9.eE]+)\\s*,\\s*([-0-9.eE]+)\\s*,\\s*([-0-9.eE]+)\\s*\\]");
            if (!match.Success) return Vector3.zero;
            float x = float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            float y = float.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            float z = float.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Blender's -Y is the model's forward, which comes into Unity as +Z; up stays up.
            return new Vector3(-x, z, -y);
        }

        /// <summary>
        /// Where each stage's rider has their ankles on the stirrups (RiderSeat_&lt;pct&gt;.json, written by
        /// rider_clips_stage.py: Blender metres from the mount rig), in the mount model's space.
        /// </summary>
        private static void Stirrups(int percent, out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            string json = $"{RiderFolder}/RiderSeat_{percent}.json";
            if (!File.Exists(json)) return;
            string text = File.ReadAllText(json);
            left = Ankle(text, "l");
            right = Ankle(text, "r");
        }

        private static Vector3 Ankle(string json, string side)
        {
            var match = System.Text.RegularExpressions.Regex.Match(json,
                "\"" + side + "\"" + @"\s*:\s*\[\s*([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\s*\]");
            if (!match.Success) return Vector3.zero;
            var c = System.Globalization.CultureInfo.InvariantCulture;
            float x = float.Parse(match.Groups[1].Value, c), y = float.Parse(match.Groups[2].Value, c), z = float.Parse(match.Groups[3].Value, c);
            return new Vector3(-x, z, -y);
        }

        private static MountDefinition Definition(in Row row)
        {
            string path = $"{MountsRoot}/MountDef_{row.Key}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<MountDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MountDefinition>();
                definition.firstMax = row.FirstMax;
                definition.secondMax = row.SecondMax;
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.mountId = row.Box;
            definition.displayName = row.Name;
            definition.description = row.Description;
            // A stat the table changes takes the table's maximum with it (a tuned maximum of the same stat is kept).
            if (definition.firstStat != row.First) definition.firstMax = row.FirstMax;
            if (definition.secondStat != row.Second) definition.secondMax = row.SecondMax;
            definition.firstStat = row.First;
            definition.secondStat = row.Second;

            if (definition.stages == null || definition.stages.Length != MountRules.StageCount)
                definition.stages = new MountStageLook[MountRules.StageCount];
            for (int stage = 0; stage < MountRules.StageCount; stage++)
            {
                int percent = StagePercents[stage];
                MountStageLook look = definition.stages[stage];
                if (row.StageNames != null) look.name = row.StageNames[stage];
                if (row.Glows != null && look.glow.a <= 0f) look.glow = row.Glows[stage];
                look.model = StagePrefab(row, percent);
                string folder = $"{StageFolder(row, percent)}/Animations/A_{row.Key}_{percent}_";
                look.idle = ClipIn(folder + "IdleBreathe.fbx");
                look.walk = ClipIn(folder + "Walk.fbx");
                look.run = ClipIn(folder + "Run.fbx");
                look.idleExtras = new[] { ClipIn(folder + "IdleSniff.fbx"), ClipIn(folder + "IdleLookAround.fbx") }.Where(c => c != null).ToArray();
                look.seat = look.model != null ? Seat(percent) : Vector3.zero;
                if (look.model != null && row.SeatForward != null) look.seat.z += row.SeatForward[stage];
                Stirrups(percent, out look.stirrupLeft, out look.stirrupRight);
                if (look.model == null) look.stirrupLeft = look.stirrupRight = Vector3.zero;
                string rider = $"{RiderFolder}/A_Rider_";
                look.riderIdle = ClipIn($"{rider}RideIdle_{percent}.fbx");
                look.riderWalk = ClipIn($"{rider}RideWalk_{percent}.fbx");
                look.riderRun = ClipIn($"{rider}RideGallop_{percent}.fbx");
                look.mountUp = ClipIn($"{rider}MountUp_{percent}.fbx");
                look.dismount = ClipIn($"{rider}Dismount_{percent}.fbx");
                look.riderIdleExtras = new[] { ClipIn($"{rider}RideLookAround_{percent}.fbx"), ClipIn($"{rider}RidePatNeck_{percent}.fbx") }
                    .Where(c => c != null).ToArray();
                definition.stages[stage] = look;
            }

            bool built = definition.stages[0].model != null;
            definition.itemId = built ? row.ItemId : (ushort)0;
            // The mount's own rider clips are its first stage's (each stage has its own; these are the fallback).
            MountStageLook first = definition.stages[0];
            definition.riderIdle = built ? first.riderIdle : null;
            definition.riderWalk = built ? first.riderWalk : null;
            definition.riderRun = built ? first.riderRun : null;
            definition.riderIdleExtras = built ? first.riderIdleExtras : new AnimationClip[0];
            definition.mountUp = built ? first.mountUp : null;
            definition.dismount = built ? first.dismount : null;
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static ItemDefinition Item(in Row row, MountDefinition definition)
        {
            string path = $"{ItemFolder}/Mount_{row.Key}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = row.ItemId;
                AssetDatabase.CreateAsset(item, path);
            }
            if (item.itemId != row.ItemId)
                Debug.LogError($"[Mounts] {path} has item id {item.itemId}, the table says {row.ItemId}. Ids are never renumbered.", item);
            item.displayName = row.Name;
            item.description = row.ItemDescription;
            item.rarity = ItemRarity.Rare;
            item.equipSlot = EquipSlot.None;
            item.maxStack = 1;
            item.itemLevel = 0;
            item.useEffect = ItemUseEffect.RegisterMount;
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(MountStageIconBaker.ItemIconPath(row.Key));
            if (icon != null) item.icon = icon;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>Mount items belong in the item catalog like every other item.</summary>
        private static void RegisterItems(List<ItemDefinition> mounts)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[Mounts] No item catalog yet: run Krytical Nova/Items/1 and 6, then build the mounts again.");
                return;
            }
            var items = catalog.Items.Where(i => i != null).Concat(mounts).Distinct().OrderBy(i => i.itemId).ToList();
            catalog.SetContents(items, catalog.StarterEquipped.ToList(), catalog.StarterBag.ToList());
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
