using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using KryticalNova.Networking.Entities.Tests;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Layer C: a server and two clients in one process (loopback, hand-ticked), the player ghost owned by each client.
    /// Client 0 clicks; client 1 only watches. At 100 ms one-way delay the owner predicts about 7 ticks ahead and is rolled back
    /// by every snapshot, which is the case these tests are about.
    /// </summary>
    public class PlayerGhostWorldTests
    {
        static readonly int3 Goal = new int3(20000, 0, 0);

        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        static void Log(string line) => Debug.Log("[PLAYER-M3A] " + line);

        // ---------------------------------------------------------------------------------------------- click, move, arrive
        [TestCase(0)]
        [TestCase(100)]
        public void Click_MovesAndArrives_ServerAndOwnerStayEqual(int delayMs)
        {
            using var rig = PlayerRig.Create(2, delayMs);
            uint clickTick = rig.ServerTick;
            rig.Sources[0].Click(Goal);
            rig.Tick(200);                                   // 20 m at 5 m/s is about 150 ticks

            var sm = rig.ServerMove(); var sp = rig.ServerPose();
            var om = rig.OwnerMove(); var op = rig.OwnerPose();
            Assert.AreEqual(Goal.x, sp.XMm, "server arrived");
            Assert.AreEqual(Goal.x, op.XMm, "owner arrived");
            Assert.AreEqual(0, sm.Flags & MoveFlags.Moving);
            Assert.AreEqual(sm, om, "move state equal on server and owner after arrival");
            Assert.AreEqual(sp, op);

            var cmp = rig.CompareOwner(0, clickTick, rig.ServerTick - 1);
            var diag = rig.Get<PlayerMoveDiag>(rig.Clients[0], rig.GhostIn(0, 0));
            Log($"click/arrive delay={delayMs} ms: common ticks={cmp.Common} mismatches={cmp.Mismatch} first={cmp.FirstMismatchTick}; " +
                $"owner runs={diag.Runs} firstRuns={diag.FirstRuns} maxRunsOneTick={diag.MaxRunsOneTick} orderStarts={diag.OrderStarts}");
            Assert.GreaterOrEqual(cmp.Common, 150, "too few ticks compared");
            Assert.AreEqual(0, cmp.Mismatch, "integer state differs between server and owner at tick " + cmp.FirstMismatchTick + ": " + cmp.FirstDump);
            if (delayMs > 0)
            {
                Assert.Greater(diag.Runs, diag.FirstRuns, "no re-simulation happened: the test proves nothing about rollback");
                // MaxRunsOneTick of the diag counts runs of the same tick back to back; a tick is re-simulated in a LATER frame, so the
                // ring slot (which keeps the tick) is where the count of repeats shows
                var ring = rig.Clients[0].EntityManager.GetBuffer<PlayerMoveTrace>(rig.GhostIn(0, 0), true);
                int most = 0; for (int i = 0; i < ring.Length; i++) most = math.max(most, ring[i].RunsForTick);
                Log($"most re-simulations of one tick: {most}");
                Assert.Greater(most, 1, "no tick was simulated twice");
            }
        }

        [Test]
        public void ADogleg_WithACorner_Converges()
        {
            using var rig = PlayerRig.Create(2, 100);
            rig.ClientPlanner.Dogleg = true; rig.ServerPlanner.Dogleg = true;
            uint t0 = rig.ServerTick;
            rig.Sources[0].Click(new int3(12000, 0, 9000));
            rig.Tick(220);
            Assert.AreEqual(new int3(12000, 0, 9000), rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            var cmp = rig.CompareOwner(0, t0, rig.ServerTick - 1);
            Assert.AreEqual(0, cmp.Mismatch);
        }

        // ---------------------------------------------------------------------------------------------- observer
        [Test]
        public void Observer_SeesAnInterpolatedPose_NearTheServer()
        {
            using var rig = PlayerRig.Create(2, 0);
            var seen = new List<float>();
            var obsGhost = rig.GhostIn(1, 0);
            var obs = rig.Clients[1].EntityManager;
            Assert.IsFalse(obs.HasComponent<GhostOwnerIsLocal>(obsGhost) && obs.IsComponentEnabled<GhostOwnerIsLocal>(obsGhost),
                "client 1 must see the player of client 0 as somebody else's ghost");
            rig.Sources[0].Click(Goal);
            float worst = 0f;
            for (int i = 0; i < 220; i++)
            {
                rig.Tick(1);
                float server = PlayerRig.M(rig.ServerPose()).x;
                float seenX = obs.GetComponentData<LocalTransform>(obsGhost).Position.x;
                seen.Add(seenX);
                worst = math.max(worst, server - seenX);
                Assert.LessOrEqual(seenX, server + 0.05f, "an interpolated ghost is never ahead of the server");
            }
            Log($"observer: worst lag behind the server {worst:F2} m over a 20 m walk at 5 m/s");
            Assert.Less(worst, 3.0f, "the observer follows the server closely");
            int distinct = 0; for (int i = 1; i < seen.Count; i++) if (seen[i] != seen[i - 1]) distinct++;
            Assert.Greater(distinct, 60, "the observer moves smoothly, not in jumps");
            Assert.AreEqual(Goal.x * 0.001f, seen[seen.Count - 1], 0.02f);
            Assert.AreEqual(rig.ServerPose(), rig.Get<PlayerPose>(rig.Clients[1], obsGhost), "the integer pose on the observer is the server's");
        }

        // ---------------------------------------------------------------------------------------------- stop
        [TestCase(0)]
        [TestCase(100)]
        public void Stop_MidPath_StopsServerAndOwnerAtTheSameSpot(int delayMs)
        {
            using var rig = PlayerRig.Create(2, delayMs);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(50);
            Assert.AreNotEqual(0, rig.ServerMove().Flags & MoveFlags.Moving);
            rig.Sources[0].Stop();
            rig.Tick(60);
            var sp = rig.ServerPose();
            Assert.AreEqual(0, rig.ServerMove().Flags & MoveFlags.Moving);
            Assert.AreEqual(0, rig.OwnerMove().Flags & MoveFlags.Moving);
            Assert.Greater(sp.XMm, 3000); Assert.Less(sp.XMm, 40000);
            Assert.AreEqual(sp, rig.OwnerPose());
            Assert.AreEqual(sp, rig.Get<PlayerPose>(rig.Clients[1], rig.GhostIn(1, 0)), "the observer ends at the same spot");
        }

        // ---------------------------------------------------------------------------------------------- teleport
        [Test]
        public void Teleport_SnapsEveryWorld_DropsTheWalk_AndBumpsTeleportSeq()
        {
            using var rig = PlayerRig.Create(2, 100);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(40);
            var before = rig.Get<TeleportSeq>(rig.Server, rig.ServerGhosts[0]).Value;
            var dest = new int3(-7000, 500, 12000);
            PlayerEntitiesSetup.Teleport(rig.Server.EntityManager, rig.ServerGhosts[0], dest, 9000);
            rig.Tick(120);
            Assert.AreEqual(dest, rig.ServerPose().Mm);
            Assert.AreEqual(dest, rig.OwnerPose().Mm, "the owner lands on the placement");
            var observer = rig.Clients[1];
            var og = rig.GhostIn(1, 0);
            Assert.AreEqual(dest, observer.EntityManager.GetComponentData<PlayerPose>(og).Mm);
            Assert.AreEqual(0, rig.ServerMove().Flags & MoveFlags.Moving, "the walk is dropped");
            Assert.AreEqual(0, rig.OwnerMove().Flags & MoveFlags.Moving);
            byte after = rig.Get<TeleportSeq>(rig.Server, rig.ServerGhosts[0]).Value;
            Assert.AreEqual((byte)(before + 1), after);
            Assert.AreEqual(after, rig.Get<TeleportSeq>(rig.Clients[0], rig.GhostIn(0, 0)).Value);
            Assert.AreEqual(after, rig.Get<TeleportSeq>(observer, og).Value);
            Assert.AreEqual(dest.x * 0.001f, rig.Get<LocalTransform>(rig.Clients[0], rig.GhostIn(0, 0)).Position.x, 0.002f);
            // and the player can walk again from the new place
            rig.Sources[0].Click(dest + new int3(0, 0, 6000));
            rig.Tick(120);
            Assert.AreEqual(dest + new int3(0, 0, 6000), rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
        }

        // ---------------------------------------------------------------------------------------------- client-planned path
        [Test]
        public void ClientPlannedPath_SurvivesRollback_AndIsWrittenOnce()
        {
            using var rig = PlayerRig.Create(2, 100);
            rig.ClientPlanner.Dogleg = true; rig.ServerPlanner.Dogleg = true;
            var og = rig.GhostIn(0, 0);
            var cem = rig.Clients[0].EntityManager;
            rig.Sources[0].Click(new int3(15000, 0, 8000));
            rig.Tick(30);
            var header = cem.GetComponentData<ClientPlanHeader>(og);
            Assert.AreEqual(1, header.Writes);
            Assert.AreEqual(3, header.Count);
            Assert.AreEqual(3, cem.GetBuffer<ClientPlanCorner>(og, true).Length);
            rig.Tick(200);
            var after = cem.GetComponentData<ClientPlanHeader>(og);
            Assert.AreEqual(1, after.Writes, "no rollback or snapshot may re-plan the way");
            Assert.AreEqual(3, cem.GetBuffer<ClientPlanCorner>(og, true).Length, "the client-only plan survives every rollback");
            var diag = cem.GetComponentData<PlayerMoveDiag>(og);
            Assert.Greater(diag.Runs, diag.FirstRuns, "rollbacks happened");
            // the server's own corners arrived for the same order
            var plan = cem.GetComponentData<PathPlan>(og);
            Assert.AreEqual(header.Seq, plan.Seq, "the server's plan came back for the click");
            Assert.AreEqual(3, cem.GetBuffer<PathCorner>(og, true).Length);
            Assert.AreEqual(new int3(15000, 0, 8000), rig.OwnerPose().Mm);
        }

        [Test]
        public void ServerCornersOnlyGoToTheOwner()
        {
            using var rig = PlayerRig.Create(2, 0);
            rig.Sources[0].Click(Goal);
            rig.Tick(40);
            Assert.AreEqual(2, rig.Clients[0].EntityManager.GetBuffer<PathCorner>(rig.GhostIn(0, 0), true).Length);
            Assert.AreEqual(0, rig.Clients[1].EntityManager.GetBuffer<PathCorner>(rig.GhostIn(1, 0), true).Length, "observers never get the path");
        }

        // ---------------------------------------------------------------------------------------------- server refusal
        [Test]
        public void ARefusedClick_ServerKeepsStill_AndTheOwnerIsTakenBack_AndStaysThere()
        {
            using var rig = PlayerRig.Create(2, 100);
            rig.ServerPlanner.RefuseAll = true;               // the owner can plan, the server cannot
            rig.Sources[0].Click(Goal);
            rig.Tick(120);
            Assert.AreEqual(0, rig.ServerPose().XMm);
            Assert.AreEqual(0, rig.OwnerPose().XMm, "the owner's prediction is corrected and not started again by the rollback");
            Assert.AreEqual(0, rig.OwnerMove().Flags & MoveFlags.Moving);
            Assert.AreEqual(rig.Get<MoveState>(rig.Clients[0], rig.GhostIn(0, 0)).RefusedSeq, rig.Get<ClientPlanHeader>(rig.Clients[0], rig.GhostIn(0, 0)).Seq);
            // later clicks work again
            rig.ServerPlanner.RefuseAll = false;
            rig.Sources[0].Click(new int3(5000, 0, 0));
            rig.Tick(80);
            Assert.AreEqual(5000, rig.ServerPose().XMm);
            Assert.AreEqual(5000, rig.OwnerPose().XMm);
        }

        // ---------------------------------------------------------------------------------------------- server order
        [Test]
        public void ServerOrder_OverridesTheOwnersWalk()
        {
            using var rig = PlayerRig.Create(2, 100);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(40);
            var target = new int3(0, 0, 15000);
            PlayerEntitiesSetup.IssueOrder(rig.Server.EntityManager, rig.ServerGhosts[0], target);
            rig.Tick(260);
            var sm = rig.ServerMove();
            Assert.AreEqual(target, rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose(), "the owner follows the server's order");
            Assert.AreNotEqual(0, sm.OrderSeq & PlayerLimits.ServerSeqBit, "the last walk was the server's");
            Assert.AreEqual(sm, rig.OwnerMove());
            // a click after the server order walks again (the server order is not replayed)
            rig.Sources[0].Click(new int3(0, 0, 20000));
            rig.Tick(120);
            Assert.AreEqual(new int3(0, 0, 20000), rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
        }

        [Test]
        public void ServerOrderMidWalk_OnlyTheTickOfTheOrderIsMispredicted_AndStateConvergesAfterIt()
        {
            // regression for RandomCommandSequences seed 11: the owner cannot predict a server order, so its trace entry of the order's
            // first tick differs by one step; every other tick, and the final state, must be equal
            using var rig = PlayerRig.Create(2, 100);
            uint t0 = rig.ServerTick;
            rig.Sources[0].Click(new int3(30000, 0, 0));
            rig.Tick(60);
            rig.IssueOrder(0, new int3(-5000, 0, 9000));
            rig.Tick(150);
            var skipped = rig.CompareOwner(0, t0, rig.ServerTick - 1);
            Assert.GreaterOrEqual(skipped.Common, 150, "every tick after the order is compared");
            rig.Tick(150);
            skipped = rig.CompareOwner(0, t0, rig.ServerTick - 1);
            Assert.AreEqual(0, skipped.Mismatch, skipped.FirstDump);
            rig.Unpredictable.Clear(); rig.SkipOrderStartTicks = false;
            var strict = rig.CompareOwner(0, t0, rig.ServerTick - 1);
            Assert.LessOrEqual(strict.Mismatch, 2, "only the order's own tick (and its neighbour) may differ: " + strict.FirstDump);
            Assert.AreEqual(rig.ServerMove(), rig.OwnerMove());
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
        }

        // ---------------------------------------------------------------------------------------------- walk and run
        [Test]
        public void WalkThenRun_RunIsFaster_AndBothWorldsAgree()
        {
            using var rig = PlayerRig.Create(2, 100);
            int Ticks(int3 to, bool run)
            {
                rig.Sources[0].Click(to, run);
                int wait = 0;
                while ((rig.ServerMove().Flags & MoveFlags.Moving) == 0 && wait++ < 80) rig.Tick(1);   // the click is on its way (100 ms)
                Assert.Less(wait, 80, "the server never started the walk");
                int n = 0;
                while ((rig.ServerMove().Flags & MoveFlags.Moving) != 0 && n < 600) { rig.Tick(1); n++; }
                rig.Tick(40);
                Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
                Assert.AreEqual(to, rig.ServerPose().Mm);
                return n;
            }
            int walk = Ticks(new int3(30000, 0, 0), false);
            int run = Ticks(new int3(0, 0, 0), true);
            Log($"walk 30 m = {walk} ticks, run 30 m = {run} ticks");
            Assert.Less(run, walk);
        }

        sealed class FakeRunCost : IRunCost
        {
            public bool Affordable;
            public int Asked, Paid;
            public bool CanPay(Entity ghost, out string reason)
            {
                Asked++;
                reason = Affordable ? null : "Not enough Flux.";
                return Affordable;
            }
            public void Pay(Entity ghost) => Paid++;
        }

        [Test]
        public void ARunningClick_ThePlayerCannotPayFor_IsRefused_AndAPaidOneIsChargedOnce()
        {
            using var rig = PlayerRig.Create(2, 100);
            var cost = new FakeRunCost { Affordable = false };
            var plan = rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>();
            plan.RunCost = cost;
            rig.Sources[0].Click(Goal, true);
            rig.Tick(120);
            Assert.AreEqual(0, rig.ServerPose().XMm, "no Flux: the run does not start");
            Assert.AreEqual(0, rig.OwnerPose().XMm, "and the owner is taken back");
            Assert.AreEqual(0, cost.Paid);
            Assert.AreEqual("Not enough Flux.", plan.LastCostRefusal);
            // a walk costs nothing and is not asked about
            int asked = cost.Asked;
            rig.Sources[0].Click(new int3(5000, 0, 0));
            rig.Tick(80);
            Assert.AreEqual(5000, rig.ServerPose().XMm);
            Assert.AreEqual(asked, cost.Asked, "walking is free");
            // with the Flux the run starts and is paid once
            cost.Affordable = true;
            rig.Sources[0].Click(new int3(15000, 0, 0), true);
            rig.Tick(120);
            Assert.AreEqual(15000, rig.ServerPose().XMm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            Assert.AreEqual(1, cost.Paid, "one destination, one price");
        }

        [Test]
        public void ASpeedChangeMidWalk_ReachesTheOwner_AndBothWorldsEndEqual()
        {
            // what a GM speed, a mount or a new Run rank does: the server writes MoveStats (PlayerMoveStatsRefreshSystem in the game)
            using var rig = PlayerRig.Create(2, 100);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(40);
            var faster = new MoveStats { WalkMmps = 10000, RunMmps = 15000 };
            rig.Server.EntityManager.SetComponentData(rig.ServerGhosts[0], faster);
            rig.Tick(30);
            Assert.AreEqual(faster, rig.Get<MoveStats>(rig.Clients[0], rig.GhostIn(0, 0)), "the owner gets the new speeds");
            Assert.AreEqual(10000, rig.ServerMove().SpeedMmps, "the walk goes on at the new speed");
            rig.Tick(200);
            Assert.AreEqual(new int3(40000, 0, 0), rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            Assert.AreEqual(rig.ServerMove(), rig.OwnerMove());
        }

        // ------------------------------------------------------------------------------------------------ owner's feel bar (30 Oct)
        [Test]
        public void ANewClickMidWalk_IsTakenOnTheNextTick_AndTheServerAgreesWithoutASnap()
        {
            using var rig = PlayerRig.Create(2, 100);
            uint t0 = rig.ServerTick;
            rig.Sources[0].Click(new int3(40000, 0, 0));
            rig.Tick(40);
            ushort first = rig.OwnerMove().OrderSeq;
            Assert.AreNotEqual(0, rig.OwnerMove().Flags & MoveFlags.Moving, "walking to A");
            var goalB = new int3(0, 0, 15000);
            // The click is gathered for the tick the client is predicting this frame (logged). When that tick is still partial
            // the walk cannot start on it yet (moves run on full ticks); it must start on that very tick, i.e. on the first
            // frame in which the tick is fully predicted, never a tick later.
            uint clickTick = 0;
            PlayerClickLog.Sink = l => { if (l.StartsWith("[KnClick] client")) clickTick = uint.Parse(l.Substring(l.LastIndexOf('=') + 1)); };
            rig.Sources[0].Click(goalB);
            int frames = 0;
            try
            {
                do { rig.Tick(1); frames++; }
                while (frames < 4 && (clickTick == 0 || NewestFullTick(rig.Clients[0]) < clickTick));
            }
            finally { PlayerClickLog.Sink = null; }
            Log($"click gathered for tick {clickTick}; fully predicted after {frames} frame(s)");
            Assert.AreNotEqual(0, clickTick, "the click was gathered");
            Assert.LessOrEqual(frames, 2, "the click's tick is fully predicted within one tick of the press");
            var owner = rig.OwnerMove();
            Assert.AreNotEqual(first, owner.OrderSeq, "the owner drops A and walks to B on the click's own tick, no wait for the server");
            var ring = rig.Clients[0].EntityManager.GetBuffer<PlayerMoveTrace>(rig.GhostIn(0, 0), true);
            var ownTrace = ring[(int)(clickTick % (uint)ring.Length)];
            Assert.AreEqual(clickTick, ownTrace.Tick, "the owner traced the click's tick");
            Assert.AreEqual(owner.OrderSeq, ownTrace.OrderSeq, "B started on exactly the tick the click was gathered for");
            Assert.AreEqual(rig.Get<ClientPlanHeader>(rig.Clients[0], rig.GhostIn(0, 0)).Seq, owner.OrderSeq);
            Assert.AreNotEqual(0, owner.Flags & MoveFlags.Moving);
            rig.Tick(260);
            Assert.AreEqual(goalB, rig.ServerPose().Mm, "the server took the same click: its result stands");
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            var trace = rig.CompareOwner(0, t0, rig.ServerTick - 1);
            Assert.AreEqual(0, trace.Mismatch, "owner and server agreed on every tick, so nothing was corrected: " + trace.FirstDump);
        }

        [Test]
        public void AtThreeFramesATick_EveryClickWalks_WhicheverFrameOfItsTickItLandsOn()
        {
            // owner's GUI test 30 Oct (84 fps): a click followed by an empty frame of the same tick reached server and owner with
            // its seq and goal wiped (Netcode keeps the event's count across the frames of a tick, not the plain fields), so most
            // clicks only turned the body. Here each click lands on a different frame of its tick.
            using var rig = PlayerRig.Create(1, 100);
            rig.Tw.FramesPerTick = 3;
            rig.Tick(60);                                   // the client clock settles at the new frame rate
            var lines = new List<string>();
            PlayerClickLog.Sink = l => { lock (lines) lines.Add(l); };
            try
            {
                for (int phase = 0; phase < 3; phase++)
                {
                    rig.Tick(phase);                        // move the click to another frame of its tick
                    var goal = new int3(3000 + 4000 * phase, 0, 2000 * phase);
                    rig.Sources[0].Click(goal);
                    int frames = 0;
                    while (rig.OwnerMove().OrderSeq == 0 || (rig.OwnerMove().Flags & MoveFlags.Moving) == 0 || rig.OwnerMove().OrderSeq != rig.Get<ClientPlanHeader>(rig.Clients[0], rig.GhostIn(0, 0)).Seq)
                    {
                        rig.Tick(1);
                        Assert.Less(++frames, 12, $"phase {phase}: the owner never started the walk. Log: {string.Join(" | ", lines)}");
                    }
                    rig.Tick(3 * 400);
                    Assert.AreEqual(goal, rig.ServerPose().Mm, $"phase {phase}: the server walked the click. Log: {string.Join(" | ", lines)}");
                    Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
                }
            }
            finally { PlayerClickLog.Sink = null; }
            Assert.IsFalse(lines.Exists(l => l.StartsWith("[KnClick] refused")), "no click was refused: " + string.Join(" | ", lines));
        }

        [Test]
        public void WithLostAndLatePackets_EveryClickIsStillTakenWithItsGoal()
        {
            // owner's GUI run 31 Oct: a click whose own tick reached the server late came as an event with seq 0 and was refused
            // ("noseq"); the newest click's fields now ride on every input until the next click
            using var rig = PlayerRig.Create(1, 100, dropInterval: 3);
            rig.Tw.FramesPerTick = 3;
            rig.Tick(90);
            var lines = new List<string>();
            PlayerClickLog.Sink = l => { lock (lines) lines.Add(l); };
            try
            {
                for (int i = 0; i < 6; i++)
                {
                    var goal = new int3(4000 * (i % 3 + 1), 0, i % 2 == 0 ? 3000 : -3000);
                    rig.Sources[0].Click(goal);
                    rig.Tick(3 * 150);
                    Assert.AreEqual(goal, rig.ServerPose().Mm, $"click {i}: the server walked it. Log: {string.Join(" | ", lines)}");
                    // the click's fields ride on every later input: they must never start the ended walk again
                    var ended = rig.ServerMove();
                    Assert.AreEqual(0, ended.Flags & MoveFlags.Moving, $"click {i}: the walk ended");
                    rig.Tick(3 * 30);
                    Assert.AreEqual(ended, rig.ServerMove(), $"click {i}: the carried seq did not restart it");
                    Assert.AreEqual(goal, rig.ServerPose().Mm);
                }
                // a click replaced mid-walk is not taken up again by the next one's carried fields either
                rig.Sources[0].Click(new int3(-20000, 0, 0));
                rig.Tick(3 * 10);
                rig.Sources[0].Click(new int3(0, 0, 9000));
                rig.Tick(3 * 200);
                Assert.AreEqual(new int3(0, 0, 9000), rig.ServerPose().Mm, "the newer click stands; the replaced one never comes back");
                rig.Tick(3 * 30);
                Assert.AreEqual(new int3(0, 0, 9000), rig.ServerPose().Mm);
            }
            finally { PlayerClickLog.Sink = null; }
            Assert.IsFalse(lines.Exists(l => l.StartsWith("[KnClick] refused")), "no click was refused: " + string.Join(" | ", lines));
        }

        /// <summary>The newest tick the client has predicted in full (a partial tick does not move the body).</summary>
        static uint NewestFullTick(World client)
        {
            var nt = KnTestWorld.Singleton<NetworkTime>(client);
            if (!nt.ServerTick.IsValid) return 0;
            uint t = nt.ServerTick.TickIndexForValidTick;
            return nt.IsPartialTick ? t - 1 : t;
        }

        sealed class FakeClickBudget : IClickBudget
        {
            public bool Open;
            public int Asked;
            public bool Allow(Entity ghost) { Asked++; return Open; }
        }

        [Test]
        public void AClickOverTheRateLimit_IsRefusedBeforePlanning_AndTheNextAllowedOneWalks()
        {
            using var rig = PlayerRig.Create(2, 100);
            var budget = new FakeClickBudget { Open = false };
            var plan = rig.Server.GetExistingSystemManaged<PlayerServerPlanSystem>();
            plan.ClickBudget = budget;
            rig.Sources[0].Click(Goal);
            rig.Tick(120);
            Assert.AreEqual(0, rig.ServerPose().XMm, "over the limit: the server does not walk");
            Assert.AreEqual(0, rig.OwnerPose().XMm, "and the owner is taken back");
            Assert.AreEqual(1, plan.OverBudget);
            Assert.AreEqual(1, budget.Asked, "one click, asked once");
            budget.Open = true;
            rig.Sources[0].Click(new int3(5000, 0, 0));
            rig.Tick(80);
            Assert.AreEqual(5000, rig.ServerPose().XMm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
        }

        [Test]
        public void AClientThatClaimsFasterSpeeds_IsCorrectedToTheServersWalk()
        {
            // a tampered client writes its own MoveStats (5x the walk); the server walks at its own and its snapshots win
            using var rig = PlayerRig.Create(2, 100);
            var cheat = new MoveStats { WalkMmps = 25000, RunMmps = 45000 };
            var honest = rig.Get<MoveStats>(rig.Server, rig.ServerGhosts[0]);
            rig.Sources[0].Click(new int3(40000, 0, 0));
            int fastest = 0;
            for (int i = 0; i < 120; i++)
            {
                rig.Clients[0].EntityManager.SetComponentData(rig.GhostIn(0, 0), cheat);
                rig.Tick(1);
                fastest = math.max(fastest, rig.ServerMove().SpeedMmps);
            }
            Assert.LessOrEqual(fastest, honest.WalkMmps, "the server never walked faster than its own speeds");
            rig.Tick(300);
            Assert.AreEqual(honest, rig.Get<MoveStats>(rig.Clients[0], rig.GhostIn(0, 0)), "the snapshots put the server's speeds back");
            Assert.AreEqual(new int3(40000, 0, 0), rig.ServerPose().Mm);
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose(), "the owner ends where the server put it, not where the cheat would");
        }

        [Test]
        public void RunFlag_IsOnTheMoveState_WhileRunning()
        {
            using var rig = PlayerRig.Create(2, 0);
            rig.Sources[0].Click(Goal, true);
            rig.Tick(25);
            Assert.AreNotEqual(0, rig.ServerMove().Flags & MoveFlags.Run);
            Assert.AreEqual(9000, rig.ServerMove().SpeedMmps);
            Assert.AreEqual(9000, rig.Get<MoveState>(rig.Clients[1], rig.GhostIn(1, 0)).SpeedMmps, "observers learn the speed (animation)");
        }

        // ---------------------------------------------------------------------------------------------- property
        [TestCase(11)]
        [TestCase(23)]
        [TestCase(42)]
        [TestCase(77)]
        public void RandomCommandSequences_KeepServerAndOwnerIntegerStateEqual(int seed)
        {
            using var rig = PlayerRig.Create(2, 100);
            var rnd = new System.Random(seed);
            uint t0 = rig.ServerTick;
            int next = 0;
            for (int t = 0; t < 420; t++)
            {
                if (t >= next)
                {
                    int pick = rnd.Next(10);
                    if (pick < 7) rig.Sources[0].Click(new int3(rnd.Next(-15000, 15000), 0, rnd.Next(-15000, 15000)), rnd.Next(3) == 0);
                    else if (pick < 9) rig.Sources[0].Stop();
                    else rig.IssueOrder(0, new int3(rnd.Next(-9000, 9000), 0, rnd.Next(-9000, 9000)));
                    next = t + rnd.Next(5, 40);
                }
                rig.Tick(1);
            }
            rig.Tick(420);                                  // everything settles
            var cmp = rig.CompareOwner(0, t0 + 20, rig.ServerTick - 1);
            Log($"random seed {seed}: common={cmp.Common} mismatch={cmp.Mismatch} first={cmp.FirstMismatchTick}");
            Assert.GreaterOrEqual(cmp.Common, 400);
            Assert.AreEqual(0, cmp.Mismatch, "first difference at tick " + cmp.FirstMismatchTick + ": " + cmp.FirstDump);
            Assert.AreEqual(rig.ServerMove(), rig.OwnerMove());
            Assert.AreEqual(rig.ServerPose(), rig.OwnerPose());
            Assert.AreEqual(rig.ServerPose(), rig.Get<PlayerPose>(rig.Clients[1], rig.GhostIn(1, 0)));
        }

        // ---------------------------------------------------------------------------------------------- budget
        [Test]
        public void Budget_200Players_MovingOnTheServer()
        {
            using var rig = PlayerRig.Create(0, 0, warmupTicks: 0);
            var em = rig.Server.EntityManager;
            var prefab = KnTestWorld.Singleton<PlayerGhostPrefab>(rig.Server).Prefab;
            var ghosts = new Entity[200];
            for (int i = 0; i < ghosts.Length; i++)
                ghosts[i] = PlayerEntitiesSetup.SpawnServer(em, prefab, 0, new int3((i % 20) * 3000, 0, (i / 20) * 3000));
            rig.Tick(5);
            for (int i = 0; i < ghosts.Length; i++)
                PlayerEntitiesSetup.IssueOrder(em, ghosts[i], new int3((i % 20) * 3000 + 60000, 0, (i / 20) * 3000 + 30000));
            rig.Tick(5);
            Assert.AreEqual(200, CountMoving(rig.Server), "all 200 walk");

            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int ticks = 300;
            rig.Tick(ticks);
            sw.Stop();
            double perTick = sw.Elapsed.TotalMilliseconds / ticks;
            Log($"budget: 200 moving players on the server world, {ticks} ticks in {sw.Elapsed.TotalMilliseconds:F0} ms = {perTick:F3} ms per world tick (all server systems, Editor, Burst {(Unity.Burst.BurstCompiler.IsEnabled ? "on" : "off")})");
            Assert.Less(perTick, 8.0, "a world tick with 200 walking players stays well inside the 33 ms tick");
            Assert.Greater(CountMoving(rig.Server), 100, "still walking: 60 m takes more than the measured window");
        }

        static int CountMoving(World w)
        {
            using var q = w.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MoveState>());
            using var arr = q.ToComponentDataArray<MoveState>(Allocator.Temp);
            int n = 0;
            foreach (var m in arr) if ((m.Flags & MoveFlags.Moving) != 0) n++;
            return n;
        }

        // ---------------------------------------------------------------------------------------------- shape of the prefab
        [Test]
        public void TheGhostOwnsTheComponents_TheWalkNeeds()
        {
            using var rig = PlayerRig.Create(2, 0, warmupTicks: 5);
            var e = rig.GhostIn(0, 0);
            var em = rig.Clients[0].EntityManager;
            foreach (var t in new[] { typeof(PlayerInput), typeof(ServerOrder), typeof(MoveState), typeof(PlayerPose), typeof(TeleportSeq),
                                     typeof(PathPlan), typeof(ClientPlanHeader), typeof(MoveStats), typeof(LocalTransform), typeof(GhostOwner) })
                Assert.IsTrue(em.HasComponent(e, ComponentType.ReadOnly(t)), t.Name);
            Assert.IsTrue(em.HasBuffer<PathCorner>(e));
            Assert.IsTrue(em.HasBuffer<ClientPlanCorner>(e));
            Assert.IsTrue(em.IsComponentEnabled<GhostOwnerIsLocal>(e), "the owner's ghost is flagged local");
            Assert.AreEqual(0, rig.Tw.Interesting.Count(l => l.StartsWith("[Error]") || l.StartsWith("[Exception]")),
                "errors in the log: " + string.Join(" | ", rig.Tw.Interesting));
        }
    }
}
