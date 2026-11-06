using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Player.Entities.Tests;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>
    /// The mouse's way in: <see cref="BodyClicks.Ground"/> (what <c>PointAndClickPlayer</c> calls in view mode) through
    /// <see cref="BodyClickSource"/> and <see cref="ClickCommandSource"/> to the ghost, at three frames a tick (the game at 84 fps).
    /// Owner's GUI test 30 Oct: the marker showed but the first click of a new destination did not walk.
    /// </summary>
    public class BodyClickPathTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();
        [TearDown] public void TearDown() { PlayerClickLog.Sink = null; BodyClicks.Clear(); }

        [Test]
        public void OneGroundClick_FromStandingStill_Walks_OnEveryFrameOfItsTick()
        {
            using var rig = PlayerRig.Create(1, 100);
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().Source = new ClickCommandSource(new BodyClickSource());
            rig.Tw.FramesPerTick = 3;
            rig.Tick(60);
            BodyClicks.Clear();
            var lines = new List<string>();
            PlayerClickLog.Sink = l => { lock (lines) lines.Add(l); };
            for (int phase = 0; phase < 3; phase++)
            {
                rig.Tick(30 + phase);                       // standing still a second, then a click on another frame of its tick
                var goal = new int3(3000 * (phase + 1), 0, -2000 * phase);
                ushort before = rig.OwnerMove().OrderSeq;
                BodyClicks.Ground(new Vector3(goal.x, goal.y, goal.z) * 0.001f, false);   // ONE click
                int frames = 0;
                while (rig.OwnerMove().OrderSeq == before || (rig.OwnerMove().Flags & MoveFlags.Moving) == 0)
                {
                    rig.Tick(1);
                    Assert.Less(++frames, 9, $"phase {phase}: one click did not start the owner's walk within 3 ticks. Log: {string.Join(" | ", lines)}");
                }
                rig.Tick(3 * 300);
                Assert.AreEqual(goal, rig.ServerPose().Mm, $"phase {phase}: the server walked the one click. Log: {string.Join(" | ", lines)}");
                Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            }
            Assert.IsFalse(lines.Exists(l => l.StartsWith("[KnClick] refused")), "no click was refused: " + string.Join(" | ", lines));
        }

        [Test]
        public void TheSecondClickOnAHostileBody_AttacksItsGhost_AndAGroundClickCallsItOff()
        {
            // M4.4j: PointAndClickPlayer in view mode puts the clicked body in BodyClicks.Attack; the click source finds its ghost
            using var rig = PlayerRig.Create(2, 0, new[] { new int3(0, 0, 0), new int3(0, 0, 1200) });
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().Source = new ClickCommandSource(new BodyClickSource());
            var body = new GameObject("other player's view");
            Entity view = rig.GhostIn(0, 1);
            int ghostId = rig.Get<GhostInstance>(rig.Clients[0], view).ghostId;
            GhostBodyRegistry.Register(rig.Clients[0], view, 0, default, body, false);
            try
            {
                Assert.AreEqual(ghostId, BodyClickSource.GhostIdOf(body.transform), "the body's own ghost");
                BodyClicks.Attack(body.transform);
                rig.Tick(10);
                Assert.AreEqual(ghostId, rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]).TargetGhostId, "the server attacks the clicked body's ghost");

                BodyClicks.Ground(new Vector3(-3f, 0f, 0f), false);
                rig.Tick(10);
                Assert.AreEqual(0, rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]).TargetGhostId, "a ground click calls the attack off (NGO)");
            }
            finally
            {
                GhostBodyRegistry.Unregister(rig.Clients[0], view, out _);
                Object.DestroyImmediate(body);
            }
        }

        [Test]
        public void AnEmotesSheathe_PutsTheGhostsWeaponAway_EndsTheAttack_AndNeverDrawsIt()
        {
            // NGO PlayerEmotes.RequestPlay:193-194: the owner puts the weapon away before the emote (RequestSetWeaponDraw(false))
            using var rig = PlayerRig.Create(2, 0, new[] { new int3(0, 0, 0), new int3(0, 0, 1200) });
            rig.Clients[0].GetExistingSystemManaged<PlayerInputGatherSystem>().Source = new ClickCommandSource(new BodyClickSource());
            var body = new GameObject("other player's view");
            Entity view = rig.GhostIn(0, 1);
            GhostBodyRegistry.Register(rig.Clients[0], view, 0, default, body, false);
            try
            {
                BodyClicks.SheatheWeapon();
                rig.Tick(10);
                Assert.AreEqual(0, rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]).WeaponDrawn, "sheathed already: the emote never draws it");

                BodyClicks.Attack(body.transform);
                rig.Tick(10);
                CombatState drawn = rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]);
                Assert.AreEqual(1, drawn.WeaponDrawn, "the attack drew the weapon");
                Assert.AreNotEqual(0, drawn.TargetGhostId);

                BodyClicks.SheatheWeapon();
                rig.Tick(10);
                CombatState after = rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]);
                Assert.AreEqual(0, after.WeaponDrawn, "the emote put the weapon away on the server");
                Assert.AreEqual(0, after.TargetGhostId, "and the attack ended");
                Assert.AreEqual(0, rig.Get<CombatState>(rig.Clients[0], rig.GhostIn(0, 0)).WeaponDrawn, "the owner's ghost shows it sheathed");
            }
            finally
            {
                GhostBodyRegistry.Unregister(rig.Clients[0], view, out _);
                Object.DestroyImmediate(body);
            }
        }
    }
}
