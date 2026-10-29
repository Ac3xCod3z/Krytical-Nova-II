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
    public sealed class PlayerPresentationState : MonoBehaviour
    {
        public PlayerAnimState State;
        public int NetworkId;
        /// <summary>Times the body was set to a spot without easing (spawn, placement, a huge correction); tests and debugging.</summary>
        public int Snaps;
    }
}
