using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
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
    }
}
