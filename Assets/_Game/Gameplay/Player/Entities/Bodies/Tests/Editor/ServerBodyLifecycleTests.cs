using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    // ---- a probe "rule / RPC receive" system of the same group: it must always see bodies already synced ----
    internal static class SyncProbe
    {
        public static bool On;
        public static int Samples, Mismatches;
        public static void Reset() { On = false; Samples = 0; Mismatches = 0; }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup))]
    internal partial class SyncProbeRulesSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!SyncProbe.On) return;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !EntityManager.Exists(entry.Ghost)) continue;
                PlayerPose pose = EntityManager.GetComponentData<PlayerPose>(entry.Ghost);
                SyncProbe.Samples++;
                if ((entry.Body.transform.position - PoseMath.Metres(pose)).sqrMagnitude > 1e-8f) SyncProbe.Mismatches++;
            }
        }
    }

    /// <summary>
    /// M3a-2b step 5 (worlds): the server body lifecycle with a stand-in body. Body first, ghost last; a refused load leaves nothing;
    /// the body follows the ghost before any rule of the tick; the mover drives the ghost; spawn spots do not collide.
    /// </summary>
    public class ServerBodyLifecycleTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() => SyncProbe.Reset();

        static void Log(string line) => Debug.Log("[M3A2B-BODY] " + line);

        static uint ServerTick(BodyRig rig) => KnTestWorld.Singleton<NetworkTime>(rig.Server).ServerTick.TickIndexForValidTick;

        [Test]
        public void ARefusedLoad_LeavesNoGhostNoBody_AndOneRefusal()
        {
            using var rig = BodyRig.Create(1);
            rig.Rules.Answer = "That character is not on this server.";
            rig.Enter(0);
            Assert.AreEqual(1, rig.AdmissionSystem.Refused);
            Assert.AreEqual(0, rig.Admission.Admitted);
            Assert.AreEqual(1, rig.Rules.Bodies.Count, "a body was built and asked");
            Assert.IsTrue(rig.Rules.Bodies[0] == null, "the refused body is destroyed");
            Assert.AreEqual(0, rig.ServerGhosts, "no ghost for a refused player");
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            Assert.AreEqual(0, rig.InGameOnServer, "no snapshots for a refused player");
            CollectionAssert.AreEqual(new[] { "body", "begin", "refused" }, rig.Log);
        }

        [Test]
        public void Admitted_InOrder_BodyThenLoadThenGhost_ThenInGame()
        {
            using var rig = BodyRig.Create(1);
            rig.Rules.AdmittedProbe = () => "ingame=" + rig.InGameOnServer + " ghosts=" + rig.ServerGhosts;
            rig.Enter(0);
            CollectionAssert.AreEqual(new[] { "body", "begin", "loaded", "ghost", "admitted(ingame=0 ghosts=1)" }, rig.Log,
                "the ghost is the last thing made and snapshots start only after the admission returned");
            Assert.AreEqual(1, rig.InGameOnServer);

            PlayerKey key = rig.KeyOf(0);
            Assert.IsTrue(key.IsValid);
            GameObject body = rig.BodyOf(key);
            Assert.IsNotNull(body, "the registry holds the server body by key");
            BodyRoleTag tag = body.GetComponent<BodyRoleTag>();
            Assert.AreEqual(NetRoleKind.ServerBody, tag.Kind);
            Assert.AreEqual(key, tag.Key);
            Assert.IsInstanceOf<ServerBodyMover>(tag.Mover);
            Entity ghost = rig.GhostOf(key);
            Assert.AreNotEqual(Entity.Null, ghost);
            Assert.AreEqual(KnTestWorld.NetworkIdOf(rig.Tw.Clients[0]), rig.Server.EntityManager.GetComponentData<GhostOwner>(ghost).NetworkId);
            Assert.AreEqual(key, rig.Rules.Contexts[0].Key);
            Assert.AreEqual("Vesper", rig.Rules.Contexts[0].Name);
            Assert.AreEqual(string.Empty, rig.Rules.Contexts[0].Account, "no proven account in this rig: never one from the request");

            int seen = rig.Tw.TickUntil(() => KnTestWorld.Count<PlayerGhostTag>(rig.Tw.Clients[0]) == 1, 120);
            Assert.GreaterOrEqual(seen, 0, "the ghost reaches the client once it is in game");
        }

        [Test]
        public void TheBody_FollowsTheGhostEveryTick_BeforeTheRulesOfThatTick_AndPhysicsSyncsOncePerTick()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            GameObject body = rig.BodyOf(key);
            var sync = rig.Server.GetExistingSystemManaged<ServerBodySyncSystem>();
            int calls = 0;
            sync.SyncPhysics = () => calls++;
            int before = sync.PhysicsSyncs;
            uint tick0 = ServerTick(rig);

            SyncProbe.On = true;
            body.GetComponent<BodyRoleTag>().Mover.MoveTo(new Vector3(6f, 0f, 0f), false);
            rig.Tick(45);
            int ticks = (int)(ServerTick(rig) - tick0);

            Assert.Greater(SyncProbe.Samples, 40);
            Assert.AreEqual(0, SyncProbe.Mismatches, "a rule of the tick saw a body that was not at its ghost");
            Assert.AreEqual(sync.PhysicsSyncs - before, calls, "every counted sync called the physics sync");
            Assert.AreEqual(ticks, calls, "exactly once per server tick while a body exists");
            Assert.Greater(body.transform.position.x, 1f, "the order walked the ghost, and the body came along");
            var mover = (ServerBodyMover)body.GetComponent<BodyRoleTag>().Mover;
            Assert.AreEqual(1, mover.Orders);
        }

        [Test]
        public void Teleport_MovesTheBodyAtOnce_AndTheGhostNextTick_WithANewTeleportNumber()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            GameObject body = rig.BodyOf(key);
            Entity ghost = rig.GhostOf(key);
            byte seq = rig.Server.EntityManager.GetComponentData<TeleportSeq>(ghost).Value;

            var to = new Vector3(3f, 0f, -4f);
            body.GetComponent<BodyRoleTag>().Mover.Teleport(to, Quaternion.Euler(0f, 90f, 0f));
            Assert.That(Vector3.Distance(to, body.transform.position), Is.LessThan(1e-4f), "the body at once");
            rig.Tick(2);
            PlayerPose pose = rig.Server.EntityManager.GetComponentData<PlayerPose>(ghost);
            Assert.AreEqual(new int3(3000, 0, -4000), pose.Mm);
            Assert.AreEqual(9000, pose.YawCdeg);
            Assert.AreNotEqual(seq, rig.Server.EntityManager.GetComponentData<TeleportSeq>(ghost).Value);
            Assert.That(Vector3.Distance(to, body.transform.position), Is.LessThan(1e-4f), "the sync keeps it there");
        }

        [Test]
        public void Stop_DropsTheWalk()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            PlayerKey key = rig.KeyOf(0);
            GameObject body = rig.BodyOf(key);
            IBodyMover mover = body.GetComponent<BodyRoleTag>().Mover;
            mover.MoveTo(new Vector3(20f, 0f, 0f), false);
            rig.Tick(15);
            mover.Stop();
            rig.Tick(2);
            float x = body.transform.position.x;
            rig.Tick(15);
            Assert.AreEqual(x, body.transform.position.x, 1e-3f, "no walk after the stop");
            Assert.Greater(x, 0.1f);
        }

        [Test]
        public void TwoPlayers_GetDifferentSpawnSpots_TheSecondPickSeesTheFirstBody()
        {
            using var rig = BodyRig.Create(2);
            rig.SpawnPoints.Add(new Vector3(10f, 0f, 10f));
            rig.SpawnPoints.Add(new Vector3(-10f, 0f, 10f));
            rig.Enter(0);
            rig.Enter(1);
            Vector3 a = rig.BodyOf(rig.KeyOf(0)).transform.position;
            Vector3 b = rig.BodyOf(rig.KeyOf(1)).transform.position;
            Assert.AreEqual(10f, a.x, 1e-3f);
            Assert.AreEqual(-10f, b.x, 1e-3f);
            Assert.AreEqual(0, rig.OccupiedSeen[0].Count);
            Assert.AreEqual(1, rig.OccupiedSeen[1].Count, "the second pick saw the first body");
            Entity ghostB = rig.GhostOf(rig.KeyOf(1));
            Assert.AreEqual(-10000, rig.Server.EntityManager.GetComponentData<PlayerPose>(ghostB).XMm, "the ghost starts on the body's spot");
        }

        [Test]
        public void ARepeatOfTheSameKey_IsRefused_WithoutASecondBody()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            var request = new KnAdmissionRequest { Key = rig.KeyOf(0), NetworkId = 99, CharacterId = "x", Name = "y", RecipeText = "z" };
            Assert.AreEqual(ServerBodyAdmission.AlreadyInText, rig.Admission.Admit(request));
            Assert.AreEqual(1, rig.Rules.Bodies.Count);
        }

        [Test]
        public void TheWorldGoingDown_TellsTheBodiesAndTakesThemAway()
        {
            var rig = BodyRig.Create(1);
            try
            {
                rig.Enter(0);
                GameObject body = rig.BodyOf(rig.KeyOf(0));
                var recorder = body.GetComponent<LeaveRecorder>();
                Assert.IsNotNull(recorder, "the stand-in's rule survived the assembler (an unknown type is kept)");
                rig.Tw.Dispose();
                Assert.IsTrue(body == null, "no body outlives its server world");
                Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            }
            finally { rig.Dispose(); }
        }

        /// <summary>Measure only (no time assert in CI): 200 bodies synced in one server update.</summary>
        [Test]
        public void Measure_200BodiesSync()
        {
            using var rig = BodyRig.Create(1);
            var em = rig.Server.EntityManager;
            Entity prefab = KnTestWorld.Singleton<PlayerGhostPrefab>(rig.Server).Prefab;
            var bodies = new List<GameObject>();
            try
            {
                for (int i = 0; i < 200; i++)
                {
                    PlayerKey key = PlayerKeys.Mint(100000UL + (ulong)i);
                    Entity ghost = PlayerEntitiesSetup.SpawnServer(em, prefab, 1000 + i, new int3(i * 100, 0, 0));
                    em.AddComponentData(ghost, new PlayerGhostKey { Key = key.Value });
                    var go = new GameObject("Body" + i);
                    bodies.Add(go);
                    GhostBodyRegistry.SetServerBody(key, go);
                }
                var sync = rig.Server.GetExistingSystemManaged<ServerBodySyncSystem>();
                var watch = Stopwatch.StartNew();
                sync.Update();
                watch.Stop();
                Assert.AreEqual(200, sync.LastSynced);
                Log($"200 bodies synced in {watch.Elapsed.TotalMilliseconds:0.000} ms (one update, Editor, physics sync included)");
            }
            finally
            {
                foreach (GameObject go in bodies) if (go != null) Object.DestroyImmediate(go);
            }
        }
    }
}
