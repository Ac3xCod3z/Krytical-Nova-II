using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>Named teleport target for GM tools (gm.tp / gm.poi). Drop on any empty GameObject in a zone.</summary>
    [DisallowMultipleComponent]
    public sealed class PointOfInterest : MonoBehaviour
    {
        private static readonly List<PointOfInterest> ActivePoints = new List<PointOfInterest>(16);

        [Tooltip("Short id typed into gm.tp (case-insensitive). Defaults to the GameObject name when empty.")]
        [SerializeField] private string poiId;
        [SerializeField] private string displayName;

        public static IReadOnlyList<PointOfInterest> Active => ActivePoints;
        public string Id => string.IsNullOrWhiteSpace(poiId) ? name : poiId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? Id : displayName;

        private void OnEnable() => ActivePoints.Add(this);
        private void OnDisable() => ActivePoints.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.1f, 0.75f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 2f);
        }
    }
}
