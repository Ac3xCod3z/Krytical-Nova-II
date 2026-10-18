using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// Ways over the <see cref="SurfaceGraph"/>: from the ground up a wall onto a top, from the ground onto a spot on a
    /// wall, from a wall to another spot on it, and from a wall over its edge or down to the ground. Every search runs from
    /// all its ways in at once with the walks at either end in the cost, so it picks the wall, and the place on it, that
    /// makes the whole way shortest.
    /// </summary>
    public static class SurfaceRoute
    {
        public sealed class Route
        {
            /// <summary>The walk on the ground NavMesh to the surface's foot (empty when the way starts on the surface).</summary>
            public Vector3[] Approach = System.Array.Empty<Vector3>();
            /// <summary>The nodes on the surface, in order.</summary>
            public int[] Nodes;
            /// <summary>The walk on the NavMesh from where the way leaves the surface to the place asked for (empty when it ends on the surface).</summary>
            public Vector3[] Departure = System.Array.Empty<Vector3>();
            /// <summary>Where the way leaves the surface, when it does.</summary>
            public bool HasExit;
            public SurfaceGraph.Join Exit;
            /// <summary>Where the way came onto the surface, when it did.</summary>
            public bool HasEntry;
            public SurfaceGraph.Join Entry;
            public float Length;
        }

        private const int MaxEntries = 10;
        private const int MaxExits = 16;
        private const float CornerCost = 1.2f;

        /// <summary>From <paramref name="from"/> (ground) up a surface onto <paramref name="to"/> (a top); null when there is no way.</summary>
        public static Route Plan(SurfaceGraph graph, Vector3 from, Vector3 to, NavMeshQueryFilter filter)
        {
            if (graph == null || graph.NodeCount == 0) return null;
            List<Way> entries = WaysIn(graph, from, filter, false);
            Dictionary<int, Way> exits = WaysOut(graph, to, SurfaceGraph.JoinKind.Top, filter);
            return Finish(graph, entries, exits, to);
        }

        /// <summary>
        /// From <paramref name="from"/> to <paramref name="to"/> (both on the NavMesh, a roof or the ground) over a surface:
        /// on at its foot or over its edge, off the same either way. The way down from a roof to the street, or up.
        /// </summary>
        public static Route Through(SurfaceGraph graph, Vector3 from, Vector3 to, NavMeshQueryFilter filter)
        {
            if (graph == null || graph.NodeCount == 0) return null;
            List<Way> entries = WaysIn(graph, from, filter, true);
            Dictionary<int, Way> exits = WaysOut(graph, to, SurfaceGraph.JoinKind.Top, filter);
            foreach (KeyValuePair<int, Way> ground in WaysOut(graph, to, SurfaceGraph.JoinKind.Ground, filter))
                if (!exits.TryGetValue(ground.Key, out Way known) || ground.Value.Cost < known.Cost) exits[ground.Key] = ground.Value;
            return Finish(graph, entries, exits, to);
        }

        /// <summary>
        /// From <paramref name="from"/> (the ground, or a roof) onto the surface at <paramref name="goal"/>, coming onto it
        /// at its foot or over its edge from above; null when there is no way.
        /// </summary>
        public static Route ToNode(SurfaceGraph graph, Vector3 from, int goal, NavMeshQueryFilter filter)
        {
            if (graph == null || goal < 0 || goal >= graph.NodeCount) return null;
            List<Way> entries = WaysIn(graph, from, filter, true);
            var exits = new Dictionary<int, Way> { [goal] = default };
            return Finish(graph, entries, exits, graph.points[goal]);
        }

        /// <summary>Along the surface from <paramref name="start"/> to <paramref name="goal"/>; null when they are not joined.</summary>
        public static Route Along(SurfaceGraph graph, int start, int goal)
        {
            if (graph == null || start < 0 || goal < 0 || start >= graph.NodeCount || goal >= graph.NodeCount) return null;
            var entries = new List<Way> { new Way { Node = start } };
            var exits = new Dictionary<int, Way> { [goal] = default };
            return Finish(graph, entries, exits, graph.points[goal]);
        }

        /// <summary>
        /// From <paramref name="start"/> on the surface off it at a join of <paramref name="kind"/> (over an edge onto a top,
        /// or down onto the ground) and on to <paramref name="to"/>; null when there is no way.
        /// </summary>
        public static Route Off(SurfaceGraph graph, int start, Vector3 to, SurfaceGraph.JoinKind kind, NavMeshQueryFilter filter)
        {
            if (graph == null || start < 0 || start >= graph.NodeCount) return null;
            var entries = new List<Way> { new Way { Node = start } };
            Dictionary<int, Way> exits = WaysOut(graph, to, kind, filter);
            return Finish(graph, entries, exits, to);
        }

        /// <summary>A way in or out: the node, the join, the walk to or from it and what that walk costs.</summary>
        private struct Way
        {
            public int Node;
            public float Cost;
            public Vector3[] Walk;
            public bool HasJoin;
            public SurfaceGraph.Join Join;
        }

        private static List<Way> WaysIn(SurfaceGraph graph, Vector3 from, NavMeshQueryFilter filter, bool fromAbove)
        {
            var path = new NavMeshPath();
            var ways = new List<Way>();
            var joins = new List<SurfaceGraph.Join>(Nearest(graph, from, SurfaceGraph.JoinKind.Ground, MaxEntries));
            if (fromAbove) joins.AddRange(Nearest(graph, from, SurfaceGraph.JoinKind.Top, MaxEntries));
            foreach (SurfaceGraph.Join join in joins)
            {
                if (!NavMesh.CalculatePath(from, join.navPoint, filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] walk = path.corners;
                ways.Add(new Way { Node = join.node, Cost = Length(walk) + Vector3.Distance(join.navPoint, graph.points[join.node]), Walk = walk, HasJoin = true, Join = join });
            }
            return ways;
        }

        private static Dictionary<int, Way> WaysOut(SurfaceGraph graph, Vector3 to, SurfaceGraph.JoinKind kind, NavMeshQueryFilter filter)
        {
            var path = new NavMeshPath();
            var ways = new Dictionary<int, Way>();
            foreach (SurfaceGraph.Join join in Nearest(graph, to, kind, MaxExits))
            {
                if (!NavMesh.CalculatePath(join.navPoint, to, filter, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                Vector3[] walk = path.corners;
                float cost = Length(walk) + Vector3.Distance(join.navPoint, graph.points[join.node]);
                if (!ways.TryGetValue(join.node, out Way known) || cost < known.Cost)
                    ways[join.node] = new Way { Node = join.node, Cost = cost, Walk = walk, HasJoin = true, Join = join };
            }
            return ways;
        }

        /// <summary>A* from every way in at once; a node that is a way out finishes with its walk.</summary>
        private static Route Finish(SurfaceGraph graph, List<Way> entries, Dictionary<int, Way> exits, Vector3 toward)
        {
            if (entries.Count == 0 || exits.Count == 0) return null;
            int n = graph.NodeCount;
            // Scratch reused from search to search: a whole zone's graph is hundreds of thousands of nodes.
            if (graph.SearchCost == null || graph.SearchCost.Length != n)
            {
                graph.SearchCost = new float[n];
                graph.SearchFrom = new int[n];
                graph.SearchStamp = new int[n];
                graph.SearchGeneration = 0;
            }
            if (graph.SearchGeneration > int.MaxValue - 4) { System.Array.Clear(graph.SearchStamp, 0, n); graph.SearchGeneration = 0; }
            graph.SearchGeneration += 2;
            int open0 = graph.SearchGeneration, shut = open0 + 1;
            float[] g = graph.SearchCost;
            int[] came = graph.SearchFrom;
            int[] stamp = graph.SearchStamp;
            void Touch(int i) { if (stamp[i] < open0) { stamp[i] = open0; g[i] = float.PositiveInfinity; came[i] = -1; } }
            var open = new MinHeap(256);
            var entryOf = new Dictionary<int, Way>();
            foreach (Way way in entries)
            {
                Touch(way.Node);
                if (way.Cost >= g[way.Node]) continue;
                g[way.Node] = way.Cost;
                entryOf[way.Node] = way;
                open.Push(way.Node, way.Cost + Vector3.Distance(graph.points[way.Node], toward));
            }

            float best = float.PositiveInfinity;
            int bestExit = -1;
            while (open.Count > 0)
            {
                open.Pop(out int current, out float f);
                if (f >= best) break;
                if (stamp[current] == shut) continue;
                stamp[current] = shut;
                if (exits.TryGetValue(current, out Way exit) && g[current] + exit.Cost < best)
                {
                    best = g[current] + exit.Cost;
                    bestExit = current;
                }
                Vector3 p = graph.points[current];
                for (int e = graph.edgeStart[current]; e < graph.edgeStart[current + 1]; e++)
                {
                    int next = graph.edgeTo[e];
                    Touch(next);
                    if (stamp[next] == shut) continue;
                    float step = Vector3.Distance(p, graph.points[next]);
                    if (graph.edgeKind[e] == SurfaceGraph.EdgeKind.Corner) step *= CornerCost; // a corner is a little slower
                    float tentative = g[current] + step;
                    if (tentative >= g[next]) continue;
                    g[next] = tentative;
                    came[next] = current;
                    open.Push(next, tentative + Vector3.Distance(graph.points[next], toward));
                }
            }
            if (bestExit < 0) return null;

            var nodes = new List<int>();
            int at = bestExit;
            while (at >= 0) { nodes.Add(at); at = came[at]; }
            nodes.Reverse();
            Way first = entryOf.TryGetValue(nodes[0], out Way way0) ? way0 : default;
            Way last = exits[bestExit];
            return new Route
            {
                Approach = first.Walk ?? System.Array.Empty<Vector3>(),
                HasEntry = first.HasJoin,
                Entry = first.Join,
                Nodes = nodes.ToArray(),
                Departure = last.Walk ?? System.Array.Empty<Vector3>(),
                HasExit = last.HasJoin,
                Exit = last.Join,
                Length = best,
            };
        }

        /// <summary>The joins of a kind nearest a point (from the graph's own grid).</summary>
        private static IEnumerable<SurfaceGraph.Join> Nearest(SurfaceGraph graph, Vector3 point, SurfaceGraph.JoinKind kind, int count)
        {
            var kept = new List<SurfaceGraph.Join>(count);
            graph.JoinsNear(point, kind, count, kept);
            return kept;
        }

        public static float Length(Vector3[] corners)
        {
            float length = 0f;
            if (corners == null) return length;
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
