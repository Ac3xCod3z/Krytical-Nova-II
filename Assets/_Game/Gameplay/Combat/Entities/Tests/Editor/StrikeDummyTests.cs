using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// M4.2b: every scene Strike Dummy gets one ghost on the server, each client links it to its copy, the ghost carries the place,
    /// the capsule and the HP, a lethal hit refills (NGO: never a death), and the ghost goes with its dummy.
    /// </summary>
    public sealed class StrikeDummyTests
    {
        const string GhostSubScenePath = "Assets/_Game/Gameplay/Player/Entities/Scenes/PlayerGhostPrefabs.unity";

        [Test]
        public void ASceneDummy_GetsOneGhost_TheClientLinksIt_AndItsHpReachesTheClient_RefilledNeverDead()
        {
            string guid = AssetDatabase.AssetPathToGUID(GhostSubScenePath);
            Assert.IsFalse(string.IsNullOrEmpty(guid), "no ghost subscene: run Build Player Ghost Assets");
            // the server ghosts every dummy in the open scene: one another test left behind (8 Nov 2026: NavWorldTests left Zone01 open)
            // fails here by name, never as a wrong ghost count
            foreach (CombatEntity left in Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Assert.IsFalse(StrikeDummies.IsSceneDummy(left), $"another test left the dummy '{left.name}' in the open scene ({left.gameObject.scene.path}): a test that opens a scene closes it in its teardown");
            var go = new GameObject("M4.2b dummy");
            go.transform.position = new Vector3(3f, 0f, 4f);
            go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            var capsule = go.AddComponent<CapsuleCollider>();   // the scene dummy's: r 0.5, h 2 on Y (an added one defaults to h 1)
            capsule.radius = 0.5f;
            capsule.height = 2f;
            var dummy = go.AddComponent<CombatEntity>();
            try
            {
                using var tw = new KnTestWorld();
                tw.CreateWorlds(1);
                tw.LoadSubScene(new Unity.Entities.Hash128(guid));
                tw.Connect();
                var server = tw.Server.GetExistingSystemManaged<StrikeDummyServerSystem>();
                var client = tw.Clients[0].GetExistingSystemManaged<StrikeDummyClientSystem>();
                Assert.IsNotNull(server, "the server world has the dummy system");
                Assert.IsNotNull(client, "the client world has the dummy system");
                tw.TickUntil(() => client.Linked >= 1, 300);
                tw.Tick(5);
                Assert.AreEqual(1, server.Spawned, "one ghost for the one dummy");
                Assert.AreEqual(1, client.Linked, "linked once");

                int id = CombatTargetRegistry.GhostIdOf(go.transform);
                Assert.AreNotEqual(0, id, "a click on it names its ghost");
                Assert.AreSame(go.transform, CombatTargetRegistry.BodyOf(tw.Server, id), "the server's copy");
                Assert.AreSame(go.transform, CombatTargetRegistry.BodyOf(tw.Clients[0], id), "the client's copy (one object here, as Play DOTS here)");
                Assert.IsTrue(CombatTargetRegistry.IsServerCopy(go), "so the client never writes its HP");

                CombatTarget seen = ClientTarget(tw);
                Assert.AreEqual(new int3(3000, 0, 4000), seen.FeetMm, "where it stands");
                Assert.AreEqual(400, seen.RadiusMm, "its capsule, scale included");
                Assert.AreEqual(2000, seen.HeightMm);
                Assert.AreEqual(CombatTargetKinds.StrikeDummy, seen.Kind);
                Assert.AreEqual(Mathf.RoundToInt(dummy.MaxHealth * 1000f), seen.MaxHealthMilli);
                Assert.IsTrue(seen.Alive);

                // a lethal hit: refilled at once, no death (NGO CombatEntity.TakeDamage on an infinite standalone entity)
                int died = 0;
                dummy.Died += () => died++;
                Assert.IsTrue(dummy.InfiniteHealth, "a Strike Dummy is always infinite");
                dummy.TakeDamage(new DamageResult { DamageDealt = 1e7f, RawPhysicalDamage = 1e7f, MitigatedPhysicalDamage = 1e7f }, null);
                Assert.AreEqual(dummy.MaxHealth, dummy.CurrentHealth, 1e-3, "full again");
                Assert.IsTrue(dummy.IsAlive);
                Assert.AreEqual(0, died, "never a death");

                // a hit that leaves it standing: the client's ghost carries the lower HP
                dummy.TakeDamage(new DamageResult { DamageDealt = 25f, RawPhysicalDamage = 25f, MitigatedPhysicalDamage = 25f }, null);
                int want = Mathf.RoundToInt(dummy.CurrentHealth * 1000f);
                Assert.Less(want, seen.MaxHealthMilli, "planned: HP below Max");
                tw.TickUntil(() => ClientTarget(tw).HealthMilli == want, 120);
                Assert.AreEqual(want, ClientTarget(tw).HealthMilli, "the server's HP on the client");

                // the dummy goes away (its scene unloads): so does its ghost
                Object.DestroyImmediate(go);
                tw.TickUntil(() => ClientCount(tw) == 0, 300);
                Assert.AreEqual(0, ClientCount(tw), "no ghost left");
            }
            finally
            {
                if (go != null) Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OnlyAStandaloneSceneEntity_IsADummy_AndItsCapsuleCountsItsScale()
        {
            var go = new GameObject("dummy");
            var body = new GameObject("player body");
            try
            {
                go.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = 0.5f;
                capsule.height = 2f;
                var dummy = go.AddComponent<CombatEntity>();
                Assert.IsTrue(StrikeDummies.IsSceneDummy(dummy), "standalone, no Hostile, no body tag");
                StrikeDummies.CapsuleOf(dummy, out int radius, out int height);
                Assert.AreEqual(400, radius);
                Assert.AreEqual(2000, height);

                body.AddComponent<BodyRoleTag>();
                var child = new GameObject("part");
                child.transform.SetParent(body.transform);
                Assert.IsFalse(StrikeDummies.IsSceneDummy(child.AddComponent<CombatEntity>()), "never anything on a player body");
                go.SetActive(false);
                Assert.IsFalse(StrikeDummies.IsSceneDummy(dummy), "not while switched off");
                Assert.IsFalse(StrikeDummies.IsSceneDummy(null));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(body);
            }
        }

        static CombatTarget ClientTarget(KnTestWorld tw)
        {
            using var query = tw.Clients[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatTarget>());
            using var all = query.ToComponentDataArray<CombatTarget>(Allocator.Temp);
            Assert.AreEqual(1, all.Length, "one dummy ghost on the client");
            return all[0];
        }

        static int ClientCount(KnTestWorld tw)
        {
            using var query = tw.Clients[0].EntityManager.CreateEntityQuery(ComponentType.ReadOnly<CombatTarget>());
            return query.CalculateEntityCount();
        }
    }
}
