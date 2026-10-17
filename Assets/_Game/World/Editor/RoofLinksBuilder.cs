using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Roofs close enough to jump between are joined (owner, 17 Oct 2026: "if close enough, jumpable"). Each roof is
    /// a NavMesh island of its own; a run across to the next roof stopped at the edge. Where the edge of one roof is
    /// within a short gap and a little height of another, a two-way NavMeshLink joins them, so a route can be planned
    /// over the roofs; PointAndClickPlayer crosses each link with the real Jump. Longer gaps are still crossed by
    /// choosing Jump and clicking, as before. Run after the NavMesh bake: links are not baked.
    /// </summary>
    public static class RoofLinksBuilder
    {
        public const string RootName = "--- Roof Links ---";
        private const float MinimumRoofHeight = 2.5f; // above the ground: lower is a stall or a step
        private const float MaxGap = 4f;              // metres of air between the two edges
        private const float MaxRise = 3f;             // up or down
        private const float EdgeStep = 1f;            // samples along an edge
        private const float KeepEvery = 3f;           // at most one link per this much edge between two roofs
        private const float LinkWidth = 1f;
        private const float LinkCost = 3f;            // a walk round, when there is one, is chosen first

        [MenuItem("Krytical Nova/Zone/Roof Jump Links (Open Scene)")]
        private static void Menu()
        {
            int made = Build();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Roofs] {made} roof jump link(s).");
        }

        public static int Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject old in scene.GetRootGameObjects())
                if (old.name == RootName) Object.DestroyImmediate(old); // so old links do not join the islands we look at
            var root = new GameObject(RootName); // made in the open scene

            GameObject townRoot = GameObject.Find(StarterTownBuilder.TownRootName);
            town = townRoot != null ? townRoot.transform : null;
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            int groundMask = 1 << LayerMask.NameToLayer("Ground");
            var kept = new Dictionary<long, (float gap, Vector3 from, Vector3 to)>();
            var path = new NavMeshPath();
            foreach ((Vector3 a, Vector3 b, Vector3 inside) in BoundaryEdges(mesh))
            {
                Vector3 along = b - a;
                float length = along.magnitude;
                if (length < 0.2f) continue;
                Vector3 outward = Vector3.Cross(Vector3.up, along / length);
                if (Vector3.Dot(outward, inside - a) > 0f) outward = -outward;
                outward.y = 0f;
                outward.Normalize();
                for (float s = EdgeStep * 0.5f; s < length; s += EdgeStep)
                {
                    Vector3 from = a + along * (s / length);
                    if (!IsRoof(from, groundMask)) continue;
                    if (!FindAcross(from, outward, groundMask, path, out Vector3 to, out float gap)) continue;
                    long key = Key(from, to);
                    if (!kept.TryGetValue(key, out var best) || gap < best.gap) kept[key] = (gap, from, to);
                }
            }

            int made = 0;
            foreach (var link in kept.Values)
            {
                var holder = new GameObject("Roof Link");
                holder.transform.SetParent(root.transform, false);
                holder.transform.position = (link.from + link.to) * 0.5f;
                var navLink = holder.AddComponent<NavMeshLink>();
                navLink.startPoint = holder.transform.InverseTransformPoint(link.from);
                navLink.endPoint = holder.transform.InverseTransformPoint(link.to);
                navLink.width = LinkWidth;
                navLink.bidirectional = true;
                navLink.costModifier = LinkCost;
                navLink.area = 0;
                made++;
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return made;
        }

        private static Transform town;

        /// <summary>A walkable top on one of the town's own pieces (a building, not a rock outside), high off the ground.</summary>
        private static bool IsRoof(Vector3 point, int groundMask)
        {
            if (!Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 200f, groundMask) || point.y - ground.point.y < MinimumRoofHeight)
                return false;
            return town != null && Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit top, 1.5f, ~groundMask, QueryTriggerInteraction.Ignore)
                && top.collider.transform.IsChildOf(town);
        }

        /// <summary>The nearest other roof straight out from an edge, within the gap and the rise, with nothing in the way.</summary>
        private static bool FindAcross(Vector3 from, Vector3 outward, int groundMask, NavMeshPath path, out Vector3 to, out float gap)
        {
            to = default;
            gap = 0f;
            for (float d = 0.6f; d <= MaxGap + 1.2f; d += 0.3f)
            {
                Vector3 probe = from + outward * d;
                if (!NavMesh.SamplePosition(probe, out NavMeshHit hit, MaxRise, NavMesh.AllAreas)) continue;
                Vector3 flat = hit.position - probe;
                flat.y = 0f;
                if (flat.magnitude > 0.5f || Mathf.Abs(hit.position.y - from.y) > MaxRise) continue;
                if (!IsRoof(hit.position, groundMask)) continue;
                // Another island: no walk joins them (or only a long way round, which the link's cost leaves alone).
                if (NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) return false;
                Vector3 up = Vector3.up * 1.2f;
                if (Physics.Linecast(from + up, hit.position + up, ~groundMask, QueryTriggerInteraction.Ignore)) return false;
                Vector3 across = hit.position - from;
                across.y = 0f;
                gap = across.magnitude;
                if (gap > MaxGap + 1.2f) return false; // the edges' own margins add up to about a metre
                to = hit.position;
                return true;
            }
            return false;
        }

        /// <summary>Edges that belong to one triangle only: where walkable ground ends, with a point inside it.</summary>
        private static IEnumerable<(Vector3, Vector3, Vector3)> BoundaryEdges(NavMeshTriangulation mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] t = mesh.indices;
            // Vertices are repeated per polygon: weld by position so shared edges are found.
            var weld = new Dictionary<Vector3Int, int>();
            var id = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var cell = Vector3Int.RoundToInt(v[i] * 20f);
                if (!weld.TryGetValue(cell, out int w)) weld[cell] = w = i;
                id[i] = w;
            }
            var count = new Dictionary<long, int>();
            for (int i = 0; i < t.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    long k = EdgeKey(id[t[i + e]], id[t[i + (e + 1) % 3]]);
                    count[k] = count.TryGetValue(k, out int c) ? c + 1 : 1;
                }
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 centre = (v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f;
                for (int e = 0; e < 3; e++)
                {
                    int p = t[i + e], q = t[i + (e + 1) % 3];
                    if (count[EdgeKey(id[p], id[q])] == 1) yield return (v[p], v[q], centre);
                }
            }
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        /// <summary>One link per stretch of edge between the same two places.</summary>
        private static long Key(Vector3 from, Vector3 to)
        {
            int fx = Mathf.FloorToInt(from.x / KeepEvery), fz = Mathf.FloorToInt(from.z / KeepEvery);
            int tx = Mathf.FloorToInt(to.x / KeepEvery), tz = Mathf.FloorToInt(to.z / KeepEvery);
            // Either way round is the same link.
            long a = ((long)(fx & 0xFFFF) << 16) | (uint)(fz & 0xFFFF), b = ((long)(tx & 0xFFFF) << 16) | (uint)(tz & 0xFFFF);
            return a < b ? (a << 32) | b : (b << 32) | a;
        }
    }
}
