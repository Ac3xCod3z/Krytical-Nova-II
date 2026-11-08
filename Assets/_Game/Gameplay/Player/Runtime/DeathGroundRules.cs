using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>What the body was doing when it died, read where the death is decided (server, or offline).</summary>
    public struct DeathGroundQuery
    {
        /// <summary>A jump was pressed and has not ended (from the crouch before take-off to the landing).</summary>
        public bool JumpPressed;
        /// <summary>The feet have left the ground in that jump (take-off on).</summary>
        public bool JumpAirborne;
        /// <summary>On a wall or in a tree (Wallstride).</summary>
        public bool OnWall;
        /// <summary>A vault leg is running.</summary>
        public bool InVault;
        /// <summary>On the water (Waterstride), or walking off it to the shore.</summary>
        public bool OnWater;
        /// <summary>Metres between the feet and the solid ground under them.</summary>
        public float FeetAboveGround;
    }

    /// <summary>
    /// Owner rule, 5 Nov 2026: a player who dies while not on solid ground de-rezzes where it is and is never dropped:
    /// from the jump's take-off on, on a wall, on the water, in a vault, or 0.15 m or more above the ground under it
    /// (a ledge, the moment after a landing). The crouch before take-off still has the feet down: a normal fall.
    /// </summary>
    public static class DeathGroundRules
    {
        /// <summary>The feet this far (m) or more above the ground under them are off the ground.</summary>
        public const float AirborneHeight = 0.15f;

        public static bool DeRezzes(in DeathGroundQuery q)
            => q.JumpAirborne || q.OnWall || q.InVault || q.OnWater || q.FeetAboveGround >= AirborneHeight;

        /// <summary>
        /// The feet's height over the nearest surface under them: the higher of the solid ground a ray finds below
        /// (<paramref name="groundY"/>, NaN when none) and the walkable ground there (<paramref name="walkableY"/>,
        /// NaN when none), so a walkway over the terrain still counts as ground. 0 when neither is found.
        /// </summary>
        public static float FeetAboveGround(float feetY, float groundY, float walkableY)
        {
            float best = float.NaN;
            if (!float.IsNaN(groundY) && groundY <= feetY + 0.3f) best = groundY;
            if (!float.IsNaN(walkableY) && walkableY <= feetY + 0.3f && (float.IsNaN(best) || walkableY > best)) best = walkableY;
            return float.IsNaN(best) ? 0f : Mathf.Max(0f, feetY - best);
        }
    }
}
