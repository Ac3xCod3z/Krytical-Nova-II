using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the small networked prefabs gameplay systems put into the world: the item drop today, the Kiosk
    /// stand-in later. Each is created once and then left alone, so what is tuned on the prefab survives; delete a
    /// prefab to have it rebuilt. Runs as part of Krytical Nova ▸ Setup ▸ Player Prefab, because the player's
    /// components are what spawn them.
    /// A prefab the server spawns must also be known to every client's NetworkManager, or the client cannot create
    /// it and never sees the object: this step keeps the project's network prefab list complete and makes sure the
    /// NetworkManager prefab, and its instances in scenes, use that list.
    /// </summary>
    public static class GameplayPrefabsSetup
    {
        public const string WorldDropPath = "Assets/_Game/Gameplay/Items/Prefabs/WorldDrop.prefab";
        public const string KioskStandPath = "Assets/_Game/Gameplay/Economy/Prefabs/KioskStand.prefab";
        private const string KioskTable = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_MarketTable_02.prefab";
        private const string PlayerModelPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        public const string NetworkManagerPrefabPath = "Assets/_Game/Networking/Prefabs/NetworkManager.prefab";
        // Kept by Netcode itself (Project Settings ▸ Netcode for GameObjects): every prefab with a NetworkObject.
        public const string NetworkPrefabsListPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string PrefabListsProperty = "NetworkConfig.Prefabs.NetworkPrefabsLists";
        /// <summary>Every prefab this project spawns over the network, the player aside. A new one adds its path here.</summary>
        private static readonly string[] SpawnedPrefabs = { WorldDropPath, KioskStandPath };
        /// <summary>Every prefab in this folder is spawned over the network too: one per Hostile species, made by the Hostile builder.</summary>
        public const string HostilePrefabFolder = "Assets/_Game/Gameplay/Hostiles/Prefabs";
        private const string DropModel = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/SM_Prop_Box_Supplies_01.prefab";
        private const float DropSize = 0.42f;   // metres across: reads as a parcel, not as furniture
        private const float DropHover = 0.28f;

        public static void Run()
        {
            EnsureWorldDrop();
            EnsureKioskStand();
            EnsureRegistered();
            AssetDatabase.SaveAssets();
        }

        /// <summary>The spawned prefabs are in the network prefab list, and the NetworkManager prefab uses that list.</summary>
        public static void EnsureRegistered()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            if (list == null)
            {
                Debug.LogError($"[Setup] No network prefab list at {NetworkPrefabsListPath}: clients will not see spawned objects.");
                return;
            }
            var paths = new System.Collections.Generic.List<string>(SpawnedPrefabs);
            if (AssetDatabase.IsValidFolder(HostilePrefabFolder))
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { HostilePrefabFolder }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (string path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || list.Contains(prefab)) continue;
                list.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(list);
                Debug.Log($"[Setup] Registered {path} as a network prefab.");
            }

            GameObject root = PrefabUtility.LoadPrefabContents(NetworkManagerPrefabPath);
            try
            {
                var manager = root != null ? root.GetComponent<NetworkManager>() : null;
                if (manager == null) { Debug.LogError($"[Setup] {NetworkManagerPrefabPath} has no NetworkManager."); return; }
                var so = new SerializedObject(manager);
                if (!UsesList(so, list))
                {
                    SerializedProperty lists = so.FindProperty(PrefabListsProperty);
                    lists.arraySize = 1;
                    lists.GetArrayElementAtIndex(0).objectReferenceValue = list;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, NetworkManagerPrefabPath);
                    Debug.Log($"[Setup] {NetworkManagerPrefabPath} now uses {NetworkPrefabsListPath}.");
                }
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// The open scene's NetworkManager takes its prefab list from the NetworkManager prefab: a scene that
        /// overrides the list with its own (or an empty one) is put back. Returns true when the scene changed.
        /// </summary>
        public static bool EnsureSceneUsesPrefabList()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsListPath);
            bool changed = false;
            foreach (var manager in Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include))
            {
                var so = new SerializedObject(manager);
                if (UsesList(so, list)) continue;
                SerializedProperty lists = so.FindProperty(PrefabListsProperty);
                if (PrefabUtility.IsPartOfPrefabInstance(manager)) PrefabUtility.RevertPropertyOverride(lists, InteractionMode.AutomatedAction);
                so.Update();
                if (!UsesList(so, list))
                {
                    lists = so.FindProperty(PrefabListsProperty);
                    lists.arraySize = 1;
                    lists.GetArrayElementAtIndex(0).objectReferenceValue = list;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                changed = true;
                Debug.Log($"[Setup] NetworkManager '{manager.name}' in the open scene now uses {NetworkPrefabsListPath}.");
            }
            return changed;
        }

        private static bool UsesList(SerializedObject manager, NetworkPrefabsList list)
        {
            SerializedProperty lists = manager.FindProperty(PrefabListsProperty);
            if (lists == null || list == null) return false;
            for (int i = 0; i < lists.arraySize; i++)
                if (lists.GetArrayElementAtIndex(i).objectReferenceValue == list) return true;
            return false;
        }

        private static void EnsureWorldDrop()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(WorldDropPath) != null) return;
            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(WorldDropPath).Replace('\\', '/'));

            var root = new GameObject("WorldDrop") { layer = Mathf.Max(0, Zone01Setup.EnsureInteractableLayer()) };
            try
            {
                root.AddComponent<NetworkObject>();
                var collider = root.AddComponent<SphereCollider>();
                collider.center = new Vector3(0f, DropHover, 0f);
                collider.radius = 0.45f;

                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, DropHover, 0f);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(DropModel);
                if (model != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visual.transform);
                    foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                    Fit(instance.transform, DropSize);
                }
                else Debug.LogWarning($"[Setup] Drop model not found at {DropModel}; the drop has no visual.");
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;

                var drop = root.AddComponent<WorldDrop>();
                var so = new SerializedObject(drop);
                so.FindProperty("catalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
                so.FindProperty("visual").objectReferenceValue = visual.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, WorldDropPath);
                Debug.Log($"[Setup] Created {WorldDropPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// A Kiosk as it stands in the world: a click target, a place for the stand-in (a doll of the player model
        /// built at run time in the owner's look) and a market table in front of it.
        /// </summary>
        private static void EnsureKioskStand()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(KioskStandPath) != null) return;
            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(KioskStandPath).Replace('\\', '/'));

            var root = new GameObject("KioskStand") { layer = Mathf.Max(0, Zone01Setup.EnsureInteractableLayer()) };
            try
            {
                root.AddComponent<NetworkObject>();
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, 0.92f, 0.4f);
                capsule.radius = 0.75f;
                capsule.height = 1.9f;

                var anchor = new GameObject("Stand-In");
                anchor.transform.SetParent(root.transform, false);

                var table = AssetDatabase.LoadAssetAtPath<GameObject>(KioskTable);
                if (table != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(table, root.transform);
                    instance.transform.localPosition = new Vector3(0f, 0f, 0.95f);
                    instance.transform.localRotation = Quaternion.identity;
                    foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                }
                else Debug.LogWarning($"[Setup] Kiosk table not found at {KioskTable}; the stand has no counter.");
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;

                var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSystemsSetup.PlayerPrefabPath);
                var motor = player != null ? player.GetComponent<PlayerAnimancerMotor>() : null;
                var identity = player != null ? player.GetComponent<PlayerIdentity>() : null;

                var stand = root.AddComponent<KioskStand>();
                var so = new SerializedObject(stand);
                so.FindProperty("dollModel").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
                so.FindProperty("dollIdle").objectReferenceValue = motor != null ? motor.IdleClip : null;
                so.FindProperty("database").objectReferenceValue = identity != null ? identity.Database : null;
                so.FindProperty("dollAnchor").objectReferenceValue = anchor.transform;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, KioskStandPath);
                Debug.Log($"[Setup] Created {KioskStandPath}.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Scales and centres a model under its parent so its largest side is <paramref name="size"/> metres.</summary>
        private static void Fit(Transform model, float size)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;
            bool any = false;
            var bounds = new Bounds();
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!any) return;
            float largest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = largest > 0.001f ? size / largest : 1f;
            model.localScale = Vector3.one * scale;
            // Centre it on the parent: props keep their pivot at a corner or the floor.
            model.position -= (bounds.center - model.parent.position) * scale;
        }
    }
}
