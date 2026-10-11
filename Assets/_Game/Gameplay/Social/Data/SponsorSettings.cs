using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The numbers of Sponsor / Recruit (Twelve Sky 2: Mentor / Disciple): who may sponsor whom, what the Recruit
    /// gains while the Sponsor is near, and what the Sponsor earns for being there. Only the bonus and the points a
    /// minute come from the reference; the rest are proposals to be tuned. Nothing in code may hard-code one.
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
        [Tooltip("The extra share of kill XP a Recruit earns: 0.5 is +50%. It does not add to the Squad boost: the larger of the two counts.")]
        [Min(0f)] public float experienceBonus = 0.5f;
        [Tooltip("How close the two must be, in metres, in the same map, both alive.")]
        [Min(1f)] public float rangeMetres = 40f;
        [Tooltip("Sponsor points the Sponsor earns for each full minute spent near a Recruit, however many are near.")]
        [Min(0)] public int pointsPerMinute = 250;
        [Tooltip("Seconds between two looks at who is near whom.")]
        [Range(1f, 30f)] public float checkSeconds = 5f;
    }
}
