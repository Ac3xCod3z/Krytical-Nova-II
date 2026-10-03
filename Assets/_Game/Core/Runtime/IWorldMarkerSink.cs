using UnityEngine;

namespace KryticalNova.Core
{
    public enum WorldTargetKind
    {
        Hostile,
        Friendly
    }

    /// <summary>
    /// Client-only ground markers: the ring at a chosen move destination and the ring under the selected target.
    /// Implemented by the UI assembly; null on a dedicated server.
    /// </summary>
    public interface IWorldMarkerSink
    {
        /// <summary>
        /// Shows the destination ring; it hides itself once <paramref name="mover"/> arrives, or after a short while
        /// unless <paramref name="untilArrival"/> is set (a long journey ordered from the map).
        /// </summary>
        void ShowMoveMarker(Transform mover, Vector3 point, Vector3 normal, bool untilArrival = false);
        void HideMoveMarker();
        void SetTarget(Transform target, WorldTargetKind kind);
        void ClearTarget();
    }
}
