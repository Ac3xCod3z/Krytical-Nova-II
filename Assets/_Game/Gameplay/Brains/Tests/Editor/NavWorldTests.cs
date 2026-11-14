using System;
using System.IO;
using NUnit.Framework;
using Unity.AI.Navigation.LowLevel;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Brains.Tests
{
    /// <summary>
    /// M5.4b: the NavWorld backend on the real starter map. The probe's Editor run (its file is the Editor side of the Linux proof:
    /// <c>Logs/navprobe-WindowsEditor.txt</c>) and one brain walking a NavMesh path there.
    /// </summary>
    public sealed class NavWorldTests
    {
        // KnLinuxTestBuild.MapScenePath (Tools, not referenced from here): the map the Linux probe runs on
        const string Map = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        static readonly PathBackend Backend = new PathBackend { Kind = PathBackendKind.NavWorld, AgentTypeId = 0, Extents = new float3(1f, 2f, 1f) };

        [OneTimeSetUp]
        public void OpenMap() => EditorSceneManager.OpenScene(Map, OpenSceneMode.Single);

        /// <summary>The town closed again: its dummies and objects never reach the tests after this one (8 Nov 2026: StrikeDummyTests).</summary>
        [OneTimeTearDown]
        public void CloseMap() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void Zone01_NavWorldPaths_MatchCalculatePath()
        {
            NavProbe.Report r = NavProbe.Run(Backend);
            NavProbe.Write(r, Path.GetFullPath("Logs"));
            Assert.AreEqual(NavProbe.Pairs, r.Pairs, "pairs on the mesh");
            Assert.IsTrue(r.Passed, NavProbe.Summary(r));
        }

        /// <summary>
        /// The pinned pair (owner, 12 Nov 2026). On this pair NavWorld and CalculatePath agree on the first 22 corners and then take
        /// neighbouring corridors to the end: NavWorld's route was 5.9 m (4 %) longer on a path of about 640 m, all of it on town
        /// ground. That is accepted as it is; this test fails the day NavWorld's route is more than 5 % longer than Unity's, or leaves
        /// the ground for a NavMesh above the map (the Concord 040 sky deck stands 300 m over this spot).
        /// </summary>
        [Test]
        public void Zone01_ThePinnedPair_NavWorldsRouteIsAtMostFivePercentLonger_AndStaysOnTheGround()
        {
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(74.75f, 0.40f, 105.25f), out NavMeshHit from, 1f, NavMesh.AllAreas), "the start is on the mesh");
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(320.00f, 3.73f, 435.50f), out NavMeshHit to, 1f, NavMesh.AllAreas), "the end is on the mesh");

            var path = new NavMeshPath();
            Assert.IsTrue(NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete, "Unity's path is complete");
            float unity = 0f;
            for (int i = 0; i + 1 < path.corners.Length; i++) unity += Vector3.Distance(path.corners[i], path.corners[i + 1]);

            var starts = new NativeArray<float3>(1, Allocator.TempJob);
            var ends = new NativeArray<float3>(1, Allocator.TempJob);
            var corners = new NativeArray<float3>(NavPaths.MaxCorners, Allocator.TempJob);
            var counts = new NativeArray<int>(1, Allocator.TempJob);
            NavWorld world = NavWorld.GetDefaultWorld();
            NavPaths nav = NavPaths.Create(world, NavProbe.ProbeNodeBudget);
            try
            {
                starts[0] = from.position; ends[0] = to.position;
                nav.Search(world, Backend, NavProbe.ProbeNodeBudget, starts, ends, corners, counts);
                Assert.Greater(counts[0], 1, "NavWorld has a path");
                float ours = 0f;
                for (int i = 0; i < counts[0]; i++)
                {
                    Assert.LessOrEqual(corners[i].y, NavProbe.MaxPairHeight, $"corner {i} at {corners[i]} is not on the town's ground");
                    if (i + 1 < counts[0]) ours += math.distance(corners[i], corners[i + 1]);
                }
                Assert.LessOrEqual(math.distance(corners[counts[0] - 1].xz, ((float3)to.position).xz), NavProbe.MatchMetres, "NavWorld's path ends at the end");
                Assert.LessOrEqual(ours, unity * 1.05f, $"NavWorld's route is {ours:F1} m, Unity's {unity:F1} m: more than 5 % longer");
                Debug.Log(FormattableString.Invariant($"[NavWorldTests] the pinned pair: NavWorld {ours:F1} m in {counts[0]} corners, Unity {unity:F1} m in {path.corners.Length} corners"));
            }
            finally
            {
                nav.Dispose(); starts.Dispose(); ends.Dispose(); corners.Dispose(); counts.Dispose();
            }
        }

        [Test]
        public void OnTheNavMesh_ABrainWalksItsPathToTheEnd()
        {
            Assert.IsTrue(FarPair(out Vector3 from, out Vector3 to), "two points 15-40 m apart joined by a path");
            using var w = new BrainTestWorld(movement: true);
            w.Em.CreateSingleton(Backend);
            Entity b = w.Walker(from, 1, to, 5f, BrainMoverFlags.Run);
            w.Step(30 * BrainTestWorld.TickRate);
            float3 there = w.Position(b);
            Assert.LessOrEqual(math.distance(there.xz, ((float3)to).xz), 0.3f, $"from {from} to {to}, ended at {there}");
            Assert.AreEqual(to.y, there.y, 0.5f, "standing on the mesh");
        }

        /// <summary>
        /// Owner, 6 Nov 2026: monsters never walk on water. The zone's Water sheet lies at the water level under the whole map and makes
        /// everything below it Not Walkable (ZoneTerrainGenerator), so no walkable mesh, and no point along any NavWorld path, is under it.
        /// </summary>
        [Test]
        public void NoMeshAndNoPathUnderTheWater()
        {
            GameObject water = GameObject.Find("Water");
            if (water == null) Assert.Inconclusive("Zone01 has no Water sheet");
            float level = water.transform.position.y;
            const float Slack = 0.3f;   // the bake's height error near the shore
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            int under = 0;
            foreach (Vector3 v in mesh.vertices) if (v.y < level - Slack) under++;
            Assert.AreEqual(0, under, $"walkable mesh vertices under the water level {level}");

            // the probe's own search on 50 pairs: every corner, and a point every 0.5 m between corners, stays above the water level
            var rng = new Unity.Mathematics.Random(NavProbe.Seed);
            var starts = new NativeArray<float3>(50, Allocator.TempJob);
            var ends = new NativeArray<float3>(50, Allocator.TempJob);
            var corners = new NativeArray<float3>(50 * NavPaths.MaxCorners, Allocator.TempJob);
            var counts = new NativeArray<int>(50, Allocator.TempJob);
            NavWorld world = NavWorld.GetDefaultWorld();
            NavPaths nav = NavPaths.Create(world, NavProbe.ProbeNodeBudget);
            try
            {
                for (int i = 0; i < 50; i++)
                {
                    starts[i] = mesh.vertices[rng.NextInt(mesh.vertices.Length)];
                    ends[i] = mesh.vertices[rng.NextInt(mesh.vertices.Length)];
                }
                nav.Search(world, Backend, NavProbe.ProbeNodeBudget, starts, ends, corners, counts);
                int paths = 0;
                for (int q = 0; q < 50; q++)
                {
                    if (counts[q] == 0) continue;
                    paths++;
                    for (int k = 0; k + 1 < counts[q]; k++)
                    {
                        float3 a = corners[q * NavPaths.MaxCorners + k], b = corners[q * NavPaths.MaxCorners + k + 1];
                        int steps = math.max(1, (int)(math.distance(a, b) / 0.5f));
                        for (int s = 0; s <= steps; s++)
                        {
                            float3 p = math.lerp(a, b, s / (float)steps);
                            NavLocation ground = world.MapLocation(p, Backend.Extents, Backend.AgentTypeId);
                            float y = world.IsValid(ground) ? ground.position.y : p.y;
                            Assert.GreaterOrEqual(y, level - Slack, $"pair {q}: the path goes under the water at {p}");
                        }
                    }
                }
                Assert.Greater(paths, 0, "some pairs have paths");
            }
            finally
            {
                nav.Dispose(); starts.Dispose(); ends.Dispose(); corners.Dispose(); counts.Dispose();
            }
        }

        // ---- Sky Concord (the 040 deck above this map): every move follows the mesh round objects (owner, 14 Nov 2026) ----

        /// <summary>Concord's deck vertices (the sky NavMesh above <see cref="NavProbe.MaxPairHeight"/>); inconclusive without the deck.</summary>
        static Vector3[] DeckVertices()
        {
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            var deck = new System.Collections.Generic.List<Vector3>();
            foreach (Vector3 v in mesh.vertices) if (v.y > NavProbe.MaxPairHeight) deck.Add(v);
            if (deck.Count < 100) Assert.Inconclusive("Zone01 has no Concord 040 deck NavMesh");
            return deck.ToArray();
        }

        static bool OnMesh(float3 p, float within) =>
            NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) && math.distance(((float3)hit.position).xz, p.xz) <= within;

        /// <summary>
        /// The walk heatmap's straight trails (14 Nov 2026): the brain walks straight only when its raycast says nothing is in the way. On
        /// 2,000 pairs of deck points 3-80 m apart, the brain's raycast never calls a walk clear that Unity's own NavMesh.Raycast calls
        /// blocked (planters, kiosks, the deck's edge), and a start beside the mesh is never "clear".
        /// </summary>
        [Test]
        public void OnSkyConcord_TheBrainsRaycastNeverCallsABlockedWalkClear()
        {
            Vector3[] deck = DeckVertices();
            NavWorld world = NavWorld.GetDefaultWorld();
            var rng = new Unity.Mathematics.Random(NavProbe.Seed);
            int pairs = 0, unityBlocked = 0, oursBlocked = 0, wrongClear = 0, overCautious = 0;
            string firstWrong = null;
            for (int tries = 0; tries < 20000 && pairs < 2000; tries++)
            {
                Vector3 a = deck[rng.NextInt(deck.Length)], b = deck[rng.NextInt(deck.Length)];
                float d = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(b.x, 0f, b.z));
                if (d < 3f || d > 80f || Mathf.Abs(a.y - b.y) > 1f) continue;
                NavLocation start = world.MapLocation(a, Backend.Extents, Backend.AgentTypeId);
                if (!world.IsValid(start)) continue;
                pairs++;
                bool unity = NavMesh.Raycast(a, b, out _, NavMesh.AllAreas);
                bool ours = BrainMoveSystem.NavBlocked(world, start, b);
                if (unity) unityBlocked++;
                if (ours) oursBlocked++;
                if (unity && !ours) { wrongClear++; firstWrong ??= $"{a} -> {b} ({d:F1} m)"; }
                if (!unity && ours) overCautious++;
            }
            Debug.Log(FormattableString.Invariant($"[NavWorldTests] Concord raycasts: {pairs} pairs, Unity blocked {unityBlocked}, the brain's blocked {oursBlocked}, wrongly clear {wrongClear}, over-cautious {overCautious}"));
            Assert.Greater(pairs, 500, "pairs on the deck");
            Assert.AreEqual(0, wrongClear, $"the brain called {wrongClear} blocked walks clear, first {firstWrong}");

            // a start 3 m beside the deck (in the air past its edge, or inside a planter) is never "clear"
            Assert.IsTrue(NavMesh.FindClosestEdge(deck[0], out NavMeshHit edge, NavMesh.AllAreas), "an edge of the deck");
            Vector3 outside = edge.position - Inward(edge) * 3f;
            NavLocation off = world.MapLocation(outside, Backend.Extents, Backend.AgentTypeId);
            if (!world.IsValid(off)) Assert.IsTrue(BrainMoveSystem.NavBlocked(world, off, deck[1]), "an off-mesh start counts as blocked");
        }

        /// <summary>The way from a mesh edge into the walkable mesh (the edge's normal or its opposite, whichever lands on the mesh).</summary>
        static Vector3 Inward(NavMeshHit edge)
        {
            Vector3 n = new Vector3(edge.normal.x, 0f, edge.normal.z).normalized;
            return OnMesh(edge.position + n * 0.3f, 0.05f) ? n : -n;
        }

        /// <summary>A pair of deck points whose straight line Unity's raycast calls blocked but with a complete path round, 10-40 m apart.</summary>
        static bool DeckPairRound(Vector3[] deck, out Vector3 from, out Vector3 to)
        {
            var path = new NavMeshPath();
            for (int i = 0; i < deck.Length; i += 37)
            for (int j = i + 11; j < deck.Length; j += 53)
            {
                from = deck[i]; to = deck[j];
                float d = Vector3.Distance(from, to);
                if (d < 10f || d > 40f || !NavMesh.Raycast(from, to, out _, NavMesh.AllAreas)) continue;
                if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 3)
                    return true;
            }
            from = to = default;
            return false;
        }

        /// <summary>A walker whose straight line crosses something on the deck goes round it: every tick it stands on the mesh, and it arrives.</summary>
        [Test]
        public void OnSkyConcord_AWalkerGoesRoundWhatIsInTheWay_OnTheMeshEveryTick()
        {
            Vector3[] deck = DeckVertices();
            Assert.IsTrue(DeckPairRound(deck, out Vector3 from, out Vector3 to), "a deck pair with something in between");
            using var w = new BrainTestWorld(movement: true);
            w.Em.CreateSingleton(Backend);
            Entity b = w.Walker(from, 1, to, 1.4f);
            int off = 0;
            float3 firstOff = default;
            for (int tick = 0; tick < 60 * BrainTestWorld.TickRate; tick++)
            {
                w.Step();
                float3 p = w.Position(b);
                if (!OnMesh(p, 0.15f)) { if (off == 0) firstOff = p; off++; }
                if (math.distance(p.xz, ((float3)to).xz) <= 0.3f) break;
            }
            float3 there = w.Position(b);
            Assert.AreEqual(0, off, $"from {from} to {to}: {off} ticks off the mesh, first at {firstOff}");
            Assert.LessOrEqual(math.distance(there.xz, ((float3)to).xz), 0.3f, $"from {from} to {to}, ended at {there}");
        }

        /// <summary>Eight walkers squeezed onto one spot by a deck edge: the crowd's push sorts them out on the mesh, nobody goes over the edge.</summary>
        [Test]
        public void OnSkyConcord_ACrowdPushedAgainstAnEdgeStaysOnTheMesh()
        {
            Vector3[] deck = DeckVertices();
            Assert.IsTrue(NavMesh.FindClosestEdge(deck[deck.Length / 2], out NavMeshHit edge, NavMesh.AllAreas), "an edge of the deck");
            Vector3 inward = Inward(edge);
            Vector3 at = edge.position + inward * 0.3f;   // just inside the edge
            using var w = new BrainTestWorld(movement: true);
            w.Em.CreateSingleton(Backend);
            var crowd = new Entity[8];
            for (int i = 0; i < crowd.Length; i++)
                crowd[i] = w.Walker(at, (uint)(10 + i), at - inward * 4f, 1.4f);   // each wants to walk over the edge
            w.Step(5 * BrainTestWorld.TickRate);
            for (int i = 0; i < crowd.Length; i++)
                Assert.IsTrue(OnMesh(w.Position(crowd[i]), 0.15f), $"walker {i} at {w.Position(crowd[i])} left the deck (edge at {edge.position})");
        }

        static bool FarPair(out Vector3 from, out Vector3 to)
        {
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            var path = new NavMeshPath();
            for (int i = 0; i < mesh.vertices.Length; i += 97)
            for (int j = i + 13; j < mesh.vertices.Length; j += 131)
            {
                from = mesh.vertices[i]; to = mesh.vertices[j];
                float d = Vector3.Distance(from, to);
                if (d < 15f || d > 40f) continue;
                if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length >= 3)
                    return true;
            }
            from = to = default;
            return false;
        }
    }
}
