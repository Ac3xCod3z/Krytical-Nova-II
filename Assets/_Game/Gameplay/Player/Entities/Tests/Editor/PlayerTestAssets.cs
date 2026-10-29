using System.IO;
using Unity.Entities;
using Unity.NetCode;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = UnityEngine.Hash128;
using Object = UnityEngine.Object;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Makes the player ghost prefab and the SubScene that carries it, once per Editor session (the way spike M1 proved: a ghost
    /// prefab only reaches a world through a SubScene; the helper that bakes without one is internal to the package).
    /// The folder is rebuilt at the first test and deleted by <see cref="Cleanup"/>; it is git-ignored.
    /// </summary>
    public static class PlayerTestAssets
    {
        public const string Dir = "Assets/_Game/Gameplay/Player/Entities/Tests/Editor/Generated";
        public const int TraceTicks = 512;
        static bool s_Done;
        static Hash128 s_SubGuid;

        public static Hash128 SubSceneGuid { get { Ensure(); return s_SubGuid; } }

        public static void Ensure()
        {
            if (s_Done) return;
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets/_Game/Gameplay/Player/Entities/Tests/Editor", "Generated");

            // additive scene creation needs a SAVED active scene
            var baseScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(baseScene, Dir + "/Base.unity");

            var prefab = MakePrefab();
            s_SubGuid = MakeSubScene("PlayerPrefabSub", scene =>
            {
                var go = new GameObject("PlayerPrefabHolder");
                go.AddComponent<PlayerGhostPrefabAuthoring>().prefab = prefab;
                SceneManager.MoveGameObjectToScene(go, scene);
            });
            s_Done = true;
        }

        public static void Cleanup()
        {
            if (!s_Done) return;
            s_Done = false;
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        static GameObject MakePrefab()
        {
            var go = new GameObject("PlayerGhostTest");
            var ghost = go.AddComponent<GhostAuthoringComponent>();
            ghost.HasOwner = true;
            ghost.DefaultGhostMode = GhostMode.OwnerPredicted;
            ghost.SupportedGhostModes = GhostModeMask.All;
            var player = go.AddComponent<PlayerGhostAuthoring>();
            player.traceTicks = TraceTicks;
            var path = Dir + "/PlayerGhostTest.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            AssetDatabase.ImportAsset(path);
            // the internal prefabId (asset GUID) is stamped by the save; make sure it is there
            var so = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
            var prop = so.FindProperty("prefabId");
            if (prop != null && string.IsNullOrEmpty(prop.stringValue))
            {
                prop.stringValue = AssetDatabase.AssetPathToGUID(path);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssetIfDirty(prefab);
            }
            return prefab;
        }

        static Hash128 MakeSubScene(string name, System.Action<Scene> fill)
        {
            var subPath = $"{Dir}/{name}.unity";
            var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            fill(sub);
            EditorSceneManager.SaveScene(sub, subPath);
            EditorSceneManager.CloseScene(sub, true);

            var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject(name + "_SubScene");
            go.SetActive(false);
            var ss = go.AddComponent<SubScene>();
            ss.AutoLoadScene = false;
            ss.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(subPath);
            go.SetActive(true);
            EditorSceneManager.SaveScene(parent, $"{Dir}/{name}Parent.unity");
            var guid = ss.SceneGUID;
            AssetDatabase.Refresh();
            return guid;
        }
    }
}
