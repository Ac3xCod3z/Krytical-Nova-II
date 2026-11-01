using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

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
    }
}
