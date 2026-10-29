using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>The ghost-to-body registry on its own: no netcode, a plain world and plain GameObjects.</summary>
    public class GhostBodyRegistryTests
    {
        World m_World;
        readonly System.Collections.Generic.List<GameObject> m_Made = new System.Collections.Generic.List<GameObject>();

        [SetUp] public void SetUp() { GhostBodyRegistry.Clear(); LocalPlayer.Set(null); m_World = new World("registry-test"); }

        [TearDown]
        public void TearDown()
        {
            GhostBodyRegistry.Clear();
            LocalPlayer.Set(null);
            if (m_World != null && m_World.IsCreated) m_World.Dispose();
            foreach (var g in m_Made) if (g != null) Object.DestroyImmediate(g);
            m_Made.Clear();
        }

        GameObject Body(string name = "body") { var g = new GameObject(name); m_Made.Add(g); return g; }

        Entity Ghost() => m_World.EntityManager.CreateEntity();

        [Test]
        public void Register_ThenLookups_FindTheBody_ByGhost_NetworkId_AndKey()
        {
            var ghost = Ghost(); var body = Body(); var key = new PlayerKey(7);
            var entry = GhostBodyRegistry.Register(m_World, ghost, 3, key, body, false);
            Assert.AreEqual(1, GhostBodyRegistry.Count);
            Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(m_World, ghost, out var a)); Assert.AreSame(entry, a);
            Assert.IsTrue(GhostBodyRegistry.TryGetByNetworkId(m_World, 3, out var b)); Assert.AreSame(entry, b);
            Assert.IsTrue(GhostBodyRegistry.TryGetByKey(key, out var c)); Assert.AreSame(entry, c);
            Assert.IsFalse(GhostBodyRegistry.TryGetByKey(PlayerKey.None, out _), "a client's None key never matches");
            Assert.AreSame(body.transform, entry.View.Transform);
            Assert.AreEqual(key, entry.View.Owner);
        }

        [Test]
        public void Unregister_RemovesTheEntry_AndTheView()
        {
            var ghost = Ghost();
            GhostBodyRegistry.Register(m_World, ghost, 1, PlayerKey.None, Body(), false);
            Assert.AreEqual(1, PlayerBodies.All.Count);
            Assert.IsTrue(GhostBodyRegistry.Unregister(m_World, ghost, out var entry));
            Assert.IsNotNull(entry);
            Assert.AreEqual(0, GhostBodyRegistry.Count);
            Assert.AreEqual(0, PlayerBodies.All.Count);
            Assert.IsFalse(GhostBodyRegistry.Unregister(m_World, ghost, out _));
        }

        [Test]
        public void ADestroyedBody_IsNeverHandedOut()
        {
            var ghost = Ghost(); var body = Body();
            GhostBodyRegistry.Register(m_World, ghost, 1, PlayerKey.None, body, true);
            Assert.AreSame(body, LocalPlayer.Object);
            Object.DestroyImmediate(body);
            Assert.IsFalse(GhostBodyRegistry.TryGetByGhost(m_World, ghost, out _));
            Assert.AreEqual(0, GhostBodyRegistry.Count);
            Assert.AreEqual(0, PlayerBodies.All.Count);
        }

        [Test]
        public void ADisposedWorld_LeavesNoEntries()
        {
            GhostBodyRegistry.Register(m_World, Ghost(), 1, PlayerKey.None, Body(), false);
            GhostBodyRegistry.Register(m_World, Ghost(), 2, PlayerKey.None, Body(), false);
            m_World.Dispose();
            Assert.AreEqual(0, GhostBodyRegistry.Count);
            Assert.AreEqual(0, PlayerBodies.All.Count);
        }

        [Test]
        public void TakeWorld_ReturnsOnlyThatWorldsEntries()
        {
            using var other = new World("registry-other");
            GhostBodyRegistry.Register(m_World, Ghost(), 1, PlayerKey.None, Body(), false);
            GhostBodyRegistry.Register(other, other.EntityManager.CreateEntity(), 2, PlayerKey.None, Body(), false);
            var taken = GhostBodyRegistry.TakeWorld(m_World);
            Assert.AreEqual(1, taken.Count);
            Assert.AreEqual(1, GhostBodyRegistry.Count);
            GhostBodyRegistry.TakeWorld(other);
        }

        [Test]
        public void TheLocalBody_GoesToLocalPlayer_AndLeavesItWhenRemoved()
        {
            var ghost = Ghost(); var body = Body();
            GhostBodyRegistry.Register(m_World, ghost, 1, PlayerKey.None, body, true);
            Assert.AreSame(body, LocalPlayer.Object);
            Assert.IsTrue(GhostBodyRegistry.TryGetByGhost(m_World, ghost, out var entry));
            Assert.IsTrue(entry.View.IsLocal);
            GhostBodyRegistry.Unregister(m_World, ghost, out _);
            Assert.IsNull(LocalPlayer.Object);
        }

        [Test]
        public void ARemoteBody_NeverTouchesLocalPlayer()
        {
            var mine = Body("mine");
            GhostBodyRegistry.Register(m_World, Ghost(), 1, PlayerKey.None, mine, true);
            GhostBodyRegistry.Register(m_World, Ghost(), 2, PlayerKey.None, Body("theirs"), false);
            Assert.AreSame(mine, LocalPlayer.Object);
            Assert.AreEqual(2, PlayerBodies.All.Count);
        }

        [Test]
        public void ServerBodies_AreKeptByKey_AndDeadOnesDropped()
        {
            var key = new PlayerKey(5); var sb = Body("server body");
            GhostBodyRegistry.SetServerBody(key, sb);
            Assert.IsTrue(GhostBodyRegistry.TryGetServerBody(key, out var got)); Assert.AreSame(sb, got);
            Object.DestroyImmediate(sb);
            Assert.IsFalse(GhostBodyRegistry.TryGetServerBody(key, out _));
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            GhostBodyRegistry.SetServerBody(PlayerKey.None, Body());
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount, "a body without a key is not kept");
        }

        [Test]
        public void Clear_ResetsEveryStatic()
        {
            GhostBodyRegistry.Register(m_World, Ghost(), 1, PlayerKey.None, Body(), true);
            GhostBodyRegistry.SetServerBody(new PlayerKey(9), Body());
            GhostBodyRegistry.Clear();   // what the SubsystemRegistration hook calls
            Assert.AreEqual(0, GhostBodyRegistry.Count);
            Assert.AreEqual(0, GhostBodyRegistry.ServerBodyCount);
            Assert.AreEqual(0, PlayerBodies.All.Count);
            Assert.IsNull(LocalPlayer.Object);
        }

        [Test]
        public void ClickCommandSource_ConvertsMetresToMillimetres_AndRunAndStop()
        {
            var fake = new FakeClickSource();
            var source = new ClickCommandSource(fake);
            Assert.IsFalse(source.TryTake(out _));
            fake.Click(new Vector3(1.2346f, 0f, -2f), run: true);
            fake.Stop();
            Assert.IsTrue(source.TryTake(out var c));
            Assert.AreEqual(PlayerCommandKind.Click, c.Kind);
            Assert.IsTrue(c.Run);
            Assert.AreEqual(1235, c.GoalMm.x);
            Assert.AreEqual(-2000, c.GoalMm.z);
            Assert.IsTrue(source.TryTake(out var s));
            Assert.AreEqual(PlayerCommandKind.Stop, s.Kind);
        }
    }
}
