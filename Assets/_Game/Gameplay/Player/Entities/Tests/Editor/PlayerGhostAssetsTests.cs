using NUnit.Framework;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.NetCode;
using UnityEditor;
using UnityEngine;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Editor;
using Hash128 = UnityEngine.Hash128;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// M3a-2b R8: the shipped player ghost prefab and its subscene (made by <see cref="PlayerGhostAssetsBuilder"/>) have the right shape,
    /// and loading that subscene gives the server and a client world the <see cref="PlayerGhostPrefab"/> the admission needs.
    /// </summary>
    public class PlayerGhostAssetsTests
    {
        [Test]
        public void TheShippedGhostPrefab_IsAnOwnerPredictedPlayerGhost()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGhostAssetsBuilder.PrefabPath);
            Assert.IsNotNull(prefab, "no " + PlayerGhostAssetsBuilder.PrefabPath + ": run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets");
            Assert.AreEqual(PlayerGhostAssetsBuilder.GhostName, prefab.name, "the name is hashed into the ghost type");
            var ghost = prefab.GetComponent<GhostAuthoringComponent>();
            Assert.IsNotNull(ghost);
            Assert.IsTrue(ghost.HasOwner);
            Assert.AreEqual(GhostMode.OwnerPredicted, ghost.DefaultGhostMode);
            var player = prefab.GetComponent<PlayerGhostAuthoring>();
            Assert.IsNotNull(player);
            Assert.AreEqual(0, player.TraceTicks, "the shipped ghost keeps no test history ring");
        }

        [Test]
        public void TheShippedSubScene_HoldsTheShippedGhostPrefab()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayerGhostAssetsBuilder.SubScenePath);
            Assert.IsNotNull(scene, "no " + PlayerGhostAssetsBuilder.SubScenePath + ": run Build Player Ghost Assets");
            string text = System.IO.File.ReadAllText(PlayerGhostAssetsBuilder.SubScenePath);
            string prefabGuid = AssetDatabase.AssetPathToGUID(PlayerGhostAssetsBuilder.PrefabPath);
            StringAssert.Contains("guid: " + prefabGuid, text, "the holder points at the shipped ghost prefab");
        }

        [Test]
        public void TheWorldAssets_NameTheSubSceneAndThePlayerPrefab_AndLoadFromResources()
        {
            var assets = Resources.Load<PlayerWorldAssets>(PlayerWorldAssets.ResourcePath);
            Assert.IsNotNull(assets, "no " + PlayerGhostAssetsBuilder.WorldAssetsPath + ": run Build Player Ghost Assets");
            Assert.AreEqual(PlayerGhostAssetsBuilder.WorldAssetsPath, AssetDatabase.GetAssetPath(assets));
            Assert.IsTrue(assets.ghostScene.IsReferenceValid, "the ghost subscene is referenced");
            Assert.AreEqual(new EntitySceneReference(AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayerGhostAssetsBuilder.SubScenePath)),
                assets.ghostScene, "it is the shipped subscene");
            Assert.AreEqual(PlayerGhostAssetsBuilder.PlayerPrefabPath, AssetDatabase.GetAssetPath(assets.playerPrefab));
        }

        [Test]
        public void LoadingTheShippedSubScene_GivesEveryWorldThePlayerGhostPrefab()
        {
            string guid = AssetDatabase.AssetPathToGUID(PlayerGhostAssetsBuilder.SubScenePath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), "no subscene: run Build Player Ghost Assets");
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            // Entities reads a GUID string in its own byte order (UnityEngine.Hash128.Parse would name another scene)
            tw.LoadSubScene(new Unity.Entities.Hash128(guid));
            foreach (World w in tw.AllWorlds)
            {
                Entity prefab = KnTestWorld.Singleton<PlayerGhostPrefab>(w).Prefab;
                Assert.AreNotEqual(Entity.Null, prefab, w.Name);
                Assert.IsTrue(w.EntityManager.HasComponent<PlayerGhostTag>(prefab), w.Name + ": the prefab is the player ghost");
                Assert.IsTrue(w.EntityManager.HasComponent<PlayerLook>(prefab), w.Name + ": with the look version of step 8");
            }
        }
    }
}
