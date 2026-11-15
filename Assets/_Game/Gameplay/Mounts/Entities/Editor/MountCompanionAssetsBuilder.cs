using System.IO;
using KryticalNova.Player.Entities.Editor;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.Mounts.Entities.Editor
{
    /// <summary>
    /// The Mount Companion's entity-netcode assets (day list item 19): <see cref="PrefabPath"/> (an interpolated ghost with no owner, its
    /// <see cref="MountCompanionGhostAuthoring"/>) and a <see cref="MountCompanionGhostPrefabAuthoring"/> holder in the player ghost subscene
    /// (<see cref="PlayerGhostAssetsBuilder.SubScenePath"/>), beside the player's and the Hostiles'. Safe to run any time: an existing prefab
    /// and holder are updated in place, so the ghost type (the prefab's GUID) never changes. Menu Krytical Nova ▸ Mounts ▸ Build Companion
    /// Ghost, or in batch <c>-executeMethod KryticalNova.Mounts.Entities.Editor.MountCompanionAssetsBuilder.Build</c>.
    /// </summary>
    public static class MountCompanionAssetsBuilder
    {
        public const string PrefabFolder = "Assets/_Game/Gameplay/Mounts/Entities/Prefabs";
        public const string PrefabPath = PrefabFolder + "/MountCompanionGhost.prefab";
        /// <summary>The ghost's name: netcode hashes it into the ghost type, so it never changes.</summary>
        public const string GhostName = "MountCompanionGhost";
        public const string HolderName = "MountCompanionGhostPrefabHolder";

        [MenuItem("Krytical Nova/Mounts/Build Companion Ghost")]
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
            if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/_Game/Gameplay/Mounts/Entities", "Prefabs");
            GameObject prefab = EnsurePrefab();
            EnsureHolder(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[KnBuild] Mount Companion ghost assets: " + PrefabPath + " in " + PlayerGhostAssetsBuilder.SubScenePath);
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
                ghost.OptimizationMode = GhostOptimizationMode.Dynamic;   // it walks
                ghost.Importance = PlayerGhostAssetsBuilder.Importance(ghost.Importance, s => s.importanceCompanion);
                if (root.GetComponent<MountCompanionGhostAuthoring>() == null) root.AddComponent<MountCompanionGhostAuthoring>();
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

        // the ghost type is the prefab's asset GUID, in a field Netcode only fills in memory: written and saved every time (as the Hostiles')
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
                MountCompanionGhostPrefabAuthoring holder = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out holder)) break;
                if (holder == null)
                {
                    var go = new GameObject(HolderName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    holder = go.AddComponent<MountCompanionGhostPrefabAuthoring>();
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
