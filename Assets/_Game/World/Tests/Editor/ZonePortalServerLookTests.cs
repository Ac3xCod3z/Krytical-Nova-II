using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.World.Portals;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// M7.3 the zone portal on a server of the entity netcode: a server body that walks inside the radius (3 m + 0.75) is told the portal's
    /// line, only that player, once per entry, not again within the 3 s cooldown, and a body outside or with no player key never is.
    /// </summary>
    public class ZonePortalServerLookTests
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

        sealed class Told : IOwnerMessages
        {
            public readonly List<(string body, string text)> Lines = new List<(string, string)>();
            public bool Tell(Component part, string text) { Lines.Add((part.name, text)); return true; }
        }

        readonly List<GameObject> m_Made = new List<GameObject>();
        INetRoleAdapter m_OldRole;
        IOwnerMessages m_OldMessages;
        ZonePortal m_Portal;
        Told m_Told;

        [SetUp]
        public void SetUp()
        {
            m_OldRole = NetRole.Adapter;
            m_OldMessages = OwnerMessages.Current;
            NetRole.Install(new ServerRole());
            OwnerMessages.Current = m_Told = new Told();
            var go = Make("Portal", Vector3.zero);
            go.AddComponent<BoxCollider>().isTrigger = true;
            m_Portal = go.AddComponent<ZonePortal>();
        }

        [TearDown]
        public void TearDown()
        {
            NetRole.Install(m_OldRole);
            OwnerMessages.Current = m_OldMessages;
            foreach (var go in m_Made) Object.DestroyImmediate(go);
            m_Made.Clear();
        }

        GameObject Make(string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.position = at;
            m_Made.Add(go);
            return go;
        }

        Body BodyAt(string name, Vector3 at, ulong key) => new Body { Transform = Make(name, at).transform, Owner = new PlayerKey(key) };

        [Test]
        public void ABodyThatWalksIn_IsToldOnce_PerEntry_AfterTheCooldown_AndOnlyThatPlayer()
        {
            var a = BodyAt("A", new Vector3(10, 0, 0), 1);
            var b = BodyAt("B", new Vector3(0, 0, 20), 2);
            var keyless = BodyAt("NoKey", new Vector3(1, 0, 0), 0);
            var bodies = new List<IPlayerBody> { a, b, keyless };

            m_Portal.ServerLook(bodies, 0f);
            Assert.AreEqual(0, m_Told.Lines.Count, "outside the radius, or no player key: nobody is told");

            a.Transform.position = new Vector3(3.6f, 5f, 0);      // 3.6 m flat (inside 3 + 0.75), height does not count
            m_Portal.ServerLook(bodies, 1f);
            Assert.AreEqual(1, m_Told.Lines.Count, "A walked in");
            Assert.AreEqual("A", m_Told.Lines[0].body, "only the player who walked in is told");
            Assert.AreEqual(m_Portal.SealedMessage, m_Told.Lines[0].text, "the portal's own line");

            m_Portal.ServerLook(bodies, 1.25f);
            m_Portal.ServerLook(bodies, 10f);
            Assert.AreEqual(1, m_Told.Lines.Count, "standing inside is one entry");

            a.Transform.position = new Vector3(10, 0, 0);          // out and back in within the cooldown
            m_Portal.ServerLook(bodies, 10.25f);
            a.Transform.position = Vector3.zero;
            m_Portal.ServerLook(bodies, 10.5f);
            Assert.AreEqual(2, m_Told.Lines.Count, "a new entry after the 3 s cooldown (first use at 1 s)");
            a.Transform.position = new Vector3(10, 0, 0);
            m_Portal.ServerLook(bodies, 10.75f);
            a.Transform.position = Vector3.zero;
            m_Portal.ServerLook(bodies, 11f);
            Assert.AreEqual(2, m_Told.Lines.Count, "out and in again within 3 s: not told again");

            b.Transform.position = new Vector3(0, 0, 1);
            m_Portal.ServerLook(bodies, 11.25f);
            Assert.AreEqual(3, m_Told.Lines.Count, "B's cooldown is its own");
            Assert.AreEqual("B", m_Told.Lines[2].body);
        }
    }
}
