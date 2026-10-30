using System.IO;
using Unity.Entities.Serialization;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Player.Entities.Editor
{
    /// <summary>
    /// Makes the shipped player ghost prefab and the subscene that carries it (M3a-2b, design R8): a ghost prefab only reaches a
    /// netcode world through a subscene (spike M1), and without this one every admission is refused "not ready". Run again at any
    /// time: an existing prefab and subscene are updated in place, never recreated, so their GUIDs (the ghost type and the scene
    /// reference) stay the same. Menu Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets, or in batch
    /// <c>-executeMethod KryticalNova.Player.Entities.Editor.PlayerGhostAssetsBuilder.Build</c>.
    /// </summary>
    public static class PlayerGhostAssetsBuilder
    {
        public const string PrefabFolder = "Assets/_Game/Gameplay/Player/Entities/Prefabs";
        public const string PrefabPath = PrefabFolder + "/PlayerGhost.prefab";
        public const string SceneFolder = "Assets/_Game/Gameplay/Player/Entities/Scenes";
        public const string SubScenePath = SceneFolder + "/PlayerGhostPrefabs.unity";
        public const string HolderName = "PlayerGhostPrefabHolder";
        public const string ResourcesFolder = "Assets/_Game/Gameplay/Player/Entities/Resources";
        public const string WorldAssetsPath = ResourcesFolder + "/" + PlayerWorldAssets.ResourcePath + ".asset";
        /// <summary>The networked gameplay player (the body built around each ghost).</summary>
        public const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        /// <summary>The ghost's name: netcode hashes it into the ghost type, so it never changes.</summary>
        public const string GhostName = "PlayerGhost";

        [MenuItem("Krytical Nova/Online/Entities/Build Player Ghost Assets")]
        public static void BuildMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            // in the Editor the subscene is opened next to the open scene, which needs that scene saved (a batch run opens it alone)
            if (!Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                Debug.LogError("[KnBuild] Save the open scene first: the player ghost subscene is opened next to it.");
                return;
            }
            EnsureFolder(PrefabFolder);
            EnsureFolder(SceneFolder);
            EnsureFolder(ResourcesFolder);
            GameObject prefab = EnsurePrefab();
            EnsureSubScene(prefab);
            EnsureWorldAssets();
            AssetDatabase.SaveAssets();
            Debug.Log("[KnBuild] player ghost assets: " + PrefabPath + ", " + SubScenePath + " (scene GUID " +
                      AssetDatabase.AssetPathToGUID(SubScenePath) + ") and " + WorldAssetsPath);
        }

        static void EnsureWorldAssets()
        {
            var assets = AssetDatabase.LoadAssetAtPath<PlayerWorldAssets>(WorldAssetsPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<PlayerWorldAssets>();
                AssetDatabase.CreateAsset(assets, WorldAssetsPath);
            }
            assets.ghostScene = new EntitySceneReference(AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath));
            assets.playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (assets.playerPrefab == null) Debug.LogError("[KnBuild] no player prefab at " + PlayerPrefabPath);
            EditorUtility.SetDirty(assets);
        }

        static GameObject EnsurePrefab()
        {
            bool exists = File.Exists(PrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(GhostName);
            try
            {
                root.name = GhostName;
                // not ??: in the Editor a missing component comes back as a "fake null" object that ?? lets through
                var ghost = root.GetComponent<GhostAuthoringComponent>();
                if (ghost == null) ghost = root.AddComponent<GhostAuthoringComponent>();
                ghost.HasOwner = true;
                ghost.DefaultGhostMode = GhostMode.OwnerPredicted;
                ghost.SupportedGhostModes = GhostModeMask.All;
                ghost.OptimizationMode = GhostOptimizationMode.Dynamic;
                if (root.GetComponent<PlayerGhostAuthoring>() == null) root.AddComponent<PlayerGhostAuthoring>(); // speeds keep their values
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

        // The ghost type is the prefab's asset GUID, kept in an internal field the save normally fills; make sure it is there.
        static void StampPrefabId(GameObject prefab)
        {
            var so = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
            SerializedProperty id = so.FindProperty("prefabId");
            string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
            if (id == null) return;
            // Netcode's OnValidate fills it in memory when the prefab loads, so "already right" here says nothing about the file:
            // write it and save every time (the baker throws on an empty id).
            id.stringValue = guid;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        static void EnsureSubScene(GameObject prefab)
        {
            // A batch run has nothing open worth keeping and cannot add a scene beside an untitled one: the subscene is the only scene.
            bool single = Application.isBatchMode;
            Scene scene = File.Exists(SubScenePath)
                ? EditorSceneManager.OpenScene(SubScenePath, single ? OpenSceneMode.Single : OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                PlayerGhostPrefabAuthoring holder = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out holder)) break;
                if (holder == null)
                {
                    var go = new GameObject(HolderName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    holder = go.AddComponent<PlayerGhostPrefabAuthoring>();
                }
                holder.prefab = prefab;
                EditorUtility.SetDirty(holder);
                if (!EditorSceneManager.SaveScene(scene, SubScenePath))
                    throw new IOException("the player ghost subscene could not be saved at " + SubScenePath);
            }
            finally
            {
                if (!single) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
