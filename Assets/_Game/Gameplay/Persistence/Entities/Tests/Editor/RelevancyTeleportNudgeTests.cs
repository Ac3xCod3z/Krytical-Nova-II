using NUnit.Framework;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// R3-3b (Master, 15 Nov 2026: "keep teleports showing players at once"): a teleport about to be applied nudges the relevancy fill,
    /// whose signature counts the nudge, so it fills in that tick; with no teleport nothing is nudged (the timed fill stays timed).
    /// </summary>
    public class RelevancyTeleportNudgeTests
    {
        [Test]
        public void ATeleportThisTick_NudgesTheFill_AndNoTeleportDoesNot()
        {
            using var world = new World("kn-relevancy-nudge");
            EntityManager em = world.EntityManager;
            Entity config = em.CreateSingleton(new RelevancyConfig { PlayerRadius = 150f, ObjectRadius = 150f, FillIntervalTicks = 6 });
            Entity body = em.CreateEntity(typeof(PlayerGhostTag), typeof(TeleportRequest), typeof(Simulate));
            em.SetComponentEnabled<TeleportRequest>(body, false);
            SystemHandle nudge = world.CreateSystem<RelevancyTeleportNudgeSystem>();

            nudge.Update(world.Unmanaged);
            Assert.AreEqual(0u, em.GetComponentData<RelevancyConfig>(config).Nudge, "no teleport waiting: the fill keeps its interval");

            em.SetComponentEnabled<TeleportRequest>(body, true);
            nudge.Update(world.Unmanaged);
            Assert.AreEqual(1u, em.GetComponentData<RelevancyConfig>(config).Nudge, "a teleport this tick: the fill runs at once");

            em.SetComponentEnabled<TeleportRequest>(body, false);   // the teleport job switches the request off once applied
            nudge.Update(world.Unmanaged);
            Assert.AreEqual(1u, em.GetComponentData<RelevancyConfig>(config).Nudge);
        }

        [Test]
        public void TheFillsSignature_ChangesWithTheNudge()
        {
            // the fill adds Mark(Nudge, ...) to the signature it compares with the last fill's
            Assert.AreNotEqual(RelevancyFillSystem.Mark(0u, 0x4B4E5633u), RelevancyFillSystem.Mark(1u, 0x4B4E5633u));
        }
    }
}
