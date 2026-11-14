using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation.Tests
{
    /// <summary>
    /// The stand-in targets a Hostile may strike that are not players (day list item 19 phase 4: the TANK companion's hidden body): found by
    /// world and ghost, replaced, unregistered, and dropped when the body or the world is gone.
    /// </summary>
    public class StandInTargetsTests
    {
        World m_World;
        GameObject m_Body;

        [SetUp] public void SetUp() { StandInTargets.Clear(); m_World = new World("stand-in-test"); m_Body = new GameObject("stand-in"); }

        [TearDown]
        public void TearDown()
        {
            StandInTargets.Clear();
            if (m_World != null && m_World.IsCreated) m_World.Dispose();
            if (m_Body != null) Object.DestroyImmediate(m_Body);
        }

        [Test]
        public void Registered_IsFoundByWorldAndGhost_Only()
        {
            Entity ghost = m_World.EntityManager.CreateEntity(), other = m_World.EntityManager.CreateEntity();
            StandInTargets.Register(m_World, ghost, m_Body.transform);
            Assert.IsTrue(StandInTargets.TryGet(m_World, ghost, out Component body));
            Assert.AreSame(m_Body.transform, body);
            Assert.IsFalse(StandInTargets.TryGet(m_World, other, out _), "another ghost");
            using var second = new World("stand-in-other");
            Assert.IsFalse(StandInTargets.TryGet(second, ghost, out _), "the same entity in another world");
            StandInTargets.Register(m_World, ghost, m_Body.transform);
            Assert.AreEqual(1, StandInTargets.Count, "a second registration replaces the first");
            StandInTargets.Unregister(m_World, ghost);
            Assert.IsFalse(StandInTargets.TryGet(m_World, ghost, out _));
        }

        [Test]
        public void ADestroyedBody_OrADisposedWorld_IsDropped()
        {
            Entity ghost = m_World.EntityManager.CreateEntity();
            StandInTargets.Register(m_World, ghost, m_Body.transform);
            Object.DestroyImmediate(m_Body);
            Assert.IsFalse(StandInTargets.TryGet(m_World, ghost, out _), "the body is gone");
            Assert.AreEqual(0, StandInTargets.Count);

            m_Body = new GameObject("stand-in 2");
            StandInTargets.Register(m_World, ghost, m_Body.transform);
            m_World.Dispose();
            Assert.AreEqual(0, StandInTargets.Count, "the world is gone");
        }
    }
}
