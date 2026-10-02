using System.Collections.Generic;
using System.IO;
using KryticalNova.NPC;
using KryticalNova.Player;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// One-click, re-runnable Zone01 (starter town) setup: creates the scene from SampleScene, the
    /// Interactable layer, NPC data + prefabs, then runs terrain → VFX → city → traffic → UI → NavMesh → minimap →
    /// build settings.
    /// Hand-sculpted terrain, tuned NPC definitions and existing NPC prefabs are kept on re-runs.
    /// </summary>
    public static class Zone01Setup
    {
        public const string ScenePath = "Assets/Scenes/Zone01_StarterTown.unity";
        public const string InteractableLayerName = "Interactable";
        private const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        private const string NavMeshFolder = "Assets/Scenes/Zone01_StarterTown";
        private const string NpcDataFolder = "Assets/GameData/NPC";
        private const string IdleControllerPath = "Assets/Prefabs/NPC/AC_NPC_Idle.controller";
        private const string IdleClipSource = "Assets/Synty/SidekickCharacters/_Demos/Animations/BodyCycles/A_Body_IdleSubtle.fbx";
        private const string CharacterFolder = "Assets/Synty/PolygonSciFiCity/Prefabs/Characters/";
        private const string PanelSettingsPath = "Assets/UI/Progression/ProgressionPanelSettings.asset";
        private const string NpcUxmlPath = "Assets/UI/NPC/NpcInteraction.uxml";

        private struct NpcSeed
        {
            public string Id, Model, Name, Title, Greeting, Services;
            public NpcRole Role;
        }

        private static readonly NpcSeed[] Npcs =
        {
            new NpcSeed { Id = "HerbMaster", Role = NpcRole.HerbMaster, Model = "SM_Chr_Medical_Male_01",
                Name = "Herb Master", Title = "Remedies · Scrolls · Combinations",
                Greeting = "Pills for the body, tabs for the spirit. Stock up before you leave the walls.",
                Services = "Sells health and mana pills, tabs, Calabash bottles and Scrolls of Return (quick teleport back to town). Issues entry quests and handles basic item combinations for valuable rewards." },
            new NpcSeed { Id = "Blacksmith", Role = NpcRole.Blacksmith, Model = "SM_Chr_Muscle_Male_01",
                Name = "Blacksmith", Title = "Weapons · Armor · Crafting",
                Greeting = "Bring me steel and coin, and I'll make you something worth swinging.",
                Services = "Buys, sells, upgrades and crafts early-game weapons and armor." },
            new NpcSeed { Id = "TrainerBlade", Role = NpcRole.Trainer, Model = "SM_Chr_Monk_Male_01",
                Name = "Blade Trainer", Title = "Martial Arts · Blade",
                Greeting = "A blade is only as sharp as the hand that guides it.",
                Services = "Faction martial arts instructor specialising in blades. Learn and upgrade your combat skills." },
            new NpcSeed { Id = "TrainerFist", Role = NpcRole.Trainer, Model = "SM_Chr_CyborgNinja_01",
                Name = "Fist Trainer", Title = "Martial Arts · Unarmed",
                Greeting = "Before the weapon, there is the body. Show me your stance.",
                Services = "Faction martial arts instructor specialising in unarmed styles. Learn and upgrade your combat skills." },
            new NpcSeed { Id = "Trader", Role = NpcRole.Trader, Model = "SM_Chr_Rich_Female_01",
                Name = "Trader", Title = "Community Marketplace",
                Greeting = "Everything has a buyer. Everything.",
                Services = "With a Commission Receipt, list rare items (12% enchant or more) for sale, or browse and buy gear listed by other players." },
            new NpcSeed { Id = "MallManager", Role = NpcRole.MallManager, Model = "SM_Chr_Hologram_Female_01",
                Name = "Mall Manager", Title = "Item Mall Services",
                Greeting = "Premium goods, premium service. How may I assist?",
                Services = "Manages Item Mall functions: downgrading, extracting and processing premium visual goods and tickets." },
            new NpcSeed { Id = "GuardCaptain", Role = NpcRole.GuardCaptain, Model = "SM_Chr_Cop_01",
                Name = "Guard Captain", Title = "Quests · Battle Teleports",
                Greeting = "Ready to earn your keep? The frontier won't defend itself.",
                Services = "Standard leveling quests, and the gateway to level-appropriate formation battles, resistance battles and master maps." },
            new NpcSeed { Id = "Agent", Role = NpcRole.Agent, Model = "SM_Chr_Hacker_Female_01",
                Name = "Agent", Title = "Enhancement · Penetration",
                Greeting = "Sockets, slots, penetration. Paid in Contribution Points.",
                Services = "Item enhancement: buy penetration materials with Contribution Points (CP) to add socket slots to your gear." },
        };

        // ---------------------------------------------------------------- all steps

        [MenuItem("Krytical Nova/Zone/Build Zone01 (All Steps)")]
        public static void BuildAll()
        {
            if (!OpenZoneScene()) return;
            EnsureInteractableLayer();
            CreateNpcAssets();
            RemoveSampleTerrain();
            ZoneTerrainGenerator.Generate(false);
            PortalVfxBuilder.BuildAll();
            ColorBible.ApplyAll();
            PostProcessSetup.Apply();
            if (RuinDemolisher.Library().Count == 0)
                Debug.LogWarning("[Zone01] No demolished ruins baked yet: run Krytical Nova > Zone > Bake Demolished Ruin Library, then Build Starter Town.");
            StarterTownBuilder.Build();
            CityTrafficBuilder.Build();
            // Wind materials on town foliage, then grow grass now that roads/sidewalks/buildings exist to mask it.
            FoliageAssetBuilder.ApplyToScene();
            ZoneTerrainGenerator.RepaintDetailsMenu();
            EnsureZoneUi();
            AddInteractableToPlayerRaycasts();
            BakeNavMesh();
            MinimapBaker.Bake();
            UpdateBuildSettings();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[Zone01] Starter town built and saved: " + ScenePath);
        }

        // ---------------------------------------------------------------- scene

        /// <summary>Creates the zone scene from SampleScene on first run, then makes it the open scene.</summary>
        public static bool OpenZoneScene()
        {
            if (!File.Exists(ScenePath))
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                {
                    Debug.LogError($"[Zone01] Could not copy {SourceScenePath} to {ScenePath}.");
                    return false;
                }
            }

            var active = EditorSceneManager.GetActiveScene();
            if (active.path == ScenePath) return true;
            if (active.isDirty)
            {
                Debug.LogError($"[Zone01] Save or discard changes in {active.name} first; the zone scene is opened in its place.");
                return false;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return true;
        }

        /// <summary>The copied SampleScene terrain is replaced by the generated zone terrain.</summary>
        private static void RemoveSampleTerrain()
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == "Terrain" && root.GetComponent<Terrain>() != null)
                    Object.DestroyImmediate(root);
        }

        [MenuItem("Krytical Nova/Zone/Rebake NavMesh")]
        public static void BakeNavMesh()
        {
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            if (surface == null) { Debug.LogError("[Zone01] No NavMeshSurface in the scene."); return; }

            surface.RemoveData();
            surface.navMeshData = null;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) { Debug.LogError("[Zone01] NavMesh bake produced no data."); return; }

            WeatherVfxBuilder.EnsureFolder(NavMeshFolder);
            string path = $"{NavMeshFolder}/NavMesh-{surface.name}.asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
            Debug.Log($"[Zone01] NavMesh baked to {path}.");
        }

        private static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ---------------------------------------------------------------- layer + raycasts

        public static int EnsureInteractableLayer()
        {
            int existing = LayerMask.NameToLayer(InteractableLayerName);
            if (existing >= 0) return existing;

            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 6; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(layer.stringValue)) continue;
                layer.stringValue = InteractableLayerName;
                tagManager.ApplyModifiedProperties();
                return i;
            }
            Debug.LogError("[Zone01] No free user layer for 'Interactable'.");
            return -1;
        }

        /// <summary>Clicks must be able to hit NPC capsules: add the layer to every player's raycast mask.</summary>
        private static void AddInteractableToPlayerRaycasts()
        {
            int layer = LayerMask.NameToLayer(InteractableLayerName);
            if (layer < 0) return;

            foreach (var player in Object.FindObjectsByType<PointAndClickPlayer>(FindObjectsInactive.Include))
                AddLayer(player, layer);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var player = prefab != null ? prefab.GetComponent<PointAndClickPlayer>() : null;
                if (player == null) continue;
                AddLayer(player, layer);
                PrefabUtility.SavePrefabAsset(prefab);
            }
        }

        private static void AddLayer(PointAndClickPlayer player, int layer)
        {
            var so = new SerializedObject(player);
            var mask = so.FindProperty("raycastLayers");
            if ((mask.intValue & (1 << layer)) != 0) return;
            mask.intValue |= 1 << layer;
            so.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------- UI

        private static void EnsureZoneUi()
        {
            // Looked up by name: the UI assembly is excluded from server builds, so it isn't referenced here.
            var uiType = System.Type.GetType("KryticalNova.UI.NpcInteractionUI, KryticalNova.UI");
            if (uiType == null) { Debug.LogError("[Zone01] NpcInteractionUI type not found."); return; }

            var ui = Object.FindAnyObjectByType(uiType, FindObjectsInactive.Include) as Component;
            GameObject go;
            if (ui == null)
            {
                go = new GameObject("Zone UI");
                Undo.RegisterCreatedObjectUndo(go, "Create Zone UI");
                go.AddComponent<UIDocument>();
                go.AddComponent(uiType);
            }
            else go = ui.gameObject;

            var doc = go.GetComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(NpcUxmlPath);
            doc.sortingOrder = 1;
        }

        // ---------------------------------------------------------------- NPC assets

        [MenuItem("Krytical Nova/Zone/Create NPC Assets")]
        public static void CreateNpcAssets()
        {
            int layer = EnsureInteractableLayer();
            WeatherVfxBuilder.EnsureFolder(NpcDataFolder);
            WeatherVfxBuilder.EnsureFolder(StarterTownBuilder.NpcPrefabFolder);
            var controller = IdleController();

            foreach (var seed in Npcs)
            {
                var definition = Definition(seed);
                string prefabPath = $"{StarterTownBuilder.NpcPrefabFolder}/NPC_{seed.Id}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null) continue; // keep hand edits

                var model = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFolder + seed.Model + ".prefab");
                if (model == null) { Debug.LogError($"[Zone01] Missing character {seed.Model}"); continue; }

                var root = new GameObject("NPC_" + seed.Id) { layer = Mathf.Max(0, layer) };
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                var animator = instance.GetComponentInChildren<Animator>();
                if (animator != null)
                {
                    animator.runtimeAnimatorController = controller;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.92f, 0f);
                capsule.radius = 0.45f;
                capsule.height = 1.85f;

                var npc = root.AddComponent<NpcInteractable>();
                var so = new SerializedObject(npc);
                so.FindProperty("definition").objectReferenceValue = definition;
                so.FindProperty("animator").objectReferenceValue = animator;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static NpcDefinition Definition(NpcSeed seed)
        {
            string path = $"{NpcDataFolder}/NPC_{seed.Id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<NpcDefinition>(path);
            if (def != null) return def;
            def = ScriptableObject.CreateInstance<NpcDefinition>();
            def.role = seed.Role;
            def.displayName = seed.Name;
            def.title = seed.Title;
            def.greeting = seed.Greeting;
            def.services = seed.Services;
            AssetDatabase.CreateAsset(def, path);
            return def;
        }

        private static RuntimeAnimatorController IdleController()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(IdleControllerPath);
            if (controller != null) return controller;

            AnimationClip clip = null;
            foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(IdleClipSource))
                if (asset is AnimationClip c && !c.name.StartsWith("__preview")) { clip = c; break; }
            if (clip == null) { Debug.LogWarning("[Zone01] Idle clip not found; NPCs will hold their bind pose."); return null; }
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(IdleControllerPath, clip);
        }
    }
}
