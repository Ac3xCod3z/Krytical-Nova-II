using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Gives a worn part the motion of the bare body under it for the body keys the part itself lacks: each vertex
    /// takes the key's delta at the nearest point of the bare body's surface, interpolated across the triangle it
    /// lands on. The same transfer the Blender session uses (`mannequin_gear.py`, `mannequin_clip.py`), done at merge
    /// time because Synty's own parts stay untouched:
    ///  - gear on the body (torso, arms, hips, legs, hands, feet, the chest wrap) takes kn_bust, kn_butt and kn_defined;
    ///  - hair and facial hair take the body keys but masculineFeminine, Synty's heavy, skinny and buff included (Synty
    ///    ships them empty or weak; the hair is modelled for either head, so the feminine key stays its own, as in Blender);
    ///  - attachments keep riding their bones and take nothing.
    /// A part keeps a key of its own only when it moves at least half as far as the body under it would move it.
    /// Positions are compared at rest (no key applied, all parts share the
    /// Sidekick base model's space), so a result depends only on the two meshes and is worked out once and kept.
    /// </summary>
    public static class BodyKeyTransfer
    {
        /// <summary>The three body sliders the Blender session added (22 Oct 2026).</summary>
        public static readonly string[] SliderKeys = { "kn_bust", "kn_butt", "kn_defined" };
        /// <summary>Every body key: Synty's four and the three sliders.</summary>
        public static readonly string[] BodyKeys =
            { "masculineFeminine", "defaultSkinny", "defaultHeavy", "defaultBuff", "kn_bust", "kn_butt", "kn_defined" };
        /// <summary>What hair and facial hair take from the body: every body key but the feminine one.</summary>
        public static readonly string[] HairKeys = { "defaultSkinny", "defaultHeavy", "defaultBuff", "kn_bust", "kn_butt", "kn_defined" };

        // A key whose largest move is under this share of the body's height counts as empty (about 2 mm on 1.75 m).
        private const float EmptyShare = 0.0012f;
        // A part's own key is kept when it moves at least this share of what the body under it would move it.
        private const float OwnKeyShare = 0.5f;
        private const int GridCells = 24;
        private const int MaxCached = 32;

        /// <summary>The body keys a part of <paramref name="region"/> takes from the body under it, or null.</summary>
        public static string[] KeysFor(BodyRegion region)
        {
            if (region == BodyRegion.Hair || region == BodyRegion.FacialHair) return HairKeys;
            if (region == BodyRegion.Wrap || EquipSlots.IsBodyRegion(region)) return SliderKeys;
            return null;
        }

        // ---------------- The bare body ----------------

        /// <summary>The bare body (and head) as one surface: rest positions, triangles, a grid, and each key's full delta.</summary>
        public sealed class Source
        {
            internal readonly Mesh[] meshes;
            internal readonly Vector3[] verts;
            internal readonly int[] tris;
            internal readonly Dictionary<string, Vector3[]> deltas = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            internal readonly HashSet<Mesh> meshSet = new HashSet<Mesh>();
            internal readonly float emptyBelow;
            // Uniform grid: the triangles overlapping each cell.
            private readonly Vector3 origin;
            private readonly float cell;
            private readonly int nx, ny, nz;
            private readonly int[] cellStart, cellTris;
            private readonly int[] seen;
            private int stamp;

            internal Source(Mesh[] meshes, IReadOnlyList<SidekickPartRef> parts)
            {
                this.meshes = meshes;
                var v = new List<Vector3>(8192);
                var t = new List<int>(24576);
                var buffer = new List<Vector3>(4096);
                var indices = new List<int>(8192);
                foreach (var part in parts)
                {
                    var m = part.mesh;
                    if (m == null || !m.isReadable) continue;
                    meshSet.Add(m);
                    int baseIndex = v.Count, vc = m.vertexCount;
                    m.GetVertices(buffer);
                    v.AddRange(buffer);
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        m.GetIndices(indices, s);
                        var topology = m.GetTopology(s);
                        if (topology == MeshTopology.Triangles)
                            for (int i = 0; i + 2 < indices.Count; i += 3)
                            { t.Add(indices[i] + baseIndex); t.Add(indices[i + 1] + baseIndex); t.Add(indices[i + 2] + baseIndex); }
                        else if (topology == MeshTopology.Quads)
                            for (int i = 0; i + 3 < indices.Count; i += 4)
                            {
                                int a = indices[i] + baseIndex, b = indices[i + 1] + baseIndex, c = indices[i + 2] + baseIndex, d = indices[i + 3] + baseIndex;
                                t.Add(a); t.Add(b); t.Add(c); t.Add(a); t.Add(c); t.Add(d);
                            }
                    }
                    foreach (string key in BodyKeys)
                    {
                        if (!TryFullDelta(m, key, out var delta)) continue;
                        // Grown part by part: earlier parts without the key stay zero.
                        if (!deltas.TryGetValue(key, out var all)) all = Array.Empty<Vector3>();
                        if (all.Length < baseIndex + vc) Array.Resize(ref all, baseIndex + vc);
                        Array.Copy(delta, 0, all, baseIndex, vc);
                        deltas[key] = all;
                    }
                }
                verts = v.ToArray();
                tris = t.ToArray();
                foreach (string key in BodyKeys)
                    if (deltas.TryGetValue(key, out var all) && all.Length < verts.Length)
                    {
                        Array.Resize(ref all, verts.Length);
                        deltas[key] = all;
                    }

                var bounds = new Bounds(verts.Length > 0 ? verts[0] : Vector3.zero, Vector3.zero);
                foreach (var p in verts) bounds.Encapsulate(p);
                emptyBelow = Mathf.Max(bounds.size.y, 0.0001f) * EmptyShare;
                // A key the whole body does not move is no key at all.
                var empty = new List<string>();
                foreach (var kv in deltas) if (MaxMove(kv.Value) < emptyBelow) empty.Add(kv.Key);
                foreach (string key in empty) deltas.Remove(key);

                cell = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.y), Mathf.Max(bounds.size.z, 0.0001f)) / GridCells;
                origin = bounds.min;
                nx = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / cell));
                ny = Mathf.Max(1, Mathf.CeilToInt(bounds.size.y / cell));
                nz = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / cell));
                int triCount = tris.Length / 3;
                seen = new int[triCount];
                var counts = new int[nx * ny * nz + 1];
                for (int pass = 0; pass < 2; pass++)
                {
                    if (pass == 1)
                    {
                        cellStart = new int[counts.Length];
                        for (int c = 1; c < counts.Length; c++) cellStart[c] = cellStart[c - 1] + counts[c - 1];
                        cellTris = new int[cellStart[counts.Length - 1] + counts[counts.Length - 1]];
                        Array.Clear(counts, 0, counts.Length);
                    }
                    for (int tri = 0; tri < triCount; tri++)
                    {
                        Vector3 a = verts[tris[tri * 3]], b = verts[tris[tri * 3 + 1]], c = verts[tris[tri * 3 + 2]];
                        CellOf(Vector3.Min(a, Vector3.Min(b, c)), out int x0, out int y0, out int z0);
                        CellOf(Vector3.Max(a, Vector3.Max(b, c)), out int x1, out int y1, out int z1);
                        for (int z = z0; z <= z1; z++)
                        for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            int index = (z * ny + y) * nx + x;
                            if (pass == 1) cellTris[cellStart[index] + counts[index]] = tri;
                            counts[index]++;
                        }
                    }
                }
            }

            public bool HasKey(string key) => deltas.ContainsKey(key);
            public bool IsEmpty => tris.Length == 0;

            private void CellOf(Vector3 p, out int x, out int y, out int z)
            {
                x = Mathf.Clamp((int)((p.x - origin.x) / cell), 0, nx - 1);
                y = Mathf.Clamp((int)((p.y - origin.y) / cell), 0, ny - 1);
                z = Mathf.Clamp((int)((p.z - origin.z) / cell), 0, nz - 1);
            }

            /// <summary>The triangle of the surface nearest <paramref name="p"/> and where on it, as barycentric weights.</summary>
            internal void Nearest(Vector3 p, out int tri, out Vector3 weights)
            {
                tri = -1;
                weights = default;
                float best = float.MaxValue;
                if (++stamp == int.MaxValue) { Array.Clear(seen, 0, seen.Length); stamp = 1; }
                CellOf(p, out int cx, out int cy, out int cz);
                int maxRing = Mathf.Max(nx, Mathf.Max(ny, nz));
                for (int r = 0; r <= maxRing; r++)
                {
                    for (int z = cz - r; z <= cz + r; z++)
                    for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                    {
                        if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) continue;
                        // Only the shell of this ring: the inside was searched by the smaller rings.
                        if (Math.Max(Math.Abs(x - cx), Math.Max(Math.Abs(y - cy), Math.Abs(z - cz))) != r) continue;
                        int index = (z * ny + y) * nx + x;
                        for (int i = cellStart[index], end = cellStart[index + 1]; i < end; i++)
                        {
                            int candidate = cellTris[i];
                            if (seen[candidate] == stamp) continue;
                            seen[candidate] = stamp;
                            Vector3 a = verts[tris[candidate * 3]], b = verts[tris[candidate * 3 + 1]], c = verts[tris[candidate * 3 + 2]];
                            Vector3 bary = ClosestOnTriangle(p, a, b, c);
                            Vector3 q = a * bary.x + b * bary.y + c * bary.z;
                            float d = (q - p).sqrMagnitude;
                            if (d < best) { best = d; tri = candidate; weights = bary; }
                        }
                    }
                    // Every cell of the next ring lies at least r cells away from p.
                    if (tri >= 0 && Mathf.Sqrt(best) <= r * cell) return;
                }
            }

            internal Vector3 Delta(string key, int tri, Vector3 w)
            {
                var d = deltas[key];
                return d[tris[tri * 3]] * w.x + d[tris[tri * 3 + 1]] * w.y + d[tris[tri * 3 + 2]] * w.z;
            }
        }

        // ---------------- One part on one body ----------------

        private sealed class Fit
        {
            public int[] tri;
            public Vector3[] weights;
            public readonly Dictionary<string, Vector3[]> deltas = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            public readonly Dictionary<string, bool> ownKey = new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        private static readonly List<Source> sources = new List<Source>();
        private static readonly Dictionary<(Source, Mesh), Fit> fits = new Dictionary<(Source, Mesh), Fit>();
        private static readonly List<Mesh> lookup = new List<Mesh>(16);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            sources.Clear();
            fits.Clear();
        }

        /// <summary>The bare body made of <paramref name="parts"/>, built once per set of meshes; null when there is none.</summary>
        public static Source SourceFor(IReadOnlyList<SidekickPartRef> parts)
        {
            if (parts == null || parts.Count == 0) return null;
            lookup.Clear();
            foreach (var part in parts)
                if (part.mesh != null) lookup.Add(part.mesh);
            if (lookup.Count == 0) return null;
            foreach (var known in sources)
            {
                if (known.meshes.Length != lookup.Count) continue;
                bool same = true;
                for (int i = 0; i < lookup.Count && same; i++) same = known.meshes[i] == lookup[i];
                if (!same) continue;
                // Most recently used last.
                sources.Remove(known);
                sources.Add(known);
                return known;
            }
            if (sources.Count >= MaxCached) Evict(sources[0]); // the one used longest ago
            var source = new Source(lookup.ToArray(), parts);
            sources.Add(source);
            return source;
        }

        /// <summary>
        /// Adds to <paramref name="positions"/> (the part's vertices, keys already baked) the motion of every body key
        /// the part takes from <paramref name="source"/> and does not have itself, at the weight <paramref name="shape"/>
        /// gives it. A part's own key that this replaces is left out of the bake (<see cref="KeepsOwnKey"/>).
        /// </summary>
        public static void Apply(Source source, in SidekickPartRef part, in BodyShapeWeights shape, List<Vector3> positions)
        {
            if (source == null || source.IsEmpty || part.mesh == null) return;
            var keys = KeysFor(part.region);
            if (keys == null || source.meshSet.Contains(part.mesh)) return;
            Fit fit = null;
            foreach (string key in keys)
            {
                float weight = shape.WeightFor(key);
                if (weight <= 0f || !source.HasKey(key)) continue;
                fit ??= FitFor(source, part.mesh);
                if (OwnsKey(fit, source, part.mesh, key)) continue;
                var delta = Transferred(fit, source, key);
                float scale = weight * 0.01f;
                int count = Mathf.Min(delta.Length, positions.Count);
                for (int i = 0; i < count; i++) positions[i] += delta[i] * scale;
            }
        }

        /// <summary>
        /// True when the part brings a key of its own for <paramref name="blendShapeName"/> and so keeps it; false when
        /// the transfer replaces it (the key is empty or nearly so on a part that takes it from the body).
        /// </summary>
        public static bool KeepsOwnKey(Source source, in SidekickPartRef part, string blendShapeName)
        {
            if (source == null || source.IsEmpty || part.mesh == null) return true;
            var keys = KeysFor(part.region);
            if (keys == null || source.meshSet.Contains(part.mesh)) return true;
            foreach (string key in keys)
            {
                if (!blendShapeName.Contains(key)) continue;
                if (!source.HasKey(key)) return true;
                return OwnsKey(FitFor(source, part.mesh), source, part.mesh, key);
            }
            return true;
        }

        private static readonly List<(Source, Mesh)> evicted = new List<(Source, Mesh)>();

        private static void Evict(Source source)
        {
            sources.Remove(source);
            evicted.Clear();
            foreach (var key in fits.Keys) if (key.Item1 == source) evicted.Add(key);
            foreach (var key in evicted) fits.Remove(key);
        }

        private static bool OwnsKey(Fit fit, Source source, Mesh mesh, string key)
        {
            if (fit.ownKey.TryGetValue(key, out bool owns)) return owns;
            float transferred = MaxMove(Transferred(fit, source, key));
            owns = TryFullDelta(mesh, key, out var delta)
                && MaxMove(delta) >= Mathf.Max(source.emptyBelow, transferred * OwnKeyShare);
            fit.ownKey[key] = owns;
            return owns;
        }

        private static Vector3[] Transferred(Fit fit, Source source, string key)
        {
            if (fit.deltas.TryGetValue(key, out var delta)) return delta;
            delta = new Vector3[fit.tri.Length];
            for (int i = 0; i < delta.Length; i++)
                if (fit.tri[i] >= 0) delta[i] = source.Delta(key, fit.tri[i], fit.weights[i]);
            fit.deltas[key] = delta;
            return delta;
        }

        private static Fit FitFor(Source source, Mesh mesh)
        {
            if (fits.TryGetValue((source, mesh), out var fit)) return fit;
            var verts = new List<Vector3>(mesh.vertexCount);
            mesh.GetVertices(verts);
            fit = new Fit { tri = new int[verts.Count], weights = new Vector3[verts.Count] };
            for (int i = 0; i < verts.Count; i++)
            {
                source.Nearest(verts[i], out fit.tri[i], out var w);
                fit.weights[i] = w;
            }
            fits[(source, mesh)] = fit;
            return fit;
        }

        // ---------------- Helpers ----------------

        /// <summary>The delta of a mesh's key at weight 100 (its last frame, scaled to 100), or false when it has none.</summary>
        private static bool TryFullDelta(Mesh m, string key, out Vector3[] delta)
        {
            delta = null;
            for (int s = 0; s < m.blendShapeCount; s++)
            {
                if (!m.GetBlendShapeName(s).Contains(key)) continue;
                int frames = m.GetBlendShapeFrameCount(s);
                if (frames == 0) return false;
                delta = new Vector3[m.vertexCount];
                m.GetBlendShapeFrameVertices(s, frames - 1, delta, null, null);
                float frameWeight = m.GetBlendShapeFrameWeight(s, frames - 1);
                if (frameWeight > 0f && !Mathf.Approximately(frameWeight, 100f))
                    for (int i = 0; i < delta.Length; i++) delta[i] *= 100f / frameWeight;
                return true;
            }
            return false;
        }

        private static float MaxMove(Vector3[] delta)
        {
            float max = 0f;
            for (int i = 0; i < delta.Length; i++) max = Mathf.Max(max, delta[i].sqrMagnitude);
            return Mathf.Sqrt(max);
        }

        /// <summary>Barycentric weights of the point of triangle abc nearest p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return new Vector3(1f - v, v, 0f);
            }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return new Vector3(1f - w, 0f, w);
            }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return new Vector3(0f, 1f - w, w);
            }
            float denom = 1f / (va + vb + vc);
            float v2 = vb * denom, w2 = vc * denom;
            return new Vector3(1f - v2 - w2, v2, w2);
        }
    }
}
