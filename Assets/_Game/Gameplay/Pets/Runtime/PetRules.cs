using System;

namespace KryticalNova.Pets
{
    /// <summary>The numbers the pet rules run on, in whole units (see <see cref="PetItemState"/>). Built from <c>PetSettings</c>.</summary>
    public readonly struct PetTuning
    {
        /// <summary>XP units that fill the growth meter (100,000 XP by design).</summary>
        public readonly uint XpToFull;
        public readonly uint KillXp, EventWinXp, EventLossXp;
        /// <summary>Hunger units lost per second of play (0.05 % a minute by design).</summary>
        public readonly double HungerPerSecond;

        public PetTuning(float xpToFull, float killXp, float eventWinXp, float eventLossXp, float hungerPercentPerMinute)
        {
            XpToFull = Math.Max(PetRules.GrowthSteps, Units(xpToFull));
            KillXp = Units(killXp);
            EventWinXp = Units(eventWinXp);
            EventLossXp = Units(eventLossXp);
            HungerPerSecond = Math.Max(0.0, hungerPercentPerMinute) * (PetItemState.FullHungerUnits / 100.0) / 60.0;
        }

        /// <summary>The design values of <c>12_pet_evolution_system.md</c>.</summary>
        public static PetTuning Default => new PetTuning(100_000f, 0.04f, 50f, 25f, 0.05f);

        private static uint Units(float xp) => (uint)Math.Round(Math.Max(0f, xp) * PetItemState.XpUnitsPerXp);
    }

    /// <summary>
    /// The pet rules, free of networking so they can be tested. A pet grows one stat with a 0–100 % growth meter:
    /// every 1 % pays out 1/100 of the stat's maximum, so only a pet at 100 % gives all of it. It changes to its
    /// next look at 25, 50, 75 and 100 %. A pet at 0 % hunger gives nothing and earns nothing until it is fed.
    /// </summary>
    public static class PetRules
    {
        public const int GrowthSteps = 100;
        /// <summary>Looks a pet has: the base one and one per evolution.</summary>
        public const int StageCount = 5;
        public const int LastStage = StageCount - 1;
        /// <summary>Growth points between two evolutions.</summary>
        public const int GrowthPerStage = GrowthSteps / LastStage;

        /// <summary>Whole percent of growth, 0–100.</summary>
        public static int GrowthPercent(in PetItemState state, in PetTuning tuning) =>
            (int)Math.Min(GrowthSteps, (ulong)state.XpUnits * GrowthSteps / tuning.XpToFull);

        /// <summary>Growth with its fraction, 0–100, for the meter.</summary>
        public static float GrowthExact(in PetItemState state, in PetTuning tuning) =>
            (float)Math.Min(GrowthSteps, state.XpUnits * (double)GrowthSteps / tuning.XpToFull);

        /// <summary>0 (base) to 4 (fully evolved).</summary>
        public static int Stage(int growthPercent) => Math.Min(LastStage, Math.Max(0, growthPercent) / GrowthPerStage);

        /// <summary>The growth percent at which a stage begins (stage 1 at 25, stage 4 at 100).</summary>
        public static int StageStart(int stage) => Math.Min(GrowthSteps, Math.Max(0, stage) * GrowthPerStage);

        /// <summary>The stat the owner gets from a pet with <paramref name="maxValue"/> at 100 %.</summary>
        public static float StatBonus(float maxValue, int growthPercent, bool fed) =>
            fed ? maxValue * Math.Min(GrowthSteps, Math.Max(0, growthPercent)) / GrowthSteps : 0f;

        /// <summary>Adds XP units. A starving pet earns nothing; the meter stops at full.</summary>
        public static PetItemState AddXp(in PetItemState state, uint xpUnits, in PetTuning tuning)
        {
            if (!state.IsFed || xpUnits == 0) return state;
            ulong total = Math.Min(tuning.XpToFull, (ulong)state.XpUnits + xpUnits);
            return state.WithXp((uint)total);
        }

        /// <summary>Sets growth outright (GM): the XP at which that percent begins.</summary>
        public static PetItemState WithGrowth(in PetItemState state, float growthPercent, in PetTuning tuning)
        {
            double clamped = Math.Min(GrowthSteps, Math.Max(0.0, growthPercent));
            return state.WithXp((uint)Math.Ceiling(tuning.XpToFull * clamped / GrowthSteps));
        }

        /// <summary>
        /// Hunger over <paramref name="seconds"/> of play. Only whole units are taken from the meter; the rest is
        /// handed back in <paramref name="carry"/> for the next call, so slow ticks lose nothing.
        /// </summary>
        public static PetItemState Drain(in PetItemState state, double seconds, in PetTuning tuning, ref double carry)
        {
            if (seconds <= 0.0 || !state.IsFed) return state;
            carry += seconds * tuning.HungerPerSecond;
            if (carry < 1.0) return state;
            uint whole = (uint)Math.Min(carry, PetItemState.FullHungerUnits);
            carry -= whole;
            return state.WithHungerDeficit(Math.Min(PetItemState.FullHungerUnits, state.HungerDeficit + whole));
        }

        /// <summary>Fed back to 100 %.</summary>
        public static PetItemState Feed(in PetItemState state) => state.WithHungerDeficit(0);

        /// <summary>Sets hunger outright (GM), 0–100.</summary>
        public static PetItemState WithHunger(in PetItemState state, float hungerPercent)
        {
            double clamped = Math.Min(100.0, Math.Max(0.0, hungerPercent));
            return state.WithHungerDeficit((uint)Math.Round(PetItemState.FullHungerUnits * (100.0 - clamped) / 100.0));
        }
    }
}
