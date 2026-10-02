using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using RipVertices.QuickDestruction.Runtime;

namespace RipVertices.QuickDestruction.Editor
{
    internal class BakeOutcome
    {
        public bool Success;
        public bool Cancelled;
        public string Error;
        public string PrefabPath;
        public string OutputFolder;
        public GeneratedManifest Manifest;
        public int FragmentCount;
        public int SecondaryFragmentCount;
        public int SecondaryBodyCount;
        public float TotalMass;
        public float Seconds;
        public GameObject SceneInstance;
        public List<string> Warnings = new List<string>();
    }

    /// <summary>
    /// Maps one object's local Bake progress into a stable whole-batch range. Keeping the
    /// item number in the native progress window is essential because synchronous mesh
    /// fracture temporarily prevents the EditorWindow itself from repainting.
    /// </summary>
    internal readonly struct BakeProgressContext
    {
        public readonly int ItemIndex;
        public readonly int ItemCount;

        public BakeProgressContext(int itemIndex, int itemCount)
        {
            ItemCount = Mathf.Max(1, itemCount);
            ItemIndex = Mathf.Clamp(itemIndex, 0, ItemCount - 1);
        }

        public string Title =>
            $"Quick Destruction - Batch Bake {ItemIndex + 1}/{ItemCount}";

        public float OverallProgress(float itemProgress)
        {
            return Mathf.Clamp01((ItemIndex + Mathf.Clamp01(itemProgress)) / ItemCount);
        }

        public string FormatMessage(string sourceName, string phase,
            double itemElapsedSeconds)
        {
            return $"Object {ItemIndex + 1} of {ItemCount}: " +
                   $"{(string.IsNullOrEmpty(sourceName) ? "Unnamed" : sourceName)}\n" +
                   $"{phase}  |  {Math.Max(0d, itemElapsedSeconds):0.0}s on this object";
        }
    }

    /// <summary>
    /// Editor-only, non-destructive source assembled from several scene MeshFilters. The
    /// proxy keeps the anchor's transform while every input mesh is transformed into that
    /// local space. Preview and Bake can therefore use the normal one-mesh pipeline and
    /// produce exactly the same whole-building partition without changing the authored
    /// modular hierarchy.
    /// </summary>
    internal sealed class CombinedMeshSource : IDisposable
    {
        public const int MaxPrimaryFragmentCount = 600;

        readonly Mesh m_mesh;
        readonly List<WorkMesh> m_moduleCollisionSources;
        bool m_disposed;

        public GameObject ProxyObject { get; }
        public GameObject Anchor { get; }
        public IReadOnlyList<GameObject> OriginalRoots { get; }
        public int MeshCount { get; }
        public int VertexCount => m_mesh != null ? m_mesh.vertexCount : 0;
        public int TriangleCount { get; }
        /// <summary>
        /// One transformed source per authored MeshFilter. The rendered/fractured proxy
        /// intentionally merges module seams, but intact collision must retain these
        /// boundaries so a room corner does not become one large convex triangle.
        /// </summary>
        public IReadOnlyList<WorkMesh> ModuleCollisionSources =>
            m_moduleCollisionSources;

        CombinedMeshSource(GameObject proxyObject, Mesh mesh, GameObject anchor,
            List<GameObject> originalRoots, List<WorkMesh> moduleCollisionSources,
            int meshCount, int triangleCount)
        {
            ProxyObject = proxyObject;
            m_mesh = mesh;
            Anchor = anchor;
            OriginalRoots = originalRoots;
            m_moduleCollisionSources = moduleCollisionSources ?? new List<WorkMesh>();
            MeshCount = meshCount;
            TriangleCount = triangleCount;
        }

        internal static bool TryCreate(IReadOnlyList<GameObject> selectedRoots,
            out CombinedMeshSource source, out string error,
            Func<string, float, bool> continueProgress = null)
        {
            source = null;
            error = null;
            // A generated Whole Building still links the authored roots that it disabled.
            // Always rebuild the temporary proxy from those roots when they are available;
            // using the previous merged Intact mesh would lose per-module collider bounds on
            // the next Bake and could recreate a convex triangle across a room corner.
            List<GameObject> roots = ResolveUnavailableGeneratedSources(
                NormalizeRoots(selectedRoots), preferOriginalSources: true);
            if (roots.Count == 0)
            {
                error = "Select one or more scene objects that contain a MeshFilter.";
                return false;
            }

            GameObject anchor = roots[0];
            for (int i = 1; i < roots.Count; i++)
            {
                if (roots[i].scene != anchor.scene)
                {
                    error = "A merged building source must belong to one scene. " +
                            "Move the selected modules into the same scene and try again.";
                    return false;
                }
            }

            var filters = new List<MeshFilter>();
            var seenFilters = new HashSet<MeshFilter>();
            for (int rootIndex = 0; rootIndex < roots.Count; rootIndex++)
            {
                GameObject root = roots[rootIndex];
                DestructibleRoot bakedRoot = root.GetComponent<DestructibleRoot>();
                GameObject filterRoot = bakedRoot != null && bakedRoot.IntactRoot != null
                    ? bakedRoot.IntactRoot
                    : root;
                MeshFilter[] nested = filterRoot.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < nested.Length; i++)
                {
                    MeshFilter filter = nested[i];
                    if (filter == null || filter.sharedMesh == null ||
                        !seenFilters.Add(filter))
                        continue;
                    filters.Add(filter);
                }
            }

            if (filters.Count == 0)
            {
                error = "The selected assembly contains no MeshFilter with a mesh.";
                return false;
            }

            GameObject proxy = null;
            Mesh combined = null;
            var readableMeshes = new List<Mesh>();
            var readableOwnership = new List<bool>();
            var simplifiedMeshes = new List<Mesh>();
            var materialMeshes = new List<Mesh>();
            var moduleCollisionSources = new List<WorkMesh>();
            try
            {
                proxy = new GameObject(anchor.name + "_WholeBuilding")
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = anchor.layer,
                    tag = anchor.tag
                };
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(
                    proxy, anchor.scene);
                proxy.transform.SetPositionAndRotation(
                    anchor.transform.position, anchor.transform.rotation);
                proxy.transform.localScale = anchor.transform.lossyScale;

                var materials = new List<Material>();
                var materialGroups = new List<List<CombineInstance>>();
                long totalVertices = 0;
                long combinedVertices = 0;
                for (int filterIndex = 0; filterIndex < filters.Count; filterIndex++)
                {
                    MeshFilter filter = filters[filterIndex];
                    if (continueProgress != null && !continueProgress(
                            $"Reading module {filterIndex + 1}/{filters.Count}: " +
                            filter.gameObject.name,
                            (float)filterIndex / Mathf.Max(1, filters.Count + 1)))
                    {
                        error = "Assembly source preparation was cancelled.";
                        return false;
                    }

                    MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null)
                    {
                        error = $"'{filter.gameObject.name}' has a MeshFilter but no " +
                                "MeshRenderer on the same object. Add the renderer or remove " +
                                "this module from the assembly selection.";
                        return false;
                    }

                    Mesh readable = ReadableMeshUtility.Acquire(filter.sharedMesh,
                        out bool ownsReadable, out string readableError);
                    if (readable == null)
                    {
                        error = $"Could not read module '{filter.gameObject.name}'. " +
                                readableError;
                        return false;
                    }
                    readableMeshes.Add(readable);
                    readableOwnership.Add(ownsReadable);
                    totalVertices += readable.vertexCount;

                    Material[] rendererMaterials = renderer.sharedMaterials;
                    Matrix4x4 toProxy = proxy.transform.worldToLocalMatrix *
                                               filter.transform.localToWorldMatrix;
                    Mesh simplifiedModule = null;
                    if (readable.subMeshCount == 1 &&
                        TryCreateSimplifiedPlanarRectangle(
                            readable, 0, out simplifiedModule))
                    {
                        simplifiedMeshes.Add(simplifiedModule);
                    }
                    moduleCollisionSources.Add(CreateTransformedModuleSource(
                        simplifiedModule != null ? simplifiedModule : readable, toProxy));
                    for (int submesh = 0; submesh < readable.subMeshCount; submesh++)
                    {
                        if (readable.GetTopology(submesh) != MeshTopology.Triangles)
                            continue;
                        Material material = rendererMaterials != null &&
                                            rendererMaterials.Length > 0
                            ? rendererMaterials[Mathf.Min(
                                submesh, rendererMaterials.Length - 1)]
                            : null;
                        int materialIndex = materials.IndexOf(material);
                        if (materialIndex < 0)
                        {
                            materialIndex = materials.Count;
                            materials.Add(material);
                            materialGroups.Add(new List<CombineInstance>());
                        }
                        Mesh combineMesh = readable;
                        int combineSubmesh = submesh;
                        if (simplifiedModule != null)
                        {
                            combineMesh = simplifiedModule;
                            combineSubmesh = 0;
                        }
                        else if (TryCreateSimplifiedPlanarRectangle(
                                 readable, submesh, out Mesh simplified))
                        {
                            simplifiedMeshes.Add(simplified);
                            combineMesh = simplified;
                            combineSubmesh = 0;
                        }
                        materialGroups[materialIndex].Add(new CombineInstance
                        {
                            mesh = combineMesh,
                            subMeshIndex = combineSubmesh,
                            transform = toProxy
                        });
                    }
                }

                if (materialGroups.Count == 0)
                {
                    error = "The selected assembly contains no triangle submeshes.";
                    return false;
                }
                if (continueProgress != null && !continueProgress(
                        $"Combining {filters.Count} modules into one fracture source...", 0.9f))
                {
                    error = "Assembly source preparation was cancelled.";
                    return false;
                }

                var finalCombine = new List<CombineInstance>(materialGroups.Count);
                for (int materialIndex = 0;
                     materialIndex < materialGroups.Count; materialIndex++)
                {
                    var materialMesh = new Mesh
                    {
                        name = $"{anchor.name} [QD Material {materialIndex}]",
                        hideFlags = HideFlags.HideAndDontSave,
                        indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
                    };
                    materialMesh.CombineMeshes(
                        materialGroups[materialIndex].ToArray(), true, true, false);
                    materialMeshes.Add(materialMesh);
                    // What the combined mesh will really hold. CombineMeshes copies a
                    // source's whole vertex buffer once per CombineInstance, and the loop
                    // above hands it one instance per submesh, so a module carrying several
                    // materials contributes its vertices several times over.
                    combinedVertices += materialMesh.vertexCount;
                    finalCombine.Add(new CombineInstance
                    {
                        mesh = materialMesh,
                        subMeshIndex = 0,
                        transform = Matrix4x4.identity
                    });
                }

                combined = new Mesh
                {
                    name = anchor.name + " [QD Whole Building Source]",
                    hideFlags = HideFlags.HideAndDontSave,
                    // Sized from the real combined total, not from the sum of the source
                    // meshes. Those were counted one per module while the combine receives
                    // one instance per submesh, so a multi-material building could be handed
                    // a 16-bit index buffer for a mesh that needs more than 65535 indices.
                    indexFormat = combinedVertices > 65000
                        ? UnityEngine.Rendering.IndexFormat.UInt32
                        : UnityEngine.Rendering.IndexFormat.UInt16
                };
                combined.CombineMeshes(finalCombine.ToArray(), false, false, false);
                combined.RecalculateBounds();
                if (combined.normals == null ||
                    combined.normals.Length != combined.vertexCount)
                    combined.RecalculateNormals();

                var proxyFilter = proxy.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = combined;
                var proxyRenderer = proxy.AddComponent<MeshRenderer>();
                proxyRenderer.sharedMaterials = materials.ToArray();
                proxyRenderer.forceRenderingOff = true;

                int combinedTriangles = 0;
                for (int submesh = 0; submesh < combined.subMeshCount; submesh++)
                    combinedTriangles += (int)(combined.GetIndexCount(submesh) / 3L);
                source = new CombinedMeshSource(proxy, combined, anchor, roots,
                    moduleCollisionSources, filters.Count, combinedTriangles);
                proxy = null;
                combined = null;
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not combine the selected building modules: " +
                        exception.Message;
                return false;
            }
            finally
            {
                for (int i = 0; i < readableMeshes.Count; i++)
                    ReadableMeshUtility.Release(readableMeshes[i], readableOwnership[i]);
                for (int i = 0; i < simplifiedMeshes.Count; i++)
                    if (simplifiedMeshes[i] != null)
                        UnityEngine.Object.DestroyImmediate(simplifiedMeshes[i]);
                for (int i = 0; i < materialMeshes.Count; i++)
                    if (materialMeshes[i] != null)
                        UnityEngine.Object.DestroyImmediate(materialMeshes[i]);
                if (proxy != null) UnityEngine.Object.DestroyImmediate(proxy);
                if (combined != null) UnityEngine.Object.DestroyImmediate(combined);
            }
        }

        static WorkMesh CreateTransformedModuleSource(Mesh mesh, Matrix4x4 transform)
        {
            WorkMesh source = WorkMesh.FromMesh(mesh);
            Matrix4x4 normalTransform = transform.inverse.transpose;
            for (int i = 0; i < source.Positions.Count; i++)
                source.Positions[i] = transform.MultiplyPoint3x4(source.Positions[i]);
            for (int i = 0; i < source.Normals.Count; i++)
            {
                Vector3 normal = normalTransform.MultiplyVector(source.Normals[i]);
                source.Normals[i] = normal.sqrMagnitude > 1e-16f
                    ? normal.normalized
                    : Vector3.up;
            }

            // Mesh.CombineMeshes mirrors winding when a module has a negative scale. The
            // parallel collision source must do the same or its generated shell points in
            // the opposite direction from the fracture source.
            if (transform.determinant < 0f)
            {
                for (int submesh = 0; submesh < source.Submeshes.Length; submesh++)
                {
                    List<int> triangles = source.Submeshes[submesh];
                    for (int i = 0; i + 2 < triangles.Count; i += 3)
                    {
                        int swap = triangles[i + 1];
                        triangles[i + 1] = triangles[i + 2];
                        triangles[i + 2] = swap;
                    }
                }
            }
            return source;
        }

        /// <summary>
        /// Removes only redundant grid triangulation from a completely filled planar
        /// rectangle. The conservative attribute checks deliberately leave arbitrary
        /// panels, holes, warped UVs and authored vertex shading untouched.
        /// </summary>
        internal static bool TryCreateSimplifiedPlanarRectangle(Mesh source, int submesh,
            out Mesh simplified)
        {
            simplified = null;
            if (source == null || submesh < 0 || submesh >= source.subMeshCount ||
                source.GetTopology(submesh) != MeshTopology.Triangles)
                return false;

            int[] triangles = source.GetTriangles(submesh);
            if (triangles == null || triangles.Length <= 6) return false;
            Vector3[] vertices = source.vertices;
            if (vertices == null || vertices.Length < 4) return false;

            var referenced = new HashSet<int>();
            Vector3 minimum = new Vector3(float.PositiveInfinity,
                float.PositiveInfinity, float.PositiveInfinity);
            Vector3 maximum = new Vector3(float.NegativeInfinity,
                float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < triangles.Length; i++)
            {
                int index = triangles[i];
                if (index < 0 || index >= vertices.Length) return false;
                if (!referenced.Add(index)) continue;
                minimum = Vector3.Min(minimum, vertices[index]);
                maximum = Vector3.Max(maximum, vertices[index]);
            }

            Vector3 size = maximum - minimum;
            float largest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (largest <= 1e-6f) return false;
            int flatAxis = size.x <= size.y && size.x <= size.z
                ? 0 : size.y <= size.z ? 1 : 2;
            float flatSize = Axis(size, flatAxis);
            float planeTolerance = Mathf.Max(1e-5f, largest * 1e-5f);
            if (flatSize > planeTolerance) return false;
            int uAxis = flatAxis == 0 ? 1 : 0;
            int vAxis = flatAxis == 2 ? 1 : 2;
            if (flatAxis == 1) vAxis = 2;
            float minU = Axis(minimum, uAxis);
            float maxU = Axis(maximum, uAxis);
            float minV = Axis(minimum, vAxis);
            float maxV = Axis(maximum, vAxis);
            float width = maxU - minU;
            float height = maxV - minV;
            if (width <= planeTolerance || height <= planeTolerance) return false;

            Vector3 faceNormal = Vector3.zero;
            double coveredArea = 0d;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude <= planeTolerance * planeTolerance) continue;
                Vector3 normal = cross.normalized;
                if (faceNormal == Vector3.zero) faceNormal = normal;
                else if (Vector3.Dot(faceNormal, normal) < 0.9999f) return false;
                double twiceArea = Math.Abs(
                    (Axis(b, uAxis) - Axis(a, uAxis)) *
                    (Axis(c, vAxis) - Axis(a, vAxis)) -
                    (Axis(c, uAxis) - Axis(a, uAxis)) *
                    (Axis(b, vAxis) - Axis(a, vAxis)));
                coveredArea += twiceArea * 0.5d;
            }
            if (faceNormal == Vector3.zero) return false;
            double rectangleArea = (double)width * height;
            if (Math.Abs(coveredArea - rectangleArea) >
                Math.Max(1e-6d, rectangleArea * 0.0001d))
                return false;

            int[] corners = { -1, -1, -1, -1 };
            foreach (int index in referenced)
            {
                Vector3 point = vertices[index];
                float u = Axis(point, uAxis);
                float v = Axis(point, vAxis);
                if (Mathf.Abs(u - minU) <= planeTolerance &&
                    Mathf.Abs(v - minV) <= planeTolerance) corners[0] = index;
                else if (Mathf.Abs(u - maxU) <= planeTolerance &&
                         Mathf.Abs(v - minV) <= planeTolerance) corners[1] = index;
                else if (Mathf.Abs(u - maxU) <= planeTolerance &&
                         Mathf.Abs(v - maxV) <= planeTolerance) corners[2] = index;
                else if (Mathf.Abs(u - minU) <= planeTolerance &&
                         Mathf.Abs(v - maxV) <= planeTolerance) corners[3] = index;
            }
            for (int i = 0; i < corners.Length; i++)
                if (corners[i] < 0) return false;

            Vector3[] normals = source.normals;
            if (normals != null && normals.Length > 0)
            {
                if (normals.Length != vertices.Length) return false;
                Vector3 normal = normals[corners[0]].normalized;
                foreach (int index in referenced)
                    if (Vector3.Dot(normal, normals[index].normalized) < 0.9999f)
                        return false;
            }
            Vector4[] tangents = source.tangents;
            if (tangents != null && tangents.Length > 0)
            {
                if (tangents.Length != vertices.Length) return false;
                Vector3 tangent = new Vector3(tangents[corners[0]].x,
                    tangents[corners[0]].y, tangents[corners[0]].z).normalized;
                float handedness = tangents[corners[0]].w;
                foreach (int index in referenced)
                {
                    var current = new Vector3(tangents[index].x,
                        tangents[index].y, tangents[index].z).normalized;
                    if (Vector3.Dot(tangent, current) < 0.9999f ||
                        Mathf.Abs(handedness - tangents[index].w) > 1e-4f)
                        return false;
                }
            }

            var uvChannels = new List<Vector4>[8];
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                var values = new List<Vector4>();
                source.GetUVs(channel, values);
                if (values.Count == 0) continue;
                if (values.Count != vertices.Length ||
                    !IsBilinearAttribute(values, referenced, corners, vertices,
                        uAxis, vAxis, minU, maxU, minV, maxV))
                    return false;
                uvChannels[channel] = values;
            }

            Color32[] colors = source.colors32;
            if (colors != null && colors.Length > 0)
            {
                if (colors.Length != vertices.Length ||
                    !IsBilinearColor(colors, referenced, corners, vertices,
                        uAxis, vAxis, minU, maxU, minV, maxV))
                    return false;
            }

            simplified = new Mesh
            {
                name = source.name + " [QD Planar Rectangle]",
                hideFlags = HideFlags.HideAndDontSave
            };
            var cornerVertices = new Vector3[4];
            for (int i = 0; i < 4; i++) cornerVertices[i] = vertices[corners[i]];
            simplified.vertices = cornerVertices;
            Vector3 generatedNormal = Vector3.Cross(
                cornerVertices[1] - cornerVertices[0],
                cornerVertices[2] - cornerVertices[0]);
            simplified.triangles = Vector3.Dot(generatedNormal, faceNormal) >= 0f
                ? new[] { 0, 1, 2, 0, 2, 3 }
                : new[] { 0, 2, 1, 0, 3, 2 };
            if (normals != null && normals.Length == vertices.Length)
            {
                var cornerNormals = new Vector3[4];
                for (int i = 0; i < 4; i++) cornerNormals[i] = normals[corners[i]];
                simplified.normals = cornerNormals;
            }
            else
            {
                simplified.RecalculateNormals();
            }
            if (tangents != null && tangents.Length == vertices.Length)
            {
                var cornerTangents = new Vector4[4];
                for (int i = 0; i < 4; i++) cornerTangents[i] = tangents[corners[i]];
                simplified.tangents = cornerTangents;
            }
            for (int channel = 0; channel < uvChannels.Length; channel++)
            {
                if (uvChannels[channel] == null) continue;
                var cornerValues = new List<Vector4>(4);
                for (int i = 0; i < 4; i++)
                    cornerValues.Add(uvChannels[channel][corners[i]]);
                simplified.SetUVs(channel, cornerValues);
            }
            if (colors != null && colors.Length == vertices.Length)
            {
                var cornerColors = new Color32[4];
                for (int i = 0; i < 4; i++) cornerColors[i] = colors[corners[i]];
                simplified.colors32 = cornerColors;
            }
            simplified.RecalculateBounds();
            return true;
        }

        static float Axis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        static bool IsBilinearAttribute(IReadOnlyList<Vector4> values,
            HashSet<int> referenced, int[] corners, Vector3[] positions,
            int uAxis, int vAxis, float minU, float maxU, float minV, float maxV)
        {
            // Two triangles preserve an affine attribute field. A genuinely bilinear
            // warp depends on the original grid and must keep that triangulation.
            Vector4 crossTerm = values[corners[0]] + values[corners[2]] -
                                values[corners[1]] - values[corners[3]];
            if (crossTerm.sqrMagnitude > 1e-7f) return false;
            foreach (int index in referenced)
            {
                float u = Mathf.InverseLerp(minU, maxU, Axis(positions[index], uAxis));
                float v = Mathf.InverseLerp(minV, maxV, Axis(positions[index], vAxis));
                Vector4 expected = Vector4.Lerp(
                    Vector4.Lerp(values[corners[0]], values[corners[1]], u),
                    Vector4.Lerp(values[corners[3]], values[corners[2]], u), v);
                if ((values[index] - expected).sqrMagnitude > 1e-7f) return false;
            }
            return true;
        }

        static bool IsBilinearColor(Color32[] values, HashSet<int> referenced,
            int[] corners, Vector3[] positions, int uAxis, int vAxis,
            float minU, float maxU, float minV, float maxV)
        {
            Vector4 crossTerm = (Vector4)(Color)values[corners[0]] + (Vector4)(Color)values[corners[2]] -
                                (Vector4)(Color)values[corners[1]] - (Vector4)(Color)values[corners[3]];
            if (crossTerm.sqrMagnitude > 1e-5f) return false;
            foreach (int index in referenced)
            {
                float u = Mathf.InverseLerp(minU, maxU, Axis(positions[index], uAxis));
                float v = Mathf.InverseLerp(minV, maxV, Axis(positions[index], vAxis));
                Color expected = Color.Lerp(
                    Color.Lerp(values[corners[0]], values[corners[1]], u),
                    Color.Lerp(values[corners[3]], values[corners[2]], u), v);
                Color actual = values[index];
                Vector4 delta = (Vector4)actual - (Vector4)expected;
                if (delta.sqrMagnitude > 1e-5f) return false;
            }
            return true;
        }

        internal static List<GameObject> NormalizeRoots(
            IReadOnlyList<GameObject> selectedRoots)
        {
            var candidates = new List<GameObject>();
            if (selectedRoots == null) return candidates;
            for (int i = 0; i < selectedRoots.Count; i++)
            {
                GameObject item = selectedRoots[i];
                if (item != null && item.scene.IsValid() && !candidates.Contains(item))
                    candidates.Add(item);
            }

            var roots = new List<GameObject>();
            for (int i = 0; i < candidates.Count; i++)
            {
                bool nestedUnderSelection = false;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (i == j) continue;
                    if (candidates[i].transform.IsChildOf(candidates[j].transform))
                    {
                        nestedUnderSelection = true;
                        break;
                    }
                }
                if (!nestedUnderSelection) roots.Add(candidates[i]);
            }
            return roots;
        }

        /// <summary>
        /// Whole Building outputs made by the affected editor version can have an Intact
        /// MeshFilter whose transient proxy serialized as fileID 0. A placed instance still
        /// records the authored modules it disabled, so use those modules as the repair source
        /// instead of trying to fracture fragments or returning QD-ASSEMBLY-001.
        /// </summary>
        internal static List<GameObject> ResolveUnavailableGeneratedSources(
            List<GameObject> roots, bool preferOriginalSources = false)
        {
            var resolved = new List<GameObject>();
            for (int i = 0; i < roots.Count; i++)
            {
                GameObject root = roots[i];
                DestructibleRoot bakedRoot = root != null
                    ? root.GetComponent<DestructibleRoot>()
                    : null;
                if (bakedRoot != null &&
                    (preferOriginalSources || !HasUsableIntactMesh(bakedRoot)))
                {
                    int recoveredBefore = resolved.Count;
                    if (bakedRoot.OriginalSourceObjects != null)
                    {
                        for (int sourceIndex = 0;
                             sourceIndex < bakedRoot.OriginalSourceObjects.Count;
                             sourceIndex++)
                        {
                            AddRecoverableOriginal(resolved,
                                bakedRoot.OriginalSourceObjects[sourceIndex], root);
                        }
                    }
                    AddRecoverableOriginal(resolved,
                        bakedRoot.OriginalSourceObject, root);
                    if (resolved.Count > recoveredBefore) continue;
                }

                if (root != null && !resolved.Contains(root)) resolved.Add(root);
            }
            return NormalizeRoots(resolved);
        }

        internal static bool HasUsableIntactMesh(DestructibleRoot root)
        {
            if (root == null) return false;
            GameObject intact = root.IntactRoot;
            if (intact == null)
            {
                Transform intactChild = root.transform.Find("Intact");
                if (intactChild != null) intact = intactChild.gameObject;
            }
            if (intact == null) return false;

            MeshFilter[] filters = intact.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i] != null && filters[i].sharedMesh != null)
                    return true;
            return false;
        }

        static void AddRecoverableOriginal(List<GameObject> resolved,
            GameObject original, GameObject generatedRoot)
        {
            if (original == null || original == generatedRoot ||
                !original.scene.IsValid() || generatedRoot == null ||
                original.scene != generatedRoot.scene || resolved.Contains(original))
                return;
            resolved.Add(original);
        }

        public void Dispose()
        {
            if (m_disposed) return;
            m_disposed = true;
            if (ProxyObject != null) UnityEngine.Object.DestroyImmediate(ProxyObject);
            if (m_mesh != null) UnityEngine.Object.DestroyImmediate(m_mesh);
        }
    }

    internal sealed class SecondaryBakeData
    {
        public int ParentIndex;
        public Vector3 ParentCentroid;
        public readonly List<WorkMesh> WorkMeshes = new List<WorkMesh>();
        public readonly List<float> Volumes = new List<float>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        // Render-only interior chips/splinters. Kept separate from the closed child
        // mesh so physics and topology remain identical to the solver output.
        public readonly List<Mesh> InteriorScatterMeshes = new List<Mesh>();
        public readonly List<List<Mesh>> DynamicColliderMeshes = new List<List<Mesh>>();
        public readonly List<Vector3> LocalPositions = new List<Vector3>();
    }

    /// <summary>
    /// Transactional bake: snapshot -> fracture -> physics -> output. The source prefab and
    /// mesh are never modified. All generated assets are owned by a manifest; rebake replaces
    /// only manifest entries and cancel removes only session files.
    /// </summary>
    internal static class BakePipeline
    {
        internal const int DeferredFragmentPhysicsThreshold = 96;

        internal static bool ShouldDeferFragmentPhysics(bool mergedAssembly,
            int primaryFragmentCount, IntactBodyMode intactBodyMode) =>
            mergedAssembly &&
            primaryFragmentCount >= DeferredFragmentPhysicsThreshold &&
            intactBodyMode != IntactBodyMode.Dynamic;

        public const string GeneratedRootFolder = "Assets/QuickDestruction/Generated";
        public const string QuickProfileFolder = "Assets/QuickDestruction/QuickProfiles";
        const string DemoProfileFolder = "Assets/QuickDestruction/Demo/Demo_Profiles";
        const int AssetStorePathLengthLimit = 140;
        const int GeneratedAssetNameLimit = 32;
        const string LegacyGeneratedRootFolder = "Assets/QuickDestruction_Generated";
        const string LegacyQuickProfileFolder = GeneratedRootFolder + "/QuickProfiles";
        const double ProgressDisplayDelaySeconds = 0.75d;
        const double ProgressRefreshIntervalSeconds = 1d;
        sealed class LegacyMigrationState
        {
            public bool CheckedLegacyGeneratedFolder;
            public bool CheckedQuickProfileFolder;
            public bool CheckedAssetStorePathMigration;
        }

        static readonly LegacyMigrationState s_legacyMigration =
            new LegacyMigrationState();

        [InitializeOnLoadMethod]
        static void QueueLegacyGeneratedFolderMigration()
        {
            EditorApplication.delayCall += EnsureGeneratedRoot;
        }

        /// <summary>
        /// Creates the package-local output folder and moves legacy generated output once.
        /// Quick Profiles are migrated outside Generated so moving or cleaning bake output
        /// cannot invalidate profile references. AssetDatabase.MoveAsset preserves GUIDs.
        /// </summary>
        internal static void EnsureGeneratedRoot()
        {
            if (!s_legacyMigration.CheckedLegacyGeneratedFolder)
            {
                s_legacyMigration.CheckedLegacyGeneratedFolder = true;
                if (AssetDatabase.IsValidFolder(LegacyGeneratedRootFolder) &&
                    !AssetDatabase.IsValidFolder(GeneratedRootFolder))
                {
                    string error = AssetDatabase.MoveAsset(
                        LegacyGeneratedRootFolder, GeneratedRootFolder);
                    if (!string.IsNullOrEmpty(error))
                        UnityEngine.Debug.LogWarning(
                            "[Quick Destruction] Could not move legacy generated assets: " + error);
                }
            }

            EnsureFolder(GeneratedRootFolder);
            EnsureQuickProfileRoot();
            EnsureCompactAssetStorePaths();
        }

        internal static void EnsureQuickProfileRoot()
        {
            EnsureFolder(QuickProfileFolder);
            if (s_legacyMigration.CheckedQuickProfileFolder) return;
            s_legacyMigration.CheckedQuickProfileFolder = true;

            if (!AssetDatabase.IsValidFolder(LegacyQuickProfileFolder)) return;
            string[] profileGuids = AssetDatabase.FindAssets(
                "t:DestructionProfile", new[] { LegacyQuickProfileFolder });
            foreach (string guid in profileGuids)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(sourcePath) ||
                    !sourcePath.StartsWith(LegacyQuickProfileFolder + "/",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                string destinationPath = QuickProfileFolder + "/" +
                    Path.GetFileName(sourcePath);
                if (AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
                    destinationPath = AssetDatabase.GenerateUniqueAssetPath(destinationPath);

                string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
                if (!string.IsNullOrEmpty(error))
                    UnityEngine.Debug.LogWarning(
                        "[Quick Destruction] Could not move Quick Profile out of Generated: " +
                        error);
            }
        }

        /// <summary>
        /// Keeps package paths below the Asset Store validator's 140-character limit.
        /// Generated output is identified by its folder GUID, so compacting it retains a
        /// stable name and <see cref="AssetDatabase.MoveAsset"/> preserves all prefab and
        /// scene references. Demo bakes use the same treatment for their owned folders.
        /// </summary>
        static void EnsureCompactAssetStorePaths()
        {
            if (s_legacyMigration.CheckedAssetStorePathMigration) return;
            s_legacyMigration.CheckedAssetStorePathMigration = true;

            int moved = CompactOutputFolders(GeneratedRootFolder, "g");
            moved += CompactOutputFolders(DemoProfileFolder, "d");
            if (moved > 0) AssetDatabase.SaveAssets();
        }

        static int CompactOutputFolders(string root, string prefix)
        {
            if (!AssetDatabase.IsValidFolder(root)) return 0;
            int moved = 0;
            foreach (string sourceFolder in AssetDatabase.GetSubFolders(root))
            {
                if (!PathNeedsCompaction(sourceFolder)) continue;
                string guid = AssetDatabase.AssetPathToGUID(sourceFolder);
                if (string.IsNullOrEmpty(guid)) continue;
                string destinationFolder = root + "/" + prefix + "_" +
                    guid.Substring(0, Mathf.Min(12, guid.Length));
                if (string.Equals(sourceFolder, destinationFolder,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                if (AssetDatabase.IsValidFolder(destinationFolder))
                {
                    UnityEngine.Debug.LogWarning(
                        "[Quick Destruction] Could not compact generated folder because " +
                        "its stable destination already exists: " + destinationFolder);
                    continue;
                }

                string error = AssetDatabase.MoveAsset(sourceFolder, destinationFolder);
                if (!string.IsNullOrEmpty(error))
                {
                    UnityEngine.Debug.LogWarning(
                        "[Quick Destruction] Could not compact generated folder: " + error);
                    continue;
                }
                RewriteManifestEntryPaths(destinationFolder, sourceFolder);
                moved++;
            }
            return moved;
        }

        static bool PathNeedsCompaction(string folder)
        {
            if (PathExceedsAssetStoreLimit(folder)) return true;
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (PathExceedsAssetStoreLimit(assetPath)) return true;
            }
            return false;
        }

        static bool PathExceedsAssetStoreLimit(string assetPath)
        {
            const string assetsPrefix = "Assets/";
            return !string.IsNullOrEmpty(assetPath) &&
                   assetPath.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase) &&
                   assetPath.Length - assetsPrefix.Length >= AssetStorePathLengthLimit;
        }

        static void RewriteManifestEntryPaths(string destinationFolder,
            string sourceFolder)
        {
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:GeneratedManifest", new[] { destinationFolder }))
            {
                GeneratedManifest manifest = AssetDatabase.LoadAssetAtPath<GeneratedManifest>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (manifest == null || manifest.Entries == null) continue;
                bool changed = false;
                foreach (GeneratedManifest.Entry entry in manifest.Entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.AssetPath))
                        continue;
                    string destinationPath;
                    if (entry.AssetPath.StartsWith(sourceFolder + "/",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        destinationPath = destinationFolder +
                            entry.AssetPath.Substring(sourceFolder.Length);
                    }
                    else
                    {
                        // A few older demo manifests were copied from Generated into
                        // Demo_Profiles and retained their former root in AssetPath.
                        // Resolve their own direct child by filename while it exists,
                        // rather than carrying that stale path into the compact folder.
                        destinationPath = destinationFolder + "/" +
                            Path.GetFileName(entry.AssetPath);
                        if (AssetDatabase.LoadMainAssetAtPath(destinationPath) == null)
                            continue;
                    }
                    if (string.Equals(entry.AssetPath, destinationPath,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    entry.AssetPath = destinationPath;
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(manifest);
            }
        }

        /// <summary>
        /// Output folder for one source/profile pair. The short suffix comes from the
        /// source mesh asset GUID (or an identity hash for non-asset meshes) so
        /// different props that share a name never overwrite each other's output.
        /// </summary>
        public static string GetOutputFolder(GameObject source, DestructionProfile profile)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            string safeName = GetGeneratedAssetName(source.name);
            Mesh mesh = null;
            Material[] materials = null;
            Vector3 meshWorldScale = Vector3.one;
            // A generated destructible keeps its source mesh on the (possibly inactive)
            // IntactRoot; a plain lookup could return the first fragment instead and
            // the folder suffix would change with the edit view.
            var bakedRoot = source.GetComponentInParent<DestructibleRoot>();
            if (bakedRoot != null)
            {
                string existingPrefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        bakedRoot.gameObject);
                string existingManifestPath =
                    FindGeneratedManifestPath(existingPrefabPath, profile);
                if (!string.IsNullOrEmpty(existingManifestPath))
                {
                    return Path.GetDirectoryName(existingPrefabPath)
                        ?.Replace('\\', '/') ?? GeneratedRootFolder;
                }

                GameObject intact = bakedRoot.IntactRoot;
                if (intact == null)
                {
                    Transform intactChild = bakedRoot.transform.Find("Intact");
                    if (intactChild != null) intact = intactChild.gameObject;
                }
                var intactFilter = intact != null
                    ? intact.GetComponentInChildren<MeshFilter>(true)
                    : null;
                if (intactFilter != null)
                {
                    mesh = intactFilter.sharedMesh;
                    materials = GetSourceMaterials(intactFilter);
                    meshWorldScale = intactFilter.transform.lossyScale;
                }
            }
            else
            {
                var filter = MeshAnalyzer.ResolveSourceMeshFilter(source);
                if (filter != null)
                {
                    mesh = filter.sharedMesh;
                    materials = GetSourceMaterials(filter);
                    meshWorldScale = filter.transform.lossyScale;
                }
            }
            return $"{GeneratedRootFolder}/{safeName}_p{GetProfileIdentitySuffix(profile)}_" +
                   $"{GetMeshIdentitySuffix(mesh)}" +
                   GetMaterialIdentitySuffix(materials) +
                   GetShellScaleIdentitySuffix(profile, meshWorldScale);
        }

        static string GetGeneratedAssetName(string sourceName)
        {
            string safeName = MakeSafeFileName(GetDestructibleBaseName(sourceName));
            return safeName.Length <= GeneratedAssetNameLimit
                ? safeName
                : safeName.Substring(0, GeneratedAssetNameLimit);
        }

        static string GetProfileIdentitySuffix(DestructionProfile profile)
        {
            if (profile == null) return "noprofile";
            string path = AssetDatabase.GetAssetPath(profile);
            string guid = string.IsNullOrEmpty(path) ? string.Empty :
                AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(guid))
                return guid.Substring(0, Mathf.Min(8, guid.Length));

            unchecked
            {
                uint hash = 2166136261u;
                string name = profile.name ?? string.Empty;
                foreach (char character in name)
                    hash = (hash ^ character) * 16777619;
                return hash.ToString("x8");
            }
        }

        /// <summary>
        /// Hollow Shell thickness is authored in scene units, so the same mesh at two
        /// different scales solves to two different shells. That is already an input to
        /// <see cref="ComputeFractureHash"/>; without it here as well, both bakes would
        /// share one folder and the second would silently overwrite the first.
        /// </summary>
        static string GetShellScaleIdentitySuffix(DestructionProfile profile,
            Vector3 meshWorldScale)
        {
            if (profile == null || !profile.Solidify) return string.Empty;
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(Mathf.Abs(meshWorldScale.x) * 10000f);
                h = h * 31 + Mathf.RoundToInt(Mathf.Abs(meshWorldScale.y) * 10000f);
                h = h * 31 + Mathf.RoundToInt(Mathf.Abs(meshWorldScale.z) * 10000f);
                return "_s" + ((uint)h).ToString("x8");
            }
        }

        static Material[] GetSourceMaterials(MeshFilter filter)
        {
            var renderer = filter != null
                ? filter.GetComponent<MeshRenderer>()
                : null;
            return renderer != null ? renderer.sharedMaterials : null;
        }

        /// <summary>
        /// Distinguishes two sources that share a mesh but not its surfaces.
        ///
        /// The folder name is the bake's identity, and reusing it for an equal mesh is the
        /// point: a modular kit places the same panel forty times and bakes it once. What
        /// the mesh alone cannot say is which materials that panel wears. A Root + Nested
        /// batch over a building whose walls, floors and roof are all the same plane mesh
        /// therefore wrote every one of them into a single folder, and each bake overwrote
        /// the last: every instance ended up rendering whichever surfaces happened to be
        /// baked last. Equal mesh AND equal materials still share one bake.
        /// </summary>
        static string GetMaterialIdentitySuffix(Material[] materials)
        {
            if (materials == null || materials.Length == 0) return string.Empty;
            bool anyAssigned = false;
            unchecked
            {
                int h = 17;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                    {
                        h = h * 31;
                        continue;
                    }
                    anyAssigned = true;
                    string path = AssetDatabase.GetAssetPath(material);
                    h = h * 31 + (!string.IsNullOrEmpty(path)
                        ? AssetDatabase.AssetPathToGUID(path).GetHashCode()
                        : material.name.GetHashCode());
                }
                return anyAssigned ? "_m" + ((uint)h).ToString("x8") : string.Empty;
            }
        }

        internal static string GetManifestPath(GameObject source,
            DestructionProfile profile)
        {
            if (source == null || profile == null) return null;
            DestructibleRoot bakedRoot =
                source.GetComponentInParent<DestructibleRoot>();
            if (bakedRoot != null)
            {
                string prefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        bakedRoot.gameObject);
                string existing = FindGeneratedManifestPath(prefabPath, profile);
                if (!string.IsNullOrEmpty(existing)) return existing;
            }

            return $"{GetOutputFolder(source, profile)}/" +
                   $"{GetGeneratedAssetName(source.name)}" +
                   "_Manifest.asset";
        }

        static string GetMeshIdentitySuffix(Mesh mesh)
        {
            if (mesh == null) return "nomesh00";
            string path = AssetDatabase.GetAssetPath(mesh);
            if (!string.IsNullOrEmpty(path))
            {
                string guid = AssetDatabase.AssetPathToGUID(path);
                string suffix = guid.Length >= 8 ? guid.Substring(0, 8) : guid;
                // FBX meshes share their model's GUID. The local file ID identifies the
                // actual mesh, including equally named mesh owners in different branches.
                if (AssetDatabase.IsSubAsset(mesh) &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string _,
                        out long localId))
                    suffix += "_" + localId.ToString("x");
                return suffix;
            }
            unchecked
            {
                int h = mesh.vertexCount;
                h = h * 31 + (int)mesh.GetIndexCount(0);
                h = h * 31 + (int)(mesh.bounds.size.sqrMagnitude * 1000f);
                return Mathf.Abs(h).ToString("x8");
            }
        }

        public static int ComputeFractureHash(Mesh mesh, DestructionProfile p)
        {
            return ComputeFractureHash(mesh, p, Vector3.one);
        }

        public static int ComputeFractureHash(Mesh mesh, DestructionProfile p,
            Vector3 meshWorldScale)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (mesh != null ? mesh.vertexCount : 0);
                h = h * 31 + (mesh != null ? (int)mesh.GetIndexCount(0) : 0);
                // Multiply before truncating, and mix in the mesh asset identity so
                // equal-sized meshes from different assets do not collide.
                h = h * 31 + (mesh != null ? (int)(mesh.bounds.size.sqrMagnitude * 1000f) : 0);
                string meshAssetPath = mesh != null ? AssetDatabase.GetAssetPath(mesh) : null;
                h = h * 31 + (!string.IsNullOrEmpty(meshAssetPath)
                    ? AssetDatabase.AssetPathToGUID(meshAssetPath).GetHashCode()
                    : 0);
                // The GUID is stable across a reimport. Include the dependency hash as
                // well so Preview never reuses a solved layout after an artist changes
                // the mesh while keeping its asset, vertex count and bounds intact.
                if (!string.IsNullOrEmpty(meshAssetPath))
                {
                    string revision = AssetDatabase.GetAssetDependencyHash(
                        meshAssetPath).ToString();
                    h = h * 31 + revision.GetHashCode();
                }
                h = h * 31 + p.Seed;
                h = h * 31 + (int)p.Pattern;
                h = h * 31 + p.FragmentCount;
                h = h * 31 + Mathf.RoundToInt(
                    p.FragmentShapeIrregularity * 1000f);
                h = h * 31 + (p.Solidify ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(p.SolidifyThickness * 10000f);
                if (p.Solidify)
                {
                    h = h * 31 + Mathf.RoundToInt(
                        Mathf.Abs(meshWorldScale.x) * 10000f);
                    h = h * 31 + Mathf.RoundToInt(
                        Mathf.Abs(meshWorldScale.y) * 10000f);
                    h = h * 31 + Mathf.RoundToInt(
                        Mathf.Abs(meshWorldScale.z) * 10000f);
                }
                h = h * 31 + Mathf.RoundToInt(p.InteriorUVScale * 1000f);
                h = h * 31 + (int)p.CutSurfaceUVMode;
                h = h * 31 + (int)p.GlassNormalAxis;
                h = h * 31 + p.RadialSpokes;
                h = h * 31 + p.RadialRings;
                h = h * 31 + Mathf.RoundToInt(p.RadialJitter * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.DirectionalCutStrength * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.FiberCutShare * 1000f);
                h = h * 31 + (int)p.WoodAxis;
                h = h * 31 + Mathf.RoundToInt(p.WoodEndSharpness * 1000f);
                h = h * 31 + (p.BrickUseCellSize ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(p.BrickCellSize.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.BrickCellSize.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.BrickCellSize.z * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.BrickJitter * 1000f);
                h = h * 31 + p.SlabLayers;
                h = h * 31 + (int)p.SlabNormalAxis;
                h = h * 31 + Mathf.RoundToInt(p.SlabJitter * 1000f);
                h = h * 31 + (int)p.PillarAxis;
                h = h * 31 + Mathf.RoundToInt(p.ColumnJitter * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.ImpactFocus * 1000f);
                // Placed hit and scatter length change the generated geometry, so the
                // preview cache has to treat them as inputs like every other control.
                h = h * 31 + (p.ImpactPointOverride ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(p.ImpactPointLocal.x * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.ImpactPointLocal.y * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.ImpactPointLocal.z * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.InteriorScatterLength * 1000f);
                h = h * 31 + (int)p.SecondaryBreakup;
                h = h * 31 + p.SecondaryEligibleFragments;
                h = h * 31 + p.SecondaryPiecesPerFragment;
                h = h * 31 + Mathf.RoundToInt(
                    p.SecondaryMinParentVolumeRatio * 1000f);
                h = h * 31 + Mathf.RoundToInt(
                    p.SecondaryIrregularity * 1000f);
                h = h * 31 + GeneratedManifest.SolverVersion;
                return h;
            }
        }

        public static int ComputeInputHash(Mesh mesh, DestructionProfile p)
        {
            return ComputeInputHash(mesh, p, Vector3.one);
        }

        public static int ComputeInputHash(Mesh mesh, DestructionProfile p,
            Vector3 meshWorldScale)
        {
            unchecked
            {
                int h = ComputeFractureHash(mesh, p, meshWorldScale);
                h = h * 31 + ComputeScatterHash(p);
                h = h * 31 + (p.InteriorUseSourceMaterial ? 1 : 0);
                if (!p.InteriorUseSourceMaterial && p.InteriorMaterial != null)
                {
                    string materialPath = AssetDatabase.GetAssetPath(p.InteriorMaterial);
                    string materialGuid = !string.IsNullOrEmpty(materialPath)
                        ? AssetDatabase.AssetPathToGUID(materialPath)
                        : p.InteriorMaterial.name;
                    h = h * 31 + (!string.IsNullOrEmpty(materialGuid)
                        ? materialGuid.GetHashCode()
                        : 0);
                }
                h = h * 31 + (int)p.Colliders;
                h = h * 31 + Mathf.RoundToInt(p.Density);
                h = h * 31 + Mathf.RoundToInt(p.MinFragmentMass * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.MaxFragmentMass * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.Drag * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.AngularDrag * 1000f);
                h = h * 31 + (p.UseGravity ? 1 : 0);
                h = h * 31 + (int)p.FragmentCollisionDetection;
                h = h * 31 + (int)p.FragmentInterpolation;
                h = h * 31 + (int)p.IntactBody;
                h = h * 31 + Mathf.RoundToInt(p.IntactMassMultiplier * 1000f);
                h = h * 31 + (int)p.SupportDetection;
                h = h * 31 + Mathf.RoundToInt(p.RequiredSupportRatio * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.SupportProbeDistance * 1000f);
                h = h * 31 + p.SupportProbeGrid;
                h = h * 31 + Mathf.RoundToInt(p.SupportProbeInset * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.SecondaryStrength * 100f);
                h = h * 31 + Mathf.RoundToInt(
                    p.SecondaryMinImpactVelocity * 100f);
                h = h * 31 + p.MaxActiveSecondaryBodies;
                return h;
            }
        }

        /// <summary>
        /// Render-only interior scatter does not alter fracture planes, but it must
        /// still invalidate a baked prefab and a displayed preview.
        /// </summary>
        internal static int ComputeScatterHash(DestructionProfile p)
        {
            if (p == null) return 0;
            unchecked
            {
                int h = 17;
                h = h * 31 + Mathf.RoundToInt(p.ConcreteIrregularity * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.WoodSplitIrregularity * 1000f);
                // The pattern selects which strength field feeds the scatter and which
                // shape it builds, so switching preset has to invalidate it too. Without
                // the pattern here the preview kept the previous preset's chips.
                h = h * 31 + (int)p.Pattern;
                h = h * 31 + (int)p.InteriorScatterStyle;
                h = h * 31 + Mathf.RoundToInt(p.InteriorScatterLength * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.BrickJitter * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.SlabJitter * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.ColumnJitter * 1000f);
                h = h * 31 + Mathf.RoundToInt(p.WoodEndSharpness * 1000f);
                return h;
            }
        }

        internal static List<int> SelectSecondaryParents(
            IList<float> volumes, DestructionProfile profile)
        {
            var selected = new List<int>();
            if (profile == null ||
                profile.SecondaryBreakup == SecondaryBreakupLevel.Off ||
                volumes == null || volumes.Count == 0)
                return selected;

            float total = 0f;
            for (int i = 0; i < volumes.Count; i++) total += volumes[i];
            float average = total / volumes.Count;
            float minimum =
                average * profile.SecondaryMinParentVolumeRatio;

            var candidates = new List<int>();
            for (int i = 0; i < volumes.Count; i++)
                if (volumes[i] >= minimum)
                    candidates.Add(i);

            candidates.Sort((a, b) =>
            {
                int volumeCompare = volumes[b].CompareTo(volumes[a]);
                return volumeCompare != 0 ? volumeCompare : a.CompareTo(b);
            });

            int targetLimit = profile.SecondaryEligibleFragments;
            if (profile.SecondaryBreakup == SecondaryBreakupLevel.Low)
                targetLimit = Mathf.Max(2, Mathf.RoundToInt(volumes.Count * 0.30f));
            else if (profile.SecondaryBreakup == SecondaryBreakupLevel.Medium)
                targetLimit = Mathf.Max(3, Mathf.RoundToInt(volumes.Count * 0.50f));
            else if (profile.SecondaryBreakup == SecondaryBreakupLevel.High)
                targetLimit = Mathf.Max(4, Mathf.RoundToInt(volumes.Count * 0.75f));

            int count = Mathf.Min(targetLimit, candidates.Count);
            for (int i = 0; i < count; i++)
                selected.Add(candidates[i]);
            return selected;
        }

        internal static int SecondarySeed(int primarySeed, int parentIndex)
        {
            unchecked
            {
                return primarySeed * 486187739 +
                       parentIndex * 16777619 + 1013904223;
            }
        }

        public static BakeOutcome Bake(GameObject source, DestructionProfile profile,
            bool placeInScene, bool allowUnsafeOpenFullSolid = false,
            BakeProgressContext? batchProgress = null, bool mergedAssembly = false,
            bool showShellThicknessWhileIntact = false,
            IReadOnlyList<WorkMesh> mergedModuleCollisionSources = null,
            Transform placementParent = null,
            AnalysisReport precomputedReport = null)
        {
            var outcome = new BakeOutcome();
            if (source == null)
            {
                outcome.Error = "Source object is missing or was unloaded. Select a scene object and try again.";
                return outcome;
            }
            if (profile == null)
            {
                outcome.Error = "Destruction Profile is missing or was unloaded. Reassign the profile and try again.";
                return outcome;
            }

            // A generated destructible is a prefab instance with an Intact child and an
            // inactive fragment pool. Rebaking that instance must update its prefab in
            // place; treating it as fresh source would create "*_Destructible_Destructible"
            // siblings and leave the old instance behind.
            DestructibleRoot rebakeRoot = source.GetComponentInParent<DestructibleRoot>();
            if (rebakeRoot != null && !rebakeRoot.gameObject.scene.IsValid())
            {
                // Rebake overwrites the generated prefab in place, so it needs a scene
                // instance as anchor. Baking the project asset would treat the
                // destructible as a fresh source and nest a second destructible.
                outcome.Error = "QD-BAKE-001: Select the scene instance, not the prefab asset\n" +
                                $"Why: \"{rebakeRoot.name}\" is a baked destructible prefab asset; " +
                                "rebaking updates its generated prefab through a scene instance.\n" +
                                "Fix: Drag the prefab into a scene, select that instance and bake again.";
                return outcome;
            }
            bool rebakingSceneRoot = rebakeRoot != null && rebakeRoot.gameObject.scene.IsValid();
            if (rebakingSceneRoot)
                source = rebakeRoot.gameObject;
            string existingPrefabPath = rebakingSceneRoot
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rebakeRoot.gameObject)
                : string.Empty;
            string existingManifestPath =
                FindGeneratedManifestPath(existingPrefabPath, profile);
            GeneratedManifest priorManifest = string.IsNullOrEmpty(existingManifestPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GeneratedManifest>(existingManifestPath);
            bool rebakingGeneratedPrefab = !string.IsNullOrEmpty(existingPrefabPath) &&
                                          (existingPrefabPath.StartsWith(
                                               GeneratedRootFolder + "/",
                                               StringComparison.OrdinalIgnoreCase) ||
                                           !string.IsNullOrEmpty(existingManifestPath));
            string rebakeOriginalSourceName = null;

            // Rebake must always fracture the prefab's intact mesh, never a fragment.
            // The fragment-preview edit view deactivates IntactRoot, and a plain
            // GetComponentInChildren would then return the first fragment's mesh, so
            // the rebake would re-cut debris and overwrite the prefab with it.
            GameObject intactSource = null;
            if (rebakingSceneRoot)
            {
                intactSource = rebakeRoot.IntactRoot;
                if (intactSource == null)
                {
                    Transform intactChild = rebakeRoot.transform.Find("Intact");
                    if (intactChild != null) intactSource = intactChild.gameObject;
                }
                if (intactSource == null)
                {
                    outcome.Error = "QD-BAKE-002: The destructible's intact mesh could not be found\n" +
                                    "Why: IntactRoot is unassigned and the prefab has no child named \"Intact\".\n" +
                                    "Fix: Rebake from the original source model, or restore the prefab's Intact child.";
                    return outcome;
                }
            }

            var sessionAssets = new List<string>();
            var timer = Stopwatch.StartNew();
            double nextProgressUpdate = EditorApplication.timeSinceStartup +
                                        (batchProgress.HasValue
                                            ? 0d
                                            : ProgressDisplayDelaySeconds);
            GameObject buildRoot = null;
            Mesh embeddedSourceSnapshot = null;
            string generatedRootName = null;
            string folder = null;
            // True when this bake reuses previously generated output in place;
            // rollback then must not delete the pre-existing manifest/prefab.
            bool reusedOutput = false;

            bool ContinueProgress(string message, float value, bool force = false)
            {
                double now = EditorApplication.timeSinceStartup;
                if (!force && now < nextProgressUpdate) return true;
                string title = batchProgress.HasValue
                    ? batchProgress.Value.Title
                    : "Quick Destruction";
                string displayMessage = batchProgress.HasValue
                    ? batchProgress.Value.FormatMessage(
                        source.name, message, timer.Elapsed.TotalSeconds)
                    : message;
                float displayValue = batchProgress.HasValue
                    ? batchProgress.Value.OverallProgress(value)
                    : Mathf.Clamp01(value);
                bool keepGoing = !EditorUtility.DisplayCancelableProgressBar(
                    title, displayMessage, displayValue);
                nextProgressUpdate = EditorApplication.timeSinceStartup +
                                     ProgressRefreshIntervalSeconds;
                return keepGoing;
            }

            void RequireProgress(string message, float value, bool force = false)
            {
                if (!ContinueProgress(message, value, force))
                    throw new OperationCanceledException();
            }

            try
            {
                // -------------------------------------------------- gate
                var swBakeStep = System.Diagnostics.Stopwatch.StartNew();
                // A batch already analyzed this exact source to decide whether to bake
                // it at all. Re-running the analyzer here doubled the geometry work for
                // every object in the list, for a result that cannot have changed since.
                var report = precomputedReport ?? MeshAnalyzer.Analyze(source);
                if (report.Status == AnalysisSeverity.Blocked)
                {
                    AnalysisItem blocker = report.Items.Find(
                        item => item != null && item.Severity == AnalysisSeverity.Blocked);
                    outcome.Error = blocker != null
                        ? $"{blocker.Code}: {blocker.Title}\nWhy: {blocker.Why}\nFix: {blocker.Fix}"
                        : "Analyze reports a blocker. Fix the source before baking.";
                    return outcome;
                }
                if (!MeshAnalyzer.ValidateClosedFractureInput(
                        report, profile, allowUnsafeOpenFullSolid,
                        out string fractureInputError))
                {
                    outcome.Error = fractureInputError;
                    return outcome;
                }
                DestructionProfileData solverProfile =
                    MeshAnalyzer.CreateSafeSolverProfile(report, profile,
                        out string safetyNotice);
                solverProfile.MergeContactingSourceShells = mergedAssembly;
                if (!string.IsNullOrEmpty(safetyNotice))
                    outcome.Warnings.Add(safetyNotice);
                if (mergedAssembly && profile.FragmentCount >
                    MeshAnalyzer.MaxSafePrimaryFragmentCount)
                {
                    outcome.Warnings.Add(
                        $"Whole Building mode requested {profile.FragmentCount:n0} primary " +
                        "pieces. Large buildings should use activation and visible-fragment " +
                        "budgets so all baked Rigidbodies do not wake in one frame.");
                }
#pragma warning disable 0162
                if (FractureEngine.VerboseSolverProfiling)
                UnityEngine.Debug.Log($"[QD Profile] Bake Phase 1 (Analyze & Safety Gate): {swBakeStep.ElapsedMilliseconds}ms");
                swBakeStep.Restart();

                // includeInactive: the intact mesh stays available while the fragment
                // preview hides IntactRoot; FracturedRoot is never a fracture source.
                var filter = rebakingSceneRoot
                    ? intactSource.GetComponentInChildren<MeshFilter>(true)
                    : MeshAnalyzer.ResolveSourceMeshFilter(source);
                if (filter == null || filter.sharedMesh == null)
                {
                    // Fresh sources are mesh-gated by the analyzer; a rebake can still
                    // land here when the prefab's intact mesh was stripped by hand.
                    outcome.Error = rebakingSceneRoot
                        ? "QD-BAKE-003: The intact root has no MeshFilter with a mesh\n" +
                          $"Why: \"{intactSource.name}\" must carry the mesh the prefab was baked from.\n" +
                          "Fix: Rebake from the original source model instead."
                        : "QD-GEO-001: No mesh found\n" +
                          "Why: A MeshFilter with a valid mesh is required.\n" +
                          "Fix: Add a MeshFilter with a mesh, or select a different object.";
                    return outcome;
                }
                // Only a Hollow Shell bake records an authoring source, and whenever it
                // did, the intact renderer now carries the thickened shell. Rebaking that
                // would solidify an already solidified mesh, once per rebake.
                var sourceMesh = priorManifest != null &&
                                 priorManifest.AuthoringSourceMesh != null
                    ? priorManifest.AuthoringSourceMesh
                    : filter.sharedMesh;
                var sourceRenderer = filter.GetComponent<MeshRenderer>();
                Material[] sourceMaterials = sourceRenderer != null
                    ? sourceRenderer.sharedMaterials
                    : new Material[sourceMesh.subMeshCount];
                bool needsFragmentEffectColor = profile.UseDefaultHitParticles ||
                                                profile.HitParticlePrefab != null;
                Dictionary<Material, Color> effectColors = needsFragmentEffectColor
                    ? BuildMaterialEffectColorCache(sourceMaterials,
                        (index, count) => RequireProgress(
                            $"Sampling material colors {index + 1}/{count}...",
                            0.02f + (float)index / Mathf.Max(1, count) * 0.015f))
                    : new Dictionary<Material, Color>();

                // The MeshFilter may sit on a transformed child of the source root.
                // Bake that node's local TRS into the generated Intact/Fractured
                // containers so the prefab reproduces the source arrangement exactly.
                Matrix4x4 filterLocal =
                    source.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3 filterLocalPos = filterLocal.GetPosition();
                Quaternion filterLocalRot = filterLocal.rotation;
                Vector3 filterLocalScale = filterLocal.lossyScale;
                solverProfile = SolverProfileSpace.ForMeshTransform(
                    solverProfile, filter.transform.lossyScale);
                bool buildPreparedIntactShell = showShellThicknessWhileIntact &&
                                                solverProfile.Solidify;
                // Whole Building always needs the prepared shell for collision fallback,
                // even when its intact renderer deliberately keeps the authored thin look.
                // The old path captured it only for the optional thick intact visual, so
                // Convex Mesh collision was cooked from zero-thickness planes instead.
                solverProfile.CapturePreparedSource = buildPreparedIntactShell ||
                                                      (mergedAssembly &&
                                                       solverProfile.Solidify &&
                                                       (mergedModuleCollisionSources == null ||
                                                        mergedModuleCollisionSources.Count == 0));
                if (showShellThicknessWhileIntact && !solverProfile.Solidify)
                {
                    outcome.Warnings.Add(
                        "Show Hollow Shell Thickness While Intact was ignored because the " +
                        "active profile uses Full Solid. Choose Hollow Shell and rebake to " +
                        "give the intact visual authored wall thickness.");
                }

                // -------------------------------------------------- fracture
                bool Progress(float t)
                {
                    // DisplayCancelableProgressBar crosses into Unity's native UI and
                    // can cost far more than one small mesh split. Updating it for every
                    // fragment made a 48-piece, 96-triangle fence spend most of Bake in
                    // repaint/message pumping. The solver still calls us every step; only
                    // the native repaint is throttled.
                    return ContinueProgress(
                        $"Fracturing {source.name}...", t * 0.68f);
                }

                Mesh solverMesh = ReadableMeshUtility.AcquireForFracture(
                    sourceMesh, solverProfile.Solidify,
                    out bool ownsSolverMesh, out string readableError);
                if (solverMesh == null)
                {
                    outcome.Error = $"QD-GEO-002: Mesh data could not be read\n" +
                                    $"Why: {readableError}\n" +
                                    "Fix: Reimport the model or enable Read/Write in its importer.";
                    return outcome;
                }

                FractureResult fracture;
                try
                {
                    fracture = FractureEngine.Fracture(solverMesh, solverProfile, Progress);
                }
                finally
                {
                    ReadableMeshUtility.Release(solverMesh, ownsSolverMesh);
                }
#pragma warning disable 0162
                if (FractureEngine.VerboseSolverProfiling)
                UnityEngine.Debug.Log($"[QD Profile] Bake Phase 2 (Primary Fracture): {swBakeStep.ElapsedMilliseconds}ms ({fracture?.Fragments?.Count ?? 0} pieces)");
                swBakeStep.Restart();
                if (fracture.Cancelled)
                {
                    outcome.Cancelled = true;
                    outcome.Error =
                        "Bake cancelled before any assets were written. Previous output was left untouched.";
                    return outcome;
                }
                // Preserve solver diagnostics even when a safety gate below aborts the
                // bake. Previously the generic failure returned before these warnings
                // reached Last Bake Report, leaving users with no actionable cause.
                outcome.Warnings.AddRange(fracture.Warnings);
                if (fracture.Fragments.Count < 2)
                {
                    outcome.Error = MeshAnalyzer.BuildFractureFailureMessage(
                        report, fracture, profile);
                    return outcome;
                }
                if (!MeshAnalyzer.ValidatePrimaryFragmentCount(
                        fracture.Fragments.Count, profile.FragmentCount,
                        profile.Pattern,
                        mergedAssembly
                            ? MeshAnalyzer.MaxMergedAssemblyFragmentCount
                            : MeshAnalyzer.MaxSafePrimaryFragmentCount,
                        out string primaryFragmentBudgetError))
                {
                    outcome.Error = primaryFragmentBudgetError;
                    return outcome;
                }
                if (!MeshAnalyzer.ValidateGeneratedFragments(fracture.Fragments,
                        "Primary fracture", out string generatedSafetyError))
                {
                    outcome.Error = generatedSafetyError;
                    return outcome;
                }

                float totalVolume = 0f;
                foreach (float v in fracture.Volumes) totalVolume += v;
                float avgVolume = totalVolume / fracture.Volumes.Count;

                // -------------------------------------------------- optional level-two fracture
                var secondaryData = new Dictionary<int, SecondaryBakeData>();
                List<int> secondaryParents = SelectSecondaryParents(
                    fracture.Volumes, profile);
                for (int candidate = 0;
                     candidate < secondaryParents.Count; candidate++)
                {
                    int parentIndex = secondaryParents[candidate];
                    bool SecondaryProgress(float value)
                    {
                        float overall = secondaryParents.Count > 0
                            ? (candidate + Mathf.Clamp01(value)) /
                              secondaryParents.Count
                            : 1f;
                        return ContinueProgress(
                            $"Secondary breakup {candidate + 1}/{secondaryParents.Count}...",
                            0.68f + overall * 0.12f);
                    }

                    FractureResult secondary = FractureEngine.FractureSecondary(
                        fracture.Fragments[parentIndex], profile,
                        SecondarySeed(profile.Seed, parentIndex),
                        profile.SecondaryPiecesPerFragment, SecondaryProgress);
                    if (secondary.Cancelled)
                    {
                        outcome.Cancelled = true;
                        outcome.Error =
                            "Bake cancelled during secondary breakup, before any assets were written. Previous output was left untouched.";
                        return outcome;
                    }
                    if (secondary.Fragments.Count < 2)
                    {
                        outcome.Warnings.Add(
                            $"Primary fragment {parentIndex} could not produce a valid second level.");
                        continue;
                    }
                    if (!MeshAnalyzer.ValidateGeneratedFragments(secondary.Fragments,
                            "Secondary fracture", out string secondarySafetyError))
                    {
                        outcome.Warnings.Add(secondarySafetyError);
                        continue;
                    }

                    var data = new SecondaryBakeData
                    {
                        ParentIndex = parentIndex,
                        ParentCentroid =
                            fracture.Fragments[parentIndex].ComputeBounds().center
                    };
                    data.WorkMeshes.AddRange(secondary.Fragments);
                    data.Volumes.AddRange(secondary.Volumes);
                    float secondaryTotal = 0f;
                    for (int i = 0; i < data.Volumes.Count; i++)
                        secondaryTotal += data.Volumes[i];
                    secondaryData.Add(parentIndex, data);

                    foreach (string warning in secondary.Warnings)
                    {
                        outcome.Warnings.Add(
                            $"Secondary parent {parentIndex}: {warning}");
                    }
                }
                if (profile.SecondaryBreakup != SecondaryBreakupLevel.Off &&
                    secondaryParents.Count == 0)
                {
                    outcome.Warnings.Add(
                        "Secondary breakup is enabled, but no simulated primary fragment met " +
                        "the Minimum Parent Size threshold.");
                }
#pragma warning disable 0162
                if (FractureEngine.VerboseSolverProfiling)
                UnityEngine.Debug.Log($"[QD Profile] Bake Phase 3 (Secondary Breakup): {swBakeStep.ElapsedMilliseconds}ms ({secondaryData.Count} secondary parent blocks)");
                swBakeStep.Restart();

                // -------------------------------------------------- folders
                RequireProgress("Preparing generated assets...", 0.8f, true);
                EnsureGeneratedRoot();
                string safeName = GetGeneratedAssetName(source.name);
                folder = GetOutputFolder(source, profile);
                string manifestPath;
                string prefabPath;

                if (rebakingGeneratedPrefab)
                {
                    // Keep the exact folder, manifest and prefab GUID that produced the
                    // selected instance. This makes Bake & Setup an actual update action.
                    folder = Path.GetDirectoryName(existingPrefabPath)
                        ?.Replace('\\', '/') ?? folder;
                    prefabPath = existingPrefabPath;
                    manifestPath = existingManifestPath;
                    if (string.IsNullOrEmpty(manifestPath))
                        manifestPath = FindGeneratedManifestPath(prefabPath, profile);
                    if (!string.IsNullOrEmpty(manifestPath))
                    {
                        if (priorManifest != null &&
                            !string.IsNullOrEmpty(priorManifest.SourceName))
                        {
                            rebakeOriginalSourceName = priorManifest.SourceName;
                            safeName = GetGeneratedAssetName(priorManifest.SourceName);
                        }
                    }
                    else
                    {
                        manifestPath = $"{folder}/{safeName}_Manifest.asset";
                    }
                }
                else
                {
                    manifestPath = $"{folder}/{safeName}_Manifest.asset";
                    prefabPath = $"{folder}/{safeName}_Destructible.prefab";
                }
                outcome.OutputFolder = folder;
                EnsureFolder(folder);
                // Optional fallback: every fragment chooses the source material covering
                // most of its exterior, so roof, wall and trim cuts match their own region.
                bool useSourceInterior = profile.InteriorUseSourceMaterial &&
                                         Array.Exists(sourceMaterials, material => material != null);
                bool autoInterior = !useSourceInterior && profile.InteriorMaterial == null;
                string matPath = $"{folder}/{safeName}_Interior.mat";

                // Rebake overwrites output in place: the manifest and prefab keep their
                // GUIDs, so placed scene instances do not become Missing Prefab. Only
                // orphaned assets that this bake no longer writes are deleted.
                var manifest = AssetDatabase.LoadAssetAtPath<GeneratedManifest>(manifestPath);
                reusedOutput = manifest != null;
                if (reusedOutput)
                {
                    // A merged building stores its Intact mesh as a sub-asset of this
                    // manifest. On an in-place rebake that same mesh is also our current
                    // input. Snapshot it before removing the previous generated meshes;
                    // otherwise DestroyImmediate below invalidates sourceMesh and the new
                    // Intact copy throws MissingReferenceException.
                    string currentSourcePath = AssetDatabase.GetAssetPath(sourceMesh);
                    if (string.Equals(currentSourcePath, manifestPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        embeddedSourceSnapshot =
                            UnityEngine.Object.Instantiate(sourceMesh);
                        embeddedSourceSnapshot.name = sourceMesh.name + "_RebakeSource";
                        embeddedSourceSnapshot.hideFlags = HideFlags.HideAndDontSave;
                        sourceMesh = embeddedSourceSnapshot;
                    }

                    var keep = new HashSet<string> { manifestPath, prefabPath };
                    if (autoInterior) keep.Add(matPath);
                    foreach (var entry in manifest.Entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
                        if (!keep.Contains(entry.AssetPath))
                            AssetDatabase.DeleteAsset(entry.AssetPath);
                    }
                    // Previously embedded fragment meshes are replaced below.
                    foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(manifestPath))
                    {
                        if (sub is Mesh oldMesh)
                            UnityEngine.Object.DestroyImmediate(oldMesh, true);
                    }
                    manifest.Entries.Clear();
                    manifest.Warnings.Clear();
                }
                else
                {
                    manifest = ScriptableObject.CreateInstance<GeneratedManifest>();
                    manifest.name = $"{safeName}_Manifest";
                    AssetDatabase.CreateAsset(manifest, manifestPath);
                    sessionAssets.Add(manifestPath);
                }

                // -------------------------------------------------- manifest + interior material
                manifest.SchemaVersion = GeneratedManifest.CurrentSchemaVersion;
                manifest.BakedSolverVersion = GeneratedManifest.SolverVersion;
                manifest.SourceName = !string.IsNullOrEmpty(rebakeOriginalSourceName)
                    ? rebakeOriginalSourceName
                    : source.name;
                manifest.ProfileName = profile.name;
                manifest.InputHash = ComputeInputHash(
                    sourceMesh, profile, filter.transform.lossyScale);
                manifest.BakeTimestampUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
                manifest.SourceGuid = string.IsNullOrEmpty(sourcePath)
                    ? "" : AssetDatabase.AssetPathToGUID(sourcePath);
                manifest.IntactUsesShellThickness = buildPreparedIntactShell;
                manifest.AuthoringSourceMesh = null;
                manifest.Entries.Add(new GeneratedManifest.Entry { AssetPath = manifestPath, Kind = "manifest" });

                // Imported source meshes already have a persistent asset reference. Whole
                // Building, however, fractures a HideAndDontSave proxy mesh; referencing it
                // directly from the prefab serializes as fileID 0 once the proxy is released.
                // Embed one intact visual copy beside the fragment meshes so placement keeps
                // a visible building and rebake can still resolve its authoritative source.
                Mesh intactVisualMesh;
                if (buildPreparedIntactShell)
                {
                    if (fracture.PreparedSource == null)
                        throw new Exception(
                            "The prepared Hollow Shell source was not returned by the fracture solver.");

                    Mesh authoringSourceMesh = UnityEngine.Object.Instantiate(sourceMesh);
                    authoringSourceMesh.name = $"{safeName}_AuthoringSource";
                    authoringSourceMesh.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(authoringSourceMesh, manifest);
                    manifest.AuthoringSourceMesh = authoringSourceMesh;

                    intactVisualMesh = fracture.PreparedSource.ToMesh(
                        $"{safeName}_Intact_HollowShell");
                    intactVisualMesh.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(intactVisualMesh, manifest);
                }
                else if (string.IsNullOrEmpty(sourcePath))
                {
                    intactVisualMesh = UnityEngine.Object.Instantiate(sourceMesh);
                    intactVisualMesh.name = $"{safeName}_Intact";
                    intactVisualMesh.hideFlags = HideFlags.None;
                    AssetDatabase.AddObjectToAsset(intactVisualMesh, manifest);
                }
                else
                {
                    intactVisualMesh = sourceMesh;
                }

                Material interior = useSourceInterior ? null : profile.InteriorMaterial;
                if (interior == null)
                {
                    // Reuse the previously generated interior material when it exists.
                    interior = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (interior == null)
                    {
                        interior = CreateDefaultInteriorMaterial();
                        AssetDatabase.CreateAsset(interior, matPath);
                        sessionAssets.Add(matPath);
                    }
                    manifest.Entries.Add(new GeneratedManifest.Entry { AssetPath = matPath, Kind = "material" });
                }

                // -------------------------------------------------- fragment meshes (sub-assets)
                // Volume and mass derive from the MeshFilter node's world scale, which is
                // what the placed instance reproduces (source root scale x node offset).
                float scaleVolume = Mathf.Abs(
                    filter.transform.lossyScale.x *
                    filter.transform.lossyScale.y *
                    filter.transform.lossyScale.z);
                if (scaleVolume < 1e-8f) scaleVolume = 1f;

                // Stable source-island ordering is shared by compound intact colliders,
                // structural support probes and every generated fragment record. Whole
                // Building can stitch near-matching authored module seams inside the
                // solver. Re-splitting sourceMesh here used to discard that repair: the
                // preview fractured a stacked wall as one surface, while Bake created one
                // probe group per authored row. Use the solver's post-stitch/pre-fracture
                // topology; the fallback keeps this path safe for older/custom results.
                WorkMesh structuralSource = fracture.StructuralSource ??
                                            WorkMesh.FromMesh(sourceMesh);
                List<WorkMesh> intactComponents =
                    structuralSource.SplitConnectedComponents();
                List<WorkMesh> intactCollisionComponents =
                    BuildIntactCollisionComponents(structuralSource,
                        fracture.PreparedSource, mergedModuleCollisionSources,
                        mergedAssembly, solverProfile.Solidify,
                        solverProfile.SolidifyThickness);
                var intactPanelBoxes = new List<OrientedBox>(
                    intactCollisionComponents.Count);
                int intactPanelBoxCount = 0;
                int mergedCoplanarPanelCount = 0;
                // Merging is about how the assembly is partitioned for collision and for
                // support probes, which has nothing to do with whether a shell had to be
                // thickened. Gating it on Solidify meant a kit of already-solid modules —
                // the case that needs it most — skipped the whole pass: 64 authored cube
                // blocks stayed 64 collision parts, 64 probe grids and 39 structural
                // groups, barely one fragment each. Exact Mesh still opts out, because
                // there the authored triangles are the collider.
                if (mergedAssembly &&
                    profile.Colliders != ColliderStrategy.ExactMesh)
                {
                    for (int i = 0; i < intactCollisionComponents.Count; i++)
                    {
                        if (TryGetOrientedBox(intactCollisionComponents[i],
                                out OrientedBox panelBox))
                            intactPanelBoxCount++;
                        intactPanelBoxes.Add(panelBox);
                    }

                    // Modular kits commonly build one wall from several perfectly flat
                    // panels. Treating every authored tile as a separate probe created a
                    // hard horizontal support section at each storey and multiplied the
                    // recurring probe work. Merge only rectangular, coplanar tiles whose
                    // union is still one rectangle. Corners, gaps and irregular/solid
                    // shapes remain independent collision parts.
                    mergedCoplanarPanelCount = MergeCoplanarPanelGroups(
                        intactCollisionComponents, intactPanelBoxes);
                    intactPanelBoxCount = 0;
                    for (int i = 0; i < intactPanelBoxes.Count; i++)
                        if (intactPanelBoxes[i].IsValid) intactPanelBoxCount++;
                }
                else
                {
                    for (int i = 0; i < intactCollisionComponents.Count; i++)
                        intactPanelBoxes.Add(default);
                }

                var fragMeshes = new List<Mesh>(fracture.Fragments.Count);
                // Render-only chips/splinters are intentionally kept out of WorkMesh:
                // every collider and support calculation continues to use the closed,
                // planar fracture mesh.
                var fragInteriorScatterMeshes = new List<Mesh>(fracture.Fragments.Count);
                var fragDynamicColliderMeshes = new List<List<Mesh>>(
                    fracture.Fragments.Count);
                var fragCentroids = new List<Vector3>(fracture.Fragments.Count);
                var fragmentStructuralGroups = new List<int>(fracture.Fragments.Count);
                // Whole Building deliberately lets fracture planes cross authored seams,
                // so the solver can return every fragment with StructuralGroup 0 after
                // stitching the building into one connected WorkMesh. Runtime support must
                // still measure each continuous wall/floor surface locally: otherwise one weak height
                // band in a lower wall is also the weakest band of the floors and every
                // neighbouring wall, and ReleaseGroupAboveSection drops the whole building
                // even while its total footing remains above Required Support. The intact
                // collision components retain the authored module/island ordering used by
                // StructuralSupportNode.ProbeColliders, so use those same components to
                // restore fragment membership after the global fracture.
                IReadOnlyList<WorkMesh> structuralGroupSources = mergedAssembly &&
                    intactCollisionComponents.Count > 0
                    ? intactCollisionComponents
                    : intactComponents;
                // Centroids and structural groups feed the support connectivity bake
                // below, so compute them before any fragment is rebased.
                for (int i = 0; i < fracture.Fragments.Count; i++)
                {
                    WorkMesh wm = fracture.Fragments[i];
                    Vector3 centroid = wm.ComputeBounds().center;
                    fragCentroids.Add(centroid);
                    fragmentStructuralGroups.Add(mergedAssembly
                        ? FindStructuralGroup(centroid, structuralGroupSources,
                            intactPanelBoxes)
                        : wm.StructuralGroup >= 0
                            ? wm.StructuralGroup
                            : FindStructuralGroup(centroid, intactComponents));
                }

                List<int>[] supportNeighbours = null;
                List<float>[] supportNeighbourContactAreas = null;
                bool[] glassBoundaryAnchors = null;
                bool[] groundAnchors = null;
                // Structural release needs connectivity-to-ground data for every
                // pattern, not just radial glass. The weld-vertex adjacency build is
                // O(vertices), trivial next to the fracture itself.
                if (profile.Pattern == FracturePattern.RadialGlass ||
                    profile.IntactBody == IntactBodyMode.ReleaseWhenUnsupported)
                {
                    BuildSupportConnectivityData(fracture.Fragments, sourceMesh.bounds,
                        fragmentStructuralGroups,
                        profile.Pattern == FracturePattern.RadialGlass,
                        out supportNeighbours, out supportNeighbourContactAreas,
                        out glassBoundaryAnchors,
                        out groundAnchors);
                }
                int totalTris = 0;
                int primaryDynamicCompoundBudget =
                    DynamicCompoundPartBudget(fracture.Fragments.Count, false);
                int secondaryDynamicCompoundBudget =
                    DynamicCompoundPartBudget(fracture.Fragments.Count, true);
                // Only Exact Mesh needs a dynamic compound. Convex Mesh means what the UI
                // promises: one fitted hull per fragment. Dense bakes used to receive a
                // compound decomposition on top of that hull, which then replaced it the
                // moment the fragment turned dynamic — four cooked shapes and four
                // broadphase entries per active piece, for fragments that are near-convex
                // to begin with.
                bool prepareDynamicCompound =
                    profile.Colliders == ColliderStrategy.ExactMesh;

                for (int i = 0; i < fracture.Fragments.Count; i++)
                {
                    int fragmentIndex = i;
                    float fragmentProgress = fracture.Fragments.Count > 0
                        ? (float)i / fracture.Fragments.Count
                        : 1f;
                    RequireProgress(
                        $"Preparing fragment meshes {i + 1}/{fracture.Fragments.Count}...",
                        0.80f + fragmentProgress * 0.055f);
                    var wm = fracture.Fragments[i];
                    Vector3 centroid = fragCentroids[i];
                    wm.Rebase(centroid);
                    Mesh interiorScatter = InteriorScatterMeshBuilder.Build(wm, profile,
                        $"Frag_{i:000}_InteriorScatter");
                    var mesh = wm.ToMesh($"Frag_{i:000}");
                    fragMeshes.Add(mesh);
                    fragInteriorScatterMeshes.Add(interiorScatter);
                    var dynamicMeshes = new List<Mesh>();
                    if (prepareDynamicCompound)
                    {
                        dynamicMeshes = BuildDynamicCompoundMeshes(wm,
                            $"Frag_{i:000}_Dynamic", primaryDynamicCompoundBudget,
                            local => RequireProgress(
                                $"Preparing dynamic collider {fragmentIndex + 1}/" +
                                $"{fracture.Fragments.Count}...",
                                0.80f +
                                (fragmentIndex + Mathf.Clamp01(local)) /
                                Mathf.Max(1f, fracture.Fragments.Count) * 0.055f));
                        for (int part = 0; part < dynamicMeshes.Count; part++)
                            AssetDatabase.AddObjectToAsset(dynamicMeshes[part], manifest);
                    }
                    fragDynamicColliderMeshes.Add(dynamicMeshes);
                    totalTris += wm.TriangleCount;
                    if (interiorScatter != null)
                        totalTris += interiorScatter.triangles.Length / 3;
                    AssetDatabase.AddObjectToAsset(mesh, manifest);
                    if (interiorScatter != null)
                        AssetDatabase.AddObjectToAsset(interiorScatter, manifest);
                }

                // Build compound intact collision for disconnected source islands. One
                // convex hull around a bridge deck plus separate posts spans the empty
                // gaps and makes unrelated elements behave like one solid block.
                var intactColliderMeshes = new List<Mesh>();
                if (profile.Colliders != ColliderStrategy.PrimitiveBox &&
                    intactCollisionComponents.Count > 1)
                {
                    for (int i = 0; i < intactCollisionComponents.Count; i++)
                    {
                        if (intactPanelBoxes[i].IsValid)
                        {
                            // Keep list indices aligned with collision components, but do
                            // not serialize a redundant triangulated mesh for a rectangular
                            // authored wall/floor module.
                            intactColliderMeshes.Add(null);
                            continue;
                        }
                        Mesh colliderMesh = intactCollisionComponents[i].ToMesh(
                            $"IntactCollider_{i:000}");
                        intactColliderMeshes.Add(colliderMesh);
                        AssetDatabase.AddObjectToAsset(colliderMesh, manifest);
                    }
                    outcome.Warnings.Add(
                        $"Intact collision uses {intactCollisionComponents.Count} compound " +
                        "parts. In Whole Building modular workflow each authored panel keeps " +
                        "its own Hollow Shell collider instead of one convex hull spanning " +
                        "the room interior.");
                    if (intactPanelBoxCount > 0)
                    {
                        outcome.Warnings.Add(
                            $"{intactPanelBoxCount} rectangular modular wall/floor " +
                            "collider(s) use oriented BoxColliders with the authored Hollow " +
                            "Shell thickness. No triangulated collider surface is stored " +
                             "for those panels; irregular modules keep the convex-mesh fallback.");
                    }
                    if (mergedCoplanarPanelCount > 0)
                    {
                        outcome.Warnings.Add(
                            $"Whole Building combined {mergedCoplanarPanelCount} coplanar " +
                            "modular panel seam(s) into continuous wall/floor colliders and " +
                            "support groups. Corners and real gaps remain separate.");
                    }
                }

                int secondaryFragmentCount = 0;
                for (int p = 0; p < secondaryParents.Count; p++)
                {
                    int parentIndex = secondaryParents[p];
                    if (!secondaryData.TryGetValue(parentIndex, out var data))
                        continue;

                    for (int i = 0; i < data.WorkMeshes.Count; i++)
                    {
                        float secondaryAssetProgress = secondaryParents.Count > 0
                            ? (p + (float)i / Mathf.Max(1, data.WorkMeshes.Count)) /
                              secondaryParents.Count
                            : 1f;
                        RequireProgress(
                            $"Preparing level-two meshes {p + 1}/" +
                            $"{secondaryParents.Count}...",
                            0.855f + secondaryAssetProgress * 0.025f);
                        WorkMesh work = data.WorkMeshes[i];
                        Vector3 centroid = work.ComputeBounds().center;
                        data.LocalPositions.Add(centroid - data.ParentCentroid);
                        work.Rebase(centroid);
                        Mesh interiorScatter = InteriorScatterMeshBuilder.Build(work,
                            profile,
                            $"Frag_{parentIndex:000}_Secondary_{i:00}_InteriorScatter");
                        Mesh mesh = work.ToMesh(
                            $"Frag_{parentIndex:000}_Secondary_{i:00}");
                        data.Meshes.Add(mesh);
                        data.InteriorScatterMeshes.Add(interiorScatter);
                        var dynamicMeshes = new List<Mesh>();
                        if (prepareDynamicCompound)
                        {
                            int parentProgressIndex = p;
                            int childProgressIndex = i;
                            dynamicMeshes = BuildDynamicCompoundMeshes(work,
                                $"Frag_{parentIndex:000}_Secondary_{i:00}_Dynamic",
                                secondaryDynamicCompoundBudget,
                                local => RequireProgress(
                                    $"Preparing level-two dynamic collider " +
                                    $"{parentProgressIndex + 1}/" +
                                    $"{secondaryParents.Count}...",
                                    0.855f +
                                    (parentProgressIndex +
                                     (childProgressIndex + Mathf.Clamp01(local)) /
                                     Mathf.Max(1f, data.WorkMeshes.Count)) /
                                    Mathf.Max(1f, secondaryParents.Count) * 0.025f));
                            for (int part = 0; part < dynamicMeshes.Count; part++)
                                AssetDatabase.AddObjectToAsset(dynamicMeshes[part], manifest);
                        }
                        data.DynamicColliderMeshes.Add(dynamicMeshes);
                        totalTris += work.TriangleCount;
                        if (interiorScatter != null)
                            totalTris += interiorScatter.triangles.Length / 3;
                        secondaryFragmentCount++;
                        AssetDatabase.AddObjectToAsset(mesh, manifest);
                        if (interiorScatter != null)
                            AssetDatabase.AddObjectToAsset(interiorScatter, manifest);
                    }
                }

                manifest.FragmentCount = fragMeshes.Count;
                manifest.SecondaryFragmentCount = secondaryFragmentCount;
                manifest.TotalTriangles = totalTris;
                // Convex cooking is pre-tested per mesh in AddConvexCollider — a degenerate
                // shell already falls back on its own, and a mesh PhysX cannot hull never
                // reaches the runtime. This blanket density switch is only a last resort for
                // a bake so dense that cooking cost itself becomes the problem, and 256 sat
                // six pieces above the default Fragment Count: an ordinary building bake
                // straddled it, so one run shipped convex hulls and the next silently shipped
                // oversized boxes that interpenetrate and shove the debris apart. Buildings
                // are authored dense on purpose, so the gate belongs well clear of them.
                bool useStablePrimitiveColliders =
                    profile.Colliders == ColliderStrategy.ConvexMesh &&
                    fragMeshes.Count + secondaryFragmentCount >= StableBoxColliderFragmentLimit;
                if (useStablePrimitiveColliders)
                {
                    outcome.Warnings.Add(
                        $"This bake has {fragMeshes.Count + secondaryFragmentCount} pieces, " +
                        $"at or above the {StableBoxColliderFragmentLimit}-piece limit, so it " +
                        "uses stable per-fragment Box Colliders instead of PhysX convex " +
                        "cooking. Reduce Fragment Count to keep convex collision.");
                }
                if (secondaryFragmentCount > 160)
                {
                    outcome.Warnings.Add(
                        $"Bake generated {secondaryFragmentCount} level-two pieces. " +
                        "Use this only for close-up hero destruction or reduce the secondary preset.");
                }

                // -------------------------------------------------- build hierarchy
                RequireProgress("Building prefab hierarchy...", 0.88f, true);
                string rootSourceName =
                    !string.IsNullOrEmpty(rebakeOriginalSourceName)
                        ? rebakeOriginalSourceName
                        : source.name;
                generatedRootName =
                    GetDestructibleBaseName(rootSourceName) + "_Destructible";
                buildRoot = new GameObject(generatedRootName);
                // The baked prefab inherits the source's layer, tag and static flags.
                buildRoot.layer = source.layer;
                buildRoot.tag = source.tag;
                GameObjectUtility.SetStaticEditorFlags(buildRoot,
                    GameObjectUtility.GetStaticEditorFlags(source));
                var root = buildRoot.AddComponent<DestructibleRoot>();
                root.Profile = profile;
                root.BakedAsWholeBuilding = mergedAssembly;
                bool deferInactiveFragmentPhysics = ShouldDeferFragmentPhysics(
                    mergedAssembly, fragMeshes.Count, profile.IntactBody);
                root.DeferInactiveFragmentPhysics = deferInactiveFragmentPhysics;
                if (deferInactiveFragmentPhysics)
                {
                    outcome.Warnings.Add(
                        $"Large Whole Building defers inactive fragment physics: only pieces " +
                        $"that actually detach receive a Rigidbody. This keeps a local first " +
                        $"break from registering all {fragMeshes.Count} pieces with PhysX.");
                }
                // Present on every new bake, but completely idle until the user assigns
                // a particle prefab or sound clip. Scene-instance overrides survive rebakes.
                buildRoot.AddComponent<DestructionBreakEffects>();
                // Idle while disabled. Keeping it on new prefabs means a profile's
                // runtime-only toggle can be changed without rebaking geometry.
                var runtimeSplitter = buildRoot.AddComponent<RuntimeFragmentSplitter>();
                runtimeSplitter.enabled = profile.RuntimeFragmentSplitting;
                // Prefab assets must not stamp the same save ID onto every instance.
                // Scene placement below assigns a serialized per-instance ID.
                root.StableId = "";

                if (profile.IntactBody != IntactBodyMode.Static)
                {
                    var intactBody = buildRoot.AddComponent<Rigidbody>();
                    intactBody.mass = Mathf.Clamp(
                        totalVolume * scaleVolume * profile.Density *
                        profile.IntactMassMultiplier,
                        Mathf.Max(0.01f, profile.MinFragmentMass), 100000f);
#if UNITY_6000_0_OR_NEWER
                    intactBody.linearDamping = profile.Drag;
                    intactBody.angularDamping = profile.AngularDrag;
#else
                    intactBody.drag = profile.Drag;
                    intactBody.angularDrag = profile.AngularDrag;
#endif
                    intactBody.useGravity = profile.UseGravity;
                    intactBody.isKinematic =
                        profile.IntactBody == IntactBodyMode.ReleaseWhenUnsupported;
                    intactBody.collisionDetectionMode = profile.FragmentCollisionDetection;
                    intactBody.interpolation = profile.FragmentInterpolation;
                    root.IntactBody = intactBody;
                }

                // Intact child - visual + collision damage input. Reproduces the source
                // MeshFilter node's local TRS so child-node sources bake identically to
                // what the Edit-Mode preview shows.
                var intact = new GameObject("Intact");
                intact.transform.SetParent(buildRoot.transform, false);
                intact.transform.localPosition = filterLocalPos;
                intact.transform.localRotation = filterLocalRot;
                intact.transform.localScale = filterLocalScale;
                intact.layer = filter.gameObject.layer;
                var intactFilter = intact.AddComponent<MeshFilter>();
                intactFilter.sharedMesh = intactVisualMesh;
                var intactRenderer = intact.AddComponent<MeshRenderer>();
                intactRenderer.sharedMaterials = sourceMaterials;
                if (sourceRenderer != null)
                {
                    intactRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                    intactRenderer.receiveShadows = sourceRenderer.receiveShadows;
                    intactRenderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                    intactRenderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                }
                // An intact renderer with no explicit anchor samples one ambient value at
                // its bounds centre. Hundreds of fractured renderers with the same null
                // anchor each sample at their own bounds centre instead, exposing the cut
                // triangulation as broad light/dark patches before anything moves. Bake a
                // prefab-local anchor at the exact intact sample position (or copy the
                // authored custom anchor position) so the attached shell is indistinguishable
                // from Intact and never stores a scene-object reference in the prefab.
                var attachedProbeAnchorObject =
                    new GameObject("Attached Light Probe Anchor");
                attachedProbeAnchorObject.transform.SetParent(
                    buildRoot.transform, false);
                attachedProbeAnchorObject.layer = source.layer;
                Vector3 attachedProbeLocal = sourceRenderer != null &&
                    sourceRenderer.probeAnchor != null
                    ? source.transform.InverseTransformPoint(
                        sourceRenderer.probeAnchor.position)
                    : Matrix4x4.TRS(filterLocalPos, filterLocalRot,
                        filterLocalScale).MultiplyPoint3x4(
                            intactVisualMesh.bounds.center);
                attachedProbeAnchorObject.transform.localPosition =
                    attachedProbeLocal;
                Transform attachedProbeAnchor =
                    attachedProbeAnchorObject.transform;
                intactRenderer.probeAnchor = attachedProbeAnchor;
                Collider intactCollider;
                var intactColliders = new List<Collider>();
                var intactDynamicColliders = new List<Collider>();
                var intactColliderStructuralGroups = new List<int>();
                var intactDynamicColliderStructuralGroups = new List<int>();
                if (intactCollisionComponents.Count > 1)
                {
                    intactCollider = null;
                    for (int i = 0; i < intactCollisionComponents.Count; i++)
                    {
                        Mesh componentMesh = intactColliderMeshes.Count > i
                            ? intactColliderMeshes[i]
                            : null;
                        Collider part;
                        if (intactPanelBoxes[i].IsValid)
                        {
                            part = AddOrientedBoxCollider(intact,
                                intactPanelBoxes[i], i);
                        }
                        else if (profile.Colliders == ColliderStrategy.PrimitiveBox)
                        {
                            part = AddBoxCollider(intact,
                                intactCollisionComponents[i].ComputeBounds());
                        }
                        else if (profile.Colliders == ColliderStrategy.ExactMesh)
                        {
                            part = AddExactCollider(intact, componentMesh);
                            Collider dynamicPart = AddConvexCollider(intact, componentMesh);
                            part.enabled = profile.IntactBody != IntactBodyMode.Dynamic;
                            dynamicPart.enabled = profile.IntactBody == IntactBodyMode.Dynamic;
                            intactDynamicColliders.Add(dynamicPart);
                            intactDynamicColliderStructuralGroups.Add(i);
                        }
                        else
                        {
                            part = AddConvexCollider(intact, componentMesh);
                        }
                        intactColliders.Add(part);
                        intactColliderStructuralGroups.Add(i);
                        if (intactCollider == null) intactCollider = part;
                    }
                }
                else
                {
                    Mesh singleCollisionMesh = null;
                    bool usePanelBox = intactPanelBoxes.Count == 1 &&
                                       intactPanelBoxes[0].IsValid;
                    if (profile.Colliders != ColliderStrategy.PrimitiveBox && !usePanelBox)
                    {
                        singleCollisionMesh = intactCollisionComponents.Count == 1
                            ? intactCollisionComponents[0].ToMesh("IntactCollider_000")
                            : sourceMesh;
                        if (singleCollisionMesh != sourceMesh)
                            AssetDatabase.AddObjectToAsset(singleCollisionMesh, manifest);
                    }
                    if (usePanelBox)
                    {
                        intactCollider = AddOrientedBoxCollider(intact,
                            intactPanelBoxes[0], 0);
                    }
                    else if (profile.Colliders == ColliderStrategy.PrimitiveBox)
                    {
                        intactCollider = AddBoxCollider(intact,
                            intactCollisionComponents.Count == 1
                                ? intactCollisionComponents[0].ComputeBounds()
                                : sourceMesh.bounds);
                    }
                    else if (profile.Colliders == ColliderStrategy.ExactMesh)
                    {
                        intactCollider = AddExactCollider(intact, singleCollisionMesh);
                        Collider dynamicCollider = AddConvexCollider(
                            intact, singleCollisionMesh);
                        intactCollider.enabled =
                            profile.IntactBody != IntactBodyMode.Dynamic;
                        dynamicCollider.enabled =
                            profile.IntactBody == IntactBodyMode.Dynamic;
                        intactDynamicColliders.Add(dynamicCollider);
                        intactDynamicColliderStructuralGroups.Add(0);
                    }
                    else
                    {
                        intactCollider = AddConvexCollider(intact, singleCollisionMesh);
                    }
                    intactColliders.Add(intactCollider);
                    intactColliderStructuralGroups.Add(0);
                }
                root.IntactExactColliders = profile.Colliders == ColliderStrategy.ExactMesh
                    ? intactColliders
                    : new List<Collider>();
                root.IntactDynamicColliders = intactDynamicColliders;
                root.IntactColliders = new List<Collider>(intactColliders);
                root.IntactColliderStructuralGroups =
                    new List<int>(intactColliderStructuralGroups);
                for (int i = 0; i < intactDynamicColliders.Count; i++)
                {
                    root.IntactColliders.Add(intactDynamicColliders[i]);
                    root.IntactColliderStructuralGroups.Add(
                        intactDynamicColliderStructuralGroups[i]);
                }
                // A child collider attached to a parent Rigidbody is a compound collider;
                // Unity sends its collision callback to DestructibleRoot on buildRoot.
                // Static intact objects have no Rigidbody there, so retain the child relay.
                if (root.IntactBody == null)
                {
                    var relay = intact.AddComponent<DestructibleCollisionRelay>();
                    relay.Root = root;
                    // Oriented BoxColliders live on lightweight children so each authored
                    // modular panel can keep its own rotation. Static colliders do not send
                    // collision messages to a MonoBehaviour on their transform parent, so
                    // give those hosts the same narrow forwarding relay.
                    for (int i = 0; i < intactColliders.Count; i++)
                    {
                        Collider collider = intactColliders[i];
                        if (collider == null || collider.gameObject == intact) continue;
                        var childRelay = collider.gameObject
                            .AddComponent<DestructibleCollisionRelay>();
                        childRelay.Root = root;
                    }
                }

                if (profile.IntactBody == IntactBodyMode.ReleaseWhenUnsupported)
                {
                    var structure = buildRoot.AddComponent<StructuralSupportNode>();
                    structure.Root = root;
                    structure.IntactBody = root.IntactBody;
                    structure.ProbeCollider = intactCollider;
                    structure.ProbeColliders = intactColliders;
                    root.StructuralNode = structure;

                    if (!profile.CollisionDamage)
                    {
                        outcome.Warnings.Add(
                            "Structural release is enabled but Collision Damage is disabled. " +
                            "The object can fall, but ground impact will not fracture it automatically.");
                    }
                }

                // Fractured child - pre-baked pool of fragments, inactive until break.
                // Fragment centroids live in mesh space, so this container carries the
                // same filter-node local TRS as the Intact child.
                var fractured = new GameObject("Fractured");
                fractured.transform.SetParent(buildRoot.transform, false);
                fractured.transform.localPosition = filterLocalPos;
                fractured.transform.localRotation = filterLocalRot;
                fractured.transform.localScale = filterLocalScale;
                fractured.layer = source.layer;

                // Still-attached fragments must not cast their fracture caps and shared
                // renderer edges into the shadow map. This exterior proxy starts as the
                // intact mesh and is rebuilt by DestructibleRoot from only the exterior
                // submeshes that remain attached after every activation batch.
                var attachedShadowProxy = new GameObject("Attached Shadow Proxy");
                attachedShadowProxy.transform.SetParent(fractured.transform, false);
                attachedShadowProxy.layer = source.layer;
                var attachedShadowFilter = attachedShadowProxy.AddComponent<MeshFilter>();
                attachedShadowFilter.sharedMesh = intactVisualMesh;
                var attachedShadowRenderer =
                    attachedShadowProxy.AddComponent<MeshRenderer>();
                attachedShadowRenderer.sharedMaterials = sourceMaterials;
                attachedShadowRenderer.receiveShadows = false;
                attachedShadowRenderer.lightProbeUsage =
                    UnityEngine.Rendering.LightProbeUsage.Off;
                attachedShadowRenderer.reflectionProbeUsage =
                    UnityEngine.Rendering.ReflectionProbeUsage.Off;
                attachedShadowRenderer.shadowCastingMode = sourceRenderer != null &&
                    sourceRenderer.shadowCastingMode ==
                        UnityEngine.Rendering.ShadowCastingMode.Off
                    ? UnityEngine.Rendering.ShadowCastingMode.Off
                    : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
                root.AttachedShadowProxyFilter = attachedShadowFilter;
                root.AttachedShadowProxyRenderer = attachedShadowRenderer;

                int secondaryBodyCount = 0;
                float totalMass = 0f;
                for (int i = 0; i < fragMeshes.Count; i++)
                {
                    RequireProgress(
                        $"Building fragment objects {i + 1}/{fragMeshes.Count}...",
                        0.88f + (float)i / Mathf.Max(1, fragMeshes.Count) * 0.09f);
                    var go = new GameObject($"Frag_{i:000}");
                    go.transform.SetParent(fractured.transform, false);
                    go.transform.localPosition = fragCentroids[i];
                    go.layer = source.layer;

                    var mf = go.AddComponent<MeshFilter>();
                    mf.sharedMesh = fragMeshes[i];
                    var mr = go.AddComponent<MeshRenderer>();
                    Material[] fragmentMaterials = FragmentMaterialUtility.BuildForFragment(
                        fragMeshes[i], sourceMaterials, interior, useSourceInterior);
                    mr.sharedMaterials = fragmentMaterials;
                    if (sourceRenderer != null)
                    {
                        mr.shadowCastingMode = sourceRenderer.shadowCastingMode;
                        mr.receiveShadows = sourceRenderer.receiveShadows;
                        // Probe settings belong to the renderer state being reproduced just as much as
                        // the shadow flags do. Left at their constructor defaults, every fragment became
                        // an independent probe sampler taking its ambient at its own bounds centre, so a
                        // single wall turned into a few hundred slightly different ambient values and the
                        // fracture pattern read as shading seams before anything had even moved.
                        mr.lightProbeUsage = sourceRenderer.lightProbeUsage;
                        mr.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                    }
                    mr.probeAnchor = attachedProbeAnchor;
                    if (fragInteriorScatterMeshes[i] != null)
                    {
                        Material interiorScatterMaterial =
                            FragmentMaterialUtility.GetCutSurfaceMaterial(
                                fragmentMaterials, interior);
                        AddInteriorScatterRenderer(go, fragInteriorScatterMeshes[i],
                            interiorScatterMaterial, mr);
                    }


                    var record = new FragmentRecord
                    {
                        Transform = go.transform,
                        Renderer = mr,
                        AttachedProbeAnchor = attachedProbeAnchor,
                        InitialLocalPosition = go.transform.localPosition,
                        InitialLocalRotation = go.transform.localRotation,
                        InitialLocalScale = go.transform.localScale,
                        Volume = fracture.Volumes[i],
                        StructuralGroup = fragmentStructuralGroups[i],
                        BoundaryAnchor = glassBoundaryAnchors != null &&
                                         glassBoundaryAnchors[i],
                        GroundAnchor = groundAnchors != null &&
                                       groundAnchors[i],
                        SupportNeighbours = supportNeighbours != null
                            ? supportNeighbours[i]
                            : new List<int>(),
                        SupportNeighbourContactAreas = supportNeighbourContactAreas != null
                            ? supportNeighbourContactAreas[i]
                            : new List<float>()
                    };

                    Collider primaryCollider = null;
                    Collider primaryDynamicCollider = null;
                    var primaryDynamicColliders = new List<Collider>();
                    Rigidbody primaryBody = null;
                    if (profile.Colliders == ColliderStrategy.PrimitiveBox)
                    {
                        primaryCollider = AddBoxCollider(go, fragMeshes[i].bounds);
                    }
                    else if (profile.Colliders == ColliderStrategy.ExactMesh)
                    {
                        primaryCollider = AddExactCollider(go, fragMeshes[i]);
                        primaryDynamicColliders = AddConvexColliders(go,
                            fragDynamicColliderMeshes[i], false);
                    }
                    else
                    {
                        primaryCollider = AddStableConvexCollider(go, fragMeshes[i],
                            useStablePrimitiveColliders);
                        primaryDynamicColliders = AddConvexColliders(go,
                            fragDynamicColliderMeshes[i], false);
                    }

                    float mass = Mathf.Clamp(
                        fracture.Volumes[i] * scaleVolume * profile.Density,
                        profile.MinFragmentMass, profile.MaxFragmentMass);
                    // A large merged shell can expose hundreds of fragments after one local
                    // hit. Registering a kinematic Rigidbody and an enabled MonoBehaviour for
                    // every still-attached piece creates an avoidable PhysX/Update cost. Keep
                    // render/collision geometry only until ActivateFragment asks for physics.
                    // A level-two parent retains its body because its child hierarchy derives
                    // mass and carrier references from it during Bake.
                    bool needsBakedBody = !deferInactiveFragmentPhysics ||
                                           secondaryData.ContainsKey(i);
                    if (needsBakedBody)
                    {
                        primaryBody = go.AddComponent<Rigidbody>();
                        primaryBody.mass = mass;
#if UNITY_6000_0_OR_NEWER
                        primaryBody.linearDamping = profile.Drag;
                        primaryBody.angularDamping = profile.AngularDrag;
#else
                        primaryBody.drag = profile.Drag;
                        primaryBody.angularDrag = profile.AngularDrag;
#endif
                        primaryBody.isKinematic = true;
                        primaryBody.useGravity = profile.UseGravity;
                        primaryBody.collisionDetectionMode =
                            fragMeshes.Count >= 160
                                ? CollisionDetectionMode.Discrete
                                : profile.FragmentCollisionDetection;
                        primaryBody.interpolation = profile.FragmentInterpolation;
                        ConfigureFragmentSolver(primaryBody,
                            profile.Colliders == ColliderStrategy.ExactMesh,
                            fragMeshes.Count);
                    }

                    if (profile.Pattern != FracturePattern.RadialGlass)
                    {
                        var fr = go.AddComponent<FragmentRuntime>();
                        fr.Root = root;
                        fr.FragmentIndex = i;
                        fr.Level = 0;
                        fr.enabled = !deferInactiveFragmentPhysics;
                        Material dominantEffectMaterial =
                            FragmentMaterialUtility.GetDominantSourceMaterial(
                                fragMeshes[i], Mathf.Max(0, fragMeshes[i].subMeshCount - 1),
                                sourceMaterials);
                        if (dominantEffectMaterial != null && effectColors.TryGetValue(
                            dominantEffectMaterial, out Color primaryEffectColor))
                        {
                            fr.EffectColor = primaryEffectColor;
                            fr.HasEffectColor = true;
                        }
                    }

                    record.Body = primaryBody;
                    record.Collider = primaryCollider;
                    record.DynamicCollider = primaryDynamicCollider;
                    record.DynamicColliders = primaryDynamicColliders;
                    record.Mass = mass;
                    totalMass += mass;

                    if (secondaryData.TryGetValue(i, out var data))
                    {
                        record.SecondaryNode = AddSecondaryHierarchy(
                            go, root, i, primaryBody, primaryCollider,
                            primaryDynamicCollider, primaryDynamicColliders, mr,
                            data, sourceMaterials, effectColors, interior,
                            useSourceInterior, profile, useStablePrimitiveColliders,
                            ref secondaryBodyCount);
                    }

                    record.ProbeRenderers = new List<Renderer>(
                        go.GetComponentsInChildren<Renderer>(true));
                    record.ExteriorSubMeshCount =
                        Mathf.Max(0, fragMeshes[i].subMeshCount - 1);
                    record.DynamicShadowCastingModes = new List<
                        UnityEngine.Rendering.ShadowCastingMode>(
                        record.ProbeRenderers.Count);
                    for (int rendererIndex = 0;
                         rendererIndex < record.ProbeRenderers.Count; rendererIndex++)
                    {
                        Renderer fragmentRenderer =
                            record.ProbeRenderers[rendererIndex];
                        record.DynamicShadowCastingModes.Add(fragmentRenderer != null
                            ? fragmentRenderer.shadowCastingMode
                            : UnityEngine.Rendering.ShadowCastingMode.Off);
                        if (fragmentRenderer != null)
                            fragmentRenderer.shadowCastingMode =
                                UnityEngine.Rendering.ShadowCastingMode.Off;
                    }

                    root.Fragments.Add(record);
                }

                ValidatePrimaryFragmentIdentityOrThrow(root, profile.Pattern);

                fractured.SetActive(false);
                root.IntactRoot = intact;
                root.FracturedRoot = fractured;

                // -------------------------------------------------- prefab
                // SaveAsPrefabAsset overwrites an existing prefab in place and preserves
                // its GUID, so rebakes never orphan placed scene instances.
                bool prefabExisted =
                    AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
                RequireProgress("Saving generated prefab...", 0.975f, true);
                PrefabUtility.SaveAsPrefabAsset(buildRoot, prefabPath, out bool prefabOk);
                if (!prefabOk) throw new Exception("Prefab save failed (QD-OUT-001).");
                if (!prefabExisted) sessionAssets.Add(prefabPath);
                manifest.Entries.Add(new GeneratedManifest.Entry { AssetPath = prefabPath, Kind = "prefab" });

                manifest.BakeSeconds = (float)timer.Elapsed.TotalSeconds;
                manifest.Warnings.AddRange(outcome.Warnings);
                EditorUtility.SetDirty(manifest);
                // A batch flushes once when it finishes: saving here re-serialized every
                // dirty asset in the project for each object in the list. The prefab above
                // is already written before this call, so nothing depends on it per object.
                if (!batchProgress.HasValue) AssetDatabase.SaveAssets();

                // -------------------------------------------------- optional scene placement
                if (placeInScene && source.scene.IsValid())
                {
                    if (rebakingGeneratedPrefab && rebakeRoot != null)
                    {
                        // Saving the prefab updates its scene instance. Do not instantiate
                        // or disable anything here: this is the same authored object.
                        if (!string.Equals(rebakeRoot.gameObject.name, generatedRootName,
                                StringComparison.Ordinal))
                        {
                            Undo.RecordObject(rebakeRoot.gameObject,
                                "Quick Destruction Normalize Name");
                            rebakeRoot.gameObject.name = generatedRootName;
                            EditorUtility.SetDirty(rebakeRoot.gameObject);
                        }
                        Selection.activeGameObject = rebakeRoot.gameObject;
                        outcome.SceneInstance = rebakeRoot.gameObject;
                    }
                    else
                    {
                        GameObject instance = PlaceGeneratedInstance(
                            prefabPath, source, profile, null, placementParent);
                        if (instance != null)
                        {
                            Selection.activeGameObject = instance;
                            outcome.SceneInstance = instance;
                        }
                    }
                }

                outcome.Success = true;
                outcome.PrefabPath = prefabPath;
                outcome.Manifest = manifest;
                outcome.FragmentCount = fragMeshes.Count;
                outcome.SecondaryFragmentCount = secondaryFragmentCount;
                outcome.SecondaryBodyCount = secondaryBodyCount;
                outcome.TotalMass = totalMass;
                outcome.Seconds = (float)timer.Elapsed.TotalSeconds;
#pragma warning disable 0162
                if (FractureEngine.VerboseSolverProfiling)
                {
                    UnityEngine.Debug.Log($"[QD Profile] Bake Phase 4 (Asset Creation & Prefab Assembly): {swBakeStep.ElapsedMilliseconds}ms");
                    UnityEngine.Debug.Log($"[QD Profile] === BAKE TOTAL TIME: {timer.ElapsedMilliseconds} ms ({timer.Elapsed.TotalSeconds:F2} s) ===");
                }
                return outcome;
            }
            catch (OperationCanceledException)
            {
                foreach (var path in sessionAssets)
                    AssetDatabase.DeleteAsset(path);
                DeleteFolderIfEmpty(folder);
                outcome.Cancelled = true;
                outcome.Error = reusedOutput
                    ? "Bake cancelled. Existing generated output was being replaced; " +
                      "run Bake again to restore a complete set."
                    : "Bake cancelled. Newly generated session assets were removed and " +
                      "the source was left unchanged.";
                return outcome;
            }
            catch (Exception e)
            {
                // Transaction rollback: remove only files newly created by this session.
                // Reused output (rebake) is never deleted; it may need a repair rebake.
                foreach (var path in sessionAssets)
                    AssetDatabase.DeleteAsset(path);
                DeleteFolderIfEmpty(folder);
                outcome.Error = reusedOutput
                    ? $"Bake failed: {e.Message}. Existing generated output may be " +
                      "partially overwritten - run Bake again to repair it."
                    : $"Bake failed: {e.Message}";
                UnityEngine.Debug.LogException(e);
                return outcome;
            }
            finally
            {
                if (buildRoot != null) UnityEngine.Object.DestroyImmediate(buildRoot);
                if (embeddedSourceSnapshot != null)
                    UnityEngine.Object.DestroyImmediate(embeddedSourceSnapshot);
                // A batch owns one uninterrupted native progress window. Clearing it
                // after every object makes the bar flash/disappear and loses the only UI
                // that can repaint while the synchronous solver is running.
                if (!batchProgress.HasValue)
                    EditorUtility.ClearProgressBar();
            }
        }

        static void ValidatePrimaryFragmentIdentityOrThrow(
            DestructibleRoot root, FracturePattern pattern)
        {
            if (root == null || root.Fragments == null)
                throw new Exception("Primary fragment identity validation received no root (QD-ID-001).");

            var transforms = new HashSet<Transform>();
            for (int i = 0; i < root.Fragments.Count; i++)
            {
                FragmentRecord fragment = root.Fragments[i];
                if (fragment == null || fragment.Transform == null)
                    throw new Exception($"Fragment record {i} has no Transform (QD-ID-002).");
                if (!transforms.Add(fragment.Transform))
                    throw new Exception($"Fragment record {i} reuses another record's Transform (QD-ID-003).");
                if (fragment.StructuralGroup < 0)
                    throw new Exception($"Fragment record {i} has no structural group (QD-ID-004).");

                if (pattern != FracturePattern.RadialGlass)
                {
                    FragmentRuntime runtime =
                        fragment.Transform.GetComponent<FragmentRuntime>();
                    if (runtime == null || runtime.Root != root ||
                        runtime.FragmentIndex != i || runtime.Level != 0)
                        throw new Exception(
                            $"Fragment record {i} has an inconsistent runtime identity (QD-ID-005).");
                }

                if (fragment.Collider != null &&
                    fragment.Collider.transform != fragment.Transform &&
                    !fragment.Collider.transform.IsChildOf(fragment.Transform))
                    throw new Exception(
                        $"Fragment record {i} owns a collider from another hierarchy (QD-ID-006).");

                List<int> neighbours = fragment.SupportNeighbours;
                List<float> areas = fragment.SupportNeighbourContactAreas;
                if (neighbours == null) continue;
                if (areas != null && areas.Count != 0 &&
                    areas.Count != neighbours.Count)
                    throw new Exception(
                        $"Fragment record {i} has a misaligned support-area list (QD-ID-007).");

                var uniqueNeighbours = new HashSet<int>();
                for (int n = 0; n < neighbours.Count; n++)
                {
                    int neighbour = neighbours[n];
                    if (neighbour < 0 || neighbour >= root.Fragments.Count ||
                        neighbour == i || !uniqueNeighbours.Add(neighbour))
                        throw new Exception(
                            $"Fragment record {i} has an invalid support index {neighbour} (QD-ID-008).");
                }
            }
        }

        /// <summary>
        /// Puts the generated prefab where <paramref name="source"/> stands and disables the
        /// original, reusing an instance already sitting there instead of stacking a second
        /// one. Exposed so identical copies of one prefab can be set up from a single bake:
        /// they all resolve to the same generated prefab, so re-running the fracture solver
        /// per copy would only reproduce geometry that already exists.
        /// </summary>
        /// <summary>Name suffix of the container a batch groups its output under.</summary>
        internal const string OutputGroupSuffix = "_Destructibles";

        /// <summary>
        /// True for the meshless grouping object a Root + Nested batch leaves behind. A
        /// rebake selects that container, so it has to be recognised as the batch's own
        /// output rather than mistaken for one more authored hierarchy to bake.
        /// </summary>
        internal static bool IsOutputGroupContainer(GameObject candidate)
        {
            if (candidate == null ||
                !candidate.name.EndsWith(OutputGroupSuffix, StringComparison.Ordinal) ||
                candidate.GetComponent<MeshFilter>() != null ||
                candidate.GetComponent<DestructibleRoot>() != null)
                return false;
            // Every direct child is a generated destructible. An authored object that
            // merely happens to carry the suffix in its name is not this.
            Transform t = candidate.transform;
            if (t.childCount == 0) return false;
            for (int i = 0; i < t.childCount; i++)
            {
                if (t.GetChild(i).GetComponent<DestructibleRoot>() == null)
                    return false;
            }
            return true;
        }

        internal static GameObject PlaceGeneratedInstance(string prefabPath,
            GameObject source, DestructionProfile profile)
        {
            return PlaceGeneratedInstance(prefabPath, source, profile, null);
        }

        internal static GameObject PlaceGeneratedInstance(string prefabPath,
            GameObject source, DestructionProfile profile,
            IReadOnlyList<GameObject> originalSources)
        {
            return PlaceGeneratedInstance(prefabPath, source, profile,
                originalSources, null);
        }

        /// <summary>
        /// <paramref name="placementParent"/> collects the output of a batch under one
        /// object instead of dropping one loose destructible per nested mesh at the scene
        /// root. The pieces stay fully independent - this only parents them.
        /// </summary>
        internal static GameObject PlaceGeneratedInstance(string prefabPath,
            GameObject source, DestructionProfile profile,
            IReadOnlyList<GameObject> originalSources, Transform placementParent)
        {
            if (source == null || !source.scene.IsValid()) return null;
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null) return null;

            List<GameObject> existingInstances = FindOverlappingGeneratedInstances(
                prefabPath, source, profile, originalSources);
            GameObject instance = null;
            for (int i = 0; i < existingInstances.Count; i++)
            {
                GameObject candidate = existingInstances[i];
                if (candidate == null) continue;
                bool sameOutput = string.Equals(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate),
                    prefabPath, StringComparison.OrdinalIgnoreCase);
                if (sameOutput && instance == null)
                {
                    instance = candidate;
                    Undo.RecordObject(instance.transform, "Quick Destruction Update");
                    continue;
                }
                // Everything else here is either an exact overlapping copy of the output
                // we keep - those can only render and receive damage on top of one another
                // - or this source's previous output, which a new prefab path has just
                // superseded. Reusing a stale instance would leave the scene showing the
                // geometry of an earlier bake, so remove it through Undo instead.
                Undo.DestroyObjectImmediate(candidate);
            }
            if (instance == null)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    prefabAsset, source.scene);
                Undo.RegisterCreatedObjectUndo(instance, "Quick Destruction Setup");
            }
            instance.transform.SetPositionAndRotation(
                source.transform.position, source.transform.rotation);
            instance.transform.localScale = source.transform.lossyScale;
            if (placementParent != null && instance.scene == placementParent.gameObject.scene &&
                instance.transform.parent != placementParent &&
                !placementParent.IsChildOf(instance.transform))
            {
                Undo.SetTransformParent(instance.transform, placementParent,
                    "Quick Destruction Group Output");
                // Re-apply what the source defines: reparenting only preserves the world
                // transform exactly while the container itself is unrotated and unscaled.
                instance.transform.SetPositionAndRotation(
                    source.transform.position, source.transform.rotation);
            }
            var instanceRoot = instance.GetComponent<DestructibleRoot>();
            if (instanceRoot != null)
            {
                Undo.RecordObject(instanceRoot, "Quick Destruction Link Sources");
                instanceRoot.StableId = Guid.NewGuid().ToString("N");
                instanceRoot.OriginalSourceObject = source;
                instanceRoot.OriginalSourceObjects ??= new List<GameObject>();
                instanceRoot.OriginalSourceObjects.Clear();
                instanceRoot.OriginalSourceActiveStates ??= new List<bool>();
                instanceRoot.OriginalSourceActiveStates.Clear();
                instanceRoot.OriginalHierarchyRoot = null;
                instanceRoot.OriginalHierarchyRootActiveState = true;
                if (originalSources != null)
                {
                    for (int i = 0; i < originalSources.Count; i++)
                    {
                        GameObject original = originalSources[i];
                        if (original != null && original.scene == source.scene &&
                            !instanceRoot.OriginalSourceObjects.Contains(original))
                        {
                            instanceRoot.OriginalSourceObjects.Add(original);
                            instanceRoot.OriginalSourceActiveStates.Add(original.activeSelf);
                        }
                    }
                }
                if (instanceRoot.OriginalSourceObjects.Count == 0)
                {
                    instanceRoot.OriginalSourceObjects.Add(source);
                    instanceRoot.OriginalSourceActiveStates.Add(source.activeSelf);
                }
                // How this instance was produced has to survive on the instance itself. The
                // window's mode is session state that any domain reload drops back to
                // Selected Objects, and the linked roots alone cannot stand in for it: a
                // plain bake also leaves exactly one entry behind.
                instanceRoot.BakedAsWholeBuilding =
                    instanceRoot.BakedAsWholeBuilding ||
                    (originalSources != null && originalSources.Count > 0);
                EditorUtility.SetDirty(instanceRoot);
            }
            IReadOnlyList<GameObject> sourcesToDisable = originalSources != null &&
                                                        originalSources.Count > 0
                ? originalSources
                : new[] { source };
            var disabled = new HashSet<GameObject>();
            for (int i = 0; i < sourcesToDisable.Count; i++)
            {
                GameObject original = sourcesToDisable[i];
                if (original == null || original.scene != source.scene ||
                    !disabled.Add(original))
                    continue;
                Undo.RecordObject(original, "Quick Destruction Setup");
                original.SetActive(false);
                EditorUtility.SetDirty(original);
            }
            return instance;
        }

        /// <summary>
        /// Scene destructibles that this placement has to take over rather than sit on top
        /// of: any instance a previous bake of this same source produced, or an unlinked
        /// legacy instance of the same output/profile standing where the source stands.
        /// </summary>
        static List<GameObject> FindOverlappingGeneratedInstances(
            string prefabPath, GameObject source, DestructionProfile profile,
            IReadOnlyList<GameObject> originalSources)
        {
            var matches = new List<GameObject>();
            if (source == null || !source.scene.IsValid() ||
                string.IsNullOrEmpty(prefabPath))
                return matches;

            DestructibleRoot[] roots = Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            Vector3 position = source.transform.position;
            Quaternion rotation = source.transform.rotation;
            Vector3 scale = source.transform.lossyScale;
            for (int i = 0; i < roots.Length; i++)
            {
                DestructibleRoot root = roots[i];
                if (root == null || root.gameObject == source ||
                    root.gameObject.scene != source.scene ||
                    EditorUtility.IsPersistent(root))
                    continue;

                // A previous output of this very source is claimed wherever it now stands:
                // it describes the same object and only one of the two may remain.
                if (LinksGeneratedSource(root, source, originalSources))
                {
                    matches.Add(root.gameObject);
                    continue;
                }

                // Older outputs may have no source link. Only those can fall back to a
                // matching output/profile and transform: imported children commonly share
                // a profile and pivot without representing the same object.
                string rootPrefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject);
                bool sameOutput = string.Equals(rootPrefabPath, prefabPath,
                    StringComparison.OrdinalIgnoreCase);
                bool unlinkedLegacyOutput = root.OriginalSourceObject == null &&
                    (root.OriginalSourceObjects == null ||
                     root.OriginalSourceObjects.Count == 0);
                // A matching prefab and pivot still cannot take an output away from a
                // different source. Shared meshes and coincident pivots are valid in an
                // imported hierarchy. Linked outputs were matched by source above.
                if (!unlinkedLegacyOutput) continue;
                if (!sameOutput &&
                    !(profile != null && root.Profile == profile))
                    continue;

                Transform candidate = root.transform;
                if ((candidate.position - position).sqrMagnitude > 1e-8f ||
                    Quaternion.Angle(candidate.rotation, rotation) > 0.01f ||
                    (candidate.lossyScale - scale).sqrMagnitude > 1e-8f)
                    continue;
                matches.Add(root.gameObject);
            }
            return matches;
        }

        /// <summary>
        /// True when <paramref name="root"/> records that it replaced <paramref name="source"/>
        /// or any of <paramref name="originalSources"/>. These links are written by
        /// <see cref="PlaceGeneratedInstance"/> itself, so they are the only reliable identity
        /// an already placed destructible carries.
        /// </summary>
        static bool LinksGeneratedSource(DestructibleRoot root, GameObject source,
            IReadOnlyList<GameObject> originalSources)
        {
            if (root == null) return false;
            if (IsGeneratedSourceMatch(root.OriginalSourceObject, source, originalSources))
                return true;
            if (root.OriginalSourceObjects == null) return false;
            for (int i = 0; i < root.OriginalSourceObjects.Count; i++)
                if (IsGeneratedSourceMatch(root.OriginalSourceObjects[i], source,
                        originalSources))
                    return true;
            return false;
        }

        static bool IsGeneratedSourceMatch(GameObject link, GameObject source,
            IReadOnlyList<GameObject> originalSources)
        {
            if (link == null) return false;
            if (link == source) return true;
            if (originalSources == null) return false;
            for (int i = 0; i < originalSources.Count; i++)
                if (originalSources[i] != null && originalSources[i] == link)
                    return true;
            return false;
        }

        static SecondaryFragmentNode AddSecondaryHierarchy(
            GameObject parent, DestructibleRoot root, int parentIndex,
            Rigidbody parentBody, Collider parentCollider,
            Collider parentDynamicCollider, List<Collider> parentDynamicColliders,
            Renderer parentRenderer,
            SecondaryBakeData data, Material[] sourceMaterials,
            Dictionary<Material, Color> effectColors,
            Material interior, bool useSourceInterior,
            DestructionProfile profile, bool useStablePrimitiveColliders,
            ref int bodyCount)
        {
            var node = parent.AddComponent<SecondaryFragmentNode>();
            node.Root = root;
            node.ParentFragmentIndex = parentIndex;
            node.ParentBody = parentBody;
            node.ParentCollider = parentCollider;
            node.ParentDynamicCollider = parentDynamicCollider;
            node.ParentDynamicColliders = parentDynamicColliders ?? new List<Collider>();
            node.ParentRenderer = parentRenderer;

            var secondaryRoot = new GameObject("Secondary Fragments");
            secondaryRoot.transform.SetParent(parent.transform, false);
            node.SecondaryRoot = secondaryRoot;

            float totalVolume = 0f;
            for (int i = 0; i < data.Volumes.Count; i++)
                totalVolume += data.Volumes[i];
            totalVolume = Mathf.Max(totalVolume, 1e-8f);

            for (int i = 0; i < data.Meshes.Count; i++)
            {
                var child = new GameObject($"Secondary_{i:00}");
                child.transform.SetParent(secondaryRoot.transform, false);
                child.transform.localPosition = data.LocalPositions[i];
                child.layer = parent.layer;

                var filter = child.AddComponent<MeshFilter>();
                filter.sharedMesh = data.Meshes[i];
                var renderer = child.AddComponent<MeshRenderer>();
                Material[] childMaterials = FragmentMaterialUtility.BuildForFragment(
                    data.Meshes[i], sourceMaterials, interior, useSourceInterior);
                renderer.sharedMaterials = childMaterials;
                if (parentRenderer != null)
                {
                    renderer.shadowCastingMode = parentRenderer.shadowCastingMode;
                    renderer.receiveShadows = parentRenderer.receiveShadows;
                    renderer.lightProbeUsage = parentRenderer.lightProbeUsage;
                    renderer.reflectionProbeUsage = parentRenderer.reflectionProbeUsage;
                    renderer.probeAnchor = parentRenderer.probeAnchor;
                }
                if (i < data.InteriorScatterMeshes.Count &&
                    data.InteriorScatterMeshes[i] != null)
                {
                    Material scatterMaterial =
                        FragmentMaterialUtility.GetCutSurfaceMaterial(
                            childMaterials, interior);
                    AddInteriorScatterRenderer(child, data.InteriorScatterMeshes[i],
                        scatterMaterial, renderer);
                }

                var record = new FragmentRecord
                {
                    Transform = child.transform,
                    Renderer = renderer,
                    AttachedProbeAnchor = parentRenderer != null
                        ? parentRenderer.probeAnchor
                        : null,
                    ProbeRenderers = new List<Renderer>(
                        child.GetComponentsInChildren<Renderer>(true)),
                    ExteriorSubMeshCount =
                        Mathf.Max(0, data.Meshes[i].subMeshCount - 1),
                    InitialLocalPosition = child.transform.localPosition,
                    InitialLocalRotation = child.transform.localRotation,
                    InitialLocalScale = child.transform.localScale,
                    Volume = data.Volumes[i]
                };

                record.DynamicShadowCastingModes = new List<
                    UnityEngine.Rendering.ShadowCastingMode>(
                    record.ProbeRenderers.Count);
                for (int rendererIndex = 0;
                     rendererIndex < record.ProbeRenderers.Count; rendererIndex++)
                {
                    Renderer childRenderer = record.ProbeRenderers[rendererIndex];
                    record.DynamicShadowCastingModes.Add(childRenderer != null
                        ? childRenderer.shadowCastingMode
                        : UnityEngine.Rendering.ShadowCastingMode.Off);
                    if (childRenderer != null)
                        childRenderer.shadowCastingMode =
                            UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                Collider collider;
                Collider dynamicCollider = null;
                var dynamicColliders = new List<Collider>();
                if (profile.Colliders == ColliderStrategy.PrimitiveBox)
                {
                    collider = AddBoxCollider(child, data.Meshes[i].bounds);
                }
                else if (profile.Colliders == ColliderStrategy.ExactMesh)
                {
                    collider = AddExactCollider(child, data.Meshes[i]);
                    dynamicColliders = AddConvexColliders(child,
                        data.DynamicColliderMeshes[i], false);
                }
                else
                {
                    collider = AddStableConvexCollider(child, data.Meshes[i],
                        useStablePrimitiveColliders);
                    dynamicColliders = AddConvexColliders(child,
                        data.DynamicColliderMeshes[i], false);
                }
                var body = child.AddComponent<Rigidbody>();
                body.mass = Mathf.Clamp(
                    parentBody.mass * (data.Volumes[i] / totalVolume),
                    profile.MinFragmentMass, profile.MaxFragmentMass);
#if UNITY_6000_0_OR_NEWER
                body.linearDamping = profile.Drag;
                body.angularDamping = profile.AngularDrag;
#else
                body.drag = profile.Drag;
                body.angularDrag = profile.AngularDrag;
#endif
                body.isKinematic = true;
                body.useGravity = profile.UseGravity;
                body.collisionDetectionMode =
                    data.Meshes.Count >= 96
                        ? CollisionDetectionMode.Discrete
                        : profile.FragmentCollisionDetection;
                body.interpolation = profile.FragmentInterpolation;
                ConfigureFragmentSolver(body,
                    profile.Colliders == ColliderStrategy.ExactMesh,
                    data.Meshes.Count);

                var runtime = child.AddComponent<FragmentRuntime>();
                runtime.Root = root;
                runtime.FragmentIndex = parentIndex;
                runtime.Level = 1;
                Material dominantEffectMaterial =
                    FragmentMaterialUtility.GetDominantSourceMaterial(
                        data.Meshes[i], Mathf.Max(0, data.Meshes[i].subMeshCount - 1),
                        sourceMaterials);
                if (dominantEffectMaterial != null && effectColors != null &&
                    effectColors.TryGetValue(dominantEffectMaterial,
                        out Color secondaryEffectColor))
                {
                    runtime.EffectColor = secondaryEffectColor;
                    runtime.HasEffectColor = true;
                }

                record.Body = body;
                record.Collider = collider;
                record.DynamicCollider = dynamicCollider;
                record.DynamicColliders = dynamicColliders;
                record.Mass = body.mass;
                bodyCount++;

                node.Children.Add(record);
            }

            secondaryRoot.SetActive(false);
            return node;
        }

        // ------------------------------------------------------------------ clean

        public static int CleanGenerated(GeneratedManifest manifest)
        {
            return CleanGenerated(manifest, out _, out _);
        }

        public static int CleanGenerated(GeneratedManifest manifest,
            out int removedSceneInstances, out int reactivatedSources)
        {
            removedSceneInstances = 0;
            reactivatedSources = 0;
            if (manifest == null) return 0;
            RestoreSceneInstances(manifest,
                out removedSceneInstances, out reactivatedSources);
            string manifestPath = AssetDatabase.GetAssetPath(manifest);
            int removed = 0;
            foreach (var entry in manifest.Entries)
            {
                if (entry.AssetPath == manifestPath) continue;
                if (AssetDatabase.DeleteAsset(entry.AssetPath)) removed++;
            }
            if (AssetDatabase.DeleteAsset(manifestPath)) removed++;
            // Remove the per-bake output folder once nothing is left inside it.
            if (!string.IsNullOrEmpty(manifestPath))
            {
                int slash = manifestPath.LastIndexOf('/');
                if (slash > 0)
                    DeleteFolderIfEmpty(manifestPath.Substring(0, slash));
            }
            return removed;
        }

        /// <summary>
        /// Removes every bake set through its ownership manifest. Quick Profiles and any
        /// asset not explicitly listed by a manifest are intentionally preserved.
        /// </summary>
        public static int CleanAllGenerated(out int cleanedBakeSets,
            out int removedSceneInstances, out int reactivatedSources)
        {
            removedSceneInstances = 0;
            reactivatedSources = 0;
            int removed = 0;
            cleanedBakeSets = 0;
            var countedManifestPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            // AssetDatabase can expose a manifest scheduled by an import/refresh only
            // after the first deletion pass. Resolve every newly visible or temporarily
            // undeletable bake set inside this one command instead of requiring a second
            // click in the window.
            for (int pass = 0; pass < 4; pass++)
            {
                List<GeneratedManifest> manifests = FindAllManifests();
                if (manifests.Count == 0) break;
                var manifestPaths = new List<string>(manifests.Count);
                for (int i = 0; i < manifests.Count; i++)
                {
                    string path = AssetDatabase.GetAssetPath(manifests[i]);
                    if (!string.IsNullOrEmpty(path)) manifestPaths.Add(path);
                }
                if (manifestPaths.Count == 0) break;

                for (int i = 0; i < manifestPaths.Count; i++)
                {
                    GeneratedManifest manifest =
                        AssetDatabase.LoadAssetAtPath<GeneratedManifest>(manifestPaths[i]);
                    if (manifest == null) continue;
                    removed += CleanGenerated(manifest,
                        out int sceneInstances, out int sources);
                    removedSceneInstances += sceneInstances;
                    reactivatedSources += sources;
                    if (countedManifestPaths.Add(manifestPaths[i]))
                        cleanedBakeSets++;
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            // An unpacked generated prefab no longer has an asset path and older bake
            // versions may not match the current profile name. Its explicit source link
            // is still authoritative and lets Clean All restore it safely.
            RestoreLinkedGeneratedSceneInstances(
                out int linkedInstances, out int linkedSources);
            removedSceneInstances += linkedInstances;
            reactivatedSources += linkedSources;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return removed;
        }

        static List<GameObject> GetLinkedOriginalSources(DestructibleRoot root)
        {
            var sources = new List<GameObject>();
            if (root == null) return sources;
            if (root.OriginalSourceObjects != null)
            {
                for (int i = 0; i < root.OriginalSourceObjects.Count; i++)
                {
                    GameObject source = root.OriginalSourceObjects[i];
                    if (source != null && source != root.gameObject &&
                        source.scene.IsValid() &&
                        source.scene == root.gameObject.scene && !sources.Contains(source))
                        sources.Add(source);
                }
            }
            GameObject legacySource = root.OriginalSourceObject;
            if (legacySource != null && legacySource != root.gameObject &&
                legacySource.scene.IsValid() &&
                legacySource.scene == root.gameObject.scene &&
                !sources.Contains(legacySource))
                sources.Add(legacySource);
            GameObject hierarchyRoot = root.OriginalHierarchyRoot;
            if (hierarchyRoot != null && hierarchyRoot != root.gameObject &&
                hierarchyRoot.scene.IsValid() &&
                hierarchyRoot.scene == root.gameObject.scene &&
                !sources.Contains(hierarchyRoot))
                sources.Add(hierarchyRoot);
            return sources;
        }

        static bool GetAuthoredActiveState(DestructibleRoot root, GameObject source)
        {
            if (root != null && source != null && root.OriginalSourceObjects != null &&
                root.OriginalSourceActiveStates != null)
            {
                int index = root.OriginalSourceObjects.IndexOf(source);
                if (index >= 0 && index < root.OriginalSourceActiveStates.Count)
                    return root.OriginalSourceActiveStates[index];
            }
            if (root != null && source != null &&
                root.OriginalHierarchyRoot == source)
                return root.OriginalHierarchyRootActiveState;
            // Legacy outputs only linked one active source which placement disabled.
            return true;
        }

        static void RestoreLinkedGeneratedSceneInstances(
            out int removedInstances, out int reactivatedSources)
        {
            removedInstances = 0;
            reactivatedSources = 0;
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            DestructibleRoot[] roots = Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            var matches = new List<DestructibleRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                DestructibleRoot root = roots[i];
                if (root == null || !root.gameObject.scene.IsValid() ||
                    EditorUtility.IsPersistent(root))
                    continue;
                if (GetLinkedOriginalSources(root).Count == 0)
                    continue;
                matches.Add(root);
            }

            for (int i = 0; i < matches.Count; i++)
            {
                DestructibleRoot root = matches[i];
                List<GameObject> sources = GetLinkedOriginalSources(root);
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    GameObject source = sources[sourceIndex];
                    if (source == null) continue;
                    bool authoredActive = GetAuthoredActiveState(root, source);
                    if (source.activeSelf == authoredActive) continue;
                    Undo.RecordObject(source, "Quick Destruction Clean All - Restore Source");
                    source.SetActive(authoredActive);
                    EditorUtility.SetDirty(source);
                    if (authoredActive) reactivatedSources++;
                }
                Transform container = root.transform.parent;
                Undo.DestroyObjectImmediate(root.gameObject);
                removedInstances++;
                DestroyEmptyOutputGroup(container);
            }
        }

        static void RestoreSceneInstances(GeneratedManifest manifest,
            out int removedInstances, out int reactivatedSources)
        {
            removedInstances = 0;
            reactivatedSources = 0;
            if (manifest == null || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            string prefabPath = null;
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                GeneratedManifest.Entry entry = manifest.Entries[i];
                if (entry != null && entry.Kind == "prefab")
                {
                    prefabPath = entry.AssetPath;
                    break;
                }
            }

            DestructibleRoot[] roots = Resources.FindObjectsOfTypeAll<DestructibleRoot>();
            var matches = new List<DestructibleRoot>();
            for (int i = 0; i < roots.Length; i++)
            {
                DestructibleRoot root = roots[i];
                if (root == null || !root.gameObject.scene.IsValid() ||
                    EditorUtility.IsPersistent(root))
                    continue;
                string instancePrefabPath =
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject);
                bool ownsInstance = !string.IsNullOrEmpty(prefabPath) &&
                    string.Equals(instancePrefabPath, prefabPath,
                        StringComparison.OrdinalIgnoreCase);
                bool hasExplicitSource = root.OriginalSourceObject != null &&
                    root.OriginalSourceObject.scene == root.gameObject.scene &&
                    SourceMatchesManifest(root.OriginalSourceObject, manifest) &&
                    root.Profile != null &&
                    string.Equals(root.Profile.name, manifest.ProfileName,
                        StringComparison.Ordinal);
                if (ownsInstance || hasExplicitSource) matches.Add(root);
            }

            for (int i = 0; i < matches.Count; i++)
            {
                DestructibleRoot root = matches[i];
                List<GameObject> sources = GetLinkedOriginalSources(root);
                if (sources.Count == 0)
                {
                    GameObject legacy = FindLegacyOriginalSource(root, manifest);
                    if (legacy != null) sources.Add(legacy);
                }
                for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    GameObject source = sources[sourceIndex];
                    if (source == null) continue;
                    bool authoredActive = GetAuthoredActiveState(root, source);
                    if (source.activeSelf == authoredActive) continue;
                    Undo.RecordObject(source, "Quick Destruction Clean - Restore Source");
                    source.SetActive(authoredActive);
                    EditorUtility.SetDirty(source);
                    if (authoredActive) reactivatedSources++;
                }
                Transform container = root.transform.parent;
                Undo.DestroyObjectImmediate(root.gameObject);
                removedInstances++;
                DestroyEmptyOutputGroup(container);
            }
        }

        /// <summary>
        /// Removes the container a batch created once Clean has taken the last
        /// destructible out of it. An empty grouping object left in the Hierarchy is
        /// noise, and it is only removed while it holds nothing else.
        /// </summary>
        static void DestroyEmptyOutputGroup(Transform container)
        {
            if (container == null || EditorUtility.IsPersistent(container) ||
                !container.gameObject.scene.IsValid() || container.childCount > 0 ||
                !container.name.EndsWith(OutputGroupSuffix, StringComparison.Ordinal) ||
                container.GetComponents<Component>().Length > 1)
                return;
            Undo.DestroyObjectImmediate(container.gameObject);
        }

        static GameObject FindLegacyOriginalSource(DestructibleRoot generatedRoot,
            GeneratedManifest manifest)
        {
            if (generatedRoot == null || manifest == null) return null;
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject candidate = objects[i];
                if (candidate == null || !candidate.scene.IsValid() ||
                    candidate.scene != generatedRoot.gameObject.scene ||
                    EditorUtility.IsPersistent(candidate) || candidate.activeSelf ||
                    candidate.GetComponent<DestructibleRoot>() != null ||
                    !string.Equals(candidate.name, manifest.SourceName,
                        StringComparison.Ordinal))
                    continue;
                Transform a = candidate.transform;
                Transform b = generatedRoot.transform;
                if ((a.position - b.position).sqrMagnitude > 1e-8f ||
                    Quaternion.Angle(a.rotation, b.rotation) > 0.01f ||
                    (a.lossyScale - b.lossyScale).sqrMagnitude > 1e-8f)
                    continue;
                MeshFilter filter = candidate.GetComponentInChildren<MeshFilter>(true);
                if (filter == null || filter.sharedMesh == null) continue;
                string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                string meshGuid = string.IsNullOrEmpty(meshPath)
                    ? string.Empty
                    : AssetDatabase.AssetPathToGUID(meshPath);
                if (!string.IsNullOrEmpty(manifest.SourceGuid) &&
                    !string.Equals(meshGuid, manifest.SourceGuid,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                return candidate;
            }
            return null;
        }

        static bool SourceMatchesManifest(GameObject source,
            GeneratedManifest manifest)
        {
            if (source == null || manifest == null ||
                !string.Equals(source.name, manifest.SourceName,
                    StringComparison.Ordinal))
                return false;
            if (string.IsNullOrEmpty(manifest.SourceGuid)) return true;
            MeshFilter filter = source.GetComponentInChildren<MeshFilter>(true);
            if (filter == null || filter.sharedMesh == null) return false;
            string path = AssetDatabase.GetAssetPath(filter.sharedMesh);
            return !string.IsNullOrEmpty(path) &&
                   string.Equals(AssetDatabase.AssetPathToGUID(path),
                       manifest.SourceGuid, StringComparison.OrdinalIgnoreCase);
        }

        public static List<GeneratedManifest> FindAllManifests()
        {
            var list = new List<GeneratedManifest>();
            // This is called while drawing the Bake tab. Searching the entire project on
            // every Repaint scales badly in asset-heavy projects; generated manifests are
            // owned by one known folder.
            if (!AssetDatabase.IsValidFolder(GeneratedRootFolder)) return list;
            foreach (var guid in AssetDatabase.FindAssets(
                         "t:GeneratedManifest", new[] { GeneratedRootFolder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<GeneratedManifest>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (m != null) list.Add(m);
            }
            return list;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Piece count at which a ConvexMesh bake gives up per-fragment convex hulls. Box
        /// colliders are an oversized approximation that interpenetrates neighbours, so this
        /// stays far above what a dense building actually produces.
        /// </summary>
        const int StableBoxColliderFragmentLimit = 400;

        internal static void BuildSupportConnectivityData(IReadOnlyList<WorkMesh> fragments,
            Bounds sourceBounds, IReadOnlyList<int> structuralGroups,
            bool bakeBoundaryAnchors,
            out List<int>[] neighbours, out List<float>[] neighbourContactAreas,
            out bool[] boundaryAnchors,
            out bool[] groundAnchors)
        {
            int count = fragments.Count;
            neighbours = new List<int>[count];
            neighbourContactAreas = new List<float>[count];
            boundaryAnchors = bakeBoundaryAnchors ? new bool[count] : null;
            groundAnchors = new bool[count];
            for (int i = 0; i < count; i++)
            {
                neighbours[i] = new List<int>();
                neighbourContactAreas[i] = new List<float>();
            }
            if (count == 0) return;

            Vector3 sourceSize = sourceBounds.size;
            float largestSize = Mathf.Max(sourceSize.x,
                Mathf.Max(sourceSize.y, sourceSize.z));
            float weld = Mathf.Max(1e-5f, largestSize * 0.0005f);
            float edgeTolerance = Mathf.Max(2e-5f, largestSize * 0.001f);

            int thinAxis = sourceSize.x <= sourceSize.y && sourceSize.x <= sourceSize.z
                ? 0
                : sourceSize.y <= sourceSize.z ? 1 : 2;
            int planeAxisA = thinAxis == 0 ? 1 : 0;
            int planeAxisB = thinAxis == 2 ? 1 : 2;

            var fragmentBounds = new Bounds[count];
            var fragmentReferenceAreas = new float[count];
            var groupMinY = new Dictionary<int, float>();
            var groupMaxY = new Dictionary<int, float>();
            float globalMinY = float.MaxValue;

            var ownersByVertex = new Dictionary<Vector3Int, List<int>>();
            var positionByVertex = new Dictionary<Vector3Int, Vector3>();
            for (int fragmentIndex = 0; fragmentIndex < count; fragmentIndex++)
            {
                WorkMesh fragment = fragments[fragmentIndex];
                Bounds bounds = fragment.ComputeBounds();
                fragmentBounds[fragmentIndex] = bounds;
                float volume = fragment.ComputeVolume();
                fragmentReferenceAreas[fragmentIndex] = volume > 1e-9f
                    ? Mathf.Pow(volume, 2f / 3f)
                    : 0f;
                if (boundaryAnchors != null)
                {
                    boundaryAnchors[fragmentIndex] =
                        TouchesBoundary(bounds, sourceBounds, planeAxisA, edgeTolerance) ||
                        TouchesBoundary(bounds, sourceBounds, planeAxisB, edgeTolerance);
                }

                int group = structuralGroups != null &&
                            fragmentIndex < structuralGroups.Count
                    ? structuralGroups[fragmentIndex]
                    : -1;
                globalMinY = Mathf.Min(globalMinY, bounds.min.y);
                groupMinY[group] = groupMinY.TryGetValue(group, out float minY)
                    ? Mathf.Min(minY, bounds.min.y)
                    : bounds.min.y;
                groupMaxY[group] = groupMaxY.TryGetValue(group, out float maxY)
                    ? Mathf.Max(maxY, bounds.max.y)
                    : bounds.max.y;

                var uniqueVertices = new Dictionary<Vector3Int, Vector3>();
                for (int v = 0; v < fragment.Positions.Count; v++)
                {
                    Vector3 p = fragment.Positions[v];
                    var key = new Vector3Int(
                        Mathf.RoundToInt(p.x / weld),
                        Mathf.RoundToInt(p.y / weld),
                        Mathf.RoundToInt(p.z / weld));
                    if (!uniqueVertices.ContainsKey(key))
                        uniqueVertices.Add(key, p);
                }

                foreach (KeyValuePair<Vector3Int, Vector3> vertex in uniqueVertices)
                {
                    Vector3Int key = vertex.Key;
                    if (!ownersByVertex.TryGetValue(key, out List<int> owners))
                    {
                        owners = new List<int>(2);
                        ownersByVertex.Add(key, owners);
                        positionByVertex.Add(key, vertex.Value);
                    }
                    owners.Add(fragmentIndex);
                }
            }

            // A corner or line contact has no bonded area and must not carry a large shard.
            // Older bakes treated any two common points as full support, which could leave a
            // triangular cantilever hanging from one visual edge. Contact areas are computed
            // once here and later reused by the event-driven support sweep; there is no
            // per-frame mesh or collider query.
            var sharedPoints = new Dictionary<long, List<Vector3>>();
            foreach (KeyValuePair<Vector3Int, List<int>> vertex in ownersByVertex)
            {
                List<int> owners = vertex.Value;
                for (int a = 0; a < owners.Count; a++)
                {
                    for (int b = a + 1; b < owners.Count; b++)
                    {
                        int lo = Mathf.Min(owners[a], owners[b]);
                        int hi = Mathf.Max(owners[a], owners[b]);
                        long pair = ((long)lo << 32) | (uint)hi;
                        if (!sharedPoints.TryGetValue(pair, out List<Vector3> points))
                        {
                            points = new List<Vector3>(4);
                            sharedPoints.Add(pair, points);
                        }
                        points.Add(positionByVertex[vertex.Key]);
                    }
                }
            }

            float minimumResolvedArea = weld * weld;
            foreach (KeyValuePair<long, List<Vector3>> pair in sharedPoints)
            {
                float contactArea = MaxSharedContactTriangleArea(pair.Value);
                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffffL);
                // Besides rejecting zero-area lines, discard a face smaller than one
                // percent of the smaller fragment's characteristic area. This catches a
                // true but microscopic ligament without making ordinary thin-wall cuts
                // brittle. The retained-area sweep handles larger contacts progressively.
                float fragmentScaleArea = Mathf.Min(fragmentReferenceAreas[a],
                    fragmentReferenceAreas[b]);
                float minimumStructuralArea = fragmentScaleArea > 0f
                    ? fragmentScaleArea * 0.01f
                    : 0f;
                if (contactArea < Mathf.Max(minimumResolvedArea,
                        minimumStructuralArea))
                    continue;
                neighbours[a].Add(b);
                neighbourContactAreas[a].Add(contactArea);
                neighbours[b].Add(a);
                neighbourContactAreas[b].Add(contactArea);
            }

            // Ground anchors are the bottom fragments of each structural group that
            // reaches the object's lowest point. A carried deck or beam floats above
            // that level and intentionally receives no anchor: its release stays
            // governed by the structural group rule, never by the connectivity sweep.
            float groundTolerance = Mathf.Max(2e-5f,
                Mathf.Max(1e-5f, sourceSize.y) * 0.02f);
            for (int i = 0; i < count; i++)
            {
                int group = structuralGroups != null && i < structuralGroups.Count
                    ? structuralGroups[i]
                    : -1;
                if (!groupMinY.TryGetValue(group, out float groupBaseY) ||
                    groupBaseY > globalMinY + groundTolerance)
                    continue;
                float groupHeight = Mathf.Max(1e-5f, groupMaxY[group] - groupBaseY);
                float tolerance = Mathf.Max(2e-5f, groupHeight * 0.02f);
                groundAnchors[i] =
                    fragmentBounds[i].min.y <= groupBaseY + tolerance;
            }
        }

        internal static float MaxSharedContactTriangleArea(
            IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 3) return 0f;
            float largest = 0f;
            for (int a = 0; a < points.Count - 2; a++)
            {
                for (int b = a + 1; b < points.Count - 1; b++)
                {
                    Vector3 edge = points[b] - points[a];
                    for (int c = b + 1; c < points.Count; c++)
                    {
                        float area = Vector3.Cross(edge,
                            points[c] - points[a]).magnitude * 0.5f;
                        if (area > largest) largest = area;
                    }
                }
            }
            return largest;
        }

        static bool TouchesBoundary(Bounds fragment, Bounds source,
            int axis, float tolerance)
        {
            return GetAxis(fragment.min, axis) <= GetAxis(source.min, axis) + tolerance ||
                   GetAxis(fragment.max, axis) >= GetAxis(source.max, axis) - tolerance;
        }

        static float GetAxis(Vector3 value, int axis)
        {
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        /// <summary>
        /// Adds purely visual interior scatter below a fragment.  It deliberately has
        /// no Rigidbody or Collider: caps remain the authoritative closed shape for
        /// support, convex cooking and runtime collision.
        /// </summary>
        static void AddInteriorScatterRenderer(GameObject fragment, Mesh mesh,
            Material material, Renderer sourceRenderer)
        {
            if (fragment == null || mesh == null || material == null) return;
            var detail = new GameObject("Interior Scatter");
            detail.transform.SetParent(fragment.transform, false);
            detail.layer = fragment.layer;
            var filter = detail.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = detail.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (sourceRenderer != null)
            {
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
                renderer.probeAnchor = sourceRenderer.probeAnchor;
            }
        }

        /// <summary>
        /// Chooses collision topology independently from structural topology. Structural
        /// probes intentionally see stitched neighbouring modules as one building; an
        /// intact convex collider must instead retain each authored panel boundary or a
        /// wall/floor corner becomes a solid triangular prism across the room.
        /// </summary>
        internal static List<WorkMesh> BuildIntactCollisionComponents(
            WorkMesh structuralSource, WorkMesh preparedSource,
            IReadOnlyList<WorkMesh> mergedModuleSources, bool mergedAssembly,
            bool solidify, float solidifyThickness)
        {
            var result = new List<WorkMesh>();
            if (mergedAssembly && mergedModuleSources != null &&
                mergedModuleSources.Count > 0)
            {
                float thickness = Mathf.Max(0.001f, solidifyThickness);
                for (int moduleIndex = 0;
                     moduleIndex < mergedModuleSources.Count; moduleIndex++)
                {
                    WorkMesh module = mergedModuleSources[moduleIndex];
                    if (module == null) continue;
                    List<WorkMesh> moduleIslands = module.SplitConnectedComponents();
                    for (int islandIndex = 0;
                         islandIndex < moduleIslands.Count; islandIndex++)
                    {
                        WorkMesh island = moduleIslands[islandIndex];
                        if (island == null || island.TriangleCount == 0) continue;
                        // A mixed modular building can contain Plane panels beside walls
                        // that already have authored depth. Only the open panel needs a
                        // generated shell; solidifying a closed wall again would replace
                        // its intended thickness with the profile value and inflate its
                        // collision topology.
                        result.Add(solidify && !island.IsClosedManifold()
                            ? island.Solidified(thickness)
                            : island.Clone());
                    }
                }
            }

            // Direct rebake calls may not carry the transient module map. The prepared
            // solver source still supplies real thickness, which is safer than returning
            // to the former flat/zero-volume collider path.
            if (result.Count == 0 && mergedAssembly && solidify &&
                preparedSource != null)
                result.AddRange(preparedSource.SplitConnectedComponents());

            if (result.Count == 0 && structuralSource != null)
                result.AddRange(structuralSource.SplitConnectedComponents());

            return result;
        }

        internal struct OrientedBox
        {
            public Vector3 Center;
            public Vector3 Size;
            public Quaternion Rotation;
            public bool IsValid;
        }

        /// <summary>
        /// Recognizes an exact rectangular prism without relying on triangle diagonals.
        /// A solidified authored Plane has eight unique corners even though its render mesh
        /// contains duplicated hard-normal/rim vertices. Arbitrary or bevelled modules do
        /// not satisfy the eight-corner test and safely retain their convex MeshCollider.
        /// </summary>
        internal static bool TryGetOrientedBox(WorkMesh mesh, out OrientedBox box)
        {
            box = default;
            if (mesh == null || mesh.Positions == null || mesh.Positions.Count < 8)
                return false;

            Bounds bounds = mesh.ComputeBounds();
            float scale = Mathf.Max(bounds.size.magnitude, 0.001f);
            float tolerance = Mathf.Max(scale * 1e-5f, 1e-6f);
            float toleranceSqr = tolerance * tolerance;
            var corners = new List<Vector3>(8);
            for (int i = 0; i < mesh.Positions.Count; i++)
            {
                Vector3 point = mesh.Positions[i];
                bool duplicate = false;
                for (int j = 0; j < corners.Count; j++)
                {
                    if ((corners[j] - point).sqrMagnitude > toleranceSqr) continue;
                    duplicate = true;
                    break;
                }
                if (duplicate) continue;
                corners.Add(point);
                if (corners.Count > 8) return false;
            }
            if (corners.Count != 8) return false;

            Vector3 origin = corners[0];
            var vectors = new Vector3[7];
            for (int i = 1; i < corners.Count; i++)
                vectors[i - 1] = corners[i] - origin;

            bool Contains(Vector3 expected)
            {
                for (int i = 0; i < corners.Count; i++)
                    if ((corners[i] - expected).sqrMagnitude <= toleranceSqr)
                        return true;
                return false;
            }

            for (int a = 0; a < vectors.Length - 2; a++)
            {
                Vector3 edgeX = vectors[a];
                float lengthX = edgeX.magnitude;
                if (lengthX <= tolerance) continue;
                for (int b = a + 1; b < vectors.Length - 1; b++)
                {
                    Vector3 edgeY = vectors[b];
                    float lengthY = edgeY.magnitude;
                    if (lengthY <= tolerance ||
                        Mathf.Abs(Vector3.Dot(edgeX, edgeY) /
                                  (lengthX * lengthY)) > 1e-4f)
                        continue;
                    for (int c = b + 1; c < vectors.Length; c++)
                    {
                        Vector3 edgeZ = vectors[c];
                        float lengthZ = edgeZ.magnitude;
                        if (lengthZ <= tolerance ||
                            Mathf.Abs(Vector3.Dot(edgeX, edgeZ) /
                                      (lengthX * lengthZ)) > 1e-4f ||
                            Mathf.Abs(Vector3.Dot(edgeY, edgeZ) /
                                      (lengthY * lengthZ)) > 1e-4f)
                            continue;

                        if (!Contains(origin + edgeX) ||
                            !Contains(origin + edgeY) ||
                            !Contains(origin + edgeZ) ||
                            !Contains(origin + edgeX + edgeY) ||
                            !Contains(origin + edgeX + edgeZ) ||
                            !Contains(origin + edgeY + edgeZ) ||
                            !Contains(origin + edgeX + edgeY + edgeZ))
                            continue;

                        Vector3 axisX = edgeX / lengthX;
                        Vector3 axisY = edgeY / lengthY;
                        Vector3 axisZ = edgeZ / lengthZ;
                        if (Vector3.Dot(Vector3.Cross(axisX, axisY), axisZ) < 0f)
                            axisZ = -axisZ;
                        Quaternion rotation = Quaternion.LookRotation(axisZ, axisY);
                        Vector3 center = Vector3.zero;
                        for (int i = 0; i < corners.Count; i++) center += corners[i];
                        center /= corners.Count;
                        box = new OrientedBox
                        {
                            Center = center,
                            Size = new Vector3(lengthX, lengthY, lengthZ),
                            Rotation = rotation,
                            IsValid = true
                        };
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Collapses a tiled rectangular wall/floor into the smallest set of rectangular
        /// collision/support groups. Only a gap-free rectangular union is accepted, so an
        /// L-shaped room corner or a real opening can never gain a bridging collider.
        /// </summary>
        internal static int MergeCoplanarPanelGroups(List<WorkMesh> components,
            List<OrientedBox> boxes)
        {
            if (components == null || boxes == null ||
                components.Count != boxes.Count || boxes.Count < 2)
                return 0;

            float tolerance = GetPanelMergeTolerance(boxes);
            int mergedCount = 0;
            bool changed;
            do
            {
                changed = false;
                for (int a = 0; a < boxes.Count - 1 && !changed; a++)
                {
                    if (!boxes[a].IsValid) continue;
                    for (int b = a + 1; b < boxes.Count; b++)
                    {
                        if (!boxes[b].IsValid ||
                            !TryMergeCoplanarBoxes(boxes[a], boxes[b], tolerance,
                                out OrientedBox merged))
                            continue;

                        boxes[a] = merged;
                        boxes.RemoveAt(b);
                        // A merged rectangular box no longer needs triangle geometry.
                        // Retain the first component only as an aligned placeholder for
                        // the existing compound-collider build path.
                        components.RemoveAt(b);
                        mergedCount++;
                        changed = true;
                        break;
                    }
                }
            } while (changed);

            return mergedCount;
        }

        static float GetPanelMergeTolerance(IReadOnlyList<OrientedBox> boxes)
        {
            Bounds bounds = default;
            bool hasBounds = false;
            float smallestThickness = float.MaxValue;
            for (int i = 0; i < boxes.Count; i++)
            {
                OrientedBox box = boxes[i];
                if (!box.IsValid) continue;
                GetBoxAxes(box, out Vector3 axisX, out Vector3 axisY,
                    out Vector3 axisZ);
                Vector3 half = box.Size * 0.5f;
                Vector3 extent = Abs(axisX) * half.x + Abs(axisY) * half.y +
                                 Abs(axisZ) * half.z;
                Bounds next = new Bounds(box.Center, extent * 2f);
                if (!hasBounds)
                {
                    bounds = next;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(next.min);
                    bounds.Encapsulate(next.max);
                }
                smallestThickness = Mathf.Min(smallestThickness,
                    Mathf.Min(box.Size.x, Mathf.Min(box.Size.y, box.Size.z)));
            }

            if (!hasBounds) return 1e-5f;
            float diagonal = Mathf.Max(bounds.size.magnitude, 1e-3f);
            float conservative = diagonal * 0.001f;
            float thicknessLimit = smallestThickness < float.MaxValue
                ? smallestThickness * 0.25f
                : diagonal * 0.005f;
            return Mathf.Max(1e-5f,
                Mathf.Max(conservative, Mathf.Min(diagonal * 0.005f,
                    thicknessLimit)));
        }

        /// <summary>
        /// The union of two boxes is itself a box whenever they are flush on two axes and
        /// adjacent on the third. Which axis plays the part of "thickness" makes no
        /// difference to that fact, but the test used to be anchored on each box's THINNEST
        /// axis. That is well defined for a wall panel and completely arbitrary for a cube,
        /// so a wall built from cube modules never merged: the axis picked for two
        /// neighbours placed them in different planes and the coplanarity test refused, and
        /// 64 blocks stayed 64 collision parts and 64 probe grids. Try the thin axis first,
        /// so panel kits keep exactly the grouping they had, then the other two.
        /// </summary>
        static bool TryMergeCoplanarBoxes(OrientedBox a, OrientedBox b,
            float tolerance, out OrientedBox merged)
        {
            GetThinAxis(a, out int thinA, out _, out _, out _, out _, out _, out _);
            if (TryMergeBoxesAlongAxis(a, b, thinA, tolerance, out merged))
                return true;
            for (int axis = 0; axis < 3; axis++)
            {
                if (axis == thinA) continue;
                if (TryMergeBoxesAlongAxis(a, b, axis, tolerance, out merged))
                    return true;
            }
            merged = default;
            return false;
        }

        static bool TryMergeBoxesAlongAxis(OrientedBox a, OrientedBox b,
            int axis, float tolerance, out OrientedBox merged)
        {
            merged = default;
            GetBoxFrame(a, axis, out Vector3 normalA, out Vector3 tangentA,
                out Vector3 tangentB, out float thicknessA);
            GetBoxFrame(b, axis, out Vector3 normalB, out Vector3 bTangentA,
                out Vector3 bTangentB, out float thicknessB);
            int thinA = axis;

            if (Mathf.Abs(Vector3.Dot(normalA, normalB)) < 0.9999f ||
                Mathf.Abs(thicknessA - thicknessB) > tolerance)
                return false;

            // Reject in-plane rotation. The projected interval test below is only exact
            // when both tiles share the same authored wall axes.
            float axisAlignment = Mathf.Max(
                Mathf.Min(Mathf.Abs(Vector3.Dot(tangentA, bTangentA)),
                    Mathf.Abs(Vector3.Dot(tangentB, bTangentB))),
                Mathf.Min(Mathf.Abs(Vector3.Dot(tangentA, bTangentB)),
                    Mathf.Abs(Vector3.Dot(tangentB, bTangentA))));
            if (axisAlignment < 0.9999f) return false;

            float planeA = Vector3.Dot(a.Center, normalA);
            float planeB = Vector3.Dot(b.Center, normalA);
            if (Mathf.Abs(planeA - planeB) > tolerance) return false;

            ProjectBoxInterval(a, tangentA, out float aMinU, out float aMaxU);
            ProjectBoxInterval(a, tangentB, out float aMinV, out float aMaxV);
            ProjectBoxInterval(b, tangentA, out float bMinU, out float bMaxU);
            ProjectBoxInterval(b, tangentB, out float bMinV, out float bMaxV);

            bool sameU = Mathf.Abs(aMinU - bMinU) <= tolerance &&
                         Mathf.Abs(aMaxU - bMaxU) <= tolerance;
            bool sameV = Mathf.Abs(aMinV - bMinV) <= tolerance &&
                         Mathf.Abs(aMaxV - bMaxV) <= tolerance;
            bool touchesU = bMinU <= aMaxU + tolerance &&
                            aMinU <= bMaxU + tolerance;
            bool touchesV = bMinV <= aMaxV + tolerance &&
                            aMinV <= bMaxV + tolerance;
            if (!(sameV && touchesU) && !(sameU && touchesV)) return false;

            float minU = Mathf.Min(aMinU, bMinU);
            float maxU = Mathf.Max(aMaxU, bMaxU);
            float minV = Mathf.Min(aMinV, bMinV);
            float maxV = Mathf.Max(aMaxV, bMaxV);
            Vector3 center = tangentA * ((minU + maxU) * 0.5f) +
                             tangentB * ((minV + maxV) * 0.5f) +
                             normalA * ((planeA + planeB) * 0.5f);
            Vector3 size = a.Size;
            int[] surfaceAxes = SurfaceAxisIndices(thinA);
            size[thinA] = Mathf.Max(thicknessA, thicknessB);
            size[surfaceAxes[0]] = maxU - minU;
            size[surfaceAxes[1]] = maxV - minV;
            merged = new OrientedBox
            {
                Center = center,
                Size = size,
                Rotation = a.Rotation,
                IsValid = true
            };
            return true;
        }

        static void ProjectBoxInterval(OrientedBox box, Vector3 axis,
            out float minimum, out float maximum)
        {
            GetBoxAxes(box, out Vector3 x, out Vector3 y, out Vector3 z);
            float radius = Mathf.Abs(Vector3.Dot(x, axis)) * box.Size.x * 0.5f +
                           Mathf.Abs(Vector3.Dot(y, axis)) * box.Size.y * 0.5f +
                           Mathf.Abs(Vector3.Dot(z, axis)) * box.Size.z * 0.5f;
            float center = Vector3.Dot(box.Center, axis);
            minimum = center - radius;
            maximum = center + radius;
        }

        static void GetThinAxis(OrientedBox box, out int thinAxis,
            out Vector3 normal, out Vector3 tangentA, out Vector3 tangentB,
            out float thickness, out float extentA, out float extentB)
        {
            thinAxis = box.Size.x <= box.Size.y && box.Size.x <= box.Size.z ? 0 :
                box.Size.y <= box.Size.z ? 1 : 2;
            int[] surface = SurfaceAxisIndices(thinAxis);
            GetBoxAxes(box, out Vector3 x, out Vector3 y, out Vector3 z);
            Vector3[] axes = { x, y, z };
            normal = axes[thinAxis];
            tangentA = axes[surface[0]];
            tangentB = axes[surface[1]];
            thickness = box.Size[thinAxis];
            extentA = box.Size[surface[0]] * 0.5f;
            extentB = box.Size[surface[1]] * 0.5f;
        }

        /// <summary>Box axes and thickness for an explicitly chosen axis.</summary>
        static void GetBoxFrame(OrientedBox box, int axis, out Vector3 normal,
            out Vector3 tangentA, out Vector3 tangentB, out float thickness)
        {
            int[] surface = SurfaceAxisIndices(axis);
            GetBoxAxes(box, out Vector3 x, out Vector3 y, out Vector3 z);
            Vector3[] axes = { x, y, z };
            normal = axes[axis];
            tangentA = axes[surface[0]];
            tangentB = axes[surface[1]];
            thickness = box.Size[axis];
        }

        static int[] SurfaceAxisIndices(int thinAxis)
        {
            return thinAxis == 0 ? new[] { 1, 2 } :
                thinAxis == 1 ? new[] { 0, 2 } : new[] { 0, 1 };
        }

        static void GetBoxAxes(OrientedBox box, out Vector3 x,
            out Vector3 y, out Vector3 z)
        {
            x = box.Rotation * Vector3.right;
            y = box.Rotation * Vector3.up;
            z = box.Rotation * Vector3.forward;
        }

        static Vector3 Abs(Vector3 value) => new Vector3(
            Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        static Collider AddConvexCollider(GameObject go, Mesh mesh)
        {
            if (RequiresBoxConvexFallback(mesh))
                return AddBoxCollider(go, mesh != null ? mesh.bounds : new Bounds());

            // Pre-test PhysX convex cooking. If PhysX QuickHull cannot process the mesh topology,
            // fall back to a clean BoxCollider to prevent runtime PhysX console errors.
            try
            {
                if (mesh != null)
                {
                    // BakeMesh needs the mesh's real id; Unity 6000.3 moved it to EntityId.
#if UNITY_6000_3_OR_NEWER
                    Physics.BakeMesh(mesh.GetEntityId(), convex: true);
#else
                    Physics.BakeMesh(mesh.GetInstanceID(), convex: true);
#endif
                }
            }
            catch
            {
                return AddBoxCollider(go, mesh != null ? mesh.bounds : new Bounds());
            }

            var mc = go.AddComponent<MeshCollider>();
            mc.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning |
                                MeshColliderCookingOptions.WeldColocatedVertices;
            mc.convex = true;
            mc.sharedMesh = mesh;
            return mc;
        }

        static Collider AddStableConvexCollider(GameObject go, Mesh mesh,
            bool forcePrimitive)
        {
            return forcePrimitive
                ? AddBoxCollider(go, mesh != null ? mesh.bounds : new Bounds())
                : AddConvexCollider(go, mesh);
        }

        /// <summary>
        /// True only for meshes PhysX cannot turn into a usable convex hull: degenerate,
        /// hair-thin or genuinely flat shells. The volume test walks the extremal
        /// tetrahedron of the whole vertex set. Sampling only the first vertices instead
        /// would classify almost every fragment as flat, because mesh vertices are stored
        /// face by face and the opening face is planar by definition. That mistake turns
        /// the entire bake into oversized Box Colliders, which interpenetrate their
        /// neighbours and make the debris jitter and shove each other apart.
        /// </summary>
        static bool RequiresBoxConvexFallback(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount < 4) return true;
            Bounds bounds = mesh.bounds;
            Vector3 size = bounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float shortest = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            if (longest <= 1e-4f) return true;
            // PhysX cooks hulls thinner than roughly the default contact offset without
            // reliable volume, so only true slivers keep the Box fallback.
            if (shortest <= 0.004f || shortest <= longest * 0.004f) return true;

            Vector3[] verts = mesh.vertices;
            if (verts.Length < 4) return true;

            // Flatness is measured against the object size: a 3 m slab and a 5 cm shard
            // need the same relative thickness before a convex hull becomes unstable.
            float planeTolerance = Mathf.Max(0.001f, longest * 0.004f);

            Vector3 a = verts[0];
            Vector3 b = a;
            float bestSqr = 0f;
            for (int i = 1; i < verts.Length; i++)
            {
                float d = (verts[i] - a).sqrMagnitude;
                if (d > bestSqr) { bestSqr = d; b = verts[i]; }
            }
            if (bestSqr <= planeTolerance * planeTolerance) return true;

            Vector3 ab = b - a;
            Vector3 c = a;
            float bestArea = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                float area = Vector3.Cross(ab, verts[i] - a).sqrMagnitude;
                if (area > bestArea) { bestArea = area; c = verts[i]; }
            }
            Vector3 normal = Vector3.Cross(ab, c - a);
            if (normal.sqrMagnitude < 1e-12f) return true;
            normal = normal.normalized;

            float thickness = 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                float d = Mathf.Abs(Vector3.Dot(verts[i] - a, normal));
                if (d > thickness) thickness = d;
            }

            return thickness < planeTolerance;
        }

        static bool HasNonManifoldConvexInput(Mesh mesh, float scale)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            float cell = Mathf.Max(scale * 1e-6f, 1e-7f);
            var weldLookup = new Dictionary<Vector3Int, int>(vertices.Length);
            var welded = new int[vertices.Length];
            int nextWeld = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = vertices[i];
                var key = new Vector3Int(
                    Mathf.RoundToInt(point.x / cell),
                    Mathf.RoundToInt(point.y / cell),
                    Mathf.RoundToInt(point.z / cell));
                if (!weldLookup.TryGetValue(key, out int id))
                {
                    id = nextWeld++;
                    weldLookup.Add(key, id);
                }
                welded[i] = id;
            }

            var edgeUse = new Dictionary<long, int>(triangles.Length);
            bool AddEdge(int a, int b)
            {
                if (a == b) return false;
                int lo = Mathf.Min(a, b), hi = Mathf.Max(a, b);
                long key = ((long)lo << 32) | (uint)hi;
                edgeUse.TryGetValue(key, out int count);
                count++;
                edgeUse[key] = count;
                return count <= 2;
            }

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (a < 0 || b < 0 || c < 0 || a >= vertices.Length ||
                    b >= vertices.Length || c >= vertices.Length)
                    return true;
                Vector3 cross = Vector3.Cross(vertices[b] - vertices[a],
                    vertices[c] - vertices[a]);
                if (cross.sqrMagnitude <= cell * cell * cell * cell)
                    return true;
                if (!AddEdge(welded[a], welded[b]) ||
                    !AddEdge(welded[b], welded[c]) ||
                    !AddEdge(welded[c], welded[a]))
                    return true;
            }
            return false;
        }

        static void ConfigureFragmentSolver(Rigidbody body, bool compound,
            int siblingCount)
        {
            if (body == null) return;
            bool largeSet = siblingCount >= 128;
            body.solverIterations = largeSet ? (compound ? 8 : 6)
                : compound ? 14 : 10;
            body.solverVelocityIterations = largeSet ? (compound ? 3 : 2)
                : compound ? 6 : 4;
            body.maxDepenetrationVelocity = largeSet ? 10f
                : compound ? 18f : 12f;
        }

        static List<Collider> AddConvexColliders(GameObject go,
            List<Mesh> meshes, bool enabled)
        {
            var result = new List<Collider>();
            if (meshes == null) return result;
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] == null) continue;
                Collider collider = AddConvexCollider(go, meshes[i]);
                collider.enabled = enabled;
                result.Add(collider);
            }
            return result;
        }

        /// <summary>
        /// Builds a dynamic compound approximation from the final fragment WorkMesh.
        /// Spatially clipped parts keep cavities and strong concavities open instead of
        /// wrapping the entire solidified shell in one convex hull.
        /// </summary>
        static List<Mesh> BuildDynamicCompoundMeshes(WorkMesh source,
            string namePrefix, int targetPartCount,
            Action<float> progress = null)
        {
            var parts = new List<WorkMesh>();
            if (source == null || source.TriangleCount == 0) return new List<Mesh>();
            parts.Add(source.Clone());

            targetPartCount = Mathf.Clamp(targetPartCount, 1, 12);
            var unsplittable = new HashSet<WorkMesh>();
            while (parts.Count < targetPartCount)
            {
                progress?.Invoke((float)parts.Count / targetPartCount);
                int candidateIndex = -1;
                float candidateScore = -1f;
                for (int i = 0; i < parts.Count; i++)
                {
                    if (unsplittable.Contains(parts[i])) continue;
                    Bounds bounds = parts[i].ComputeBounds();
                    Vector3 size = bounds.size;
                    float score = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) *
                                  Mathf.Max(1, parts[i].TriangleCount);
                    if (score <= candidateScore) continue;
                    candidateScore = score;
                    candidateIndex = i;
                }
                if (candidateIndex < 0) break;

                WorkMesh candidate = parts[candidateIndex];
                Bounds candidateBounds = candidate.ComputeBounds();
                Vector3 sizeOrder = candidateBounds.size;
                int firstAxis = sizeOrder.x >= sizeOrder.y
                    ? (sizeOrder.x >= sizeOrder.z ? 0 : 2)
                    : (sizeOrder.y >= sizeOrder.z ? 1 : 2);
                bool split = false;
                for (int attempt = 0; attempt < 3 && !split; attempt++)
                {
                    int axis = (firstAxis + attempt) % 3;
                    Vector3 normal = axis == 0 ? Vector3.right
                        : axis == 1 ? Vector3.up : Vector3.forward;
                    float extent = axis == 0 ? candidateBounds.size.x
                        : axis == 1 ? candidateBounds.size.y : candidateBounds.size.z;
                    if (extent < 1e-5f) continue;

                    MeshSlicer.Slice(candidate,
                        new Plane(normal, candidateBounds.center), 1f,
                        CutSurfaceUVMode.PlanarProjection,
                        out WorkMesh positive, out WorkMesh negative,
                        // These meshes are immediately cooked as convex hulls. The
                        // direct cap is sufficient; running the expensive generic hole
                        // repair for every compound part cannot improve the convex hull.
                        repairCutTopology: false);
                    if (!IsUsableCompoundPart(positive) ||
                        !IsUsableCompoundPart(negative))
                        continue;

                    parts[candidateIndex] = positive;
                    parts.Add(negative);
                    split = true;
                }

                if (!split) unsplittable.Add(candidate);
            }

            var meshes = new List<Mesh>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                progress?.Invoke((i + 0.5f) / Mathf.Max(1, parts.Count));
                if (!IsUsableCompoundPart(parts[i])) continue;
                meshes.Add(parts[i].ToMesh($"{namePrefix}_{i:00}"));
            }
            // A valid fragment should always yield at least one collider. Keeping this
            // fallback local avoids silently producing a Rigidbody with no collision.
            if (meshes.Count == 0)
                meshes.Add(source.ToMesh($"{namePrefix}_00"));
            progress?.Invoke(1f);
            return meshes;
        }

        static int DynamicCompoundPartBudget(int primaryFragmentCount,
            bool secondary)
        {
            // Exact Mesh remains the untouched non-convex collider while a piece is
            // kinematic. Once it becomes a Rigidbody, Unity requires convex collision.
            // Thousands of hidden fallback hulls make both collider cooking and prefab
            // serialization stall, so large bakes use a deliberately smaller compound.
            if (primaryFragmentCount >= 160) return 2;
            if (primaryFragmentCount >= 96) return secondary ? 2 : 3;
            if (primaryFragmentCount >= 48) return secondary ? 3 : 4;
            return secondary ? 6 : 8;
        }

        static bool IsUsableCompoundPart(WorkMesh part)
        {
            if (part == null || part.TriangleCount < 4) return false;
            Vector3 size = part.ComputeBounds().size;
            const float epsilon = 1e-6f;
            return size.x > epsilon && size.y > epsilon && size.z > epsilon;
        }

        static Collider AddExactCollider(GameObject go, Mesh mesh)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = false;
            mc.sharedMesh = mesh;
            return mc;
        }

        internal static int FindStructuralGroup(Vector3 point,
            IReadOnlyList<WorkMesh> components,
            IReadOnlyList<OrientedBox> panelBoxes = null)
        {
            if (components == null || components.Count <= 1) return 0;
            int best = 0;
            float bestDistance = float.MaxValue;
            float bestCenterDistance = float.MaxValue;
            for (int i = 0; i < components.Count; i++)
            {
                float distance;
                float centerDistance;
                if (panelBoxes != null && i < panelBoxes.Count &&
                    panelBoxes[i].IsValid)
                {
                    OrientedBox box = panelBoxes[i];
                    Vector3 local = Quaternion.Inverse(box.Rotation) *
                                    (point - box.Center);
                    Vector3 outside = new Vector3(
                        Mathf.Max(0f, Mathf.Abs(local.x) - box.Size.x * 0.5f),
                        Mathf.Max(0f, Mathf.Abs(local.y) - box.Size.y * 0.5f),
                        Mathf.Max(0f, Mathf.Abs(local.z) - box.Size.z * 0.5f));
                    distance = outside.sqrMagnitude;
                    centerDistance = local.sqrMagnitude;
                }
                else
                {
                    Bounds bounds = components[i].ComputeBounds();
                    distance = (bounds.ClosestPoint(point) - point).sqrMagnitude;
                    centerDistance = (bounds.center - point).sqrMagnitude;
                }

                // At a real wall/floor junction the fragment centroid can be inside both
                // boxes. Resolve that tie by the nearest module centre instead of always
                // choosing index zero, which used to serialize an entire room as group 0.
                const float tieEpsilon = 1e-8f;
                if (distance < bestDistance - tieEpsilon ||
                    (Mathf.Abs(distance - bestDistance) <= tieEpsilon &&
                     centerDistance < bestCenterDistance))
                {
                    bestDistance = distance;
                    bestCenterDistance = centerDistance;
                    best = i;
                }
            }
            return best;
        }

        static Collider AddBoxCollider(GameObject go, Bounds bounds)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = bounds.center;
            bc.size = bounds.size;
            return bc;
        }

        static Collider AddOrientedBoxCollider(GameObject parent, OrientedBox box,
            int index)
        {
            var host = new GameObject($"Intact Collider {index:000}");
            host.transform.SetParent(parent.transform, false);
            host.transform.localPosition = box.Center;
            host.transform.localRotation = box.Rotation;
            host.transform.localScale = Vector3.one;
            host.layer = parent.layer;
            var collider = host.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = box.Size;
            return collider;
        }

        static Material CreateDefaultInteriorMaterial()
        {
            Shader shader = PresetMaterialLibrary.FindPipelineLitShader();
            var mat = new Material(shader) { name = "Interior" };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.42f, 0.4f, 0.38f));
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", new Color(0.42f, 0.4f, 0.38f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            return mat;
        }

        static Dictionary<Material, Color> BuildMaterialEffectColorCache(
            Material[] materials, Action<int, int> progress = null)
        {
            var result = new Dictionary<Material, Color>();
            if (materials == null) return result;
            for (int i = 0; i < materials.Length; i++)
            {
                progress?.Invoke(i, materials.Length);
                Material material = materials[i];
                if (material != null && !result.ContainsKey(material))
                    result.Add(material, ResolveMaterialEffectColor(material));
            }
            return result;
        }

        /// <summary>Samples the actual albedo used by a source material without changing
        /// texture importer Read/Write state. One tiny GPU readback per distinct material is
        /// cached for the bake, then serialized onto every fragment using that material.</summary>
        static Color ResolveMaterialEffectColor(Material material)
        {
            if (material == null) return Color.white;
            Color tint = Color.white;
            if (material.HasProperty("_BaseColor"))
                tint = material.GetColor("_BaseColor");
            else if (material.HasProperty("_Color"))
                tint = material.GetColor("_Color");

            Texture texture = null;
            string[] textureProperties = { "_BaseMap", "_BaseColorMap", "_MainTex" };
            for (int i = 0; i < textureProperties.Length && texture == null; i++)
                if (material.HasProperty(textureProperties[i]))
                    texture = material.GetTexture(textureProperties[i]);
            if (texture == null)
            {
                tint.a = 1f;
                return tint;
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture target = RenderTexture.GetTemporary(8, 8, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D sample = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                sample = new Texture2D(8, 8, TextureFormat.RGBA32, false, false);
                sample.ReadPixels(new Rect(0, 0, 8, 8), 0, 0, false);
                sample.Apply(false, false);
                Color[] pixels = sample.GetPixels();
                Color average = Color.black;
                float weight = 0f;
                for (int i = 0; i < pixels.Length; i++)
                {
                    float alpha = Mathf.Max(0.01f, pixels[i].a);
                    average += pixels[i] * alpha;
                    weight += alpha;
                }
                if (weight > 0f) average /= weight;
                Color resolved = new Color(average.r * tint.r, average.g * tint.g,
                    average.b * tint.b, 1f);
                float value = Mathf.Max(resolved.r, Mathf.Max(resolved.g, resolved.b));
                if (value < 0.18f)
                    resolved *= 0.18f / Mathf.Max(0.001f, value);
                resolved.a = 1f;
                return resolved;
            }
            catch
            {
                tint.a = 1f;
                return tint;
            }
            finally
            {
                RenderTexture.active = previous;
                if (sample != null) UnityEngine.Object.DestroyImmediate(sample);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            string leaf = path.Substring(slash + 1);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Deletes a folder through AssetDatabase (taking its .meta along)
        /// when it no longer contains any assets.</summary>
        static void DeleteFolderIfEmpty(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder)) return;
            if (AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0)
                AssetDatabase.DeleteAsset(folder);
        }

        internal static string MakeSafeFileName(string name)
        {
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }

        internal static string GetDestructibleBaseName(string name)
        {
            const string suffix = "_Destructible";
            string result = string.IsNullOrWhiteSpace(name)
                ? "Destructible"
                : name.Trim();

            // Copies of one prefab differ only by Unity's " (1)" duplicate marker. Keeping
            // it gave every copy its own output folder, prefab and fragment meshes even
            // though the source mesh and profile were identical — five copies of a building
            // meant five bakes and five sets of assets. Both suffixes are stripped in any
            // order, because a duplicated baked object is named "Wall_Destructible (1)".
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                if (result.Length > suffix.Length &&
                    result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(0, result.Length - suffix.Length);
                    stripped = true;
                }
                string trimmed = TrimDuplicateMarker(result);
                if (trimmed.Length != result.Length)
                {
                    result = trimmed;
                    stripped = true;
                }
            }
            return string.IsNullOrWhiteSpace(result) ? "Destructible" : result;
        }

        /// <summary>
        /// Removes one trailing Unity duplicate marker such as " (1)". An empty "()" or a
        /// non-numeric "(copy)" is left alone: those are author-chosen names, not the
        /// suffix the Editor appends when an object is duplicated.
        /// </summary>
        static string TrimDuplicateMarker(string name)
        {
            int close = name.Length - 1;
            if (close < 3 || name[close] != ')') return name;
            int open = name.LastIndexOf('(', close);
            if (open <= 0 || close - open <= 1 || name[open - 1] != ' ') return name;
            for (int i = open + 1; i < close; i++)
                if (!char.IsDigit(name[i])) return name;
            return name.Substring(0, open - 1).TrimEnd();
        }

        static string FindGeneratedManifestPath(string prefabPath,
            DestructionProfile profile)
        {
            if (string.IsNullOrEmpty(prefabPath)) return null;
            string folder = Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) ||
                !AssetDatabase.IsValidFolder(folder))
                return null;

            foreach (string guid in AssetDatabase.FindAssets(
                         "t:GeneratedManifest", new[] { folder }))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(
                        Path.GetDirectoryName(candidate)?.Replace('\\', '/'),
                        folder, StringComparison.OrdinalIgnoreCase))
                    continue;
                GeneratedManifest manifest =
                    AssetDatabase.LoadAssetAtPath<GeneratedManifest>(candidate);
                if (manifest != null &&
                    (profile == null ||
                     string.Equals(manifest.ProfileName, profile.name,
                         StringComparison.Ordinal)))
                    return candidate;
            }
            return null;
        }
    }
}
