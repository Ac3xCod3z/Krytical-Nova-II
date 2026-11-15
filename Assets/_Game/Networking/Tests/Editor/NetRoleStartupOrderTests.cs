using KryticalNova.Core;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// Perf round 3 chunk 0 (15 Nov 2026): on a built dedicated entity server the entity netcode made its worlds and installed its NetRole
    /// adapter and session in Entities' BeforeSceneLoad bootstrap, and NGO's own BeforeSceneLoad install then replaced them (the order of
    /// the two is not fixed), so the server answered Offline: gm.target said "No network session is running" and no bot went on
    /// Autopilot. NGO's startup install now only fills an empty slot. Both orders end with the first netcode in front.
    /// </summary>
    public class NetRoleStartupOrderTests
    {
        sealed class OtherNetcode : INetRoleAdapter
        {
            public NetRoleKind Process => NetRoleKind.ServerBody;
            public NetRoleKind Of(Component c) => NetRoleKind.ServerBody;
            public bool IsOwner(Component c) => false;
            public bool IsOwnedByServer(Component c) => true;
            public bool IsRemoteOwned(Component c) => false;
            public ulong OwnerId(Component c) => 0;
            public void DisconnectOwner(Component c, string reason) { }
        }

        sealed class OtherSession : INetSessionSource
        {
            public bool IsRunning => true;
            public bool IsServer => true;
            public bool IsClient => false;
            public bool IsHost => false;
            public bool IsConnectedClient => false;
            public bool IsShuttingDown => false;
            public int PlayerCount => 3;
            public int NetworkedObjectCount => -1;
            public int TickRate => 30;
            public bool TryGetRttMs(out long milliseconds) { milliseconds = 0; return false; }
        }

        INetRoleAdapter m_Adapter;
        INetSessionSource m_Source;

        [SetUp]
        public void SetUp()
        {
            m_Adapter = NetRole.Adapter;
            m_Source = NetSession.Source;
            NetRole.Install(null);
            NetSession.Use(null);
        }

        [TearDown]
        public void TearDown()
        {
            NetRole.Install(m_Adapter);
            NetSession.Use(m_Source);
        }

        [Test]
        public void TheEntityServerFirst_NgoStartupLeavesItInFront()
        {
            var entity = new OtherNetcode();
            var session = new OtherSession();
            NetRole.Install(entity);
            NetSession.Use(session);
            NgoNetRoleAdapter.InstallIfEmpty();
            NgoNetSession.RegisterIfEmpty();
            Assert.AreSame(entity, NetRole.Adapter, "the entity netcode's adapter stays");
            Assert.AreEqual(NetRoleKind.ServerBody, NetRole.Process, "the server answers ServerBody, not NGO's Offline");
            Assert.AreSame(session, NetSession.Source, "the entity netcode's session stays");
        }

        [Test]
        public void NgoFirst_IsInstalledAsBefore()
        {
            NgoNetRoleAdapter.InstallIfEmpty();
            NgoNetSession.RegisterIfEmpty();
            Assert.IsInstanceOf<NgoNetRoleAdapter>(NetRole.Adapter);
            Assert.AreSame(NgoNetSession.Instance, NetSession.Source);
            Assert.AreEqual(NetRoleKind.Offline, NetRole.Process, "no NGO session listening: offline, as before");
        }
    }
}
