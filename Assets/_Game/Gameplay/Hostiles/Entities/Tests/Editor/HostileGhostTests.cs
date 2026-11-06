using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M5.5a: the Hostile ghost's fields (NGO's HostileAnimState and attack / flinch RPCs), written only when they change.</summary>
    public sealed class HostileGhostTests
    {
        static (HostileTestWorld w, SystemHandle write) World()
        {
            var w = new HostileTestWorld();
            return (w, w.World.CreateSystem<HostileGhostWriteSystem>());
        }

        static Entity Ghosted(HostileTestWorld w, float3 at, byte behaviour)
        {
            Entity h = w.Hostile(at, behaviour);
            w.Em.AddComponentData(h, new HostileGhost());
            w.Em.AddComponentData(h, new HostileGhostSource { LastPosition = at });
            HostileLife life = w.Em.GetComponentData<HostileLife>(h);
            life.Species = 7; life.Level = 4; life.Tier = 1;
            w.Em.SetComponentData(h, life);
            return h;
        }

        static void Step(HostileTestWorld w, SystemHandle write, int ticks = 1)
        {
            for (int i = 0; i < ticks; i++)
            {
                w.Step();
                write.Update(w.World.Unmanaged);
            }
        }

        [Test]
        public void FieldsWrittenOnlyOnChange()
        {
            var (w, write) = World();
            using (w)
            {
                Entity h = Ghosted(w, float3.zero, HostileBehaviourKind.FightsBack);
                Step(w, write, 2);
                HostileGhost g = w.Em.GetComponentData<HostileGhost>(h);
                Assert.AreEqual(7, g.Species); Assert.AreEqual(4, g.Level); Assert.AreEqual(1, g.Tier);

                uint version = w.World.EntityManager.GlobalSystemVersion;
                for (int t = 0; t < 30; t++)
                {
                    Step(w, write);
                    var handle = w.Em.GetComponentTypeHandle<HostileGhost>(true);
                    Assert.IsFalse(w.Em.GetChunk(h).DidChange(ref handle, version), $"idle tick {t}: the ghost was written");
                }
            }
        }

        [Test]
        public void AHit_CountsAFlinch_AndEngagesTheGhost()
        {
            var (w, write) = World();
            using (w)
            {
                Entity h = Ghosted(w, float3.zero, HostileBehaviourKind.FightsBack);
                w.SetPlayers((new float3(1.5f, 0f, 0f), SensedFlags.Alive));
                Step(w, write, 2);
                w.Hit(h, 100, 10f);
                Step(w, write);
                HostileGhost g = w.Em.GetComponentData<HostileGhost>(h);
                Assert.AreEqual(1, g.HitCounter);
                Assert.AreEqual(HostileMode.Engage, g.Mode);
                Assert.AreNotEqual(0, g.Flags & HostileGhostFlags.InCombat);
            }
        }

        [Test]
        public void TheSpeed_IsTheDistanceMovedThisTick_InNgosSteps()
        {
            var (w, write) = World();
            using (w)
            {
                Entity h = Ghosted(w, float3.zero, HostileBehaviourKind.FightsBack);
                Step(w, write);
                // 0.1 m in one 30 Hz tick = 3 m/s = 60 steps of 0.05 m/s
                w.Em.SetComponentData(h, LocalTransform.FromPosition(new float3(0.1f, 0f, 0f)));
                Step(w, write);
                Assert.AreEqual(60, w.Em.GetComponentData<HostileGhost>(h).SpeedSteps);
                Assert.AreEqual(255, HostileGhostRules.SpeedSteps(40f), "clamped");
            }
        }
    }
}
