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

        /// <summary>
        /// The rule the baker joins two nodes by (before its physics checks): a step along a face is at most
        /// <see cref="link"/> times the nodes' spacing scale and turns at most <see cref="maxTurn"/>; a corner turns more,
        /// up to <see cref="cornerTurn"/>, and reaches <see cref="cornerFactor"/> times as far. A version 4 file keeps the
        /// rule and works out the pairs it allows again when it is read, so only which of them were kept is stored.
        /// </summary>
        [Serializable]
        public struct LinkRule
        {
            public float link, maxTurn, cornerTurn, cornerFactor;
            public bool IsSet => link > 0f && cornerFactor > 0f;
        }

        private const int Magic = 0x47534E4B; // "KNSG"
        // 2: edges as offsets from their node, packed with their kind; 3: points in centimetre steps; 4: edges not stored,
        // worked out again from the points, normals and spacing by the link rule, with one bit for each pair it allows
        // (owner, 22 Oct 2026: the wall graphs are "really big"). A version 3 file still reads.
        private const int Version3 = 3;
        private const int Version = 4;
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
        /// <summary>Each node's sample spacing over the wall spacing (terrain and far walls are coarser); null in a version 3 file.</summary>
        [NonSerialized] public float[] scales;
        [NonSerialized] public LinkRule rule;
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
        public void Set(Vector3[] nodePoints, Vector3[] nodeNormals, int[] starts, int[] to, EdgeKind[] kinds, Join[] nodeJoins, string note,
            float[] nodeScales = null, LinkRule linkRule = default)
        {
            scales = nodeScales;
            rule = linkRule;
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

        /// <summary>The graph as compressed bytes, for its file: version 4 when the spacing and the link rule are known.</summary>
        public byte[] Encode() => scales != null && rule.IsSet ? EncodeLinked() : EncodeEdges();

        /// <summary>Version 3: every edge written out.</summary>
        private byte[] EncodeEdges()
        {
            using var memory = new MemoryStream();
            using (var zip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(zip))
            {
                w.Write(Magic);
                w.Write(Version3);
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
            int version = r.ReadInt32();
            if (version == Version) { DecodeLinked(r); return; }
            if (version != Version3) throw new InvalidDataException("another version");
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

        /// <summary>
        /// Version 4. The points, normals and spacing are written as they will be read; the pairs the link rule allows
        /// between those read values are worked out (<see cref="LinkPairs"/>) and one bit each says whether it is an edge;
        /// an edge the rule does not give again (rounding at its limits) is written out after them. Joins go in node order,
        /// each as the step from the one before.
        /// </summary>
        private byte[] EncodeLinked()
        {
            int n = points.Length;
            var qp = new Vector3[n];
            var qn = new Vector3[n];
            var qs = new float[n];
            using var memory = new MemoryStream();
            using (var zip = new GZipStream(memory, System.IO.Compression.CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(zip))
            {
                w.Write(Magic);
                w.Write(Version);
                w.Write(n);
                w.Write(joins.Length);
                w.Write(rule.link); w.Write(rule.maxTurn); w.Write(rule.cornerTurn); w.Write(rule.cornerFactor);
                Vector3Int last = Vector3Int.zero;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = points[i];
                    var q = new Vector3Int(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
                    WriteSigned(w, q.x - last.x); WriteSigned(w, q.y - last.y); WriteSigned(w, q.z - last.z);
                    last = q;
                    qp[i] = new Vector3(q.x, q.y, q.z) * 0.01f;
                }
                for (int i = 0; i < n; i++)
                {
                    Vector3 v = normals[i];
                    sbyte x = (sbyte)Mathf.RoundToInt(v.x * 127f), y = (sbyte)Mathf.RoundToInt(v.y * 127f), z = (sbyte)Mathf.RoundToInt(v.z * 127f);
                    w.Write(x); w.Write(y); w.Write(z);
                    qn[i] = new Vector3(x, y, z).normalized;
                }
                for (int i = 0; i < n; i++)
                {
                    byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(scales[i] * ScaleSteps), 1, 255);
                    w.Write(b);
                    qs[i] = b / ScaleSteps;
                }

                LinkPairs(qp, qn, qs, rule, out int[] pairStart, out int[] pairTo, out EdgeKind[] pairKind);
                var bits = new byte[(pairTo.Length + 7) / 8];
                var extra = new List<int>[n];
                for (int i = 0; i < n; i++)
                {
                    for (int e = edgeStart[i]; e < edgeStart[i + 1]; e++)
                    {
                        int found = -1;
                        for (int c = pairStart[i]; c < pairStart[i + 1]; c++)
                            if (pairTo[c] == edgeTo[e] && pairKind[c] == edgeKind[e]) { found = c; break; }
                        if (found >= 0) bits[found >> 3] |= (byte)(1 << (found & 7));
                        else (extra[i] ??= new List<int>()).Add(e);
                    }
                }
                w.Write(pairTo.Length);
                w.Write(bits);
                for (int i = 0; i < n; i++)
                {
                    List<int> list = extra[i];
                    WriteVarint(w, (uint)(list?.Count ?? 0));
                    if (list == null) continue;
                    foreach (int e in list)
                    {
                        int delta = edgeTo[e] - i;
                        uint zig = (uint)((delta << 1) ^ (delta >> 31));
                        WriteVarint(w, (zig << 1) | (uint)edgeKind[e]);
                    }
                }

                var order = new int[joins.Length];
                for (int i = 0; i < order.Length; i++) order[i] = i;
                Array.Sort(order, (a, b) => joins[a].node != joins[b].node ? joins[a].node.CompareTo(joins[b].node) : a.CompareTo(b));
                int lastNode = 0;
                foreach (int i in order)
                {
                    Join j = joins[i];
                    WriteVarint(w, (uint)(j.node - lastNode));
                    lastNode = j.node;
                    Vector3 d = (j.navPoint - qp[j.node]) * 100f;
                    WriteSigned(w, Mathf.RoundToInt(d.x)); WriteSigned(w, Mathf.RoundToInt(d.y)); WriteSigned(w, Mathf.RoundToInt(d.z));
                    w.Write((byte)j.kind);
                }
            }
            return memory.ToArray();
        }

        private void DecodeLinked(BinaryReader r)
        {
            int n = r.ReadInt32(), jn = r.ReadInt32();
            var linkRule = new LinkRule { link = r.ReadSingle(), maxTurn = r.ReadSingle(), cornerTurn = r.ReadSingle(), cornerFactor = r.ReadSingle() };
            var p = new Vector3[n];
            Vector3Int last = Vector3Int.zero;
            for (int i = 0; i < n; i++)
            {
                last += new Vector3Int(ReadSigned(r), ReadSigned(r), ReadSigned(r));
                p[i] = new Vector3(last.x, last.y, last.z) * 0.01f;
            }
            var nn = new Vector3[n];
            for (int i = 0; i < n; i++) nn[i] = new Vector3(r.ReadSByte(), r.ReadSByte(), r.ReadSByte()).normalized;
            var sc = new float[n];
            for (int i = 0; i < n; i++) sc[i] = r.ReadByte() / ScaleSteps;

            LinkPairs(p, nn, sc, linkRule, out int[] pairStart, out int[] pairTo, out EdgeKind[] pairKind);
            int pairCount = r.ReadInt32();
            if (pairCount != pairTo.Length) throw new InvalidDataException($"the link rule gave {pairTo.Length} pairs, the file {pairCount}");
            byte[] bits = r.ReadBytes((pairCount + 7) / 8);
            var s = new int[n + 1];
            var t = new List<int>(pairCount);
            var k = new List<EdgeKind>(pairCount);
            for (int i = 0; i < n; i++)
            {
                s[i] = t.Count;
                for (int c = pairStart[i]; c < pairStart[i + 1]; c++)
                {
                    if ((bits[c >> 3] & (1 << (c & 7))) == 0) continue;
                    t.Add(pairTo[c]);
                    k.Add(pairKind[c]);
                }
                uint extras = ReadVarint(r);
                for (uint x = 0; x < extras; x++)
                {
                    uint packed = ReadVarint(r);
                    uint zig = packed >> 1;
                    t.Add(i + ((int)(zig >> 1) ^ -(int)(zig & 1)));
                    k.Add((EdgeKind)(packed & 1));
                }
            }
            s[n] = t.Count;
            var j = new Join[jn];
            int node = 0;
            for (int i = 0; i < jn; i++)
            {
                node += (int)ReadVarint(r);
                var d = new Vector3(ReadSigned(r), ReadSigned(r), ReadSigned(r)) * 0.01f;
                j[i] = new Join { node = node, navPoint = p[node] + d, kind = (JoinKind)r.ReadByte() };
            }
            Set(p, nn, s, t.ToArray(), k.ToArray(), j, bakeNote, sc, linkRule);
        }

        private const float ScaleSteps = 10f;

        /// <summary>
        /// Every ordered pair of nodes the link rule allows, before any physics check, for each node in the order of the
        /// node it leads to: what the baker's edges are chosen from. Worked out the same way when a graph is written and
        /// when it is read, from the same rounded values.
        /// </summary>
        public static void LinkPairs(Vector3[] p, Vector3[] nrm, float[] sc, LinkRule linkRule, out int[] start, out int[] to, out EdgeKind[] kind)
        {
            int n = p.Length;
            if (n == 0) { start = new int[1]; to = Array.Empty<int>(); kind = Array.Empty<EdgeKind>(); return; }
            float smallest = float.PositiveInfinity;
            foreach (float s in sc) smallest = Mathf.Min(smallest, s);
            float cell = Mathf.Max(0.05f, linkRule.link * linkRule.cornerFactor * smallest);
            var sortedKeys = new long[n];
            var order = new int[n];
            for (int i = 0; i < n; i++) { sortedKeys[i] = CellKey(p[i], cell); order[i] = i; }
            Array.Sort(sortedKeys, order); // within a cell the order does not matter: the pairs are sorted at the end
            var cells = new Dictionary<long, (int from, int count)>(n / 4 + 1);
            for (int a = 0; a < n;)
            {
                int b = a;
                while (b < n && sortedKeys[b] == sortedKeys[a]) b++;
                cells[sortedKeys[a]] = (a, b - a);
                a = b;
            }

            var pairKeys = new List<long>(n * 10);
            var pairKinds = new List<EdgeKind>(n * 10);
            for (int i = 0; i < n; i++)
            {
                float reach = linkRule.link * linkRule.cornerFactor * sc[i];
                int span = Mathf.CeilToInt(reach / cell);
                Vector3Int c = CellOf(p[i], cell);
                for (int x = -span; x <= span; x++)
                for (int y = -span; y <= span; y++)
                for (int z = -span; z <= span; z++)
                {
                    if (!cells.TryGetValue(PackCell(c.x + x, c.y + y, c.z + z), out var range)) continue;
                    for (int o = range.from; o < range.from + range.count; o++)
                    {
                        int j = order[o];
                        // Each pair is found from the side with the coarser spacing, whose reach covers it; equal sides
                        // each find their own.
                        if (j == i || sc[j] > sc[i]) continue;
                        float d = Vector3.Distance(p[i], p[j]);
                        if (d > reach) continue;
                        float turn = Vector3.Angle(nrm[i], nrm[j]);
                        EdgeKind k;
                        if (turn <= linkRule.maxTurn && d <= linkRule.link * sc[i]) k = EdgeKind.Face;
                        else if (turn > linkRule.maxTurn && turn <= linkRule.cornerTurn) k = EdgeKind.Corner;
                        else continue;
                        pairKeys.Add(((long)i << 32) | (uint)j);
                        pairKinds.Add(k);
                        if (sc[j] < sc[i])
                        {
                            pairKeys.Add(((long)j << 32) | (uint)i);
                            pairKinds.Add(k);
                        }
                    }
                }
            }
            long[] sortedPairs = pairKeys.ToArray();
            EdgeKind[] kinds = pairKinds.ToArray();
            Array.Sort(sortedPairs, kinds);
            start = new int[n + 1];
            to = new int[sortedPairs.Length];
            kind = kinds;
            int at = 0;
            for (int i = 0; i < n; i++)
            {
                start[i] = at;
                while (at < sortedPairs.Length && (int)(sortedPairs[at] >> 32) == i)
                {
                    to[at] = (int)(sortedPairs[at] & 0xFFFFFFFF);
                    at++;
                }
            }
            start[n] = at;
        }

        private static Vector3Int CellOf(Vector3 p, float cell) =>
            new Vector3Int(Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

        private static long CellKey(Vector3 p, float cell)
        {
            Vector3Int c = CellOf(p, cell);
            return PackCell(c.x, c.y, c.z);
        }

        private static long PackCell(int x, int y, int z) =>
            ((long)(x + (1 << 20)) << 42) | ((long)(y + (1 << 20)) << 21) | (long)(z + (1 << 20));


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
