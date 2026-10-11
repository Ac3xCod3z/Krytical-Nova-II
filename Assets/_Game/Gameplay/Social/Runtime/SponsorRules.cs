using System;
using KryticalNova.Factions;

namespace KryticalNova.Social
{
    /// <summary>
    /// The rules of Sponsor / Recruit, free of networking so they can be tested: who of two characters would be the
    /// Sponsor, whether the link may be made, when the Recruit has caught up, how the bonus sits beside the Squad
    /// boost and how nearness turns into Sponsor points. The numbers come in from <see cref="SponsorSettings"/>.
    /// </summary>
    public static class SponsorRules
    {
        public const string ReasonSelf = "You can't sponsor yourself.";
        public const string ReasonOtherFaction = "A Sponsor and a Recruit must be of the same faction.";
        public const string ReasonHasSponsor = "That character already has a Sponsor.";
        public const string ReasonYouHaveSponsor = "You already have a Sponsor.";
        public const string ReasonIsRecruit = "A Recruit can't sponsor anyone.";
        public const string ReasonIsSponsor = "A Sponsor can't be recruited.";

        /// <summary>
        /// Who of the two would be the Sponsor: the one who has reached the Sponsor level, when the other has not.
        /// False when neither has, or both have.
        /// </summary>
        public static bool TryRoles(int levelA, int levelB, int sponsorMinLevel, out bool aSponsors)
        {
            bool a = levelA >= sponsorMinLevel, b = levelB >= sponsorMinLevel;
            aSponsors = a;
            return a != b;
        }

        /// <summary>True when the Recruit is no longer more than <paramref name="levelGap"/> levels below the Sponsor: the link ends.</summary>
        public static bool Outgrown(int sponsorLevel, int recruitLevel, int levelGap) => sponsorLevel - recruitLevel <= Math.Max(0, levelGap);

        /// <summary>Why a Sponsor may not take on a Recruit (shown to whoever asked), or null.</summary>
        public static string LinkBlockedReason(int sponsorLevel, int recruitLevel, FactionId sponsorFaction, FactionId recruitFaction,
            int sponsorRecruits, bool sponsorIsRecruit, bool recruitHasSponsor, bool recruitIsSponsor, int sponsorMinLevel, int levelGap, int maxRecruits)
        {
            if (sponsorFaction != recruitFaction || sponsorFaction == FactionId.None) return ReasonOtherFaction;
            if (sponsorLevel < sponsorMinLevel) return $"A Sponsor must be level {sponsorMinLevel}.";
            if (Outgrown(sponsorLevel, recruitLevel, levelGap)) return $"A Recruit must be more than {levelGap} levels below the Sponsor.";
            if (sponsorIsRecruit) return ReasonIsRecruit;
            if (recruitIsSponsor) return ReasonIsSponsor;
            if (recruitHasSponsor) return ReasonHasSponsor;
            if (sponsorRecruits >= maxRecruits) return $"A Sponsor can have {maxRecruits} Recruit{(maxRecruits == 1 ? string.Empty : "s")} at a time.";
            return null;
        }

        /// <summary>The boost a kill is paid with: the Sponsor's bonus does not add to the Squad's, the larger counts.</summary>
        public static float ExperienceBoost(float squadBoost, float sponsorBonus) => Math.Max(Math.Max(0f, squadBoost), Math.Max(0f, sponsorBonus));

        /// <summary>
        /// The Sponsor points owed for time spent near a Recruit: one payment for each full minute.
        /// <paramref name="nearSeconds"/> is what has been counted so far; what is left of it after the payment comes back in it.
        /// </summary>
        public static int PointsDue(ref float nearSeconds, int pointsPerMinute)
        {
            if (nearSeconds < 60f || pointsPerMinute <= 0)
            {
                if (pointsPerMinute <= 0) nearSeconds = 0f;
                return 0;
            }
            int minutes = (int)(nearSeconds / 60f);
            nearSeconds -= minutes * 60f;
            long points = (long)minutes * pointsPerMinute;
            return (int)Math.Min(int.MaxValue, points);
        }
    }
}
