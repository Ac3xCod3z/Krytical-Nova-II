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
    [DefaultExecutionOrder(-200)] // after the animation, before the camera follows the body in LateUpdate (as PlayerMoveAnticipation was)
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
        /// Frames on which something moved or turned the body between the follow (end of the simulation) and LateUpdate (owner's GUI
        /// retry 3, 30 Oct: the body slid without turning); the pose is put back each time. Debugging and the Play DOTS here log.
        /// </summary>
        public int Overwritten;

        void LateUpdate() => ReapplyPose();

        /// <summary>Puts the follow's pose back if anything changed it since; true when it had to.</summary>
        public bool ReapplyPose()
        {
            if (!HasPose) return false;
            var t = transform;
            if ((t.position - PosePosition).sqrMagnitude < 1e-8f && Mathf.Abs(Mathf.DeltaAngle(t.eulerAngles.y, PoseYaw)) < 0.01f) return false;
            Overwritten++;
            t.SetPositionAndRotation(PosePosition, Quaternion.Euler(0f, PoseYaw, 0f));
            return true;
        }
    }
}
