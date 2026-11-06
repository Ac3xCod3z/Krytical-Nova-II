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

        [Test]
        public void Zone01_NavWorldPaths_MatchCalculatePath()
        {
            NavProbe.Report r = NavProbe.Run(Backend);
            NavProbe.Write(r, Path.GetFullPath("Logs"));
            Assert.AreEqual(NavProbe.Pairs, r.Pairs, "pairs on the mesh");
            Assert.IsTrue(r.Passed, NavProbe.Summary(r));
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
