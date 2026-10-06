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
        public const string ScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        public const string InteractableLayerName = "Interactable";
        private const string SourceScenePath = "Assets/_Game/Scenes/Development/SampleScene.unity";
        private const string NavMeshFolder = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown";
        private const string NpcDataFolder = "Assets/_Game/Gameplay/NPC/Data/Assets";
        private const string IdleControllerPath = "Assets/_Game/Characters/NPC/Animations/AC_NPC_Idle.controller";
        private const string IdleClipSource = "Assets/Synty/SidekickCharacters/_Demos/Animations/BodyCycles/A_Body_IdleSubtle.fbx";
        private const string CharacterFolder = "Assets/Synty/PolygonSciFiCity/Prefabs/Characters/";
        private const string PanelSettingsPath = "Assets/_Game/UI/HUD/ProgressionPanelSettings.asset";
        private const string NpcUxmlPath = "Assets/_Game/UI/NPC/NpcInteraction.uxml";

        private struct NpcSeed
        {
            public string Id, Model, Name, Title, Greeting, Services;
            public NpcRole Role;
            public NpcOffer Offers; // working services: a dialogue button each
        }

        private static readonly NpcSeed[] Npcs =
        {
            new NpcSeed { Id = "HerbMaster", Role = NpcRole.HerbMaster, Model = "SM_Chr_Medical_Male_01",
                Name = "Apothecary", Title = "Stims · Flasks · Recall Tags",
                Greeting = "Stims for the body, Flux for the mind. Stock up before you leave the walls.",
                Services = "Sells HP and Flux Stims, Flasks and Recall Tags (a quick trip back to town). Hands out starter Contracts and handles basic item combinations." },
            new NpcSeed { Id = "Blacksmith", Role = NpcRole.Blacksmith, Model = "SM_Chr_Muscle_Male_01",
                Name = "Armorer", Title = "Weapons · Armor · Tempering",
                Greeting = "Bring me steel and coin, and I'll make you something worth swinging.",
                Services = "Buys, sells and crafts weapons and armor. Tempering, Fusion and Tuning are done at this forge." },
            new NpcSeed { Id = "TrainerBlade", Role = NpcRole.Trainer, Model = "SM_Chr_Monk_Male_01",
                Name = "Monoblade Instructor", Title = "Combat Skills · Monoblade",
                Greeting = "A blade is only as sharp as the hand that guides it.",
                Services = "Hegemony Instructor for the Monoblade. Learn and upgrade your combat skills." },
            new NpcSeed { Id = "TrainerFist", Role = NpcRole.Trainer, Model = "SM_Chr_CyborgNinja_01",
                Name = "Servo Claws Instructor", Title = "Combat Skills · Servo Claws",
                Greeting = "Before the weapon, there is the body. Show me your stance.",
                Services = "Null Instructor for Servo Claws. Learn and upgrade your combat skills." },
            new NpcSeed { Id = "Trader", Role = NpcRole.Trader, Model = "SM_Chr_Rich_Female_01",
                Name = "Broker", Title = "The Exchange",
                Greeting = "Everything has a buyer. Everything.",
                Services = "With a Commission Receipt, list Rare gear (12% Temper or more) on the Exchange, or browse and buy gear listed by other players." },
            new NpcSeed { Id = "MallManager", Role = NpcRole.MallManager, Model = "SM_Chr_Hologram_Female_01",
                Name = "Store Clerk", Title = "Nova Store",
                Greeting = "Premium goods, premium service. How may I assist?",
                Services = "Runs the Nova Store: premium cosmetic goods and tickets." },
            new NpcSeed { Id = "GuardCaptain", Role = NpcRole.GuardCaptain, Model = "SM_Chr_Cop_01",
                Name = "Marshal", Title = "Contracts · Battle Transit",
                Greeting = "Ready to earn your keep? The frontier won't defend itself.",
                Services = "Standard leveling Contracts, and Transit to level-appropriate battles and Runs." },
            new NpcSeed { Id = "Agent", Role = NpcRole.Agent, Model = "SM_Chr_Hacker_Female_01",
                Name = "Fixer", Title = "Slotting · Cores",
                Greeting = "Slots, Cores, cutters. Paid in Renown.",
                Services = "Slotting: buy Slot Cutters with Renown to cut Core slots into your gear." },
            // Ids match the plaza's stall table (StarterTownBuilder.PlazaStalls): Crew Hall and Wayfinder stalls.
            new NpcSeed { Id = "GuildDirector", Role = NpcRole.Vaultkeeper, Model = "SM_Chr_Rich_Male_01",
                Name = "Vaultkeeper", Title = "Vault · Crew Registry", Offers = NpcOffer.Vault | NpcOffer.CrewRegistry,
                Greeting = "What you leave with me stays yours. What you sign here binds a Crew.",
                Services = "Keeps your Vault: storage for this character's items and Creds. Registers new Crews." },
            new NpcSeed { Id = "GateMaster", Role = NpcRole.Wayfinder, Model = "SM_Chr_Android_Female_01",
                Name = "Wayfinder", Title = "Deep Vault · Transit", Offers = NpcOffer.DeepVault,
                Greeting = "Every road starts here. So does everything you want waiting at the end of one.",
                Services = "Opens your Deep Vault: storage shared by every character of your account. Transit to other zones comes later." },
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
            if (RuinBaker.Library().Count == 0)
                Debug.LogWarning("[Zone01] No ruins baked yet: run Krytical Nova > Zone > Bake Ruin Library (Quick Destruction), then Build Starter Town.");
            StarterTownBuilder.Build();
            CityTrafficBuilder.Build();
            // Wind materials on town foliage, then grow grass now that roads/sidewalks/buildings exist to mask it.
            FoliageAssetBuilder.ApplyToScene();
            ZoneTerrainGenerator.RepaintDetailsMenu();
            GrassSystemBuilder.BuildAll();
            EnsureZoneUi();
            EnsureTradeZone();
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

        // The zone is no longer the boot scene: the front end comes first (see BuildSceneOrder).
        private static void UpdateBuildSettings() => BuildSceneOrder.Apply();

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
        internal static void AddInteractableToPlayerRaycasts()
        {
            int layer = LayerMask.NameToLayer(InteractableLayerName);
            if (layer < 0) return;

            foreach (var player in Object.FindObjectsByType<PointAndClickPlayer>(FindObjectsInactive.Include))
                AddLayer(player, layer);

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }))
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

        internal static void EnsureZoneUi()
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

        // ---------------------------------------------------------------- stalls that opened since the town was built

        /// <summary>
        /// Brings a town built earlier in line with the stall table, without rebuilding it: every stall that now has
        /// an NPC but still stands as a "future" slot in the open scene gets its vendor and a lit sign. Does nothing
        /// in a scene without such stalls, so it is safe on any zone. Returns how many it opened.
        /// </summary>
        public static int OpenReadyStalls()
        {
            CreateNpcAssets();
            int opened = StarterTownBuilder.OpenReadyStallsInScene();
            if (opened > 0)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                Debug.Log($"[Zone01] {opened} stall(s) opened. Save the scene to keep them.");
            }
            return opened;
        }

        // ---------------------------------------------------------------- commerce

        private const string TradeZoneName = "Trade Zone - Central Plaza";
        private const string NeutralZoneName = "Neutral Ground - Starter Town (for testing)";

        /// <summary>
        /// The starter town's two zones for dealings between players. The Central Plaza is where Kiosks may be set
        /// up. The whole walled town is neutral ground for now (factions may trade, buy and befriend each other
        /// there) so cross-faction play can be tested before the dedicated non-combat zone exists: untick Cross
        /// Faction on it, or delete it, when that zone is built. Each is created once and then left alone, so its
        /// size and rules can be tuned in the Inspector. Does nothing in any other scene. Returns true when it added one.
        /// </summary>
        public static bool EnsureTradeZone()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath) return false;
            // The plaza: 30 m either side of the beacon. The town: out to the wall.
            bool added = EnsureZone(TradeZoneName, "Central Plaza", new Vector2(60f, 60f), kiosks: true, neutral: false);
            float town = StarterTownBuilder.WallHalf * 2f;
            added |= EnsureZone(NeutralZoneName, "Starter Town", new Vector2(town, town), kiosks: false, neutral: true);
            return added;
        }

        private static bool EnsureZone(string objectName, string zoneName, Vector2 size, bool kiosks, bool neutral)
        {
            if (GameObject.Find(objectName) != null) return false;
            var zone = new GameObject(objectName).AddComponent<KryticalNova.Economy.TradeZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("zoneName").stringValue = zoneName;
            so.FindProperty("size").vector2Value = size;
            so.FindProperty("allowKiosks").boolValue = kiosks;
            so.FindProperty("crossFaction").boolValue = neutral;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
            Debug.Log($"[Zone01] Added '{objectName}'. Save the scene to keep it.");
            return true;
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

        /// <summary>Re-applies the names, text and services above to the NPC definitions that already exist (role and prefab untouched).</summary>
        [MenuItem("Krytical Nova/Zone/Refresh NPC Text From Seeds")]
        public static void RefreshNpcText()
        {
            foreach (var seed in Npcs)
            {
                var def = AssetDatabase.LoadAssetAtPath<NpcDefinition>($"{NpcDataFolder}/NPC_{seed.Id}.asset");
                if (def == null) continue;
                def.displayName = seed.Name;
                def.title = seed.Title;
                def.greeting = seed.Greeting;
                def.services = seed.Services;
                def.offers = seed.Offers;
                EditorUtility.SetDirty(def);
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
            def.offers = seed.Offers;
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
