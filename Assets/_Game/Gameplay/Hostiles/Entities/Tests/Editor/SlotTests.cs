using System.Collections.Generic;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M6.5 the stage manager (brain plan s3.6, owner Q2 A): grid 12 by weight, attack budget 10 by cost, rotation, Bruisers weigh 8.</summary>
    public sealed class SlotTests
    {
        const byte Alive = SensedFlags.Alive;

        static List<Entity> Ring(HostileTestWorld w, int count, float radius, byte tier = 0)
        {
            var list = new List<Entity>();
            for (int i = 0; i < count; i++)
            {
                float a = 2f * math.PI * i / count;
                Entity h = w.Hostile(new float3(math.sin(a) * radius, 0f, math.cos(a) * radius), HostileBehaviourKind.FightsBack, (uint)(i + 1));
                HostileLife life = w.Em.GetComponentData<HostileLife>(h);
                life.Tier = tier;
                w.Em.SetComponentData(h, life);
                w.Hit(h, 100, 10f);
                list.Add(h);
            }
            return list;
        }

        [Test]
        public void NeverOverCapacity()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((float3.zero, Alive));
            List<Entity> pack = Ring(w, 12, 1.5f);
            int mostSwinging = 0;
            for (int t = 0; t < 3000; t++)
            {
                w.Step();
                int grid = 0, attack = 0, swinging = 0;
                foreach (Entity h in pack)
                {
                    HostileSlot s = w.Em.GetComponentData<HostileSlot>(h);
                    if (s.Held != 0) grid += 4;
                    attack += s.Cost;
                    byte phase = w.Brain(h).Phase;
                    if (phase == HostilePhase.Windup) swinging++;
                }
                Assert.LessOrEqual(grid, 12, "grid capacity at tick " + w.Tick);
                Assert.LessOrEqual(attack, 10, "attack capacity at tick " + w.Tick);
                mostSwinging = math.max(mostSwinging, swinging);
            }
            Assert.LessOrEqual(mostSwinging, 3, "at most three wind up at once");
            Assert.Greater(w.Strikes, 100, "and they do swing, in turns");
        }

        [Test]
        public void Rotation_NoWaitOver2s()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((float3.zero, Alive));
            List<Entity> pack = Ring(w, 12, 1.5f);
            var swings = new int[pack.Count];
            uint longest = 0;
            for (int t = 0; t < 3000; t++)
            {
                w.Step();
                for (int i = 0; i < pack.Count; i++)
                {
                    HostileSlot s = w.Em.GetComponentData<HostileSlot>(pack[i]);
                    HostileBrain b = w.Brain(pack[i]);
                    if (b.Phase == HostilePhase.Windup && b.AttackStartTick == w.Tick) swings[i]++;
                    if (t > 120 && s.WaitingSinceTick != 0) longest = math.max(longest, w.Tick - s.WaitingSinceTick);
                }
            }
            Assert.LessOrEqual(longest, (uint)(2 * HostileTestWorld.TickRate), "nobody waits more than 2 s for its turn");
            for (int i = 0; i < pack.Count; i++) Assert.Greater(swings[i], 20, "every one of the twelve gets its turns (" + i + ")");
        }

        [Test]
        public void BruiserTakesTwoSlots()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((float3.zero, Alive));
            List<Entity> bruiser = Ring(w, 1, 1.5f, (byte)HostileTier.Bruiser);
            var normals = new List<Entity>();
            for (int i = 0; i < 3; i++)
            {
                Entity h = w.Hostile(new float3(-1.5f + i, 0f, -1.2f), HostileBehaviourKind.FightsBack, (uint)(10 + i));
                w.Hit(h, 100, 1f);
                normals.Add(h);
            }
            bool seen = false;
            for (int t = 0; t < 600; t++)
            {
                w.Step();
                if (w.Em.GetComponentData<HostileSlot>(bruiser[0]).Held == 0) continue;
                seen = true;
                int held = 0;
                foreach (Entity h in normals) if (w.Em.GetComponentData<HostileSlot>(h).Held != 0) held++;
                Assert.LessOrEqual(held, 1, "a Bruiser (8) leaves room for one normal (4) on a grid of 12");
            }
            Assert.IsTrue(seen, "the Bruiser held a slot");
        }
    }

    /// <summary>M6.5 assist (owner Q2 A: on for AttacksOnSight species only): a hit calls its own kind of the zone within 6 m.</summary>
    public sealed class AssistTests
    {
        const byte Alive = SensedFlags.Alive;

        static Entity Make(HostileTestWorld w, float3 at, ushort species, bool assist, uint id)
        {
            Entity h = w.Hostile(at, HostileBehaviourKind.FightsBack, id);
            HostileLife life = w.Em.GetComponentData<HostileLife>(h);
            life.Species = species;
            life.Assist = (byte)(assist ? 1 : 0);
            w.Em.SetComponentData(h, life);
            return h;
        }

        [Test]
        public void SameSpeciesWithin6m_Joins()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((new float3(0f, 0f, -1.5f), Alive));
            Entity hit = Make(w, float3.zero, 5, true, 1);
            Entity near = Make(w, new float3(4f, 0f, 0f), 5, true, 2);
            Entity far = Make(w, new float3(10f, 0f, 0f), 5, true, 3);
            Entity other = Make(w, new float3(3f, 0f, 0f), 6, true, 4);
            w.Hit(hit, 100, 10f);
            w.Step(3);
            Assert.AreEqual(HostileMode.Engage, w.Brain(hit).Mode);
            Assert.AreEqual(100, w.Target(near).GhostId, "the same species 4 m away joins");
            Assert.AreEqual(HostileMode.Engage, w.Brain(near).Mode);
            Assert.AreEqual(0, w.Target(far).GhostId, "10 m away: beyond the assist radius");
            Assert.AreEqual(0, w.Target(other).GhostId, "another species stays out");
        }

        [Test]
        public void FightsBackSpecies_DoNotAssist()
        {
            using var w = new HostileTestWorld();
            w.SetPlayers((new float3(0f, 0f, -1.5f), Alive));
            Entity hit = Make(w, float3.zero, 2, false, 1);
            Entity near = Make(w, new float3(3f, 0f, 0f), 2, false, 2);
            w.Hit(hit, 100, 10f);
            w.Step(3);
            Assert.AreEqual(HostileMode.Engage, w.Brain(hit).Mode, "the one hit fights back");
            Assert.AreEqual(0, w.Target(near).GhostId, "its neighbour does not assist (assist off)");
        }
    }
}
