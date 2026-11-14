using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// The lighter meshes of a townsperson's look (owner 14 Nov 2026: fewer polygons with distance). A look's mesh is merged from Sidekick
    /// parts at run time, so Unity's Mesh LOD generator (Editor only) cannot make them: this one does, once per look when it is baked, never
    /// per body. Vertex clustering: the mesh is cut into a grid of cells and the vertices of one cell, one submesh and one colour cell of the
    /// swatch (its UV, so colours never bleed) become one; the cell size is searched until about <c>share</c> of the triangles are left.
    /// Each kept vertex keeps its own UV, normal and bone weights, so the light mesh skins on the same bones and draws in the same
    /// materials. No blend shapes (a face is a few pixels that far away).
    /// </summary>
    public static class TownsfolkMeshLod
    {
        /// <summary>UV cells per side the swatch is read in (a vertex never joins one of another colour).</summary>
        public const int UvCells = 32;
        const int SearchSteps = 9;

        static readonly List<Vector2> s_Uv = new();
        static readonly List<Vector3> s_Verts = new(), s_Normals = new();
        static readonly List<Vector4> s_Tangents = new();
        static readonly List<BoneWeight> s_Weights = new();
        static readonly List<int> s_Tris = new();

        /// <summary>A copy of <paramref name="source"/> with about <paramref name="share"/> of its triangles; null when nothing can be made.</summary>
        public static Mesh Make(Mesh source, float share, string name)
        {
            if (source == null || !source.isReadable || share >= 0.999f) return null;
            source.GetVertices(s_Verts);
            source.GetUVs(0, s_Uv);
            int n = s_Verts.Count;
            if (n < 3) return null;
            bool hasUv = s_Uv.Count == n;
            int subs = source.subMeshCount;
            var tris = new int[subs][];
            var vertSub = new int[n];
            int total = 0;
            for (int s = 0; s < subs; s++)
            {
                source.GetTriangles(s_Tris, s);
                tris[s] = s_Tris.ToArray();
                total += tris[s].Length / 3;
                foreach (int v in tris[s]) vertSub[v] = s;
            }
            if (total < 8) return null;
            int target = Mathf.Max(4, Mathf.RoundToInt(total * Mathf.Clamp01(share)));

            Bounds b = source.bounds;
            float size = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z), 0.01f);
            float lo = size / 4000f, hi = size / 6f;   // lo keeps almost everything, hi almost nothing
            var map = new int[n];
            var cells = new Dictionary<long, int>(n);
            float best = hi;
            for (int step = 0; step < SearchSteps; step++)
            {
                float cell = Mathf.Sqrt(lo * hi);
                int kept = Cluster(cell, b.min, hasUv, vertSub, map, cells, tris);
                if (kept > target) lo = cell; else { hi = cell; best = cell; }
            }
            Cluster(best, b.min, hasUv, vertSub, map, cells, tris);
            return Build(source, name, map, tris, n);
        }

        /// <summary>Triangles left when vertices are joined in cells of <paramref name="cell"/> metres (<paramref name="map"/> = each vertex's keeper).</summary>
        static int Cluster(float cell, Vector3 origin, bool hasUv, int[] vertSub, int[] map, Dictionary<long, int> cells, int[][] tris)
        {
            cells.Clear();
            float inv = 1f / cell;
            for (int v = 0; v < map.Length; v++)
            {
                Vector3 p = s_Verts[v] - origin;
                long x = (long)(p.x * inv) & 0xFFF, y = (long)(p.y * inv) & 0xFFF, z = (long)(p.z * inv) & 0xFFF;   // 12 bits: size / 4000 still fits
                long u = 0, w = 0;
                if (hasUv)
                {
                    Vector2 t = s_Uv[v];
                    u = (long)Mathf.Floor(Mathf.Repeat(t.x, 1f) * UvCells) & 0x1F;
                    w = (long)Mathf.Floor(Mathf.Repeat(t.y, 1f) * UvCells) & 0x1F;
                }
                long key = x | y << 12 | z << 24 | u << 36 | w << 41 | (long)(vertSub[v] & 0xFF) << 46;
                if (!cells.TryGetValue(key, out int keeper)) { keeper = v; cells[key] = v; }
                map[v] = keeper;
            }
            int kept = 0;
            foreach (int[] list in tris)
                for (int i = 0; i + 2 < list.Length; i += 3)
                {
                    int a = map[list[i]], c = map[list[i + 1]], d = map[list[i + 2]];
                    if (a != c && c != d && a != d) kept++;
                }
            return kept;
        }

        static Mesh Build(Mesh source, string name, int[] map, int[][] tris, int n)
        {
            // only the keepers are copied, renumbered in their old order
            var index = new int[n];
            int count = 0;
            for (int v = 0; v < n; v++) index[v] = map[v] == v ? count++ : -1;
            var mesh = new Mesh { name = name, indexFormat = count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            var verts = new List<Vector3>(count);
            for (int v = 0; v < n; v++) if (index[v] >= 0) verts.Add(s_Verts[v]);
            mesh.SetVertices(verts);

            source.GetNormals(s_Normals);
            if (s_Normals.Count == n) mesh.SetNormals(Pick(s_Normals, index, count));
            source.GetTangents(s_Tangents);
            if (s_Tangents.Count == n) mesh.SetTangents(Pick(s_Tangents, index, count));
            var uv = new List<Vector2>();
            for (int ch = 0; ch < 4; ch++)
            {
                source.GetUVs(ch, uv);
                if (uv.Count == n) mesh.SetUVs(ch, Pick(uv, index, count));
            }
            Color32[] colors = source.colors32;
            if (colors != null && colors.Length == n)
            {
                var c = new Color32[count];
                for (int v = 0; v < n; v++) if (index[v] >= 0) c[index[v]] = colors[v];
                mesh.colors32 = c;
            }
            source.GetBoneWeights(s_Weights);
            if (s_Weights.Count == n)
            {
                var w = new BoneWeight[count];
                for (int v = 0; v < n; v++) if (index[v] >= 0) w[index[v]] = s_Weights[v];
                mesh.boneWeights = w;
                mesh.bindposes = source.bindposes;
            }

            mesh.subMeshCount = tris.Length;
            var list = new List<int>();
            for (int s = 0; s < tris.Length; s++)
            {
                list.Clear();
                int[] t = tris[s];
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    int a = map[t[i]], c = map[t[i + 1]], d = map[t[i + 2]];
                    if (a == c || c == d || a == d) continue;
                    list.Add(index[a]); list.Add(index[c]); list.Add(index[d]);
                }
                mesh.SetTriangles(list, s, false);
            }
            mesh.bounds = source.bounds;
            mesh.UploadMeshData(true);   // drawn only; nothing reads it back
            return mesh;
        }

        static List<T> Pick<T>(List<T> from, int[] index, int count)
        {
            var to = new List<T>(count);
            for (int v = 0; v < index.Length; v++) if (index[v] >= 0) to.Add(from[v]);
            return to;
        }

        /// <summary>Triangles of every submesh of <paramref name="mesh"/>.</summary>
        public static int Triangles(Mesh mesh)
        {
            if (mesh == null) return 0;
            long sum = 0;
            for (int s = 0; s < mesh.subMeshCount; s++) sum += mesh.GetIndexCount(s) / 3;
            return (int)sum;
        }
    }
}
