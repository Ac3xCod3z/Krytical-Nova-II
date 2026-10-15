using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The Recall Tag (Twelve Sky 2: Scroll of Return): how long the trip back to town takes to start and what
    /// stops it. The destination is the map's own spawn points, the same place a death leads to. Read by
    /// <see cref="PlayerRecall"/>; the numbers are first picks (15 Oct 2026), to tune.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Recall Settings", fileName = "RecallSettings")]
    public sealed class RecallSettings : ScriptableObject
    {
        [Tooltip("Seconds the character stands still, channelling, before the trip.")]
        [Min(0f)] public float channelSeconds = 5f;

        [Tooltip("Metres the character may drift during the channel before it counts as moving and the trip is off.")]
        [Min(0.05f)] public float moveTolerance = 0.5f;

        [Tooltip("A Tag whose trip was stopped (a move, a hit, a fight) goes back into the Pack.")]
        public bool refundWhenStopped = true;
    }
}
