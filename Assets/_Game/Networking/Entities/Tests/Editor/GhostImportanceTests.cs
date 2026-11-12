using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>Wave 4 relevancy, importance: the package's distance scaling is installed from the settings, and each connection's place follows the ghost it owns.</summary>
    public class GhostImportanceTests
    {
        static RelevancySettings Settings(bool enabled)
        {
            var s = ScriptableObject.CreateInstance<RelevancySettings>();
            s.enabled = enabled;
            return s;
        }

        [Test]
        public void SwitchedOff_InstallsNothing()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(0);
            Assert.IsFalse(RelevancySetup.Install(tw.Server, Settings(false)));
            Assert.AreEqual(0, KnTestWorld.Count<GhostImportance>(tw.Server), "off leaves the world as it was before relevancy");
            Assert.AreEqual(0, KnTestWorld.Count<GhostDistanceData>(tw.Server));
        }

        [Test]
        public void SwitchedOn_InstallsTheDistanceScaling_WithTheTileOfTheSettings_Once()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(0);
            RelevancySettings s = Settings(true);
            s.importanceTileSize = 32;
            Assert.IsTrue(RelevancySetup.Install(tw.Server, s));
            RelevancySetup.InstallImportance(tw.Server, s);   // a second call adds nothing
            Assert.AreEqual(1, KnTestWorld.Count<GhostImportance>(tw.Server));
            Assert.AreEqual(new int3(32, 32, 32), KnTestWorld.Singleton<GhostDistanceData>(tw.Server).TileSize);
            GhostImportance importance = KnTestWorld.Singleton<GhostImportance>(tw.Server);
            Assert.AreEqual(ComponentType.ReadOnly<GhostConnectionPosition>(), importance.GhostConnectionComponentType);
            Assert.AreEqual(ComponentType.ReadOnly<GhostDistancePartitionShared>(), importance.GhostImportancePerChunkDataType);
            tw.Tick(3);   // the partitioning and send systems run with it and nothing throws
            Assert.IsFalse(string.Join("\n", tw.Interesting).Contains("Exception"), string.Join("\n", tw.Interesting));
        }

        [Test]
        public void AConnectionsPlace_FollowsTheGhostItOwns_AndStaysWithoutABody()
        {
            using var tw = new KnTestWorld();
            tw.CreateWorlds(2);
            RelevancySetup.Install(tw.Server, Settings(true));
            tw.Connect(goInGame: true);
            int first = KnTestWorld.NetworkIdOf(tw.Clients[0]);
            EntityManager em = tw.Server.EntityManager;

            // a stand-in for the first player's body: owned by its connection (the place needs no real ghost)
            Entity body = em.CreateEntity(typeof(GhostOwner), typeof(LocalTransform));
            em.SetComponentData(body, new GhostOwner { NetworkId = first });
            em.SetComponentData(body, LocalTransform.FromPosition(new float3(120f, 4f, -300f)));
            tw.Tick(2);

            int seen = 0;
            using EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<GhostConnectionPosition>());
            using var ids = q.ToComponentDataArray<NetworkId>(Allocator.Temp);
            using var places = q.ToComponentDataArray<GhostConnectionPosition>(Allocator.Temp);
            Assert.AreEqual(2, ids.Length, "every in-game connection gets a place");
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i].Value == first) { Assert.AreEqual(new float3(120f, 4f, -300f), places[i].Position); seen++; }
                else Assert.AreEqual(float3.zero, places[i].Position, "no body yet: the place stays where it was");
            }
            Assert.AreEqual(1, seen);
        }
    }
}
