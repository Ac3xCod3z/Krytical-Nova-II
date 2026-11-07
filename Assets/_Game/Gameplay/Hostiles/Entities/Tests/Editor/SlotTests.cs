using System.Collections.Generic;
using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// M6.5 the stage manager (brain plan s3.6, owner Q2 A) with crowding by body size (owner pick A, 7 Nov 2026): places by body arc in 90 %
    /// of the circle, a Bruiser's arc double, no two places overlap, and the attack budget 10 by cost, rotating.
    /// </summary>
    public sealed class SlotTests
    {
        const byte Alive = SensedFlags.Alive;

        static List<Entity> Ring(HostileTestWorld w, int count, float radius, byte tier = 0, int radiusMm = 450, uint firstId = 1)
        {
            var list = new List<Entity>();
            for (int i = 0; i < count; i++)
            {
                float a = 2f * math.PI * i / count;
                Entity h = w.Hostile(new float3(math.sin(a) * radius, 0f, math.cos(a) * radius), HostileBehaviourKind.FightsBack, (uint)(firstId + i));
                HostileLife life = w.Em.GetComponentData<HostileLife>(h);
                life.Tier = tier;
                life.RadiusMm = radiusMm;
                w.Em.SetComponentData(h, life);
                w.Hit(h, 100, 10f);
                list.Add(h);
            }
            return list;
        }

        static HostileBrainConfig Config()
        {
            var settings = UnityEngine.ScriptableObject.CreateInstance<HostileBrainSettings>();
            HostileBrainConfig c = HostileBrainConfig.From(settings, HostileTestWorld.TickRate);
            UnityEngine.Object.DestroyImmediate(settings);
            return c;
        }

        /// <summary>The arc a held Hostile takes, as the slot system works it out.</summary>
        static float ArcOf(HostileTestWorld w, Entity h, in HostileBrainConfig c)
        {
            HostileLife life = w.Em.GetComponentData<HostileLife>(h);
            float r = life.RadiusMm * 0.001f;
            float stand = HostileRingRules.Stand(c.PlayerRadius, r, c.SlotStandGap, life.AttackRange + c.ReachSlack);
            return HostileRingRules.Arc(r, stand, life.Tier == (byte)HostileTier.Bruiser ? c.BruiserArcFactor : 1f);
        }

        static bool Busy(byte phase) => phase == HostilePhase.Tell || phase == HostilePhase.Windup || phase == HostilePhase.Recover;

        [Test]
        public void TheDefaults_StandAtPlayerPlusBodyPlusGap_SevenNormalBodiesFitIn90Percent()
        {
            HostileBrainConfig c = Config();
            Assert.AreEqual(0.5f, c.PlayerRadius, 1e-5f, "the player's hit capsule");
            Assert.AreEqual(0.2f, c.SlotStandGap, 1e-5f);
            Assert.AreEqual(0.9f, c.SlotRingFill, 1e-5f);
            Assert.AreEqual(2f, c.BruiserArcFactor, 1e-5f);
            float stand = HostileRingRules.Stand(c.PlayerRadius, 0.45f, c.SlotStandGap, 2.3f);
            Assert.AreEqual(1.15f, stand, 1e-5f);
            float arc = HostileRingRules.Arc(0.45f, stand, 1f);
            Assert.AreEqual(7, (int)(HostileRingRules.Room(c.SlotRingFill) / arc), "seven 0.45 m bodies round a player");
            Assert.AreEqual(2.2f, HostileRingRules.Stand(c.PlayerRadius, 1.5f, c.SlotStandGap, 4f), 1e-5f, "a big body stands farther out by itself");
            Assert.AreEqual(2.3f, HostileRingRules.Stand(c.PlayerRadius, 3f, c.SlotStandGap, 2.3f), 1e-5f, "never beyond its reach");
            Assert.AreEqual(2f * arc, HostileRingRules.Arc(0.45f, stand, 2f), 1e-5f, "a Bruiser's arc counts double");
        }

        [Test]
        public void MixedSizes_NeverOverlapOnTheRing()
        {
            // any mix of sizes, tiers and bearings whose arcs fit: no two bodies overlap, all the way round
            HostileBrainConfig c = Config();
            var rng = new Random(9031);
            for (int trial = 0; trial < 500; trial++)
            {
                int n = rng.NextInt(1, 14);
                var radii = new List<float>();
                var stands = new List<float>();
                var arcList = new List<float>();
                float used = 0f;
                for (int i = 0; i < n; i++)
                {
                    float r = rng.NextFloat(0.2f, 2.5f);
                    float stand = HostileRingRules.Stand(c.PlayerRadius, r, c.SlotStandGap, 10f);
                    float arc = HostileRingRules.Arc(r, stand, rng.NextBool() ? c.BruiserArcFactor : 1f);
                    if (used + arc > HostileRingRules.Room(c.SlotRingFill)) break;
                    used += arc;
                    radii.Add(r); stands.Add(stand); arcList.Add(arc);
                }
                int m = radii.Count;
                var bearingList = new List<float>();
                for (int i = 0; i < m; i++) bearingList.Add(rng.NextFloat(-math.PI, math.PI));
                var order = new List<int>();
                for (int i = 0; i < m; i++) order.Add(i);
                order.Sort((a, b) => bearingList[a].CompareTo(bearingList[b]));
                var bearings = new NativeArray<float>(m, Allocator.Temp);
                var arcs = new NativeArray<float>(m, Allocator.Temp);
                var places = new NativeArray<float>(m, Allocator.Temp);
                for (int k = 0; k < m; k++) { bearings[k] = bearingList[order[k]]; arcs[k] = arcList[order[k]]; }
                HostileRingRules.Layout(bearings, arcs, places);
                bearings.Dispose();
                arcs.Dispose();
                for (int a = 0; a < m; a++)
                    for (int b = a + 1; b < m; b++)
                    {
                        int ia = order[a], ib = order[b];
                        float2 pa = new float2(math.sin(places[a]), math.cos(places[a])) * stands[ia];
                        float2 pb = new float2(math.sin(places[b]), math.cos(places[b])) * stands[ib];
                        Assert.GreaterOrEqual(math.distance(pa, pb) + 1e-3f, radii[ia] + radii[ib],
                            $"trial {trial}: bodies {ia} ({radii[ia]:F2} m) and {ib} ({radii[ib]:F2} m) overlap");
                    }
                for (int a = 0; a < m; a++)
                    Assert.GreaterOrEqual(stands[order[a]] - radii[order[a]] + 1e-4f, c.PlayerRadius + c.SlotStandGap, "clear of the player");
                places.Dispose();
            }
        }

        [Test]
        public void ATwentyMobSwarm_NeverAboveTheStrikeCap_NorTheRing_AndTheyTakeTurns()
        {
            HostileBrainConfig c = Config();
            using var w = new HostileTestWorld();
            w.SetPlayers((float3.zero, Alive));
            List<Entity> swarm = Ring(w, 14, 1.5f);
            swarm.AddRange(Ring(w, 6, 1.9f, radiusMm: 900, firstId: 40));   // bigger bodies in the same swarm
            float room = HostileRingRules.Room(c.SlotRingFill);
            int mostSwinging = 0, mostHeld = 0;
            for (int t = 0; t < 3000; t++)
            {
                w.Step();
                int attack = 0, swinging = 0, held = 0;
                float arcs = 0f, busyArcs = 0f;
                foreach (Entity h in swarm)
                {
                    HostileSlot s = w.Em.GetComponentData<HostileSlot>(h);
                    byte phase = w.Brain(h).Phase;
                    if (s.Placed != 0) { held++; arcs += ArcOf(w, h, c); if (Busy(phase)) busyArcs += ArcOf(w, h, c); }
                    attack += s.Cost;
                    if (phase == HostilePhase.Windup) swinging++;
                }
                // one swinging keeps its place, so the ring runs over at most by what was already swinging when the room was shared
                Assert.LessOrEqual(arcs - busyArcs, room + 1e-3f, "the ring's room at tick " + w.Tick);
                Assert.LessOrEqual(attack, c.AttackCapacity, "the strike cap at tick " + w.Tick);
                mostSwinging = math.max(mostSwinging, swinging);
                mostHeld = math.max(mostHeld, held);
            }
            Assert.LessOrEqual(mostSwinging, 3, "at most three wind up at once (attack budget 10, light 3)");
            Assert.Less(mostHeld, swarm.Count, "not all twenty stand round the player");
            Assert.Greater(w.Strikes, 100, "and they do swing, in turns");
        }

        [Test]
        public void Rotation_EveryOneGetsItsTurns()
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
            // fair rotation: 12 Hostiles, 3 swinging at once, so nobody waits more than three swing cycles (tell 4 + windup 12 + recover 6 here)
            // plus a few ticks; the plan's "2 s" held before Q3's tell (7 Nov 2026: 66-68 ticks with it). The capacities are the owner's to tune.
            Assert.LessOrEqual(longest, 3u * (4u + 12u + 6u) + 6u, "nobody waits longer than three others' swings");
            for (int i = 0; i < pack.Count; i++) Assert.Greater(swings[i], 20, "every one of the twelve gets its turns (" + i + ")");
        }

        [Test]
        public void ABruisersArc_CountsDouble()
        {
            HostileBrainConfig c = Config();
            using var w = new HostileTestWorld();
            w.SetPlayers((float3.zero, Alive));
            List<Entity> bruiser = Ring(w, 1, 1.5f, (byte)HostileTier.Bruiser);
            List<Entity> normals = Ring(w, 8, 1.6f, firstId: 10);
            float normalArc = ArcOf(w, normals[0], c), bruiserArc = ArcOf(w, bruiser[0], c);
            Assert.AreEqual(2f * normalArc, bruiserArc, 1e-4f);
            int room = (int)((HostileRingRules.Room(c.SlotRingFill) - bruiserArc) / normalArc);   // 5 normals beside the Bruiser
            bool seen = false;
            for (int t = 0; t < 600; t++)
            {
                w.Step();
                if (w.Em.GetComponentData<HostileSlot>(bruiser[0]).Placed == 0) continue;
                int held = 0, busy = 0;
                foreach (Entity h in normals)
                {
                    if (w.Em.GetComponentData<HostileSlot>(h).Placed == 0) continue;
                    held++;
                    if (Busy(w.Brain(h).Phase)) busy++;
                }
                seen = true;
                Assert.LessOrEqual(held - busy, room, "beside a Bruiser (two normals' room) at most " + room + " normals are given a place");
            }
            Assert.IsTrue(seen, "the Bruiser held a place");
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
