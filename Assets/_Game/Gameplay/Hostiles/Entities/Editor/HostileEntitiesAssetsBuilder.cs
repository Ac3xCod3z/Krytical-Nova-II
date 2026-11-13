using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.Player.Entities.Editor;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.Hostiles.Entities.Editor
{
    /// <summary>
    /// The Hostiles' entity-netcode assets (M5.5, plan s4): <see cref="PrefabPath"/> (an interpolated ghost with no owner, its
    /// <see cref="HostileGhostAuthoring"/>) and a <see cref="HostileGhostPrefabAuthoring"/> holder in the player ghost subscene
    /// (<see cref="PlayerGhostAssetsBuilder.SubScenePath"/>), beside the player's. Safe to run any time: an existing prefab and holder
    /// are updated in place, so the ghost type (the prefab's GUID) never changes. Menu Krytical Nova ▸ Hostiles ▸ Build DOTS Assets,
    /// or in batch <c>-executeMethod KryticalNova.Hostiles.Entities.Editor.HostileEntitiesAssetsBuilder.Build</c>.
    /// </summary>
    public static class HostileEntitiesAssetsBuilder
    {
        public const string PrefabFolder = "Assets/_Game/Gameplay/Hostiles/Entities/Prefabs";
        public const string PrefabPath = PrefabFolder + "/HostileGhost.prefab";
        /// <summary>The ghost's name: netcode hashes it into the ghost type, so it never changes.</summary>
        public const string GhostName = "HostileGhost";
        public const string HolderName = "HostileGhostPrefabHolder";
        /// <summary>The NGO species prefabs the server bodies are made from.</summary>
        public const string SpeciesFolder = "Assets/_Game/Gameplay/Hostiles/Prefabs";
        public const string BodyFolder = SpeciesFolder + "/ServerBody";

        [MenuItem("Krytical Nova/Hostiles/Build DOTS Assets")]
        public static void BuildMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            if (!Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogError("[KnBuild] Save the open scene first: the player ghost subscene is opened next to it.");
                return;
            }
            if (!File.Exists(PlayerGhostAssetsBuilder.SubScenePath))
            {
                Debug.LogError("[KnBuild] " + PlayerGhostAssetsBuilder.SubScenePath + " is missing: run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets first.");
                return;
            }
            if (!AssetDatabase.IsValidFolder(PrefabFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(PrefabFolder)?.Replace('\\', '/'), Path.GetFileName(PrefabFolder));
            GameObject prefab = EnsurePrefab();
            EnsureHolder(prefab);
            BuildBodies();
            AssetDatabase.SaveAssets();
            KeepBodiesOutOfNgoPrefabs();
            Debug.Log("[KnBuild] Hostile ghost assets: " + PrefabPath + " in " + PlayerGhostAssetsBuilder.SubScenePath);
        }

        static GameObject EnsurePrefab()
        {
            bool exists = File.Exists(PrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(GhostName);
            try
            {
                root.name = GhostName;
                var ghost = root.GetComponent<GhostAuthoringComponent>();
                if (ghost == null) ghost = root.AddComponent<GhostAuthoringComponent>();
                ghost.HasOwner = false;
                ghost.DefaultGhostMode = GhostMode.Interpolated;
                ghost.SupportedGhostModes = GhostModeMask.Interpolated;
                ghost.OptimizationMode = GhostOptimizationMode.Dynamic;   // they walk
                ghost.Importance = PlayerGhostAssetsBuilder.Importance(ghost.Importance, s => s.importanceHostile);
                if (root.GetComponent<HostileGhostAuthoring>() == null) root.AddComponent<HostileGhostAuthoring>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            AssetDatabase.ImportAsset(PrefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            StampPrefabId(prefab);
            return prefab;
        }

        /// <summary>
        /// The hidden server body of every species (M5.5d): the species prefab with its HostileNet, CombatEntity, stats and colliders,
        /// without Emerald, HostileBrainLink or the NavMeshAgent (the entity brain moves it) and with its renderers off, plus an
        /// <see cref="EcsHostileBrain"/>; saved as <c>ServerBody/HostileBody_&lt;Species&gt;.prefab</c> and listed in the catalog.
        /// </summary>
        static void BuildBodies()
        {
            if (!AssetDatabase.IsValidFolder(BodyFolder)) AssetDatabase.CreateFolder(SpeciesFolder, "ServerBody");
            var entries = new List<HostileBodyCatalog.Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab Hostile_", new[] { SpeciesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path)?.Replace('\\', '/') != SpeciesFolder) continue;   // not ServerBody's own
                var species = AssetDatabase.LoadAssetAtPath<HostileNet>(path);
                if (species == null) continue;
                string bodyPath = BodyFolder + "/" + Path.GetFileName(path).Replace("Hostile_", "HostileBody_");
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Strip(root);
                    if (root.GetComponent<EcsHostileBrain>() == null) root.AddComponent<EcsHostileBrain>();
                    PrefabUtility.SaveAsPrefabAsset(root, bodyPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                entries.Add(new HostileBodyCatalog.Entry { species = species, body = AssetDatabase.LoadAssetAtPath<HostileNet>(bodyPath) });
            }
            var catalog = AssetDatabase.LoadAssetAtPath<HostileBodyCatalog>(HostileBodyCatalog.AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<HostileBodyCatalog>();
                AssetDatabase.CreateAsset(catalog, HostileBodyCatalog.AssetPath);
            }
            catalog.entries = entries.ToArray();
            EditorUtility.SetDirty(catalog);
            EnsureWorldAssets(catalog);
            Debug.Log($"[KnBuild] Hostile server bodies: {entries.Count} in {BodyFolder}, catalog {HostileBodyCatalog.AssetPath}");
        }

        public const string NgoDefaultPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

        /// <summary>
        /// The server bodies carry a NetworkObject (HostileNet needs one) but are never spawned by NGO, and NGO adds every such prefab to its
        /// default list when it is saved (7 Nov 2026: all ten were). There they would count as species of their own
        /// (<see cref="HostileSpawner.Species"/> reads the NetworkManager's list): a view could be made of an invisible body. Taken out again.
        /// </summary>
        static void KeepBodiesOutOfNgoPrefabs()
        {
            Object list = AssetDatabase.LoadMainAssetAtPath(NgoDefaultPrefabsPath);
            if (list == null) return;
            var so = new SerializedObject(list);
            SerializedProperty entries = so.FindProperty("List");
            if (entries == null || !entries.isArray) { Debug.LogError("[KnBuild] " + NgoDefaultPrefabsPath + " has no List: the server bodies were not checked"); return; }
            int removed = 0;
            for (int i = entries.arraySize - 1; i >= 0; i--)
            {
                Object prefab = entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab")?.objectReferenceValue;
                string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
                if (path == null || !path.StartsWith(BodyFolder + "/", StringComparison.Ordinal)) continue;
                entries.DeleteArrayElementAtIndex(i);
                removed++;
            }
            if (removed == 0) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(list);
            Debug.Log($"[KnBuild] {removed} server bodies taken out of NGO's {NgoDefaultPrefabsPath} (never NGO-spawned)");
        }

        public const string ResourcesFolder ="Assets/_Game/Gameplay/Hostiles/Entities/Resources";
        public const string WorldAssetsPath = ResourcesFolder + "/" + HostileWorldAssets.ResourcePath + ".asset";
        const string HostileBrainSettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileBrainSettings.asset";

        /// <summary>The settings and the catalog a built server loads (Resources).</summary>
        static void EnsureWorldAssets(HostileBodyCatalog catalog)
        {
            if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets/_Game/Gameplay/Hostiles/Entities", "Resources");
            var assets = AssetDatabase.LoadAssetAtPath<HostileWorldAssets>(WorldAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<HostileWorldAssets>();
                AssetDatabase.CreateAsset(assets, WorldAssetsPath);
            }
            assets.brain = AssetDatabase.LoadAssetAtPath<KryticalNova.Brains.BrainSettings>(KryticalNova.Brains.BrainPaths.SettingsAsset);
            assets.hostiles = AssetDatabase.LoadAssetAtPath<HostileBrainSettings>(HostileBrainSettingsPath);
            assets.bodies = catalog;
            EditorUtility.SetDirty(assets);
            if (assets.brain == null || assets.hostiles == null) Debug.LogError("[KnBuild] " + WorldAssetsPath + ": a settings asset is missing");
        }

        static bool IsBrainPart(Component c)
        {
            if (c == null) return false;
            Type t = c.GetType();
            return (t.Namespace ?? "").StartsWith("EmeraldAI", StringComparison.Ordinal) || t.Name == "HostileBrainLink" || c is NavMeshAgent;
        }

        /// <summary>Removes Emerald, the link and the agent, one that nothing left still requires first; renderers off.</summary>
        static void Strip(GameObject root)
        {
            for (int pass = 0; pass < 32; pass++)
            {
                List<Component> parts = root.GetComponentsInChildren<Component>(true).Where(IsBrainPart).ToList();
                if (parts.Count == 0) break;
                bool removed = false;
                foreach (Component part in parts)
                {
                    Type type = part.GetType();
                    bool required = part.gameObject.GetComponents<Component>().Any(other => other != null && other != part
                        && other.GetType().GetCustomAttributes(typeof(RequireComponent), true).Cast<RequireComponent>()
                            .Any(r => (r.m_Type0 != null && r.m_Type0.IsAssignableFrom(type)) || (r.m_Type1 != null && r.m_Type1.IsAssignableFrom(type))
                                      || (r.m_Type2 != null && r.m_Type2.IsAssignableFrom(type))));
                    if (required) continue;
                    Object.DestroyImmediate(part, true);
                    removed = true;
                }
                if (!removed) { Debug.LogError("[KnBuild] " + root.name + ": brain parts left that others require: " + string.Join(", ", parts.Select(p => p.GetType().Name))); break; }
            }
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }

        // the ghost type is the prefab's asset GUID, in a field Netcode only fills in memory: written and saved every time (as the player's)
        static void StampPrefabId(GameObject prefab)
        {
            var so = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
            SerializedProperty id = so.FindProperty("prefabId");
            if (id == null) return;
            id.stringValue = AssetDatabase.AssetPathToGUID(PrefabPath);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        static void EnsureHolder(GameObject prefab)
        {
            bool single = Application.isBatchMode;
            Scene scene = EditorSceneManager.OpenScene(PlayerGhostAssetsBuilder.SubScenePath, single ? OpenSceneMode.Single : OpenSceneMode.Additive);
            try
            {
                HostileGhostPrefabAuthoring holder = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out holder)) break;
                if (holder == null)
                {
                    var go = new GameObject(HolderName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    holder = go.AddComponent<HostileGhostPrefabAuthoring>();
                }
                holder.prefab = prefab;
                EditorUtility.SetDirty(holder);
                if (!EditorSceneManager.SaveScene(scene, PlayerGhostAssetsBuilder.SubScenePath))
                    throw new IOException("the player ghost subscene could not be saved at " + PlayerGhostAssetsBuilder.SubScenePath);
            }
            finally
            {
                if (!single) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
