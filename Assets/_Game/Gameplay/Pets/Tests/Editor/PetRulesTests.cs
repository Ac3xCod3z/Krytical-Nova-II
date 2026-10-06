using KryticalNova.Items;
using KryticalNova.Pets;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The numbers of _Docs/Design/12_pet_evolution_system.md, checked against the rules.</summary>
    public sealed class PetRulesTests
    {
        private static readonly PetTuning Tuning = PetTuning.Default;

        private static PetItemState At(float growthPercent) => PetRules.WithGrowth(default, growthPercent, Tuning);

        [Test]
        public void ANewPet_HasNoXpAndAFullBelly_AndIsAllZeroes()
        {
            var fresh = PetItemState.Unpack(0);
            Assert.AreEqual(0f, fresh.Xp);
            Assert.AreEqual(100f, fresh.HungerPercent);
            Assert.IsTrue(fresh.IsFed);
            Assert.AreEqual(0UL, fresh.Pack(), "a plain new pet item needs no data");
        }

        [Test]
        public void State_RoundTripsThroughTheItem()
        {
            var state = new PetItemState(7_654_321, 43_210);
            var stack = new ItemStack(8001, 1, state.Pack());
            Assert.AreEqual(state, PetItemState.Of(stack));
            Assert.AreEqual(76_543.21f, state.Xp, 0.01f);
            Assert.AreEqual(56.79f, state.HungerPercent, 0.001f);
        }

        [Test]
        public void Growth_TakesOneHundredThousandXp_AThousandPerPercent()
        {
            Assert.AreEqual(100_000u * PetItemState.XpUnitsPerXp, Tuning.XpToFull);
            var state = PetRules.AddXp(default, 999 * PetItemState.XpUnitsPerXp, Tuning);
            Assert.AreEqual(0, PetRules.GrowthPercent(state, Tuning));
            state = PetRules.AddXp(state, 1 * PetItemState.XpUnitsPerXp, Tuning);
            Assert.AreEqual(1, PetRules.GrowthPercent(state, Tuning));
            state = PetRules.AddXp(state, 500_000 * PetItemState.XpUnitsPerXp, Tuning);
            Assert.AreEqual(100, PetRules.GrowthPercent(state, Tuning), "the meter stops at full");
            Assert.AreEqual(Tuning.XpToFull, state.XpUnits);
        }

        [Test]
        public void AKill_IsWorthFourHundredthsOfAnXp_SoFullGrowthIsTwoAndAHalfMillionKills()
        {
            Assert.AreEqual(4u, Tuning.KillXp);
            Assert.AreEqual(2_500_000u, Tuning.XpToFull / Tuning.KillXp);

            // One kill every 3 seconds is 50 XP an hour: about 2,000 hours alone.
            double hours = (Tuning.XpToFull / (double)Tuning.KillXp) * 3.0 / 3600.0;
            Assert.AreEqual(2083.3, hours, 0.1);
        }

        [Test]
        public void EventXp_IsFiftyForAWinAndTwentyFiveForALoss()
        {
            Assert.AreEqual(50u * PetItemState.XpUnitsPerXp, Tuning.EventWinXp);
            Assert.AreEqual(25u * PetItemState.XpUnitsPerXp, Tuning.EventLossXp);
        }

        [TestCase(0, 0)]
        [TestCase(24, 0)]
        [TestCase(25, 1)]
        [TestCase(49, 1)]
        [TestCase(50, 2)]
        [TestCase(75, 3)]
        [TestCase(99, 3)]
        [TestCase(100, 4)]
        public void Pets_EvolveAtTwentyFiveFiftySeventyFiveAndAHundred(int growth, int stage)
        {
            Assert.AreEqual(stage, PetRules.Stage(growth));
            Assert.AreEqual(growth, PetRules.GrowthPercent(At(growth), Tuning));
        }

        // The stat table of the doc: per 1 %, and at each evolution point.
        [TestCase(2200f, 22f, 550f, 1100f, 1650f, 2200f, TestName = "Tidecaller Defense / Drone Core Health")]
        [TestCase(1100f, 11f, 275f, 550f, 825f, 1100f, TestName = "Kitsune Spirit Attack")]
        [TestCase(2000f, 20f, 500f, 1000f, 1500f, 2000f, TestName = "Emberwing Flux")]
        public void Stat_PaysOutOneHundredthOfTheMaximumPerPercent(float max, float perPercent, float at25, float at50, float at75, float at100)
        {
            Assert.AreEqual(0f, PetRules.StatBonus(max, 0, true));
            Assert.AreEqual(perPercent, PetRules.StatBonus(max, 1, true), 0.0001f);
            Assert.AreEqual(at25, PetRules.StatBonus(max, 25, true), 0.0001f);
            Assert.AreEqual(at50, PetRules.StatBonus(max, 50, true), 0.0001f);
            Assert.AreEqual(at75, PetRules.StatBonus(max, 75, true), 0.0001f);
            Assert.AreEqual(at100, PetRules.StatBonus(max, 100, true), 0.0001f);
            Assert.AreEqual(at100, PetRules.StatBonus(max, 250, true), 0.0001f, "growth past full gives nothing more");
        }

        [Test]
        public void Hunger_DrainsThreePercentAnHour_AboutThirtyThreeHoursFromFullToEmpty()
        {
            double carry = 0.0;
            var state = PetRules.Drain(default, 3600.0, Tuning, ref carry);
            Assert.AreEqual(97f, state.HungerPercent, 0.001f);

            carry = 0.0;
            state = PetRules.Drain(default, 33.0 * 3600.0, Tuning, ref carry);
            Assert.IsTrue(state.IsFed, "33 hours of play is not quite enough to empty it");
            state = PetRules.Drain(state, 0.34 * 3600.0, Tuning, ref carry);
            Assert.IsFalse(state.IsFed);
            Assert.AreEqual(0f, state.HungerPercent);
        }

        [Test]
        public void Hunger_TickedInSmallSteps_LosesNothing()
        {
            double carry = 0.0;
            PetItemState state = default;
            for (int frame = 0; frame < 60 * 60 * 30; frame++) // one hour at 30 frames a second
                state = PetRules.Drain(state, 1.0 / 30.0, Tuning, ref carry);
            Assert.AreEqual(97f, state.HungerPercent, 0.002f);
        }

        [Test]
        public void AStarvingPet_GivesNothingAndEarnsNothing_UntilItIsFed()
        {
            var starving = PetRules.WithHunger(At(50), 0f);
            Assert.IsFalse(starving.IsFed);
            Assert.AreEqual(0f, PetRules.StatBonus(2200f, PetRules.GrowthPercent(starving, Tuning), starving.IsFed));
            Assert.AreEqual(starving, PetRules.AddXp(starving, Tuning.EventWinXp, Tuning), "no XP while starving");
            Assert.AreEqual(50, PetRules.GrowthPercent(starving, Tuning), "growth already earned is kept");

            var fed = PetRules.Feed(starving);
            Assert.AreEqual(100f, fed.HungerPercent);
            Assert.AreEqual(1100f, PetRules.StatBonus(2200f, PetRules.GrowthPercent(fed, Tuning), fed.IsFed));
            Assert.AreNotEqual(fed, PetRules.AddXp(fed, Tuning.KillXp, Tuning));
        }

        [Test]
        public void HungerCanBeSetOutright()
        {
            Assert.AreEqual(20f, PetRules.WithHunger(default, 20f).HungerPercent, 0.0001f);
            Assert.AreEqual(100f, PetRules.WithHunger(default, 250f).HungerPercent);
            Assert.AreEqual(0f, PetRules.WithHunger(default, -5f).HungerPercent);
        }
    }
}
