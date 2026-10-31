using UnityEngine;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>What the animation needs from the ghost, refreshed every frame by <see cref="PlayerPresentationFollowSystem"/>.</summary>
    [System.Serializable]
    public struct PlayerAnimState
    {
        /// <summary>Metres per second the ghost walks at (0 when standing).</summary>
        public float Speed;
        public bool Moving;
        public bool Running;
    }

    /// <summary>
    /// Sits on a presentation body (Player.prefab as ClientView, later). Animancer and the other view scripts read
    /// <see cref="State"/>; nothing here writes back to the ghost.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(290)] // after the LateUpdates that may touch the body (cards 200), before it is drawn
    public sealed class PlayerPresentationState : MonoBehaviour
    {
        public PlayerAnimState State;
        public int NetworkId;
        /// <summary>Times the body was set to a spot without easing (spawn, placement, a huge correction); tests and debugging.</summary>
        public int Snaps;

        /// <summary>Where <see cref="PlayerPresentationFollowSystem"/> put the body this frame (metres, yaw in degrees).</summary>
        public bool HasPose;
        public Vector3 PosePosition;
        public float PoseYaw;
        /// <summary>
        /// Frames on which something moved or turned the body after the follow placed it (owner's GUI retries 3-4, 30-31 Oct 2026: the
        /// hit reaction held a ghost body's root at its first rotation). Counted, never corrected: a second writer must be found and
        /// fixed, not hidden. Debugging and the Play DOTS here log.
        /// </summary>
        public int Overwritten;
        /// <summary>The yaw the follow was given this frame (degrees) and whether it came from PoseAhead (else the ghost's transform); debugging.</summary>
        public float InputYaw;
        public bool InputFromPoseAhead;

        void LateUpdate() => CheckPose();

        /// <summary>The body's yaw when the check ran this frame (degrees); debugging.</summary>
        public float YawAtCheck;

        /// <summary>True (and counted) when the body is no longer where the follow put it this frame.</summary>
        public bool CheckPose()
        {
            var t = transform;
            YawAtCheck = t.eulerAngles.y;
            if (!HasPose) return false;
            if ((t.position - PosePosition).sqrMagnitude < 1e-8f && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, PoseYaw)) < 0.01f) return false;
            Overwritten++;
            return true;
        }
    }
}
