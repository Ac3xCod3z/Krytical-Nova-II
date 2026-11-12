using System.Collections.Generic;

namespace KryticalNova.Hostiles
{
    /// <summary>One Squad member as the payout of a Hostile kill sees them (free of the scene, so it can be tested).</summary>
    public struct PayoutMember
    {
        /// <summary>The member's character level.</summary>
        public int Level;
        /// <summary>In the map of the kill (today: the same scene, one map server). The only Squad condition: no distance.</summary>
        public bool SameMap;
        /// <summary>A Sponsor in a Squad with one of its Recruits.</summary>
        public bool IsEscort;
    }

    /// <summary>Who a kill pays, at which level gap, and whether the Squad boost is added.</summary>
    public struct Payout
    {
        /// <summary>Index into the members given to <see cref="KillPayoutPlan.Plan"/>.</summary>
        public int Index;
        public int Gap;
        /// <summary>False: paid with no boost at all (an escort paid by its own gap).</summary>
        public bool SquadBoost;
    }

    public enum PayoutOutcome
    {
        /// <summary>No Squad: the holder alone, by its own gap, with no boost.</summary>
        Solo,
        /// <summary>A Squad member's kill: every member in the map.</summary>
        Squad,
        /// <summary>An escorting Sponsor holds the kill: it pays XP to no one.</summary>
        EscortPaysNoXp,
    }

    /// <summary>
    /// The decision of <see cref="HostileKillRewards"/> of who a Hostile kill pays, kept apart from the GameObjects. The holder pays alone
    /// without a Squad; in a Squad every member in the map is paid by its own level gap, a member <c>squadCarryLevels</c> or more
    /// gap-worse than the Squad's highest by the highest's gap (escorting Sponsors do not count as highest); an escort that did not hold
    /// the kill is paid by its own gap with no boost.
    /// </summary>
    public static class KillPayoutPlan
    {
        public static PayoutOutcome Plan(int hostileLevel, LevelGapSettings gaps, IReadOnlyList<PayoutMember> members, int holderIndex,
            List<Payout> into)
        {
            into.Clear();
            if (members == null || members.Count <= 1) return PayoutOutcome.Solo;
            if (holderIndex >= 0 && holderIndex < members.Count && members[holderIndex].IsEscort) return PayoutOutcome.EscortPaysNoXp;

            int highestGap = int.MaxValue;
            for (int i = 0; i < members.Count; i++)
                if (members[i].SameMap && !members[i].IsEscort)
                    highestGap = System.Math.Min(highestGap, LevelGapRules.Gap(hostileLevel, members[i].Level));
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].SameMap) continue; // only members in the map of the kill
                int ownGap = LevelGapRules.Gap(hostileLevel, members[i].Level);
                into.Add(members[i].IsEscort
                    ? new Payout { Index = i, Gap = ownGap, SquadBoost = false }
                    : new Payout { Index = i, Gap = LevelGapRules.SquadGap(gaps, ownGap, highestGap), SquadBoost = true });
            }
            return PayoutOutcome.Squad;
        }
    }
}
