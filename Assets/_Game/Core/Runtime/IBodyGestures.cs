using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// What a character's body can be asked to act out by a system that does not own it (looting today).
    /// Implemented by the character's animation motor. Purely visual, and shown on every peer.
    /// </summary>
    public interface IBodyGestures
    {
        /// <summary>Server (or offline): bend down for something lying at <paramref name="point"/>.</summary>
        void ShowPickUp(Vector3 point);
    }
}
