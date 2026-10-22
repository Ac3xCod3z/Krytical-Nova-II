using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// Waterstride (owner, 22 Oct 2026): running on water, the passive beside Wallstride. When it opens, what it costs,
    /// how fast it goes and how the water is mapped. Every number of the system is here; the motor is
    /// <see cref="PlayerWaterstrideMotor"/>, the map <see cref="KryticalNova.World.WaterMap"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Waterstride Settings", fileName = "WaterstrideSettings")]
    public sealed class WaterstrideSettings : ScriptableObject
    {
        [Header("Arsenal")]
        [Tooltip("Its tile and card in the Arsenal. Given by the icon library (Krytical Nova ▸ Icons ▸ Icon Library), like every skill's: do not set it by hand.")]
        public Sprite icon;

        [Header("When it opens (all three, as Wallstride)")]
        [Min(1)] public int requiredLevel = 60;
        [Tooltip("Run at this rank (its highest is 20).")]
        [Min(1)] public int requiredRunRank = 20;
        [Tooltip("Jump at this rank (its highest is 20).")]
        [Min(1)] public int requiredJumpRank = 20;

        [Header("On the water")]
        [Tooltip("Flux every second while on the water, running or standing. Out of Flux, the player is carried back to the nearest shore.")]
        [Min(0f)] public float fluxPerSecond = 3f;
        [Tooltip("Speed on the water as a share of the player's speed on the ground.")]
        [Range(0.3f, 1.5f)] public float speedShare = 1f;
        [Tooltip("Speed (metres a second) when Flux has run out and the player is carried back to the shore.")]
        [Min(1f)] public float strandedSpeed = 6f;
        [Tooltip("Farthest the shore may be (metres) from the place a run onto the water sets out from it, or lands on it.")]
        [Min(1f)] public float shoreReach = 4f;
        [Tooltip("Most water cells a route may search: a route round a whole lake is longer than this.")]
        [Min(1000)] public int maxRouteCells = 60000;

        [Header("The water map (Krytical Nova ▸ Zone ▸ Water Map)")]
        [Tooltip("Metres a cell of the map covers.")]
        [Min(0.25f)] public float mapCellSize = 1f;
        [Tooltip("Water shallower than this (metres) is waded, not run on: it is ground.")]
        [Min(0f)] public float minDepth = 0.35f;
        [Tooltip("The body, a capsule this wide standing on the water, must fit in a cell.")]
        [Min(0.1f)] public float bodyRadius = 0.3f;
    }
}
