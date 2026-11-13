using KryticalNova.Player.Entities;
using NUnit.Framework;
using Unity.Entities;

namespace KryticalNova.Combat.Entities.Tests
{
    /// <summary>Build 17 item 11.2 step 2: the server's dodge ring and who counts as attacking (the rule itself: DodgeRulesTests).</summary>
    public sealed class DodgeWiringTests
    {
        [Test]
        public void TheDodgeRing_NumbersEachDodge_AndKeepsTheNewestFour()
        {
            using var world = new World("dodge ring");
            EntityManager em = world.EntityManager;
            Entity e = em.CreateEntity();
            em.AddBuffer<DodgeEvent>(e);
            var seq = new DodgeSeq();
            for (int i = 0; i < 6; i++)
                DodgeRing.Append(em.GetBuffer<DodgeEvent>(e), ref seq, new DodgeEvent { Tick = (uint)(100 + i), Move = (byte)DodgeMove.SidestepRight });
            DynamicBuffer<DodgeEvent> ring = em.GetBuffer<DodgeEvent>(e);
            Assert.AreEqual(DodgeRing.Capacity, ring.Length, "the newest four");
            Assert.AreEqual(3u, ring[0].Seq, "the oldest two rolled out");
            Assert.AreEqual(6u, DodgeRing.Newest(ring));
            Assert.AreEqual(105u, ring[ring.Length - 1].Tick);
        }

        [Test]
        public void APlayer_IsAttacking_WithAComboStepOrATarget_AndNotWhenIdle()
        {
            Assert.IsFalse(HostileStrikeServerSystem.IsAttacking(new CombatState()), "idle: dodges");
            Assert.IsTrue(HostileStrikeServerSystem.IsAttacking(new CombatState { Step = 2 }), "mid-combo: the miss just misses");
            Assert.IsTrue(HostileStrikeServerSystem.IsAttacking(new CombatState { TargetGhostId = 7 }), "attacking a target");
        }

        [Test]
        public void TheCounter_NeedsAPlayerAndAHostile()
        {
            Assert.IsFalse(HostileStrikeServerSystem.Counter(null, null, 1.25f));
        }
    }
}
