using System;
using System.Collections.Generic;
using KryticalNova.Factions;

namespace KryticalNova.Social
{
    /// <summary>
    /// The party rules, free of networking so they can be tested: a party is for players of one faction, and a
    /// player may join only while within <see cref="MaxLevelGap"/> levels (above or below) of every member.
    /// The rule is checked when a player joins; a member who later outlevels the others stays. A Sponsor hunting
    /// with one of its Recruits is left out of the level rule, both ways.
    /// A party also earns an experience boost: the full value only for a full party that is all in the same map.
    /// </summary>
    public static class PartyRules
    {
        public const int MaxLevelGap = 10;
        public const int MaxSize = 5;
        /// <summary>
        /// The experience boost of a full party whose members are all in the same map (0.2 = +20%), where no
        /// <see cref="SquadSettings"/> says otherwise: the number to tune is <see cref="SquadSettings.maxExperienceBoost"/>.
        /// </summary>
        public const float MaxExperienceBoost = 0.2f;

        /// <summary>True when two levels are at most <see cref="MaxLevelGap"/> apart.</summary>
        public static bool LevelsMatch(int a, int b) => Math.Abs(a - b) <= MaxLevelGap;

        /// <summary>
        /// The share of extra experience a member earns: every other member in their map adds an equal part of
        /// <see cref="MaxExperienceBoost"/>, so only a party of <see cref="MaxSize"/> that is all together earns all of
        /// it. A smaller party, or one with members elsewhere, earns less; with no other member there, nothing.
        /// </summary>
        /// <param name="membersInSameMap">Members in the earning player's map, that player included.</param>
        /// <param name="maxBoost">The boost of a full party all together.</param>
        public static float ExperienceBoost(int partySize, int membersInSameMap, float maxBoost = MaxExperienceBoost)
        {
            partySize = Math.Min(partySize, MaxSize);
            membersInSameMap = Math.Min(membersInSameMap, partySize);
            if (partySize < 2 || membersInSameMap < 2) return 0f;
            return Math.Max(0f, maxBoost) * (membersInSameMap - 1) / (MaxSize - 1);
        }

        /// <param name="partyFaction">The faction of the party (of its leader, or of the player inviting).</param>
        /// <param name="memberLevels">The level of every current member; of the inviter alone before a party exists.</param>
        /// <param name="levelFree">For each member, true when the level rule leaves them out (a Sponsor with a Recruit in the party). Null = nobody.</param>
        /// <param name="candidateLevelFree">True when the level rule leaves the candidate out.</param>
        public static PartyCheck Check(FactionId partyFaction, IReadOnlyList<int> memberLevels, FactionId candidateFaction, int candidateLevel,
            IReadOnlyList<bool> levelFree = null, bool candidateLevelFree = false)
        {
            if (partyFaction == FactionId.None || candidateFaction != partyFaction) return PartyCheck.OtherFaction;
            if (memberLevels.Count >= MaxSize) return PartyCheck.Full;
            if (candidateLevelFree) return PartyCheck.Allowed;
            for (int i = 0; i < memberLevels.Count; i++)
            {
                if (levelFree != null && i < levelFree.Count && levelFree[i]) continue;
                if (!LevelsMatch(memberLevels[i], candidateLevel)) return PartyCheck.LevelTooFar;
            }
            return PartyCheck.Allowed;
        }
    }
}
