using KryticalNova.Networking.Entities.Tests;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Scenes;
using Unity.Transforms;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = UnityEngine.Hash128;
using Object = UnityEngine.Object;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// M5.5 B71-B72, the network half (server + client in one process, the real Hostile ghost prefab of the DOTS asset builder): what the
    /// server writes on a Hostile ghost reaches a client as written, an attack's start tick and index (the view starts its clip from
    /// it), the heavy mark, the death and its tick, and a ghost the server removes leaves the client. The view half (the clip, the
    /// death clip, the pool) is the owner's hand test (plan M5.5), as it needs the species bodies and a camera.
    /// </summary>
    public sealed class HostileGhostNetTests
    {
        const string GhostPrefabPath = "Assets/_Game/Gameplay/Hostiles/Entities/Prefabs/HostileGhost.prefab";
        const string Dir = "Assets/_Game/Gameplay/Hostiles/Entities/Tests/Editor/Generated";
        static bool s_Made;
        static Hash128 s_Sub;

        /// <summary>A SubScene with only the Hostile ghost holder in it (as PlayerTestAssets makes the player's), once per session.</summary>
        static Hash128 SubScene()
        {
            if (s_Made) return s_Sub;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            Assert.IsNotNull(prefab, GhostPrefabPath + " is missing: run Krytical Nova > Hostiles > Build DOTS Assets");
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
            AssetDatabase.CreateFolder("Assets/_Game/Gameplay/Hostiles/Entities/Tests/Editor", "Generated");
            var baseScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(baseScene, Dir + "/Base.unity");

            string subPath = Dir + "/HostileGhostSub.unity";
            var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var holder = new GameObject("HostileGhostPrefabHolder");
            holder.AddComponent<HostileGhostPrefabAuthoring>().prefab = prefab;
            SceneManager.MoveGameObjectToScene(holder, sub);
            EditorSceneManager.SaveScene(sub, subPath);
            EditorSceneManager.CloseScene(sub, true);

            var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("HostileGhostSub_SubScene");
            go.SetActive(false);
            var ss = go.AddComponent<SubScene>();
            ss.AutoLoadScene = false;
            ss.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(subPath);
            go.SetActive(true);
            EditorSceneManager.SaveScene(parent, Dir + "/HostileGhostSubParent.unity");
            s_Sub = ss.SceneGUID;
            AssetDatabase.Refresh();
            s_Made = true;
            return s_Sub;
        }

        [OneTimeTearDown]
        public void Cleanup()
        {
            if (!s_Made) return;
            s_Made = false;
            if (AssetDatabase.IsValidFolder(Dir)) AssetDatabase.DeleteAsset(Dir);
        }

        static KnTestWorld Rig()
        {
            var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            tw.LoadSubScene(SubScene());
            tw.Connect();
            return tw;
        }

        static Entity Spawn(KnTestWorld tw, HostileGhost fields, float3 at)
        {
            Entity prefab = KnTestWorld.Singleton<HostileGhostPrefab>(tw.Server).Prefab;
            EntityManager em = tw.Server.EntityManager;
            Entity e = em.Instantiate(prefab);
            em.SetComponentData(e, LocalTransform.FromPosition(at));
            em.SetComponentData(e, fields);
            return e;
        }

        static bool TryClientGhost(KnTestWorld tw, out HostileGhost ghost, out Entity entity)
        {
            ghost = default;
            entity = Entity.Null;
            EntityManager em = tw.Clients[0].EntityManager;
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<HostileGhost>(), ComponentType.ReadOnly<GhostInstance>());
            if (q.CalculateEntityCount() != 1) return false;
            using NativeArray<Entity> all = q.ToEntityArray(Allocator.Temp);
            entity = all[0];
            ghost = em.GetComponentData<HostileGhost>(entity);
            return true;
        }

        [Test]
        public void B71_AnAttacksStart_ReachesTheClient_AsTheServerWroteIt_TickIndexAndHeavy()
        {
            using KnTestWorld tw = Rig();
            uint now = KnTestWorld.Singleton<NetworkTime>(tw.Server).ServerTick.TickIndexForValidTick;
            var fields = new HostileGhost { Species = 7, Level = 4, Tier = 1, Mode = 2, AttackStartTick = now, AttackIndex = 2, AttackTargetGhostId = 3,
                                            Flags = HostileGhostFlags.InCombat | HostileGhostFlags.Heavy, SpeedSteps = 40 };
            Entity server = Spawn(tw, fields, new float3(3f, 0f, 4f));
            Assert.GreaterOrEqual(tw.TickUntil(() => TryClientGhost(tw, out _, out _), 300), 0, "the Hostile ghost reached the client");
            tw.Tick(10);
            Assert.IsTrue(TryClientGhost(tw, out HostileGhost seen, out Entity client));
            Assert.AreEqual(now, seen.AttackStartTick, "the server's start tick: the view starts its clip from it");
            Assert.AreEqual(2, seen.AttackIndex);
            Assert.AreEqual(3, seen.AttackTargetGhostId);
            Assert.AreNotEqual(0, seen.Flags & HostileGhostFlags.Heavy, "heavy: the view shows its \"!\"");
            Assert.AreEqual(7, seen.Species);
            Assert.AreEqual(4, seen.Level);
            Assert.AreEqual(40, seen.SpeedSteps);
            float3 place = tw.Clients[0].EntityManager.GetComponentData<LocalTransform>(client).Position;
            Assert.AreEqual(3f, place.x, 0.05f, "the place, interpolated");
            Assert.AreEqual(4f, place.z, 0.05f);

            // the next attack: a new start tick, the light one
            fields.AttackStartTick = now + 45;
            fields.AttackIndex = 0;
            fields.Flags = HostileGhostFlags.InCombat;
            tw.Server.EntityManager.SetComponentData(server, fields);
            Assert.GreaterOrEqual(tw.TickUntil(() => TryClientGhost(tw, out HostileGhost g, out _) && g.AttackStartTick == now + 45, 120), 0,
                "the new start reached the client");
            Assert.IsTrue(TryClientGhost(tw, out seen, out _));
            Assert.AreEqual(0, seen.AttackIndex);
            Assert.AreEqual(0, seen.Flags & HostileGhostFlags.Heavy, "light: no mark");
        }

        [Test]
        public void B72_ADeath_ReachesTheClient_WithItsTick_AndAGhostTheServerRemoves_LeavesIt()
        {
            using KnTestWorld tw = Rig();
            Entity server = Spawn(tw, new HostileGhost { Species = 7, Level = 4, Mode = 2, Flags = HostileGhostFlags.InCombat }, float3.zero);
            Assert.GreaterOrEqual(tw.TickUntil(() => TryClientGhost(tw, out _, out _), 300), 0, "the Hostile ghost reached the client");

            uint now = KnTestWorld.Singleton<NetworkTime>(tw.Server).ServerTick.TickIndexForValidTick;
            tw.Server.EntityManager.SetComponentData(server, new HostileGhost
            {
                Species = 7, Level = 4, Mode = (byte)HostileMode.Dead, Flags = HostileGhostFlags.Dead, DeathIndex = 1, DeadTick = now,
            });
            Assert.GreaterOrEqual(tw.TickUntil(() => TryClientGhost(tw, out HostileGhost g, out _) && (g.Flags & HostileGhostFlags.Dead) != 0, 120), 0,
                "the death reached the client");
            Assert.IsTrue(TryClientGhost(tw, out HostileGhost seen, out _));
            Assert.AreEqual(now, seen.DeadTick, "its tick: the view knows how far into the death it is");
            Assert.AreEqual(1, seen.DeathIndex, "the death clip");
            Assert.AreEqual(0, seen.SpeedSteps);
            Assert.AreEqual(1, HostileViewClientSystem.StateOf(seen).DeathIndex, "the view's state: NGO's HostileAnimState, dead");
            Assert.AreNotEqual(0, HostileViewClientSystem.StateOf(seen).Flags & HostileGhostFlags.Dead);

            // the body goes back to the pool on the server: its ghost goes, and the client's with it (the view is released then)
            tw.Server.EntityManager.DestroyEntity(server);
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<HostileGhost>(tw.Clients[0]) == 0, 120), 0, "the ghost left the client");
        }
    }
}
