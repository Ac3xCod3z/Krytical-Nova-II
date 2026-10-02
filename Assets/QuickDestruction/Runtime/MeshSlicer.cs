using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction.Runtime
{
    /// <summary>
    /// Editor-baked character of newly exposed fracture surfaces. The outer/source mesh
    /// remains untouched; both fracture halves receive the same generated cap surface.
    /// </summary>
    public enum BakedCutDetailStyle
    {
        None,
        ConcreteChips,
        WoodSplinters
    }

    /// <summary>
    /// Editable mesh representation used by the fracture solver. The last submesh is always
    /// the interior (cut surface) submesh so exterior material IDs are preserved.
    /// </summary>
    // The editor assembly consumes this solver representation when previewing and
    // baking. It deliberately lives in Runtime so the shared geometry code has a
    // single implementation; keep the type public across the assembly boundary.
    public class WorkMesh
    {
        // Intersection vertices are calculated through different triangle-edge paths.
        // At building scale their mathematically identical positions can differ by a few
        // last float bits after rebasing for prefab export.  Two parts per million keeps
        // those cut seams connected without bridging authored gaps (the modular contact
        // matcher uses much larger, explicitly bounded tolerances for that job).
        const float TopologyWeldRelativeTolerance = 2e-6f;

        public int StructuralGroup = -1;
        public List<Vector3> Positions = new List<Vector3>();
        public List<Vector3> Normals = new List<Vector3>();
        public List<Vector2> UVs = new List<Vector2>();
        public List<int>[] Submeshes;

        public int SubmeshCount => Submeshes.Length;
        public int InteriorSubmesh => Submeshes.Length - 1;

        public WorkMesh(int submeshCount)
        {
            Submeshes = new List<int>[submeshCount];
            for (int i = 0; i < submeshCount; i++)
                Submeshes[i] = new List<int>();
        }

        public static WorkMesh FromMesh(Mesh mesh)
        {
            // +1 submesh for the interior surface.
            var wm = new WorkMesh(mesh.subMeshCount + 1);
            mesh.GetVertices(wm.Positions);
            mesh.GetNormals(wm.Normals);
            mesh.GetUVs(0, wm.UVs);

            if (wm.Normals.Count != wm.Positions.Count)
            {
                wm.Normals.Clear();
                for (int i = 0; i < wm.Positions.Count; i++) wm.Normals.Add(Vector3.up);
            }
            if (wm.UVs.Count != wm.Positions.Count)
            {
                wm.UVs.Clear();
                for (int i = 0; i < wm.Positions.Count; i++) wm.UVs.Add(Vector2.zero);
            }

            for (int s = 0; s < mesh.subMeshCount; s++)
                wm.Submeshes[s].AddRange(mesh.GetTriangles(s));
            return wm;
        }

        public Mesh ToMesh(string name)
        {
            RemoveCoincidentTriangles();
            // Never delete a cap merely because its projection overlaps another
            // coplanar triangle. A previous z-fighting cleanup used that heuristic,
            // but partial overlap is not proof that the whole triangle is redundant:
            // removing it turns an otherwise filled Full Solid fragment into an open
            // shell. Exact coincident faces are already removed safely above by their
            // welded vertex triplet. The slicer now owns cap construction/repair, so
            // mesh export must preserve every non-duplicate triangle it produced.
            var mesh = new Mesh { name = name };
            if (Positions.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, UVs);
            mesh.subMeshCount = SubmeshCount;
            for (int s = 0; s < SubmeshCount; s++)
                mesh.SetTriangles(Submeshes[s], s, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// Removes exact/near-exact duplicate faces left by imported overlapping shells or
        /// by coincident cap repair passes. Material order is deterministic: the first
        /// source submesh wins, so a later interior cap cannot z-fight an exterior wall.
        /// </summary>
        void RemoveCoincidentTriangles()
        {
            if (Positions.Count < 3) return;
            // This cleanup runs after the fracture result has already passed its
            // watertightness gate.  Its deliberately wider weld tolerance can regard two
            // very close authored/cap faces as the same triangle even though they are
            // distinct at the solver's manifold tolerance.  Removing one then turns a
            // valid closed fragment into the open strips seen only after Bake.  Keep the
            // cleanup transactional for closed inputs: duplicates may be removed only if
            // the exported topology remains closed.
            bool preserveClosedTopology = IsClosedManifold();
            List<int>[] originalSubmeshes = null;
            if (preserveClosedTopology)
            {
                originalSubmeshes = new List<int>[SubmeshCount];
                for (int s = 0; s < SubmeshCount; s++)
                    originalSubmeshes[s] = new List<int>(Submeshes[s]);
            }

            Bounds bounds = ComputeBounds();
            float cell = Mathf.Max(bounds.size.magnitude * 2e-6f, 1e-7f);
            // Neighborhood weld: coincident vertices straddling a quantization cell
            // boundary must still merge, or their triangles survive as false duplicates.
            var welder = new PositionWelder(cell);
            var welded = new int[Positions.Count];
            for (int i = 0; i < Positions.Count; i++)
                welded[i] = welder.Weld(Positions[i]);

            var seen = new HashSet<TriangleKey>();
            for (int s = 0; s < SubmeshCount; s++)
            {
                List<int> source = Submeshes[s];
                var clean = new List<int>(source.Count);
                for (int i = 0; i + 2 < source.Count; i += 3)
                {
                    int wa = welded[source[i]];
                    int wb = welded[source[i + 1]];
                    int wc = welded[source[i + 2]];
                    if (wa == wb || wb == wc || wc == wa) continue;
                    var key = new TriangleKey(wa, wb, wc);
                    if (!seen.Add(key)) continue;
                    clean.Add(source[i]);
                    clean.Add(source[i + 1]);
                    clean.Add(source[i + 2]);
                }
                Submeshes[s] = clean;
            }

            if (preserveClosedTopology && !IsClosedManifold())
                Submeshes = originalSubmeshes;
        }

        /// <summary>
        /// Removes interior (cut-surface) triangles that are coplanar with, face the same
        /// way as, and overlap already kept geometry. Repair passes over a damaged cut
        /// contour can lay caps across the same region with different triangulations;
        /// exact-duplicate removal cannot catch those and at runtime they z-fight.
        /// Exterior triangles are never removed. Smaller cap triangles are kept first so
        /// a finely triangulated correct cap survives while an oversized stray blade
        /// covering it is dropped.
        /// </summary>
        void RemoveOverlappingCoplanarCapTriangles()
        {
            int interior = InteriorSubmesh;
            if (interior < 0 || Submeshes[interior].Count < 3) return;

            Bounds bounds = ComputeBounds();
            float scale = Mathf.Max(bounds.size.magnitude, 1e-3f);
            float planeTolerance = Mathf.Max(scale * 2e-5f, 2e-6f);
            float dCell = planeTolerance * 2f;

            var keptA = new List<Vector3>();
            var keptB = new List<Vector3>();
            var keptC = new List<Vector3>();
            var keptN = new List<Vector3>();
            var cells = new Dictionary<PlaneCellKey, List<int>>();

            PlaneCellKey KeyOf(Vector3 n, float d, int dx, int dy, int dz, int dw)
            {
                return new PlaneCellKey(
                    Mathf.RoundToInt(n.x * 200f) + dx,
                    Mathf.RoundToInt(n.y * 200f) + dy,
                    Mathf.RoundToInt(n.z * 200f) + dz,
                    Mathf.RoundToInt(d / dCell) + dw);
            }

            void Register(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float d)
            {
                int id = keptA.Count;
                keptA.Add(a);
                keptB.Add(b);
                keptC.Add(c);
                keptN.Add(n);
                PlaneCellKey key = KeyOf(n, d, 0, 0, 0, 0);
                if (!cells.TryGetValue(key, out List<int> list))
                {
                    list = new List<int>(2);
                    cells.Add(key, list);
                }
                list.Add(id);
            }

            bool OverlapsKept(Vector3 a, Vector3 b, Vector3 c, Vector3 n, float d)
            {
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                for (int dw = -1; dw <= 1; dw++)
                {
                    if (!cells.TryGetValue(KeyOf(n, d, dx, dy, dz, dw),
                            out List<int> list))
                        continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        int id = list[k];
                        Vector3 keptNormal = keptN[id];
                        if (Vector3.Dot(keptNormal, n) < 0.9999f) continue;
                        Vector3 keptPoint = keptA[id];
                        if (Mathf.Abs(Vector3.Dot(keptNormal, a - keptPoint)) > planeTolerance ||
                            Mathf.Abs(Vector3.Dot(keptNormal, b - keptPoint)) > planeTolerance ||
                            Mathf.Abs(Vector3.Dot(keptNormal, c - keptPoint)) > planeTolerance)
                            continue;
                        if (CoplanarTrianglesOverlap(a, b, c,
                                keptPoint, keptB[id], keptC[id], keptNormal))
                            return true;
                    }
                }
                return false;
            }

            // Exterior triangles are immovable blockers: a cap must never cover one.
            for (int s = 0; s < SubmeshCount; s++)
            {
                if (s == interior) continue;
                List<int> triangles = Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    Vector3 a = Positions[triangles[i]];
                    Vector3 b = Positions[triangles[i + 1]];
                    Vector3 c = Positions[triangles[i + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.sqrMagnitude < 1e-18f) continue;
                    Vector3 n = cross.normalized;
                    Register(a, b, c, n, Vector3.Dot(n, a));
                }
            }

            List<int> caps = Submeshes[interior];
            int capCount = caps.Count / 3;
            var order = new int[capCount];
            var areaKeys = new float[capCount];
            for (int t = 0; t < capCount; t++)
            {
                order[t] = t;
                Vector3 a = Positions[caps[t * 3]];
                Vector3 b = Positions[caps[t * 3 + 1]];
                Vector3 c = Positions[caps[t * 3 + 2]];
                areaKeys[t] = Vector3.Cross(b - a, c - a).sqrMagnitude;
            }
            System.Array.Sort(areaKeys, order);

            var keptCaps = new List<int>(caps.Count);
            for (int i = 0; i < capCount; i++)
            {
                int t = order[i];
                int ia = caps[t * 3], ib = caps[t * 3 + 1], ic = caps[t * 3 + 2];
                Vector3 a = Positions[ia];
                Vector3 b = Positions[ib];
                Vector3 c = Positions[ic];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-18f)
                {
                    keptCaps.Add(ia);
                    keptCaps.Add(ib);
                    keptCaps.Add(ic);
                    continue;
                }
                Vector3 n = cross.normalized;
                float d = Vector3.Dot(n, a);
                if (OverlapsKept(a, b, c, n, d)) continue;
                keptCaps.Add(ia);
                keptCaps.Add(ib);
                keptCaps.Add(ic);
                Register(a, b, c, n, d);
            }
            Submeshes[interior] = keptCaps;
        }

        /// <summary>Overlap = one centroid strictly inside the other triangle, or any
        /// proper edge crossing. Shared edges/vertices of adjacent triangles never count.</summary>
        static bool CoplanarTrianglesOverlap(Vector3 a1, Vector3 b1, Vector3 c1,
            Vector3 a2, Vector3 b2, Vector3 c2, Vector3 normal)
        {
            Vector3 reference = Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right;
            Vector3 u = Vector3.Cross(normal, reference).normalized;
            Vector3 v = Vector3.Cross(normal, u);

            Vector2 p0 = Flatten(a1, u, v), p1 = Flatten(b1, u, v), p2 = Flatten(c1, u, v);
            Vector2 q0 = Flatten(a2, u, v), q1 = Flatten(b2, u, v), q2 = Flatten(c2, u, v);

            if (CentroidStrictlyInside((p0 + p1 + p2) / 3f, q0, q1, q2) ||
                CentroidStrictlyInside((q0 + q1 + q2) / 3f, p0, p1, p2))
                return true;

            Vector2[] first = { p0, p1, p2 };
            Vector2[] second = { q0, q1, q2 };
            for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                if (EdgesCrossProperly(first[i], first[(i + 1) % 3],
                        second[j], second[(j + 1) % 3]))
                    return true;
            }
            return false;
        }

        static Vector2 Flatten(Vector3 point, Vector3 u, Vector3 v)
        {
            return new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v));
        }

        static bool CentroidStrictlyInside(Vector2 point,
            Vector2 a, Vector2 b, Vector2 c)
        {
            const float epsilon = 1e-12f;
            float d1 = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
            float d2 = (c.x - b.x) * (point.y - b.y) - (c.y - b.y) * (point.x - b.x);
            float d3 = (a.x - c.x) * (point.y - c.y) - (a.y - c.y) * (point.x - c.x);
            return (d1 > epsilon && d2 > epsilon && d3 > epsilon) ||
                   (d1 < -epsilon && d2 < -epsilon && d3 < -epsilon);
        }

        static bool EdgesCrossProperly(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            const float epsilon = 1e-12f;
            float abC = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            float abD = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);
            float cdA = (d.x - c.x) * (a.y - c.y) - (d.y - c.y) * (a.x - c.x);
            float cdB = (d.x - c.x) * (b.y - c.y) - (d.y - c.y) * (b.x - c.x);
            return ((abC > epsilon && abD < -epsilon) ||
                    (abC < -epsilon && abD > epsilon)) &&
                   ((cdA > epsilon && cdB < -epsilon) ||
                    (cdA < -epsilon && cdB > epsilon));
        }

        readonly struct PlaneCellKey : System.IEquatable<PlaneCellKey>
        {
            readonly int X;
            readonly int Y;
            readonly int Z;
            readonly int W;

            public PlaneCellKey(int x, int y, int z, int w)
            {
                X = x;
                Y = y;
                Z = z;
                W = w;
            }

            public bool Equals(PlaneCellKey other) =>
                X == other.X && Y == other.Y && Z == other.Z && W == other.W;

            public override bool Equals(object obj) =>
                obj is PlaneCellKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = hash * 397 ^ Y;
                    hash = hash * 397 ^ Z;
                    return hash * 397 ^ W;
                }
            }
        }

        sealed class PlanarContactFace
        {
            public int Submesh;
            public int Slot;
            public int A;
            public int B;
            public int C;
            public bool Forward;
        }

        sealed class PlanarContactPatch
        {
            public readonly List<(int submesh, int slot)> Triangles =
                new List<(int, int)>();
            public readonly Dictionary<long, int> EdgeUse =
                new Dictionary<long, int>();
            public HashSet<long> Boundary;
        }

        /// <summary>
        /// Removes an internal double-wall where two closed manifolds are stacked with the
        /// same footprint and the same cap triangulation (a segmented pillar, a box on a
        /// box). The two coincident caps face opposite ways; dropping both merges the
        /// interiors into one clean closed solid. Whole Building may additionally match
        /// complete coplanar patches by their welded boundary, so two identical rectangular
        /// contacts still merge when their internal diagonal runs the other way. Partial
        /// contacts are never touched. A same-winding duplicate is left to normal dedupe.
        /// Returns faces removed.
        /// </summary>
        public int RemoveInternalContactFaces(bool matchPlanarPatches = false)
        {
            if (Positions.Count < 3) return 0;
            Bounds bounds = ComputeBounds();
            float cell = Mathf.Max(bounds.size.magnitude * 2e-6f, 1e-7f);
            var welder = new PositionWelder(cell);
            var welded = new int[Positions.Count];
            for (int i = 0; i < Positions.Count; i++)
                welded[i] = welder.Weld(Positions[i]);

            var first = new Dictionary<TriangleKey, (int submesh, int slot, int a, int b, int c)>();
            var drop = new HashSet<(int, int)>();
            for (int s = 0; s < SubmeshCount; s++)
            {
                List<int> tris = Submeshes[s];
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    int wa = welded[tris[i]], wb = welded[tris[i + 1]], wc = welded[tris[i + 2]];
                    if (wa == wb || wb == wc || wc == wa) continue;
                    var key = new TriangleKey(wa, wb, wc);
                    if (!first.TryGetValue(key, out var prev)) { first[key] = (s, i, wa, wb, wc); continue; }
                    // Cyclic rotation of the first triple = same winding (a real duplicate).
                    bool sameWinding =
                        (wa == prev.a && wb == prev.b && wc == prev.c) ||
                        (wa == prev.b && wb == prev.c && wc == prev.a) ||
                        (wa == prev.c && wb == prev.a && wc == prev.b);
                    if (!sameWinding) { drop.Add((s, i)); drop.Add((prev.submesh, prev.slot)); }
                }
            }
            if (matchPlanarPatches)
                AddMatchingPlanarContactPatches(welded, cell, drop);
            if (drop.Count == 0) return 0;

            for (int s = 0; s < SubmeshCount; s++)
            {
                List<int> tris = Submeshes[s];
                var clean = new List<int>(tris.Count);
                for (int i = 0; i + 2 < tris.Count; i += 3)
                {
                    if (drop.Contains((s, i))) continue;
                    clean.Add(tris[i]); clean.Add(tris[i + 1]); clean.Add(tris[i + 2]);
                }
                Submeshes[s] = clean;
            }
            return drop.Count;
        }

        void AddMatchingPlanarContactPatches(int[] welded, float planeCell,
            HashSet<(int, int)> drop)
        {
            var planes = new Dictionary<PlaneCellKey, List<PlanarContactFace>>();
            const float normalCell = 10000f;
            const float signEpsilon = 1e-6f;
            for (int submesh = 0; submesh < SubmeshCount; submesh++)
            {
                List<int> triangles = Submeshes[submesh];
                for (int slot = 0; slot + 2 < triangles.Count; slot += 3)
                {
                    int ia = triangles[slot];
                    int ib = triangles[slot + 1];
                    int ic = triangles[slot + 2];
                    Vector3 a = Positions[ia];
                    Vector3 b = Positions[ib];
                    Vector3 c = Positions[ic];
                    Vector3 normal = Vector3.Cross(b - a, c - a);
                    float magnitude = normal.magnitude;
                    if (magnitude <= 1e-10f) continue;
                    normal /= magnitude;

                    bool flip = normal.x < -signEpsilon ||
                                (Mathf.Abs(normal.x) <= signEpsilon &&
                                 (normal.y < -signEpsilon ||
                                  (Mathf.Abs(normal.y) <= signEpsilon &&
                                   normal.z < 0f)));
                    Vector3 canonical = flip ? -normal : normal;
                    float distance = Vector3.Dot(canonical, a);
                    var key = new PlaneCellKey(
                        Mathf.RoundToInt(canonical.x * normalCell),
                        Mathf.RoundToInt(canonical.y * normalCell),
                        Mathf.RoundToInt(canonical.z * normalCell),
                        Mathf.RoundToInt(distance / Mathf.Max(planeCell, 1e-7f)));
                    if (!planes.TryGetValue(key, out List<PlanarContactFace> faces))
                    {
                        faces = new List<PlanarContactFace>();
                        planes.Add(key, faces);
                    }
                    faces.Add(new PlanarContactFace
                    {
                        Submesh = submesh,
                        Slot = slot,
                        A = welded[ia],
                        B = welded[ib],
                        C = welded[ic],
                        Forward = !flip
                    });
                }
            }

            foreach (List<PlanarContactFace> faces in planes.Values)
            {
                List<PlanarContactPatch> forward =
                    BuildPlanarContactPatches(faces, true);
                List<PlanarContactPatch> backward =
                    BuildPlanarContactPatches(faces, false);
                if (forward.Count == 0 || backward.Count == 0) continue;

                var matchedBackward = new HashSet<int>();
                for (int i = 0; i < forward.Count; i++)
                {
                    PlanarContactPatch a = forward[i];
                    if (a.Boundary == null || a.Boundary.Count < 3) continue;
                    for (int j = 0; j < backward.Count; j++)
                    {
                        if (matchedBackward.Contains(j)) continue;
                        PlanarContactPatch b = backward[j];
                        if (b.Boundary == null ||
                            !a.Boundary.SetEquals(b.Boundary))
                            continue;
                        for (int triangle = 0;
                             triangle < a.Triangles.Count; triangle++)
                            drop.Add(a.Triangles[triangle]);
                        for (int triangle = 0;
                             triangle < b.Triangles.Count; triangle++)
                            drop.Add(b.Triangles[triangle]);
                        matchedBackward.Add(j);
                        break;
                    }
                }
            }
        }

        static List<PlanarContactPatch> BuildPlanarContactPatches(
            List<PlanarContactFace> allFaces, bool forward)
        {
            var faces = new List<PlanarContactFace>();
            for (int i = 0; i < allFaces.Count; i++)
                if (allFaces[i].Forward == forward) faces.Add(allFaces[i]);
            if (faces.Count == 0) return new List<PlanarContactPatch>();

            var parent = new int[faces.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int item)
            {
                while (parent[item] != item)
                {
                    parent[item] = parent[parent[item]];
                    item = parent[item];
                }
                return item;
            }
            void Union(int a, int b)
            {
                int rootA = Find(a);
                int rootB = Find(b);
                if (rootA != rootB) parent[rootB] = rootA;
            }

            var firstOnEdge = new Dictionary<long, int>();
            for (int i = 0; i < faces.Count; i++)
            {
                Connect(faces[i].A, faces[i].B, i);
                Connect(faces[i].B, faces[i].C, i);
                Connect(faces[i].C, faces[i].A, i);
            }

            void Connect(int a, int b, int faceIndex)
            {
                long edge = ContactEdgeKey(a, b);
                if (firstOnEdge.TryGetValue(edge, out int other))
                    Union(faceIndex, other);
                else
                    firstOnEdge.Add(edge, faceIndex);
            }

            var byRoot = new Dictionary<int, PlanarContactPatch>();
            for (int i = 0; i < faces.Count; i++)
            {
                int root = Find(i);
                if (!byRoot.TryGetValue(root, out PlanarContactPatch patch))
                {
                    patch = new PlanarContactPatch();
                    byRoot.Add(root, patch);
                }
                PlanarContactFace face = faces[i];
                patch.Triangles.Add((face.Submesh, face.Slot));
                CountPatchEdge(patch.EdgeUse, face.A, face.B);
                CountPatchEdge(patch.EdgeUse, face.B, face.C);
                CountPatchEdge(patch.EdgeUse, face.C, face.A);
            }

            var result = new List<PlanarContactPatch>(byRoot.Count);
            foreach (PlanarContactPatch patch in byRoot.Values)
            {
                patch.Boundary = new HashSet<long>();
                foreach (KeyValuePair<long, int> edge in patch.EdgeUse)
                    if (edge.Value == 1) patch.Boundary.Add(edge.Key);
                result.Add(patch);
            }
            return result;
        }

        static void CountPatchEdge(Dictionary<long, int> edgeUse, int a, int b)
        {
            long edge = ContactEdgeKey(a, b);
            edgeUse.TryGetValue(edge, out int count);
            edgeUse[edge] = count + 1;
        }

        static long ContactEdgeKey(int a, int b)
        {
            uint lo = (uint)Mathf.Min(a, b);
            uint hi = (uint)Mathf.Max(a, b);
            return ((long)lo << 32) | hi;
        }

        readonly struct TriangleKey : System.IEquatable<TriangleKey>
        {
            readonly int A;
            readonly int B;
            readonly int C;

            public TriangleKey(int a, int b, int c)
            {
                if (a > b) (a, b) = (b, a);
                if (b > c) (b, c) = (c, b);
                if (a > b) (a, b) = (b, a);
                A = a;
                B = b;
                C = c;
            }

            public bool Equals(TriangleKey other) =>
                A == other.A && B == other.B && C == other.C;

            public override bool Equals(object obj) =>
                obj is TriangleKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = A;
                    hash = hash * 397 ^ B;
                    return hash * 397 ^ C;
                }
            }
        }

        struct BoundaryStitchEdge
        {
            public int WeldA;
            public int WeldB;
            public Vector3 A;
            public Vector3 B;
            public int Count;
        }

        struct BoundaryStitchCandidate
        {
            public int Left;
            public int Right;
            public bool Reversed;
            public float Score;
        }

        /// <summary>
        /// Snaps complete, nearly coincident boundary-edge pairs to one shared position.
        /// Whole Building uses this before Hollow Shell generation so small modular
        /// placement errors do not turn authored rows into separately capped strips.
        /// Point contacts and partial overlaps remain separate. Opposite source winding is
        /// accepted because modular Plane assets are often flipped independently; matching
        /// complete rim edges are geometric continuity regardless of their face normal.
        /// Returns the number of matched seam-edge pairs.
        /// </summary>
        public int StitchMatchingBoundaryEdges(float requestedTolerance)
        {
            if (Positions.Count < 4 || TriangleCount < 2) return 0;
            float tolerance = Mathf.Max(requestedTolerance, 1e-7f);
            Bounds bounds = ComputeBounds();
            float topologyCell = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            var topologyWelder = new PositionWelder(topologyCell);
            var welded = new int[Positions.Count];
            var sourceVertices = new List<int>[Positions.Count];
            for (int i = 0; i < Positions.Count; i++)
            {
                int id = topologyWelder.Weld(Positions[i]);
                welded[i] = id;
                if (sourceVertices[id] == null) sourceVertices[id] = new List<int>(2);
                sourceVertices[id].Add(i);
            }

            var edgeLookup = new Dictionary<long, BoundaryStitchEdge>();
            void Register(int indexA, int indexB)
            {
                int weldA = welded[indexA];
                int weldB = welded[indexB];
                if (weldA == weldB) return;
                int lo = Mathf.Min(weldA, weldB);
                int hi = Mathf.Max(weldA, weldB);
                long key = ((long)lo << 32) | (uint)hi;
                if (edgeLookup.TryGetValue(key, out BoundaryStitchEdge edge))
                {
                    edge.Count++;
                    edgeLookup[key] = edge;
                    return;
                }
                edgeLookup.Add(key, new BoundaryStitchEdge
                {
                    WeldA = weldA,
                    WeldB = weldB,
                    A = topologyWelder.Representative(weldA),
                    B = topologyWelder.Representative(weldB),
                    Count = 1
                });
            }

            int exteriorCount = Mathf.Max(0, InteriorSubmesh);
            for (int submesh = 0; submesh < exteriorCount; submesh++)
            {
                List<int> triangles = Submeshes[submesh];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    Vector3 normal = Vector3.Cross(
                        Positions[b] - Positions[a], Positions[c] - Positions[a]);
                    if (normal.sqrMagnitude <= 1e-16f) continue;
                    normal.Normalize();
                    Register(a, b);
                    Register(b, c);
                    Register(c, a);
                }
            }

            var boundary = new List<BoundaryStitchEdge>();
            foreach (KeyValuePair<long, BoundaryStitchEdge> pair in edgeLookup)
                if (pair.Value.Count == 1)
                    boundary.Add(pair.Value);
            if (boundary.Count < 2) return 0;

            Vector3Int CellOf(Vector3 point) => new Vector3Int(
                Mathf.RoundToInt(point.x / tolerance),
                Mathf.RoundToInt(point.y / tolerance),
                Mathf.RoundToInt(point.z / tolerance));
            var midpointBuckets = new Dictionary<Vector3Int, List<int>>();
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3Int key = CellOf((boundary[i].A + boundary[i].B) * 0.5f);
                if (!midpointBuckets.TryGetValue(key, out List<int> bucket))
                {
                    bucket = new List<int>(2);
                    midpointBuckets.Add(key, bucket);
                }
                bucket.Add(i);
            }

            var candidates = new List<BoundaryStitchCandidate>();
            for (int left = 0; left < boundary.Count; left++)
            {
                BoundaryStitchEdge a = boundary[left];
                Vector3 edgeA = a.B - a.A;
                float lengthA = edgeA.magnitude;
                if (lengthA <= 1e-7f) continue;
                Vector3Int center = CellOf((a.A + a.B) * 0.5f);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (!midpointBuckets.TryGetValue(key, out List<int> nearby)) continue;
                    for (int n = 0; n < nearby.Count; n++)
                    {
                        int right = nearby[n];
                        if (right <= left) continue;
                        BoundaryStitchEdge b = boundary[right];
                        Vector3 edgeB = b.B - b.A;
                        float lengthB = edgeB.magnitude;
                        if (lengthB <= 1e-7f ||
                            Mathf.Abs(Vector3.Dot(edgeA / lengthA, edgeB / lengthB)) < 0.999f)
                            continue;

                        // Never derive a large merge reach from the bounds of a huge
                        // building. A matching module seam may close at most two percent
                        // of its own edge length as well as the global/thickness allowance
                        // chosen by Whole Building. This absorbs centimetre-scale placement
                        // drift on stock Unity Plane rims while keeping windows, trim slots
                        // and parallel walls distinct.
                        float edgeTolerance = Mathf.Min(
                            tolerance, Mathf.Min(lengthA, lengthB) * 0.02f);
                        float edgeToleranceSqr = edgeTolerance * edgeTolerance;
                        if (edgeTolerance <= 1e-7f ||
                            Mathf.Abs(lengthA - lengthB) > edgeTolerance * 2f)
                            continue;

                        float directA = (a.A - b.A).sqrMagnitude;
                        float directB = (a.B - b.B).sqrMagnitude;
                        float reverseA = (a.A - b.B).sqrMagnitude;
                        float reverseB = (a.B - b.A).sqrMagnitude;
                        bool reversed = reverseA + reverseB < directA + directB;
                        float first = reversed ? reverseA : directA;
                        float second = reversed ? reverseB : directB;
                        if (first > edgeToleranceSqr || second > edgeToleranceSqr)
                            continue;
                        candidates.Add(new BoundaryStitchCandidate
                        {
                            Left = left,
                            Right = right,
                            Reversed = reversed,
                            Score = first + second
                        });
                    }
                }
            }
            if (candidates.Count == 0) return 0;
            candidates.Sort((left, right) => left.Score.CompareTo(right.Score));

            int weldCount = topologyWelder.Count;
            var parent = new int[weldCount];
            var rank = new byte[weldCount];
            for (int i = 0; i < weldCount; i++) parent[i] = i;
            int Find(int value)
            {
                while (parent[value] != value)
                {
                    parent[value] = parent[parent[value]];
                    value = parent[value];
                }
                return value;
            }
            void Union(int left, int right)
            {
                left = Find(left);
                right = Find(right);
                if (left == right) return;
                if (rank[left] < rank[right]) parent[left] = right;
                else
                {
                    parent[right] = left;
                    if (rank[left] == rank[right]) rank[left]++;
                }
            }

            var paired = new bool[boundary.Count];
            int stitched = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                BoundaryStitchCandidate candidate = candidates[i];
                if (paired[candidate.Left] || paired[candidate.Right]) continue;
                BoundaryStitchEdge left = boundary[candidate.Left];
                BoundaryStitchEdge right = boundary[candidate.Right];
                Union(left.WeldA, candidate.Reversed ? right.WeldB : right.WeldA);
                Union(left.WeldB, candidate.Reversed ? right.WeldA : right.WeldB);
                paired[candidate.Left] = true;
                paired[candidate.Right] = true;
                stitched++;
            }
            if (stitched == 0) return 0;

            var sums = new Vector3[weldCount];
            var counts = new int[weldCount];
            for (int i = 0; i < weldCount; i++)
            {
                int root = Find(i);
                sums[root] += topologyWelder.Representative(i);
                counts[root]++;
            }
            for (int weld = 0; weld < weldCount; weld++)
            {
                int root = Find(weld);
                if (counts[root] <= 1 || sourceVertices[weld] == null) continue;
                Vector3 snapped = sums[root] / counts[root];
                List<int> vertices = sourceVertices[weld];
                for (int i = 0; i < vertices.Count; i++) Positions[vertices[i]] = snapped;
            }
            return stitched;
        }

        /// <summary>
        /// Builds an inward-offset shell before fracture. Closed inputs receive a reversed
        /// inner surface; open inputs additionally receive rim quads along boundary edges.
        /// The generated rim uses the interior submesh so it follows the cut-face material.
        /// </summary>
        public WorkMesh Solidified(float requestedThickness)
        {
            // Preserve the historical direct-call behaviour. The fracture pipeline uses
            // the overload below and supplies the profile's explicit cut-surface mode.
            return Solidified(requestedThickness, CutSurfaceUVMode.EdgeStretch, 1f);
        }

        /// <summary>
        /// Builds an inward shell and maps its generated boundary rim with the same UV
        /// contract as newly exposed cut faces. FitToCut keeps one uniform texel scale on
        /// the long edge and through wall thickness; EdgeStretch deliberately retains the
        /// legacy one-dimensional extrusion.
        /// </summary>
        public WorkMesh Solidified(float requestedThickness,
            CutSurfaceUVMode rimUVMode, float rimUVScale)
        {
            Bounds bounds = ComputeBounds();
            float maxUsefulThickness = Mathf.Max(0.001f, bounds.size.magnitude * 0.25f);
            float thickness = Mathf.Clamp(requestedThickness, 0.001f, maxUsefulThickness);
            var result = new WorkMesh(SubmeshCount)
            {
                StructuralGroup = StructuralGroup
            };

            result.Positions.AddRange(Positions);
            result.Normals.AddRange(Normals);
            result.UVs.AddRange(UVs);
            for (int s = 0; s < SubmeshCount; s++)
                result.Submeshes[s].AddRange(Submeshes[s]);

            // Imported UV seams and hard normals duplicate vertex indices. Weld by
            // position for topology and average their normals for one watertight inward
            // offset; exterior UVs and normals themselves remain untouched.
            var weldLookup = new Dictionary<Vector3Int, int>(Positions.Count);
            var welded = new int[Positions.Count];
            float weldCell = Mathf.Max(bounds.size.magnitude, 0.001f) * 1e-6f;
            int nextWeldId = 0;
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 p = Positions[i];
                var key = new Vector3Int(
                    Mathf.RoundToInt(p.x / weldCell),
                    Mathf.RoundToInt(p.y / weldCell),
                    Mathf.RoundToInt(p.z / weldCell));
                if (!weldLookup.TryGetValue(key, out int weldId))
                {
                    weldId = nextWeldId++;
                    weldLookup.Add(key, weldId);
                }
                welded[i] = weldId;
            }

            var weldNormalSums = new Vector3[nextWeldId];
            var weldNormals = new List<Vector3>[nextWeldId];
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 normal = i < Normals.Count && Normals[i].sqrMagnitude > 1e-12f
                    ? Normals[i].normalized
                    : Vector3.up;
                weldNormalSums[welded[i]] += normal;
                List<Vector3> distinct = weldNormals[welded[i]];
                if (distinct == null)
                {
                    distinct = new List<Vector3>(3);
                    weldNormals[welded[i]] = distinct;
                }
                bool duplicate = false;
                for (int n = 0; n < distinct.Count; n++)
                {
                    if (Vector3.Dot(distinct[n], normal) < 0.9999f) continue;
                    duplicate = true;
                    break;
                }
                if (!duplicate) distinct.Add(normal);
            }

            int innerOffset = Positions.Count;
            for (int i = 0; i < Positions.Count; i++)
            {
                Vector3 sourceNormal = i < Normals.Count &&
                                       Normals[i].sqrMagnitude > 1e-12f
                    ? Normals[i].normalized
                    : Vector3.up;
                Vector3 fallback = weldNormalSums[welded[i]].normalized;
                if (fallback.sqrMagnitude < 1e-12f) fallback = sourceNormal;
                Vector3 offset = ResolveSolidifyOffset(
                    weldNormals[welded[i]], thickness, fallback);
                result.Positions.Add(Positions[i] - offset);
                // The miter offset above belongs to geometry only. Reusing its averaged
                // direction as the shading normal bends a perfectly flat module towards
                // every perpendicular neighbour; after triangulation those gradients show
                // up as diagonal dark streaks. Preserve the authored face normal instead.
                result.Normals.Add(-sourceNormal);
                result.UVs.Add(i < UVs.Count ? UVs[i] : Vector2.zero);
            }

            var edges = new Dictionary<long, SolidifyEdge>();

            int exteriorCount = Mathf.Max(0, InteriorSubmesh);
            for (int s = 0; s < exteriorCount; s++)
            {
                List<int> triangles = Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    // Reversed winding makes the offset surface face the cavity.
                    result.Submeshes[s].Add(c + innerOffset);
                    result.Submeshes[s].Add(b + innerOffset);
                    result.Submeshes[s].Add(a + innerOffset);
                    RegisterSolidifyEdge(edges, a, b, welded[a], welded[b], s);
                    RegisterSolidifyEdge(edges, b, c, welded[b], welded[c], s);
                    RegisterSolidifyEdge(edges, c, a, welded[c], welded[a], s);
                }
            }

            foreach (KeyValuePair<long, SolidifyEdge> pair in edges)
            {
                SolidifyEdge edge = pair.Value;
                if (edge.Count != 1) continue;

                Vector3 outerA = Positions[edge.A];
                Vector3 outerB = Positions[edge.B];
                Vector3 innerA = result.Positions[edge.A + innerOffset];
                Vector3 innerB = result.Positions[edge.B + innerOffset];
                Vector3 edgeDirection = outerB - outerA;
                Vector3 inward = ((innerA - outerA) + (innerB - outerB)) * 0.5f;
                Vector3 sideNormal = Vector3.Cross(inward, edgeDirection).normalized;
                if (sideNormal.sqrMagnitude < 1e-12f) sideNormal = Vector3.up;

                Vector2 sourceUvA = edge.A >= 0 && edge.A < UVs.Count
                    ? UVs[edge.A] : Vector2.zero;
                Vector2 sourceUvB = edge.B >= 0 && edge.B < UVs.Count
                    ? UVs[edge.B] : Vector2.zero;
                ResolveSolidifyRimUV(edge.Submesh, outerA, outerB, innerA, innerB,
                    sourceUvA, sourceUvB, rimUVMode, rimUVScale,
                    out Vector2 outerUvA, out Vector2 outerUvB,
                    out Vector2 innerUvA, out Vector2 innerUvB);
                List<int> rim = result.Submeshes[Mathf.Clamp(
                    edge.Submesh, 0, result.InteriorSubmesh - 1)];
                // A boundary quad next to a hard miter need not be perfectly planar.
                // Giving both triangles one averaged normal makes its internal diagonal
                // visible. Emit hard triangle vertices with the exact geometric normal;
                // this affects only the narrow outer rim, not vertex count on broad faces.
                AddSolidifyRimTriangle(result, rim,
                    outerA, innerA, innerB,
                    outerUvA, innerUvA, innerUvB, sideNormal);
                AddSolidifyRimTriangle(result, rim,
                    outerA, innerB, outerB,
                    outerUvA, innerUvB, outerUvB, sideNormal);
            }

            return result;
        }

        void ResolveSolidifyRimUV(int submesh, Vector3 outerA, Vector3 outerB,
            Vector3 innerA, Vector3 innerB, Vector2 sourceUvA, Vector2 sourceUvB,
            CutSurfaceUVMode mode, float uvScale,
            out Vector2 outerUvA, out Vector2 outerUvB,
            out Vector2 innerUvA, out Vector2 innerUvB)
        {
            if (mode == CutSurfaceUVMode.EdgeStretch)
            {
                // Intentional legacy extrusion: the boundary UV is inherited exactly and
                // the texture is stretched through the generated thickness.
                outerUvA = innerUvA = sourceUvA;
                outerUvB = innerUvB = sourceUvB;
                return;
            }

            float edgeLength = Mathf.Max(Vector3.Distance(outerA, outerB), 1e-5f);
            float rimWidth = Mathf.Max(
                (Vector3.Distance(outerA, innerA) +
                 Vector3.Distance(outerB, innerB)) * 0.5f, 1e-5f);

            if (mode == CutSurfaceUVMode.PlanarProjection)
            {
                float scale = Mathf.Max(0.01f, uvScale);
                outerUvA = Vector2.zero;
                outerUvB = new Vector2(edgeLength * scale, 0f);
                innerUvA = new Vector2(0f, rimWidth * scale);
                innerUvB = new Vector2(edgeLength * scale, rimWidth * scale);
                return;
            }

            // Fit one physical rectangle into the source submesh's UV island using a
            // single uniform scale. The former mapping assigned identical UVs to outer
            // and inner vertices, giving the rim zero UV width and visibly stretching a
            // one-dimensional texture line across the complete side of a thick wall.
            if (!TryGetSubmeshUvBounds(submesh, out Vector2 uvMin, out Vector2 uvMax))
            {
                uvMin = Vector2.zero;
                uvMax = Vector2.one;
            }

            Vector2 sourceDelta = sourceUvB - sourceUvA;
            float sourceUvLength = sourceDelta.magnitude;
            if (sourceUvLength > 1e-5f)
            {
                // Preserve the authored UV distance of this exact boundary segment.
                // Unity's built-in Plane has ten short edges on each side. Fitting every
                // one independently to the complete island repeated the material ten
                // times along the generated thickness rim and looked like horizontal
                // stretching. Extending the real edge UV perpendicular to itself keeps
                // adjacent segments continuous and gives the thickness the same texel
                // density as the source edge.
                Vector2 alongSource = sourceDelta / sourceUvLength;
                Vector2 acrossSource = new Vector2(-alongSource.y, alongSource.x);
                Vector2 uvCenter = (uvMin + uvMax) * 0.5f;
                Vector2 edgeCenter = (sourceUvA + sourceUvB) * 0.5f;
                if (Vector2.Dot(uvCenter - edgeCenter, acrossSource) < 0f)
                    acrossSource = -acrossSource;

                float sourceDensity = sourceUvLength / edgeLength;
                float targetWidth = rimWidth * sourceDensity;
                float availableWidth = Mathf.Min(
                    DistanceToUvBounds(sourceUvA, acrossSource, uvMin, uvMax),
                    DistanceToUvBounds(sourceUvB, acrossSource, uvMin, uvMax));
                if (targetWidth > 1e-6f &&
                    availableWidth + 1e-5f >= targetWidth)
                {
                    outerUvA = sourceUvA;
                    outerUvB = sourceUvB;
                    innerUvA = sourceUvA + acrossSource * targetWidth;
                    innerUvB = sourceUvB + acrossSource * targetWidth;
                    return;
                }
            }

            Vector2 along = sourceDelta;
            if (along.sqrMagnitude <= 1e-10f)
                along = (uvMax.x - uvMin.x) >= (uvMax.y - uvMin.y)
                    ? Vector2.right : Vector2.up;
            else
                along.Normalize();
            Vector2 across = new Vector2(-along.y, along.x);

            float availableU = Mathf.Max(uvMax.x - uvMin.x, 1e-5f);
            float availableV = Mathf.Max(uvMax.y - uvMin.y, 1e-5f);
            float requiredU = edgeLength * Mathf.Abs(along.x) +
                              rimWidth * Mathf.Abs(across.x);
            float requiredV = edgeLength * Mathf.Abs(along.y) +
                              rimWidth * Mathf.Abs(across.y);
            float fitScale = Mathf.Min(
                availableU / Mathf.Max(requiredU, 1e-5f),
                availableV / Mathf.Max(requiredV, 1e-5f));
            Vector2 center = (uvMin + uvMax) * 0.5f;
            Vector2 halfEdge = along * (edgeLength * fitScale * 0.5f);
            Vector2 halfWidth = across * (rimWidth * fitScale * 0.5f);
            outerUvA = center - halfEdge - halfWidth;
            outerUvB = center + halfEdge - halfWidth;
            innerUvA = center - halfEdge + halfWidth;
            innerUvB = center + halfEdge + halfWidth;
        }

        static float DistanceToUvBounds(Vector2 origin, Vector2 direction,
            Vector2 minimum, Vector2 maximum)
        {
            float distance = float.PositiveInfinity;
            if (direction.x > 1e-6f)
                distance = Mathf.Min(distance, (maximum.x - origin.x) / direction.x);
            else if (direction.x < -1e-6f)
                distance = Mathf.Min(distance, (minimum.x - origin.x) / direction.x);
            if (direction.y > 1e-6f)
                distance = Mathf.Min(distance, (maximum.y - origin.y) / direction.y);
            else if (direction.y < -1e-6f)
                distance = Mathf.Min(distance, (minimum.y - origin.y) / direction.y);
            return float.IsInfinity(distance) ? 0f : Mathf.Max(0f, distance);
        }

        bool TryGetSubmeshUvBounds(int submesh, out Vector2 minimum,
            out Vector2 maximum)
        {
            minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            if (submesh < 0 || submesh >= InteriorSubmesh) return false;

            List<int> triangles = Submeshes[submesh];
            bool found = false;
            for (int i = 0; i < triangles.Count; i++)
            {
                int index = triangles[i];
                if (index < 0 || index >= UVs.Count) continue;
                minimum = Vector2.Min(minimum, UVs[index]);
                maximum = Vector2.Max(maximum, UVs[index]);
                found = true;
            }
            return found && maximum.x - minimum.x > 1e-5f &&
                   maximum.y - minimum.y > 1e-5f;
        }

        /// <summary>
        /// Finds a bounded miter displacement whose projection along every hard face normal
        /// equals the requested wall thickness. A normalized average moves a 90-degree
        /// corner only 0.707x along each face, warping both otherwise planar inner walls.
        /// Iterated orthogonal projections solve the small (normally 1-3 normal) system
        /// without allocating matrices and keep duplicate seam vertices coincident.
        /// </summary>
        static Vector3 ResolveSolidifyOffset(IReadOnlyList<Vector3> normals,
            float thickness, Vector3 fallback)
        {
            if (normals == null || normals.Count == 0)
                return fallback * thickness;
            if (normals.Count == 1)
                return normals[0] * thickness;

            Vector3 offset = Vector3.zero;
            for (int iteration = 0; iteration < 12; iteration++)
            {
                for (int i = 0; i < normals.Count; i++)
                {
                    Vector3 normal = normals[i];
                    offset += normal * (thickness - Vector3.Dot(offset, normal));
                }
            }

            // Nearly opposing or inconsistent normals do not define a sensible miter.
            // Acute architectural corners can legitimately exceed one thickness, but an
            // unbounded spike would be worse than the former conservative average.
            float maxLength = thickness * 4f;
            if (float.IsNaN(offset.x) || float.IsNaN(offset.y) || float.IsNaN(offset.z) ||
                float.IsInfinity(offset.x) || float.IsInfinity(offset.y) ||
                float.IsInfinity(offset.z) || offset.sqrMagnitude > maxLength * maxLength)
                return fallback * thickness;
            return offset;
        }

        static void AddSolidifyRimTriangle(WorkMesh mesh, List<int> triangles,
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uvA, Vector2 uvB, Vector2 uvC, Vector3 fallbackNormal)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.sqrMagnitude < 1e-12f) normal = fallbackNormal;
            triangles.Add(mesh.AddVertex(a, normal, uvA));
            triangles.Add(mesh.AddVertex(b, normal, uvB));
            triangles.Add(mesh.AddVertex(c, normal, uvC));
        }

        struct SolidifyEdge
        {
            public int A;
            public int B;
            public int Count;
            public int Submesh;
        }

        static void RegisterSolidifyEdge(
            Dictionary<long, SolidifyEdge> edges, int a, int b,
            int weldedA, int weldedB, int submesh)
        {
            int lo = Mathf.Min(weldedA, weldedB);
            int hi = Mathf.Max(weldedA, weldedB);
            long key = ((long)lo << 32) | (uint)hi;
            if (edges.TryGetValue(key, out SolidifyEdge edge))
            {
                edge.Count++;
                edges[key] = edge;
            }
            else
            {
                edges.Add(key, new SolidifyEdge
                    { A = a, B = b, Count = 1, Submesh = submesh });
            }
        }

        /// <summary>
        /// Detects a substantial inward-facing surface, which strongly suggests that an
        /// artist already modeled the object's cavity. Solidifying such a mesh would add a
        /// second inner wall and cause coincident faces/z-fighting.
        /// </summary>
        public bool HasLikelyAuthoredInnerShell(out float inwardAreaRatio)
        {
            inwardAreaRatio = 0f;
            if (Positions.Count < 4) return false;
            Bounds bounds = ComputeBounds();

            // A surface with no thickness has no inside, so it cannot carry an authored
            // inner shell — and the test below is undefined on one. The bounds centre lies
            // in the surface itself, so every face normal is perpendicular to
            // (faceCenter - center) and each dot product is decided by floating-point noise
            // alone. The ratio then lands near half, right inside the hollow band, and a
            // plain wall or floor panel is reported as an authored hollow shell. That made
            // Solidify skip the one kind of mesh that cannot be fractured without it,
            // leaving a zero-volume source that produced no fragments at all.
            Vector3 size = bounds.size;
            float largestExtent = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float thinnestExtent = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (largestExtent > 0f &&
                thinnestExtent <= Mathf.Max(1e-5f, largestExtent * 0.002f))
                return false;

            Vector3 center = bounds.center;
            double inwardArea = 0d;
            double totalArea = 0d;
            int exteriorCount = Mathf.Max(0, InteriorSubmesh);
            for (int s = 0; s < exteriorCount; s++)
            {
                List<int> triangles = Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    Vector3 a = Positions[triangles[i]];
                    Vector3 b = Positions[triangles[i + 1]];
                    Vector3 c = Positions[triangles[i + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    double area = cross.magnitude * 0.5d;
                    if (area <= 1e-14d) continue;
                    totalArea += area;
                    Vector3 faceCenter = (a + b + c) / 3f;
                    if (Vector3.Dot(cross, faceCenter - center) < 0f)
                        inwardArea += area;
                }
            }
            if (totalArea <= 1e-12d) return false;
            inwardAreaRatio = (float)(inwardArea / totalArea);
            // A completely reversed mesh approaches 1.0 and is a different import issue.
            // Authored hollow shells usually contribute comparable outer/inner areas.
            return inwardAreaRatio >= 0.18f && inwardAreaRatio <= 0.62f;
        }

        public WorkMesh Clone()
        {
            var clone = new WorkMesh(SubmeshCount)
            {
                StructuralGroup = StructuralGroup
            };
            clone.Positions.AddRange(Positions);
            clone.Normals.AddRange(Normals);
            clone.UVs.AddRange(UVs);
            for (int i = 0; i < SubmeshCount; i++)
                clone.Submeshes[i].AddRange(Submeshes[i]);
            return clone;
        }

        /// <summary>
        /// Splits disconnected triangle islands into independent WorkMeshes. Fracture
        /// planes partition space, so without this pass two separate posts that happen to
        /// remain on the same side of every plane become one Rigidbody and one convex hull.
        /// Vertices are welded by position so cap/exterior seams remain connected.
        /// </summary>
        public List<WorkMesh> SplitConnectedComponents()
        {
            var triangleSubmesh = new List<int>();
            var triangleA = new List<int>();
            var triangleB = new List<int>();
            var triangleC = new List<int>();
            for (int s = 0; s < SubmeshCount; s++)
            {
                List<int> triangles = Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    triangleSubmesh.Add(s);
                    triangleA.Add(triangles[i]);
                    triangleB.Add(triangles[i + 1]);
                    triangleC.Add(triangles[i + 2]);
                }
            }
            int triangleCount = triangleA.Count;
            if (triangleCount == 0) return new List<WorkMesh>();

            Bounds bounds = ComputeBounds();
            float weldCell = Mathf.Max(
                bounds.size.magnitude * TopologyWeldRelativeTolerance, 1e-7f);
            // Use the same neighbourhood weld as cap repair and modular seam stitching.
            // A single quantized-cell lookup splits two genuinely shared vertices when
            // tiny transform drift places them on opposite sides of a rounding boundary.
            // That produced a ruler-straight horizontal break on only one of two otherwise
            // identical Whole Building walls, depending solely on their world position.
            var positionWelder = new PositionWelder(weldCell);
            var welded = new int[Positions.Count];
            for (int i = 0; i < Positions.Count; i++)
                welded[i] = positionWelder.Weld(Positions[i]);

            var parent = new int[triangleCount];
            var rank = new byte[triangleCount];
            for (int i = 0; i < triangleCount; i++) parent[i] = i;
            int Find(int value)
            {
                while (parent[value] != value)
                {
                    parent[value] = parent[parent[value]];
                    value = parent[value];
                }
                return value;
            }
            void Union(int left, int right)
            {
                left = Find(left);
                right = Find(right);
                if (left == right) return;
                if (rank[left] < rank[right]) parent[left] = right;
                else
                {
                    parent[right] = left;
                    if (rank[left] == rank[right]) rank[left]++;
                }
            }

            // Surface connectivity requires a shared edge, not merely one coincident
            // point. This prevents a bridge deck, post and brace touching at a corner
            // from becoming one Auto Axis element, while welded UV/hard-normal seams
            // still remain part of the same plank.
            var trianglesByEdge = new Dictionary<long, List<int>>();
            void RegisterTriangleEdge(int triangle, int vertexA, int vertexB)
            {
                int a = welded[vertexA];
                int b = welded[vertexB];
                if (a == b) return;
                int lo = Mathf.Min(a, b);
                int hi = Mathf.Max(a, b);
                long edge = ((long)lo << 32) | (uint)hi;
                if (!trianglesByEdge.TryGetValue(edge, out List<int> incidents))
                {
                    incidents = new List<int>(2);
                    trianglesByEdge.Add(edge, incidents);
                }
                incidents.Add(triangle);
            }
            for (int t = 0; t < triangleCount; t++)
            {
                RegisterTriangleEdge(t, triangleA[t], triangleB[t]);
                RegisterTriangleEdge(t, triangleB[t], triangleC[t]);
                RegisterTriangleEdge(t, triangleC[t], triangleA[t]);
            }

            // Direction each face travels along a given welded edge, plus the position of
            // its opposite corner. Two faces of one consistently wound shell always run
            // along their shared edge in OPPOSITE directions; two coincident faces
            // belonging to different shells run the SAME way, because their normals are
            // mirrored. That is what tells them apart when geometry alone cannot.
            bool EdgeUseOf(int triangle, int weldLo, int weldHi, out int oppositeWeld)
            {
                int wa = welded[triangleA[triangle]];
                int wb = welded[triangleB[triangle]];
                int wc = welded[triangleC[triangle]];
                if (wa == weldLo && wb == weldHi) { oppositeWeld = wc; return true; }
                if (wa == weldHi && wb == weldLo) { oppositeWeld = wc; return false; }
                if (wb == weldLo && wc == weldHi) { oppositeWeld = wa; return true; }
                if (wb == weldHi && wc == weldLo) { oppositeWeld = wa; return false; }
                if (wc == weldLo && wa == weldHi) { oppositeWeld = wb; return true; }
                oppositeWeld = wb;
                return false;
            }

            var fanAngle = new List<float>(4);
            var fanForward = new List<bool>(4);
            var fanTriangle = new List<int>(4);
            var fanPaired = new List<bool>(4);
            var fanNormal = new List<Vector3>(4);
            var fanSpoke = new List<Vector3>(4);

            foreach (KeyValuePair<long, List<int>> pair in trianglesByEdge)
            {
                List<int> incidents = pair.Value;
                // Exactly two faces means a normal manifold surface edge (including a
                // welded UV or hard-normal seam).
                if (incidents.Count == 2)
                {
                    Union(incidents[0], incidents[1]);
                    continue;
                }
                if (incidents.Count <= 2) continue;

                // Three or more faces means several authored shells meet along the same
                // geometric edge — a segmented column where the lower mesh keeps its top
                // cap and the upper mesh keeps its bottom one, both at identical
                // coordinates. Matching ORIGINAL vertex indices cannot resolve this: a
                // hard-normal or UV seam, which is exactly how a cylinder's cap is
                // authored, already gives that cap its own duplicate indices, so its own
                // wall looks no more related to it than the neighbouring shell does. That
                // is what tore caps off as flying zero-thickness sheets.
                //
                // Resolve it the way mesh repair does: fan the incident faces out around the
                // edge and pair each with the ANGULARLY NEAREST face that runs the other
                // way and encloses material with it. Nearest must be measured both ways
                // round — the side walls of two stacked boxes are coplanar and continue
                // each other perfectly, so a fan walked in one direction only reaches the
                // neighbouring shell's wall (180 degrees on) before a shell's own cap
                // (90 degrees back), and fuses the two solids through their flat side.
                int weldLo = (int)(pair.Key >> 32);
                int weldHi = (int)(pair.Key & 0xFFFFFFFFL);
                if (weldLo >= positionWelder.Count || weldHi >= positionWelder.Count)
                    continue;
                Vector3 edgeStart = positionWelder.Representative(weldLo);
                Vector3 axis = positionWelder.Representative(weldHi) - edgeStart;
                if (axis.sqrMagnitude <= 1e-16f) continue;
                axis = axis.normalized;

                fanAngle.Clear();
                fanForward.Clear();
                fanTriangle.Clear();
                fanPaired.Clear();
                fanNormal.Clear();
                fanSpoke.Clear();
                Vector3 reference = Vector3.zero;
                Vector3 bitangent = Vector3.zero;
                for (int i = 0; i < incidents.Count; i++)
                {
                    int triangle = incidents[i];
                    bool forward = EdgeUseOf(triangle, weldLo, weldHi, out int oppositeWeld);
                    if (oppositeWeld < 0 || oppositeWeld >= positionWelder.Count) continue;
                    Vector3 faceA = Positions[triangleA[triangle]];
                    Vector3 faceNormal = Vector3.Cross(
                        Positions[triangleB[triangle]] - faceA,
                        Positions[triangleC[triangle]] - faceA);
                    if (faceNormal.sqrMagnitude <= 1e-20f) continue;
                    Vector3 spoke = positionWelder.Representative(oppositeWeld) - edgeStart;
                    spoke -= axis * Vector3.Dot(spoke, axis);
                    if (spoke.sqrMagnitude <= 1e-16f) continue;
                    spoke = spoke.normalized;
                    if (fanTriangle.Count == 0)
                    {
                        reference = spoke;
                        bitangent = Vector3.Cross(axis, reference);
                        fanAngle.Add(0f);
                    }
                    else
                    {
                        float angle = Mathf.Atan2(
                            Vector3.Dot(spoke, bitangent), Vector3.Dot(spoke, reference));
                        if (angle < 0f) angle += 2f * Mathf.PI;
                        fanAngle.Add(angle);
                    }
                    fanForward.Add(forward);
                    fanTriangle.Add(triangle);
                    fanPaired.Add(false);
                    fanNormal.Add(faceNormal.normalized);
                    fanSpoke.Add(spoke);
                }
                if (fanTriangle.Count < 2) continue;

                var order = new int[fanTriangle.Count];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                System.Array.Sort(order, (l, r) => fanAngle[l].CompareTo(fanAngle[r]));

                const float coincidentAngle = 1e-4f;
                for (int i = 0; i < order.Length; i++)
                {
                    int self = order[i];
                    if (fanPaired[self]) continue;
                    int best = -1;
                    float bestGap = float.MaxValue;
                    for (int j = 0; j < order.Length; j++)
                    {
                        int other = order[j];
                        if (other == self || fanPaired[other]) continue;
                        if (fanForward[other] == fanForward[self]) continue;
                        float gap = Mathf.Abs(fanAngle[other] - fanAngle[self]);
                        if (gap > Mathf.PI) gap = 2f * Mathf.PI - gap;
                        // A zero dihedral means the faces are coincident: two shells resting
                        // against each other, never a fold inside one surface.
                        if (gap < coincidentAngle) continue;
                        if (gap >= bestGap) continue;
                        // Stacked caps share their diagonal, where all four faces are
                        // coplanar. Two triangles of ONE cap point the same way; the
                        // opposing cap points exactly against it, so anti-parallel means
                        // the two sides of a zero-thickness sheet — a shell boundary.
                        if (Vector3.Dot(fanNormal[self], fanNormal[other]) < -0.9999f) continue;
                        // The wedge between a genuine pair is filled with material, so both
                        // faces must turn their back on it. This is what separates a shell's
                        // own cap from the coincident cap of the shell resting on it: the two
                        // sit at the same angle, but only one closes material with this wall.
                        Vector3 wedge = fanSpoke[self] + fanSpoke[other];
                        if (wedge.sqrMagnitude > 1e-12f)
                        {
                            wedge = wedge.normalized;
                            if (Vector3.Dot(wedge, fanNormal[self]) >= 0f ||
                                Vector3.Dot(wedge, fanNormal[other]) >= 0f) continue;
                        }
                        bestGap = gap;
                        best = other;
                    }
                    if (best < 0) continue;
                    Union(fanTriangle[self], fanTriangle[best]);
                    fanPaired[self] = true;
                    fanPaired[best] = true;
                }
            }

            var componentTriangles = new Dictionary<int, List<int>>();
            var componentOrder = new List<int>();
            for (int t = 0; t < triangleCount; t++)
            {
                int root = Find(t);
                if (!componentTriangles.TryGetValue(root, out List<int> list))
                {
                    list = new List<int>();
                    componentTriangles.Add(root, list);
                    componentOrder.Add(root);
                }
                list.Add(t);
            }
            if (componentTriangles.Count == 1)
                return new List<WorkMesh> { this };

            var result = new List<WorkMesh>(componentTriangles.Count);
            for (int componentIndex = 0;
                 componentIndex < componentOrder.Count; componentIndex++)
            {
                List<int> members = componentTriangles[componentOrder[componentIndex]];
                var component = new WorkMesh(SubmeshCount)
                {
                    StructuralGroup = StructuralGroup
                };
                var vertexMap = new Dictionary<int, int>();
                int Copy(int sourceIndex)
                {
                    if (vertexMap.TryGetValue(sourceIndex, out int existing))
                        return existing;
                    int created = component.AddVertex(
                        Positions[sourceIndex], Normals[sourceIndex], UVs[sourceIndex]);
                    vertexMap.Add(sourceIndex, created);
                    return created;
                }
                for (int i = 0; i < members.Count; i++)
                {
                    int triangle = members[i];
                    List<int> destination = component.Submeshes[triangleSubmesh[triangle]];
                    destination.Add(Copy(triangleA[triangle]));
                    destination.Add(Copy(triangleB[triangle]));
                    destination.Add(Copy(triangleC[triangle]));
                }
                result.Add(component);
            }
            return result;
        }

        public int TriangleCount
        {
            get
            {
                int n = 0;
                for (int s = 0; s < SubmeshCount; s++) n += Submeshes[s].Count / 3;
                return n;
            }
        }

        public bool IsClosedManifold()
        {
            if (Positions.Count < 4 || TriangleCount < 4) return false;
            Bounds bounds = ComputeBounds();
            float cell = Mathf.Max(
                bounds.size.magnitude * TopologyWeldRelativeTolerance, 1e-7f);
            var welder = new PositionWelder(cell);
            var welded = new int[Positions.Count];
            for (int i = 0; i < Positions.Count; i++)
                welded[i] = welder.Weld(Positions[i]);

            var edgeUse = new Dictionary<long, int>();
            void CountEdge(int a, int b)
            {
                if (a == b) return;
                int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                long key = ((long)lo << 32) | (uint)hi;
                edgeUse.TryGetValue(key, out int count);
                edgeUse[key] = count + 1;
            }
            for (int s = 0; s < SubmeshCount; s++)
            {
                List<int> triangles = Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int wa = welded[triangles[i]];
                    int wb = welded[triangles[i + 1]];
                    int wc = welded[triangles[i + 2]];
                    // A triangle degenerate after the position weld owns no real edges:
                    // its two collapsed sides coincide and inflate the use count of the
                    // genuine edge they land on (3-4 uses instead of 2), which made this
                    // gate reject geometrically closed cuts.
                    if (wa == wb || wb == wc || wc == wa) continue;
                    CountEdge(wa, wb);
                    CountEdge(wb, wc);
                    CountEdge(wc, wa);
                }
            }
            foreach (KeyValuePair<long, int> pair in edgeUse)
                if (pair.Value != 2) return false;
            return edgeUse.Count > 0;
        }

        public Bounds ComputeBounds()
        {
            if (Positions.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var min = Positions[0];
            var max = Positions[0];
            for (int i = 1; i < Positions.Count; i++)
            {
                min = Vector3.Min(min, Positions[i]);
                max = Vector3.Max(max, Positions[i]);
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        /// <summary>Signed volume via divergence theorem. Valid for (approximately) closed meshes.</summary>
        public float ComputeVolume()
        {
            double v = 0;
            for (int s = 0; s < SubmeshCount; s++)
            {
                var tris = Submeshes[s];
                for (int i = 0; i < tris.Count; i += 3)
                {
                    Vector3 a = Positions[tris[i]];
                    Vector3 b = Positions[tris[i + 1]];
                    Vector3 c = Positions[tris[i + 2]];
                    v += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
                }
            }
            return Mathf.Abs((float)v);
        }

        /// <summary>Moves the pivot to the given point (subtracts it from every vertex).</summary>
        public void Rebase(Vector3 pivot)
        {
            for (int i = 0; i < Positions.Count; i++)
                Positions[i] -= pivot;
        }

        public int AddVertex(Vector3 pos, Vector3 normal, Vector2 uv)
        {
            Positions.Add(pos);
            Normals.Add(normal);
            UVs.Add(uv);
            return Positions.Count - 1;
        }
    }

    /// <summary>
    /// Position weld with a 3x3x3 cell-neighborhood lookup, matching the ChainLoops
    /// endpoint search. Two points nearer than the quantization cell can still land in
    /// different cells when they straddle a cell boundary; a single-cell exact match then
    /// reports false "not welded" pairs and mass-produces phantom open edges. Each point
    /// registers under its own cell, lookups scan the 27 neighboring cells and pick the
    /// closest registered point within the cell tolerance.
    /// </summary>
    public struct PositionWelder
    {
        struct Entry
        {
            public int Id;
            public Vector3 Point;
        }

        readonly float cellSqr;
        readonly Dictionary<Vector3Int, List<Entry>> buckets;
        readonly List<Vector3> representatives;

        public PositionWelder(float cell)
        {
            Cell = Mathf.Max(cell, 1e-12f);
            cellSqr = Cell * Cell;
            buckets = new Dictionary<Vector3Int, List<Entry>>();
            representatives = new List<Vector3>();
        }

        public float Cell { get; }

        public int Count => representatives.Count;

        public Vector3 Representative(int id) => representatives[id];

        Vector3Int KeyOf(Vector3 point)
        {
            return new Vector3Int(
                Mathf.RoundToInt(point.x / Cell),
                Mathf.RoundToInt(point.y / Cell),
                Mathf.RoundToInt(point.z / Cell));
        }

        public int Weld(Vector3 point)
        {
            Vector3Int center = KeyOf(point);
            int bestId = -1;
            float bestSqr = cellSqr;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                var key = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                if (!buckets.TryGetValue(key, out List<Entry> candidates)) continue;
                for (int i = 0; i < candidates.Count; i++)
                {
                    float distSqr = (candidates[i].Point - point).sqrMagnitude;
                    if (distSqr > bestSqr) continue;
                    bestSqr = distSqr;
                    bestId = candidates[i].Id;
                }
            }
            if (bestId >= 0) return bestId;
            bestId = representatives.Count;
            representatives.Add(point);
            if (!buckets.TryGetValue(center, out List<Entry> bucket))
            {
                bucket = new List<Entry>(2);
                buckets.Add(center, bucket);
            }
            bucket.Add(new Entry { Id = bestId, Point = point });
            return bestId;
        }
    }

    /// <summary>
    /// Splits a WorkMesh with a plane into a positive and negative half, generating capped
    /// interior surfaces with either planar or source-edge-stretched UVs on the cut.
    /// </summary>
    public static class MeshSlicer
    {
        struct CutSegment
        {
            public Vector3 A;
            public Vector3 B;
            public Vector2 UVA;
            public Vector2 UVB;
        }

        /// <summary>Slices the mesh. Either output may be null when the plane misses the mesh.</summary>
        public static void Slice(WorkMesh source, Plane plane, float capUVScale,
            CutSurfaceUVMode capUVMode,
            out WorkMesh positive, out WorkMesh negative,
            bool repairCutTopology = true, float bakedInteriorJaggedness = 0f,
            BakedCutDetailStyle bakedCutDetailStyle = BakedCutDetailStyle.None)
        {
            var pos = new WorkMesh(source.SubmeshCount)
            {
                StructuralGroup = source.StructuralGroup
            };
            var neg = new WorkMesh(source.SubmeshCount)
            {
                StructuralGroup = source.StructuralGroup
            };

            var posMap = new Dictionary<int, int>(source.Positions.Count / 2);
            var negMap = new Dictionary<int, int>(source.Positions.Count / 2);
            // Edge key -> (posIndex, negIndex) for shared intersection vertices.
            var edgeCache = new Dictionary<long, Vector2Int>(64);
            var segments = new List<CutSegment>(64);

            float boundsSize = source.ComputeBounds().size.magnitude;
            int vertexCount = source.Positions.Count;
            var dist = new float[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                dist[i] = plane.GetDistanceToPoint(source.Positions[i]);

            float snapEpsilon = Mathf.Max(
                source.ComputeBounds().size.magnitude * 1e-6f, 1e-7f);
            for (int i = 0; i < vertexCount; i++)
                if (Mathf.Abs(dist[i]) < snapEpsilon)
                    dist[i] = 0f;

            for (int s = 0; s < source.SubmeshCount; s++)
            {
                var tris = source.Submeshes[s];
                for (int t = 0; t < tris.Count; t += 3)
                {
                    int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                    bool pa = dist[ia] >= 0f, pb = dist[ib] >= 0f, pc = dist[ic] >= 0f;

                    if (pa && pb && pc)
                    {
                        EmitTriangle(source, pos, posMap, s, ia, ib, ic);
                    }
                    else if (!pa && !pb && !pc)
                    {
                        EmitTriangle(source, neg, negMap, s, ia, ib, ic);
                    }
                    else
                    {
                        SplitTriangle(source, dist, pos, neg, posMap, negMap, edgeCache,
                            segments, s, ia, ib, ic);
                    }
                }
            }

            int needlesBeforeCaps = s_diagnostics.RejectedNeedleCapCount;
            if (pos.TriangleCount > 0 && neg.TriangleCount > 0 && segments.Count > 0)
                BuildCaps(segments, plane, capUVScale, capUVMode, pos, neg,
                    Mathf.Clamp01(bakedInteriorJaggedness), bakedCutDetailStyle);

            bool hasRealCut = pos.TriangleCount > 0 && neg.TriangleCount > 0 &&
                              segments.Count > 0;
            // A rejected needle ear leaves its contour edges open; treat the half as
            // unclosed so the fill pass below repairs the gap instead of exporting it.
            bool capsRejectedNeedles = s_diagnostics.RejectedNeedleCapCount > needlesBeforeCaps;
            bool positiveClosed = !hasRealCut || !repairCutTopology ||
                                  (!capsRejectedNeedles &&
                                   HasManifoldCutBoundary(pos, plane));
            bool negativeClosed = !hasRealCut || !repairCutTopology ||
                                  (!capsRejectedNeedles &&
                                   HasManifoldCutBoundary(neg, plane));

            // The normal path is already closed by BuildCaps. Run the substantially more
            // expensive topology repair only when validation finds a real residual hole.
            // At 200+ fragments this avoids hundreds of unnecessary contour collection,
            // chaining and triangulation passes without weakening malformed-cut recovery.
            if (repairCutTopology && !positiveClosed)
            {
                s_diagnostics.FilledCutHoleCount += FillCutHoles(pos, plane, -plane.normal,
                    capUVScale, capUVMode);
                positiveClosed = HasManifoldCutBoundary(pos, plane);
            }
            if (repairCutTopology && !negativeClosed)
            {
                s_diagnostics.FilledCutHoleCount += FillCutHoles(neg, plane, plane.normal,
                    capUVScale, capUVMode);
                negativeClosed = HasManifoldCutBoundary(neg, plane);
            }

            // Report a rare unresolved case, but keep the actual fracture. A useful cut
            // with a diagnostic is preferable to silently returning the untouched nested
            // outer and inner source surfaces as two apparent fragments.
            if (hasRealCut && (!positiveClosed || !negativeClosed))
            {
                s_diagnostics.UnresolvedCutBoundaryCount++;
            }

            positive = pos.TriangleCount > 0 ? pos : null;
            negative = neg.TriangleCount > 0 ? neg : null;
        }

        static bool HasManifoldCutBoundary(WorkMesh mesh, Plane plane)
        {
            if (mesh == null || mesh.Positions.Count == 0) return false;
            Bounds bounds = mesh.ComputeBounds();
            float weldCell = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            float planeTolerance = Mathf.Max(bounds.size.magnitude * 2e-5f, 2e-6f);
            var welder = new PositionWelder(weldCell);
            var welded = new int[mesh.Positions.Count];
            for (int i = 0; i < mesh.Positions.Count; i++)
                welded[i] = welder.Weld(mesh.Positions[i]);

            var edgeUse = new Dictionary<long, int>();
            void Count(int a, int b)
            {
                if (a == b) return;
                int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                long key = ((long)lo << 32) | (uint)hi;
                edgeUse.TryGetValue(key, out int count);
                edgeUse[key] = count + 1;
            }
            for (int s = 0; s < mesh.SubmeshCount; s++)
            {
                List<int> triangles = mesh.Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int a = welded[triangles[i]];
                    int b = welded[triangles[i + 1]];
                    int c = welded[triangles[i + 2]];
                    // Skip weld-degenerate triangles for the same reason as
                    // IsClosedManifold: their collapsed edges corrupt the use count of
                    // genuine cut-boundary edges and force needless hole fills.
                    if (a == b || b == c || c == a) continue;
                    Count(a, b);
                    Count(b, c);
                    Count(c, a);
                }
            }

            foreach (KeyValuePair<long, int> pair in edgeUse)
            {
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffffL);
                if (Mathf.Abs(plane.GetDistanceToPoint(welder.Representative(a))) <= planeTolerance &&
                    Mathf.Abs(plane.GetDistanceToPoint(welder.Representative(b))) <= planeTolerance &&
                    pair.Value != 2)
                    return false;
            }
            return true;
        }

        static int FillCutHoles(WorkMesh mesh, Plane plane, Vector3 capNormal,
            float uvScale, CutSurfaceUVMode uvMode)
        {
            if (mesh == null || mesh.TriangleCount == 0) return 0;
            int repairedContours = 0;
            // A first fill can expose a smaller residual loop around a T-junction. Two
            // passes close that residual without repeatedly layering caps indefinitely.
            for (int pass = 0; pass < 2; pass++)
            {
                List<CutSegment> boundary = CollectOpenCutSegments(mesh, plane);
                if (boundary.Count == 0) break;
                int trianglesBefore = mesh.TriangleCount;
                repairedContours += FillCutContours(mesh, boundary, plane,
                    capNormal, uvScale, uvMode);
                if (mesh.TriangleCount == trianglesBefore) break;
            }
            return repairedContours;
        }

        /// <summary>
        /// Repairs small, planar holes that already exist in an imported Full Solid source.
        /// Cut repair alone cannot see these contours because they do not lie on the current
        /// fracture plane (a common example is the open top of an otherwise solid post).
        /// Deliberately ignores non-planar and branched boundaries: guessing a surface there
        /// is more harmful than keeping the authored opening intact.
        /// </summary>
        public static int FillPlanarSourceHoles(WorkMesh mesh, float uvScale,
            CutSurfaceUVMode uvMode)
        {
            if (mesh == null || mesh.Positions.Count == 0) return 0;

            List<WorkMesh> components = mesh.SplitConnectedComponents();
            if (components.Count > 1)
            {
                mesh.Positions.Clear();
                mesh.Normals.Clear();
                mesh.UVs.Clear();
                for (int s = 0; s < mesh.SubmeshCount; s++)
                    mesh.Submeshes[s].Clear();

                int totalFilled = 0;
                for (int c = 0; c < components.Count; c++)
                {
                    totalFilled += FillPlanarSourceHoles(components[c], uvScale, uvMode);
                    int offset = mesh.Positions.Count;
                    mesh.Positions.AddRange(components[c].Positions);
                    mesh.Normals.AddRange(components[c].Normals);
                    mesh.UVs.AddRange(components[c].UVs);
                    int minSub = Mathf.Min(mesh.SubmeshCount, components[c].SubmeshCount);
                    for (int s = 0; s < minSub; s++)
                    {
                        List<int> src = components[c].Submeshes[s];
                        List<int> dst = mesh.Submeshes[s];
                        for (int i = 0; i < src.Count; i++)
                            dst.Add(src[i] + offset);
                    }
                }
                return totalFilled;
            }

            int totalFilledSingle = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                List<CutSegment> boundary = CollectOpenBoundarySegments(mesh);
                if (boundary.Count == 0) break;

                List<List<Vector3>> rawLoops = ChainLoops(boundary);
                int filledInPass = 0;
                for (int i = 0; i < rawLoops.Count; i++)
                {
                    List<Vector3> loop = CleanCapLoop(rawLoops[i]);
                    if (loop == null || loop.Count < 3) continue;

                    // Only genuinely flat loops are sealed. A non-planar boundary — a
                    // stepped or socketed post top, an L-shaped rim — cannot be closed
                    // by one flat cap without folding, and guessing a surface there is
                    // worse than leaving the authored opening. A cut face is always flat,
                    // so cut-cap holes still qualify.
                    if (!TryGetPlanarLoopFrame(loop, mesh.ComputeBounds(),
                            out Vector3 normal, out Vector3 u, out Vector3 v))
                        continue;

                    int trianglesBefore = mesh.TriangleCount;
                    // Weld the cap into the existing rim instead of emitting a coincident
                    // but disconnected copy. A disconnected cap breaks off as its own
                    // island (the "flying faces"), and its winding depends on the loop
                    // direction, so one post seals correctly and the next flips. Reusing
                    // the rim indices and orienting outward fixes both at once.
                    EmitWeldedSourceCap(mesh, loop, normal);
                    if (mesh.TriangleCount > trianglesBefore) filledInPass++;
                }

                totalFilledSingle += filledInPass;
                if (filledInPass == 0) break;
            }

            return totalFilledSingle;
        }

        /// <summary>
        /// Triangulates a flat boundary loop of an open source and welds it into the mesh:
        /// every cap vertex reuses the existing rim vertex at that position, so the cap is
        /// part of the same surface, never a floating coincident copy. The normal is forced
        /// to point away from the mesh body, so all sealed holes face outward consistently.
        /// </summary>
        static void EmitWeldedSourceCap(WorkMesh mesh, List<Vector3> loop, Vector3 normal)
        {
            if (loop.Count < 3) return;

            // Outward = away from the mesh centroid. A post-top hole sits above the body,
            // so its cap must face up, not down into the solid.
            Vector3 body = Vector3.zero;
            for (int i = 0; i < mesh.Positions.Count; i++) body += mesh.Positions[i];
            body /= Mathf.Max(1, mesh.Positions.Count);
            Vector3 capCentre = Vector3.zero;
            for (int i = 0; i < loop.Count; i++) capCentre += loop[i];
            capCentre /= loop.Count;
            if (Vector3.Dot(normal, capCentre - body) < 0f) normal = -normal;

            // Map every loop position onto the existing rim vertex. The loop came from the
            // mesh's own boundary edges, so an exact-ish match always exists.
            Bounds bounds = mesh.ComputeBounds();
            float weld = Mathf.Max(bounds.size.magnitude * 1e-5f, 1e-6f);
            float weldSqr = weld * weld;
            var ring = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                int found = -1;
                for (int p = mesh.Positions.Count - 1; p >= 0; p--)
                {
                    if ((mesh.Positions[p] - loop[i]).sqrMagnitude <= weldSqr)
                    {
                        found = p;
                        break;
                    }
                }
                ring[i] = found >= 0
                    ? found
                    : mesh.AddVertex(loop[i], normal, Vector2.zero);
            }

            Vector3 u = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            var pts = new Vector2[loop.Count];
            for (int i = 0; i < loop.Count; i++)
                pts[i] = new Vector2(Vector3.Dot(loop[i], u), Vector3.Dot(loop[i], v));

            List<int> tris = EarClip(pts) ?? TriangulateDamagedLoop(pts);
            List<int> submesh = mesh.Submeshes[0];
            if (tris != null)
            {
                for (int i = 0; i < tris.Count; i += 3)
                {
                    int a = ring[tris[i]], b = ring[tris[i + 1]], c = ring[tris[i + 2]];
                    Vector3 face = Vector3.Cross(
                        mesh.Positions[b] - mesh.Positions[a],
                        mesh.Positions[c] - mesh.Positions[a]);
                    if (Vector3.Dot(face, normal) < 0f) { int t = b; b = c; c = t; }
                    submesh.Add(a); submesh.Add(b); submesh.Add(c);
                }
                return;
            }

            // Degenerate contour: a centroid fan still closes the hole watertight.
            int centre = mesh.AddVertex(capCentre, normal, Vector2.zero);
            for (int i = 0; i < loop.Count; i++)
            {
                int a = ring[i], b = ring[(i + 1) % loop.Count];
                Vector3 face = Vector3.Cross(
                    mesh.Positions[b] - mesh.Positions[a],
                    mesh.Positions[centre] - mesh.Positions[a]);
                if (Vector3.Dot(face, normal) < 0f) { int t = a; a = b; b = t; }
                submesh.Add(a); submesh.Add(b); submesh.Add(centre);
            }
        }

        static List<CutSegment> CollectOpenBoundarySegments(WorkMesh mesh)
        {
            Bounds bounds = mesh.ComputeBounds();
            float weldCell = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            var welder = new PositionWelder(weldCell);
            var welded = new int[mesh.Positions.Count];
            for (int i = 0; i < mesh.Positions.Count; i++)
                welded[i] = welder.Weld(mesh.Positions[i]);

            var counts = new Dictionary<long, int>();
            var orientedVertices = new Dictionary<long, Vector2Int>();
            void Count(int sourceA, int sourceB)
            {
                int a = welded[sourceA], b = welded[sourceB];
                if (a == b) return;
                long key = EdgeKey(a, b);
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
                if (count == 0)
                    orientedVertices[key] = new Vector2Int(sourceA, sourceB);
            }

            for (int s = 0; s < mesh.SubmeshCount; s++)
            {
                List<int> triangles = mesh.Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    Count(triangles[i], triangles[i + 1]);
                    Count(triangles[i + 1], triangles[i + 2]);
                    Count(triangles[i + 2], triangles[i]);
                }
            }

            var result = new List<CutSegment>();
            foreach (KeyValuePair<long, int> pair in counts)
            {
                if (pair.Value % 2 == 0) continue;
                Vector2Int vertices = orientedVertices[pair.Key];
                result.Add(new CutSegment
                {
                    A = mesh.Positions[vertices.x],
                    B = mesh.Positions[vertices.y],
                    UVA = mesh.UVs[vertices.x],
                    UVB = mesh.UVs[vertices.y]
                });
            }
            return result;
        }

        static bool TryGetPlanarLoopFrame(List<Vector3> loop, Bounds meshBounds,
            out Vector3 normal, out Vector3 u, out Vector3 v)
        {
            normal = Vector3.zero;
            u = Vector3.zero;
            v = Vector3.zero;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < loop.Count; i++) centre += loop[i];
            centre /= loop.Count;

            // Newell's method remains stable for circles imported as many short edges.
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 a = loop[i] - centre;
                Vector3 b = loop[(i + 1) % loop.Count] - centre;
                normal += Vector3.Cross(a, b);
            }
            if (normal.sqrMagnitude < 1e-12f) return false;
            normal.Normalize();

            float tolerance = Mathf.Max(meshBounds.size.magnitude * 2e-4f, 2e-5f);
            for (int i = 0; i < loop.Count; i++)
                if (Mathf.Abs(Vector3.Dot(loop[i] - centre, normal)) > tolerance)
                    return false;

            u = Vector3.Cross(normal,
                Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            v = Vector3.Cross(normal, u);
            return true;
        }

        static List<CutSegment> CollectOpenCutSegments(WorkMesh mesh, Plane plane)
        {
            Bounds bounds = mesh.ComputeBounds();
            float weldCell = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            float planeTolerance = Mathf.Max(bounds.size.magnitude * 2e-5f, 2e-6f);
            var welder = new PositionWelder(weldCell);
            var welded = new int[mesh.Positions.Count];
            for (int i = 0; i < mesh.Positions.Count; i++)
                welded[i] = welder.Weld(mesh.Positions[i]);

            var counts = new Dictionary<long, int>();
            var orientedVertices = new Dictionary<long, Vector2Int>();
            void Count(int sourceA, int sourceB)
            {
                int a = welded[sourceA], b = welded[sourceB];
                if (a == b) return;
                int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                long key = ((long)lo << 32) | (uint)hi;
                counts.TryGetValue(key, out int count);
                counts[key] = count + 1;
                if (count == 0)
                    orientedVertices[key] = new Vector2Int(sourceA, sourceB);
            }
            for (int s = 0; s < mesh.SubmeshCount; s++)
            {
                List<int> triangles = mesh.Submeshes[s];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    Count(triangles[i], triangles[i + 1]);
                    Count(triangles[i + 1], triangles[i + 2]);
                    Count(triangles[i + 2], triangles[i]);
                }
            }

            var result = new List<CutSegment>();
            foreach (KeyValuePair<long, int> pair in counts)
            {
                if (pair.Value % 2 == 0) continue;
                Vector2Int vertices = orientedVertices[pair.Key];
                Vector3 a = mesh.Positions[vertices.x];
                Vector3 b = mesh.Positions[vertices.y];
                if (Mathf.Abs(plane.GetDistanceToPoint(a)) > planeTolerance ||
                    Mathf.Abs(plane.GetDistanceToPoint(b)) > planeTolerance)
                    continue;
                result.Add(new CutSegment
                {
                    A = a,
                    B = b,
                    UVA = mesh.UVs[vertices.x],
                    UVB = mesh.UVs[vertices.y]
                });
            }
            return result;
        }

        static int FillCutContours(WorkMesh mesh, List<CutSegment> boundary,
            Plane plane, Vector3 capNormal, float uvScale,
            CutSurfaceUVMode uvMode)
        {
            List<List<Vector3>> rawLoops = ChainLoops(boundary);
            var loops = new List<List<Vector3>>(rawLoops.Count);
            for (int i = 0; i < rawLoops.Count; i++)
            {
                List<Vector3> clean = CleanCapLoop(rawLoops[i]);
                if (clean != null && clean.Count >= 3) loops.Add(clean);
            }
            if (loops.Count == 0) return 0;

            Vector3 n = plane.normal;
            Vector3 u = Vector3.Cross(n,
                Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);
            float invScale = Mathf.Max(uvScale, 0.0001f);
            RemoveCoincidentLoops(loops, u, v);
            int[] parents = FindLoopParents(loops, u, v);
            int filled = 0;
            for (int i = 0; i < loops.Count; i++)
            {
                if (LoopDepth(i, parents) % 2 != 0) continue;
                var holes = new List<List<Vector3>>();
                for (int h = 0; h < loops.Count; h++)
                    if (parents[h] == i) holes.Add(loops[h]);
                List<CutSegment> extrusionChain =
                    uvMode == CutSurfaceUVMode.EdgeStretch
                        ? SelectExtrusionChain(loops[i], boundary)
                        : boundary;

                if (holes.Count == 1)
                {
                    int before = mesh.TriangleCount;
                    EmitHollowBand(mesh, loops[i], holes[0], extrusionChain,
                        capNormal, u, v, invScale, uvMode);
                    if (mesh.TriangleCount > before) filled++;
                    continue;
                }

                List<Vector3> capLoop = holes.Count > 0
                    ? BridgeHoles(loops[i], holes, u, v)
                    : loops[i];
                if (capLoop == null) continue;
                int trianglesBefore = mesh.TriangleCount;
                EmitCap(mesh, capLoop, extrusionChain, capNormal,
                    u, v, invScale, uvMode, allowFanFallback: holes.Count == 0);
                if (mesh.TriangleCount > trianglesBefore) filled++;
            }
            return filled;
        }

        static void EmitTriangle(WorkMesh src, WorkMesh dst, Dictionary<int, int> map,
            int submesh, int ia, int ib, int ic)
        {
            dst.Submeshes[submesh].Add(CopyVertex(src, dst, map, ia));
            dst.Submeshes[submesh].Add(CopyVertex(src, dst, map, ib));
            dst.Submeshes[submesh].Add(CopyVertex(src, dst, map, ic));
        }

        static int CopyVertex(WorkMesh src, WorkMesh dst, Dictionary<int, int> map, int index)
        {
            if (map.TryGetValue(index, out int mapped)) return mapped;
            mapped = dst.AddVertex(src.Positions[index], src.Normals[index], src.UVs[index]);
            map.Add(index, mapped);
            return mapped;
        }

        static long EdgeKey(int a, int b)
        {
            int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        static void SplitTriangle(WorkMesh src, float[] dist, WorkMesh pos, WorkMesh neg,
            Dictionary<int, int> posMap, Dictionary<int, int> negMap,
            Dictionary<long, Vector2Int> edgeCache, List<CutSegment> segments,
            int submesh, int ia, int ib, int ic)
        {
            // Rotate indices so that the lone vertex is first.
            bool pa = dist[ia] >= 0f, pb = dist[ib] >= 0f, pc = dist[ic] >= 0f;
            int lone, e1, e2;
            bool loneOnPositive;

            if (pa == pb) { lone = ic; e1 = ia; e2 = ib; loneOnPositive = pc; }
            else if (pa == pc) { lone = ib; e1 = ic; e2 = ia; loneOnPositive = pb; }
            else { lone = ia; e1 = ib; e2 = ic; loneOnPositive = pa; }

            // Intersections on edges lone-e1 and lone-e2 (order preserves winding lone->e1->e2).
            Vector2Int cut1 = GetOrCreateEdgeVertex(src, dist, pos, neg,
                posMap, negMap, edgeCache, lone, e1);
            Vector2Int cut2 = GetOrCreateEdgeVertex(src, dist, pos, neg,
                posMap, negMap, edgeCache, lone, e2);

            WorkMesh loneMesh = loneOnPositive ? pos : neg;
            WorkMesh pairMesh = loneOnPositive ? neg : pos;
            var loneMap = loneOnPositive ? posMap : negMap;
            var pairMap = loneOnPositive ? negMap : posMap;
            int cut1Lone = loneOnPositive ? cut1.x : cut1.y;
            int cut2Lone = loneOnPositive ? cut2.x : cut2.y;
            int cut1Pair = loneOnPositive ? cut1.y : cut1.x;
            int cut2Pair = loneOnPositive ? cut2.y : cut2.x;

            // Lone side: single triangle (lone, cut1, cut2) keeps original winding
            // because (lone, e1, e2) was a forward rotation of (ia, ib, ic). When the
            // plane grazes the lone vertex both cuts collapse onto it; the zero-area
            // result must not be emitted because it welds into a degenerate face that
            // corrupts manifold edge counts downstream.
            int loneIdx = CopyVertex(src, loneMesh, loneMap, lone);
            if (!IsDegenerateTriangle(loneMesh, loneIdx, cut1Lone, cut2Lone))
            {
                loneMesh.Submeshes[submesh].Add(loneIdx);
                loneMesh.Submeshes[submesh].Add(cut1Lone);
                loneMesh.Submeshes[submesh].Add(cut2Lone);
            }

            // Pair side: quad (cut1, e1, e2, cut2) -> two triangles. Either half
            // degenerates into a zero-area sliver when a cut lands exactly on e1/e2.
            int e1Idx = CopyVertex(src, pairMesh, pairMap, e1);
            int e2Idx = CopyVertex(src, pairMesh, pairMap, e2);
            if (!IsDegenerateTriangle(pairMesh, cut1Pair, e1Idx, e2Idx))
            {
                pairMesh.Submeshes[submesh].Add(cut1Pair);
                pairMesh.Submeshes[submesh].Add(e1Idx);
                pairMesh.Submeshes[submesh].Add(e2Idx);
            }
            if (!IsDegenerateTriangle(pairMesh, cut1Pair, e2Idx, cut2Pair))
            {
                pairMesh.Submeshes[submesh].Add(cut1Pair);
                pairMesh.Submeshes[submesh].Add(e2Idx);
                pairMesh.Submeshes[submesh].Add(cut2Pair);
            }

            // A cut segment whose endpoints coincide after the weld cannot contribute
            // to a cap loop; emitting it would seed degenerate cap contours.
            Vector3 segA = pos.Positions[cut1.x];
            Vector3 segB = pos.Positions[cut2.x];
            if (cut1.x != cut2.x && (segA - segB).sqrMagnitude > 1e-14f)
            {
                segments.Add(new CutSegment
                {
                    A = segA,
                    B = segB,
                    UVA = pos.UVs[cut1.x],
                    UVB = pos.UVs[cut2.x]
                });
            }
        }

        /// <summary>Zero-area test on already mapped output vertices.</summary>
        static bool IsDegenerateTriangle(WorkMesh mesh, int a, int b, int c)
        {
            if (a == b || b == c || c == a) return true;
            Vector3 cross = Vector3.Cross(
                mesh.Positions[b] - mesh.Positions[a],
                mesh.Positions[c] - mesh.Positions[a]);
            return cross.sqrMagnitude < 1e-14f;
        }

        /// <summary>Returns (indexInPositive, indexInNegative) of the interpolated edge
        /// vertex. An intersection landing exactly on a source endpoint (t == 0 or 1,
        /// the common case after the on-plane epsilon snap) reuses that endpoint through
        /// the caller's vertex map instead of duplicating its position; a fresh vertex
        /// would weld back onto the endpoint and leave degenerate triangles behind.</summary>
        static Vector2Int GetOrCreateEdgeVertex(WorkMesh src, float[] dist, WorkMesh pos,
            WorkMesh neg, Dictionary<int, int> posMap, Dictionary<int, int> negMap,
            Dictionary<long, Vector2Int> cache, int a, int b)
        {
            long key = EdgeKey(a, b);
            if (cache.TryGetValue(key, out var found)) return found;

            float da = dist[a], db = dist[b];
            float t = Mathf.Approximately(da, db) ? 0.5f : da / (da - db);
            t = Mathf.Clamp01(t);

            Vector2Int result;
            if (t <= 0f)
            {
                result = new Vector2Int(CopyVertex(src, pos, posMap, a),
                    CopyVertex(src, neg, negMap, a));
            }
            else if (t >= 1f)
            {
                result = new Vector2Int(CopyVertex(src, pos, posMap, b),
                    CopyVertex(src, neg, negMap, b));
            }
            else
            {
                Vector3 p = Vector3.LerpUnclamped(src.Positions[a], src.Positions[b], t);
                Vector3 n = Vector3.LerpUnclamped(src.Normals[a], src.Normals[b], t).normalized;
                Vector2 uv = Vector2.LerpUnclamped(src.UVs[a], src.UVs[b], t);
                result = new Vector2Int(pos.AddVertex(p, n, uv), neg.AddVertex(p, n, uv));
            }
            cache.Add(key, result);
            return result;
        }

        // ------------------------------------------------------------------ caps

        sealed class DiagnosticsState
        {
            public int UnchainedSegmentCount;
            public int HollowCapFailureCount;
            public int DuplicateCapLoopCount;
            public int FilledCutHoleCount;
            public int FilledSourceHoleCount;
            public int UnresolvedCutBoundaryCount;
            public int RejectedNeedleCapCount;
        }

        static readonly DiagnosticsState s_diagnostics = new DiagnosticsState();

        /// <summary>Segments that failed to chain into closed loops during the current
        /// fracture run. FractureEngine aggregates this into one bake warning.</summary>
        public static int UnchainedSegmentCount => s_diagnostics.UnchainedSegmentCount;
        /// <summary>Hollow cap groups that could not be bridged without crossing contours.</summary>
        public static int HollowCapFailureCount => s_diagnostics.HollowCapFailureCount;
        /// <summary>Coincident cap contours removed before triangulation.</summary>
        public static int DuplicateCapLoopCount => s_diagnostics.DuplicateCapLoopCount;
        /// <summary>Residual cut-plane contours repaired by the post-cap fill pass.</summary>
        public static int FilledCutHoleCount => s_diagnostics.FilledCutHoleCount;
        /// <summary>Planar source contours sealed before Full Solid fracture begins.</summary>
        public static int FilledSourceHoleCount => s_diagnostics.FilledSourceHoleCount;
        /// <summary>Cuts retained with a residual boundary after both fill passes.</summary>
        public static int UnresolvedCutBoundaryCount => s_diagnostics.UnresolvedCutBoundaryCount;
        /// <summary>Cap ear triangles rejected as needles (extreme aspect ratio). Slice
        /// watches this counter and forces a hole-fill pass so a rejection never leaves
        /// an open contour behind.</summary>
        public static int RejectedNeedleCapCount => s_diagnostics.RejectedNeedleCapCount;

        public static void ResetDiagnostics()
        {
            s_diagnostics.UnchainedSegmentCount = 0;
            s_diagnostics.HollowCapFailureCount = 0;
            s_diagnostics.DuplicateCapLoopCount = 0;
            s_diagnostics.FilledCutHoleCount = 0;
            s_diagnostics.FilledSourceHoleCount = 0;
            s_diagnostics.UnresolvedCutBoundaryCount = 0;
            s_diagnostics.RejectedNeedleCapCount = 0;
        }

        public static void RestoreDiagnostics(int unchainedSegments,
            int hollowCapFailures, int duplicateCapLoops, int filledCutHoles,
            int filledSourceHoles, int unresolvedCutBoundaries, int rejectedNeedleCaps)
        {
            s_diagnostics.UnchainedSegmentCount = unchainedSegments;
            s_diagnostics.HollowCapFailureCount = hollowCapFailures;
            s_diagnostics.DuplicateCapLoopCount = duplicateCapLoops;
            s_diagnostics.FilledCutHoleCount = filledCutHoles;
            s_diagnostics.FilledSourceHoleCount = filledSourceHoles;
            s_diagnostics.UnresolvedCutBoundaryCount = unresolvedCutBoundaries;
            s_diagnostics.RejectedNeedleCapCount = rejectedNeedleCaps;
        }

        /// <summary>Maximum allowed cap triangle aspect ratio L^2/(2A). Scale-invariant:
        /// an equilateral triangle scores ~1.15, a merely thin sliver stays below 1e3.</summary>
        const float MaxCapAspectRatio = 1e4f;

        static void BuildCaps(List<CutSegment> segments, Plane plane, float uvScale,
            CutSurfaceUVMode uvMode,
            WorkMesh pos, WorkMesh neg, float bakedInteriorJaggedness,
            BakedCutDetailStyle bakedCutDetailStyle)
        {
            var loops = ChainLoops(segments);
            Vector3 n = plane.normal;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);
            float invScale = Mathf.Max(uvScale, 0.0001f);

            var cleanLoops = new List<List<Vector3>>(loops.Count);
            foreach (var loop in loops)
            {
                List<Vector3> cleanLoop = CleanCapLoop(loop);
                if (cleanLoop != null && cleanLoop.Count >= 3)
                    cleanLoops.Add(cleanLoop);
            }
            RemoveCoincidentLoops(cleanLoops, u, v);

            // Nested section loops describe a hollow shell (for example the outside and
            // inside walls of a barrel). Treat direct children as holes in their parent
            // instead of independently capping both loops and filling the cavity.
            int[] parents = FindLoopParents(cleanLoops, u, v);
            for (int i = 0; i < cleanLoops.Count; i++)
            {
                if (LoopDepth(i, parents) % 2 != 0) continue;

                // Edge Stretch must be driven by one coherent source-edge run. Feeding
                // every UV island around the contour into one cap creates a triangular
                // patchwork as unrelated seams interpolate through its interior.
                List<CutSegment> extrusionChain =
                    uvMode == CutSurfaceUVMode.EdgeStretch
                        ? SelectExtrusionChain(cleanLoops[i], segments)
                        : segments;

                var holes = new List<List<Vector3>>();
                for (int h = 0; h < cleanLoops.Count; h++)
                    if (parents[h] == i) holes.Add(cleanLoops[h]);

                // A single nested contour is the common barrel/pipe case. Stitch the
                // outer and inner rings directly. Converting an annulus into a bridged
                // simple polygon can appear to triangulate successfully while still
                // placing triangles across the cavity, making a hollow mesh look solid.
                if (holes.Count == 1)
                {
                    EmitHollowBand(pos, cleanLoops[i], holes[0], extrusionChain, -n,
                        u, v, invScale, uvMode);
                    EmitHollowBand(neg, cleanLoops[i], holes[0], extrusionChain, n,
                        u, v, invScale, uvMode);
                    continue;
                }

                // Ear clipping handles concave cross-sections; the centroid fan remains
                // the fallback only for caps without holes. Bridged hollow caps must never
                // fall back to a fan because that would fill the cavity again.
                List<Vector3> capLoop = holes.Count > 0
                    ? BridgeHoles(cleanLoops[i], holes, u, v)
                    : cleanLoops[i];
                // A bridged contour contains duplicated bridge edges. It is correct for
                // a planar cap, but raising each ear independently can fold the bridge
                // over itself and create loose interior faces around windows/cavities.
                // Detail is therefore restricted to a modest, simple contour only.
                bool supportsBakedDetail = holes.Count == 0 && capLoop != null &&
                    capLoop.Count <= 48;
                float capDetail = supportsBakedDetail
                    ? bakedInteriorJaggedness : 0f;
                bool posComplete = capLoop != null &&
                    EmitCap(pos, capLoop, extrusionChain, -n, u, v, invScale, uvMode,
                        allowFanFallback: holes.Count == 0,
                        bakedInteriorJaggedness: capDetail, cutNormal: n,
                        bakedCutDetailStyle: bakedCutDetailStyle);
                bool negComplete = capLoop != null &&
                    EmitCap(neg, capLoop, extrusionChain, n, u, v, invScale, uvMode,
                        allowFanFallback: holes.Count == 0,
                        bakedInteriorJaggedness: capDetail, cutNormal: n,
                        bakedCutDetailStyle: bakedCutDetailStyle);

                if (holes.Count > 0 && (!posComplete || !negComplete))
                    s_diagnostics.HollowCapFailureCount++;
            }
        }

        static int[] FindLoopParents(List<List<Vector3>> loops, Vector3 u, Vector3 v)
        {
            int[] parents = new int[loops.Count];
            var projected = new Vector2[loops.Count][];
            var areas = new float[loops.Count];
            var mins = new Vector2[loops.Count];
            var maxs = new Vector2[loops.Count];

            for (int i = 0; i < loops.Count; i++)
            {
                parents[i] = -1;
                projected[i] = ProjectLoop(loops[i], u, v);
                areas[i] = Mathf.Abs(SignedArea(projected[i]));

                Vector2[] pts = projected[i];
                Vector2 min = pts[0], max = pts[0];
                for (int p = 1; p < pts.Length; p++)
                {
                    min = Vector2.Min(min, pts[p]);
                    max = Vector2.Max(max, pts[p]);
                }
                mins[i] = min;
                maxs[i] = max;
            }

            for (int child = 0; child < loops.Count; child++)
            {
                float smallestParentArea = float.PositiveInfinity;
                Vector2 sample = projected[child][0];
                for (int candidate = 0; candidate < loops.Count; candidate++)
                {
                    if (candidate == child || areas[candidate] <= areas[child] ||
                        areas[candidate] >= smallestParentArea)
                        continue;

                    // 2D bounding box containment check with epsilon tolerance
                    const float eps = 1e-4f;
                    if (mins[child].x < mins[candidate].x - eps ||
                        maxs[child].x > maxs[candidate].x + eps ||
                        mins[child].y < mins[candidate].y - eps ||
                        maxs[child].y > maxs[candidate].y + eps)
                        continue;

                    if (!PointInPolygon(sample, projected[candidate]))
                        continue;

                    parents[child] = candidate;
                    smallestParentArea = areas[candidate];
                }
            }
            return parents;
        }

        static void RemoveCoincidentLoops(List<List<Vector3>> loops,
            Vector3 u, Vector3 v)
        {
            if (loops.Count < 2) return;
            float scale = 0f;
            for (int i = 0; i < loops.Count; i++)
            for (int p = 0; p < loops[i].Count; p++)
                scale = Mathf.Max(scale, Project(loops[i][p], u, v).magnitude);
            float tolerance = Mathf.Max(scale * 1e-5f, 1e-6f);

            for (int candidate = loops.Count - 1; candidate >= 0; candidate--)
            {
                for (int kept = 0; kept < candidate; kept++)
                {
                    if (!LoopsCoincident(loops[candidate], loops[kept],
                            u, v, tolerance))
                        continue;
                    loops.RemoveAt(candidate);
                    s_diagnostics.DuplicateCapLoopCount++;
                    break;
                }
            }
        }

        static bool LoopsCoincident(List<Vector3> a, List<Vector3> b,
            Vector3 u, Vector3 v, float tolerance)
        {
            Vector2[] projectedA = ProjectLoop(a, u, v);
            Vector2[] projectedB = ProjectLoop(b, u, v);
            float areaA = Mathf.Abs(SignedArea(projectedA));
            float areaB = Mathf.Abs(SignedArea(projectedB));
            float areaTolerance = Mathf.Max(Mathf.Max(areaA, areaB) * 1e-3f,
                tolerance * tolerance);
            if (Mathf.Abs(areaA - areaB) > areaTolerance) return false;
            float toleranceSqr = tolerance * tolerance;
            return LoopFollowsContour(projectedA, projectedB, toleranceSqr) &&
                   LoopFollowsContour(projectedB, projectedA, toleranceSqr);
        }

        static bool LoopFollowsContour(Vector2[] points, Vector2[] contour,
            float toleranceSqr)
        {
            for (int p = 0; p < points.Length; p++)
            {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < contour.Length; i++)
                {
                    Vector2 a = contour[i];
                    Vector2 b = contour[(i + 1) % contour.Length];
                    Vector2 ab = b - a;
                    float denominator = ab.sqrMagnitude;
                    float t = denominator > 1e-20f
                        ? Mathf.Clamp01(Vector2.Dot(points[p] - a, ab) / denominator)
                        : 0f;
                    nearest = Mathf.Min(nearest,
                        (points[p] - (a + ab * t)).sqrMagnitude);
                }
                if (nearest > toleranceSqr) return false;
            }
            return true;
        }

        static int LoopDepth(int index, int[] parents)
        {
            int depth = 0;
            int guard = parents.Length;
            while (index >= 0 && index < parents.Length && parents[index] >= 0 && guard-- > 0)
            {
                depth++;
                index = parents[index];
            }
            return depth;
        }

        static List<Vector3> BridgeHoles(List<Vector3> outer,
            List<List<Vector3>> holes, Vector3 u, Vector3 v)
        {
            var merged = new List<Vector3>(outer);
            EnsureLoopWinding(merged, u, v, counterClockwise: true);

            var merged2D = new List<Vector2>(merged.Count);
            for (int i = 0; i < merged.Count; i++)
                merged2D.Add(Project(merged[i], u, v));

            // Rightmost holes first reduces the chance that a later bridge crosses an
            // earlier one when several cavities share the same section.
            holes.Sort((a, b) => RightmostX(b, u).CompareTo(RightmostX(a, u)));
            for (int h = 0; h < holes.Count; h++)
            {
                var hole = new List<Vector3>(holes[h]);
                EnsureLoopWinding(hole, u, v, counterClockwise: false);

                var hole2D = new List<Vector2>(hole.Count);
                for (int i = 0; i < hole.Count; i++)
                    hole2D.Add(Project(hole[i], u, v));

                if (!MergeHole(merged, merged2D, hole, hole2D))
                    return null;
            }
            return merged;
        }

        static bool MergeHole(List<Vector3> outer, List<Vector2> outer2D,
            List<Vector3> hole, List<Vector2> hole2D)
        {
            if (outer.Count < 3 || hole.Count < 3) return false;
            int bestOuter = -1, bestHole = -1;

            // To prevent O(N^3) complexity due to slow monotonic improvement of bestDistance,
            // we first find the closest pair of vertices WITHOUT checking intersections.
            int closestHi = 0, closestOi = 0;
            float minDistance = float.PositiveInfinity;
            for (int hi = 0; hi < hole.Count; hi++)
            {
                Vector2 hp = hole2D[hi];
                for (int oi = 0; oi < outer.Count; oi++)
                {
                    float distance = (outer2D[oi] - hp).sqrMagnitude;
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestHi = hi;
                        closestOi = oi;
                    }
                }
            }

            // Test pairs starting from the closest vertices outwards. This rapidly establishes
            // a near-optimal bestDistance, massively pruning the expensive intersection tests.
            float bestDistance = float.PositiveInfinity;
            for (int hOffset = 0; hOffset < hole.Count; hOffset++)
            {
                int hStep = (hOffset % 2 == 1) ? (hOffset + 1) / 2 : -(hOffset / 2);
                int hi = (closestHi + hStep + hole.Count) % hole.Count;
                Vector2 hp = hole2D[hi];

                for (int oOffset = 0; oOffset < outer.Count; oOffset++)
                {
                    int oStep = (oOffset % 2 == 1) ? (oOffset + 1) / 2 : -(oOffset / 2);
                    int oi = (closestOi + oStep + outer.Count) % outer.Count;
                    Vector2 op = outer2D[oi];
                    float distance = (op - hp).sqrMagnitude;

                    if (distance >= bestDistance) continue;

                    if (!BridgeIntersectsLoop(hp, op, hole2D, hi) &&
                        !BridgeIntersectsLoop(hp, op, outer2D, oi))
                    {
                        bestDistance = distance;
                        bestOuter = oi;
                        bestHole = hi;
                    }
                }
            }

            if (bestOuter < 0 || bestHole < 0) return false;

            MergeHoleAt(outer, hole, bestOuter, bestHole);
            MergeHoleAt(outer2D, hole2D, bestOuter, bestHole);
            return true;
        }

        static bool MergeHoleAt<T>(List<T> outer, List<T> hole,
            int bestOuter, int bestHole)
        {
            if (bestOuter < 0 || bestHole < 0) return false;

            var combined = new List<T>(outer.Count + hole.Count + 2);
            for (int i = 0; i <= bestOuter; i++) combined.Add(outer[i]);
            combined.Add(hole[bestHole]);
            for (int step = 1; step < hole.Count; step++)
                combined.Add(hole[(bestHole + step) % hole.Count]);
            combined.Add(hole[bestHole]);
            combined.Add(outer[bestOuter]);
            for (int i = bestOuter + 1; i < outer.Count; i++) combined.Add(outer[i]);

            outer.Clear();
            outer.AddRange(combined);
            return true;
        }

        static bool BridgeIntersectsLoop(Vector2 a, Vector2 b, List<Vector2> loop2D,
            int allowedVertex)
        {
            for (int i = 0; i < loop2D.Count; i++)
            {
                int j = (i + 1) % loop2D.Count;
                if (i == allowedVertex || j == allowedVertex) continue;
                if (SegmentsIntersect(a, b, loop2D[i], loop2D[j]))
                    return true;
            }
            return false;
        }

        static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            const float epsilon = 1e-8f;
            float abC = Cross2(a, b, c);
            float abD = Cross2(a, b, d);
            float cdA = Cross2(c, d, a);
            float cdB = Cross2(c, d, b);
            return ((abC > epsilon && abD < -epsilon) ||
                    (abC < -epsilon && abD > epsilon)) &&
                   ((cdA > epsilon && cdB < -epsilon) ||
                    (cdA < -epsilon && cdB > epsilon));
        }

        static void EnsureLoopWinding(List<Vector3> loop, Vector3 u, Vector3 v,
            bool counterClockwise)
        {
            bool isCounterClockwise = SignedArea(ProjectLoop(loop, u, v)) > 0f;
            if (isCounterClockwise != counterClockwise) loop.Reverse();
        }

        static float RightmostX(List<Vector3> loop, Vector3 u)
        {
            float value = float.NegativeInfinity;
            for (int i = 0; i < loop.Count; i++)
                value = Mathf.Max(value, Vector3.Dot(loop[i], u));
            return value;
        }

        static Vector2[] ProjectLoop(List<Vector3> loop, Vector3 u, Vector3 v)
        {
            var result = new Vector2[loop.Count];
            for (int i = 0; i < loop.Count; i++) result[i] = Project(loop[i], u, v);
            return result;
        }

        static Vector2 Project(Vector3 point, Vector3 u, Vector3 v) =>
            new Vector2(Vector3.Dot(point, u), Vector3.Dot(point, v));

        static bool PointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) /
                    (b.y - a.y) + a.x;
                if (crosses) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Even-odd point-in-polygon test that treats boundary points as inside.
        /// The on-edge tolerance scales with the polygon extent, so a needle ear
        /// resting exactly on a cut edge is still classified as interior.
        /// </summary>
        static bool PointInPolygon2D(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;
            float extent = 0f;
            for (int i = 0; i < polygon.Length; i++)
                extent = Mathf.Max(extent,
                    Mathf.Max(Mathf.Abs(polygon[i].x), Mathf.Abs(polygon[i].y)));
            float tol = Mathf.Max(extent * 1e-6f, 1e-9f);
            float tolSqr = tol * tol;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i];
                Vector2 ab = b - a;
                float lenSqr = ab.sqrMagnitude;
                if (lenSqr <= 0f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lenSqr);
                Vector2 closest = a + ab * t;
                if ((point - closest).sqrMagnitude <= tolSqr) return true;
            }
            return PointInPolygon(point, polygon);
        }

        /// <summary>
        /// Removes only duplicate/microscopic consecutive vertices before triangulation.
        /// Collinear boundary vertices are intentionally preserved: the exterior cut uses
        /// edges A-B and B-C, so replacing them with one cap edge A-C creates a T-junction
        /// and a non-manifold result even when the surface looks visually covered.
        /// </summary>
        static List<Vector3> CleanCapLoop(List<Vector3> source)
        {
            if (source == null || source.Count < 3) return null;

            Bounds bounds = new Bounds(source[0], Vector3.zero);
            for (int i = 1; i < source.Count; i++) bounds.Encapsulate(source[i]);
            float tolerance = Mathf.Max(bounds.size.magnitude * 1e-6f, 1e-7f);
            float toleranceSqr = tolerance * tolerance;

            var clean = new List<Vector3>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 point = source[i];
                if (clean.Count == 0 ||
                    (point - clean[clean.Count - 1]).sqrMagnitude > toleranceSqr)
                    clean.Add(point);
            }
            if (clean.Count > 2 &&
                (clean[0] - clean[clean.Count - 1]).sqrMagnitude <= toleranceSqr)
                clean.RemoveAt(clean.Count - 1);

            bool removed;
            int guard = clean.Count * 2;
            do
            {
                removed = false;
                for (int i = 0; i < clean.Count && clean.Count >= 3; i++)
                {
                    Vector3 previous = clean[(i + clean.Count - 1) % clean.Count];
                    Vector3 current = clean[i];
                    Vector3 next = clean[(i + 1) % clean.Count];
                    Vector3 a = current - previous;
                    Vector3 b = next - current;
                    if (a.sqrMagnitude <= toleranceSqr || b.sqrMagnitude <= toleranceSqr)
                    {
                        clean.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            } while (removed && --guard > 0);

            return clean.Count >= 3 ? clean : null;
        }

        static bool EmitCap(WorkMesh mesh, List<Vector3> loop,
            List<CutSegment> sourceSegments, Vector3 capNormal,
            Vector3 u, Vector3 v, float invScale, CutSurfaceUVMode uvMode,
            bool allowFanFallback = true, float bakedInteriorJaggedness = 0f,
            Vector3 cutNormal = default,
            BakedCutDetailStyle bakedCutDetailStyle = BakedCutDetailStyle.None)
        {
            // Project the loop onto the cap plane and triangulate with ear clipping,
            // which fills concave cross-sections correctly.
            var pts = new Vector2[loop.Count];
            for (int i = 0; i < loop.Count; i++)
                pts[i] = new Vector2(Vector3.Dot(loop[i], u), Vector3.Dot(loop[i], v));

            List<int> triangles = EarClip(pts);
            if (triangles == null)
                triangles = TriangulateDamagedLoop(pts);
            if (triangles == null)
            {
                // When ear clipping and damaged loop triangulation fail, emit a centroid fan
                // fallback to guarantee that the loop is never left open.
                Vector3 centroid = Vector3.zero;
                for (int i = 0; i < loop.Count; i++) centroid += loop[i];
                centroid /= loop.Count;
                EmitCapFan(mesh, loop, sourceSegments, centroid,
                    capNormal, u, v, invScale, uvMode);
                return true;
            }

            CutPlaneUVBounds uvBounds = (uvMode == CutSurfaceUVMode.FitToCut)
                ? ComputeCutPlaneUVBounds(sourceSegments, u, v)
                : default;
            int submesh = mesh.InteriorSubmesh;
            bool emitFacets = bakedInteriorJaggedness > 0.0001f &&
                              cutNormal.sqrMagnitude > 1e-8f;
            var ringUV = new Vector2[loop.Count];
            // Keep the planar ring ready even when detail is enabled: only a bounded,
            // deterministic subset of ears receives a raised facet. The remainder stays
            // planar, which limits bake cost and avoids amplifying noisy source topology.
            int[] ringIdx = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 uv = (uvMode == CutSurfaceUVMode.FitToCut || uvMode == CutSurfaceUVMode.EdgeStretch)
                    ? SampleExtrudedUV(loop[i], sourceSegments, u, v, uvMode, uvBounds)
                    : new Vector2(pts[i].x * invScale, pts[i].y * invScale);
                ringUV[i] = uv;
                ringIdx[i] = mesh.AddVertex(loop[i], capNormal, uv);
            }

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 ab = loop[b] - loop[a];
                Vector3 ac = loop[c] - loop[a];
                Vector3 cross = Vector3.Cross(ab, ac);

                if (emitFacets && ShouldEmitBakedFacet(loop[a], loop[b], loop[c],
                        bakedInteriorJaggedness))
                {
                    EmitJaggedCapFacet(mesh, loop[a], ringUV[a], loop[b], ringUV[b],
                        loop[c], ringUV[c], capNormal, cutNormal,
                        bakedInteriorJaggedness, bakedCutDetailStyle, submesh);
                    continue;
                }

                if (cross.sqrMagnitude >= 1e-16f && Vector3.Dot(cross, capNormal) < 0f)
                {
                    int tmp = b; b = c; c = tmp;
                }
                mesh.Submeshes[submesh].Add(ringIdx[a]);
                mesh.Submeshes[submesh].Add(ringIdx[b]);
                mesh.Submeshes[submesh].Add(ringIdx[c]);
            }
            return true;
        }

        /// <summary>
        /// Replaces one planar cap triangle with three triangles meeting at a deterministic
        /// offset peak. The 2D footprint is unchanged, so adjacent cap triangles still
        /// meet exactly at their original edges; both fracture halves receive the same
        /// peak and therefore share one watertight, sharp interior surface.
        /// </summary>
        static void EmitJaggedCapFacet(WorkMesh mesh,
            Vector3 a, Vector2 uvA, Vector3 b, Vector2 uvB, Vector3 c, Vector2 uvC,
            Vector3 capNormal, Vector3 cutNormal, float jaggedness,
            BakedCutDetailStyle detailStyle, int submesh)
        {
            Vector3 centroid = (a + b + c) / 3f;
            float edgeA = Vector3.Distance(a, b);
            float edgeB = Vector3.Distance(b, c);
            float edgeC = Vector3.Distance(c, a);
            float longestEdge = Mathf.Max(edgeA, Mathf.Max(edgeB, edgeC));
            float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
            if (longestEdge <= 1e-6f || area <= 1e-12f)
            {
                // A valid but extremely small ear must still close the cap. Omitting
                // it while neighbouring ears receive facets would create a pin-hole.
                AddJaggedFacetTriangle(mesh, a, uvA, b, uvB, c, uvC,
                    capNormal, submesh);
                return;
            }

            float hash = DeterministicFacetHash(centroid,
                new Vector3(12.9898f, 78.233f, 37.719f));
            // Keep the displacement shallow. The old 9-face micro-detail amplified
            // thin architectural ears into long blades and made previews needlessly
            // slow. A single three-face ridge remains clearly readable in shading.
            float amplitude = Mathf.Min(longestEdge * 0.14f,
                Mathf.Sqrt(area) * 0.22f) * Mathf.Clamp01(jaggedness);
            GetFacetPeak(a, uvA, b, uvB, c, uvC, cutNormal, detailStyle,
                amplitude, hash, out Vector3 peak, out Vector2 peakUV);

            AddJaggedFacetTriangle(mesh, a, uvA, b, uvB, peak, peakUV,
                capNormal, submesh);
            AddJaggedFacetTriangle(mesh, b, uvB, c, uvC, peak, peakUV,
                capNormal, submesh);
            AddJaggedFacetTriangle(mesh, c, uvC, a, uvA, peak, peakUV,
                capNormal, submesh);
        }

        static bool ShouldEmitBakedFacet(Vector3 a, Vector3 b, Vector3 c,
            float jaggedness)
        {
            Vector3 centroid = (a + b + c) / 3f;
            float hash = DeterministicFacetHash(centroid,
                new Vector3(43.271f, 17.927f, 71.113f));
            float chance = Mathf.Lerp(0.08f, 0.38f, Mathf.Clamp01(jaggedness));
            return hash <= chance;
        }

        static float DeterministicFacetHash(Vector3 point, Vector3 salt)
        {
            float hash = Mathf.Sin(Vector3.Dot(point, salt)) * 43758.5453f;
            return hash - Mathf.Floor(hash);
        }

        /// <summary>
        /// Chooses an in-triangle peak footprint, then raises it along the shared cut
        /// normal. Because the boundary ring is never moved, source/exterior faces
        /// cannot open. Both output halves call this deterministically and therefore
        /// meet at exactly the same non-planar interior surface.
        /// </summary>
        static void GetFacetPeak(Vector3 a, Vector2 uvA, Vector3 b, Vector2 uvB,
            Vector3 c, Vector2 uvC, Vector3 cutNormal,
            BakedCutDetailStyle detailStyle, float amplitude, float hash,
            out Vector3 peak, out Vector2 peakUV)
        {
            Vector3 footprint;
            Vector2 footprintUV;
            float height = amplitude * Mathf.Lerp(0.35f, 1f, hash);
            if (detailStyle == BakedCutDetailStyle.WoodSplinters)
            {
                // Bias the raised ridge toward the longest triangle edge. Directional
                // wood cuts already align that edge with the grain, producing long,
                // pointed splinters instead of isotropic concrete-like chips.
                float ab = (b - a).sqrMagnitude;
                float bc = (c - b).sqrMagnitude;
                float ca = (a - c).sqrMagnitude;
                Vector3 edgeA, edgeB, tip;
                Vector2 edgeUVA, edgeUVB, tipUV;
                if (ab >= bc && ab >= ca)
                {
                    edgeA = a; edgeB = b; tip = c;
                    edgeUVA = uvA; edgeUVB = uvB; tipUV = uvC;
                }
                else if (bc >= ca)
                {
                    edgeA = b; edgeB = c; tip = a;
                    edgeUVA = uvB; edgeUVB = uvC; tipUV = uvA;
                }
                else
                {
                    edgeA = c; edgeB = a; tip = b;
                    edgeUVA = uvC; edgeUVB = uvA; tipUV = uvB;
                }
                float alongEdge = Mathf.Lerp(0.18f, 0.82f, hash);
                Vector3 edgePoint = Vector3.Lerp(edgeA, edgeB, alongEdge);
                Vector2 edgePointUV = Vector2.Lerp(edgeUVA, edgeUVB, alongEdge);
                float towardTip = Mathf.Lerp(0.08f, 0.24f,
                    DeterministicFacetHash((a + b + c) * 0.5f,
                        new Vector3(5.918f, 41.467f, 19.213f)));
                footprint = Vector3.Lerp(edgePoint, tip, towardTip);
                footprintUV = Vector2.Lerp(edgePointUV, tipUV, towardTip);
                height *= 1.35f;
            }
            else
            {
                // Concrete gets a broad, chipped footprint instead of a centered,
                // regular pyramid. All barycentric weights stay positive, so the
                // footprint remains inside the original cap triangle.
                float h1 = DeterministicFacetHash(a + c,
                    new Vector3(31.113f, 7.823f, 59.291f));
                float h2 = DeterministicFacetHash(b + c,
                    new Vector3(17.903f, 53.407f, 2.719f));
                float wa = 0.18f + h1 * 0.28f;
                float wb = 0.18f + h2 * 0.28f;
                float wc = 1f - wa - wb;
                footprint = a * wa + b * wb + c * wc;
                footprintUV = uvA * wa + uvB * wb + uvC * wc;
            }

            peak = footprint + cutNormal.normalized * height;
            peakUV = footprintUV;
        }

        static void AddJaggedFacetTriangle(WorkMesh mesh,
            Vector3 a, Vector2 uvA, Vector3 b, Vector2 uvB, Vector3 c, Vector2 uvC,
            Vector3 capNormal, int submesh)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-16f)
            {
                normal = capNormal;
            }
            else
            {
                if (Vector3.Dot(normal, capNormal) < 0f)
                {
                    Vector3 pointSwap = b;
                    b = c;
                    c = pointSwap;
                    Vector2 uvSwap = uvB;
                    uvB = uvC;
                    uvC = uvSwap;
                    normal = -normal;
                }
                normal.Normalize();
            }
            int ia = mesh.AddVertex(a, normal, uvA);
            int ib = mesh.AddVertex(b, normal, uvB);
            int ic = mesh.AddVertex(c, normal, uvC);
            mesh.Submeshes[submesh].Add(ia);
            mesh.Submeshes[submesh].Add(ib);
            mesh.Submeshes[submesh].Add(ic);
        }



        /// <summary>
        /// Robust annulus fallback for a hollow object cut. It advances around the outer
        /// and inner rings like a zipper and emits a triangle strip between them. Unlike a
        /// centroid fan this closes every cut edge without covering the hollow cavity.
        /// </summary>
        static void EmitHollowBand(WorkMesh mesh, List<Vector3> outerSource,
            List<Vector3> innerSource, List<CutSegment> sourceSegments,
            Vector3 capNormal, Vector3 u, Vector3 v, float invScale,
            CutSurfaceUVMode uvMode)
        {
            var outer = new List<Vector3>(outerSource);
            var inner = new List<Vector3>(innerSource);
            EnsureLoopWinding(outer, u, v, counterClockwise: true);
            EnsureLoopWinding(inner, u, v, counterClockwise: true);
            if (outer.Count < 3 || inner.Count < 3) return;

            int outerStart = 0, innerStart = 0;
            float closest = float.PositiveInfinity;
            for (int oi = 0; oi < outer.Count; oi++)
            for (int hi = 0; hi < inner.Count; hi++)
            {
                float distance = (Project(outer[oi], u, v) -
                                  Project(inner[hi], u, v)).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                outerStart = oi;
                innerStart = hi;
            }

            CutPlaneUVBounds uvBounds = (uvMode == CutSurfaceUVMode.FitToCut)
                ? ComputeCutPlaneUVBounds(sourceSegments, u, v)
                : default;
            Vector2 MakeUV(Vector3 point)
            {
                Vector2 projected = Project(point, u, v);
                return (uvMode == CutSurfaceUVMode.FitToCut || uvMode == CutSurfaceUVMode.EdgeStretch)
                    ? SampleExtrudedUV(point, sourceSegments, u, v, uvMode, uvBounds)
                    : projected * invScale;
            }

            var outerIndices = new int[outer.Count];
            var innerIndices = new int[inner.Count];
            for (int i = 0; i < outer.Count; i++)
                outerIndices[i] = mesh.AddVertex(outer[i], capNormal, MakeUV(outer[i]));
            for (int i = 0; i < inner.Count; i++)
                innerIndices[i] = mesh.AddVertex(inner[i], capNormal, MakeUV(inner[i]));

            int outerStep = 0, innerStep = 0;
            while (outerStep < outer.Count || innerStep < inner.Count)
            {
                int oi = (outerStart + outerStep) % outer.Count;
                int hi = (innerStart + innerStep) % inner.Count;
                
                bool advanceOuter;
                if (outerStep >= outer.Count)
                {
                    advanceOuter = false;
                }
                else if (innerStep >= inner.Count)
                {
                    advanceOuter = true;
                }
                else
                {
                    int nextOi = (outerStart + outerStep + 1) % outer.Count;
                    int nextHi = (innerStart + innerStep + 1) % inner.Count;
                    float distIfOuter = (inner[hi] - outer[nextOi]).sqrMagnitude;
                    float distIfInner = (inner[nextHi] - outer[oi]).sqrMagnitude;
                    advanceOuter = distIfOuter <= distIfInner;
                }

                if (advanceOuter)
                {
                    int next = (outerStart + outerStep + 1) % outer.Count;
                    AddCapTriangle(mesh, outerIndices[oi], outerIndices[next],
                        innerIndices[hi], capNormal);
                    outerStep++;
                }
                else
                {
                    int next = (innerStart + innerStep + 1) % inner.Count;
                    AddCapTriangle(mesh, outerIndices[oi], innerIndices[next],
                        innerIndices[hi], capNormal);
                    innerStep++;
                }
            }
        }

        static void AddCapTriangle(WorkMesh mesh, int a, int b, int c,
            Vector3 capNormal)
        {
            Vector3 cross = Vector3.Cross(
                mesh.Positions[b] - mesh.Positions[a],
                mesh.Positions[c] - mesh.Positions[a]);
            if (cross.sqrMagnitude >= 1e-14f && Vector3.Dot(cross, capNormal) < 0f)
            {
                int swap = b;
                b = c;
                c = swap;
            }
            List<int> triangles = mesh.Submeshes[mesh.InteriorSubmesh];
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
        }

        /// <summary>
        /// Ear-clipping triangulation of a simple 2D polygon. Returns null for
        /// self-intersecting or degenerate input so the caller can fall back to a fan.
        /// </summary>
        static List<int> EarClip(Vector2[] pts)
        {
            int n = pts.Length;
            if (n < 3) return null;

            float area = SignedArea(pts);
            if (Mathf.Abs(area) < 1e-12f) return null;
            bool ccw = area > 0f;

            var indices = new List<int>(n);
            for (int i = 0; i < n; i++) indices.Add(i);

            var result = new List<int>((n - 2) * 3);
            int guard = n * n * 4;
            // Resume the scan past a clipped ear instead of restarting from zero:
            // vertices already rejected in this pass cannot become ears before their
            // neighborhood changes, so restarting only multiplied the cost to O(n^3).
            int scan = 0;
            int rejected = 0;
            while (indices.Count > 3 && guard-- > 0)
            {
                if (scan >= indices.Count) scan = 0;
                int count = indices.Count;
                int i0 = indices[(scan + count - 1) % count];
                int i1 = indices[scan];
                int i2 = indices[(scan + 1) % count];
                bool clipped = false;
                if (IsConvex(pts[i0], pts[i1], pts[i2], ccw))
                {
                    bool containsOther = false;
                    for (int j = 0; j < count; j++)
                    {
                        int k = indices[j];
                        if (k == i0 || k == i1 || k == i2) continue;
                        // Hole bridges intentionally duplicate their two endpoint
                        // positions. A duplicate is the same polygon vertex for ear
                        // testing and must not falsely block the ear.
                        if ((pts[k] - pts[i0]).sqrMagnitude < 1e-16f ||
                            (pts[k] - pts[i1]).sqrMagnitude < 1e-16f ||
                            (pts[k] - pts[i2]).sqrMagnitude < 1e-16f)
                            continue;
                        if (PointInTriangle(pts[k], pts[i0], pts[i1], pts[i2]))
                        {
                            containsOther = true;
                            break;
                        }
                    }
                    if (!containsOther)
                    {
                        result.Add(i0);
                        result.Add(i1);
                        result.Add(i2);
                        indices.RemoveAt(scan);
                        clipped = true;
                    }
                }
                if (clipped)
                {
                    // Retest the same slot: RemoveAt(scan) already shifted the next
                    // polygon vertex into it, so advancing here skips that vertex
                    // entirely. Every skipped candidate is one ear the pass fails to
                    // clip, and a contour that runs out of retries returns null — the
                    // caller then treats the cap as untriangulable. That is a shared
                    // path: every pattern loses cuts by it, and Radial Glass, which has
                    // no iterative fallback, simply ends up with fewer wedges.
                    rejected = 0;
                    if (scan >= indices.Count) scan = 0;
                    continue;
                }
                // A full cycle without any clipped ear means the polygon cannot be
                // ear-clipped (self-intersecting or degenerate).
                if (++rejected >= indices.Count) return null;
                scan = (scan + 1) % indices.Count;
            }
            if (indices.Count != 3) return null;
            result.Add(indices[0]);
            result.Add(indices[1]);
            result.Add(indices[2]);
            return result;
        }

        /// <summary>
        /// Rescue ladder for loops that defeat plain ear clipping (imperfectly chained or
        /// locally self-intersecting cut contours). First removes local edge crossings the
        /// way earcut cures them, then splits the polygon at an interior diagonal and
        /// triangulates the halves. Returns indices into pts, or null when unrepairable.
        /// </summary>
        static List<int> TriangulateDamagedLoop(Vector2[] pts)
        {
            if (pts.Length < 3) return null;
            var indices = new List<int>(pts.Length);
            for (int i = 0; i < pts.Length; i++) indices.Add(i);

            for (int pass = 0; pass < 4; pass++)
            {
                bool cured = CureLocalIntersections(pts, indices);
                if (indices.Count < 3) return null;
                List<int> triangles = EarClipSubset(pts, indices);
                if (triangles != null) return triangles;
                if (!cured) break;
            }

            int backtracks = 0;
            return SplitAndTriangulate(pts, indices, 0, ref backtracks);
        }

        static List<int> EarClipSubset(Vector2[] pts, List<int> indices)
        {
            if (indices.Count < 3) return null;
            var subset = new Vector2[indices.Count];
            for (int i = 0; i < indices.Count; i++) subset[i] = pts[indices[i]];
            List<int> triangles = EarClip(subset);
            if (triangles == null) return null;
            for (int i = 0; i < triangles.Count; i++)
                triangles[i] = indices[triangles[i]];
            return triangles;
        }

        /// <summary>
        /// Removes pairs of consecutive vertices whose adjacent edges cross each other
        /// (a small local braid in the contour). Dropped cap vertices can leave a small
        /// T-junction against the exterior cut edges; the post-cap fill pass closes it.
        /// </summary>
        static bool CureLocalIntersections(Vector2[] pts, List<int> indices)
        {
            bool removedAny = false;
            bool removed = true;
            int guard = indices.Count;
            while (removed && indices.Count > 3 && guard-- > 0)
            {
                removed = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int n = indices.Count;
                    Vector2 a = pts[indices[(i + n - 1) % n]];
                    Vector2 b = pts[indices[i]];
                    Vector2 c = pts[indices[(i + 1) % n]];
                    Vector2 d = pts[indices[(i + 2) % n]];
                    if (!SegmentsIntersect(a, b, c, d)) continue;
                    int hi = Mathf.Max(i, (i + 1) % n);
                    int lo = Mathf.Min(i, (i + 1) % n);
                    indices.RemoveAt(hi);
                    if (hi != lo) indices.RemoveAt(lo);
                    removed = true;
                    removedAny = true;
                    break;
                }
            }
            return removedAny;
        }

        static List<int> SplitAndTriangulate(Vector2[] pts, List<int> indices, int depth, ref int backtracks)
        {
            int n = indices.Count;
            if (n < 3 || depth > 1000 || backtracks > 100) return null;

            var subset = new Vector2[n];
            for (int i = 0; i < n; i++) subset[i] = pts[indices[i]];

            for (int i = 0; i < n && backtracks <= 100; i++)
            {
                for (int j = i + 2; j < n && backtracks <= 100; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    Vector2 a = subset[i];
                    Vector2 b = subset[j];
                    if ((a - b).sqrMagnitude < 1e-20f) continue;

                    bool blocked = false;
                    for (int e = 0; e < n; e++)
                    {
                        int f = (e + 1) % n;
                        if (e == i || e == j || f == i || f == j) continue;
                        if (SegmentsIntersect(a, b, subset[e], subset[f]))
                        {
                            blocked = true;
                            break;
                        }
                    }
                    if (blocked) continue;
                    Vector2 mid = (a + b) * 0.5f;
                    if (!PointInPolygon(mid, subset)) continue;

                    var first = new List<int>(j - i + 1);
                    for (int k = i; k <= j; k++) first.Add(indices[k]);
                    var second = new List<int>(n - (j - i) + 1);
                    for (int k = j; k != i; k = (k + 1) % n) second.Add(indices[k]);
                    second.Add(indices[i]);

                    List<int> firstTriangles = TriangulateHalf(pts, first, depth, ref backtracks);
                    if (firstTriangles == null)
                    {
                        backtracks++;
                        continue;
                    }
                    List<int> secondTriangles = TriangulateHalf(pts, second, depth, ref backtracks);
                    if (secondTriangles == null)
                    {
                        backtracks++;
                        continue;
                    }
                    firstTriangles.AddRange(secondTriangles);
                    return firstTriangles;
                }
            }
            return null;
        }

        static List<int> TriangulateHalf(Vector2[] pts, List<int> half, int depth, ref int backtracks)
        {
            if (half.Count < 3) return new List<int>();
            List<int> triangles = EarClipSubset(pts, half);
            if (triangles != null) return triangles;
            CureLocalIntersections(pts, half);
            triangles = EarClipSubset(pts, half);
            if (triangles != null) return triangles;
            return SplitAndTriangulate(pts, half, depth + 1, ref backtracks);
        }

        /// <summary>True when every corner of the loop turns the same way, meaning a
        /// centroid fan cannot produce overlapping triangles.</summary>
        static bool IsConvexLoop(Vector2[] pts)
        {
            int n = pts.Length;
            if (n < 3) return false;
            float sign = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                Vector2 c = pts[(i + 2) % n];
                float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if (Mathf.Abs(cross) <= 1e-12f) continue;
                float current = cross > 0f ? 1f : -1f;
                if (sign == 0f) sign = current;
                else if (current != sign) return false;
            }
            return sign != 0f;
        }

        static float SignedArea(Vector2[] pts)
        {
            float a = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 p = pts[i];
                Vector2 q = pts[(i + 1) % pts.Length];
                a += p.x * q.y - q.x * p.y;
            }
            return a * 0.5f;
        }

        static bool IsConvex(Vector2 a, Vector2 b, Vector2 c, bool ccw)
        {
            float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
            return ccw ? cross > 1e-12f : cross < -1e-12f;
        }

        static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2(p, a, b);
            float d2 = Cross2(p, b, c);
            float d3 = Cross2(p, c, a);
            const float epsilon = 1e-10f;
            // Boundary points are allowed: CleanCapLoop already removes collinear
            // vertices, while bridged hole contours necessarily touch at their bridge.
            if (Mathf.Abs(d1) <= epsilon || Mathf.Abs(d2) <= epsilon ||
                Mathf.Abs(d3) <= epsilon)
                return false;
            bool hasNeg = d1 < -epsilon || d2 < -epsilon || d3 < -epsilon;
            bool hasPos = d1 > epsilon || d2 > epsilon || d3 > epsilon;
            return !(hasNeg && hasPos);
        }

        static float Cross2(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        }

        static void EmitCapFan(WorkMesh mesh, List<Vector3> loop,
            List<CutSegment> sourceSegments, Vector3 centroid, Vector3 capNormal,
            Vector3 u, Vector3 v, float invScale, CutSurfaceUVMode uvMode)
        {
            int submesh = mesh.InteriorSubmesh;
            CutPlaneUVBounds uvBounds = (uvMode == CutSurfaceUVMode.FitToCut)
                ? ComputeCutPlaneUVBounds(sourceSegments, u, v)
                : default;
            var points = new Vector2[loop.Count];
            Vector2 centerUV = Vector2.zero;
            for (int i = 0; i < loop.Count; i++)
            {
                points[i] = new Vector2(Vector3.Dot(loop[i], u), Vector3.Dot(loop[i], v));
                centerUV += SampleExtrudedUV(loop[i], sourceSegments, u, v, uvMode, uvBounds);
            }
            centerUV /= Mathf.Max(1, loop.Count);

            int centerIdx = mesh.AddVertex(centroid, capNormal,
                (uvMode == CutSurfaceUVMode.FitToCut || uvMode == CutSurfaceUVMode.EdgeStretch)
                    ? centerUV
                    : new Vector2(Vector3.Dot(centroid, u) * invScale,
                        Vector3.Dot(centroid, v) * invScale));

            var ringIdx = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                ringIdx[i] = mesh.AddVertex(loop[i], capNormal,
                    (uvMode == CutSurfaceUVMode.FitToCut || uvMode == CutSurfaceUVMode.EdgeStretch)
                        ? SampleExtrudedUV(loop[i], sourceSegments, u, v, uvMode, uvBounds)
                        : points[i] * invScale);
            }

            for (int i = 0; i < loop.Count; i++)
            {
                int j = (i + 1) % loop.Count;
                Vector3 a = loop[i] - centroid;
                Vector3 b = loop[j] - centroid;
                // Skip degenerate slivers.
                Vector3 cross = Vector3.Cross(a, b);
                if (cross.sqrMagnitude >= 1e-14f && Vector3.Dot(cross, capNormal) < 0f)
                {
                    mesh.Submeshes[submesh].Add(centerIdx);
                    mesh.Submeshes[submesh].Add(ringIdx[j]);
                    mesh.Submeshes[submesh].Add(ringIdx[i]);
                }
                else
                {
                    mesh.Submeshes[submesh].Add(centerIdx);
                    mesh.Submeshes[submesh].Add(ringIdx[i]);
                    mesh.Submeshes[submesh].Add(ringIdx[j]);
                }
            }
        }

        struct CutPlaneUVBounds
        {
            public float uMin, uMax, vMin, vMax;
            public float pXMin, pXMax, pYMin, pYMax;
            public bool valid;
        }

        static CutPlaneUVBounds ComputeCutPlaneUVBounds(List<CutSegment> segments, Vector3 u, Vector3 v)
        {
            var b = new CutPlaneUVBounds();
            if (segments == null || segments.Count == 0) return b;

            b.uMin = float.MaxValue; b.uMax = float.MinValue;
            b.vMin = float.MaxValue; b.vMax = float.MinValue;
            b.pXMin = float.MaxValue; b.pXMax = float.MinValue;
            b.pYMin = float.MaxValue; b.pYMax = float.MinValue;

            float accumUVDist = 0f;
            float accum3DDist = 0f;

            for (int i = 0; i < segments.Count; i++)
            {
                CutSegment seg = segments[i];

                b.uMin = Mathf.Min(b.uMin, Mathf.Min(seg.UVA.x, seg.UVB.x));
                b.uMax = Mathf.Max(b.uMax, Mathf.Max(seg.UVA.x, seg.UVB.x));
                b.vMin = Mathf.Min(b.vMin, Mathf.Min(seg.UVA.y, seg.UVB.y));
                b.vMax = Mathf.Max(b.vMax, Mathf.Max(seg.UVA.y, seg.UVB.y));

                float segAx = Vector3.Dot(seg.A, u);
                float segAy = Vector3.Dot(seg.A, v);
                float segBx = Vector3.Dot(seg.B, u);
                float segBy = Vector3.Dot(seg.B, v);

                b.pXMin = Mathf.Min(b.pXMin, Mathf.Min(segAx, segBx));
                b.pXMax = Mathf.Max(b.pXMax, Mathf.Max(segAx, segBx));
                b.pYMin = Mathf.Min(b.pYMin, Mathf.Min(segAy, segBy));
                b.pYMax = Mathf.Max(b.pYMax, Mathf.Max(segAy, segBy));

                float dist3D = Vector3.Distance(seg.A, seg.B);
                float distUV = Vector2.Distance(seg.UVA, seg.UVB);

                if (dist3D > 1e-4f && distUV > 1e-5f && (distUV / dist3D) < 15.0f)
                {
                    accumUVDist += distUV;
                    accum3DDist += dist3D;
                }
            }

            float texelDensity = accum3DDist > 1e-4f ? (accumUVDist / accum3DDist) : 1.0f;
            if (texelDensity < 0.05f || texelDensity > 20.0f) texelDensity = 1.0f;

            float pWidth = Mathf.Max(1e-4f, b.pXMax - b.pXMin);
            float pHeight = Mathf.Max(1e-4f, b.pYMax - b.pYMin);

            float targetUSpan = pWidth * texelDensity;
            float targetVSpan = pHeight * texelDensity;

            float uCenter = (b.uMin + b.uMax) * 0.5f;
            float vCenter = (b.vMin + b.vMax) * 0.5f;

            b.uMin = uCenter - targetUSpan * 0.5f;
            b.uMax = uCenter + targetUSpan * 0.5f;

            b.vMin = vCenter - targetVSpan * 0.5f;
            b.vMax = vCenter + targetVSpan * 0.5f;

            b.valid = true;
            return b;
        }

        /// <summary>
        /// Maps UVs for new cut faces smoothly across 2D cut space while bounding them strictly
        /// inside the submesh's UV island [Umin..Umax, Vmin..Vmax]. Performs O(1) evaluation.
        /// </summary>
        static Vector2 SampleExtrudedUV(Vector3 point,
            List<CutSegment> segments, Vector3 u, Vector3 v, CutSurfaceUVMode uvMode, CutPlaneUVBounds b)
        {
            if (segments == null || segments.Count == 0) return Vector2.zero;

            if (uvMode == CutSurfaceUVMode.EdgeStretch)
            {
                float bestEdgeDistance = float.MaxValue;
                Vector2 bestEdgeUV = segments[0].UVA;
                for (int i = 0; i < segments.Count; i++)
                {
                    CutSegment segment = segments[i];
                    Vector3 edge = segment.B - segment.A;
                    float lengthSqr = edge.sqrMagnitude;
                    float t = lengthSqr > 1e-14f
                        ? Mathf.Clamp01(Vector3.Dot(point - segment.A, edge) / lengthSqr)
                        : 0f;
                    Vector3 closest = segment.A + edge * t;
                    float distance = (point - closest).sqrMagnitude;
                    if (distance >= bestEdgeDistance) continue;
                    bestEdgeDistance = distance;
                    bestEdgeUV = Vector2.LerpUnclamped(segment.UVA, segment.UVB, t);
                }
                return bestEdgeUV;
            }

            if (!b.valid) return segments[0].UVA;

            float px = Vector3.Dot(point, u);
            float py = Vector3.Dot(point, v);

            float rangeX = Mathf.Max(1e-5f, b.pXMax - b.pXMin);
            float rangeY = Mathf.Max(1e-5f, b.pYMax - b.pYMin);

            float tx = Mathf.Clamp01((px - b.pXMin) / rangeX);
            float ty = Mathf.Clamp01((py - b.pYMin) / rangeY);

            float finalU = Mathf.Lerp(b.uMin, b.uMax, tx);
            float finalV = Mathf.Lerp(b.vMin, b.vMax, ty);

            return new Vector2(finalU, finalV);
        }

        /// <summary>
        /// Selects one geometrically and UV-continuous source-edge run for an extruded cap.
        /// UV seams and hard corners terminate a run; the longest remaining run drives the
        /// complete generated face. This matches an artist selecting one edge chain before
        /// extruding instead of blending every island around the perimeter.
        /// </summary>
        static List<CutSegment> SelectExtrusionChain(List<Vector3> loop,
            List<CutSegment> sourceSegments)
        {
            if (loop == null || loop.Count < 2 || sourceSegments == null ||
                sourceSegments.Count == 0)
                return sourceSegments;

            Bounds bounds = new Bounds(loop[0], Vector3.zero);
            for (int i = 1; i < loop.Count; i++) bounds.Encapsulate(loop[i]);
            float tolerance = Mathf.Max(bounds.size.magnitude * 1e-5f, 1e-6f);
            float toleranceSqr = tolerance * tolerance;
            const float uvToleranceSqr = 1e-6f;
            const float minimumStraightDot = 0.8f;

            float DistanceToContourSqr(Vector3 point)
            {
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < loop.Count; i++)
                {
                    Vector3 a = loop[i];
                    Vector3 edge = loop[(i + 1) % loop.Count] - a;
                    float lengthSqr = edge.sqrMagnitude;
                    float t = lengthSqr > 1e-14f
                        ? Mathf.Clamp01(Vector3.Dot(point - a, edge) / lengthSqr)
                        : 0f;
                    nearest = Mathf.Min(nearest,
                        (point - (a + edge * t)).sqrMagnitude);
                }
                return nearest;
            }

            var candidates = new List<CutSegment>();
            for (int i = 0; i < sourceSegments.Count; i++)
            {
                CutSegment segment = sourceSegments[i];
                if (DistanceToContourSqr(segment.A) <= toleranceSqr &&
                    DistanceToContourSqr(segment.B) <= toleranceSqr)
                    candidates.Add(segment);
            }
            if (candidates.Count <= 1)
                return candidates.Count == 1 ? candidates : sourceSegments;

            int[] parent = new int[candidates.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int value)
            {
                while (parent[value] != value)
                {
                    parent[value] = parent[parent[value]];
                    value = parent[value];
                }
                return value;
            }
            void Union(int a, int b)
            {
                a = Find(a);
                b = Find(b);
                if (a != b) parent[b] = a;
            }

            bool EndsJoin(CutSegment left, bool leftA,
                CutSegment right, bool rightA)
            {
                Vector3 leftPoint = leftA ? left.A : left.B;
                Vector3 rightPoint = rightA ? right.A : right.B;
                if ((leftPoint - rightPoint).sqrMagnitude > toleranceSqr)
                    return false;
                Vector2 leftUV = leftA ? left.UVA : left.UVB;
                Vector2 rightUV = rightA ? right.UVA : right.UVB;
                if ((leftUV - rightUV).sqrMagnitude > uvToleranceSqr)
                    return false;
                Vector3 leftDirection = (leftA ? left.B - left.A : left.A - left.B).normalized;
                Vector3 rightDirection = (rightA ? right.B - right.A : right.A - right.B).normalized;
                return Mathf.Abs(Vector3.Dot(leftDirection, rightDirection)) >=
                       minimumStraightDot;
            }

            for (int i = 0; i < candidates.Count; i++)
            for (int j = i + 1; j < candidates.Count; j++)
            {
                if (EndsJoin(candidates[i], true, candidates[j], true) ||
                    EndsJoin(candidates[i], true, candidates[j], false) ||
                    EndsJoin(candidates[i], false, candidates[j], true) ||
                    EndsJoin(candidates[i], false, candidates[j], false))
                    Union(i, j);
            }

            var lengths = new Dictionary<int, float>();
            int bestRoot = Find(0);
            float bestLength = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                int root = Find(i);
                float length = (candidates[i].B - candidates[i].A).magnitude;
                lengths.TryGetValue(root, out float total);
                total += length;
                lengths[root] = total;
                if (total > bestLength)
                {
                    bestLength = total;
                    bestRoot = root;
                }
            }

            var selected = new List<CutSegment>();
            for (int i = 0; i < candidates.Count; i++)
                if (Find(i) == bestRoot) selected.Add(candidates[i]);
            return selected.Count > 0 ? selected : sourceSegments;
        }

        /// <summary>Registered segment endpoint: which segment, which end, exact position.</summary>
        struct EndpointRef
        {
            public int Segment;
            public bool IsA;
            public Vector3 Point;
        }

        /// <summary>Chains raw cut segments into closed loops. An almost-closed leftover
        /// chain keeps its real edge order and receives only the missing final edge; sorting
        /// its points would detach the cap perimeter from the fragment and create holes.</summary>
        static List<List<Vector3>> ChainLoops(List<CutSegment> segments)
        {
            var loops = new List<List<Vector3>>();
            if (segments.Count == 0) return loops;

            // Weld tolerance and quantization cell. Using the shortest segment made the
            // tolerance collapse after several cuts: one microscopic sliver could reduce
            // it below floating-point noise and thousands of otherwise matching endpoints
            // stopped chaining. Derive it from the whole cross-section instead.
            Bounds segmentBounds = new Bounds(segments[0].A, Vector3.zero);
            for (int i = 0; i < segments.Count; i++)
            {
                segmentBounds.Encapsulate(segments[i].A);
                segmentBounds.Encapsulate(segments[i].B);
            }
            float cutScale = Mathf.Max(segmentBounds.size.magnitude, 1e-3f);
            float coordinateScale = Mathf.Max(cutScale,
                Mathf.Max(Mathf.Abs(segmentBounds.center.x),
                    Mathf.Max(Mathf.Abs(segmentBounds.center.y),
                        Mathf.Abs(segmentBounds.center.z))));
            float cell = Mathf.Max(cutScale * 5e-5f,
                Mathf.Max(coordinateScale * 1e-6f, 1e-7f));
            float weldSqr = cell * cell;

            Vector3Int Q(Vector3 p) => new Vector3Int(
                Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));

            var used = new bool[segments.Count];

            // Quantized endpoint lookup. Each endpoint registers only under its own cell,
            // but lookups scan the 3x3x3 neighborhood: two points nearer than the weld
            // tolerance still land in different cells when they straddle a cell boundary,
            // so a single-cell exact match mass-produces unchained segments.
            var endpoints = new Dictionary<Vector3Int, List<EndpointRef>>(segments.Count * 2);
            void Register(Vector3Int key, EndpointRef endpoint)
            {
                if (!endpoints.TryGetValue(key, out var list))
                {
                    list = new List<EndpointRef>(2);
                    endpoints[key] = list;
                }
                list.Add(endpoint);
            }
            for (int i = 0; i < segments.Count; i++)
            {
                Register(Q(segments[i].A), new EndpointRef { Segment = i, IsA = true, Point = segments[i].A });
                Register(Q(segments[i].B), new EndpointRef { Segment = i, IsA = false, Point = segments[i].B });
            }

            // Closest unused registered endpoint to p within the weld tolerance, searching
            // the 3x3x3 cells around p's cell and comparing exact distances. Cut contours
            // may cross themselves (self-intersecting solidified shells, X-junctions where
            // several fragments meet), so several endpoints can coincide at one point.
            // Distance alone then picks an arbitrary branch, closes a wrong small loop and
            // strands the rest of the contour. Among coincident matches, prefer the segment
            // that continues the current walk direction most smoothly.
            float exactSqr = cell * cell * 1e-6f;
            EndpointRef? FindNext(Vector3 p, Vector3 walkDirection)
            {
                EndpointRef? best = null;
                float bestSqr = float.PositiveInfinity;
                float bestContinuity = float.NegativeInfinity;
                bool bestExact = false;
                Vector3Int center = Q(p);
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                    if (!endpoints.TryGetValue(key, out var candidates)) continue;
                    for (int c = 0; c < candidates.Count; c++)
                    {
                        EndpointRef candidate = candidates[c];
                        if (used[candidate.Segment]) continue;
                        float distSqr = (candidate.Point - p).sqrMagnitude;
                        if (distSqr > weldSqr) continue;
                        bool exact = distSqr <= exactSqr;
                        CutSegment segment = segments[candidate.Segment];
                        Vector3 farEnd = candidate.IsA ? segment.B : segment.A;
                        float continuity = Vector3.Dot(
                            (farEnd - candidate.Point).normalized, walkDirection);

                        bool better;
                        if (best == null) better = true;
                        else if (exact != bestExact) better = exact;
                        else if (exact) better = continuity > bestContinuity;
                        else if (distSqr < bestSqr * 0.9999f) better = true;
                        else if (distSqr <= bestSqr * 1.0001f)
                            better = continuity > bestContinuity;
                        else better = false;

                        if (better)
                        {
                            best = candidate;
                            bestSqr = distSqr;
                            bestContinuity = continuity;
                            bestExact = exact;
                        }
                    }
                }
                return best;
            }

            for (int start = 0; start < segments.Count; start++)
            {
                if (used[start]) continue;
                var loop = new List<Vector3>();
                used[start] = true;
                Vector3 startPoint = segments[start].A;
                loop.Add(startPoint);
                Vector3 current = segments[start].B;
                Vector3 walkDirection =
                    (segments[start].B - segments[start].A).normalized;
                bool closed = false;
                int chainSegments = 1;

                for (int guard = 0; guard < segments.Count + 2; guard++)
                {
                    // Closure is decided by the weld tolerance, not by a cell-key match.
                    if (loop.Count >= 3 &&
                        (current - startPoint).sqrMagnitude <= weldSqr)
                    { closed = true; break; }
                    loop.Add(current);

                    EndpointRef? next = FindNext(current, walkDirection);
                    if (!next.HasValue) break;

                    used[next.Value.Segment] = true;
                    chainSegments++;
                    Vector3 nextPoint = next.Value.IsA
                        ? segments[next.Value.Segment].B
                        : segments[next.Value.Segment].A;
                    Vector3 stepDirection = (nextPoint - current).normalized;
                    if (stepDirection.sqrMagnitude > 0.5f)
                        walkDirection = stepDirection;
                    current = nextPoint;
                }

                if (closed && loop.Count >= 3)
                {
                    loops.Add(loop);
                    continue;
                }

                // Physically weld the chain ends when they meet within the weld
                // tolerance: the cap ring's closing edge then has zero length instead
                // of spanning a gap the exterior surface does not have, which used to
                // leave a visible slit between the cap and the fragment wall.
                if (loop.Count >= 3 &&
                    (loop[loop.Count - 1] - loop[0]).sqrMagnitude <= weldSqr)
                    loop[loop.Count - 1] = loop[0];

                // Preserve the actual segment walk. EmitCap implicitly connects the last
                // point back to the first, repairing only the unmatched numerical seam.
                // Angular sorting changed the perimeter and left visible gaps between the
                // cap and the exterior triangles on concave barrel fragments.
                s_diagnostics.UnchainedSegmentCount += chainSegments;
                if (loop.Count >= 3)
                    loops.Add(loop);
            }

            return loops;
        }

        /// <summary>Sorts unchained points angularly around their centroid. Returns null
        /// when the points are degenerate (no stable plane).</summary>
        static List<Vector3> AngularSortLoop(List<Vector3> points)
        {
            if (points.Count < 3) return null;

            Vector3 centroid = Vector3.zero;
            foreach (var p in points) centroid += p;
            centroid /= points.Count;

            Vector3 n = Vector3.zero;
            for (int i = 1; i < points.Count - 1; i++)
                n += Vector3.Cross(points[i] - points[0], points[i + 1] - points[0]);
            if (n.sqrMagnitude < 1e-12f) return null;
            n.Normalize();
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(n, u);

            var sorted = new List<Vector3>(points);
            sorted.Sort((a, b) =>
            {
                float angA = Mathf.Atan2(Vector3.Dot(a - centroid, v), Vector3.Dot(a - centroid, u));
                float angB = Mathf.Atan2(Vector3.Dot(b - centroid, v), Vector3.Dot(b - centroid, u));
                return angA.CompareTo(angB);
            });
            return sorted;
        }
    }

    /// <summary>Builds per-fragment renderer slots while preserving source submeshes.</summary>
    public static class FragmentMaterialUtility
    {
        /// <summary>
        /// Returns the material used by the generated cut-surface slot. Fragment meshes
        /// keep that slot last, so scatter can share the exact same Material object instead
        /// of selecting or creating a pipeline-specific substitute.
        /// </summary>
        public static Material GetCutSurfaceMaterial(
            Material[] fragmentMaterials, Material fallback = null)
        {
            if (fragmentMaterials != null && fragmentMaterials.Length > 0)
            {
                Material cut = fragmentMaterials[fragmentMaterials.Length - 1];
                if (cut != null) return cut;

                // Malformed/legacy renderer arrays may have an empty cut slot. Reuse the
                // nearest valid material rather than leaving scatter with a null/black slot.
                for (int i = fragmentMaterials.Length - 2; i >= 0; i--)
                    if (fragmentMaterials[i] != null)
                        return fragmentMaterials[i];
            }
            return fallback;
        }

        public static Material[] BuildForFragment(
            Mesh fragmentMesh, Material[] sourceMaterials,
            Material customInterior, bool useSourceInterior)
        {
            int slotCount = fragmentMesh != null
                ? fragmentMesh.subMeshCount
                : Mathf.Max(1, (sourceMaterials?.Length ?? 0) + 1);
            var materials = new Material[slotCount];
            int exteriorCount = Mathf.Max(0, slotCount - 1);

            for (int i = 0; i < exteriorCount; i++)
                materials[i] = sourceMaterials != null && i < sourceMaterials.Length
                    ? sourceMaterials[i]
                    : null;

            if (slotCount > 0)
            {
                materials[slotCount - 1] = useSourceInterior
                    ? GetDominantSourceMaterial(fragmentMesh, exteriorCount, sourceMaterials)
                    : customInterior;
            }
            return materials;
        }

        /// <summary>
        /// Selects the source material whose exterior triangles cover the greatest area on
        /// this fragment. A roof-heavy piece therefore receives the roof material on its cut
        /// faces, while a wall-heavy piece receives the wall material.
        /// </summary>
        public static Material GetDominantSourceMaterial(
            Mesh mesh, int exteriorCount, Material[] sourceMaterials)
        {
            if (sourceMaterials == null || sourceMaterials.Length == 0)
                return null;

            int fallback = FirstValidMaterial(sourceMaterials, exteriorCount);
            if (mesh == null || exteriorCount <= 0)
                return fallback >= 0 ? sourceMaterials[fallback] : null;

            Vector3[] vertices = mesh.vertices;
            float bestArea = -1f;
            int bestSubmesh = fallback;
            int count = Mathf.Min(exteriorCount, mesh.subMeshCount);
            for (int submesh = 0; submesh < count; submesh++)
            {
                if (submesh >= sourceMaterials.Length || sourceMaterials[submesh] == null)
                    continue;

                int[] triangles = mesh.GetTriangles(submesh);
                float area = 0f;
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];
                    if ((uint)a >= vertices.Length || (uint)b >= vertices.Length ||
                        (uint)c >= vertices.Length)
                        continue;
                    area += Vector3.Cross(vertices[b] - vertices[a],
                        vertices[c] - vertices[a]).magnitude * 0.5f;
                }

                if (area > bestArea)
                {
                    bestArea = area;
                    bestSubmesh = submesh;
                }
            }

            return bestSubmesh >= 0 && bestSubmesh < sourceMaterials.Length
                ? sourceMaterials[bestSubmesh]
                : null;
        }

        static int FirstValidMaterial(Material[] materials, int exteriorCount)
        {
            int count = Mathf.Min(exteriorCount, materials.Length);
            for (int i = 0; i < count; i++)
                if (materials[i] != null) return i;
            return -1;
        }
    }
}
