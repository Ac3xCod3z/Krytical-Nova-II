using System;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>M3a-2b step 1: the role adapter, the session source and the refusal queue (plain GameObjects, no Awake needed).</summary>
    public class BodyRoleAdapterTests
    {
        private sealed class AwareStub : MonoBehaviour, IRoleAwareBody { }
        private sealed class PlainStub : MonoBehaviour { }

        private sealed class FakeNgo : INetRoleAdapter
        {
            public NetRoleKind Kind = NetRoleKind.ClientView;
            public string Dropped;
            public NetRoleKind Process => Kind;
            public NetRoleKind Of(Component c) => Kind;
            public bool IsOwner(Component c) => true;
            public bool IsOwnedByServer(Component c) => true;
            public bool IsRemoteOwned(Component c) => true;
            public ulong OwnerId(Component c) => 77;
            public void DisconnectOwner(Component c, string reason) => Dropped = reason;
        }

        private GameObject m_Body;
        private NetRoleKind m_Process;

        [SetUp]
        public void SetUp()
        {
            PlayerKeys.Reset();
            KnPlayerRefusals.Clear();
            m_Process = NetRoleKind.ServerBody;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Body != null) UnityEngine.Object.DestroyImmediate(m_Body);
            PlayerKeys.Reset();
            KnPlayerRefusals.Clear();
        }

        private BodyRoleNetRoleAdapter Make(INetRoleAdapter fallback = null) => new BodyRoleNetRoleAdapter(fallback, () => m_Process);

        private BodyRoleTag Tagged(NetRoleKind kind, PlayerKey key, ulong serial, bool local)
        {
            m_Body = new GameObject("Body");
            var tag = m_Body.AddComponent<BodyRoleTag>();
            tag.Assign(kind, key, serial, local);
            return tag;
        }

        [Test]
        public void Process_FollowsTheWorlds_AndFallsBackWhenNone()
        {
            var adapter = Make(new FakeNgo { Kind = NetRoleKind.ClientView });
            m_Process = NetRoleKind.ServerBody;
            Assert.AreEqual(NetRoleKind.ServerBody, adapter.Process);
            m_Process = NetRoleKind.Offline;
            Assert.AreEqual(NetRoleKind.ClientView, adapter.Process, "no entity world: the NGO adapter answers");
            Assert.AreEqual(NetRoleKind.Offline, Make().Process);
        }

        [Test]
        public void Of_IsOfflineForAPlainComponent_AndTheTagKindForAnAwareOne()
        {
            Tagged(NetRoleKind.ServerBody, new PlayerKey(1), 9, false);
            var aware = m_Body.AddComponent<AwareStub>();
            var plain = m_Body.AddComponent<PlainStub>();
            var adapter = Make();
            Assert.AreEqual(NetRoleKind.ServerBody, adapter.Of(aware));
            Assert.AreEqual(NetRoleKind.Offline, adapter.Of(plain), "unported systems stay stubs");
        }

        [Test]
        public void Of_AwareComponentWithoutTag_IsOffline_AndAChildFindsTheRootTag()
        {
            m_Body = new GameObject("Body");
            var childObj = new GameObject("Child");
            childObj.transform.SetParent(m_Body.transform);
            var aware = childObj.AddComponent<AwareStub>();
            var adapter = Make();
            Assert.AreEqual(NetRoleKind.Offline, adapter.Of(aware));
            var tag = m_Body.AddComponent<BodyRoleTag>();
            tag.Assign(NetRoleKind.ClientView, PlayerKey.None, 0, true);
            Assert.AreEqual(NetRoleKind.ClientView, adapter.Of(aware));
        }

        [Test]
        public void Owner_IsTheLocalClientViewOnly()
        {
            var tag = Tagged(NetRoleKind.ClientView, PlayerKey.None, 0, false);
            var aware = m_Body.AddComponent<AwareStub>();
            var adapter = Make();
            m_Process = NetRoleKind.ClientView;
            Assert.IsFalse(adapter.IsOwner(aware));
            Assert.IsTrue(adapter.IsRemoteOwned(aware));
            tag.SetLocal(true);
            Assert.IsTrue(adapter.IsOwner(aware));
            Assert.IsFalse(adapter.IsOwnedByServer(aware));
            Assert.IsTrue(BodyRoleTag.IsLocalClientView(aware));
        }

        [Test]
        public void ServerBody_IsNeverTheOwner()
        {
            Tagged(NetRoleKind.ServerBody, new PlayerKey(1), 4, true);
            var aware = m_Body.AddComponent<AwareStub>();
            Assert.IsFalse(Make().IsOwner(aware));
        }

        [Test]
        public void SetLocal_RaisesTheEventOnlyOnAChange()
        {
            var tag = Tagged(NetRoleKind.ClientView, PlayerKey.None, 0, false);
            int raised = 0;
            tag.LocalChanged += _ => raised++;
            tag.SetLocal(false);
            tag.SetLocal(true);
            tag.SetLocal(true);
            Assert.AreEqual(1, raised);
        }

        [Test]
        public void OwnerId_IsTheSerial_ThatFindsTheKey()
        {
            PlayerKey key = PlayerKeys.Mint(42);
            Tagged(NetRoleKind.ServerBody, key, 42, false);
            var aware = m_Body.AddComponent<AwareStub>();
            ulong id = Make().OwnerId(aware);
            Assert.AreEqual(42UL, id);
            Assert.AreEqual(key, PlayerKeys.Find(id), "what PlayerSaveAgent does with NetRole.OwnerId");
        }

        [Test]
        public void DisconnectOwner_QueuesOnce_AndIgnoresADeadKey()
        {
            PlayerKey key = PlayerKeys.Mint(5);
            Tagged(NetRoleKind.ServerBody, key, 5, false);
            var aware = m_Body.AddComponent<AwareStub>();
            var adapter = Make();
            adapter.DisconnectOwner(aware, "first");
            adapter.DisconnectOwner(aware, "second");
            Assert.AreEqual(1, KnPlayerRefusals.Count);
            Assert.IsTrue(KnPlayerRefusals.TryDequeue(out PlayerKey got, out string reason));
            Assert.AreEqual(key, got);
            Assert.AreEqual("first", reason);

            PlayerKeys.Release(5);
            adapter.DisconnectOwner(aware, "late");
            Assert.AreEqual(0, KnPlayerRefusals.Count, "a dead key is ignored");
        }

        [Test]
        public void Queue_DropsEntriesWhoseKeyDiedBeforeItWasTaken()
        {
            PlayerKey a = PlayerKeys.Mint(1);
            PlayerKey b = PlayerKeys.Mint(2);
            Assert.IsTrue(KnPlayerRefusals.Enqueue(a, "a"));
            Assert.IsTrue(KnPlayerRefusals.Enqueue(b, "b"));
            PlayerKeys.Release(1);
            Assert.IsTrue(KnPlayerRefusals.TryDequeue(out PlayerKey got, out _));
            Assert.AreEqual(b, got);
            Assert.IsFalse(KnPlayerRefusals.TryDequeue(out _, out _));
        }

        [Test]
        public void WithoutAnEntityWorld_TheNgoAdapterAnswersEverything()
        {
            var ngo = new FakeNgo { Kind = NetRoleKind.ClientView };
            var adapter = Make(ngo);
            m_Process = NetRoleKind.Offline;
            m_Body = new GameObject("Body");
            var plain = m_Body.AddComponent<PlainStub>();
            Assert.AreEqual(NetRoleKind.ClientView, adapter.Of(plain));
            Assert.IsTrue(adapter.IsOwner(plain));
            Assert.AreEqual(77UL, adapter.OwnerId(plain));
            adapter.DisconnectOwner(plain, "x");
            Assert.AreEqual("x", ngo.Dropped);
        }

        [Test]
        public void Install_PutsTheAdapterAndSessionInFront_OnceAndKeepsTheOldOnesAsFallback()
        {
            INetRoleAdapter oldRole = NetRole.Adapter;
            INetSessionSource oldSession = NetSession.Source;
            try
            {
                var ngo = new FakeNgo();
                NetRole.Install(ngo);
                BodyRoleNetRoleAdapter.Install();
                var installed = NetRole.Adapter;
                Assert.IsInstanceOf<BodyRoleNetRoleAdapter>(installed);
                BodyRoleNetRoleAdapter.Install();
                Assert.AreSame(installed, NetRole.Adapter, "idempotent");
                Assert.IsInstanceOf<KnNetSession>(NetSession.Source);
                Assert.AreEqual(77UL, installed.OwnerId(new GameObject("x").transform), "no world: falls back to the old adapter");
            }
            finally
            {
                NetRole.Install(oldRole);
                NetSession.Use(oldSession);
                foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (go.name == "x") UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ServerPolicy_OverrideWinsOverTheFallback()
        {
            ServerPolicy.RequireProofOverride = null;
            Assert.IsTrue(ServerPolicy.RequireProof(true));
            Assert.IsFalse(ServerPolicy.RequireProof(false));
            ServerPolicy.RequireProofOverride = false;
            Assert.IsFalse(ServerPolicy.RequireProof(true));
            ServerPolicy.RequireProofOverride = null;
        }

        [Test]
        public void NetSession_WithoutWorlds_AsksTheOldSource()
        {
            var session = new KnNetSession(null);
            Assert.IsFalse(session.IsRunning);
            Assert.IsFalse(session.IsServer);
            Assert.AreEqual(0, session.TickRate);
            Assert.IsFalse(session.TryGetRttMs(out _));
        }
    }

    /// <summary>The queue turns into a JoinRefusedRpc and a disconnect on the server (W: real test worlds).</summary>
    public class KnPlayerRefusalSystemTests
    {
        [Test]
        public void QueuedRefusal_ReachesTheClientAndClosesItsConnection()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            tw.Connect();
            tw.Tick(5);
            var keys = KnTestQueries.ServerKeys(tw);
            Assert.AreEqual(2, keys.Count);
            int firstId = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            ulong firstKey = keys.Find(k => k.id == firstId).key;

            Assert.IsTrue(KnPlayerRefusals.Enqueue(new PlayerKey(firstKey), "account is online elsewhere"));
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<KnJoinRefusal>(tw.Clients[0]) == 1, 300), 0,
                "no refusal arrived. Logs: " + string.Join(" | ", tw.Interesting));
            Assert.AreEqual("account is online elsewhere", KnTestWorld.Singleton<KnJoinRefusal>(tw.Clients[0]).Detail.ToString());
            Assert.GreaterOrEqual(tw.TickUntil(() => KnTestWorld.Count<Unity.NetCode.NetworkId>(tw.Server) == 1, 300), 0, "connection was not closed");
            Assert.AreEqual(0, KnTestWorld.Count<KnJoinRefusal>(tw.Clients[1]), "the other player is untouched");
        }
    }
}
