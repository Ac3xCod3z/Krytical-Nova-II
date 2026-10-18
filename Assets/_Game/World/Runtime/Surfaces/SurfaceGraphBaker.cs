using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// Makes a <see cref="SurfaceGraph"/> from what is solid in a region: building walls, rock and the terrain's steep
    /// slopes alike (owner, 18 Oct 2026: wallstride on any surface past a set angle). The surfaces themselves are sampled,
    /// not looked at from outside, so a face behind another one is found too: every steep triangle of the colliders in
    /// the region on a grid of its own, and the terrain's height map. Each sample gives a node standing out from the
    /// surface where the body fits and that is not inside a building; nodes one step apart along the surface (or round a
    /// corner) are joined; nodes at a surface's foot or under its edge are joined to the NavMesh there. Physics and
    /// NavMesh queries only, so it runs in the Editor and in tests alike.
    /// </summary>
    public static class SurfaceGraphBaker
    {
        public struct Options
        {
            /// <summary>Metres between two samples of a wall or a rock.</summary>
            public float Spacing;
            /// <summary>Metres between two samples of the terrain (coarser: a mountain side is large and smooth).</summary>
            public float TerrainSpacing;
            /// <summary>How far the body's root stands out from the surface.</summary>
            public float StandOff;
            /// <summary>Steepest a surface may be and still be ground (degrees from up): steeper is a wall.</summary>
            public float MinAngle;
            /// <summary>And no more than this (90 is upright, past it leans out over the climber).</summary>
            public float MaxAngle;
            /// <summary>The body, as a sphere at the node, must fit there.</summary>
            public float BodyRadius;
            /// <summary>Farthest two nodes on one face may be and still be one step apart (at <see cref="Spacing"/>).</summary>
            public float LinkDistance;
            /// <summary>Most the surface may turn between two nodes of one step (degrees).</summary>
            public float MaxTurn;
            /// <summary>Most it may turn round a corner (degrees).</summary>
            public float CornerTurn;
            /// <summary>A join to the NavMesh is made when it is this near the node (metres).</summary>
            public float JoinReach;
            /// <summary>Colliders lower than this (metres) are props, not walls (the terrain is always sampled).</summary>
            public float MinColliderHeight;
            /// <summary>
            /// Where play is dense (the town), walls are sampled at <see cref="Spacing"/>; outside it (ruins, the skyline,
            /// far walls) at <see cref="OuterSpacing"/>, still climbable with a quarter of the nodes. Empty: everywhere dense.
            /// </summary>
            public Bounds Core;
            public float OuterSpacing;
            /// <summary>Drop every patch of surface no join to the NavMesh reaches (a zone's bake; tests keep everything).</summary>
            public bool DropUnreachable;
            public int Layers;
            public NavMeshQueryFilter Filter;

            public static Options Default => new Options
            {
                Spacing = 0.5f, TerrainSpacing = 1f, StandOff = 0.38f, MinAngle = 45f, MaxAngle = 110f, BodyRadius = 0.28f,
                LinkDistance = 0.8f, MaxTurn = 50f, CornerTurn = 110f, JoinReach = 1.4f, MinColliderHeight = 2.2f, OuterSpacing = 1f,
                Layers = Physics.DefaultRaycastLayers,
                Filter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = NavMesh.AllAreas },
            };
        }

        private const float CornerLinkFactor = 1.7f;
        private static readonly int WaterLayer = LayerMask.NameToLayer("Water");
        private const float EnclosedProbe = 4f;
        private const float MinTriangleArea = 0.005f;

        public struct Result
        {
            public Vector3[] Points, Normals;
            public int[] EdgeStart, EdgeTo;
            public SurfaceGraph.EdgeKind[] EdgeKind;
            public SurfaceGraph.Join[] Joins;
            /// <summary>Samples taken of steep surfaces, and why some gave no node.</summary>
            public int Samples, Colliders, Terrains, RejectedBody, RejectedSolid, RejectedInside, Unreachable;
        }

        /// <summary>Bakes every region into one graph.</summary>
        public static Result Bake(IReadOnlyList<Bounds> regions, Options o)
        {
            // Back faces are seen while baking, so a spot inside a solid (a thick wall, a roof slab) can be told apart.
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try { return BakeSeeingBackfaces(regions, o); }
            finally { Physics.queriesHitBackfaces = backfaces; }
        }

        private static Result BakeSeeingBackfaces(IReadOnlyList<Bounds> regions, Options o)
        {
            var result = new Result();
            Physics.SyncTransforms();
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            var scales = new List<float>(); // each node's sample spacing over the wall spacing (terrain is coarser)
            var taken = new Dictionary<Vector3Int, List<int>>();
            float cell = o.Spacing * 0.9f; // samples closer than this along the same face are one node (trims, frames)

            void Offer(Vector3 surface, Vector3 n, float scale, Bounds region)
            {
                if (!region.Contains(surface)) return;
                float angle = Vector3.Angle(n, Vector3.up);
                if (angle < o.MinAngle || angle > o.MaxAngle) return;
                result.Samples++;
                Vector3 p = surface + n * o.StandOff;
                if (Taken(taken, cell, p, n, points, normals)) return;
                if (Physics.CheckSphere(p, o.BodyRadius, o.Layers, QueryTriggerInteraction.Ignore)) { result.RejectedBody++; return; }
                if (InSolid(p, n, o.Layers)) { result.RejectedSolid++; return; }
                if (Enclosed(p, n, o.Layers)) { result.RejectedInside++; return; }
                Add(taken, cell, p, points.Count);
                points.Add(p);
                normals.Add(n);
                scales.Add(scale);
            }

            var seen = new HashSet<Collider>();
            var triangle = new Vector3[3];
            foreach (Bounds region in regions)
            {
                foreach (Collider collider in Physics.OverlapBox(region.center, region.extents, Quaternion.identity, o.Layers, QueryTriggerInteraction.Ignore))
                {
                    if (!seen.Add(collider) || !Solid(collider)) continue;
                    if (collider is TerrainCollider terrainCollider)
                    {
                        result.Terrains++;
                        SampleTerrain(terrainCollider, region, o, (s, n) => Offer(s, n, o.TerrainSpacing / o.Spacing, region));
                        continue;
                    }
                    if (collider.bounds.size.y < o.MinColliderHeight) continue; // a prop: run round it, vault it
                    if (collider.gameObject.layer == WaterLayer) continue; // water is never a wall
                    result.Colliders++;
                    bool dense = o.Core.size == Vector3.zero || o.Core.Intersects(collider.bounds);
                    float spacing = dense ? o.Spacing : Mathf.Max(o.Spacing, o.OuterSpacing);
                    ForEachTriangle(collider, triangle, tri => SampleTriangle(tri, spacing, (s, n) => Offer(s, n, spacing / o.Spacing, region)));
                }
                seen.Clear();
            }

            // Edges: neighbours along the surface, and round its corners. Searched in a grid of the farthest reach.
            float reach = o.LinkDistance * CornerLinkFactor * Mathf.Max(1f, Mathf.Max(o.TerrainSpacing, o.OuterSpacing) / o.Spacing);
            var near = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < points.Count; i++) Add(near, reach, points[i], i);
            var starts = new int[points.Count + 1];
            var to = new List<int>(points.Count * 8);
            var kinds = new List<SurfaceGraph.EdgeKind>(points.Count * 8);
            float probe = o.BodyRadius * 0.6f;
            for (int i = 0; i < points.Count; i++)
            {
                starts[i] = to.Count;
                Vector3 p = points[i];
                Vector3Int c = Key(p, reach);
                for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                for (int z = -1; z <= 1; z++)
                {
                    if (!near.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out List<int> list)) continue;
                    foreach (int j in list)
                    {
                        if (j == i) continue;
                        float scale = Mathf.Max(scales[i], scales[j]);
                        float d = Vector3.Distance(p, points[j]);
                        float turn = Vector3.Angle(normals[i], normals[j]);
                        SurfaceGraph.EdgeKind kind;
                        if (turn <= o.MaxTurn && d <= o.LinkDistance * scale) kind = SurfaceGraph.EdgeKind.Face;
                        else if (turn > o.MaxTurn && turn <= o.CornerTurn && d <= o.LinkDistance * CornerLinkFactor * scale) kind = SurfaceGraph.EdgeKind.Corner;
                        else continue;
                        if (kind == SurfaceGraph.EdgeKind.Face ? Blocked(p, points[j], probe, o.Layers)
                            : BlockedRound(p, points[j], normals[i], normals[j], o.StandOff, probe, o.Layers)) continue;
                        to.Add(j);
                        kinds.Add(kind);
                    }
                }
            }
            starts[points.Count] = to.Count;

            SurfaceGraph.Join[] joins = FindJoins(points.ToArray(), normals.ToArray(), o);
            if (o.DropUnreachable) return Reachable(points, normals, starts, to, kinds, joins, result);
            result.Points = points.ToArray();
            result.Normals = normals.ToArray();
            result.EdgeStart = starts;
            result.EdgeTo = to.ToArray();
            result.EdgeKind = kinds.ToArray();
            result.Joins = joins;
            return result;
        }

        /// <summary>
        /// Keeps only the patches of surface a player can get onto: those joined to the ground or a top somewhere. A face
        /// no join reaches (the back of the skyline, scenery out of bounds) is dropped, with its nodes and edges.
        /// </summary>
        private static Result Reachable(List<Vector3> points, List<Vector3> normals, int[] starts, List<int> to,
            List<SurfaceGraph.EdgeKind> kinds, SurfaceGraph.Join[] joins, Result result)
        {
            int n = points.Count;
            var keep = new bool[n];
            var queue = new Queue<int>();
            foreach (SurfaceGraph.Join join in joins)
                if (!keep[join.node]) { keep[join.node] = true; queue.Enqueue(join.node); }
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int e = starts[i]; e < starts[i + 1]; e++)
                {
                    int j = to[e];
                    if (keep[j]) continue;
                    keep[j] = true;
                    queue.Enqueue(j);
                }
            }
            var remap = new int[n];
            var p = new List<Vector3>(n);
            var nn = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                remap[i] = keep[i] ? p.Count : -1;
                if (!keep[i]) continue;
                p.Add(points[i]);
                nn.Add(normals[i]);
            }
            var s = new int[p.Count + 1];
            var t = new List<int>(to.Count);
            var k = new List<SurfaceGraph.EdgeKind>(to.Count);
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                s[remap[i]] = t.Count;
                for (int e = starts[i]; e < starts[i + 1]; e++)
                {
                    if (!keep[to[e]]) continue;
                    t.Add(remap[to[e]]);
                    k.Add(kinds[e]);
                }
            }
            s[p.Count] = t.Count;
            var j2 = new List<SurfaceGraph.Join>(joins.Length);
            foreach (SurfaceGraph.Join join in joins)
                if (keep[join.node]) j2.Add(new SurfaceGraph.Join { node = remap[join.node], navPoint = join.navPoint, kind = join.kind });
            result.Unreachable = n - p.Count;
            result.Points = p.ToArray();
            result.Normals = nn.ToArray();
            result.EdgeStart = s;
            result.EdgeTo = t.ToArray();
            result.EdgeKind = k.ToArray();
            result.Joins = j2.ToArray();
            return result;
        }

        /// <summary>Still and solid: not a trigger, not moved by physics, not something that can be hurt or used.</summary>
        private static bool Solid(Collider collider)
        {
            if (collider == null || collider.isTrigger || !collider.enabled) return false;
            Rigidbody body = collider.attachedRigidbody;
            if (body != null && !body.isKinematic) return false;
            if (collider.GetComponentInParent<KryticalNova.Progression.IDamageable>() != null) return false;
            if (collider.GetComponentInParent<Animator>() != null) return false; // characters, creatures
            return true;
        }

        /// <summary>Every triangle of a collider's surface, in world space.</summary>
        private static void ForEachTriangle(Collider collider, Vector3[] tri, System.Action<Vector3[]> use)
        {
            Transform t = collider.transform;
            if (collider is BoxCollider box)
            {
                Vector3 c = box.center, e = box.size * 0.5f;
                var corners = new Vector3[8];
                for (int i = 0; i < 8; i++)
                    corners[i] = t.TransformPoint(c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z));
                int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
                for (int f = 0; f < 6; f++)
                {
                    int a = faces[f * 4], b = faces[f * 4 + 1], cc = faces[f * 4 + 2], d = faces[f * 4 + 3];
                    tri[0] = corners[a]; tri[1] = corners[b]; tri[2] = corners[cc];
                    OrientOut(tri, t.TransformPoint(c)); use(tri);
                    tri[0] = corners[a]; tri[1] = corners[cc]; tri[2] = corners[d];
                    OrientOut(tri, t.TransformPoint(c)); use(tri);
                }
                return;
            }
            if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            {
                Mesh mesh = meshCollider.sharedMesh;
                Vector3[] vertices;
                int[] indices;
                try
                {
                    vertices = mesh.vertices;
                    indices = mesh.triangles;
                }
                catch { return; } // not readable here
                if (vertices == null || indices == null) return;
                Matrix4x4 m = t.localToWorldMatrix;
                bool flipped = m.determinant < 0f;
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    tri[0] = m.MultiplyPoint3x4(vertices[indices[i]]);
                    tri[1] = m.MultiplyPoint3x4(vertices[indices[flipped ? i + 2 : i + 1]]);
                    tri[2] = m.MultiplyPoint3x4(vertices[indices[flipped ? i + 1 : i + 2]]);
                    use(tri);
                }
            }
            // Spheres, capsules and wheels are props and bodies, not walls.
        }

        /// <summary>A box's triangle is wound so its normal points away from the box's centre.</summary>
        private static void OrientOut(Vector3[] tri, Vector3 centre)
        {
            Vector3 n = Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]);
            if (Vector3.Dot(n, (tri[0] + tri[1] + tri[2]) / 3f - centre) < 0f) (tri[1], tri[2]) = (tri[2], tri[1]);
        }

        /// <summary>Points spread over one triangle at about <paramref name="spacing"/>, with its outward normal.</summary>
        private static void SampleTriangle(Vector3[] tri, float spacing, System.Action<Vector3, Vector3> offer)
        {
            Vector3 a = tri[0], b = tri[1], c = tri[2];
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float area = cross.magnitude * 0.5f;
            if (area < MinTriangleArea) return;
            Vector3 n = cross.normalized;
            // A grid laid along the triangle's longest edge, kept where it falls inside the triangle.
            Vector3 ab = b - a, ac = c - a;
            float lab = ab.magnitude, lac = ac.magnitude, lbc = (c - b).magnitude;
            Vector3 origin = a, u = ab;
            if (lac > lab && lac >= lbc) u = ac;
            else if (lbc > lab && lbc > lac) { origin = b; u = c - b; }
            Vector3 uDir = u.normalized;
            Vector3 vDir = Vector3.Cross(n, uDir).normalized;
            float uMin = float.MaxValue, uMax = float.MinValue, vMin = float.MaxValue, vMax = float.MinValue;
            foreach (Vector3 corner in tri)
            {
                Vector3 d = corner - origin;
                float pu = Vector3.Dot(d, uDir), pv = Vector3.Dot(d, vDir);
                uMin = Mathf.Min(uMin, pu); uMax = Mathf.Max(uMax, pu);
                vMin = Mathf.Min(vMin, pv); vMax = Mathf.Max(vMax, pv);
            }
            bool any = false;
            for (float pv = vMin + spacing * 0.5f; pv < vMax; pv += spacing)
                for (float pu = uMin + spacing * 0.5f; pu < uMax; pu += spacing)
                {
                    Vector3 p = origin + uDir * pu + vDir * pv;
                    if (!Inside(p, a, b, c, n)) continue;
                    any = true;
                    offer(p, n);
                }
            if (!any && area > spacing * spacing * 0.25f) offer((a + b + c) / 3f, n); // a sliver between the grid lines
        }

        private static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 n) =>
            Vector3.Dot(Vector3.Cross(b - a, p - a), n) >= 0f && Vector3.Dot(Vector3.Cross(c - b, p - b), n) >= 0f
            && Vector3.Dot(Vector3.Cross(a - c, p - c), n) >= 0f;

        /// <summary>The terrain's steep slopes, from its height map at <see cref="Options.TerrainSpacing"/>.</summary>
        private static void SampleTerrain(TerrainCollider collider, Bounds region, Options o, System.Action<Vector3, Vector3> offer)
        {
            var terrain = collider.GetComponent<Terrain>();
            TerrainData data = collider.terrainData;
            if (data == null) return;
            Vector3 origin = collider.transform.position;
            Vector3 size = data.size;
            float xMin = Mathf.Max(region.min.x, origin.x), xMax = Mathf.Min(region.max.x, origin.x + size.x);
            float zMin = Mathf.Max(region.min.z, origin.z), zMax = Mathf.Min(region.max.z, origin.z + size.z);
            float step = Mathf.Max(0.25f, o.TerrainSpacing);
            for (float x = xMin; x <= xMax; x += step)
                for (float z = zMin; z <= zMax; z += step)
                {
                    float u = (x - origin.x) / size.x, v = (z - origin.z) / size.z;
                    if (data.GetSteepness(u, v) < o.MinAngle) continue;
                    float y = data.GetInterpolatedHeight(u, v) + origin.y;
                    Vector3 n = data.GetInterpolatedNormal(u, v);
                    if (terrain != null) n = terrain.transform.rotation * n;
                    offer(new Vector3(x, y, z), n.normalized);
                }
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

        private const float SolidProbe = 3f;
        private const int SolidVotes = 3;

        /// <summary>
        /// Inside a solid (owner, 18 Oct 2026: places "inside what looks like roofs"): a spot within a thick wall or a
        /// roof slab touches no surface, so the body check passes it, but looking out from it most ways the first thing
        /// met is the inside of a surface. A spot out in the air sees the front of what it meets, or nothing.
        /// </summary>
        private static bool InSolid(Vector3 p, Vector3 n, int layers)
        {
            Vector3 along = Vector3.Cross(Vector3.up, n);
            along = along.sqrMagnitude > 1e-4f ? along.normalized : Vector3.Cross(Vector3.forward, n).normalized;
            int inside = 0;
            if (SeesInside(p, Vector3.up, layers)) inside++;
            if (SeesInside(p, Vector3.down, layers)) inside++;
            if (SeesInside(p, along, layers)) inside++;
            if (SeesInside(p, -along, layers)) inside++;
            if (SeesInside(p, n, layers)) inside++;
            return inside >= SolidVotes;
        }

        private static bool SeesInside(Vector3 p, Vector3 direction, int layers) =>
            Physics.Raycast(p, direction, out RaycastHit hit, SolidProbe, layers, QueryTriggerInteraction.Ignore)
            && Vector3.Dot(hit.normal, direction) > 0.05f;

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
