using System;

namespace KryticalNova.Progression
{
    /// <summary>
    /// The sums of a Respec Chip and a Memory Wipe (wave 3b, owner 12 Nov 2026), kept pure so the spend and the hand-back are
    /// pinned by one function each. Stat points: what <see cref="PlayerCharacterProgression.ResetAllocations"/> hands back.
    /// Skill points: every rank above 0 summed with the spend's own cost (combat ranks: <c>GetSkillPointCostForRank</c>; Run and
    /// Jump: each rank's <c>skillPointCost</c>). The items cap the hand-back at what the level earned, so ranks or attributes a GM
    /// gave (not bought) never turn into points; the GM's own reset is not capped.
    /// </summary>
    public static class ProgressionResetRules
    {
        /// <summary>No cap: the GM's reset (NGO's <c>ResetAllocations</c> as built).</summary>
        public const int NoCap = int.MaxValue;

        /// <summary>The refusal when nothing was placed (Respec Chip).</summary>
        public const string ReasonNoStatsPlaced = "You have no stat points placed.";
        /// <summary>The refusal when no rank is learned (Memory Wipe).</summary>
        public const string ReasonNoSkillsLearned = "You have no skills learned.";
        /// <summary>The refusal when everything placed was given, not spent (a test character, GM ranks).</summary>
        public const string ReasonOnlyGiven = "Nothing to hand back: these points were given, not spent.";
        /// <summary>The refusal while a cast, a jump or a walk is under way (Memory Wipe takes Run and Jump).</summary>
        public const string ReasonBusy = "Finish what you are doing first.";

        /// <summary>Stat points a character of <paramref name="level"/> has earned: the start plus every level's reward.</summary>
        public static int EarnedStatPoints(CharacterProgressionData data, int level) => Earned(data, level, true);

        /// <summary>Skill points a character of <paramref name="level"/> has earned: the start plus every level's reward.</summary>
        public static int EarnedSkillPoints(CharacterProgressionData data, int level) => Earned(data, level, false);

        private static int Earned(CharacterProgressionData data, int level, bool stat)
        {
            if (data == null) return 0;
            long sum = stat ? data.startingStatPoints : data.startingSkillPoints;
            for (int at = 1; at < level && data.TryGetStep(at, out CharacterLevelStep step); at++)
                sum += stat ? step.statPointReward : step.skillPointReward;
            return (int)Math.Min(Math.Max(0L, sum), int.MaxValue);
        }

        /// <summary>
        /// Stat points placed: for each attribute <c>max(0, value - starting) * max(1, cost)</c>, saturated (NGO
        /// PlayerCharacterProgression.cs:296-308).
        /// </summary>
        public static int StatPointsPlaced(int[] attributes, Func<int, int> startingValue, int allocationCost)
        {
            if (attributes == null || startingValue == null) return 0;
            long cost = Math.Max(1, allocationCost), sum = 0;
            for (int index = 0; index < attributes.Length; index++)
                sum += Math.Max(0, attributes[index] - startingValue(index)) * cost;
            return (int)Math.Min(sum, int.MaxValue);
        }

        /// <summary>Skill points one combat rank cost: the sum of <paramref name="costForRank"/>(r) for r = 0 .. rank - 1.</summary>
        public static long CombatRankCost(int rank, Func<int, int> costForRank)
        {
            long sum = 0;
            if (costForRank == null) return 0;
            for (int r = 0; r < rank; r++) sum += Math.Max(0, costForRank(r));
            return sum;
        }

        /// <summary>Skill points a Run / Jump rank cost: the sum of <paramref name="costOfRank"/>(k) for k = 1 .. rank.</summary>
        public static long MovementRankCost(int rank, Func<int, int> costOfRank)
        {
            long sum = 0;
            if (costOfRank == null) return 0;
            for (int k = 1; k <= rank; k++) sum += Math.Max(0, costOfRank(k));
            return sum;
        }

        /// <summary>
        /// What an item hands back of <paramref name="placed"/>: never more than takes the free points to what the level earned
        /// (free + placed &lt;= earned for every real player; the rest was given by a GM). <see cref="NoCap"/> = all of it.
        /// </summary>
        public static int Capped(long placed, int free, int earned)
        {
            if (placed <= 0) return 0;
            long room = earned == NoCap ? long.MaxValue : Math.Max(0L, (long)earned - Math.Max(0, free));
            return (int)Math.Min(Math.Min(placed, room), int.MaxValue);
        }

        /// <summary>The server line for a hand-back the cap trimmed (never a silent trim), or null when nothing was trimmed.</summary>
        public static string TrimLine(string item, string character, int handedBack, long placed) =>
            handedBack > 0 && handedBack < placed
                ? $"[{item}] {character}: handed back {handedBack} of {placed} (the rest was given, not spent)"
                : null;
    }
}
