using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// M6.2 / H15: a zone that falls asleep (no player within radius + wake margin) puts its Hostiles to sleep through
    /// <see cref="HostileNet.SetAsleep"/>: NGO calls the brain's Calm, then SetAwake(false) (HostileNet.cs:330-343). On the entity brain that
    /// ends the fight and stops the brain; it wakes fresh. The heal (ResetHealth) is the body's own: PlayMode B80.
    /// </summary>
    public sealed class ZoneSleepTests
    {
        [TearDown]
        public void ResetResolvers()
        {
            EcsHostileBrain.GhostIdOfBody = null;
            EcsHostileBrain.BodyOfGhost = null;
        }

        [Test]
        public void Sleep_DisablesBrains_HealsAndEndsFights()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(new float3(0f, 0f, 0f), HostileBehaviourKind.AttacksOnSight);
            var body = new GameObject("ZoneBody");
            var player = new GameObject("PlayerBody");
            try
            {
                EcsHostileBrain.GhostIdOfBody = t => t == player.transform ? 100 : 0;
                var brain = body.AddComponent<EcsHostileBrain>();
                brain.Bind(w.World, h);
                brain.SetAwake(true);
                w.SetPlayers((new float3(4f, 0f, 0f), SensedFlags.Alive));
                brain.Aggro(player.transform);
                w.Step(5);
                Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode, "in a fight before the zone sleeps");

                // HostileNet.SetAsleep(true): Calm, then the brain off
                brain.Calm();
                brain.SetAwake(false);
                Assert.IsFalse(w.Em.IsComponentEnabled<BrainActive>(h), "asleep: no brain");
                Assert.AreEqual(0, w.Target(h).GhostId, "the fight is over");
                Assert.AreEqual(0, w.Em.GetBuffer<ThreatEntry>(h).Length, "nobody is remembered");
                float3 at = w.Em.GetComponentData<LocalTransform>(h).Position;
                w.Step(60);
                Assert.AreEqual(at, w.Em.GetComponentData<LocalTransform>(h).Position, "asleep it stays where it is");
                Assert.AreEqual(0, w.Target(h).GhostId, "and picks nobody while asleep, a player in sight or not");
                Assert.AreEqual("asleep", brain.Describe());

                // the zone wakes: the brain runs again, with no fight carried over
                w.SetPlayers((new float3(30f, 0f, 0f), SensedFlags.Alive));
                brain.SetAwake(true);
                Assert.IsTrue(w.Em.IsComponentEnabled<BrainActive>(h), "awake again");
                w.Step(3);
                Assert.AreNotEqual(HostileMode.Engage, w.Brain(h).Mode, "wakes fresh");
                Assert.AreEqual(0, w.Target(h).GhostId);
            }
            finally
            {
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(player);
            }
        }
    }
}
