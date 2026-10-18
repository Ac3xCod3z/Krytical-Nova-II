using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Walls as a second ground (owner, 17 Oct 2026): every place on a steep surface a player can stand, and how they
    /// join. A node is a spot standing out from a wall, a cliff or a rock face, with that surface's normal; an edge says
    /// two nodes are one step apart along the surface (or round a corner); a join ties a node to the ground NavMesh at a
    /// surface's foot or to the walkable top over its edge. Baked in the Editor (<c>SurfaceGraphBuilder</c>) so the server
    /// never scans at run time; routes over it are found by <see cref="Surfaces.SurfaceRoute"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Surface Graph", fileName = "SurfaceGraph")]
    public sealed class SurfaceGraph : ScriptableObject
    {
        public enum EdgeKind : byte
        {
            /// <summary>A step along one face.</summary>
            Face,
            /// <summary>Round a corner onto the next face.</summary>
            Corner,
        }

        public enum JoinKind : byte
        {
            /// <summary>The walkable ground at the surface's foot.</summary>
            Ground,
            /// <summary>The walkable top over the surface's edge (a roof, a ledge, a plateau).</summary>
            Top,
        }

        [Serializable]
        public struct Join
        {
            public int node;
            /// <summary>The NavMesh point the join leads to.</summary>
            public Vector3 navPoint;
            public JoinKind kind;
        }

        [Tooltip("Where the body's root stands, out from the surface.")]
        public Vector3[] points = Array.Empty<Vector3>();
        [Tooltip("The surface's normal at each node, pointing out of it.")]
        public Vector3[] normals = Array.Empty<Vector3>();
        [Tooltip("Node i's edges are edgeTo[edgeStart[i] .. edgeStart[i + 1]).")]
        public int[] edgeStart = { 0 };
        public int[] edgeTo = Array.Empty<int>();
        public EdgeKind[] edgeKind = Array.Empty<EdgeKind>();
        public Join[] joins = Array.Empty<Join>();
        [Tooltip("What was baked, for the record: the regions and the numbers used.")]
        [TextArea] public string bakeNote;

        public int NodeCount => points != null ? points.Length : 0;

        /// <summary>The nearest node to a point, within <paramref name="maxDistance"/>; -1 when there is none.</summary>
        public int Nearest(Vector3 point, float maxDistance)
        {
            int best = -1;
            float bestSq = maxDistance * maxDistance;
            for (int i = 0; i < points.Length; i++)
            {
                float sq = (points[i] - point).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq;
                best = i;
            }
            return best;
        }

        /// <summary>Clears and fills the graph from baked lists (the builder and tests use it).</summary>
        public void Set(Vector3[] nodePoints, Vector3[] nodeNormals, int[] starts, int[] to, EdgeKind[] kinds, Join[] nodeJoins, string note)
        {
            points = nodePoints;
            normals = nodeNormals;
            edgeStart = starts;
            edgeTo = to;
            edgeKind = kinds;
            joins = nodeJoins;
            bakeNote = note;
        }
    }
}
