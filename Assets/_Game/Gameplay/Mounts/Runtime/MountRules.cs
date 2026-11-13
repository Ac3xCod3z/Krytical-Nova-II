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
        /// <summary>EXP units a Hostile kill gives the selected mount, with the XP boosters' mount share added (Rider, Convoy).</summary>
        public static uint KillXpUnits(in MountTuning tuning, float boosterBonus) =>
            KryticalNova.Progression.ExperienceBoostRules.Boosted(tuning.KillXp, boosterBonus);

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

        /// <summary>The Ride key pressed while running (the Run selection and moving) gets the running kind of getting
        /// on or off, which never stops the rider; walking or standing gets the standing kind (owner, 6 Nov 2026).</summary>
        public static bool IsRunningTransition(bool runSelected, bool travelling) => runSelected && travelling;

        /// <summary>
        /// The server takes a rider off at once in a jump's flight or on a wall (the mount has no clips for them), but never in a
        /// vault: the rider goes over mounted and lands back in the saddle (owner, 13 Nov 2026, day list 15.2).
        /// </summary>
        public static bool AirborneTakesRiderOff(bool inJumpingMove, bool inVault) => inJumpingMove && !inVault;

        /// <summary>
        /// The mounted vault's shape over its air time t (0..1): how much the mount is shown (1 whole, 0 gone) and how high the rider
        /// is between the ground pose (0) and the seat (1). The mount dissolves as the feet leave it and re-forms before the touchdown;
        /// the rider leaves the seat over the take-off and settles back into it over the landing.
        /// </summary>
        public static float VaultSeatLift(float t, float leaveShare, float landShare)
        {
            t = Math.Clamp(t, 0f, 1f);
            leaveShare = Math.Clamp(leaveShare, 0.01f, 0.49f);
            landShare = Math.Clamp(landShare, 0.01f, 0.49f);
            if (t <= leaveShare) return 1f - Smooth(t / leaveShare);
            if (t >= 1f - landShare) return Smooth((t - (1f - landShare)) / landShare);
            return 0f;
        }

        static float Smooth(float x)
        {
            x = Math.Clamp(x, 0f, 1f);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// A running getting-off comes up running when, by the time its roll ends, the clicked spot is still at least
        /// <paramref name="minDistance"/> metres of path away; otherwise it ends standing (owner, 7 Nov 2026: 4 m). The
        /// rider keeps the on-foot <paramref name="speed"/> through the dismount, so the path left then is the path left
        /// at the press less what that speed covers by <paramref name="rollEndsAt"/>.
        /// </summary>
        public static bool DismountEndsRunning(float pathLeftAtPress, float speed, float rollEndsAt, float minDistance) =>
            pathLeftAtPress - Math.Max(0f, speed) * Math.Max(0f, rollEndsAt) >= minDistance;

        /// <summary>
        /// Owner, 7 Nov 2026 (retest 2): through a running getting-off the body goes at the run through the take-off and
        /// the flip, at <paramref name="rollScale"/> of it through the roll (less ground), then either back to the run
        /// (ending running) or into a slight slide that eases to a stop over <paramref name="slideSeconds"/> (ending
        /// standing). Returns the share of the run speed at <paramref name="t"/> seconds into the getting-off.
        /// </summary>
        public static float DismountSpeedShare(float t, bool endsRunning, float rollStartsAt, float rollEndsAt, float rollScale, float slideSeconds)
        {
            float k = Math.Max(0f, Math.Min(1f, rollScale));
            if (rollStartsAt <= 0f || t < rollStartsAt) return 1f;
            if (t < rollEndsAt) return k;
            if (endsRunning) return 1f;
            if (slideSeconds <= 0f) return 0f;
            float s = (t - rollEndsAt) / slideSeconds;
            return s >= 1f ? 0f : k * (1f - s);
        }

        /// <summary>Metres covered at <paramref name="speed"/> from the press to the end of the roll (with the slowed roll).</summary>
        public static float DismountReachToRollEnd(float speed, float rollStartsAt, float rollEndsAt, float rollScale) =>
            Math.Max(0f, speed) * (Math.Max(0f, rollStartsAt) + Math.Max(0f, rollEndsAt - rollStartsAt) * Math.Max(0f, Math.Min(1f, rollScale)));

        /// <summary>Metres a getting-off that ends standing covers at <paramref name="speed"/>, the slide included.</summary>
        public static float DismountReachStanding(float speed, float rollStartsAt, float rollEndsAt, float rollScale, float slideSeconds) =>
            DismountReachToRollEnd(speed, rollStartsAt, rollEndsAt, rollScale)
            + Math.Max(0f, speed) * Math.Max(0f, Math.Min(1f, rollScale)) * Math.Max(0f, slideSeconds) * 0.5f;

        /// <summary>The run-or-stand choice with the slowed roll: up running when the clicked spot is still
        /// <paramref name="minDistance"/> or more away as the roll ends.</summary>
        public static bool DismountEndsRunning(float pathLeftAtPress, float speed, float rollStartsAt, float rollEndsAt, float rollScale, float minDistance) =>
            pathLeftAtPress - DismountReachToRollEnd(speed, rollStartsAt, rollEndsAt, rollScale) >= minDistance;

        /// <summary>The most a getting-off that ends standing may speed its whole profile up to reach the clicked spot.</summary>
        public const float DismountMostSpeedUp = 1.25f;

        /// <summary>
        /// Ending standing: the factor on the run speed that makes the profile (run, slowed roll, slide) end exactly on
        /// the clicked spot, so the body neither stalls early nor walks on afterwards; at most <see cref="DismountMostSpeedUp"/>.
        /// </summary>
        public static float DismountStandingScale(float pathLeftAtPress, float speed, float rollStartsAt, float rollEndsAt, float rollScale, float slideSeconds)
        {
            float reach = DismountReachStanding(speed, rollStartsAt, rollEndsAt, rollScale, slideSeconds);
            if (reach <= 0f) return 0f;
            return Math.Min(DismountMostSpeedUp, Math.Max(0f, pathLeftAtPress) / reach);
        }

        /// <summary>
        /// How fast the body travels through a running getting-off (owner, 7 Nov 2026: "it should feel fluid", never a
        /// stop before the flip or the roll). Ending running: the on-foot <paramref name="runSpeed"/> the whole way. Ending
        /// standing: just fast enough to reach the clicked spot as the roll ends, so the body never arrives early and
        /// stalls while the clip plays on; never faster than the run.
        /// </summary>
        public static float DismountTravelSpeed(bool endsRunning, float pathLeftAtPress, float runSpeed, float rollEndsAt)
        {
            float run = Math.Max(0f, runSpeed);
            if (endsRunning || rollEndsAt <= 0f) return run;
            return Math.Min(run, Math.Max(0f, pathLeftAtPress) / rollEndsAt);
        }
    }
}
