using System.Collections.Generic;
using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The session seams of Core (NetSession, WorldTargets, IServerLeaving) answer "offline" with no source, follow a source, and reset.</summary>
    public sealed class NetSessionTests
    {
        private sealed class FakeSession : INetSessionSource
        {
            public bool Running, Server, Client, Host, Connected;
            public int Players = 3;
            public bool IsRunning => Running;
            public bool IsServer => Server;
            public bool IsClient => Client;
            public bool IsHost => Host;
            public bool IsConnectedClient => Connected;
            public bool IsShuttingDown => false;
            public int PlayerCount => Players;
            public int NetworkedObjectCount => 7;
            public int TickRate => 60;
            public bool TryGetRttMs(out long ms) { ms = 42; return true; }
        }

        private sealed class FakeTargets : IWorldTargets
        {
            public Transform Root;
            public bool TryGet(TargetId id, out WorldTarget t) { t = new WorldTarget(id, TargetKind.Hostile, Root, true, false); return id.Value == 5; }
            public bool TryFind(Transform part, out WorldTarget t) { t = new WorldTarget(new TargetId(5), TargetKind.Player, Root, true, false); return part != null; }
            public void Collect(List<WorldTarget> into) => into.Add(new WorldTarget(new TargetId(5), TargetKind.Hostile, Root, true, false));
            public bool SyncsTransform(GameObject body) => true;
        }

        private sealed class Leaver : MonoBehaviour, IServerLeaving
        {
            public int Calls;
            public void OnServerLeaving() => Calls++;
        }

        [TearDown]
        public void Reset()
        {
            NetSession.Use(null);
            WorldTargets.Use(null);
        }

        [Test]
        public void WithNoSourceTheProcessIsOffline()
        {
            NetSession.Use(null);
            Assert.AreEqual(NetMode.Offline, NetSession.Mode);
            Assert.AreEqual("none", NetSession.ModeWord);
            Assert.IsFalse(NetSession.IsRunning);
            Assert.IsFalse(NetSession.IsServer);
            Assert.IsTrue(NetSession.DecidesGame);
            Assert.AreEqual(0, NetSession.PlayerCount);
            Assert.IsFalse(NetSession.TryGetRttMs(out _));
        }

        [Test]
        public void ASourceDecidesTheMode()
        {
            var s = new FakeSession { Running = true, Server = true, Client = true, Host = true };
            NetSession.Use(s);
            Assert.AreEqual(NetMode.Host, NetSession.Mode);
            Assert.AreEqual(3, NetSession.PlayerCount);
            s.Host = false; s.Client = false;
            Assert.AreEqual(NetMode.Server, NetSession.Mode);
            Assert.IsTrue(NetSession.IsDedicatedServer);
            s.Server = false; s.Client = true; s.Connected = true;
            Assert.AreEqual(NetMode.Client, NetSession.Mode);
            Assert.AreEqual(1, NetSession.PlayerCount);
            Assert.IsTrue(NetSession.IsRemoteClient);
            Assert.IsFalse(NetSession.DecidesGame);
            Assert.IsTrue(NetSession.TryGetRttMs(out long ms));
            Assert.AreEqual(42, ms);
        }

        [Test]
        public void ANotRunningSourceReadsOffline()
        {
            NetSession.Use(new FakeSession { Running = false, Server = true });
            Assert.AreEqual(NetMode.Offline, NetSession.Mode);
            Assert.AreEqual(0, NetSession.NetworkedObjectCount);
        }

        [Test]
        public void WorldTargetsAreNoneWithoutASourceAndTheActorCountsAsLocal()
        {
            WorldTargets.Use(null);
            var go = new GameObject("actor");
            try
            {
                Assert.IsFalse(WorldTargets.TryGet(new TargetId(5), out _));
                Assert.IsFalse(WorldTargets.TryFind(go.transform, out _));
                Assert.AreSame(go.transform, WorldTargets.RootOf(go.transform));
                Assert.IsFalse(WorldTargets.IsPlayer(go.transform));
                Assert.IsTrue(WorldTargets.IsLocalPlayer(go.transform));
                Assert.IsFalse(WorldTargets.IsLocalPlayer(null));
                Assert.IsFalse(WorldTargets.SyncsTransform(go));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void WorldTargetsFollowTheirSourceByKey()
        {
            var root = new GameObject("root");
            var child = new GameObject("child");
            child.transform.SetParent(root.transform);
            try
            {
                WorldTargets.Use(new FakeTargets { Root = root.transform });
                Assert.IsTrue(WorldTargets.TryGet(new TargetId(5), out WorldTarget t));
                Assert.AreEqual(TargetKind.Hostile, t.Kind);
                Assert.IsFalse(WorldTargets.TryGet(new TargetId(0), out _), "key 0 is none");
                Assert.AreSame(root.transform, WorldTargets.RootOf(child.transform));
                Assert.IsTrue(WorldTargets.IsPlayer(child.transform));
                Assert.IsFalse(WorldTargets.IsLocalPlayer(child.transform), "a live player that is not ours");
                var all = new List<WorldTarget>();
                WorldTargets.Collect(all);
                Assert.AreEqual(1, all.Count);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AllRulesOnABodyAreToldItsPlayerIsLeaving()
        {
            var body = new GameObject("body", typeof(Leaver));
            try
            {
                ServerLeaving.Notify(body);
                Assert.AreEqual(1, body.GetComponent<Leaver>().Calls);
                ServerLeaving.Notify(null); // nothing to do, no error
            }
            finally { Object.DestroyImmediate(body); }
        }
    }
}
