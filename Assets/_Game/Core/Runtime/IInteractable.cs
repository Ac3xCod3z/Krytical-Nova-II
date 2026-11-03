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

    /// <summary>
    /// An interactable that needs no second thought: one click walks over and uses it (an item on the ground),
    /// where an NPC is selected by the first click and talked to by the second.
    /// </summary>
    public interface IInstantInteractable : IInteractable
    {
    }

    /// <summary>
    /// An interactable that also works on a client view of the entity netcode (M7.6): using it only shows something (a Kiosk stand is
    /// browsed), and whatever is then done is a request the server decides. Others say "not networked yet" on a view.
    /// </summary>
    public interface IEntityViewInteractable : IInteractable
    {
    }
}
