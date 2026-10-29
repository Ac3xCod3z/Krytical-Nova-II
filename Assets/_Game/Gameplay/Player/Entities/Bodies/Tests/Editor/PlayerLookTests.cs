using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities.Tests;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>M3a-2b step 8 (worlds): the look of observers by version, paged, cached; a late joiner gets everyone's.</summary>
    public class PlayerLookTests
    {
        sealed class RecordingApplier : ILookApplier
        {
            public readonly Dictionary<GameObject, string> Seen = new Dictionary<GameObject, string>();
            public int Calls;
            public void Apply(GameObject body, string lookText) { Calls++; Seen[body] = lookText; }
        }

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        GameObject m_View;
        [SetUp] public void SetUp() => m_View = new GameObject("StandInLookView");
        [TearDown] public void TearDown() { if (m_View != null) Object.DestroyImmediate(m_View); }

        static string LongLook(char c, int n)
        {
            var sb = new StringBuilder("Name%20").Append('\n');
            for (int i = 0; i < n; i++) sb.Append(c);
            return sb.ToString();
        }

        RecordingApplier InstallView(BodyRig rig, World client, bool local)
        {
            PlayerPresentationSetup.Install(client, new ClientBodyFactory(m_View), null, registerLocalPlayer: local);
            var applier = new RecordingApplier();
            client.GetExistingSystemManaged<PlayerLookClientSystem>().Applier = applier;
            return applier;
        }

        static GameObject BodyIn(World client, BodyRig rig, PlayerKey ownerKey, World ownerClient)
        {
            int id = KnTestWorld.NetworkIdOf(ownerClient);
            return GhostBodyRegistry.TryGetByNetworkId(client, id, out GhostBodyEntry e) ? e.Body : null;
        }

        [Test]
        public void ALongLook_ArrivesInPages_OnTheRightBody_AndALateJoinerGetsIt()
        {
            using var rig = BodyRig.Create(2);
            World a = rig.Tw.Clients[0], b = rig.Tw.Clients[1];
            RecordingApplier seenByA = InstallView(rig, a, true);
            RecordingApplier seenByB = InstallView(rig, b, false);
            var server = rig.Server.GetExistingSystemManaged<PlayerLookServerSystem>();

            rig.Enter(a);
            string lookA = LongLook('a', 1200); // three pages
            uint versionA = server.SetLook(rig.GhostOf(rig.KeyOf(a)), lookA);
            Assert.Greater(versionA, 0u);
            int ok = rig.Tw.TickUntil(() => seenByA.Seen.Count == 1, 200);
            Assert.GreaterOrEqual(ok, 0, "A never got its own look");
            GameObject aSeenByA = BodyIn(a, rig, rig.KeyOf(a), a);
            Assert.AreEqual(lookA, seenByA.Seen[aSeenByA]);

            rig.Enter(b); // the late joiner
            server.SetLook(rig.GhostOf(rig.KeyOf(b)), LongLook('b', 10));
            ok = rig.Tw.TickUntil(() => seenByB.Seen.Count == 2 && seenByA.Seen.Count == 2, 300);
            Assert.GreaterOrEqual(ok, 0, "both clients get both looks");
            Assert.AreEqual(lookA, seenByB.Seen[BodyIn(b, rig, rig.KeyOf(a), a)], "the late joiner sees A's look");
            Assert.AreEqual(LongLook('b', 10), seenByA.Seen[BodyIn(a, rig, rig.KeyOf(b), b)]);
        }

        [Test]
        public void ACachedVersion_IsAppliedAgainWithoutAsking()
        {
            using var rig = BodyRig.Create(1);
            World a = rig.Tw.Clients[0];
            RecordingApplier seen = InstallView(rig, a, true);
            rig.Enter(a);
            rig.Server.GetExistingSystemManaged<PlayerLookServerSystem>().SetLook(rig.GhostOf(rig.KeyOf(a)), "N\nlook");
            Assert.GreaterOrEqual(rig.Tw.TickUntil(() => seen.Calls == 1, 200), 0);
            var looks = a.GetExistingSystemManaged<PlayerLookClientSystem>();
            int asked = looks.Requests;
            rig.Tick(60);
            Assert.AreEqual(1, seen.Calls, "an applied look is not applied again");
            Assert.AreEqual(asked, looks.Requests, "nor asked for again");

            looks.ForgetApplied(); // as when a body is made again for the same ghost
            rig.Tick(2);
            Assert.AreEqual(2, seen.Calls, "put on again from the cache");
            Assert.AreEqual(asked, looks.Requests, "without a request");
        }

        [Test]
        public void TheVersion_GoesUpOncePerDifferentLook()
        {
            using var rig = BodyRig.Create(1);
            rig.Enter(0);
            var server = rig.Server.GetExistingSystemManaged<PlayerLookServerSystem>();
            Entity ghost = rig.GhostOf(rig.KeyOf(0));
            uint v1 = server.SetLook(ghost, "N\none");
            Assert.AreEqual(v1, server.SetLook(ghost, "N\none"), "the same look keeps its number");
            uint v2 = server.SetLook(ghost, "N\ntwo");
            Assert.AreEqual(v1 + 1, v2);
            Assert.AreEqual(v2, rig.Server.EntityManager.GetComponentData<PlayerLook>(ghost).Version);
            Assert.AreEqual(0u, server.SetLook(Entity.Null, "x"), "no ghost, no version");
        }

        [Test]
        public void NoLookYet_ShowsThePrefabsOwn_AndNothingIsAsked()
        {
            using var rig = BodyRig.Create(1);
            World a = rig.Tw.Clients[0];
            RecordingApplier seen = InstallView(rig, a, true);
            rig.Enter(a);
            rig.Tick(60);
            Assert.AreEqual(0, seen.Calls);
            Assert.AreEqual(0, a.GetExistingSystemManaged<PlayerLookClientSystem>().Requests, "version 0 means no look: nothing to ask");
        }

        [Test]
        public void TheCache_IsBounded()
        {
            var cache = new LookCache();
            for (uint v = 1; v <= LookCache.Capacity; v++) cache.Put(v, "t");
            Assert.AreEqual(LookCache.Capacity, cache.Count);
            cache.Put(LookCache.Capacity + 1, "u");
            Assert.AreEqual(1, cache.Count, "full: it starts again");
            Assert.IsTrue(cache.TryGet(LookCache.Capacity + 1, out string text) && text == "u");
            cache.Put(0, "never");
            Assert.IsFalse(cache.TryGet(0, out _));
        }
    }
}
