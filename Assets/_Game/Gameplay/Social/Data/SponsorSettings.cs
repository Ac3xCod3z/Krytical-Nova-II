using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The numbers of Sponsor / Recruit (Twelve Sky 2: Mentor / Disciple): who may sponsor whom, what the Recruit
    /// gains while the Sponsor is near, and what the Sponsor earns for being there. Only the points a minute come
    /// from the reference; the bonus steps are the owner's (11 Oct 2026) and the rest are proposals to be tuned.
    /// Nothing in code may hard-code one.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Sponsor Settings", fileName = "SponsorSettings")]
    public sealed class SponsorSettings : ScriptableObject
    {
        [Header("Who")]
        [Tooltip("The level a character needs to be a Sponsor. The level cap for now; Prime 1 and up once Prime levels exist.")]
        [Min(1)] public int sponsorMinLevel = 112;
        [Tooltip("A Recruit must be more than this many levels below the Sponsor. When the Recruit closes to within this many, the link ends: the Recruit has caught up.")]
        [Min(0)] public int levelGap = 5;
        [Tooltip("Recruits one Sponsor may have at a time.")]
        [Range(1, 10)] public int maxRecruits = 3;
        [Tooltip("Seconds a Sponsor request waits for an answer.")]
        [Min(5f)] public float requestSeconds = 30f;

        [Header("While the Sponsor is near")]
        [Tooltip("The extra share of kill XP a Recruit earns, by how many of the Sponsor's Recruits hunt together (in the Sponsor's Squad and near the Sponsor): the first value with one, the second with two and so on; the last holds for more. 0.1 is +10%. A Recruit outside the Sponsor's Squad is paid the first value.")]
        public float[] experienceBonusSteps = { 0.1f, 0.15f, 0.2f };
        [Tooltip("On: in the Sponsor's Squad the bonus adds to the Squad boost. Off, and always outside the Sponsor's Squad: the larger of the two counts.")]
        public bool addsToSquadBoost = true;
        [Tooltip("How close the two must be, in metres, in the same map, both alive.")]
        [Min(1f)] public float rangeMetres = 40f;
        [Tooltip("Sponsor points the Sponsor earns for each full minute spent near a Recruit, however many are near.")]
        [Min(0)] public int pointsPerMinute = 250;
        [Tooltip("Seconds between two looks at who is near whom.")]
        [Range(1f, 30f)] public float checkSeconds = 5f;

        [Header("A Sponsor in a Squad with one of its Recruits")]
        [Tooltip("The Sponsor is there to help, not to hunt: a Hostile it holds pays XP to no one, and one this many levels or more below the Sponsor drops nothing either.")]
        [Min(0)] public int sponsorNoDropLevels = 5;

        /// <summary>The bonus with one Recruit, and the most it can be.</summary>
        public float FirstBonus => SponsorRules.Bonus(experienceBonusSteps, 1);
        public float TopBonus => SponsorRules.Bonus(experienceBonusSteps, int.MaxValue);
    }
}
