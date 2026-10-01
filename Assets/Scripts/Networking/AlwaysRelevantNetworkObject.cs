using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Opts a root NetworkObject out of <see cref="SpatialInterestManager"/> distance culling so every
    /// client always receives it (e.g. the world clock / weather state).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class AlwaysRelevantNetworkObject : MonoBehaviour
    {
    }
}
