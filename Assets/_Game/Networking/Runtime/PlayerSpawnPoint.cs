using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>Marks where joining players may appear. Read by <see cref="PlayerSpawnService"/> on the server.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        private static readonly List<PlayerSpawnPoint> ActivePoints = new List<PlayerSpawnPoint>(8);

        public static IReadOnlyList<PlayerSpawnPoint> Active => ActivePoints;

        private void OnEnable() => ActivePoints.Add(this);
        private void OnDisable() => ActivePoints.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.5f);
            Gizmos.DrawLine(transform.position + Vector3.up * 0.1f, transform.position + Vector3.up * 0.1f + transform.forward);
        }
    }
}
