using System.IO;
using NUnit.Framework;
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
