using System.Reflection;
using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// NetRole (DOTS seam): with no adapter every answer is the offline one; an installed adapter is asked; the startup
    /// reset removes it; the Netcode adapter reads Offline when no session is listening.
    /// </summary>
    public sealed class NetRoleTests
    {
        private INetRoleAdapter before;
        private GameObject host;

        [SetUp]
        public void Keep()
        {
            before = NetRole.Adapter;
            host = new GameObject("netrole-test", typeof(BoxCollider));
        }

        [TearDown]
        public void Restore()
        {
            NetRole.Install(before);
            Object.DestroyImmediate(host);
        }

        private sealed class Fake : INetRoleAdapter
        {
            public NetRoleKind Proc, Obj;
            public bool Owner, ServerOwned, Remote;
            public ulong Id;
            public string Dropped;
            public NetRoleKind Process => Proc;
            public NetRoleKind Of(Component c) => Obj;
            public bool IsOwner(Component c) => Owner;
            public bool IsOwnedByServer(Component c) => ServerOwned;
            public bool IsRemoteOwned(Component c) => Remote;
            public ulong OwnerId(Component c) => Id;
            public void DisconnectOwner(Component c, string reason) => Dropped = reason;
        }

        [Test]
        public void WithoutAnAdapterEverythingIsOffline()
        {
            NetRole.Install(null);
            Component c = host.GetComponent<BoxCollider>();
            Assert.IsTrue(NetRole.IsOffline);
            Assert.IsTrue(NetRole.IsServerAuthority);
            Assert.IsFalse(NetRole.IsClientView);
            Assert.AreEqual(NetRoleKind.Offline, NetRole.Of(c));
            Assert.IsTrue(NetRole.MayMutate(c));
            Assert.IsTrue(NetRole.IsUnspawned(c));
            Assert.IsTrue(NetRole.IsLocalOwner(c), "the offline avatar is the local player");
            Assert.IsFalse(NetRole.IsOwner(c));
            Assert.IsFalse(NetRole.IsRemoteOwned(c));
            Assert.AreEqual(0UL, NetRole.OwnerId(c));
            Assert.DoesNotThrow(() => NetRole.DisconnectOwner(c, "x"));
        }

        [Test]
        public void AnInstalledAdapterIsAsked()
        {
            var fake = new Fake { Proc = NetRoleKind.ClientView, Obj = NetRoleKind.ClientView, Owner = true, Remote = true, Id = 7 };
            NetRole.Install(fake);
            Component c = host.GetComponent<BoxCollider>();
            Assert.IsTrue(NetRole.IsClientView);
            Assert.IsFalse(NetRole.IsServerAuthority);
            Assert.IsFalse(NetRole.MayMutate(c));
            Assert.IsTrue(NetRole.IsNetworked(c));
            Assert.IsTrue(NetRole.IsLocalOwner(c));
            Assert.IsTrue(NetRole.IsRemoteOwned(c));
            Assert.AreEqual(7UL, NetRole.OwnerId(c));
            NetRole.DisconnectOwner(c, "bye");
            Assert.AreEqual("bye", fake.Dropped);

            fake.Obj = NetRoleKind.ServerBody;
            fake.Proc = NetRoleKind.ServerBody;
            Assert.IsTrue(NetRole.MayMutate(c));
            Assert.IsTrue(NetRole.IsServerBody(c));
            Assert.IsTrue(NetRole.IsServerAuthority);
        }

        [Test]
        public void AnUnspawnedBodyIsLocalOnlyWhenNoSessionListens()
        {
            var fake = new Fake { Proc = NetRoleKind.ClientView, Obj = NetRoleKind.Offline };
            NetRole.Install(fake);
            Component c = host.GetComponent<BoxCollider>();
            Assert.IsFalse(NetRole.IsLocalOwner(c));
            fake.Proc = NetRoleKind.Offline;
            Assert.IsTrue(NetRole.IsLocalOwner(c));
        }

        [Test]
        public void TheStartupResetRemovesTheAdapter()
        {
            NetRole.Install(new Fake { Proc = NetRoleKind.ServerBody });
            typeof(NetRole).GetMethod("ResetStatics", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.IsNull(NetRole.Adapter);
            Assert.IsTrue(NetRole.IsOffline);
        }

        [Test]
        public void TheNetcodeAdapterIsOfflineWhenNoSessionListens()
        {
            var ngo = new NgoNetRoleAdapter();
            NetRole.Install(ngo);
            Component c = host.GetComponent<BoxCollider>();
            if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening)
                Assert.Inconclusive("a session is listening in this Editor");
            Assert.AreEqual(NetRoleKind.Offline, NetRole.Process);
            Assert.AreEqual(NetRoleKind.Offline, NetRole.Of(c), "a plain object has no network identity");
            Assert.IsFalse(NetRole.IsOwner(c));
            Assert.IsFalse(NetRole.IsRemoteOwned(c));
        }
    }
}
