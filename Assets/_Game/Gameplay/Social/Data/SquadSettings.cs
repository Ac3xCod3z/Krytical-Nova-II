using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The numbers of a Squad that are tuned rather than ruled: how a member follows another. (Who may join a Squad
    /// is <see cref="PartyRules"/>.)
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Squad Settings", fileName = "SquadSettings")]
    public sealed class SquadSettings : ScriptableObject
    {
        [Header("Follow")]
        [Tooltip("Metres the follower stops short of the member it follows.")]
        [Min(0.5f)] public float followKeepDistance = 2.5f;
        [Tooltip("Metres the member must be away before a follower that has stopped sets off again. Keeps the follower from " +
                 "shuffling at every small step; never less than the keep distance.")]
        [Min(0.5f)] public float followResumeDistance = 4f;
        [Tooltip("Metres beyond which the member is lost and following ends.")]
        [Min(5f)] public float followMaxDistance = 60f;
        [Tooltip("Seconds between two looks at where the member has gone.")]
        [Range(0.1f, 2f)] public float followRepathSeconds = 0.4f;

        private void OnValidate()
        {
            followResumeDistance = Mathf.Max(followResumeDistance, followKeepDistance);
            followMaxDistance = Mathf.Max(followMaxDistance, followResumeDistance + 1f);
        }
    }
}
