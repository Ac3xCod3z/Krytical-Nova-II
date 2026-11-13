using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// The brain soak's planned spot on Zone01 (wave 4, 12 Nov 2026; owner rule: tests plan their spots, coordinates proven in edit mode,
    /// never searched near the start). Written by Krytical Nova ▸ Online ▸ Entities ▸ Soak ▸ 2 Plan Soak Spot; read by <c>soak_run.py</c>,
    /// which passes the centre to the test-lane server as <c>-knbrainsoakat x,z</c>. Design data only: nothing reads it at run time.
    /// </summary>
    public sealed class SoakSpots : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Gameplay/Hostiles/Data/SoakSpots.asset";

        [Tooltip("Map the spot was proven on.")] public string map = "Zone01_StarterTown";
        [Tooltip("Centre of the soak square (x, z metres).")] public Vector2 centre;
        [Tooltip("Hostiles the square was sampled for.")] public int count = 2000;
        [Tooltip("Metres between grid points.")] public float spacing = SoakLayout.DefaultSpacing;
        [Tooltip("Grid points with no NavMesh within reach when it was proven (the server skips those).")] public int offNav;
        [Tooltip("When it was proven (local time).")] public string provenAt;
    }
}
