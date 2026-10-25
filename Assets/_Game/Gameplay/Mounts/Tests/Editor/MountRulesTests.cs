using KryticalNova.Mounts;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The mount rules (owner, 25 Oct 2026): pet-like growth and hunger, three stages, two stats, riding speed.</summary>
    public sealed class MountRulesTests
    {
        private static readonly MountTuning Tuning = MountTuning.Default;

        private static MountState AtGrowth(float percent) => MountRules.WithGrowth(default, percent, Tuning);

        [Test]
        public void ThreeStages_At0_50_100()
        {
            Assert.AreEqual(0, MountRules.Stage(0));
            Assert.AreEqual(0, MountRules.Stage(49));
            Assert.AreEqual(1, MountRules.Stage(50));
            Assert.AreEqual(1, MountRules.Stage(99));
            Assert.AreEqual(2, MountRules.Stage(100));
            Assert.AreEqual(50, MountRules.StageStart(1));
            Assert.AreEqual(100, MountRules.StageStart(2));
        }

        [Test]
        public void Growth_FillsLikeAPet()
        {
            Assert.AreEqual(0, MountRules.GrowthPercent(default, Tuning));
            Assert.AreEqual(50, MountRules.GrowthPercent(AtGrowth(50f), Tuning));
            // A kill is worth 0.04 EXP and 100,000 EXP fills the meter, as for a pet.
            MountState one = MountRules.AddXp(default, Tuning.KillXp, Tuning);
            Assert.AreEqual(0.04f, one.Xp, 0.0001f);
            MountState full = MountRules.AddXp(AtGrowth(100f), Tuning.KillXp, Tuning);
            Assert.AreEqual(100, MountRules.GrowthPercent(full, Tuning));
        }

        [Test]
        public void StarvingMount_EarnsNothing_GivesNothing_RidesSlowest()
        {
            MountState starving = MountRules.WithHunger(AtGrowth(60f), 0f);
            Assert.IsFalse(starving.IsFed);
            Assert.AreEqual(starving, MountRules.AddXp(starving, 1000, Tuning));
            Assert.AreEqual(0f, MountRules.StatBonus(60f, 60, starving.IsFed));
            Assert.AreEqual(20f, MountRules.Speed(20f, 34f, 60, starving.IsFed), 0.0001f);
            Assert.IsTrue(MountRules.Feed(starving).IsFed);
        }

        [Test]
        public void Stats_PayOneHundredthPerPercent()
        {
            Assert.AreEqual(30f, MountRules.StatBonus(60f, 50, true), 0.0001f);
            Assert.AreEqual(300f, MountRules.StatBonus(300f, 100, true), 0.0001f);
        }

        [Test]
        public void RunSpeed_TopsOutAt34()
        {
            Assert.AreEqual(20f, MountRules.Speed(20f, 34f, 0, true), 0.0001f);
            Assert.AreEqual(27f, MountRules.Speed(20f, 34f, 50, true), 0.0001f);
            Assert.AreEqual(34f, MountRules.Speed(20f, 34f, 100, true), 0.0001f);
            Assert.AreEqual(34f, MountRules.Speed(20f, 34f, 250, true), 0.0001f);
        }

        [Test]
        public void Hunger_DrainsInWholeUnits_WithCarry()
        {
            double carry = 0;
            MountState state = default;
            // 0.05 % a minute: one minute of riding in sixty one-second steps loses 50 units, nothing lost to rounding.
            for (int i = 0; i < 60; i++) state = MountRules.Drain(state, 1.0, Tuning, ref carry);
            Assert.AreEqual(50u, state.HungerDeficit);
        }

        [Test]
        public void Book_KeepsEachBoxApart()
        {
            var book = new MountBook();
            book.Set(2, true, new MountState(500, 7));
            Assert.IsTrue(book.Owns(2));
            Assert.IsFalse(book.Owns(1));
            Assert.AreEqual(500u, book.Get(2).XpUnits);
            Assert.AreEqual(7u, book.Get(2).HungerDeficit);
            book.Set(2, false, default);
            Assert.IsFalse(book.Owns(2));
        }
    }
}
