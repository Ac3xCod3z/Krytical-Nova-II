using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Tests;
using Debug = UnityEngine.Debug;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>Plain GameObjects as player bodies; counts what it made and took away.</summary>
    sealed class PlainBodyFactory : IPlayerBodyFactory
    {
        public int Created, Destroyed;
        public readonly List<PlayerBodyRequest> Requests = new List<PlayerBodyRequest>();

        public GameObject Create(in PlayerBodyRequest request)
        {
            Created++;
            Requests.Add(request);
            var go = new GameObject("TestBody " + request.NetworkId);
            go.transform.position = request.Position;
            return go;
        }

        public void Destroy(GameObject body)
        {
            Destroyed++;
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
        }
    }

    /// <summary>The M3a-1 rig (server + 2 clients, 2 ghosts in each client) with bodies installed. Client 0 is the one local player.</summary>
    sealed class PresRig : IDisposable
    {
        public PlayerRig Rig;
        public PlainBodyFactory Factory = new PlainBodyFactory();

        public static PresRig Create(int delayMs)
        {
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            var p = new PresRig { Rig = PlayerRig.Create(2, delayMs) };
            PlayerPresentationSetup.Install(p.Rig.Clients[0], p.Factory, null, registerLocalPlayer: true);
            PlayerPresentationSetup.Install(p.Rig.Clients[1], p.Factory, null, registerLocalPlayer: false);
            p.Rig.Tick(3);
            return p;
        }

        public GhostBodyEntry EntryOf(int viewer, int owner)
        {
            Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(Rig.Clients[viewer], Rig.GhostIn(viewer, owner), out var e),
                $"client {viewer} has no body for the ghost of client {owner}");
            return e;
        }

        public Transform BodyOf(int viewer, int owner) => EntryOf(viewer, owner).Body.transform;

        public static int CountIn(World w)
        {
            int n = 0;
            foreach (var e in GhostBodyRegistry.Entries) if (ReferenceEquals(e.World, w)) n++;
            return n;
        }

        public void Dispose()
        {
            Rig?.Dispose();
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
        }
    }

    /// <summary>
    /// Layer C for the presentation: a server and two clients in one process, a plain GameObject body per ghost. Every test ticks by
    /// hand at 30 Hz; the delay variants make the owner predict ahead and roll back like the real game.
    /// </summary>
    public class PresentationWorldTests
    {
        static readonly int3 Goal = new int3(20000, 0, 0);

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); }

        static Vector3 V(float3 f) => new Vector3(f.x, f.y, f.z);

        static void Log(string line) => Debug.Log("[PLAYER-M3A2A] " + line);

        // ---------------------------------------------------------------------------------------------- spawn and despawn
        [Test]
        public void ABodyIsMadeForEveryGhost_AndDestroyedWhenItDespawns()
        {
            using var p = PresRig.Create(0);
            var rig = p.Rig;
            Assert.AreEqual(4, p.Factory.Created, "2 clients x 2 ghosts");
            Assert.AreEqual(2, PresRig.CountIn(rig.Clients[0]));
            Assert.AreEqual(2, PresRig.CountIn(rig.Clients[1]));
            Assert.AreEqual(4, PlayerBodies.All.Count);
            var doomed = p.EntryOf(0, 1).Body;
            var view = p.EntryOf(0, 1).View;
            Assert.IsTrue(view.IsAlive);
            Assert.AreEqual(KryticalNova.Core.PlayerKey.None, view.Owner, "a client keeps no keys");

            rig.Server.EntityManager.DestroyEntity(rig.ServerGhosts[1]);
            int ok = rig.Tw.TickUntil(() => PresRig.CountIn(rig.Clients[0]) == 1 && PresRig.CountIn(rig.Clients[1]) == 1, 200);
            Assert.GreaterOrEqual(ok, 0, "bodies of the despawned ghost were not removed");
            Assert.IsTrue(doomed == null, "the body object is destroyed");
            Assert.AreEqual(2, p.Factory.Destroyed);
            Assert.AreEqual(2, PlayerBodies.All.Count);
            Assert.IsFalse(view.IsAlive, "the view of a gone ghost says so");
        }

        [Test]
        public void TheBodyStartsWhereTheGhostIs()
        {
            using var p = PresRig.Create(0);
            foreach (var r in p.Factory.Requests)
                Assert.AreEqual(r.NetworkId == KnNetworkId(p, 0) ? 0f : 5f, r.Position.z, 0.01f);
        }

        static int KnNetworkId(PresRig p, int client) => KnTestWorld.NetworkIdOf(p.Rig.Clients[client]);

        // ---------------------------------------------------------------------------------------------- local and remote
        [Test]
        public void TheOwner_IsInLocalPlayer_ARemoteGhostOnlyInPlayerBodies()
        {
            using var p = PresRig.Create(0);
            var mine = p.EntryOf(0, 0);
            var theirs = p.EntryOf(0, 1);
            Assert.IsTrue(mine.IsLocal); Assert.IsFalse(theirs.IsLocal);
            Assert.AreSame(mine.Body, LocalPlayer.Object, "the owner's body is the local player");
            Assert.AreNotSame(theirs.Body, LocalPlayer.Object);
            Assert.IsTrue(mine.View.IsLocal); Assert.IsFalse(theirs.View.IsLocal);
            Assert.IsTrue(NetRole.IsLocalOwner(mine.View)); Assert.IsFalse(NetRole.IsLocalOwner(theirs.View));
            bool inList = false;
            foreach (var b in PlayerBodies.All) if (ReferenceEquals(b, theirs.View)) inList = true;
            Assert.IsTrue(inList, "a remote ghost is listed in PlayerBodies");
            Assert.AreSame(theirs.Body.transform, theirs.View.Transform);
            // the second client world's owner is NOT registered as local (it asked not to)
            Assert.IsFalse(p.EntryOf(1, 1).IsLocal);
            // the view reads the entity
            Assert.AreEqual(5f, theirs.View.GhostPosition.z, 0.01f);
        }

        // ---------------------------------------------------------------------------------------------- following
        [Test]
        public void TheBodies_ArePlacedAtTheEndOfTheSimulation_BeforeTheCameraReadsThem()
        {
            // owner's GUI retry 2 (30 Oct): placed in the PresentationSystemGroup (after every LateUpdate) the body was a frame behind
            // the Cinemachine brain (LateUpdate) and a walk across the screen juddered; the NGO game placed its walk before the camera
            using var p = PresRig.Create(0);
            var client = p.Rig.Clients[0];
            var group = client.GetExistingSystemManaged<PlayerBodiesBeforeCameraGroup>();
            Assert.IsNotNull(group);
            var sim = client.GetExistingSystemManaged<SimulationSystemGroup>();
            Assert.Contains(group, (System.Collections.ICollection)sim.ManagedSystems, "in the simulation (the player loop's Update phase)");
            var inGroup = group.ManagedSystems;
            Assert.Contains(client.GetExistingSystemManaged<PlayerBodySpawnSystem>(), (System.Collections.ICollection)inGroup);
            Assert.Contains(client.GetExistingSystemManaged<PlayerPresentationFollowSystem>(), (System.Collections.ICollection)inGroup);
        }

        [Test]
        public void TheOwnersBody_FacesTheWayItWalks_AndTurnsForANewClick()
        {
            // owner's GUI retry 3 (30 Oct): the body slid to the click still facing its old way
            using var p = PresRig.Create(100);
            var rig = p.Rig;
            var body = p.BodyOf(0, 0);
            var observer = p.BodyOf(1, 0);
            rig.Sources[0].Click(new int3(20000, 0, 0));
            rig.Tick(30);
            Assert.AreEqual(90f, body.eulerAngles.y, 1f, "walking along +X the body faces +X");
            rig.Sources[0].Click(new int3(-20000, 0, 0));
            rig.Tick(30);
            Assert.AreEqual(270f, body.eulerAngles.y, 1f, "a click behind turns it round to -X");
            rig.Tick(30);
            Assert.AreEqual(270f, observer.eulerAngles.y, 1f, "and another player sees it face that way too");
        }

        [Test]
        public void APoseChangedAfterTheFollow_IsPutBackBeforeTheCamera()
        {
            using var p = PresRig.Create(0);
            var rig = p.Rig;
            rig.Sources[0].Click(new int3(20000, 0, 0));
            rig.Tick(20);
            var body = p.BodyOf(0, 0);
            var state = p.EntryOf(0, 0).State;
            Assert.IsTrue(state.HasPose);
            Vector3 pos = body.position;
            float yaw = body.eulerAngles.y;
            Assert.IsFalse(state.ReapplyPose(), "nothing changed it: nothing to do");
            body.SetPositionAndRotation(pos + Vector3.left, Quaternion.Euler(0f, yaw + 120f, 0f));   // what an animation or a script might do
            Assert.IsTrue(state.ReapplyPose());
            Assert.AreEqual(pos, body.position);
            Assert.AreEqual(yaw, body.eulerAngles.y, 0.01f);
            Assert.AreEqual(1, state.Overwritten);
        }

        [Test]
        public void TheBodies_FollowTheGhosts_OverAWalk_At100ms()
        {
            using var p = PresRig.Create(100);
            var rig = p.Rig;
            var ownerBody = p.BodyOf(0, 0);
            var observerBody = p.BodyOf(1, 0);
            var ownerGhost = rig.GhostIn(0, 0);
            var state = p.EntryOf(0, 0).State;
            rig.Sources[0].Click(Goal);
            float worstOwner = 0f, worstObserver = 0f;
            bool sawMoving = false, sawRunning = false;
            for (int i = 0; i < 220; i++)
            {
                rig.Tick(1);
                float server = PlayerRig.M(rig.ServerPose()).x;
                var ghost = rig.Get<LocalTransform>(rig.Clients[0], ownerGhost).Position;
                worstOwner = math.max(worstOwner, Vector3.Distance(ownerBody.position, V(ghost)));
                worstObserver = math.max(worstObserver, server - observerBody.position.x);
                Assert.LessOrEqual(observerBody.position.x, server + 0.05f, "a remote body is never ahead of the server");
                sawMoving |= state.State.Moving;
                sawRunning |= state.State.Running;
            }
            Log($"follow at 100 ms: worst owner body-to-ghost {worstOwner:F3} m, worst observer lag behind the server {worstObserver:F2} m");
            Assert.Less(worstOwner, 0.5f, "the owner's body stays with its predicted ghost");
            Assert.Less(worstObserver, 3.0f, "a remote body follows the server within the interpolation delay");
            Assert.AreEqual(Goal.x * 0.001f, ownerBody.position.x, 0.02f);
            Assert.AreEqual(Goal.x * 0.001f, observerBody.position.x, 0.02f);
            Assert.IsTrue(sawMoving, "the animation state said Moving during the walk");
            Assert.IsFalse(sawRunning, "a click without run is a walk");
            Assert.IsFalse(state.State.Moving, "and it stopped on arrival");
            Assert.AreEqual(0f, state.State.Speed, 1e-4f);
        }

        [Test]
        public void ARollbackCorrection_SlidesTheBody_InsteadOfPopping()
        {
            using var p = PresRig.Create(100);
            var rig = p.Rig;
            rig.Tick(10);
            var em = rig.Clients[0].EntityManager;
            var ghost = rig.GhostIn(0, 0);
            var body = p.BodyOf(0, 0);

            // push the idle owner ghost 2 m off in its own world: the next snapshot rolls it back, which is the correction
            var pose = em.GetComponentData<PlayerPose>(ghost); pose.XMm += 2000; em.SetComponentData(ghost, pose);
            var lt = em.GetComponentData<LocalTransform>(ghost); lt.Position.x += 2f; em.SetComponentData(ghost, lt);

            float lastGhost = rig.Get<LocalTransform>(rig.Clients[0], ghost).Position.x, lastBody = body.position.x;
            float maxGhostStep = 0f, maxBodyStep = 0f;
            for (int i = 0; i < 40; i++)
            {
                rig.Tick(1);
                float g = rig.Get<LocalTransform>(rig.Clients[0], ghost).Position.x;
                maxGhostStep = math.max(maxGhostStep, math.abs(g - lastGhost));
                maxBodyStep = math.max(maxBodyStep, math.abs(body.position.x - lastBody));
                lastGhost = g; lastBody = body.position.x;
            }
            Log($"correction: ghost popped {maxGhostStep:F2} m in one frame, the body's largest frame step {maxBodyStep:F2} m");
            if (maxGhostStep < 1.5f) Assert.Inconclusive("the ghost never popped (" + maxGhostStep + " m): the injected error did not survive; the test proves nothing");
            Assert.Less(maxBodyStep, 0.7f, "the body slides");
            Assert.Less(maxBodyStep, maxGhostStep * 0.5f);
            Assert.AreEqual(lastGhost, body.position.x, 0.05f, "and ends on the ghost");
        }

        [Test]
        public void ATeleport_SnapsTheBody_WithinOneFrame()
        {
            using var p = PresRig.Create(100);
            var rig = p.Rig;
            var em = rig.Clients[0].EntityManager;
            var ghost = rig.GhostIn(0, 0);
            var entry = p.EntryOf(0, 0);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(40);
            byte seq = rig.Get<TeleportSeq>(rig.Clients[0], ghost).Value;
            int snaps = entry.State.Snaps;
            var dest = new int3(-7000, 500, 12000);
            rig.Teleport(0, dest, 9000);

            int n = 0;
            while (rig.Get<TeleportSeq>(rig.Clients[0], ghost).Value == seq && n++ < 150) rig.Tick(1);
            Assert.Less(n, 150, "the owner never saw the placement");
            float Gap() => Vector3.Distance(entry.Body.transform.position, V(em.GetComponentData<LocalTransform>(ghost).Position));
            if (Gap() > 0.01f) rig.Tick(1);                  // one more frame at most
            Assert.Less(Gap(), 0.01f, "the body is on the placement within a frame of the ghost");
            Assert.AreEqual(dest.x * 0.001f, entry.Body.transform.position.x, 0.02f);
            Assert.AreEqual(snaps + 1, entry.State.Snaps, "it snapped once, not eased");
        }

        // ---------------------------------------------------------------------------------------------- lifetime
        [Test]
        public void DisposingTheWorlds_LeavesNoEntryAndNoBody()
        {
            var p = PresRig.Create(0);
            int made = p.Factory.Created;
            Assert.Greater(made, 0);
            var local = LocalPlayer.Object;
            Assert.IsNotNull(local);
            p.Rig.Dispose();
            Assert.AreEqual(0, GhostBodyRegistry.Count, "the registry holds no dead entry");
            Assert.AreEqual(made, p.Factory.Destroyed, "every body was taken away with its world");
            Assert.IsTrue(local == null);
            Assert.IsNull(LocalPlayer.Object);
            Assert.AreEqual(0, PlayerBodies.All.Count);
        }

        // ---------------------------------------------------------------------------------------------- click source
        [Test]
        public void AClickFromTheClickSource_WalksTheOwner()
        {
            using var p = PresRig.Create(0);
            var rig = p.Rig;
            var clicks = new FakeClickSource();
            PlayerPresentationSetup.Install(rig.Clients[0], p.Factory, clicks, registerLocalPlayer: true);
            clicks.Click(new Vector3(10f, 0f, 0f));
            rig.Tick(120);
            Assert.AreEqual(0, clicks.Pending);
            Assert.AreEqual(10000, rig.ServerPose().XMm, 20);
            Assert.AreEqual(10f, p.BodyOf(0, 0).position.x, 0.05f);
        }
    }
}
