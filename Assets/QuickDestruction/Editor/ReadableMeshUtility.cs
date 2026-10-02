using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Creates an Editor-only readable snapshot for imported meshes that have Read/Write
    /// disabled. The source asset and its importer settings are never modified.
    /// </summary>
    internal static class ReadableMeshUtility
    {
        /// <summary>Use the same conservative panel preparation for single meshes,
        /// nested-mesh batches and preview as the modular building pipeline. Only a
        /// completely filled rectangle with compatible attributes can lose grid edges.
        /// The returned temporary mesh never replaces the authored mesh.</summary>
        public static Mesh AcquireForFracture(Mesh source, bool solidify,
            out bool ownsSnapshot, out string error)
        {
            Mesh readable = Acquire(source, out ownsSnapshot, out error);
            if (readable == null || !solidify || readable.subMeshCount != 1)
                return readable;
            try
            {
                if (!CombinedMeshSource.TryCreateSimplifiedPlanarRectangle(
                        readable, 0, out Mesh rectangle))
                    return readable;
                Release(readable, ownsSnapshot);
                ownsSnapshot = true;
                return rectangle;
            }
            catch
            {
                Release(readable, ownsSnapshot);
                throw;
            }
        }

        public static Mesh Acquire(Mesh source, out bool ownsSnapshot, out string error)
        {
            ownsSnapshot = false;
            error = null;

            if (source == null)
            {
                error = "No source mesh was provided.";
                return null;
            }

            if (source.isReadable) return source;

            Mesh snapshot = null;
            try
            {
                using (Mesh.MeshDataArray meshDataArray =
                       MeshUtility.AcquireReadOnlyMeshData(source))
                {
                    Mesh.MeshData meshData = meshDataArray[0];
                    snapshot = new Mesh
                    {
                        name = source.name + " [QD Readable Snapshot]",
                        hideFlags = HideFlags.HideAndDontSave,
                        indexFormat = source.indexFormat
                    };

                    using (var positions = new NativeArray<Vector3>(
                               meshData.vertexCount, Allocator.Temp,
                               NativeArrayOptions.UninitializedMemory))
                    {
                        meshData.GetVertices(positions);
                        snapshot.SetVertices(positions);
                    }

                    if (meshData.HasVertexAttribute(VertexAttribute.Normal))
                    {
                        using (var normals = new NativeArray<Vector3>(
                                   meshData.vertexCount, Allocator.Temp,
                                   NativeArrayOptions.UninitializedMemory))
                        {
                            meshData.GetNormals(normals);
                            snapshot.SetNormals(normals);
                        }
                    }

                    if (meshData.HasVertexAttribute(VertexAttribute.TexCoord0))
                    {
                        using (var uvs = new NativeArray<Vector2>(
                                   meshData.vertexCount, Allocator.Temp,
                                   NativeArrayOptions.UninitializedMemory))
                        {
                            meshData.GetUVs(0, uvs);
                            snapshot.SetUVs(0, uvs);
                        }
                    }

                    snapshot.subMeshCount = meshData.subMeshCount;
                    for (int submesh = 0; submesh < meshData.subMeshCount; submesh++)
                    {
                        SubMeshDescriptor descriptor = meshData.GetSubMesh(submesh);
                        using (var indices = new NativeArray<int>(
                                   descriptor.indexCount, Allocator.Temp,
                                   NativeArrayOptions.UninitializedMemory))
                        {
                            meshData.GetIndices(indices, submesh, true);
                            snapshot.SetIndices(
                                indices, descriptor.topology, submesh, false);
                        }
                    }

                    snapshot.bounds = source.bounds;
                }

                ownsSnapshot = true;
                return snapshot;
            }
            catch (Exception exception)
            {
                if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
                error = $"Unity could not create a readable mesh snapshot: {exception.Message}";
                return null;
            }
        }

        public static void Release(Mesh mesh, bool ownsSnapshot)
        {
            if (ownsSnapshot && mesh != null)
                UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
