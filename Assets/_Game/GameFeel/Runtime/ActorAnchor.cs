using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Where a character's body is relative to its root. A NavMeshAgent's root floats its base offset above the soles
    /// (the player's root is the capsule centre, 1 m up), so an effect meant for the feet has to come down by that
    /// much. Effects are authored in metres above the soles and brought into place through here.
    /// </summary>
    internal static class ActorAnchor
    {
        /// <summary>Metres from the character's root down to its soles; 0 for characters whose root is on the ground.</summary>
        public static float RootHeight(Transform actor)
        {
            if (actor == null) return 0f;
            var agent = actor.GetComponentInParent<NavMeshAgent>();
            return agent != null ? agent.baseOffset : 0f;
        }

        /// <summary>The point between the character's soles.</summary>
        public static Vector3 Feet(Transform actor) => actor.position - Vector3.up * RootHeight(actor);
    }
}
