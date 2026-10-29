using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// How a body is moved when its movement truth lives somewhere else (DOTS migration M3a-2b: the ghost). A ServerBody's
    /// mover turns a request into a ghost order; a ClientView's mover turns it into a click for the server. Gameplay code that
    /// used to steer the NavMeshAgent asks the body's <see cref="BodyRoleTag.Mover"/> first and falls back to the agent when
    /// there is none (the offline avatar and the Netcode for GameObjects bodies have none).
    /// </summary>
    public interface IBodyMover
    {
        /// <summary>Walk or run to a point.</summary>
        void MoveTo(Vector3 point, bool run);

        /// <summary>Stop where the body is.</summary>
        void Stop();

        /// <summary>Put the body at a place at once (a respawn, a map change, a GM move).</summary>
        void Teleport(Vector3 position, Quaternion rotation);
    }
}
