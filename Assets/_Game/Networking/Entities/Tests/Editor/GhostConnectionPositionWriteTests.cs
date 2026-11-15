using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// Perf clean-ups round 2, chunk 8: <see cref="GhostConnectionPositionSystem"/> writes a connection's place only when its body moved.
    /// A standing player's connection chunk keeps its change version; a moving one gets the body's place, as before.
    /// </summary>
    public sealed class GhostConnectionPositionWriteTests
    {
        World m_World;
        EntityManager m_Em;
        SystemHandle m_System;
        Entity m_Connection, m_Body;

        [SetUp]
        public void SetUp()
        {
            m_World = new World("chunk 8 connection place");
            m_Em = m_World.EntityManager;
            m_Em.CreateEntity(typeof(GhostImportance));
            m_Connection = m_Em.CreateEntity(typeof(NetworkId), typeof(NetworkStreamInGame), typeof(GhostConnectionPosition));
            m_Em.SetComponentData(m_Connection, new NetworkId { Value = 3 });
            m_Body = m_Em.CreateEntity(typeof(GhostOwner), typeof(LocalTransform));
            m_Em.SetComponentData(m_Body, new GhostOwner { NetworkId = 3 });
            m_Em.SetComponentData(m_Body, LocalTransform.FromPosition(new float3(4f, 1f, -2f)));
            m_System = m_World.CreateSystem<GhostConnectionPositionSystem>();
        }

        [TearDown]
        public void TearDown() => m_World.Dispose();

        uint Version()
        {
            var handle = m_Em.GetComponentTypeHandle<GhostConnectionPosition>(true);
            return m_Em.GetChunk(m_Connection).GetChangeVersion(ref handle);
        }

        void Tick() { m_System.Update(m_World.Unmanaged); m_Em.CompleteAllTrackedJobs(); }

        [Test]
        public void AStandingPlayer_KeepsItsChunkVersion_AMovingOneGetsItsPlace()
        {
            Tick();
            Assert.AreEqual(new float3(4f, 1f, -2f), m_Em.GetComponentData<GhostConnectionPosition>(m_Connection).Position, "first tick: the body's place");
            uint settled = Version();
            for (int i = 0; i < 5; i++) Tick();
            Assert.AreEqual(settled, Version(), "standing: no write, the chunk's change version stays");

            m_Em.SetComponentData(m_Body, LocalTransform.FromPosition(new float3(4.5f, 1f, -2f)));
            Tick();
            Assert.AreEqual(new float3(4.5f, 1f, -2f), m_Em.GetComponentData<GhostConnectionPosition>(m_Connection).Position, "moved: the new place");
            Assert.AreNotEqual(settled, Version(), "moved: written");
        }

        [Test]
        public void PlaceMoved_IsBitForBit()
        {
            Assert.IsFalse(GhostConnectionPositionSystem.PlaceMoved(new float3(1f, 2f, 3f), new float3(1f, 2f, 3f)));
            Assert.IsTrue(GhostConnectionPositionSystem.PlaceMoved(new float3(1f, 2f, 3f), new float3(1f, 2f, 3.0001f)));
            Assert.IsTrue(GhostConnectionPositionSystem.PlaceMoved(new float3(0f), new float3(-0f, 0f, 0f)), "-0 is written, as the old write did");
        }
    }
}
