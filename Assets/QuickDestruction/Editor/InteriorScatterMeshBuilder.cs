using System;
using System.Collections.Generic;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Builds a tiny render-only detail mesh for newly exposed cut faces.  It never
    /// changes the closed fracture mesh: the pieces are closed tetrahedral chips kept
    /// in a child renderer, so collider cooking and cap/topology validation continue
    /// to use the original watertight WorkMesh.
    /// </summary>
    internal static class InteriorScatterMeshBuilder
    {
        const float MinimumStrength = 0.08f;
        static readonly Vector3 OwnershipAxis = new Vector3(0.433f, 0.671f, 0.602f).normalized;

        struct Candidate
        {
            public Vector3 A;
            public Vector3 B;
            public Vector3 C;
            public Vector2 UVA;
            public Vector2 UVB;
            public Vector2 UVC;
            public Vector3 Normal;
            public float Hash;
            public float Area;
            public float PlaneDistance;
        }

        struct StonePlacement
        {
            public Vector3 Center;
            public float Radius;
        }

        internal static Mesh Build(WorkMesh source, DestructionProfile profile,
            string name)
        {
            if (source == null || profile == null) return null;
            float strength = GetStrength(profile);
            if (strength < MinimumStrength) return null;

            // Style decides the shape, not the pattern: an Impact-shattered plank should
            // be able to splinter, and a wooden preset should be able to chip like stone.
            // Auto keeps the historical behaviour of following the pattern.
            bool wood = profile.InteriorScatterStyle == InteriorScatterStyle.Splinters ||
                        (profile.InteriorScatterStyle == InteriorScatterStyle.Auto &&
                         profile.Pattern == FracturePattern.DirectionalWood);
            if (profile.Pattern == FracturePattern.RadialGlass) return null;
            float lengthScale = Mathf.Clamp(profile.InteriorScatterLength, 0.2f, 3f);
            int interior = source.InteriorSubmesh;
            if (interior < 0 || source.Submeshes[interior].Count < 3) return null;

            Bounds bounds = source.ComputeBounds();
            float sourceScale = Mathf.Max(bounds.size.magnitude, 0.001f);
            // Scatter belongs in the crack between two fragments. Its size was bounded only
            // by the fragment's overall diagonal and by the cut triangle it sits on, and
            // neither of those knows how much material stands behind the cut face. On a
            // floor or wall panel the fragment is wide but thin, so a chip scaled to the
            // diagonal ran straight through the visible surface and appeared as blocks
            // standing proud of an intact floor. The thinnest bounding extent is that
            // missing depth: a slab may only host detail shallower than the slab itself.
            List<Plane> boundary = CollectBoundaryPlanes(source, interior);
            // Thickness measured along the fragment's own outer faces instead of along the
            // local axes. Every module of a building baked as one object sits at some angle
            // to those axes, and a slanted wall's axis-aligned box is far thicker than the
            // wall itself - which let chips be born several times deeper than the material
            // actually standing behind them.
            float orientedThickness = MeasureBoundaryThickness(source, boundary);
            float thinnestExtent = orientedThickness > 1e-5f
                ? orientedThickness
                : Mathf.Min(bounds.size.x,
                    Mathf.Min(bounds.size.y, bounds.size.z));
            float depthLimit = thinnestExtent > 1e-5f
                ? thinnestExtent * 0.4f
                : sourceScale;
            // The exact rule: a chip may not cross the fragment's own outer surface. Those
            // exterior faces ARE its boundary, so they are collected once as half-spaces
            // and every chip is clamped to stay behind them. The thickness figure above
            // stays only as the fallback for a fragment made entirely of cut faces, which
            // has no outer surface to measure against.
            float minimumArea = sourceScale * sourceScale * 0.0000015f;
            var candidates = new List<Candidate>();
            List<int> triangles = source.Submeshes[interior];
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int ia = triangles[i];
                int ib = triangles[i + 1];
                int ic = triangles[i + 2];
                if (ia < 0 || ib < 0 || ic < 0 ||
                    ia >= source.Positions.Count || ib >= source.Positions.Count ||
                    ic >= source.Positions.Count)
                    continue;

                Vector3 a = source.Positions[ia];
                Vector3 b = source.Positions[ib];
                Vector3 c = source.Positions[ic];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float twiceArea = cross.magnitude;
                if (twiceArea * 0.5f < minimumArea) continue;
                Vector3 normal = cross / twiceArea;
                if (!OwnsCutFace(normal)) continue;

                Vector3 centroid = (a + b + c) / 3f;
                candidates.Add(new Candidate
                {
                    A = a,
                    B = b,
                    C = c,
                    UVA = source.UVs[ia],
                    UVB = source.UVs[ib],
                    UVC = source.UVs[ic],
                    Normal = normal,
                    Area = twiceArea * 0.5f,
                    Hash = Hash01(centroid, wood ? 19.731f : 43.217f),
                    PlaneDistance = Vector3.Dot(normal, centroid)
                });
            }
            if (candidates.Count == 0) return null;

            candidates.Sort((left, right) => left.Hash.CompareTo(right.Hash));
            // Concrete needs a visibly granular broken surface. Wood stays sparse
            // because its longer splinters cover substantially more area.
            int wanted = wood
                ? Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(3f, 28f,
                    strength * strength)), 3, 28)
                : Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(8f, 96f,
                    strength * strength)), 8, 96);
            int verticesPerDetail = wood ? 12 : 30;
            var vertices = new List<Vector3>(wanted * verticesPerDetail);
            var normals = new List<Vector3>(wanted * verticesPerDetail);
            var uvs = new List<Vector2>(wanted * verticesPerDetail);
            var indices = new List<int>(wanted * verticesPerDetail);
            var accepted = new List<Candidate>(wanted);
            if (wood)
            {
                Vector3 grainAxis = ResolveGrainAxis(source, profile, bounds);
                // One splinter per cut plane was the old rule, so a plank could never
                // show more than a handful no matter how high the slider went. Splinters
                // now cover a break the way real torn fibres do — many per face, kept
                // apart by spacing rather than by a one-per-plane cap.
                var placements = new List<StonePlacement>(wanted);
                float spacing = sourceScale * Mathf.Lerp(0.09f, 0.035f, strength);
                for (int pass = 0; pass < 3 && accepted.Count < wanted; pass++)
                {
                    for (int i = 0; i < candidates.Count && accepted.Count < wanted; i++)
                    {
                        Candidate candidate = candidates[i];
                        // Wood tears where the break runs across the grain; a face lying
                        // along it is a clean side surface. The threshold loosens on
                        // later passes so a plank always ends up with visible splinters.
                        float alignment = Mathf.Abs(
                            Vector3.Dot(candidate.Normal, grainAxis));
                        if (alignment < 0.5f - 0.2f * pass) continue;

                        Vector3 seat = (candidate.A + candidate.B + candidate.C) / 3f;
                        if (TooClose(seat, placements, spacing)) continue;
                        placements.Add(new StonePlacement
                        {
                            Center = seat,
                            Radius = spacing * 0.5f
                        });
                        AddScatterTetrahedron(vertices, normals, uvs, indices, candidate,
                            sourceScale, depthLimit, boundary, strength, true, grainAxis,
                            Mathf.Clamp01(profile.WoodEndSharpness), lengthScale);
                        accepted.Add(candidate);
                    }
                }
            }
            else
            {
                var placements = new List<StonePlacement>(wanted);
                int placed = 0;
                int attempt = 0;
                int maximumAttempts = wanted * 12;
                while (placed < wanted && attempt < maximumAttempts)
                {
                    Candidate candidate = candidates[attempt % candidates.Count];
                    if (AddIrregularStone(vertices, normals, uvs, indices, candidate,
                            sourceScale, depthLimit, boundary, strength, attempt,
                            placements, lengthScale))
                        placed++;
                    attempt++;
                }
            }
            if (indices.Count == 0) return null;

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0, false);
            mesh.RecalculateBounds();
            // Scatter can reuse a source material with a normal map. URP/HDRP Lit need
            // a valid tangent basis for that path; missing tangents can turn the small
            // generated faces black even though their normals and UVs are valid.
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// The fragment's outer surface as outward half-spaces. Scatter lives on the cut
        /// faces and must stay behind these: crossing one puts a chip through the visible
        /// side of a floor or wall instead of inside the crack it belongs to.
        /// </summary>
        static List<Plane> CollectBoundaryPlanes(WorkMesh source, int interiorSubmesh)
        {
            var planes = new List<Plane>();
            for (int submesh = 0; submesh < interiorSubmesh; submesh++)
            {
                List<int> triangles = source.Submeshes[submesh];
                for (int i = 0; i + 2 < triangles.Count; i += 3)
                {
                    int ia = triangles[i];
                    int ib = triangles[i + 1];
                    int ic = triangles[i + 2];
                    if (ia < 0 || ib < 0 || ic < 0 ||
                        ia >= source.Positions.Count || ib >= source.Positions.Count ||
                        ic >= source.Positions.Count)
                        continue;
                    Vector3 a = source.Positions[ia];
                    Vector3 cross = Vector3.Cross(source.Positions[ib] - a,
                        source.Positions[ic] - a);
                    if (cross.sqrMagnitude <= 1e-16f) continue;
                    planes.Add(new Plane(cross.normalized, a));
                }
            }
            return planes;
        }

        /// <summary>
        /// How far a chip may travel from <paramref name="origin"/> along
        /// <paramref name="direction"/> before it would emerge through the outer surface.
        /// Returns <paramref name="fallback"/> when the fragment exposes no outer face —
        /// a piece cut from deep inside a solid has no boundary to respect.
        /// </summary>
        static float MaxTravelInsideBoundary(List<Plane> boundary, Vector3 origin,
            Vector3 direction, float fallback)
        {
            if (boundary == null || boundary.Count == 0) return fallback;
            float longest = fallback;
            for (int i = 0; i < boundary.Count; i++)
            {
                Plane plane = boundary[i];
                float towards = Vector3.Dot(plane.normal, direction);
                if (towards <= 1e-6f) continue;
                float distance = plane.GetDistanceToPoint(origin);
                // A seat already on or outside this face cannot travel towards it at all.
                if (distance >= 0f) return 0f;
                float allowed = -distance / towards;
                if (allowed < longest) longest = allowed;
            }
            // Stop just short so the chip never becomes coplanar with the surface it hides
            // behind, which would z-fight with the intact face.
            return Mathf.Max(0f, longest * 0.9f);
        }

        /// <summary>
        /// Pushes a finished chip vertex back behind the fragment's outer surface. The travel
        /// clamp above bounds one offset from one seat along one direction, which is not the
        /// same as bounding the shape that offset ends up carrying.
        /// </summary>
        static Vector3 ClampInsideBoundary(List<Plane> boundary, Vector3 point,
            Vector3 seat)
        {
            if (boundary == null || boundary.Count == 0) return point;

            // One projection pass is not sufficient at an oblique corner. Correcting the
            // point against a later plane moves it along that plane's normal and can put it
            // back in front of an earlier, non-perpendicular plane. Alternating projections
            // converge on the intersection of the relevant half-spaces; most vertices stop
            // after the second pass, while the small cap prevents pathological meshes from
            // making scatter generation unbounded.
            const int maximumProjectionPasses = 6;
            for (int pass = 0; pass < maximumProjectionPasses; pass++)
            {
                bool corrected = false;
                for (int i = 0; i < boundary.Count; i++)
                {
                    Plane plane = boundary[i];
                    float seatDistance = plane.GetDistanceToPoint(seat);
                    // Only faces the seat itself sits behind describe this chip's surroundings.
                    // A concave fragment has outer faces whose infinite plane runs through solid
                    // material elsewhere, and those must not flatten a chip that never came near
                    // them.
                    if (seatDistance >= 0f) continue;
                    // Keep a tenth of the seat's own clearance in hand, matching the margin the
                    // travel clamp already leaves, so a pushed-back vertex never lands coplanar
                    // with the intact face and z-fights against it.
                    float allowed = seatDistance * 0.1f;
                    float distance = plane.GetDistanceToPoint(point);
                    if (distance <= allowed) continue;
                    point -= plane.normal * (distance - allowed);
                    corrected = true;
                }

                if (!corrected) break;
            }
            return point;
        }

        /// <summary>
        /// The fragment's thinnest extent measured against its own outer faces. Returns 0
        /// when the fragment exposes no outer surface, leaving the caller on its
        /// bounding-box fallback.
        /// </summary>
        static float MeasureBoundaryThickness(WorkMesh source, List<Plane> boundary)
        {
            if (source == null || boundary == null || boundary.Count == 0) return 0f;
            float thinnest = float.MaxValue;
            var measured = new List<Vector3>(6);
            for (int i = 0; i < boundary.Count; i++)
            {
                Vector3 normal = boundary[i].normal;
                bool duplicate = false;
                for (int m = 0; m < measured.Count; m++)
                {
                    if (Vector3.Dot(measured[m], normal) < 0.999f) continue;
                    duplicate = true;
                    break;
                }
                // A slab has a handful of distinct outer normals however many triangles it
                // carries, so deduplicating keeps this to a few passes over the positions.
                if (duplicate) continue;
                measured.Add(normal);
                float deepest = 0f;
                for (int v = 0; v < source.Positions.Count; v++)
                {
                    float depth = -boundary[i].GetDistanceToPoint(source.Positions[v]);
                    if (depth > deepest) deepest = depth;
                }
                if (deepest < thinnest) thinnest = deepest;
            }
            return thinnest < float.MaxValue ? thinnest : 0f;
        }

        static float GetStrength(DestructionProfile profile)
        {
            // Splinters chosen on a pattern that has no splinter slider of its own read
            // the wood one, so the control the user sees is the control that acts.
            if (profile.InteriorScatterStyle == InteriorScatterStyle.Splinters &&
                profile.Pattern != FracturePattern.DirectionalWood)
                return Mathf.Clamp01(profile.WoodSplitIrregularity);

            switch (profile.Pattern)
            {
                case FracturePattern.UniformConcrete:
                case FracturePattern.ImpactShatter:
                    return Mathf.Clamp01(profile.ConcreteIrregularity);
                case FracturePattern.DirectionalWood:
                    return Mathf.Clamp01(profile.WoodSplitIrregularity);
                case FracturePattern.BrickCells:
                    return Mathf.Clamp01(profile.BrickJitter);
                case FracturePattern.LayeredSlabs:
                    return Mathf.Clamp01(profile.SlabJitter);
                case FracturePattern.ColumnarPillars:
                    return Mathf.Clamp01(profile.ColumnJitter);
                default:
                    return 0f;
            }
        }

        // A sliced plane creates opposite normals for its two new caps. Picking one
        // canonical hemisphere means only one of the two matching caps owns scatter,
        // so detail cannot interpenetrate through an intact fracture seam.
        static bool OwnsCutFace(Vector3 normal)
        {
            float dot = Vector3.Dot(normal, OwnershipAxis);
            if (dot > 0.0001f) return true;
            if (dot < -0.0001f) return false;
            if (Mathf.Abs(normal.x) > 0.0001f) return normal.x > 0f;
            if (Mathf.Abs(normal.y) > 0.0001f) return normal.y > 0f;
            return normal.z > 0f;
        }

        static bool TooClose(Vector3 point, List<StonePlacement> placements,
            float spacing)
        {
            float spacingSqr = spacing * spacing;
            for (int i = 0; i < placements.Count; i++)
                if ((placements[i].Center - point).sqrMagnitude < spacingSqr)
                    return true;
            return false;
        }

        static bool SharesCutPlane(Candidate candidate, List<Candidate> accepted,
            float sourceScale)
        {
            float distanceTolerance = Mathf.Max(sourceScale * 0.0005f, 0.00002f);
            for (int i = 0; i < accepted.Count; i++)
            {
                Candidate other = accepted[i];
                if (Vector3.Dot(candidate.Normal, other.Normal) < 0.999f) continue;
                if (Mathf.Abs(candidate.PlaneDistance - other.PlaneDistance) <
                    distanceTolerance)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Grain direction in the fragment's own space: the explicit profile axis when the
        /// user picked one, otherwise the piece's longest side. Splinters follow it, which
        /// is what makes them line up with the piece they tore away from instead of
        /// pointing wherever the cap triangulation happened to run.
        /// </summary>
        static Vector3 ResolveGrainAxis(WorkMesh source, DestructionProfile profile,
            Bounds bounds)
        {
            // Splinters on a non-wood pattern have no grain setting of their own, so they
            // take the pattern's own length axis where one exists and fall back to the
            // piece's longest side. Either way every splinter on a break stays parallel.
            AxisMode axis = profile.Pattern == FracturePattern.DirectionalWood
                ? profile.WoodAxis
                : profile.Pattern == FracturePattern.ColumnarPillars
                    ? profile.PillarAxis
                    : profile.Pattern == FracturePattern.LayeredSlabs
                        ? profile.PreferredCutAxis
                        : AxisMode.Auto;
            switch (axis)
            {
                case AxisMode.X: return Vector3.right;
                case AxisMode.Y: return Vector3.up;
                case AxisMode.Z: return Vector3.forward;
            }
            Vector3 size = bounds.size;
            if (size.x >= size.y && size.x >= size.z) return Vector3.right;
            return size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        static void AddScatterTetrahedron(List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uvs, List<int> indices, Candidate candidate, float sourceScale,
            float depthLimit, List<Plane> boundary, float strength, bool wood,
            Vector3 grainAxis = default,
            float sharpness = 0.6f, float lengthScale = 1f)
        {
            Vector3 center = (candidate.A + candidate.B + candidate.C) / 3f;
            float footprint = wood
                ? Mathf.Lerp(0.20f, 0.38f, strength)
                : Mathf.Lerp(0.28f, 0.52f, strength);
            Vector3 a = Vector3.Lerp(center, candidate.A, footprint);
            Vector3 b = Vector3.Lerp(center, candidate.B, footprint);
            Vector3 c = Vector3.Lerp(center, candidate.C, footprint);
            Vector2 uvA = Vector2.Lerp((candidate.UVA + candidate.UVB + candidate.UVC) / 3f,
                candidate.UVA, footprint);
            Vector2 uvB = Vector2.Lerp((candidate.UVA + candidate.UVB + candidate.UVC) / 3f,
                candidate.UVB, footprint);
            Vector2 uvC = Vector2.Lerp((candidate.UVA + candidate.UVB + candidate.UVC) / 3f,
                candidate.UVC, footprint);

            float longestEdge = Mathf.Max(Vector3.Distance(candidate.A, candidate.B),
                Mathf.Max(Vector3.Distance(candidate.B, candidate.C),
                    Vector3.Distance(candidate.C, candidate.A)));
            float heightLimit = Mathf.Min(sourceScale * (wood ? 0.17f : 0.055f),
                depthLimit);
            float height = Mathf.Min(longestEdge * (wood ? 0.82f : 0.25f), heightLimit) *
                           Mathf.Lerp(0.45f, 1f, strength);
            if (height <= 0.00001f) return;

            Vector3 baseCenter = (a + b + c) / 3f;
            Vector3 tip;
            if (wood)
            {
                // Straight out along the grain, on the side the cap faces, so the
                // splinter points at the piece that separated here and stays parallel to
                // every other splinter on the same break. Only the length varies.
                Vector3 along = grainAxis.sqrMagnitude > 1e-8f
                    ? grainAxis.normalized
                    : candidate.Normal;
                if (Vector3.Dot(along, candidate.Normal) < 0f) along = -along;
                // Splinter End Sharpness stretches the visible splinters as well as the
                // cut ends, so the one slider changes the whole read of a broken end
                // instead of only the plane angle underneath it.
                float reach = Mathf.Lerp(0.75f, 2.6f, sharpness) * lengthScale;
                // Sharpness and Scatter Length multiply the reach by up to about 11, so the
                // boundary has to clamp the finished offset. Clamping `height` alone let a
                // splinter travel far past the surface it was supposed to stay behind.
                float woodOffset = height * reach *
                                   Mathf.Lerp(0.8f, 1.5f, candidate.Hash);
                woodOffset = Mathf.Min(woodOffset, MaxTravelInsideBoundary(
                    boundary, baseCenter, along, woodOffset));
                if (woodOffset <= 0.00001f) return;
                tip = baseCenter + along * woodOffset;
            }
            else
            {
                Vector3 grain = LongestEdgeDirection(
                    candidate.A, candidate.B, candidate.C);
                Vector3 lateral = Vector3.Cross(candidate.Normal, grain).normalized;
                float stoneHeight = Mathf.Min(height, MaxTravelInsideBoundary(
                    boundary, baseCenter, candidate.Normal, height));
                if (stoneHeight <= 0.00001f) return;
                tip = baseCenter + candidate.Normal * stoneHeight +
                      lateral * (stoneHeight * (candidate.Hash - 0.5f) * 0.45f);
            }

            // The barely lifted base avoids coplanar z-fighting with its parent cap.
            float lift = Mathf.Max(sourceScale * 0.00008f, 0.000002f);
            a += candidate.Normal * lift;
            b += candidate.Normal * lift;
            c += candidate.Normal * lift;
            // Lifting the base and displacing the tip happen after the scalar travel clamp.
            // Clamp the complete tetrahedron so wood splinters and the fallback stone path
            // obey the same outer boundary guarantee as the irregular concrete stones.
            a = ClampInsideBoundary(boundary, a, baseCenter);
            b = ClampInsideBoundary(boundary, b, baseCenter);
            c = ClampInsideBoundary(boundary, c, baseCenter);
            tip = ClampInsideBoundary(boundary, tip, baseCenter);
            Vector3 tetraCenter = (a + b + c + tip) * 0.25f;
            AddOutwardTriangle(vertices, normals, uvs, indices,
                a, uvA, c, uvC, b, uvB, tetraCenter);
            AddTexelMatchedTriangle(vertices, normals, uvs, indices,
                a, b, tip, candidate, tetraCenter);
            AddTexelMatchedTriangle(vertices, normals, uvs, indices,
                b, c, tip, candidate, tetraCenter);
            AddTexelMatchedTriangle(vertices, normals, uvs, indices,
                c, a, tip, candidate, tetraCenter);
        }

        static bool AddIrregularStone(List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uvs, List<int> indices, Candidate candidate, float sourceScale,
            float depthLimit, List<Plane> boundary, float strength, int serial,
            List<StonePlacement> placements, float lengthScale = 1f)
        {
            float h0 = Hash01(candidate.A + Vector3.one * (serial * 0.173f), 11.37f);
            float h1 = Hash01(candidate.B + Vector3.one * (serial * 0.291f), 29.91f);
            float h2 = Hash01(candidate.C + Vector3.one * (serial * 0.419f), 53.17f);

            // Folded barycentric coordinates guarantee that every stone starts
            // inside its source cap triangle.
            float u = h0;
            float v = h1;
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }
            Vector3 center = candidate.A + (candidate.B - candidate.A) * u +
                             (candidate.C - candidate.A) * v;
            Vector2 uvCenter = candidate.UVA + (candidate.UVB - candidate.UVA) * u +
                               (candidate.UVC - candidate.UVA) * v;

            Vector3 tangent = LongestEdgeDirection(candidate.A, candidate.B, candidate.C);
            tangent = Vector3.ProjectOnPlane(tangent, candidate.Normal).normalized;
            if (tangent.sqrMagnitude < 0.5f)
                tangent = Vector3.Cross(candidate.Normal, Vector3.up).normalized;
            if (tangent.sqrMagnitude < 0.5f)
                tangent = Vector3.Cross(candidate.Normal, Vector3.right).normalized;
            Vector3 bitangent = Vector3.Cross(candidate.Normal, tangent).normalized;

            float triangleScale = Mathf.Sqrt(Mathf.Max(candidate.Area, 0.0000001f));
            // Broad footprints deliberately overlap a little at higher strengths.
            // The scatter should visually replace most of the perfectly flat cap,
            // rather than read as isolated pebbles sitting on it.
            float highStrength = strength * strength;
            float radius = Mathf.Min(triangleScale * Mathf.Lerp(0.42f, 1.1f,
                    highStrength),
                sourceScale * Mathf.Lerp(0.08f, 0.26f, highStrength));
            radius *= Mathf.Lerp(0.82f, 1.42f, h2);

            // Cap triangulation produces long sliver triangles, and the footprint clamp
            // below squashes any stone born on one into a thin ridge running the whole
            // face — the source of stripe-shaped "stones". The inradius says how fat a
            // shape the triangle can actually host; too skinny hosts nothing.
            float perimeter = Vector3.Distance(candidate.A, candidate.B) +
                              Vector3.Distance(candidate.B, candidate.C) +
                              Vector3.Distance(candidate.C, candidate.A);
            float inradius = perimeter > 1e-6f
                ? 2f * candidate.Area / perimeter
                : 0f;
            if (inradius < sourceScale * 0.006f) return false;
            radius = Mathf.Min(radius, inradius * 1.8f);

            // Scatter Length raises how far a chip stands off the cut face. The footprint
            // stays clamped to its triangle, so longer never means wider or overhanging.
            // Same rule as the splinter path: a chip may not stand further off the cut
            // face than the material behind it is thick, and never past the fragment's own
            // outer surface, or it emerges through the visible side of the panel.
            // Measured from where the chip actually sits rather than from the triangle
            // centroid: the two are up to a triangle apart, and against an oblique outer
            // face that whole distance is depth the clamp never accounted for.
            float height = Mathf.Min(radius * Mathf.Lerp(0.32f, 0.72f, h0) * lengthScale,
                depthLimit);
            height = Mathf.Min(height, MaxTravelInsideBoundary(
                boundary, center, candidate.Normal, height));
            if (height <= 0.00001f) return false;
            if (radius <= 0.00001f) return false;

            for (int i = 0; i < placements.Count; i++)
            {
                StonePlacement other = placements[i];
                float minimumDistance = (radius + other.Radius) * 0.94f;
                if ((center - other.Center).sqrMagnitude <
                    minimumDistance * minimumDistance)
                    return false;
            }
            placements.Add(new StonePlacement { Center = center, Radius = radius });

            const int ringCount = 6;
            var bottomRing = new Vector3[ringCount];
            var topRing = new Vector3[ringCount];
            var bottomUVs = new Vector2[ringCount];
            var topUVs = new Vector2[ringCount];
            float lift = Mathf.Max(sourceScale * 0.0001f, 0.000002f);
            for (int i = 0; i < ringCount; i++)
            {
                float jitter = Hash01(center + Vector3.one * (serial * 7 + i), 71.3f);
                float angle = (i + jitter * 0.30f) * Mathf.PI * 2f / ringCount;
                float ringRadius = radius * Mathf.Lerp(0.62f, 1.30f, jitter);
                Vector3 planar = center + tangent * (Mathf.Cos(angle) * ringRadius) +
                                 bitangent * (Mathf.Sin(angle) * ringRadius);
                // Clamp every footprint point to its cap triangle. Large stones can
                // fill the wall right up to an edge without hanging into empty space.
                planar = ClosestPointOnTriangle(planar,
                    candidate.A, candidate.B, candidate.C);
                bottomRing[i] = planar + candidate.Normal * lift;
                bottomUVs[i] = MapPointToTriangleUV(planar, candidate);

                float topHash = Hash01(center + Vector3.one * (serial * 13 + i), 97.1f);
                float topRadius = ringRadius * Mathf.Lerp(0.28f, 0.58f, topHash);
                float topHeight = height * Mathf.Lerp(0.68f, 1.20f, topHash);
                topRing[i] = center + tangent * (Mathf.Cos(angle) * topRadius) +
                             bitangent * (Mathf.Sin(angle) * topRadius) +
                             candidate.Normal * topHeight;
            }

            Vector3 lateral = tangent * ((h1 - 0.5f) * radius * 0.25f) +
                              bitangent * ((h2 - 0.5f) * radius * 0.25f);
            for (int i = 0; i < ringCount; i++)
            {
                Vector3 shifted = topRing[i] + lateral;
                float heightAboveCap = Vector3.Dot(candidate.Normal, shifted) -
                                       candidate.PlaneDistance;
                Vector3 planar = shifted - candidate.Normal * heightAboveCap;
                topRing[i] = ClosestPointOnTriangle(planar,
                    candidate.A, candidate.B, candidate.C) +
                    candidate.Normal * heightAboveCap;
                topUVs[i] = MapPointToTriangleUV(
                    topRing[i] - candidate.Normal * heightAboveCap, candidate);
            }

            Vector3 bottomCenter = center + candidate.Normal * lift;
            Vector3 topCenter = center + candidate.Normal * height + lateral;
            // The travel above is one scalar, from one seat, along one direction. The
            // finished chip also spreads by `radius` sideways, overshoots that offset by up
            // to 1.2x on the top ring, and is displaced again by `lateral` after the clamp.
            // None of that changes the distance that was measured, yet all of it moves a
            // vertex towards an oblique outer face. Square to the fragment's own axes that
            // face is near perpendicular to the cut normal and the error stays invisible;
            // on a slanted wall it is precisely the gap the chips were pushing through.
            for (int i = 0; i < ringCount; i++)
            {
                bottomRing[i] = ClampInsideBoundary(boundary, bottomRing[i], center);
                topRing[i] = ClampInsideBoundary(boundary, topRing[i], center);
            }
            bottomCenter = ClampInsideBoundary(boundary, bottomCenter, center);
            topCenter = ClampInsideBoundary(boundary, topCenter, center);
            Vector3 solidCenter = (bottomCenter + topCenter) * 0.5f;

            // Closed, irregular truncated rock. The uneven top ring forms a broken
            // ridge/plateau instead of the single sharp point used by wood splinters.
            for (int i = 0; i < ringCount; i++)
            {
                int next = (i + 1) % ringCount;
                AddStoneSideTriangle(vertices, normals, uvs, indices,
                    bottomRing[i], bottomRing[next], topRing[next],
                    candidate, solidCenter);
                AddStoneSideTriangle(vertices, normals, uvs, indices,
                    bottomRing[i], topRing[next], topRing[i],
                    candidate, solidCenter);
                AddOutwardTriangle(vertices, normals, uvs, indices,
                    bottomCenter, uvCenter, bottomRing[next], bottomUVs[next],
                    bottomRing[i], bottomUVs[i], solidCenter);
                AddOutwardTriangle(vertices, normals, uvs, indices,
                    topCenter, uvCenter, topRing[i], topUVs[i],
                    topRing[next], topUVs[next], solidCenter);
            }
            return true;
        }

        static void AddStoneSideTriangle(List<Vector3> vertices,
            List<Vector3> normals, List<Vector2> uvs, List<int> indices,
            Vector3 a, Vector3 b, Vector3 c, Candidate candidate,
            Vector3 solidCenter)
        {
            AddTexelMatchedTriangle(vertices, normals, uvs, indices,
                a, b, c, candidate, solidCenter);
        }

        static void AddTexelMatchedTriangle(List<Vector3> vertices,
            List<Vector3> normals, List<Vector2> uvs, List<int> indices,
            Vector3 a, Vector3 b, Vector3 c, Candidate candidate,
            Vector3 solidCenter)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            float abLength = ab.magnitude;
            float faceDoubleArea = Vector3.Cross(ab, ac).magnitude;
            float parentDoubleArea = Mathf.Max(candidate.Area * 2f, 1e-10f);
            float parentUVDoubleArea = Mathf.Abs(
                (candidate.UVB.x - candidate.UVA.x) *
                (candidate.UVC.y - candidate.UVA.y) -
                (candidate.UVB.y - candidate.UVA.y) *
                (candidate.UVC.x - candidate.UVA.x));
            if (abLength <= 1e-7f || faceDoubleArea <= 1e-10f ||
                parentUVDoubleArea <= 1e-12f)
            {
                Vector2 center = (candidate.UVA + candidate.UVB +
                                  candidate.UVC) / 3f;
                AddOutwardTriangle(vertices, normals, uvs, indices,
                    a, center, b, center + Vector2.right * 0.0001f,
                    c, center + Vector2.up * 0.0001f, solidCenter);
                return;
            }

            float texelDensity = Mathf.Sqrt(parentUVDoubleArea /
                                             parentDoubleArea);
            Vector3 abDirection = ab / abLength;
            float cAlong = Vector3.Dot(ac, abDirection);
            float cHeight = Mathf.Sqrt(Mathf.Max(0f,
                ac.sqrMagnitude - cAlong * cAlong));
            Vector2 localA = Vector2.zero;
            Vector2 localB = new Vector2(abLength * texelDensity, 0f);
            Vector2 localC = new Vector2(cAlong * texelDensity,
                cHeight * texelDensity);
            Vector2 localCenter = (localA + localB + localC) / 3f;
            localA -= localCenter;
            localB -= localCenter;
            localC -= localCenter;

            Vector2 islandAxis = LongestUVEdge(candidate);
            Vector2 islandPerpendicular = new Vector2(-islandAxis.y,
                islandAxis.x);
            Vector2 islandCenter = (candidate.UVA + candidate.UVB +
                                    candidate.UVC) / 3f;
            Vector2 uvA = islandCenter;
            Vector2 uvB = islandCenter;
            Vector2 uvC = islandCenter;
            float fit = 1f;
            for (int attempt = 0; attempt < 14; attempt++)
            {
                uvA = islandCenter + islandAxis * (localA.x * fit) +
                      islandPerpendicular * (localA.y * fit);
                uvB = islandCenter + islandAxis * (localB.x * fit) +
                      islandPerpendicular * (localB.y * fit);
                uvC = islandCenter + islandAxis * (localC.x * fit) +
                      islandPerpendicular * (localC.y * fit);
                if (IsInsideUVTriangle(uvA, candidate) &&
                    IsInsideUVTriangle(uvB, candidate) &&
                    IsInsideUVTriangle(uvC, candidate))
                    break;
                fit *= 0.78f;
            }

            AddOutwardTriangle(vertices, normals, uvs, indices,
                a, uvA, b, uvB, c, uvC, solidCenter);
        }

        static Vector2 LongestUVEdge(Candidate candidate)
        {
            Vector2 ab = candidate.UVB - candidate.UVA;
            Vector2 bc = candidate.UVC - candidate.UVB;
            Vector2 ca = candidate.UVA - candidate.UVC;
            Vector2 longest = ab.sqrMagnitude >= bc.sqrMagnitude &&
                              ab.sqrMagnitude >= ca.sqrMagnitude
                ? ab : bc.sqrMagnitude >= ca.sqrMagnitude ? bc : ca;
            return longest.sqrMagnitude > 1e-12f
                ? longest.normalized : Vector2.right;
        }

        static bool IsInsideUVTriangle(Vector2 point, Candidate candidate)
        {
            Vector2 v0 = candidate.UVB - candidate.UVA;
            Vector2 v1 = candidate.UVC - candidate.UVA;
            Vector2 v2 = point - candidate.UVA;
            float denominator = v0.x * v1.y - v0.y * v1.x;
            if (Mathf.Abs(denominator) < 1e-12f) return false;
            float v = (v2.x * v1.y - v2.y * v1.x) / denominator;
            float w = (v0.x * v2.y - v0.y * v2.x) / denominator;
            float u = 1f - v - w;
            const float epsilon = -0.00001f;
            return u >= epsilon && v >= epsilon && w >= epsilon;
        }

        static Vector2 MapPointToTriangleUV(Vector3 point, Candidate candidate)
        {
            Vector3 v0 = candidate.B - candidate.A;
            Vector3 v1 = candidate.C - candidate.A;
            Vector3 v2 = point - candidate.A;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denominator = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denominator) < 1e-12f)
                return (candidate.UVA + candidate.UVB + candidate.UVC) / 3f;

            float v = (d11 * d20 - d01 * d21) / denominator;
            float w = (d00 * d21 - d01 * d20) / denominator;
            float u = 1f - v - w;

            // Numerical noise around a triangle edge must never sample a neighbour
            // atlas island. Renormalized non-negative barycentrics guarantee that
            // every generated UV remains inside the exact parent UV triangle.
            u = Mathf.Max(0f, u);
            v = Mathf.Max(0f, v);
            w = Mathf.Max(0f, w);
            float sum = u + v + w;
            if (sum <= 0.000001f)
                return (candidate.UVA + candidate.UVB + candidate.UVC) / 3f;
            u /= sum;
            v /= sum;
            w /= sum;
            return candidate.UVA * u + candidate.UVB * v + candidate.UVC * w;
        }

        static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a,
            Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
                return a + ab * (d1 / (d1 - d3));

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
                return a + ac * (d2 / (d2 - d6));

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                return b + (c - b) * ((d4 - d3) /
                    ((d4 - d3) + (d5 - d6)));

            float denominator = 1f / (va + vb + vc);
            float v = vb * denominator;
            float w = vc * denominator;
            return a + ab * v + ac * w;
        }

        static Vector3 LongestEdgeDirection(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 bc = c - b;
            Vector3 ca = a - c;
            Vector3 longest = ab.sqrMagnitude >= bc.sqrMagnitude &&
                              ab.sqrMagnitude >= ca.sqrMagnitude
                ? ab : bc.sqrMagnitude >= ca.sqrMagnitude ? bc : ca;
            return longest.sqrMagnitude > 1e-12f ? longest.normalized : Vector3.right;
        }

        static void AddOutwardTriangle(List<Vector3> vertices, List<Vector3> normals,
            List<Vector2> uvs, List<int> indices, Vector3 a, Vector2 uvA,
            Vector3 b, Vector2 uvB, Vector3 c, Vector2 uvC, Vector3 tetraCenter)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-16f) return;
            if (Vector3.Dot(normal, a - tetraCenter) < 0f)
            {
                Vector3 point = b;
                b = c;
                c = point;
                Vector2 uv = uvB;
                uvB = uvC;
                uvC = uv;
                normal = -normal;
            }
            normal.Normalize();
            int index = vertices.Count;
            vertices.Add(a); normals.Add(normal); uvs.Add(uvA);
            vertices.Add(b); normals.Add(normal); uvs.Add(uvB);
            vertices.Add(c); normals.Add(normal); uvs.Add(uvC);
            indices.Add(index);
            indices.Add(index + 1);
            indices.Add(index + 2);
        }

        static float Hash01(Vector3 value, float salt)
        {
            float hash = Mathf.Sin(Vector3.Dot(value,
                new Vector3(12.9898f + salt, 78.233f, 37.719f))) * 43758.5453f;
            return hash - Mathf.Floor(hash);
        }
    }
}
