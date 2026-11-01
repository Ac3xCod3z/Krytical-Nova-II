using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// A body the entity netcode moves (its <see cref="PlayerWallRunMotor"/> is switched off, so it is not in the motor's list): trees near
    /// it get their climbable colliders as near an NGO player (<see cref="TreeProxyField"/>), and the placed trees of its scene are marked as
    /// trees. Added by the entity netcode's body setup to the server's bodies and the owner's own view body (NGO's <c>WantsTreeProxies</c>:
    /// the server and the owner), M3c part 3.
    /// </summary>
    public sealed class TreeProxyAnchor : MonoBehaviour
    {
        private void OnEnable()
        {
            TreeProxyField.AddAnchor(transform);
            TreeProxyField.MarkPlacedTreesOnce(gameObject.scene);
        }

        private void OnDisable() => TreeProxyField.RemoveAnchor(transform);
    }
}
