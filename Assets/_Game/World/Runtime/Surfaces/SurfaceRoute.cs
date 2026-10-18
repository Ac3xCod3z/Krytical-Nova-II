using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// A way from a place on the ground to a place on a roof (or a plateau) that goes up a wall: walked on the NavMesh to a
    /// surface's foot, run up and along the surface over the <see cref="SurfaceGraph"/>, and over the edge onto the top.
    /// One search over every way in and out: the walks at either end are part of the cost, so it picks the wall that
    /// makes the whole way shortest. The proof of stage 1 of "walls as a second ground"; the motor that moves a body along
    /// it comes in stage 2.
    /// </summary>
    public static class SurfaceRoute
    {
        public sealed class Route
        {
            /// <summary>The walk on the ground NavMesh to the surface's foot.</summary>
            public Vector3[] Approach;
            /// <summary>The nodes on the surface, in order.</summary>
            public int[] Nodes;
            /// <summary>The walk on the top's NavMesh from over the edge to the place asked for.</summary>
            public Vector3[] Departure;
            public float Length;
        }

        private const int MaxEntries = 16;
        private const int MaxExits = 32;

        /// <summary>The shortest way from <paramref name="from"/> (ground) to <paramref name="to"/> (a top) over the graph; null when there is none.</summary>
        public static Route Plan(SurfaceGraph graph, Vector3 from, Vector3 to, NavMeshQueryFilter filter)
        {
            if (graph == null || graph.NodeCount == 0) return null;
            var path = new NavMeshPath();

            // The ways in: ground joins, nearest first, each with the walk to it.
            var entries = new List<(int node, float cost, Vector3[] walk)>();
            foreach (SurfaceGraph.Join join in Nearest(graph, from, SurfaceGraph.JoinKind.Ground, MaxEntries))
            {
                if (!NavMesh.CalculatePath(from, join.navPoint, filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] walk = path.corners;
                entries.Add((join.node, Length(walk) + Vector3.Distance(join.navPoint, graph.points[join.node]), walk));
            }
            // The ways out: top joins, nearest the goal first, each with the walk from it.
            var exits = new Dictionary<int, (float cost, Vector3[] walk)>();
            foreach (SurfaceGraph.Join join in Nearest(graph, to, SurfaceGraph.JoinKind.Top, MaxExits))
            {
                if (!NavMesh.CalculatePath(join.navPoint, to, filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] walk = path.corners;
                float cost = Length(walk) + Vector3.Distance(join.navPoint, graph.points[join.node]);
                if (!exits.TryGetValue(join.node, out var known) || cost < known.cost) exits[join.node] = (cost, walk);
            }
            if (entries.Count == 0 || exits.Count == 0) return null;

            // A* from every way in at once; a node that is a way out finishes with its walk.
            int n = graph.NodeCount;
            var g = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.PositiveInfinity; came[i] = -1; }
            var open = new MinHeap(256);
            var entryWalk = new Dictionary<int, Vector3[]>();
            foreach (var (node, cost, walk) in entries)
            {
                if (cost >= g[node]) continue;
                g[node] = cost;
                entryWalk[node] = walk;
                open.Push(node, cost + Vector3.Distance(graph.points[node], to));
            }

            float best = float.PositiveInfinity;
            int bestExit = -1;
            while (open.Count > 0)
            {
                open.Pop(out int current, out float f);
                if (f >= best) break;
                if (closed[current]) continue;
                closed[current] = true;
                if (exits.TryGetValue(current, out var exit) && g[current] + exit.cost < best)
                {
                    best = g[current] + exit.cost;
                    bestExit = current;
                }
                Vector3 p = graph.points[current];
                for (int e = graph.edgeStart[current]; e < graph.edgeStart[current + 1]; e++)
                {
                    int next = graph.edgeTo[e];
                    if (closed[next]) continue;
                    float step = Vector3.Distance(p, graph.points[next]);
                    if (graph.edgeKind[e] == SurfaceGraph.EdgeKind.Corner) step *= 1.2f; // a corner is a little slower
                    float tentative = g[current] + step;
                    if (tentative >= g[next]) continue;
                    g[next] = tentative;
                    came[next] = current;
                    open.Push(next, tentative + Vector3.Distance(graph.points[next], to));
                }
            }
            if (bestExit < 0) return null;

            var nodes = new List<int>();
            int at = bestExit;
            while (at >= 0) { nodes.Add(at); at = came[at]; }
            nodes.Reverse();
            return new Route
            {
                Approach = entryWalk.TryGetValue(nodes[0], out Vector3[] approach) ? approach : new[] { from },
                Nodes = nodes.ToArray(),
                Departure = exits[bestExit].walk,
                Length = best,
            };
        }

        /// <summary>The joins of a kind nearest a point.</summary>
        private static IEnumerable<SurfaceGraph.Join> Nearest(SurfaceGraph graph, Vector3 point, SurfaceGraph.JoinKind kind, int count)
        {
            var list = new List<SurfaceGraph.Join>();
            foreach (SurfaceGraph.Join join in graph.joins)
                if (join.kind == kind) list.Add(join);
            list.Sort((a, b) => (a.navPoint - point).sqrMagnitude.CompareTo((b.navPoint - point).sqrMagnitude));
            // Joins come in clusters along an edge: keep them a little apart so the few tried are different ways.
            var kept = new List<SurfaceGraph.Join>(count);
            foreach (SurfaceGraph.Join join in list)
            {
                bool near = false;
                foreach (SurfaceGraph.Join k in kept)
                    if ((k.navPoint - join.navPoint).sqrMagnitude < 1f) { near = true; break; }
                if (near) continue;
                kept.Add(join);
                if (kept.Count >= count) break;
            }
            return kept;
        }

        public static float Length(Vector3[] corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        /// <summary>A small binary heap of (node, priority), lowest first.</summary>
        private sealed class MinHeap
        {
            private readonly List<(int node, float priority)> items;

            public MinHeap(int capacity) => items = new List<(int, float)>(capacity);

            public int Count => items.Count;

            public void Push(int node, float priority)
            {
                items.Add((node, priority));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].priority <= items[i].priority) break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public void Pop(out int node, out float priority)
            {
                (node, priority) = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, smallest = i;
                    if (l < items.Count && items[l].priority < items[smallest].priority) smallest = l;
                    if (r < items.Count && items[r].priority < items[smallest].priority) smallest = r;
                    if (smallest == i) break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
            }
        }
    }
}
