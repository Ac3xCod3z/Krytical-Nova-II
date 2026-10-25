using System;

namespace KryticalNova.Mounts
{
    /// <summary>The numbers the mount rules run on, in whole units (see <see cref="MountState"/>). Built from <c>MountSettings</c>.</summary>
    public readonly struct MountTuning
    {
        /// <summary>EXP units that fill the growth meter.</summary>
        public readonly uint XpToFull;
        public readonly uint KillXp;
        /// <summary>Hunger units lost per second of riding.</summary>
        public readonly double HungerPerSecond;

        public MountTuning(float xpToFull, float killXp, float hungerPercentPerMinute)
        {
            XpToFull = Math.Max(MountRules.GrowthSteps, Units(xpToFull));
            KillXp = Units(killXp);
            HungerPerSecond = Math.Max(0.0, hungerPercentPerMinute) * (MountState.FullHungerUnits / 100.0) / 60.0;
        }

        /// <summary>The pets' numbers (owner, 25 Oct 2026: a mount earns and eats as a pet does).</summary>
        public static MountTuning Default => new MountTuning(100_000f, 0.04f, 0.05f);

        private static uint Units(float xp) => (uint)Math.Round(Math.Max(0f, xp) * MountState.XpUnitsPerXp);
    }

    /// <summary>
    /// The mount rules, free of networking so they can be tested. A mount grows with a 0–100 % meter as a pet does,
    /// from the EXP of its owner's Hostile kills while it is the selected mount, and gets hungry while it is ridden.
    /// It has three stages, one per model (owner, 25 Oct 2026): it evolves at 50 % and at 100 %. Each of its two stats
    /// pays 1/100 of its maximum per 1 % of growth, and its riding speed grows from the low end to the high end of its
    /// range. A mount at 0 % hunger gives no stat, earns nothing and rides at its slowest until it is fed.
    /// </summary>
    public static class MountRules
    {
        /// <summary>Boxes in the Mount window: the four mounts of the game.</summary>
        public const int Slots = 4;
        public const int GrowthSteps = 100;
        public const int StageCount = 3;
        public const int LastStage = StageCount - 1;
        /// <summary>Growth points between two evolutions.</summary>
        public const int GrowthPerStage = GrowthSteps / LastStage;

        /// <summary>Whole percent of growth, 0–100.</summary>
        public static int GrowthPercent(in MountState state, in MountTuning tuning) =>
            (int)Math.Min(GrowthSteps, (ulong)state.XpUnits * GrowthSteps / tuning.XpToFull);

        /// <summary>Growth with its fraction, 0–100, for the meter.</summary>
        public static float GrowthExact(in MountState state, in MountTuning tuning) =>
            (float)Math.Min(GrowthSteps, state.XpUnits * (double)GrowthSteps / tuning.XpToFull);

        /// <summary>0 (base), 1 (from 50 %) or 2 (at 100 %).</summary>
        public static int Stage(int growthPercent) => Math.Min(LastStage, Math.Max(0, growthPercent) / GrowthPerStage);

        /// <summary>The growth percent at which a stage begins (stage 1 at 50, stage 2 at 100).</summary>
        public static int StageStart(int stage) => Math.Min(GrowthSteps, Math.Max(0, stage) * GrowthPerStage);

        /// <summary>What one stat with <paramref name="maxValue"/> at 100 % gives now.</summary>
        public static float StatBonus(float maxValue, int growthPercent, bool fed) =>
            fed ? maxValue * Math.Min(GrowthSteps, Math.Max(0, growthPercent)) / GrowthSteps : 0f;

        /// <summary>A riding speed from its range (slowest at 0 %, fastest at 100 %); a starving mount goes at its slowest.</summary>
        public static float Speed(float slowest, float fastest, int growthPercent, bool fed)
        {
            float t = fed ? Math.Min(GrowthSteps, Math.Max(0, growthPercent)) / (float)GrowthSteps : 0f;
            return slowest + (fastest - slowest) * t;
        }

        /// <summary>Adds EXP units. A starving mount earns nothing; the meter stops at full.</summary>
        public static MountState AddXp(in MountState state, uint xpUnits, in MountTuning tuning)
        {
            if (!state.IsFed || xpUnits == 0) return state;
            ulong total = Math.Min(tuning.XpToFull, (ulong)state.XpUnits + xpUnits);
            return state.WithXp((uint)total);
        }

        /// <summary>Sets growth outright (GM): the EXP at which that percent begins.</summary>
        public static MountState WithGrowth(in MountState state, float growthPercent, in MountTuning tuning)
        {
            double clamped = Math.Min(GrowthSteps, Math.Max(0.0, growthPercent));
            return state.WithXp((uint)Math.Ceiling(tuning.XpToFull * clamped / GrowthSteps));
        }

        /// <summary>
        /// Hunger over <paramref name="seconds"/> of riding. Only whole units are taken from the meter; the rest is
        /// handed back in <paramref name="carry"/> for the next call, so slow ticks lose nothing.
        /// </summary>
        public static MountState Drain(in MountState state, double seconds, in MountTuning tuning, ref double carry)
        {
            if (seconds <= 0.0 || !state.IsFed) return state;
            carry += seconds * tuning.HungerPerSecond;
            if (carry < 1.0) return state;
            uint whole = (uint)Math.Min(carry, MountState.FullHungerUnits);
            carry -= whole;
            return state.WithHungerDeficit(Math.Min(MountState.FullHungerUnits, state.HungerDeficit + whole));
        }

        /// <summary>Fed back to 100 %.</summary>
        public static MountState Feed(in MountState state) => state.WithHungerDeficit(0);

        /// <summary>Sets hunger outright (GM), 0–100.</summary>
        public static MountState WithHunger(in MountState state, float hungerPercent)
        {
            double clamped = Math.Min(100.0, Math.Max(0.0, hungerPercent));
            return state.WithHungerDeficit((uint)Math.Round(MountState.FullHungerUnits * (100.0 - clamped) / 100.0));
        }
    }
}
