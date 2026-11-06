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
        public void TheShippedGhost_SwingsTheNgoPlayersClips_WithItsNumbers()
        {
            // M4.4b: a strike lasts clip length / strike speed (NGO PlayerMeleeCombat.GetStrikeDuration), so the clips and every
            // number MeleeTiming.Prefab carries must be the NGO Player prefab's own.
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGhostAssetsBuilder.PrefabPath).GetComponent<PlayerGhostAuthoring>();
            var melee = PlayerGhostAssetsBuilder.FindMeleeCombat(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGhostAssetsBuilder.PlayerPrefabPath));
            Assert.IsNotNull(melee, "no PlayerMeleeCombat on " + PlayerGhostAssetsBuilder.PlayerPrefabPath);
            var ngo = new SerializedObject(melee);
            AnimationClip[] clips = { player.combo1Clip, player.combo2Clip, player.combo3Clip };
            for (int i = 0; i < 3; i++)
            {
                Assert.IsNotNull(clips[i], "combo" + (i + 1) + "Clip: run Build Player Ghost Assets");
                Assert.AreSame(ngo.FindProperty("combo" + (i + 1) + "Clip").objectReferenceValue, clips[i], "the same clip asset as NGO");
                Assert.Greater(clips[i].length, 0.1f);
            }

            MeleeTiming t = MeleeTiming.Prefab(default);
            int Milli(string field) => Mathf.RoundToInt(ngo.FindProperty(field).floatValue * 1000f);
            Assert.AreEqual(new Unity.Mathematics.int3(Milli("strike1Speed"), Milli("strike2Speed"), Milli("strike3Speed")), t.SpeedMilli);
            Assert.AreEqual(new Unity.Mathematics.int3(Milli("swing1Start"), Milli("swing2Start"), Milli("swing3Start")), t.WindowStartPermille);
            Assert.AreEqual(new Unity.Mathematics.int3(Milli("swing1End"), Milli("swing2End"), Milli("swing3End")), t.WindowEndPermille);
            Assert.AreEqual(Milli("postComboDelay") * 1000, t.PostComboMicros);
            Assert.AreEqual(Milli("meleeStrikingRange"), t.ReachMm);
            Assert.AreEqual(Milli("serverMaxValidationRange"), t.ValidationMm);
            Assert.AreEqual(Milli("minCadenceTolerance"), t.MinCadencePermille);

            // the draw the first strike waits for (owner's hand test, 5 Nov 2026): the NGO motor's equip window
            MonoBehaviour motor = null;
            foreach (var c in AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGhostAssetsBuilder.PlayerPrefabPath).GetComponentsInChildren<MonoBehaviour>(true))
                if (c != null && c.GetType().Name == "PlayerAnimancerMotor") { motor = c; break; }
            Assert.IsNotNull(motor, "the NGO player's PlayerAnimancerMotor");
            Vector2 window = new SerializedObject(motor).FindProperty("equipWindow").vector2Value;
            Assert.AreEqual(Mathf.RoundToInt((window.y - window.x) * 1000000f), t.DrawMicros, "the draw lasts NGO's equip window");
        }

        [Test]
        public void TheStrikeDummyGhostPrefab_IsAStaticInterpolatedGhostWithNoOwner()
        {
            // M4.2b: the server spawns one per scene dummy and writes its place and HP; nobody owns or predicts it
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerGhostAssetsBuilder.StrikeDummyPrefabPath);
            Assert.IsNotNull(prefab, "no " + PlayerGhostAssetsBuilder.StrikeDummyPrefabPath + ": run Build Player Ghost Assets");
            Assert.AreEqual(PlayerGhostAssetsBuilder.StrikeDummyGhostName, prefab.name, "the name is hashed into the ghost type");
            var ghost = prefab.GetComponent<GhostAuthoringComponent>();
            Assert.IsNotNull(ghost);
            Assert.IsFalse(ghost.HasOwner);
            Assert.AreEqual(GhostMode.Interpolated, ghost.DefaultGhostMode);
            Assert.AreEqual(GhostOptimizationMode.Static, ghost.OptimizationMode);
            Assert.IsNotNull(prefab.GetComponent<StrikeDummyGhostAuthoring>());
        }

        [Test]
        public void TheShippedSubScene_HoldsTheShippedGhostPrefab()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayerGhostAssetsBuilder.SubScenePath);
            Assert.IsNotNull(scene, "no " + PlayerGhostAssetsBuilder.SubScenePath + ": run Build Player Ghost Assets");
            string text = System.IO.File.ReadAllText(PlayerGhostAssetsBuilder.SubScenePath);
            string prefabGuid = AssetDatabase.AssetPathToGUID(PlayerGhostAssetsBuilder.PrefabPath);
            StringAssert.Contains("guid: " + prefabGuid, text, "the holder points at the shipped ghost prefab");
            StringAssert.Contains("guid: " + AssetDatabase.AssetPathToGUID(PlayerGhostAssetsBuilder.StrikeDummyPrefabPath), text,
                "and at the Strike Dummy's (M4.2b)");
        }

        [Test]
        public void TheBakeScene_PointsAtTheGhostSubScene_WithoutAutoLoad()
        {
            // a player build bakes only subscenes a listed scene points at (B3 run 1: the built game had no copy, nobody entered)
            var bake = AssetDatabase.LoadAssetAtPath<SceneAsset>(PlayerGhostAssetsBuilder.BakeScenePath);
            Assert.IsNotNull(bake, "no " + PlayerGhostAssetsBuilder.BakeScenePath + ": run Build Player Ghost Assets");
            string text = System.IO.File.ReadAllText(PlayerGhostAssetsBuilder.BakeScenePath);
            string subSceneGuid = AssetDatabase.AssetPathToGUID(PlayerGhostAssetsBuilder.SubScenePath);
            StringAssert.Contains("guid: " + subSceneGuid, text, "the SubScene points at the ghost subscene");
            StringAssert.Contains("AutoLoadScene: 0", text, "the worlds load it themselves");
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
                Entity dummy = KnTestWorld.Singleton<StrikeDummyGhostPrefab>(w).Prefab;
                Assert.AreNotEqual(Entity.Null, dummy, w.Name + ": the Strike Dummy ghost (M4.2b)");
                Assert.IsTrue(w.EntityManager.HasComponent<CombatTarget>(dummy), w.Name + ": an attackable target");
                Assert.IsTrue(w.EntityManager.HasComponent<PositionHistory>(dummy), w.Name + ": with a hit-test ring");
            }
        }
    }
}
