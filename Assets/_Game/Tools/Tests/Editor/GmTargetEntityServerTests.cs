using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.DevTools;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// M7.4 GM commands on a server of the entity netcode: the player a command acts on is the server body of the connection the GM picked
    /// (their own for their lines), found through the player keys; with no pick a server has no player of its own, and gm.players lists the
    /// server bodies by connection id.
    /// </summary>
    public class GmTargetEntityServerTests
    {
        sealed class Body : IPlayerBody
        {
            public Transform Transform { get; set; }
            public PlayerKey Owner { get; set; }
            public bool IsLocal => false;
            public bool IsJumping => false;
            public void RebindToScene() { }
        }

        sealed class ServerRole : INetRoleAdapter
        {
            public NetRoleKind Process => NetRoleKind.ServerBody;
            public NetRoleKind Of(Component networked) => NetRoleKind.ServerBody;
            public bool IsOwner(Component networked) => false;
            public bool IsOwnedByServer(Component networked) => false;
            public bool IsRemoteOwned(Component networked) => true;
            public ulong OwnerId(Component networked) => 0;
            public void DisconnectOwner(Component networked, string reason) { }
        }

        readonly List<GameObject> m_Made = new List<GameObject>();
        INetRoleAdapter m_OldRole;
        IReadOnlyList<IPlayerBody> m_OldBodies;
        GmTarget.Saved m_OldTarget;

        [SetUp]
        public void SetUp()
        {
            m_OldRole = NetRole.Adapter;
            m_OldBodies = PlayerBodies.All;
            m_OldTarget = GmTarget.Save();
            PlayerKeys.Reset();
            NetRole.Install(new ServerRole());
            GmTarget.TargetSelf();
        }

        [TearDown]
        public void TearDown()
        {
            NetRole.Install(m_OldRole);
            PlayerBodies.Use(m_OldBodies);
            GmTarget.Restore(m_OldTarget);
            PlayerKeys.Reset();
            foreach (var go in m_Made) Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        Body Make(string name, ulong connection)
        {
            var go = new GameObject(name);
            m_Made.Add(go);
            return new Body { Transform = go.transform, Owner = PlayerKeys.Mint(connection) };
        }

        [Test]
        public void ACommand_ActsOnThePickedConnectionsServerBody_AndNeverOnSomeoneElse()
        {
            Body ace = Make("Ace", 1), bea = Make("Bea", 2);
            PlayerBodies.Use(new List<IPlayerBody> { ace, bea });

            Assert.IsFalse(GmTarget.TryGetPlayer(out _, out string error), "no pick: a server has no player of its own");
            StringAssert.Contains("gm.target", error);

            Assert.IsTrue(GmTarget.TrySetTarget(2, out error), error);
            Assert.AreEqual(2UL, GmTarget.ClientId);
            Assert.IsTrue(GmTarget.TryGetPlayer(out GameObject player, out error), error);
            Assert.AreSame(bea.Transform.gameObject, player, "connection 2 is Bea's server body");

            Assert.IsFalse(GmTarget.TrySetTarget(9, out error), "nobody on connection 9");
            Assert.IsTrue(GmTarget.TryGetPlayer(out player, out _));
            Assert.AreSame(bea.Transform.gameObject, player, "a failed pick keeps the last one");

            var listed = GmTarget.ConnectedPlayers();
            Assert.AreEqual(2, listed.Count);
            CollectionAssert.AreEquivalent(new[] { 1UL, 2UL }, new[] { listed[0].ClientId, listed[1].ClientId });

            PlayerKeys.Release(2);   // Bea left
            Assert.IsFalse(GmTarget.TryGetPlayer(out _, out _), "the picked player left: no one, not someone else");
        }
    }
}
