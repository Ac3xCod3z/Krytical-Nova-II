using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using KryticalNova.Networking.Entities.Tests;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The owner's drawn target (the pose drawn ahead between full ticks, as <c>PlayerPresentationFollowSystem</c> asks it) along a run at
    /// 28 m/s on plain ground (owner pick P1, 1 Nov: the GUI pop check saw it jump ~1.5 m in one frame, twice). Each full tick is drawn at
    /// four fractions; a step longer than the run makes in that time (+ 5 cm), or a break between a tick's end and the next tick's start,
    /// is a jump. At 0 and 100 ms.
    /// </summary>
    public class PoseAheadContinuityTests
    {
        // owner, 8 Nov 2026: an attack click's walk, overridden by the server's walk-in, stayed "pending" and pulled the drawn body
        // toward the click on every frame (KnYaw: follow input 239-258 around a pose of 260 on every partial tick)
        [Test]
        public void AWalkClick_IsPendingOnlyInItsOwnTick()
        {
            Assert.That(PlayerMovement.ClickPendingAt(4200u, 4200u), Is.True, "its own tick");
            Assert.That(PlayerMovement.ClickPendingAt(4200u, 4201u), Is.False, "a tick later: taken or overridden");
            Assert.That(PlayerMovement.ClickPendingAt(0u, 4201u), Is.True, "not stamped: as before");
            Assert.That(PlayerMovement.ClickPendingAt(4200u, 0u), Is.True, "no clock: as before");
        }

        [TestCase(0)]
        [TestCase(100)]
        public void TheOwnersDrawnTarget_NeverJumps_ThroughAFullSpeedRun(int delayMs)
        {
            using var rig = PlayerRig.Create(1, delayMs);
            var stats = rig.Get<MoveStats>(rig.Server, rig.ServerGhosts[0]);
            stats.RunMmps = 28000;
            rig.Server.EntityManager.SetComponentData(rig.ServerGhosts[0], stats);
            rig.Tick(10);
            int tickMicros = PlayerMovement.TickMicros(30);
            var cfg = PlayerMoveSettings.Default;
            rig.Sources[0].Click(new int3(40000, 0, 0), run: true);

            World client = rig.Clients[0];
            Entity ghost = rig.GhostIn(0, 0);
            var em = client.EntityManager;
            float3 last = default;
            bool has = false;
            float worst = 0f;
            string where = "";
            int jumps = 0;
            for (int t = 0; t < 120; t++)
            {
                rig.Tick();
                var move = em.GetComponentData<MoveState>(ghost);
                var pose = em.GetComponentData<PlayerPose>(ghost);
                var plan = em.GetComponentData<PathPlan>(ghost);
                var header = em.GetComponentData<ClientPlanHeader>(ghost);
                var serverCorners = em.GetBuffer<PathCorner>(ghost).Reinterpret<int3>().AsNativeArray();
                var clientCorners = em.GetBuffer<ClientPlanCorner>(ghost).Reinterpret<int3>().AsNativeArray();
                var vault = em.GetComponentData<VaultPlan>(ghost);
                var clientVault = em.GetComponentData<ClientVaultPlan>(ghost).Plan;
                for (int q = 0; q < 4; q++)
                {
                    int elapsed = tickMicros * q / 4;
                    float3 p;
                    var input = em.GetComponentData<PlayerInput>(ghost);
                    var stats0 = em.GetComponentData<MoveStats>(ghost);
                    if (PlayerMovement.PoseAheadWithClick(in move, in pose, in input, in stats0, in plan, serverCorners, in header, clientCorners, in vault,
                            in clientVault, in cfg, tickMicros, elapsed, out int3 ahead, out _))
                        p = new float3(ahead.x, ahead.y, ahead.z) * 0.001f;
                    else p = new float3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                    if (has)
                    {
                        float step = math.distance(p, last);
                        float allowed = 28f * (tickMicros / 4) * 1e-6f + 0.05f;
                        if (step > allowed)
                        {
                            jumps++;
                            if (step - allowed > worst)
                            {
                                worst = step - allowed;
                                where = $"tick {t} quarter {q}: {step:F2} m (allowed {allowed:F2}); progress {move.ProgressMm} mm, order {move.OrderSeq}, " +
                                    $"server corners for {plan.Seq} ({plan.Count}), own for {header.Seq} ({header.Count}), speed {move.SpeedMmps}";
                            }
                        }
                    }
                    last = p;
                    has = true;
                }
            }
            Assert.Greater(rig.OwnerPose().XMm, 30000, "the run happened");
            Assert.AreEqual(0, jumps, $"the drawn target jumped {jumps} time(s); worst {worst:F2} m over: {where}");
        }

        /// <summary>
        /// The same run drawn as the GUI draws it (gui-tree-2, 1 Nov: still one "path jumped 1.05 m in 15 ms" on a plain 28 m/s run after
        /// the press fix): several frames a tick, of uneven length, the frame's own place in its tick from Netcode's clock
        /// (<c>ServerTickFraction</c>, as <c>PlayerPresentationFollowSystem</c> reads it), the click partway through a tick, then a second
        /// run from standing whose click comes on a frame that completes its tick (fraction 1: before the fix that frame ran the whole tick,
        /// 0.93 m). The target may move at most the run's speed over the frame (+ 25 %, + 5 cm). At 0 and 100 ms.
        /// </summary>
        [TestCase(0, false)]
        [TestCase(100, false)]
        [TestCase(0, true)]
        [TestCase(100, true)]
        public void TheOwnersDrawnTarget_NeverJumps_AtTheGuisFrames(int delayMs, bool uneven)
        {
            using var rig = PlayerRig.Create(1, delayMs);
            var stats = rig.Get<MoveStats>(rig.Server, rig.ServerGhosts[0]);
            stats.RunMmps = 28000;
            rig.Server.EntityManager.SetComponentData(rig.ServerGhosts[0], stats);
            int[] pattern = uneven ? new[] { 2, 5, 3, 7, 4, 2, 6 } : new[] { 4 };
            rig.Tw.FramesPerTick = pattern[0];
            rig.Tick(90);                                   // the client clock settles at the frame rate
            rig.Tick(1);                                    // the press partway through its tick
            int tickMicros = PlayerMovement.TickMicros(30);
            var cfg = PlayerMoveSettings.Default;

            World client = rig.Clients[0];
            Entity ghost = rig.GhostIn(0, 0);
            var em = client.EntityManager;
            float3 last = default;
            bool has = false;
            float worst = 0f;
            string where = "";
            int jumps = 0;
            var trail = new System.Collections.Generic.List<string>();
            for (int f = 0; f < 4 * 200; f++)
            {
                if (f == 0) rig.Sources[0].Click(new int3(15000, 0, 0), run: true);
                if (f == 4 * 60) rig.Sources[0].Click(new int3(15000, 0, 9000), run: true);
                int frames = pattern[f % pattern.Length];
                rig.Tw.FramesPerTick = frames;
                rig.Tick(1);
                float dt = 1f / 30f / frames;

                var nt = KnTestWorld.Singleton<NetworkTime>(client);
                int elapsed = nt.ServerTickFraction > 0f && nt.ServerTickFraction < 1f ? (int)(nt.ServerTickFraction * tickMicros) : 0;
                var move = em.GetComponentData<MoveState>(ghost);
                var pose = em.GetComponentData<PlayerPose>(ghost);
                var plan = em.GetComponentData<PathPlan>(ghost);
                var header = em.GetComponentData<ClientPlanHeader>(ghost);
                var input = em.GetComponentData<PlayerInput>(ghost);
                var serverCorners = em.GetBuffer<PathCorner>(ghost).Reinterpret<int3>().AsNativeArray();
                var clientCorners = em.GetBuffer<ClientPlanCorner>(ghost).Reinterpret<int3>().AsNativeArray();
                var vault = em.GetComponentData<VaultPlan>(ghost);
                var clientVault = em.GetComponentData<ClientVaultPlan>(ghost).Plan;
                float3 p;
                bool ahead = PlayerMovement.PoseAheadWithClick(in move, in pose, in input, em.GetComponentData<MoveStats>(ghost), in plan, serverCorners,
                    in header, clientCorners, in vault, in clientVault, in cfg, tickMicros, elapsed, out int3 aheadMm, out _);
                p = ahead ? new float3(aheadMm.x, aheadMm.y, aheadMm.z) * 0.001f : em.GetComponentData<Unity.Transforms.LocalTransform>(ghost).Position;
                string line = $"frame {f} ({frames}/tick) tick {nt.ServerTick.TickIndexForValidTick} frac {nt.ServerTickFraction:F3}: at {p.x:F2},{p.z:F2} " +
                    $"{(ahead ? "ahead" : "ghost")}; progress {move.ProgressMm} speed {move.SpeedMmps} delay {move.DelayMicros}, order {move.OrderSeq}, " +
                    $"click {input.ClickSeq} press {input.PressPermille}, server corners {plan.Seq}/{plan.Count}, own {header.Seq}/{header.Count}";
                trail.Add(line);
                if (trail.Count > 6) trail.RemoveAt(0);
                if (has)
                {
                    float step = math.distance(p, last);
                    float allowed = 28f * dt * 1.25f + 0.05f;
                    if (step > allowed)
                    {
                        jumps++;
                        if (step - allowed > worst)
                        {
                            worst = step - allowed;
                            where = $"{step:F2} m in {dt * 1000f:F0} ms (allowed {allowed:F2}):\n  " + string.Join("\n  ", trail);
                        }
                    }
                }
                last = p;
                has = true;
            }
            Assert.AreEqual(new int3(15000, 0, 9000), rig.OwnerPose().Mm, "the runs happened");
            Assert.AreEqual(0, jumps, $"the drawn target jumped {jumps} time(s); worst {worst:F2} m over, {where}");
        }
    }
}
