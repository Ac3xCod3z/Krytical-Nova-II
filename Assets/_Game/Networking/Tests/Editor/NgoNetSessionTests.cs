using KryticalNova.Core;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>The Netcode-for-GameObjects adapter of Core's session seams: with no NetworkManager it reads offline, and it describes a loose NetworkObject.</summary>
    public sealed class NgoNetSessionTests
    {
        [Test]
        public void WithNoNetworkManagerTheAdapterIsOffline()
        {
            if (NetworkManager.Singleton != null) Assert.Ignore("a NetworkManager exists in this Editor session");
            NetSession.Use(NgoNetSession.Instance);
            try
            {
                Assert.AreEqual(NetMode.Offline, NetSession.Mode);
                Assert.AreEqual(0, NetSession.PlayerCount);
                Assert.IsFalse(NetSession.TryGetRttMs(out _));
                Assert.AreEqual(-1, NgoNetSession.Instance.NetworkedObjectCount);
                Assert.IsFalse(WorldTargetsOf().TryGet(new TargetId(9), out _));
            }
            finally { NetSession.Use(null); }
        }

        [Test]
        public void ANetworkObjectThatIsNotSpawnedIsNotLiveAndCountsAsLocal()
        {
            var go = new GameObject("loose", typeof(NetworkObject));
            var part = new GameObject("part");
            part.transform.SetParent(go.transform);
            try
            {
                Assert.IsTrue(NgoNetSession.Instance.TryFind(part.transform, out WorldTarget t));
                Assert.IsFalse(t.IsLive);
                Assert.AreSame(go.transform, t.Transform);
                Assert.IsTrue(t.Id.IsNone);
                WorldTargets.Use(NgoNetSession.Instance);
                Assert.IsTrue(WorldTargets.IsLocalPlayer(part.transform));
                Assert.IsFalse(WorldTargets.IsPlayer(part.transform));
            }
            finally
            {
                WorldTargets.Use(null);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheCoreSlotsAreEmptyAfterTheirReset()
        {
            NetSession.Use(NgoNetSession.Instance);
            NetSession.Use(null);
            Assert.IsFalse(NetSession.HasSource);
        }

        private static IWorldTargets WorldTargetsOf() => NgoNetSession.Instance;
    }
}
