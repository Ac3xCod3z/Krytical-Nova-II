using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.World.Surfaces
{
    /// <summary>
    /// Draws a <see cref="SurfaceGraph"/> in the world, for checking a bake by eye: steps along a face in cyan, corners in
    /// yellow, joins to the ground in green and to the top in magenta, and a <see cref="SurfaceRoute.Route"/> in white
    /// over it (the walks at either end in orange). Thin ribbons on its own renderers, no collider, no shadow; a test
    /// tool, never in a shipped scene.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Krytical Nova/World/Surface Graph Overlay")]
    public sealed class SurfaceGraphOverlay : MonoBehaviour
    {
        private const float GraphWidth = 0.035f;
        private const float RouteWidth = 0.12f;

        private static readonly Color FaceColor = new Color(0.1f, 0.85f, 1f, 1f);
        private static readonly Color CornerColor = new Color(1f, 0.85f, 0.1f, 1f);
        private static readonly Color GroundColor = new Color(0.2f, 1f, 0.3f, 1f);
        private static readonly Color TopColor = new Color(1f, 0.25f, 0.9f, 1f);
        private static readonly Color RouteColor = Color.white;
        private static readonly Color WalkColor = new Color(1f, 0.55f, 0.1f, 1f);

        private MeshFilter graphPart, routePart;
        private Material material;

        /// <summary>
        /// Draws the graph (and a route over it when given). A whole zone's graph is far too much to draw: only what lies
        /// within <paramref name="within"/> is drawn when it is given.
        /// </summary>
        public void Show(SurfaceGraph graph, SurfaceRoute.Route route = null, Bounds? within = null)
        {
            EnsureParts();
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            if (graph != null)
            {
                for (int i = 0; i < graph.NodeCount; i++)
                    for (int e = graph.edgeStart[i]; e < graph.edgeStart[i + 1]; e++)
                    {
                        if (within.HasValue && !within.Value.Contains(graph.points[i])) break;
                        int j = graph.edgeTo[e];
                        if (j < i) continue; // each pair once
                        Ribbon(vertices, colors, triangles, graph.points[i], graph.points[j], graph.normals[i], GraphWidth,
                            graph.edgeKind[e] == SurfaceGraph.EdgeKind.Corner ? CornerColor : FaceColor);
                    }
                foreach (SurfaceGraph.Join join in graph.joins)
                    if (!within.HasValue || within.Value.Contains(graph.points[join.node]))
                    Ribbon(vertices, colors, triangles, graph.points[join.node], join.navPoint, graph.normals[join.node], GraphWidth * 1.5f,
                        join.kind == SurfaceGraph.JoinKind.Ground ? GroundColor : TopColor);
            }
            Fill(graphPart.sharedMesh, vertices, colors, triangles);

            vertices.Clear(); colors.Clear(); triangles.Clear();
            if (graph != null && route != null)
            {
                var nodes = new Vector3[route.Nodes.Length];
                for (int i = 0; i < nodes.Length; i++) nodes[i] = graph.points[route.Nodes[i]];
                Polyline(vertices, colors, triangles, route.Approach, Vector3.up, WalkColor);
                if (route.Approach.Length > 0 && nodes.Length > 0) Polyline(vertices, colors, triangles, new[] { route.Approach[route.Approach.Length - 1], nodes[0] }, Vector3.up, RouteColor);
                for (int i = 1; i < nodes.Length; i++)
                    Ribbon(vertices, colors, triangles, nodes[i - 1], nodes[i], graph.normals[route.Nodes[i]], RouteWidth, RouteColor);
                if (route.Departure.Length > 0 && nodes.Length > 0) Polyline(vertices, colors, triangles, new[] { nodes[nodes.Length - 1], route.Departure[0] }, Vector3.up, RouteColor);
                Polyline(vertices, colors, triangles, route.Departure, Vector3.up, WalkColor);
            }
            Fill(routePart.sharedMesh, vertices, colors, triangles);
        }

        private void EnsureParts()
        {
            if (material == null)
                material = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave, renderQueue = 3500 };
            graphPart = graphPart != null ? graphPart : MakePart("Surface graph");
            routePart = routePart != null ? routePart : MakePart("Surface route");
        }

        private MeshFilter MakePart(string partName)
        {
            var part = new GameObject(partName) { hideFlags = HideFlags.DontSave };
            part.transform.SetParent(transform, false);
            var filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = partName + " (overlay)", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return filter;
        }

        private static void Polyline(List<Vector3> vertices, List<Color> colors, List<int> triangles, Vector3[] line, Vector3 facing, Color color)
        {
            if (line == null) return;
            for (int i = 1; i < line.Length; i++)
                Ribbon(vertices, colors, triangles, line[i - 1] + Vector3.up * 0.08f, line[i] + Vector3.up * 0.08f, facing, RouteWidth, color);
        }

        /// <summary>A flat strip from a to b lying in the plane whose normal is <paramref name="facing"/>, seen from both sides.</summary>
        private static void Ribbon(List<Vector3> vertices, List<Color> colors, List<int> triangles, Vector3 a, Vector3 b, Vector3 facing, float width, Color color)
        {
            Vector3 along = b - a;
            if (along.sqrMagnitude < 1e-6f) return;
            Vector3 side = Vector3.Cross(along, facing);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(along, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side = side.normalized * (width * 0.5f);
            int start = vertices.Count;
            vertices.Add(a - side); vertices.Add(a + side); vertices.Add(b + side); vertices.Add(b - side);
            for (int k = 0; k < 4; k++) colors.Add(color);
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            triangles.Add(start + 2); triangles.Add(start + 1); triangles.Add(start);
            triangles.Add(start + 3); triangles.Add(start + 2); triangles.Add(start);
        }

        private static void Fill(Mesh mesh, List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (graphPart != null && graphPart.sharedMesh != null) DestroyImmediate(graphPart.sharedMesh);
            if (routePart != null && routePart.sharedMesh != null) DestroyImmediate(routePart.sharedMesh);
            if (material != null) DestroyImmediate(material);
        }
    }
}
