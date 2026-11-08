using NUnit.Framework;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// Cadence item x6 (7 Nov 2026): the GUI Cadence probe teleported the player 6 m from the Strike Dummy (TeleportRequest, as every
    /// "Play DOTS here" try does) and its attack (the same BodyClicks.Attack the real second click sends) never struck, while the
    /// owner's own double-clicks did. This holds the server's walk in and first strike after a teleport against the same attack
    /// without one: both must strike (NGO StartAttackTarget walks in from anywhere).
    /// </summary>
    public sealed class AttackAfterTeleportTests
    {
        [OneTimeSetUp] public void OneTimeSetUp() => PlayerTestAssets.Ensure();
        [OneTimeTearDown] public void OneTimeTearDown() => PlayerTestAssets.Cleanup();

        [Test]
        public void AnAttackSixMetresOff_Strikes_WithoutATeleport() => Strikes(false);

        [Test]
        public void AnAttackSixMetresOff_Strikes_AfterATeleport() => Strikes(true);

        [Test]
        public void TheWalkIn_RunsWhenRunIsSelected_AsNgosAgentSpeed_AndTheOwnerPredictsIt() => WalkIn(true);

        [Test]
        public void TheWalkIn_WalksWhenRunIsNotSelected() => WalkIn(false);

        static void WalkIn(bool run)
        {
            // item x5 (owner's 7 Nov test: the camera jumped on the walk in with Run selected): NGO's walk in is the agent at
            // movementSkills.CurrentSpeed (PointAndClickPlayer:2128), so with Run selected it runs, free of Chi
            using var rig = PlayerRig.Create(2, 100, new[] { new int3(0, 0, 0), new int3(0, 0, 12000) });
            var em = rig.Server.EntityManager;
            MoveRanks ranks = em.GetComponentData<MoveRanks>(rig.ServerGhosts[0]);
            ranks.Run = 1;
            ranks.Selected = run ? MeleeApproachServerSystem.RunSkill : (byte)0;
            em.SetComponentData(rig.ServerGhosts[0], ranks);
            rig.Tick(5);
            uint from = rig.ServerTick;
            int target = rig.Get<GhostInstance>(rig.Server, rig.ServerGhosts[1]).ghostId;
            rig.Sources[0].Attack(target);
            int orders = 0, runs = 0;
            ushort lastSeq = rig.Get<ServerOrder>(rig.Server, rig.ServerGhosts[0]).Seq;
            for (int i = 0; i < 240; i++)
            {
                rig.Tick();
                ServerOrder o = rig.Get<ServerOrder>(rig.Server, rig.ServerGhosts[0]);
                if (o.Seq != lastSeq) { orders++; if ((o.Flags & OrderFlags.RunPaid) != 0) runs++; lastSeq = o.Seq; }
                if (rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]).Step >= 1) break;
            }
            Assert.Greater(orders, 0, "the server walked in");
            Assert.AreEqual(run ? orders : 0, runs, run ? "every walk-in order runs" : "no walk-in order runs");
            Assert.GreaterOrEqual(rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]).Step, 1, "and struck");
            PlayerRig.TraceCompare c = rig.CompareOwner(0, from, rig.ServerTick);
            Debug.Log($"[KnAttackTp] walk in run {run}: {orders} orders, {runs} at run speed; owner trace common {c.Common} mismatch {c.Mismatch} first {c.FirstDump}");
            Assert.AreEqual(0, c.Mismatch, "the owner predicted the walk in tick for tick: " + c.FirstDump);
        }

        static void Strikes(bool teleport)
        {
            using var rig = PlayerRig.Create(2, 100, new[] { new int3(0, 0, 0), new int3(0, 0, 20000) });
            if (teleport)
            {
                rig.Teleport(0, new int3(0, 0, 14000));
                rig.Tick(60);   // the probe waits 2 s
            }
            else rig.IssueOrder(0, new int3(0, 0, 14000));
            if (!teleport) rig.Tick(400);
            int target = rig.Get<GhostInstance>(rig.Server, rig.ServerGhosts[1]).ghostId;
            float3 at = PlayerRig.M(rig.ServerPose(0));
            rig.Sources[0].Attack(target);
            string seen = "never";
            for (int i = 0; i < 240; i++)
            {
                rig.Tick();
                CombatState s = rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]);
                if (s.Step >= 1) { seen = "loop " + i; break; }
                if (i % 30 == 29)
                    Debug.Log($"[KnAttackTp] teleport {teleport} loop {i}: target {s.TargetGhostId} step {s.Step} server at {PlayerRig.M(rig.ServerPose(0))}");
            }
            CombatState end = rig.Get<CombatState>(rig.Server, rig.ServerGhosts[0]);
            Debug.Log($"[KnAttackTp] teleport {teleport}: from {at}, strike 1 at {seen}, server target {end.TargetGhostId}, at {PlayerRig.M(rig.ServerPose(0))}");
            Assert.AreNotEqual("never", seen, $"teleport {teleport}: the server walked in and struck (from {at})");
        }
    }
}
