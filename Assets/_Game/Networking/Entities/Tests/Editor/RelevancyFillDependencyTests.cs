using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// A stand-in for the game's teleport job (and the brains' movers): every tick it SCHEDULES a job that writes LocalTransform and
    /// leaves it running, as PlayerTeleportSystem does. Only in the tests that add it.
    /// </summary>
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MovesTransformsInAJobTestSystem : ISystem
    {
        public void OnUpdate(ref SystemState state) => new MoveJob().Schedule();

        [BurstCompile]
        partial struct MoveJob : IJobEntity
        {
            void Execute(ref LocalTransform transform) => transform.Position += new float3(0f, 0f, 0.001f);
        }
    }

    /// <summary>
    /// The fill beside a job that still writes LocalTransform (found by the lead's first run, 12 Nov 2026): the gather is an
    /// IJobEntity.Run(), which does not wait for the jobs the system depends on, so with relevancy on the fill threw every tick
    /// ("The previously scheduled job PlayerTeleportSystem:TeleportJob writes to ... LocalTransform") and the set was never filled.
    /// </summary>
    public class RelevancyFillDependencyTests
    {
        [Test]
        public void TheFill_WaitsForAJobThatStillWritesTransforms_AndFillsTheSet()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(1);
            World server = tw.Server;
            EntityManager em = server.EntityManager;

            // a transform writer scheduled every tick, earlier in the same group, never completed by its own system
            SystemHandle mover = server.CreateSystem<MovesTransformsInAJobTestSystem>();
            server.GetExistingSystemManaged<SimulationSystemGroup>().AddSystemToUpdateList(mover);
            Entity moved = em.CreateEntity(typeof(LocalTransform));
            em.SetComponentData(moved, LocalTransform.FromPosition(new float3(1f, 2f, 3f)));

            var settings = ScriptableObject.CreateInstance<RelevancySettings>();
            Assert.IsTrue(RelevancySetup.Install(server, settings, serverSwitch: true));
            tw.Connect(goInGame: true);
            tw.Tick(8);   // more than one fill interval, the mover's job pending at every fill

            foreach (string line in tw.Interesting)
            {
                StringAssert.DoesNotContain("previously scheduled job", line);
                StringAssert.DoesNotContain("InvalidOperationException", line);
            }
            Assert.Greater(em.GetComponentData<LocalTransform>(moved).Position.z, 3f, "the mover's job really ran");

            // and the fill did its work: a named ghost is in the set in that tick
            GhostRelevancy relevancy = KnTestWorld.Singleton<GhostRelevancy>(server);
            Assert.AreEqual(GhostRelevancyMode.SetIsRelevant, relevancy.GhostRelevancyMode);
            using EntityQuery lists = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadWrite<AlwaysRelevantGhost>());
            Assert.AreEqual(1, lists.CalculateEntityCount(), "the connection carries its list: the fill ran past the gather");
            using var connections = lists.ToEntityArray(Allocator.Temp);
            using var ids = lists.ToComponentDataArray<NetworkId>(Allocator.Temp);
            // reason 200 is the test's own: reason 1 is the Callout's, whose server system takes it off again before the fill
            AlwaysRelevantGhosts.SetOnly(em.GetBuffer<AlwaysRelevantGhost>(connections[0]), 200, 77);
            tw.Tick(1);
            Assert.IsTrue(relevancy.GhostRelevancySet.ContainsKey(new RelevantGhostForConnection(ids[0].Value, 77)));
            foreach (string line in tw.Interesting) StringAssert.DoesNotContain("previously scheduled job", line);
        }
    }
}
