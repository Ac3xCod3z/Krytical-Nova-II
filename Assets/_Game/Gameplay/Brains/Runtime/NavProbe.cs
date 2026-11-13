#if KN_TEST_LANE || UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.AI.Navigation.LowLevel;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Brains
{
    /// <summary>
    /// The M5.4b Linux proof (owner, 6 Nov 2026; test lane and Editor only): the brains' NavWorld path search on the loaded map's NavMesh,
    /// checked against Unity's own <c>NavMesh.CalculatePath</c> on the same machine. Pairs of points come from a fixed seed (the same
    /// pairs on every platform), so a Linux run and an Editor run can also be compared line by line. Started in a player by
    /// <c>-knnavprobe</c> (optional <c>-knnavprobeout &lt;folder&gt;</c>); writes <c>navprobe-&lt;platform&gt;.txt</c>, logs one
    /// <c>[KnNavProbe]</c> summary line and quits (0 = PASS). PASS (owner, 6 Nov 2026): no pair where Unity's path is complete and
    /// NavWorld has none; no NavWorld path where Unity calls the pair invalid (suspicious); corners within 0.1 m where both are complete
    /// (heights 0.5 m). A NavWorld path where Unity's is partial (Unity's own search cut short on a long path) is listed apart.
    /// Every NavWorld path ends on the end's polygon (the job refuses any other).
    /// </summary>
    public sealed class NavProbe : MonoBehaviour
    {
        public const string Flag = "-knnavprobe", OutFlag = "-knnavprobeout";
        public const int Pairs = 200;
        public const uint Seed = 0x4B4E;
        public const float MatchMetres = 0.1f;
        /// <summary>Corner heights may differ this much: NavWorld stands on the polygon, CalculatePath on the detail mesh (M5.2 check 3).</summary>
        public const float HeightMetres = 0.5f;
        /// <summary>
        /// The pairs are drawn from mesh vertices at or under this height (12 Nov 2026): the map's own ground. No map's ground reaches
        /// it; a sky deck's NavMesh above it is left out of the draw, so the same seed gives the same pairs with or without the deck.
        /// </summary>
        public const float MaxPairHeight = 200f;
        /// <summary>No node budget in the probe (CalculatePath has none): the game's own budget is a design choice, not a difference.</summary>
        public const int ProbeNodeBudget = 65536;

        public struct Report
        {
            public int Pairs, BothFound, NeitherFound, CornersMatch, CornersDiffer;
            /// <summary>Unity complete, NavWorld none (a FAIL).</summary>
            public int Missed;
            /// <summary>NavWorld complete, Unity partial: Unity's search was cut short (listed apart, not a FAIL).</summary>
            public int UnityCutShort;
            /// <summary>NavWorld complete, Unity invalid (a FAIL: suspicious).</summary>
            public int Suspicious;
            /// <summary>Of <see cref="CornersMatch"/>: pairs whose corners match only thanks to the height allowance (a height off by more than <see cref="MatchMetres"/>).</summary>
            public int HeightOnly;
            public double NavWorldMsPerPath, CalculatePathMsPerPath;
            public string Text;
            public bool Passed => Pairs > 0 && Missed == 0 && Suspicious == 0 && CornersDiffer == 0;
        }

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;
            var host = new GameObject("KnNavProbe");
            DontDestroyOnLoad(host);
            host.AddComponent<NavProbe>();
        }

        IEnumerator Start()
        {
            // the map's NavMeshSurfaces add their data as they are enabled: give them a few frames
            for (int i = 0; i < 10; i++) yield return null;
            string[] args = Environment.GetCommandLineArgs();
            int o = Array.IndexOf(args, OutFlag);
            string folder = o >= 0 && o + 1 < args.Length ? args[o + 1] : Application.persistentDataPath;
            Report r = Run(new PathBackend { Kind = PathBackendKind.NavWorld, AgentTypeId = 0, Extents = new float3(1f, 2f, 1f) });
            Write(r, folder);
            Application.Quit(r.Passed ? 0 : 1);
        }
#endif

        /// <summary>Writes the report file and the summary line.</summary>
        public static string Write(Report r, string folder)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"navprobe-{Application.platform}.txt");
            File.WriteAllText(path, r.Text);
            Debug.Log("[KnNavProbe] " + Summary(r) + " -> " + path);
            return path;
        }

        public static string Summary(Report r) =>
            FormattableString.Invariant($"{(r.Passed ? "PASS" : "FAIL")} on {Application.platform}: {r.Pairs} pairs, both complete {r.BothFound}, neither {r.NeitherFound}, ") +
            FormattableString.Invariant($"missed by NavWorld {r.Missed}, suspicious {r.Suspicious}, Unity cut short {r.UnityCutShort}, corners within {MatchMetres} m (heights {HeightMetres} m) {r.CornersMatch} (on the height allowance only {r.HeightOnly}), corners differ {r.CornersDiffer} (link paths not counted), ") +
            FormattableString.Invariant($"NavWorld {r.NavWorldMsPerPath:F3} ms/path, CalculatePath {r.CalculatePathMsPerPath:F3} ms/path");

        /// <summary>Runs the pairs on the NavMesh loaded now. Never throws for a missing mesh: 0 pairs = FAIL.</summary>
        public static Report Run(PathBackend backend)
        {
            var report = new Report();
            var text = new StringBuilder();
            text.AppendLine($"# KnNavProbe {Application.platform} {Application.unityVersion} seed {Seed}");
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            if (mesh.vertices == null || mesh.vertices.Length == 0)
            {
                text.AppendLine("no NavMesh loaded");
                report.Text = text.ToString();
                return report;
            }

            // the pairs: points of the mesh itself (its vertices, settled with SamplePosition) from a fixed seed
            var rng = new Unity.Mathematics.Random(Seed);
            // ground only: a second NavMesh high above the map (the Concord 040 sky deck, 300 m over Zone01) would put its
            // vertices into the draw, change every pair after the first of them and make most pairs ground-to-deck, with no path
            Vector3[] vertices = Array.FindAll(mesh.vertices, v => v.y <= MaxPairHeight);
            if (vertices.Length == 0)
            {
                text.AppendLine(FormattableString.Invariant($"no NavMesh vertex at or under {MaxPairHeight} m"));
                report.Text = text.ToString();
                return report;
            }
            var starts = new NativeArray<float3>(Pairs, Allocator.TempJob);
            var ends = new NativeArray<float3>(Pairs, Allocator.TempJob);
            int made = 0;
            for (int tries = 0; made < Pairs && tries < Pairs * 50; tries++)
            {
                if (!OnMesh(ref rng, vertices, out Vector3 a) || !OnMesh(ref rng, vertices, out Vector3 b)) continue;
                starts[made] = a; ends[made] = b; made++;
            }
            report.Pairs = made;
            var corners = new NativeArray<float3>(Pairs * NavPaths.MaxCorners, Allocator.TempJob);
            var counts = new NativeArray<int>(Pairs, Allocator.TempJob);
            NavWorld world = NavWorld.GetDefaultWorld();
            NavPaths nav = NavPaths.Create(world, ProbeNodeBudget);
            try
            {
                var clock = Stopwatch.StartNew();
                nav.Search(world, backend, ProbeNodeBudget, starts.GetSubArray(0, made), ends.GetSubArray(0, made), corners, counts);
                report.NavWorldMsPerPath = made > 0 ? clock.Elapsed.TotalMilliseconds / made : 0;

                var path = new NavMeshPath();
                double unityMs = 0;
                for (int q = 0; q < made; q++)
                {
                    clock.Restart();
                    bool asked = NavMesh.CalculatePath(starts[q], ends[q], NavMesh.AllAreas, path);
                    unityMs += clock.Elapsed.TotalMilliseconds;
                    NavMeshPathStatus unity = asked ? path.status : NavMeshPathStatus.PathInvalid;
                    bool unityFound = unity == NavMeshPathStatus.PathComplete;
                    bool ours = counts[q] > 0;
                    Vector3[] theirs = unityFound ? path.corners : Array.Empty<Vector3>();
                    string verdict;
                    if (unityFound && !ours) { report.Missed++; verdict = "MISSED-BY-NAVWORLD"; }
                    else if (!unityFound && ours && unity == NavMeshPathStatus.PathPartial) { report.UnityCutShort++; verdict = "unity-cut-short"; }
                    else if (!unityFound && ours) { report.Suspicious++; verdict = "SUSPICIOUS-UNITY-INVALID"; }
                    else if (!ours) { report.NeitherFound++; verdict = "neither"; }
                    else
                    {
                        report.BothFound++;
                        float worst = Worst(corners, q * NavPaths.MaxCorners, counts[q], theirs, out float worstHeight);
                        bool link = HasLink(world, backend, starts[q], ends[q]);
                        if (worst <= MatchMetres)
                        {
                            report.CornersMatch++;
                            verdict = "match";
                            if (worstHeight > MatchMetres) { report.HeightOnly++; verdict = "match-height-only"; }
                        }
                        else if (link) verdict = "link";   // NavMeshLink corners differ by design (M5.2 check 2)
                        else { report.CornersDiffer++; verdict = "CORNERS-DIFFER"; }
                        verdict += FormattableString.Invariant($" worst {worst:F3} height {worstHeight:F3}");
                    }
                    text.AppendLine(FormattableString.Invariant(
                        $"{q} {P(starts[q])} {P(ends[q])} ours {counts[q]} unity {unity} {theirs.Length} {verdict}"));
                    // a pair whose corners differ shows both lists, so a reader can tell a harmless extra waypoint on the same
                    // corridor from a corner on another NavMesh data (12 Nov 2026: Zone01 with the Concord sky deck above the town)
                    if (verdict.StartsWith("CORNERS-DIFFER", StringComparison.Ordinal))
                    {
                        for (int i = 0; i < counts[q]; i++) text.AppendLine(FormattableString.Invariant($"    ours {i} {P(corners[q * NavPaths.MaxCorners + i])}"));
                        for (int i = 0; i < theirs.Length; i++) text.AppendLine(FormattableString.Invariant($"    unity {i} {P(theirs[i])}"));
                    }
                }
                report.CalculatePathMsPerPath = made > 0 ? unityMs / made : 0;
            }
            finally
            {
                nav.Dispose();
                starts.Dispose(); ends.Dispose(); corners.Dispose(); counts.Dispose();
            }
            text.AppendLine(Summary(report));
            report.Text = text.ToString();
            return report;
        }

        static bool OnMesh(ref Unity.Mathematics.Random rng, Vector3[] vertices, out Vector3 at)
        {
            Vector3 p = vertices[rng.NextInt(vertices.Length)];
            bool hit = NavMesh.SamplePosition(p, out NavMeshHit h, 1f, NavMesh.AllAreas);
            at = h.position;
            return hit;
        }

        /// <summary>
        /// The largest horizontal distance between matching corners (infinity when the counts differ, or when a height differs by more
        /// than <see cref="HeightMetres"/>).
        /// </summary>
        static float Worst(NativeArray<float3> ours, int from, int count, Vector3[] theirs, out float worstHeight)
        {
            worstHeight = 0f;
            if (count != theirs.Length) return float.PositiveInfinity;
            float worst = 0f;
            for (int i = 0; i < count; i++)
            {
                float3 a = ours[from + i], b = theirs[i];
                worstHeight = math.max(worstHeight, math.abs(a.y - b.y));
                if (math.abs(a.y - b.y) > HeightMetres) return float.PositiveInfinity;
                worst = math.max(worst, math.distance(a.xz, b.xz));
            }
            return worst;
        }

        /// <summary>Whether the corridor between the two points crosses a NavMeshLink (corners there differ from Unity's by design).</summary>
        static bool HasLink(NavWorld world, in PathBackend backend, float3 a, float3 b)
        {
            using var buffer = new NavQueryBuffer(world, Allocator.Temp, 4096);
            using var nodes = new NativeArray<NavNode>(NavPaths.MaxPath, Allocator.Temp);
            NavLocation s = world.MapLocation(a, backend.Extents, backend.AgentTypeId);
            NavLocation e = world.MapLocation(b, backend.Extents, backend.AgentTypeId);
            if (!world.IsValid(s) || !world.IsValid(e)) return false;
            NavQueryStatus st = world.BeginFindPath(buffer, s, e);
            while ((st & NavQueryStatus.InProgress) != 0) st = world.ContinueFindPath(buffer, 1024, out _);
            if ((st & NavQueryStatus.Success) == 0) return false;
            world.EndFindPath(buffer, out int size);
            int n = world.GetResultFromFindPath(buffer, nodes.Slice(0, math.min(size, NavPaths.MaxPath)));
            for (int i = 0; i < n; i++) if (world.GetNodeType(nodes[i]) == NavNodeType.Link) return true;
            return false;
        }

        static string P(float3 p) => FormattableString.Invariant($"({p.x:F2},{p.y:F2},{p.z:F2})");
    }
}
#endif
