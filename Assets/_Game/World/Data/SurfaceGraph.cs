using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Walls as a second ground (owner, 17 Oct 2026): every place on a steep surface a player can stand, and how they
    /// join. A node is a spot standing out from a wall, a cliff or a rock face, with that surface's normal; an edge says
    /// two nodes are one step apart along the surface (or round a corner); a join ties a node to the ground NavMesh at a
    /// surface's foot or to the walkable top over its edge. Baked in the Editor (<c>SurfaceGraphBuilder</c>) into a
    /// compressed binary file beside this asset, so a whole zone's graph is a few megabytes and the server never scans at
    /// run time; read on first use, with a grid to find the node nearest a place. Routes over it are found by
    /// <see cref="Surfaces.SurfaceRoute"/>.
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

        private const int Magic = 0x47534E4B; // "KNSG"
        private const int Version = 3; // 2: edges as offsets from their node, packed with their kind; 3: points in centimetre steps
        private const float IndexCell = 2f;

        [Tooltip("The baked graph: nodes, edges and joins, compressed (written by the builder).")]
        [SerializeField] private TextAsset data;
        [SerializeField] private int nodeCount;
        [Tooltip("What was baked, for the record: the regions and the numbers used.")]
        [TextArea] public string bakeNote;

        // The graph itself, read from the data on first use (or set directly by the builder and tests).
        [NonSerialized] public Vector3[] points;
        [NonSerialized] public Vector3[] normals;
        /// <summary>Node i's edges are edgeTo[edgeStart[i] .. edgeStart[i + 1]).</summary>
        [NonSerialized] public int[] edgeStart;
        [NonSerialized] public int[] edgeTo;
        [NonSerialized] public EdgeKind[] edgeKind;
        [NonSerialized] public Join[] joins;
        [NonSerialized] private Dictionary<Vector3Int, List<int>> index;
        [NonSerialized] private Dictionary<Vector2Int, List<int>>[] joinIndex;
        [NonSerialized] private volatile bool loading;
        private const float JoinCell = 8f;

        // Scratch for route searches, reused so a search allocates nothing large (one search at a time: the server's).
        [NonSerialized] internal float[] SearchCost;
        [NonSerialized] internal int[] SearchFrom;
        [NonSerialized] internal int[] SearchStamp;
        [NonSerialized] internal int SearchGeneration;

        public int NodeCount
        {
            get
            {
                EnsureLoaded();
                return points != null ? points.Length : 0;
            }
        }

        /// <summary>The graph's binary file (for the builder).</summary>
        public TextAsset Data => data;

        /// <summary>
        /// Starts reading the graph on another thread, so the first click on a wall does not wait for it (a whole zone's
        /// graph takes a second or so). The zone calls it as it starts, on the server and on every game.
        /// </summary>
        public void Preload()
        {
            if (points != null || data == null || loading) return;
            loading = true;
            byte[] bytes = data.bytes; // read here: Unity objects belong to the main thread
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    Decode(bytes);
                    BuildIndex();
                }
                catch (Exception e) { Debug.LogError($"[Surface graph] could not be read: {e.Message}"); }
                finally { loading = false; }
            });
        }

        /// <summary>Reads the graph from its data the first time it is needed.</summary>
        public void EnsureLoaded()
        {
            if (loading) { while (loading) System.Threading.Thread.Sleep(1); } // being read: wait for it
            if (points != null || data == null) return;
            try { Decode(data.bytes); }
            catch (Exception e)
            {
                Debug.LogError($"[Surface graph] {name} could not be read: {e.Message}");
                Clear();
            }
        }

        /// <summary>The nearest node to a point, within <paramref name="maxDistance"/>; -1 when there is none.</summary>
        public int Nearest(Vector3 point, float maxDistance)
        {
            EnsureLoaded();
            if (points == null || points.Length == 0) return -1;
            if (index == null) BuildIndex();
            int best = -1;
            float bestSq = maxDistance * maxDistance;
            int span = Mathf.CeilToInt(maxDistance / IndexCell);
            Vector3Int c = Key(point);
            for (int x = -span; x <= span; x++)
            for (int y = -span; y <= span; y++)
            for (int z = -span; z <= span; z++)
            {
                if (!index.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out List<int> list)) continue;
                foreach (int i in list)
                {
                    float sq = (points[i] - point).sqrMagnitude;
                    if (sq >= bestSq) continue;
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Fills the graph from baked lists (the builder and tests use it).</summary>
        public void Set(Vector3[] nodePoints, Vector3[] nodeNormals, int[] starts, int[] to, EdgeKind[] kinds, Join[] nodeJoins, string note)
        {
            points = nodePoints;
            normals = nodeNormals;
            edgeStart = starts;
            edgeTo = to;
            edgeKind = kinds;
            joins = nodeJoins;
            bakeNote = note;
            nodeCount = points != null ? points.Length : 0;
            index = null;
            joinIndex = null;
            SearchCost = null;
        }

        /// <summary>The builder points the graph at its baked file.</summary>
        public void SetData(TextAsset baked, int nodes)
        {
            data = baked;
            nodeCount = nodes;
        }

        /// <summary>The graph as compressed bytes, for its file.</summary>
        public byte[] Encode()
        {
            using var memory = new MemoryStream();
            using (var zip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(zip))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(points.Length);
                w.Write(edgeTo.Length);
                w.Write(joins.Length);
                // Points to the centimetre, each as the step from the one before (neighbours were sampled together).
                Vector3Int last = Vector3Int.zero;
                foreach (Vector3 p in points)
                {
                    var q = new Vector3Int(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
                    WriteSigned(w, q.x - last.x); WriteSigned(w, q.y - last.y); WriteSigned(w, q.z - last.z);
                    last = q;
                }
                foreach (Vector3 n in normals) { w.Write((sbyte)Mathf.RoundToInt(n.x * 127f)); w.Write((sbyte)Mathf.RoundToInt(n.y * 127f)); w.Write((sbyte)Mathf.RoundToInt(n.z * 127f)); }
                // Each node's edge count, then each edge as the zig-zag offset to its node, shifted up one bit for its
                // kind: nodes near each other were sampled near each other, so most offsets are small.
                for (int i = 0; i < points.Length; i++)
                {
                    WriteVarint(w, (uint)(edgeStart[i + 1] - edgeStart[i]));
                    for (int e = edgeStart[i]; e < edgeStart[i + 1]; e++)
                    {
                        int delta = edgeTo[e] - i;
                        uint zig = (uint)((delta << 1) ^ (delta >> 31));
                        WriteVarint(w, (zig << 1) | (uint)edgeKind[e]);
                    }
                }
                // Joins: their node, and their NavMesh point to the centimetre as the step from that node.
                foreach (Join j in joins)
                {
                    WriteVarint(w, (uint)j.node);
                    Vector3 d = (j.navPoint - points[j.node]) * 100f;
                    WriteSigned(w, Mathf.RoundToInt(d.x)); WriteSigned(w, Mathf.RoundToInt(d.y)); WriteSigned(w, Mathf.RoundToInt(d.z));
                    w.Write((byte)j.kind);
                }
            }
            return memory.ToArray();
        }

        private void Decode(byte[] bytes)
        {
            using var memory = new MemoryStream(bytes);
            using var zip = new GZipStream(memory, CompressionMode.Decompress);
            using var r = new BinaryReader(zip);
            if (r.ReadInt32() != Magic) throw new InvalidDataException("not a surface graph");
            if (r.ReadInt32() != Version) throw new InvalidDataException("another version");
            int n = r.ReadInt32(), e = r.ReadInt32(), jn = r.ReadInt32();
            var p = new Vector3[n];
            Vector3Int last = Vector3Int.zero;
            for (int i = 0; i < n; i++)
            {
                last += new Vector3Int(ReadSigned(r), ReadSigned(r), ReadSigned(r));
                p[i] = new Vector3(last.x, last.y, last.z) * 0.01f;
            }
            var nn = new Vector3[n];
            for (int i = 0; i < n; i++) nn[i] = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()).normalized;
            var s = new int[n + 1];
            var t = new int[e];
            var k = new EdgeKind[e];
            int at = 0;
            for (int i = 0; i < n; i++)
            {
                s[i] = at;
                uint count = ReadVarint(r);
                for (uint c = 0; c < count; c++)
                {
                    uint packed = ReadVarint(r);
                    uint zig = packed >> 1;
                    int delta = (int)(zig >> 1) ^ -(int)(zig & 1);
                    t[at] = i + delta;
                    k[at] = (EdgeKind)(packed & 1);
                    at++;
                }
            }
            s[n] = at;
            var j = new Join[jn];
            for (int i = 0; i < jn; i++)
            {
                int node = (int)ReadVarint(r);
                var d = new Vector3(ReadSigned(r), ReadSigned(r), ReadSigned(r)) * 0.01f;
                j[i] = new Join { node = node, navPoint = p[node] + d, kind = (JoinKind)r.ReadByte() };
            }
            Set(p, nn, s, t, k, j, bakeNote);
        }

        private static void WriteSigned(BinaryWriter w, int value) => WriteVarint(w, (uint)((value << 1) ^ (value >> 31)));

        private static int ReadSigned(BinaryReader r)
        {
            uint zig = ReadVarint(r);
            return (int)(zig >> 1) ^ -(int)(zig & 1);
        }

        private static void WriteVarint(BinaryWriter w, uint value)
        {
            while (value >= 0x80) { w.Write((byte)(value | 0x80)); value >>= 7; }
            w.Write((byte)value);
        }

        private static uint ReadVarint(BinaryReader r)
        {
            uint value = 0;
            int shift = 0;
            while (true)
            {
                byte b = r.ReadByte();
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
        }

        private void Clear() => Set(Array.Empty<Vector3>(), Array.Empty<Vector3>(), new[] { 0 }, Array.Empty<int>(), Array.Empty<EdgeKind>(), Array.Empty<Join>(), bakeNote);

        /// <summary>
        /// The joins of a kind nearest a point, nearest first, at most <paramref name="count"/>, kept at least a metre
        /// apart (joins come in clusters along an edge, and the few tried should be different ways). From a grid, so a
        /// zone's hundred thousand joins are not sorted for every click.
        /// </summary>
        public void JoinsNear(Vector3 point, JoinKind kind, int count, List<Join> into)
        {
            into.Clear();
            EnsureLoaded();
            if (joins == null || joins.Length == 0) return;
            if (joinIndex == null) BuildJoinIndex();
            Dictionary<Vector2Int, List<int>> grid = joinIndex[(int)kind];
            var c = new Vector2Int(Mathf.FloorToInt(point.x / JoinCell), Mathf.FloorToInt(point.z / JoinCell));
            var found = new List<int>(count * 8);
            for (int ring = 0; ring <= MaxJoinRings; ring++)
            {
                for (int x = -ring; x <= ring; x++)
                for (int z = -ring; z <= ring; z++)
                {
                    if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring) continue; // this ring only
                    if (grid.TryGetValue(new Vector2Int(c.x + x, c.y + z), out List<int> list)) found.AddRange(list);
                }
                // Enough found, and one ring past where they were found so nothing nearer is missed.
                if (found.Count >= count * 6 && ring >= 1) break;
            }
            found.Sort((a, b) => (joins[a].navPoint - point).sqrMagnitude.CompareTo((joins[b].navPoint - point).sqrMagnitude));
            foreach (int i in found)
            {
                Join join = joins[i];
                bool near = false;
                foreach (Join k in into)
                    if ((k.navPoint - join.navPoint).sqrMagnitude < 1f) { near = true; break; }
                if (near) continue;
                into.Add(join);
                if (into.Count >= count) break;
            }
        }

        private const int MaxJoinRings = 12; // about a hundred metres

        private void BuildJoinIndex()
        {
            var built = new[] { new Dictionary<Vector2Int, List<int>>(), new Dictionary<Vector2Int, List<int>>() };
            for (int i = 0; i < joins.Length; i++)
            {
                Vector3 p = joins[i].navPoint;
                var c = new Vector2Int(Mathf.FloorToInt(p.x / JoinCell), Mathf.FloorToInt(p.z / JoinCell));
                Dictionary<Vector2Int, List<int>> grid = built[(int)joins[i].kind];
                if (!grid.TryGetValue(c, out List<int> list)) grid[c] = list = new List<int>(8);
                list.Add(i);
            }
            joinIndex = built;
        }

        private void BuildIndex()
        {
            if (joinIndex == null && joins != null) BuildJoinIndex();
            index = new Dictionary<Vector3Int, List<int>>(points.Length / 8 + 1);
            for (int i = 0; i < points.Length; i++)
            {
                Vector3Int c = Key(points[i]);
                if (!index.TryGetValue(c, out List<int> list)) index[c] = list = new List<int>(8);
                list.Add(i);
            }
        }

        private static Vector3Int Key(Vector3 p) =>
            new Vector3Int(Mathf.FloorToInt(p.x / IndexCell), Mathf.FloorToInt(p.y / IndexCell), Mathf.FloorToInt(p.z / IndexCell));

        private void OnValidate() => points = null; // a new file: read it again
    }
}
