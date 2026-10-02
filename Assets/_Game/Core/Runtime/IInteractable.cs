using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>A world object the local player can click to walk up to and talk to (NPCs, later signs/chests).</summary>
    public interface IInteractable
    {
        string DisplayName { get; }
        /// <summary>Where the player walks to before interacting (on the NavMesh, in front of the object).</summary>
        Vector3 InteractPoint { get; }
        /// <summary>Horizontal distance from the object's position at which interaction may start.</summary>
        float InteractRange { get; }
        Vector3 Position { get; }
        void Interact(GameObject player);
    }
}
