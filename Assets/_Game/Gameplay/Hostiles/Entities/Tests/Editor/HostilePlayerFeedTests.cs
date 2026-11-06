using KryticalNova.Brains;
using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M5.3b2a: the player ghosts reach the Brain's sense world as the Hostiles mind needs them.</summary>
    public sealed class HostilePlayerFeedTests
    {
        [Test]
        public void EveryPlayerGhost_IsSensed_WithItsGhostIdFeetLifeAndWall()
        {
            using var world = new World("HostilePlayerFeedTests");
            EntityManager em = world.EntityManager;
            var brain = ScriptableObject.CreateInstance<BrainSettings>();
            var hostile = ScriptableObject.CreateInstance<HostileBrainSettings>();
            em.CreateSingleton(BrainConfig.From(brain, 30));
            em.CreateSingleton(HostileBrainConfig.From(hostile, 30));
            Object.DestroyImmediate(brain);
            Object.DestroyImmediate(hostile);
            em.CreateSingleton(new BrainTick { Tick = 1 });
            SystemHandle sense = world.CreateSystem<SenseBuildSystem>();
            SystemHandle feed = world.CreateSystem<HostilePlayerFeedSystem>();

            Entity Player(int ghostId, int3 mm, int health, int maxHealth, byte wall)
            {
                Entity e = em.CreateEntity(typeof(PlayerGhostTag), typeof(GhostInstance), typeof(PlayerPose), typeof(PlayerVitals), typeof(MoveState));
                em.SetComponentData(e, new GhostInstance { ghostId = ghostId });
                em.SetComponentData(e, new PlayerPose { XMm = mm.x, YMm = mm.y, ZMm = mm.z });
                em.SetComponentData(e, new PlayerVitals { HealthMilli = health, MaxHealthMilli = maxHealth });
                em.SetComponentData(e, new MoveState { Wall = wall });
                return e;
            }
            Entity walker = Player(7, new int3(1500, 250, -3000), 0, 0, 0);          // no Max HP yet: alive
            Entity climber = Player(8, new int3(0, 4000, 9000), 500, 1000, 1);
            Player(9, new int3(20000, 0, 0), 0, 1000, 0);                            // dead

            feed.Update(world.Unmanaged);
            sense.Update(world.Unmanaged);
            SenseWorld s = em.CreateEntityQuery(typeof(SenseWorld)).GetSingleton<SenseWorld>();
            Assert.AreEqual(3, s.Players.Length);
            Assert.IsTrue(s.ByGhostId.TryGetValue(7, out int i7));
            Assert.AreEqual(walker, s.Players[i7].Ghost);
            Assert.Less(math.distance(new float3(1.5f, 0.25f, -3f), s.Players[i7].Feet), 1e-5f, "feet in metres from the pose's millimetres");
            Assert.AreEqual(SensedFlags.Alive, s.Players[i7].Flags);
            Assert.IsTrue(s.ByGhostId.TryGetValue(8, out int i8));
            Assert.AreEqual(climber, s.Players[i8].Ghost);
            Assert.AreEqual(SensedFlags.Alive | SensedFlags.OnWall, s.Players[i8].Flags);
            Assert.IsTrue(s.ByGhostId.TryGetValue(9, out int i9));
            Assert.AreEqual(SensedFlags.Dead, s.Players[i9].Flags);
            Assert.AreEqual(float.MaxValue, SenseQueries.NearestSq(s, new float3(20f, 0f, 0f), 1f, em.CreateEntityQuery(typeof(BrainConfig)).GetSingleton<BrainConfig>().PlayerCell),
                "a dead player is nobody's nearest");

            feed.Update(world.Unmanaged);
            Assert.AreEqual(3, s.Players.Length, "rebuilt each tick, never appended");
        }
    }
}
