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
        /// <summary>
        /// Walk or run to a point. <paramref name="run"/> = run speed (Run is selected); <paramref name="payRun"/> = the server takes Run's Flux for
        /// this destination and refuses the walk without it (NGO ServerMoveTo; a later steering step of the same walk runs free).
        /// </summary>
        void MoveTo(Vector3 point, bool run, bool payRun);

        /// <summary>Stop where the body is.</summary>
        void Stop();

        /// <summary>Put the body at a place at once (a respawn, a map change, a GM move).</summary>
        void Teleport(Vector3 position, Quaternion rotation);

        /// <summary>
        /// A jump to <paramref name="landing"/> the server orders (a Squad follower copying the member's jump; M8 8.4.3c): planned and paid as
        /// the player's own jump click. False, with the reason, when it cannot be.
        /// </summary>
        bool JumpTo(Vector3 landing, out string reason);

        /// <summary>The body is in a jump's flight now (its ghost's plan; NGO's PlayerJumpMotor.IsBusy, which never runs on a ServerBody).</summary>
        bool Airborne { get; }

        /// <summary>The body is not standing still: walking, in a jump or a vault, on a wall or the water (NGO's motor CanStartEmote; M8 8.4.6).</summary>
        bool Busy { get; }
    }
}
