using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The numbers of a Squad that are tuned rather than ruled: the experience boost, and how a member follows
    /// another. (Who may join a Squad is <see cref="PartyRules"/>.)
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Squad Settings", fileName = "SquadSettings")]
    public sealed class SquadSettings : ScriptableObject
    {
        [Header("Experience")]
        [Tooltip("The extra share of kill XP of a full Squad whose members are all in the map of the kill: 0.2 is +20%. A smaller Squad, or one with members elsewhere, earns a part of it.")]
        [Range(0f, 2f)] public float maxExperienceBoost = 0.2f;

        [Header("Follow")]
        [Tooltip("Metres behind the member the follower stands (owner, 15 Oct 2026: three to four).")]
        [Min(0.5f)] public float followKeepDistance = 3.5f;
        [Tooltip("Metres the member must be away before a follower that has stopped sets off again. Keeps the follower from " +
                 "shuffling at every small step; never less than the keep distance.")]
        [Min(0.5f)] public float followResumeDistance = 5f;
        [Tooltip("Metres beyond which the member is lost and following ends.")]
        [Min(5f)] public float followMaxDistance = 60f;
        [Tooltip("Seconds between two looks at where the member has gone.")]
        [Range(0.1f, 2f)] public float followRepathSeconds = 0.4f;
        [Tooltip("The member's jumps are copied: the follower walks to where the member took off and, within this many metres of it, jumps to where the member landed.")]
        [Min(0.2f)] public float followHopReach = 0.8f;
        [Tooltip("A jump of the member's older than this many seconds is no longer copied.")]
        [Min(1f)] public float followHopMaxAge = 15f;
        [Tooltip("How many of the member's jumps are remembered to be copied in turn; the oldest gives way.")]
        [Range(1, 8)] public int followHopQueue = 4;

        private void OnValidate()
        {
            followResumeDistance = Mathf.Max(followResumeDistance, followKeepDistance + 0.5f);
            followMaxDistance = Mathf.Max(followMaxDistance, followResumeDistance + 1f);
        }
    }
}
