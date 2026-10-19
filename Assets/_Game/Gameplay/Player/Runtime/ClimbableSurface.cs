using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>What a surface is to Wallstride.</summary>
    public enum ClimbableKind : byte
    {
        /// <summary>A wall: climbed to the walkable top over its edge.</summary>
        Wall,
        /// <summary>A tree: climbed to a place to stand in it, off the ground.</summary>
        Tree,
        /// <summary>Never climbed, whatever its shape (a fence that looks like a wall, a statue).</summary>
        Never,
    }

    /// <summary>
    /// Says what a surface is for Wallstride where its shape alone would not (<see cref="WallRunRules"/>): a tree, or a
    /// surface that must never be climbed. A still, solid, steep surface on the wall layers is a wall without one.
    /// Tree colliders near players carry one of their own (<see cref="KryticalNova.Player.TreeProxyField"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClimbableSurface : MonoBehaviour
    {
        public ClimbableKind kind = ClimbableKind.Wall;
        [Tooltip("A tree: the radius of its trunk (metres).")]
        [Min(0.05f)] public float trunkRadius = 0.35f;
        [Tooltip("A tree: how high above its foot the player stands once up it (metres). 0 = the share in the Wallstride settings.")]
        [Min(0f)] public float perchHeight;
        [Tooltip("A tree: its height (metres).")]
        [Min(0f)] public float height = 8f;

        /// <summary>The foot of the trunk, in the world.</summary>
        public Vector3 TrunkBase => transform.position;

        /// <summary>Where a player coming from <paramref name="side"/> (flat, out from the trunk) stands in the tree.</summary>
        public Vector3 PerchFor(Vector3 side, float share, float bodyRadius)
        {
            float up = perchHeight > 0f ? perchHeight : height * share;
            return TrunkBase + side * (trunkRadius + bodyRadius * 0.5f) + Vector3.up * up;
        }
    }
}
