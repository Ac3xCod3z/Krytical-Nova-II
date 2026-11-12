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
        /// <summary>A Recruit of the player who holds the kill.</summary>
        public bool IsRecruitOfHolder;
        /// <summary>Within <c>SponsorSettings.rangeMetres</c> (500 m) of the holder, both alive, same map.</summary>
        public bool WithinSponsorRange;
    }

    /// <summary>Who a kill pays, at which level gap, and whether the Squad boost is added.</summary>
    public struct Payout
    {
        /// <summary>Index into the members given to <see cref="KillPayoutPlan.Plan"/>.</summary>
        public int Index;
        public int Gap;
        /// <summary>False: paid with no boost at all (an escort paid by its own gap).</summary>
        public bool SquadBoost;
        /// <summary>False: no Sponsor bonus is added (a kill an escorting Sponsor holds).</summary>
        public bool SponsorBonus;
    }

    public enum PayoutOutcome
    {
        /// <summary>No Squad: the holder alone, by its own gap, with no boost.</summary>
        Solo,
        /// <summary>A Squad member's kill: every member in the map.</summary>
        Squad,
        /// <summary>An escorting Sponsor holds the kill and one of its Recruits is within range: the Recruits and the Squad are paid, not the Sponsor.</summary>
        EscortPaysRecruits,
        /// <summary>An escorting Sponsor holds the kill and none of its Recruits is within range: the rest of the Squad only, by the Sponsor's gap at best.</summary>
        EscortNoRecruitNear,
    }

    /// <summary>
    /// The decision of <see cref="HostileKillRewards"/> of who a Hostile kill pays, kept apart from the GameObjects. The holder pays alone
    /// without a Squad; in a Squad every member in the map is paid by its own level gap, a member <c>squadCarryLevels</c> or more
    /// gap-worse than the Squad's highest by the highest's gap (escorting Sponsors do not count as highest); an escort that did not hold
    /// the kill is paid by its own gap with no boost.
    /// A kill an escorting Sponsor holds (owner pick A, 12 Nov 2026) pays the Sponsor nothing and adds no Sponsor bonus to anyone. Its
    /// Recruits within range are paid as in a member's kill, each once, by their own gap (carry as above). Every other member in the map
    /// (a mate who is no Recruit of it, a Recruit beyond the range) is paid by the Squad rule with the Sponsor's level counted as the
    /// Squad's highest, so a far higher Sponsor puts them in the low band.
    /// </summary>
    public static class KillPayoutPlan
    {
        public static PayoutOutcome Plan(int hostileLevel, LevelGapSettings gaps, IReadOnlyList<PayoutMember> members, int holderIndex,
            List<Payout> into)
        {
            into.Clear();
            if (members == null || members.Count <= 1) return PayoutOutcome.Solo;
            bool escortHolds = holderIndex >= 0 && holderIndex < members.Count && members[holderIndex].IsEscort;

            int highestGap = int.MaxValue;
            for (int i = 0; i < members.Count; i++)
                if (members[i].SameMap && !members[i].IsEscort)
                    highestGap = System.Math.Min(highestGap, LevelGapRules.Gap(hostileLevel, members[i].Level));

            if (escortHolds)
            {
                int othersHighest = System.Math.Min(highestGap, LevelGapRules.Gap(hostileLevel, members[holderIndex].Level));
                bool anyNear = false;
                for (int i = 0; i < members.Count; i++)
                {
                    PayoutMember m = members[i];
                    if (!m.SameMap || m.IsEscort) continue; // the Sponsor (and any other escort) is paid nothing
                    int ownGap = LevelGapRules.Gap(hostileLevel, m.Level);
                    bool near = m.IsRecruitOfHolder && m.WithinSponsorRange;
                    anyNear |= near;
                    into.Add(new Payout
                    {
                        Index = i,
                        Gap = LevelGapRules.SquadGap(gaps, ownGap, near ? highestGap : othersHighest),
                        SquadBoost = true,
                        SponsorBonus = false,
                    });
                }
                return anyNear ? PayoutOutcome.EscortPaysRecruits : PayoutOutcome.EscortNoRecruitNear;
            }

            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].SameMap) continue; // only members in the map of the kill
                int ownGap = LevelGapRules.Gap(hostileLevel, members[i].Level);
                into.Add(members[i].IsEscort
                    ? new Payout { Index = i, Gap = ownGap, SquadBoost = false, SponsorBonus = true }
                    : new Payout { Index = i, Gap = LevelGapRules.SquadGap(gaps, ownGap, highestGap), SquadBoost = true, SponsorBonus = true });
            }
            return PayoutOutcome.Squad;
        }
    }
}
