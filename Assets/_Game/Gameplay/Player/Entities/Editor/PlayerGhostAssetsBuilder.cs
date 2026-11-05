using System.IO;
using Unity.Entities.Serialization;
using Unity.NetCode;
using Unity.Scenes;
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
        /// <summary>The Strike Dummy's ghost (M4.2b): static, interpolated, no owner.</summary>
        public const string StrikeDummyPrefabPath = PrefabFolder + "/StrikeDummyGhost.prefab";
        public const string SceneFolder = "Assets/_Game/Gameplay/Player/Entities/Scenes";
        public const string SubScenePath = SceneFolder + "/PlayerGhostPrefabs.unity";
        /// <summary>
        /// A scene that only points at <see cref="SubScenePath"/> with a SubScene component (auto load off). A player build bakes only
        /// the subscenes that a scene in its list points to, and the ghost subscene is otherwise referenced only from
        /// <see cref="PlayerWorldAssets"/>: without this scene in the list the built game has no copy and nobody can enter (B3 run 1,
        /// 30 Oct 2026). It is listed, never loaded.
        /// </summary>
        public const string BakeScenePath = SceneFolder + "/PlayerGhostBake.unity";
        public const string BakeHolderName = "PlayerGhostSubScene";
        public const string HolderName = "PlayerGhostPrefabHolder";
        public const string ResourcesFolder = "Assets/_Game/Gameplay/Player/Entities/Resources";
        public const string WorldAssetsPath = ResourcesFolder + "/" + PlayerWorldAssets.ResourcePath + ".asset";
        /// <summary>The networked gameplay player (the body built around each ghost).</summary>
        public const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        /// <summary>The ghost's name: netcode hashes it into the ghost type, so it never changes.</summary>
        public const string GhostName = "PlayerGhost";
        public const string StrikeDummyGhostName = "StrikeDummyGhost";

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
            GameObject dummy = EnsureStrikeDummyPrefab();
            EnsureSubScene(prefab, dummy);
            EnsureBakeScene();
            EnsureWorldAssets();
            AssetDatabase.SaveAssets();
            Debug.Log("[KnBuild] player ghost assets: " + PrefabPath + ", " + StrikeDummyPrefabPath + ", " + SubScenePath + " (scene GUID " +
                      AssetDatabase.AssetPathToGUID(SubScenePath) + "), " + BakeScenePath + " and " + WorldAssetsPath);
        }

        static void EnsureBakeScene()
        {
            bool single = Application.isBatchMode;
            Scene scene = File.Exists(BakeScenePath)
                ? EditorSceneManager.OpenScene(BakeScenePath, single ? OpenSceneMode.Single : OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, single ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                SubScene sub = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out sub)) break;
                if (sub == null)
                {
                    var go = new GameObject(BakeHolderName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    sub = go.AddComponent<SubScene>();
                }
                sub.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubScenePath);
                sub.AutoLoadScene = false; // the server and client worlds load it themselves (PlayerWorldAssets.LoadGhostScene)
                EditorUtility.SetDirty(sub);
                if (!EditorSceneManager.SaveScene(scene, BakeScenePath))
                    throw new IOException("the player ghost bake scene could not be saved at " + BakeScenePath);
            }
            finally
            {
                if (!single) EditorSceneManager.CloseScene(scene, true);
            }
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
                var player = root.GetComponent<PlayerGhostAuthoring>();
                if (player == null) player = root.AddComponent<PlayerGhostAuthoring>(); // speeds keep their values
                CopyMeleeClips(player);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            AssetDatabase.ImportAsset(PrefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            StampPrefabId(prefab, PrefabPath);
            return prefab;
        }

        /// <summary>The Strike Dummy ghost: the server spawns one per scene dummy and writes its place and HP (M4.2b).</summary>
        static GameObject EnsureStrikeDummyPrefab()
        {
            bool exists = File.Exists(StrikeDummyPrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(StrikeDummyPrefabPath) : new GameObject(StrikeDummyGhostName);
            try
            {
                root.name = StrikeDummyGhostName;
                var ghost = root.GetComponent<GhostAuthoringComponent>();
                if (ghost == null) ghost = root.AddComponent<GhostAuthoringComponent>();
                ghost.HasOwner = false;
                ghost.DefaultGhostMode = GhostMode.Interpolated;
                ghost.SupportedGhostModes = GhostModeMask.Interpolated;
                ghost.OptimizationMode = GhostOptimizationMode.Static;
                if (root.GetComponent<StrikeDummyGhostAuthoring>() == null) root.AddComponent<StrikeDummyGhostAuthoring>();
                PrefabUtility.SaveAsPrefabAsset(root, StrikeDummyPrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            AssetDatabase.ImportAsset(StrikeDummyPrefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StrikeDummyPrefabPath);
            StampPrefabId(prefab, StrikeDummyPrefabPath);
            return prefab;
        }

        /// <summary>
        /// M4.4b: the ghost bakes its strike lengths from the very clips the NGO Player prefab's PlayerMeleeCombat swings, copied here
        /// by field name (this assembly does not see the Combat one).
        /// </summary>
        static void CopyMeleeClips(PlayerGhostAuthoring player)
        {
            var melee = FindMeleeCombat(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath));
            if (melee == null) { Debug.LogError("[KnBuild] no PlayerMeleeCombat on " + PlayerPrefabPath); return; }
            var from = new SerializedObject(melee);
            player.combo1Clip = from.FindProperty("combo1Clip").objectReferenceValue as AnimationClip;
            player.combo2Clip = from.FindProperty("combo2Clip").objectReferenceValue as AnimationClip;
            player.combo3Clip = from.FindProperty("combo3Clip").objectReferenceValue as AnimationClip;
        }

        /// <summary>The NGO player's PlayerMeleeCombat, found by type name, or null.</summary>
        public static MonoBehaviour FindMeleeCombat(GameObject ngoPlayer)
        {
            if (ngoPlayer == null) return null;
            foreach (var c in ngoPlayer.GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && c.GetType().Name == "PlayerMeleeCombat") return c;
            return null;
        }

        // The ghost type is the prefab's asset GUID, kept in an internal field the save normally fills; make sure it is there.
        static void StampPrefabId(GameObject prefab, string path)
        {
            var so = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
            SerializedProperty id = so.FindProperty("prefabId");
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (id == null) return;
            // Netcode's OnValidate fills it in memory when the prefab loads, so "already right" here says nothing about the file:
            // write it and save every time (the baker throws on an empty id).
            id.stringValue = guid;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssetIfDirty(prefab);
        }

        static void EnsureSubScene(GameObject prefab, GameObject strikeDummy)
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
                holder.strikeDummyPrefab = strikeDummy;
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
