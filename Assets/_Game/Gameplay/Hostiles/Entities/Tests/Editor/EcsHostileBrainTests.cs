using KryticalNova.Brains;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>M5.5c: HostileNet's IHostileBrain calls reach the entity brain as HostileBrainLink's reach Emerald (M5.5c table).</summary>
    public sealed class EcsHostileBrainTests
    {
        [TearDown]
        public void ResetResolvers()
        {
            EcsHostileBrain.GhostIdOfBody = null;
            EcsHostileBrain.BodyOfGhost = null;
        }

        [Test]
        public void TheGmsProwler_AFightsBackSpecies_AttacksOnSight_OnlyWhileTheSwitchIsOn()
        {
            // M5.5 plan: gm.hostile.spawn prowler = the Bonewalker body (a FightsBack species) at level 4, attacking on sight
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(new float3(0f, 0f, 0f), HostileBehaviourKind.FightsBack);
            var body = new GameObject("ProwlerBody");
            var species = ScriptableObject.CreateInstance<HostileDefinition>();
            species.behaviour = HostileBehaviour.FightsBack;
            species.attacks = new HostileDefinition.Attack[0];
            HostileSpawner.AuthorityOverride = () => true;
            try
            {
                var net = body.AddComponent<HostileNet>();
                var brain = body.AddComponent<EcsHostileBrain>();
                brain.Bind(w.World, h);
                brain.Configure(species, default);
                Assert.AreEqual(HostileBehaviourKind.FightsBack, w.Em.GetComponentData<HostileLife>(h).Behaviour, "the species' own");
                net.SetGmAttacksOnSight(true);
                Assert.IsTrue(net.GmAttacksOnSight);
                brain.Configure(species, default);
                Assert.AreEqual(HostileBehaviourKind.AttacksOnSight, w.Em.GetComponentData<HostileLife>(h).Behaviour, "the GM's Prowler");
                net.SetGmAttacksOnSight(false);
                brain.Configure(species, default);
                Assert.AreEqual(HostileBehaviourKind.FightsBack, w.Em.GetComponentData<HostileLife>(h).Behaviour, "off again");
            }
            finally
            {
                HostileSpawner.AuthorityOverride = null;
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(species);
            }
        }

        [Test]
        public void IHostileBrain_RoundTrip()
        {
            using var w = new HostileTestWorld();
            Entity h = w.Hostile(new float3(0f, 0f, 0f), HostileBehaviourKind.FightsBack);
            var body = new GameObject("HostileBody");
            var player = new GameObject("PlayerBody");
            try
            {
                EcsHostileBrain.GhostIdOfBody = t => t == player.transform ? 100 : 0;
                IHostileBrain brain = body.AddComponent<EcsHostileBrain>();
                ((EcsHostileBrain)brain).Bind(w.World, h);
                brain.SetAwake(true);
                Assert.IsTrue(w.Em.IsComponentEnabled<BrainActive>(h));
                brain.SetHome(new Vector3(0f, 0f, 0f), 0);
                Assert.AreEqual(1f, w.Em.GetComponentData<HostileHome>(h).WanderRadius, "wander at least 1 m, as Emerald");

                w.SetPlayers((new float3(1.5f, 0f, 0f), SensedFlags.Alive));
                w.Step();
                Assert.AreEqual(HostileMode.Idle, w.Brain(h).Mode, "FightsBack: no fight by sight");

                brain.Aggro(player.transform);
                w.Step();
                Assert.AreEqual(HostileMode.Engage, w.Brain(h).Mode, "Aggro: a fight at once");
                Assert.AreEqual(100, w.Target(h).GhostId);
                StringAssert.StartsWith("in combat", brain.Describe());

                brain.Calm();
                w.Step();
                Assert.AreNotEqual(HostileMode.Engage, w.Brain(h).Mode, "Calm: the fight is forgotten");
                Assert.AreEqual(0, w.Target(h).GhostId);
                Assert.AreEqual(0, w.Em.GetBuffer<ThreatEntry>(h).Length);

                brain.Frozen = true;
                Assert.IsFalse(w.Em.IsComponentEnabled<BrainActive>(h), "frozen: the brain stands");
                Assert.AreEqual("frozen", brain.Describe());
                brain.Frozen = false;
                brain.SetAwake(false);
                Assert.IsFalse(w.Em.IsComponentEnabled<BrainActive>(h));
                Assert.AreEqual("asleep", brain.Describe());
            }
            finally
            {
                Object.DestroyImmediate(body);
                Object.DestroyImmediate(player);
            }
        }
    }
}
