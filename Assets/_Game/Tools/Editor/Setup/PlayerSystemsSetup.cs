using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Adds the player-side component of each gameplay system to the networked Player prefab and hands it its
    /// settings asset. One table: a new system adds a row. Components and their settings are looked up by name, so
    /// a row for a system that is not in the project yet is skipped instead of breaking the build. A settings asset
    /// that does not exist yet is created with its defaults, so no system needs a "create my settings" menu item.
    /// An animation clip is taken from its FBX and only fills an empty field: a clip chosen in the Inspector is kept.
    /// </summary>
    public static class PlayerSystemsSetup
    {
        public const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        /// <summary>A component to ensure on the player, with the assets to assign to its serialized fields.</summary>
        private readonly struct Row
        {
            public readonly string Type;
            public readonly (string Field, string AssetPath)[] Assets;
            public Row(string type, params (string, string)[] assets) { Type = type; Assets = assets; }
        }

        private const string Runtime = ", KryticalNova.Runtime";
        public const string EconomySettingsPath = "Assets/_Game/Gameplay/Economy/Data/Assets/EconomySettings.asset";
        public const string GearProgressionPath = "Assets/_Game/Gameplay/Items/Data/Assets/GearProgressionSettings.asset";
        public const string NovaSettingsPath = "Assets/_Game/Gameplay/Progression/Data/Assets/NovaSettings.asset";
        private const string NovaShaderFolder = "Assets/_Game/Gameplay/Progression/Shaders/";
        public const string EmoteWheelSettingsPath = "Assets/_Game/Gameplay/Social/Data/Assets/EmoteWheelSettings.asset";
        public const string SerumSettingsPath = "Assets/_Game/Gameplay/Progression/Data/Assets/SerumSettings.asset";
        public const string SquadSettingsPath = "Assets/_Game/Gameplay/Social/Data/Assets/SquadSettings.asset";
        private const string ExpressionLibraryPath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterExpressionLibrary.asset";
        // No pack has a pick-up clip. The Base Locomotion crouch pair is made for this rig and reads as one:
        // the hand comes down to 0.48 m, the top of a drop.
        private const string Transitions = "Assets/Synty/AnimationBaseLocomotion/Animations/Sidekick/Masculine/Transitions/";
        private const string PickUpDownClip = Transitions + "Stand_ToCrouch/A_MOD_BL_Stand_ToCrouch_Masc.fbx";
        private const string PickUpUpClip = Transitions + "Crouch_ToStand/A_MOD_BL_Crouch_ToStand_Masc.fbx";

        private static readonly Row[] Rows =
        {
            new Row("KryticalNova.Persistence.PlayerSaveAgent"),
            new Row("KryticalNova.Persistence.PlayerHotbarMemory"),
            new Row("KryticalNova.Pets.PlayerPet", ("catalog", "Assets/_Game/Gameplay/Pets/Data/Assets/PetCatalog.asset")),
            new Row("KryticalNova.Economy.PlayerWallet", ("settings", EconomySettingsPath)),
            new Row("KryticalNova.Items.PlayerLoot", ("dropPrefab", GameplayPrefabsSetup.WorldDropPath)),
            new Row("KryticalNova.Economy.PlayerVault"),
            new Row("KryticalNova.Economy.PlayerDeepVault"),
            new Row("KryticalNova.Economy.PlayerTrade", ("settings", EconomySettingsPath)),
            new Row("KryticalNova.Economy.PlayerKiosk", ("settings", EconomySettingsPath), ("standPrefab", GameplayPrefabsSetup.KioskStandPath)),
            new Row("KryticalNova.Social.PlayerContacts"),
            new Row("KryticalNova.Social.PlayerCrew", ("settings", "Assets/_Game/Gameplay/Social/Data/Assets/CrewSettings.asset")),
            new Row("KryticalNova.Player.PlayerAnimancerMotor", ("pickUpDownClip", PickUpDownClip), ("pickUpUpClip", PickUpUpClip)),
            // Gear progression: worn gear reads its Temper / Fusion / Tuning, and the forge changes them.
            new Row("KryticalNova.Items.PlayerEquipment", ("progression", GearProgressionPath)),
            new Row("KryticalNova.Forge.PlayerGearWork", ("settings", GearProgressionPath)),
            // The Renown ladders at the Herald: Ascension (the Nova) and Nova Standings.
            new Row("KryticalNova.Progression.PlayerNova", ("settings", NovaSettingsPath)),
            // Emotes picked on the emote wheel.
            new Row("KryticalNova.Social.PlayerEmotes", ("settings", EmoteWheelSettingsPath)),
            // Following a Squad member.
            new Row("KryticalNova.Social.PlayerFollow", ("settings", SquadSettingsPath)),
            // Serums: what a character has taken, and the stats that stay.
            new Row("KryticalNova.Progression.PlayerSerums", ("settings", SerumSettingsPath)),
        };

        public static void Run()
        {
            if (Rows.Length == 0) return;
            GameplayPrefabsSetup.Run(); // the prefabs the player's systems put into the world
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                int added = 0;
                foreach (Row row in Rows)
                {
                    Type type = Type.GetType(row.Type + Runtime);
                    if (type == null) { Debug.LogWarning($"[Setup] Player component {row.Type} not found; skipped."); continue; }
                    Component component = root.GetComponent(type);
                    if (component == null) { component = root.AddComponent(type); added++; }

                    var so = new SerializedObject(component);
                    foreach ((string field, string assetPath) in row.Assets)
                    {
                        SerializedProperty property = so.FindProperty(field);
                        if (property != null && property.objectReferenceValue is AnimationClip) continue; // chosen by hand
                        var asset = LoadFor(type, field, assetPath);
                        if (asset == null && property != null) asset = CreateSettings(type, field, assetPath);
                        if (property == null) Debug.LogWarning($"[Setup] {row.Type} has no field '{field}'.");
                        else if (asset == null) Debug.LogWarning($"[Setup] {row.Type}.{field}: nothing at {assetPath}.");
                        else property.objectReferenceValue = asset;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                LinkGearProgression();
                LinkNovaMaterials();
                LinkEmoteLibrary();
                Debug.Log($"[Setup] Player systems wired ({Rows.Length} component(s), {added} added).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The emote wheel performs the motions of the library the character creator shows.</summary>
        private static void LinkEmoteLibrary()
        {
            var settings = AssetDatabase.LoadAssetAtPath<KryticalNova.Social.EmoteWheelSettings>(EmoteWheelSettingsPath);
            if (settings == null || settings.library != null) return;
            settings.library = AssetDatabase.LoadAssetAtPath<KryticalNova.CharacterCreation.CharacterExpressionLibrary>(ExpressionLibraryPath);
            if (settings.library == null) Debug.LogWarning($"[Setup] No expression library at {ExpressionLibraryPath}: the emote wheel will be empty.");
            EditorUtility.SetDirty(settings);
        }

        /// <summary>
        /// The four materials every character's Nova is drawn with: the star, the rings, the glow round it and the
        /// sparks in orbit. The settings hold them, so they ship in a build. Their numbers are set per tier at runtime.
        /// </summary>
        public static void LinkNovaMaterials()
        {
            var settings = AssetDatabase.LoadAssetAtPath<KryticalNova.Progression.NovaSettings>(NovaSettingsPath);
            if (settings == null) return;
            KryticalNova.Progression.NovaLookSettings look = settings.look;
            Material core = NovaMaterial("M_NovaCore", "KryticalNova/NovaCore");
            Material ring = NovaMaterial("M_NovaRing", "KryticalNova/NovaRing");
            Material corona = NovaMaterial("M_Nova", "KryticalNova/Nova");
            Material motes = NovaMaterial("M_NovaMotes", "KryticalNova/NovaMotes");
            if (look.coreMaterial == core && look.ringMaterial == ring && look.coronaMaterial == corona && look.motesMaterial == motes) return;
            look.coreMaterial = core;
            look.ringMaterial = ring;
            look.coronaMaterial = corona;
            look.motesMaterial = motes;
            EditorUtility.SetDirty(settings);
        }

        private static Material NovaMaterial(string name, string shaderName)
        {
            string path = NovaShaderFolder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Setup] Shader {shaderName} not found: part of every Nova will be missing.");
                return material;
            }
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
                EditorUtility.SetDirty(material);
            }
            return material;
        }

        /// <summary>The gear progression settings read rarity multipliers and level tiers from the stat table.</summary>
        private static void LinkGearProgression()
        {
            var settings = AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearProgressionSettings>(GearProgressionPath);
            var table = AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearStatTable>(GearStatBaker.StatTablePath);
            if (settings == null || table == null || settings.statTable == table) return;
            settings.statTable = table;
            EditorUtility.SetDirty(settings);
        }

        /// <summary>The asset at a path as the field wants it: a prefab's component when the field is of a component type.</summary>
        private static UnityEngine.Object LoadFor(Type componentType, string field, string assetPath)
        {
            FieldInfo info = componentType.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info != null && typeof(Component).IsAssignableFrom(info.FieldType))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                return prefab != null ? prefab.GetComponent(info.FieldType) : null;
            }
            if (info != null && info.FieldType == typeof(AnimationClip))
            {
                foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                    if (sub is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
                return null;
            }
            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        /// <summary>Creates the ScriptableObject a component field asks for, with its default values, at the given path.</summary>
        private static UnityEngine.Object CreateSettings(Type componentType, string field, string assetPath)
        {
            FieldInfo info = componentType.GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null || !typeof(ScriptableObject).IsAssignableFrom(info.FieldType) || info.FieldType.IsAbstract) return null;
            WeatherVfxBuilder.EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            ScriptableObject asset = ScriptableObject.CreateInstance(info.FieldType);
            AssetDatabase.CreateAsset(asset, assetPath);
            Debug.Log($"[Setup] Created {assetPath} with default values.");
            return asset;
        }
    }
}
