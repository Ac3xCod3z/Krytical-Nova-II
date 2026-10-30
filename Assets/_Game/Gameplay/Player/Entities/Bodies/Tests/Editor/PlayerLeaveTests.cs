using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>
    /// M3a-2b step 6 (worlds): a player leaves. The ghost loses its owner before anything else, the body's rules are told, the body
    /// and the ghost go, the budgets are forgotten; a second close is a no-op; a reused NetworkId never inherits the old ghost.
    /// </summary>
    public class PlayerLeaveTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        [SetUp]
        public void SetUp()
        {
            KnStopRequests.Reset();
            KnStopSystem.QuitOverride = null;
            ServerShutdown.Handler = null;
        }

        [TearDown]
        public void TearDown()
        {
            KnStopRequests.Reset();
            KnStopSystem.QuitOverride = null;
            ServerShutdown.Handler = null;
        }

        // A destroyed server ghost keeps only netcode's cleanup component until the ghost send system lets it go, so Exists stays true
        // for a while: it is gone when it no longer carries its own components.
        static bool Gone(EntityManager em, Entity ghost) => !em.Exists(ghost) || !em.HasComponent<PlayerGhostTag>(ghost);

        static string OwnerOf(BodyRig rig, Entity ghost) =>
            !Gone(rig.Server.EntityManager, ghost) ? "owner=" + rig.Server.EntityManager.GetComponentData<GhostOwner>(ghost).NetworkId : "gone";

        static List<string> After(List<string> log, string marker)
        {
            int i = log.IndexOf(marker);
            return i < 0 ? new List<string>() : log.GetRange(i + 1, log.Count - i - 1);
        }

        [Test]
        public void Leaving_InvalidatesTheGhostFirst_ThenRules_ThenBody_ThenGhost_ThenBudgets()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            Entity ghost = rig.GhostOf(key);
            GameObject body = rig.BodyOf(key);
            var recorder = body.GetComponent<LeaveRecorder>();
            recorder.Log = rig.Log;
            recorder.Probe = () => OwnerOf(rig, ghost);
            rig.Log.Add("--close--");

            rig.Disconnect(rig.Tw.Clients[0]);
            CollectionAssert.AreEqual(new[] { "invalidated", "leaving(owner=0)", "left", "body destroyed", "ghost destroyed", "forget" },
                After(rig.Log, "--close--"));
            Assert.IsTrue(body == null);
            Assert.IsTrue(Gone(rig.Server.EntityManager, ghost));
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            Assert.AreEqual(0, rig.ServerGhosts);
            Assert.AreEqual(1, rig.Leave.Left);
        }

        [Test]
        public void ASecondCloseOfTheSameKey_DoesNothing()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            rig.Disconnect(rig.Tw.Clients[0]);
            int lines = rig.Log.Count;
            rig.Leave.OnConnectionClosed(key);
            Assert.AreEqual(lines + 1, rig.Log.Count, "only the budgets are forgotten again");
            Assert.AreEqual("forget", rig.Log[rig.Log.Count - 1]);
            Assert.AreEqual(1, rig.Leave.Left);
        }

        [Test]
        public void TheRulesStillRun_WhenTheGhostIsAlreadyGone()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            Entity ghost = rig.GhostOf(key);
            GameObject body = rig.BodyOf(key);
            body.GetComponent<LeaveRecorder>().Log = rig.Log;
            rig.Server.EntityManager.DestroyEntity(ghost);
            rig.Log.Add("--close--");
            rig.Disconnect(rig.Tw.Clients[0]);
            CollectionAssert.AreEqual(new[] { "leaving", "left", "body destroyed", "forget" }, After(rig.Log, "--close--"));
            Assert.IsTrue(body == null);
        }

        [Test]
        public void TheDeferredVariant_KeepsTheGhostOwnerless_UntilItGoes()
        {
            using var rig = BodyRig.Create(1);
            rig.Leave.DeferGhostDestroy = true;
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            Entity ghost = rig.GhostOf(key);
            GameObject body = rig.BodyOf(key);
            body.GetComponent<LeaveRecorder>().Log = rig.Log;
            body.GetComponent<LeaveRecorder>().Probe = () => OwnerOf(rig, ghost);
            rig.Log.Add("--close--");
            rig.Disconnect(rig.Tw.Clients[0]);
            CollectionAssert.AreEqual(new[] { "invalidated", "leaving(owner=0)", "left", "body destroyed", "forget" }, After(rig.Log, "--close--"));

            var em = rig.Server.EntityManager;
            int ticks = 0;
            while (!Gone(em, ghost) && ticks < 5)
            {
                Assert.AreEqual(0, em.GetComponentData<GhostOwner>(ghost).NetworkId, "ownerless in between");
                Assert.IsTrue(em.HasComponent<PlayerLeaving>(ghost));
                rig.Tick();
                ticks++;
            }
            Assert.IsTrue(Gone(em, ghost), "the cleanup took it within a few ticks");
            Assert.AreEqual(1, rig.Server.GetExistingSystemManaged<PlayerLeavingCleanupSystem>().Destroyed);
        }

        [Test]
        public void AReusedNetworkId_NeverInheritsTheOldGhost()
        {
            using var rig = BodyRig.Create(1);
            World a = rig.Tw.Clients[0];
            rig.Enter(a);
            int idA = KnTestWorld.NetworkIdOf(a);
            PlayerKey keyA = rig.KeyOf(a);
            Entity ghostA = rig.GhostOf(keyA);
            rig.Disconnect(a);
            rig.Tw.Clients.Remove(a);
            a.Dispose();

            World b = rig.Tw.AddClient(rig.Tw.JoinPayload);
            rig.Tw.LoadSubScene(PlayerTestAssets.SubSceneGuid, only: b); // the ghost prefab, as the server has it
            PlayerEntitiesSetup.Install(b, new StraightLinePlanner());
            int got = rig.Tw.TickUntil(() => KnTestWorld.HasNetworkId(b), 300);
            Assert.GreaterOrEqual(got, 0, "the second client never connected");
            int idB = KnTestWorld.NetworkIdOf(b);
            if (idB != idA) Assert.Inconclusive("netcode did not hand out the same NetworkId again (" + idA + " then " + idB + ")");

            rig.Enter(b);
            PlayerKey keyB = rig.KeyOf(b);
            Assert.AreNotEqual(keyA, keyB, "a new connection gets a new key even with the old NetworkId");
            Assert.IsTrue(Gone(rig.Server.EntityManager, ghostA), "A's ghost is gone");
            Assert.AreEqual(1, rig.ServerGhosts);

            int seen = rig.Tw.TickUntil(() => KnTestWorld.Count<PlayerGhostTag>(b) == 1, 200);
            Assert.GreaterOrEqual(seen, 0, "B sees its own ghost");
            rig.Tick(5);
            using var q = b.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwnerIsLocal>());
            var mine = q.ToEntityArray(Allocator.Temp);
            int local = 0;
            foreach (Entity e in mine) if (b.EntityManager.IsComponentEnabled<GhostOwnerIsLocal>(e)) local++;
            mine.Dispose();
            Assert.AreEqual(1, local, "exactly one ghost is B's own");
        }

        [Test]
        public void TheServerStop_SavesEveryone_BeforeAnyoneIsSentAway()
        {
            using var rig = BodyRig.Create(2);
            rig.Enter(0);
            rig.Enter(1);
            foreach (World c in rig.Tw.Clients)
            {
                GameObject body = rig.BodyOf(rig.KeyOf(c));
                body.GetComponent<LeaveRecorder>().Log = rig.Log;
            }
            ServerShutdown.Handler = ServerShutdown.FromAction(() => rig.Log.Add("save all(connected=" + KnTestWorld.Count<NetworkId>(rig.Server) + ")"));
            int quits = 0;
            KnStopSystem.QuitOverride = code => quits++;
            var stop = rig.Server.GetExistingSystemManaged<KnStopSystem>();
            stop.Machine.GraceTicks = 30;
            rig.Log.Add("--stop--");

            KnStopRequests.Request(KnStopSource.Code);
            Assert.GreaterOrEqual(rig.Tw.TickUntil(() => quits > 0, 300), 0, "the server never quit");
            List<string> after = After(rig.Log, "--stop--");
            Assert.AreEqual("save all(connected=2)", after[0], "everyone saved while still connected");
            Assert.AreEqual(2, after.FindAll(l => l == "leaving").Count, "each body heard its player leave");
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            Assert.AreEqual(0, rig.ServerGhosts);
        }
    }
}
