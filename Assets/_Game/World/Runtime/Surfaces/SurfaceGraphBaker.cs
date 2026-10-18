using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// Makes a <see cref="SurfaceGraph"/> from the colliders in a region: walls, cliffs and rock faces alike, since it
    /// follows the surface rather than assuming a plane. Rays are cast at the region from eight directions on a grid;
    /// every steep surface they meet gives a node standing out from it, where the body fits and that is not inside a
    /// building; nodes one step apart along the surface (or round a corner) are joined; nodes at a surface's foot or
    /// under its edge are joined to the NavMesh there. Physics and NavMesh queries only, so it runs in the Editor and in
    /// tests alike.
    /// </summary>
    public static class SurfaceGraphBaker
    {
        public struct Options
        {
            /// <summary>Metres between two rays of the grid.</summary>
            public float Spacing;
            /// <summary>How far the body's root stands out from the surface.</summary>
            public float StandOff;
            /// <summary>Steepest a surface may be and still be ground (degrees from up): steeper is a wall.</summary>
            public float MinAngle;
            /// <summary>And no more than this (90 is upright, past it leans out over the climber).</summary>
            public float MaxAngle;
            /// <summary>The body, as a sphere at the node, must fit there.</summary>
            public float BodyRadius;
            /// <summary>Farthest two nodes on one face may be and still be one step apart.</summary>
            public float LinkDistance;
            /// <summary>Most the surface may turn between two nodes of one step (degrees).</summary>
            public float MaxTurn;
            /// <summary>Most it may turn round a corner (degrees).</summary>
            public float CornerTurn;
            /// <summary>A join to the NavMesh is made when it is this near the node (metres).</summary>
            public float JoinReach;
            public int Layers;
            public NavMeshQueryFilter Filter;

            public static Options Default => new Options
            {
                Spacing = 0.5f, StandOff = 0.38f, MinAngle = 45f, MaxAngle = 110f, BodyRadius = 0.28f, LinkDistance = 0.8f,
                MaxTurn = 50f, CornerTurn = 110f, JoinReach = 1.4f, Layers = Physics.DefaultRaycastLayers,
                Filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas },
            };
        }

        private const int Azimuths = 8;
        private const float CornerLinkFactor = 1.7f;
        private const float EnclosedProbe = 4f;
        private const float GrazingDot = 0.2f;

        public struct Result
        {
            public Vector3[] Points, Normals;
            public int[] EdgeStart, EdgeTo;
            public SurfaceGraph.EdgeKind[] EdgeKind;
            public SurfaceGraph.Join[] Joins;
            public int Rays, Hits, RejectedBody, RejectedInside;
        }

        /// <summary>Bakes every region into one graph.</summary>
        public static Result Bake(IReadOnlyList<Bounds> regions, Options o)
        {
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            var cells = new Dictionary<Vector3Int, List<int>>();
            float cell = o.Spacing * 0.7f;
            var result = new Result();
            Physics.SyncTransforms();

            foreach (Bounds region in regions)
                for (int a = 0; a < Azimuths; a++)
                {
                    float yaw = a * (360f / Azimuths) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    var across = new Vector3(-dir.z, 0f, dir.x);
                    float depth = Mathf.Abs(region.extents.x * dir.x) + Mathf.Abs(region.extents.z * dir.z);
                    float width = Mathf.Abs(region.extents.x * across.x) + Mathf.Abs(region.extents.z * across.z);
                    Vector3 start = region.center - dir * (depth + 2f);
                    for (float u = -width; u <= width; u += o.Spacing)
                        for (float y = region.min.y; y <= region.max.y; y += o.Spacing)
                        {
                            Vector3 origin = new Vector3(start.x, y, start.z) + across * u;
                            result.Rays++;
                            if (!Physics.Raycast(origin, dir, out RaycastHit hit, 2f * depth + 4f, o.Layers, QueryTriggerInteraction.Ignore)) continue;
                            if (!region.Contains(hit.point)) continue;
                            Vector3 n = hit.normal;
                            float angle = Vector3.Angle(n, Vector3.up);
                            if (angle < o.MinAngle || angle > o.MaxAngle) continue;
                            if (Vector3.Dot(n, -dir) < GrazingDot) continue;
                            result.Hits++;
                            Vector3 p = hit.point + n * o.StandOff;
                            if (Taken(cells, cell, p, n, points, normals)) continue;
                            if (Physics.CheckSphere(p, o.BodyRadius, o.Layers, QueryTriggerInteraction.Ignore)) { result.RejectedBody++; continue; }
                            if (Enclosed(p, n, o.Layers)) { result.RejectedInside++; continue; }
                            Add(cells, cell, p, points.Count);
                            points.Add(p);
                            normals.Add(n);
                        }
                }

            // Edges: neighbours along the surface, and round its corners.
            var starts = new int[points.Count + 1];
            var to = new List<int>(points.Count * 8);
            var kinds = new List<SurfaceGraph.EdgeKind>(points.Count * 8);
            float reach = o.LinkDistance * CornerLinkFactor;
            int span = Mathf.CeilToInt(reach / cell);
            float probe = o.BodyRadius * 0.6f;
            for (int i = 0; i < points.Count; i++)
            {
                starts[i] = to.Count;
                Vector3 p = points[i];
                Vector3Int c = Key(p, cell);
                for (int x = -span; x <= span; x++)
                for (int y = -span; y <= span; y++)
                for (int z = -span; z <= span; z++)
                {
                    if (!cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out List<int> list)) continue;
                    foreach (int j in list)
                    {
                        if (j == i) continue;
                        float d = Vector3.Distance(p, points[j]);
                        float turn = Vector3.Angle(normals[i], normals[j]);
                        SurfaceGraph.EdgeKind kind;
                        if (turn <= o.MaxTurn && d <= o.LinkDistance) kind = SurfaceGraph.EdgeKind.Face;
                        else if (turn > o.MaxTurn && turn <= o.CornerTurn && d <= reach) kind = SurfaceGraph.EdgeKind.Corner;
                        else continue;
                        if (kind == SurfaceGraph.EdgeKind.Face ? Blocked(p, points[j], probe, o.Layers)
                            : BlockedRound(p, points[j], normals[i], normals[j], o.StandOff, probe, o.Layers)) continue;
                        to.Add(j);
                        kinds.Add(kind);
                    }
                }
            }
            starts[points.Count] = to.Count;

            result.Points = points.ToArray();
            result.Normals = normals.ToArray();
            result.EdgeStart = starts;
            result.EdgeTo = to.ToArray();
            result.EdgeKind = kinds.ToArray();
            result.Joins = FindJoins(result.Points, result.Normals, o);
            return result;
        }

        /// <summary>
        /// Joins to the NavMesh: the walkable ground just below a node at a surface's foot, and the walkable top just
        /// over the edge above a node near it (a roof, a plateau).
        /// </summary>
        private static SurfaceGraph.Join[] FindJoins(Vector3[] points, Vector3[] normals, Options o)
        {
            var joins = new List<SurfaceGraph.Join>();
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 p = points[i];
                Vector3 outward = new Vector3(normals[i].x, 0f, normals[i].z);
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.zero;

                // The ground at the foot: a little out from the surface, at most a step below the node.
                if (NavMesh.SamplePosition(p + outward * 0.3f - Vector3.up * 0.6f, out NavMeshHit ground, o.JoinReach * 0.7f, o.Filter)
                    && ground.position.y <= p.y + 0.1f && p.y - ground.position.y <= o.JoinReach && Flat(ground.position, p) <= o.JoinReach)
                    joins.Add(new SurfaceGraph.Join { node = i, navPoint = ground.position, kind = SurfaceGraph.JoinKind.Ground });

                // The top: over the edge, into the surface and up, a little above the node. The way there is the
                // mantle's: straight up past the edge first, then in over it, so the check follows the same two legs.
                if (NavMesh.SamplePosition(p - outward * 1.1f + Vector3.up * 1.2f, out NavMeshHit top, o.JoinReach * 0.6f, o.Filter)
                    && top.position.y > p.y + 0.3f && top.position.y - p.y <= o.JoinReach + 0.4f)
                {
                    Vector3 lifted = new Vector3(p.x, top.position.y + 0.6f, p.z);
                    if (!Physics.Linecast(p, lifted, o.Layers, QueryTriggerInteraction.Ignore)
                        && !Physics.Linecast(lifted, top.position + Vector3.up * 0.6f, o.Layers, QueryTriggerInteraction.Ignore))
                        joins.Add(new SurfaceGraph.Join { node = i, navPoint = top.position, kind = SurfaceGraph.JoinKind.Top });
                }
            }
            return joins.ToArray();
        }

        private static bool Blocked(Vector3 a, Vector3 b, float radius, int layers)
        {
            Vector3 way = b - a;
            float length = way.magnitude;
            return length > 1e-4f && Physics.SphereCast(a, radius, way / length, out _, length, layers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Round a corner the way a body goes: out to a point beside the corner, then on to the next face. A straight line
        /// between two nodes on either side of an outside corner can cut across the corner itself, depending only on
        /// where the samples fell; this way does not.
        /// </summary>
        private static bool BlockedRound(Vector3 a, Vector3 b, Vector3 na, Vector3 nb, float standOff, float radius, int layers)
        {
            Vector3 outward = na + nb;
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : na;
            Vector3 beside = (a + b) * 0.5f + outward * standOff;
            return Blocked(a, beside, radius, layers) || Blocked(beside, b, radius, layers);
        }

        /// <summary>
        /// Inside a building (in through a window or a door): something overhead, something below and something on
        /// both sides along the surface, all near. A spot out on a face has open air on at least one of those.
        /// </summary>
        private static bool Enclosed(Vector3 p, Vector3 n, int layers)
        {
            Vector3 along = Vector3.Cross(Vector3.up, n);
            if (along.sqrMagnitude < 1e-4f) return false;
            along.Normalize();
            return Physics.Raycast(p, Vector3.up, EnclosedProbe, layers, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(p, Vector3.down, EnclosedProbe, layers, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(p, along, EnclosedProbe, layers, QueryTriggerInteraction.Ignore)
                && Physics.Raycast(p, -along, EnclosedProbe, layers, QueryTriggerInteraction.Ignore);
        }

        private static bool Taken(Dictionary<Vector3Int, List<int>> cells, float cell, Vector3 p, Vector3 n, List<Vector3> points, List<Vector3> normals)
        {
            Vector3Int c = Key(p, cell);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out List<int> list)) continue;
                foreach (int j in list)
                    if ((points[j] - p).sqrMagnitude < cell * cell && Vector3.Dot(normals[j], n) > 0.7f) return true;
            }
            return false;
        }

        private static void Add(Dictionary<Vector3Int, List<int>> cells, float cell, Vector3 p, int index)
        {
            Vector3Int c = Key(p, cell);
            if (!cells.TryGetValue(c, out List<int> list)) cells[c] = list = new List<int>(2);
            list.Add(index);
        }

        private static Vector3Int Key(Vector3 p, float cell) =>
            new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
