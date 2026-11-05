using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>
    /// M4.4k: each strike of a ghost's combo is played once on its body, on the attacker's own client (predicted) and on everyone else's
    /// (from the server), as NGO's PlayComboStrikeClientRpc did.
    /// </summary>
    public class StrikePresentTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        [Test]
        public void ANewStrike_IsItsStepAndStartTick()
        {
            var t100 = new NetworkTick(100);
            Assert.IsTrue(StrikePresentSystem.IsNewStrike(0, NetworkTick.Invalid, 1, t100), "the first strike");
            Assert.IsFalse(StrikePresentSystem.IsNewStrike(1, t100, 1, t100), "the same strike again (a re-prediction)");
            Assert.IsTrue(StrikePresentSystem.IsNewStrike(1, t100, 2, new NetworkTick(126)), "the next step");
            Assert.IsTrue(StrikePresentSystem.IsNewStrike(1, t100, 1, new NetworkTick(190)), "strike 1 of the next round");
            Assert.IsFalse(StrikePresentSystem.IsNewStrike(1, t100, 0, NetworkTick.Invalid), "no strike");
        }

        [Test]
        public void AStrike_IsShownOnce_OnTheAttackersOwnClient_AndOnTheOthers()
        {
            using var rig = PlayerRig.Create(2, 0, new[] { new int3(0, 0, 0), new int3(0, 0, 1200) });
            Entity own = rig.GhostIn(0, 0), seen = rig.GhostIn(1, 0);
            var ownBody = new GameObject("attacker, own view");
            var seenBody = new GameObject("attacker, seen by the other");
            GhostBodyRegistry.Register(rig.Clients[0], own, 0, default, ownBody, true);
            GhostBodyRegistry.Register(rig.Clients[1], seen, 0, default, seenBody, false);
            try
            {
                int target = rig.Get<GhostInstance>(rig.Server, rig.ServerGhosts[1]).ghostId;
                rig.Sources[0].Attack(target);
                var ownShow = rig.Clients[0].GetExistingSystemManaged<StrikePresentSystem>();
                var seenShow = rig.Clients[1].GetExistingSystemManaged<StrikePresentSystem>();
                Assert.IsNotNull(ownShow, "the system runs in client worlds");
                rig.Tick(20);   // inside strike 1 (26 ticks)
                Assert.AreEqual(1, ownShow.Shown, "strike 1 played once on the attacker's own client");
                Assert.AreEqual(1, seenShow.Shown, "and once on the other player's");
                rig.Tick(30);   // into strike 2
                Assert.AreEqual(2, ownShow.Shown, "then strike 2");
                Assert.AreEqual(2, seenShow.Shown);
            }
            finally
            {
                GhostBodyRegistry.Unregister(rig.Clients[0], own, out _);
                GhostBodyRegistry.Unregister(rig.Clients[1], seen, out _);
                Object.DestroyImmediate(ownBody);
                Object.DestroyImmediate(seenBody);
            }
        }
    }
}
