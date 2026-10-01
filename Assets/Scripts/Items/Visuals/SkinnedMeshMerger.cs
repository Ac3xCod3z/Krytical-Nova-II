using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Items.Visuals
{
    /// <summary>Sidekick body-shape blendshape weights (0-100), baked into vertices at merge time.</summary>
    [Serializable]
    public struct BodyShapeWeights
    {
        [Range(0, 100)] public float masculineFeminine;
        [Range(0, 100)] public float skinny;
        [Range(0, 100)] public float heavy;
        [Range(0, 100)] public float buff;

        public float WeightFor(string blendShapeName)
        {
            // Same Contains() matching as SidekickRuntime.UpdateBlendShapes.
            if (blendShapeName.Contains("masculineFeminine")) return masculineFeminine;
            if (blendShapeName.Contains("defaultSkinny")) return skinny;
            if (blendShapeName.Contains("defaultHeavy")) return heavy;
            if (blendShapeName.Contains("defaultBuff")) return buff;
            return 0f; // facial expressions etc. are dropped
        }
    }

    /// <summary>
    /// Merges Sidekick part meshes into ONE skinned mesh bound to an existing skeleton, in place (unlike Sidekick's
    /// Combiner, which builds a new GameObject + skeleton). Bones are matched by name; bindposes come from the reference
    /// (original baked) mesh or the source part, never the live pose, so merging while animating is safe. Body-shape
    /// blendshapes are baked and dropped, so the result skins as cheaply as a plain mesh. Runs only on equip changes.
    /// </summary>
    public sealed class SkinnedMeshMerger
    {
        private const int MaxUvChannels = 4;

        private readonly Dictionary<string, Transform> skeleton = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly Transform[] referenceBones;
        private readonly Matrix4x4[] referenceBindposes;

        // Scratch buffers reused across merges (equip-time work, but no reason to churn the GC).
        private readonly List<Vector3> positions = new List<Vector3>(32768);
        private readonly List<Vector3> normals = new List<Vector3>(32768);
        private readonly List<Vector4> tangents = new List<Vector4>(32768);
        private readonly List<Vector4>[] uvs = new List<Vector4>[MaxUvChannels];
        private readonly int[] uvDimensions = new int[MaxUvChannels];
        private readonly List<BoneWeight> boneWeights = new List<BoneWeight>(32768);
        private readonly List<int> triangles = new List<int>(98304);
        private readonly Dictionary<string, int> boneIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly List<Matrix4x4> bindposes = new List<Matrix4x4>(160);

        private readonly List<Vector3> partVerts = new List<Vector3>(8192);
        private readonly List<Vector3> partNormals = new List<Vector3>(8192);
        private readonly List<Vector4> partTangents = new List<Vector4>(8192);
        private readonly List<Vector4> partUv = new List<Vector4>(8192);
        private readonly List<BoneWeight> partWeights = new List<BoneWeight>(8192);
        private readonly List<int> partTris = new List<int>(24576);
        private readonly List<Matrix4x4> partBindposes = new List<Matrix4x4>(128);
        private readonly List<Vector2> uv2Buffer = new List<Vector2>(32768);
        private Vector3[] deltaV = Array.Empty<Vector3>();
        private Vector3[] deltaN = Array.Empty<Vector3>();
        private Vector3[] deltaT = Array.Empty<Vector3>();
        private Vector3[] deltaV2 = Array.Empty<Vector3>();
        private Vector3[] deltaN2 = Array.Empty<Vector3>();
        private Vector3[] deltaT2 = Array.Empty<Vector3>();
        private int[] remap = Array.Empty<int>();

        public SkinnedMeshMerger(Transform skeletonRoot, Transform[] referenceBones, Matrix4x4[] referenceBindposes)
        {
            this.referenceBones = referenceBones ?? Array.Empty<Transform>();
            this.referenceBindposes = referenceBindposes ?? Array.Empty<Matrix4x4>();
            foreach (var t in skeletonRoot.GetComponentsInChildren<Transform>(true))
                skeleton.TryAdd(t.name, t);
            for (int c = 0; c < MaxUvChannels; c++) uvs[c] = new List<Vector4>(32768);
        }

        public bool HasBone(string name) => skeleton.ContainsKey(name);
        public Transform FindBone(string name) => skeleton.TryGetValue(name, out var t) ? t : null;

        /// <summary>Grows any dangle bones these parts need (used when mirroring a mesh merged on another rig).</summary>
        public void EnsureBones(IReadOnlyList<SidekickPartRef> parts)
        {
            foreach (var part in parts)
            {
                if (!part.IsValid) continue;
                foreach (var boneName in part.boneNames)
                    if (!skeleton.ContainsKey(boneName)) TryCreateBone(boneName, part.extraBones, out _);
            }
        }

        /// <summary>Builds a new mesh from <paramref name="parts"/>. Bones come back in <paramref name="outBones"/> (index-aligned with bindposes).</summary>
        public Mesh Merge(IReadOnlyList<SidekickPartRef> parts, BodyShapeWeights shape, List<Transform> outBones, string meshName)
        {
            positions.Clear(); normals.Clear(); tangents.Clear(); boneWeights.Clear(); triangles.Clear();
            bindposes.Clear(); boneIndexByName.Clear(); outBones.Clear();
            for (int c = 0; c < MaxUvChannels; c++) { uvs[c].Clear(); uvDimensions[c] = 0; }

            // Reference bones first so the bone order is stable and matches the original prefab.
            for (int i = 0; i < referenceBones.Length; i++)
            {
                var bone = referenceBones[i];
                if (bone == null || boneIndexByName.ContainsKey(bone.name)) continue; // null = pruned: index shifts are fine, bindposes travel with bones
                boneIndexByName.Add(bone.name, outBones.Count);
                outBones.Add(bone);
                bindposes.Add(i < referenceBindposes.Length ? referenceBindposes[i] : bone.worldToLocalMatrix);
            }

            foreach (var part in parts)
                if (part.IsValid) AppendPart(part, shape, outBones);

            if (positions.Count == 0) return null;

            var mesh = new Mesh { name = meshName };
            mesh.indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            for (int c = 0; c < MaxUvChannels; c++)
            {
                if (uvDimensions[c] == 0) continue;
                while (uvs[c].Count < positions.Count) uvs[c].Add(Vector4.zero);
                if (uvDimensions[c] <= 2)
                {
                    uv2Buffer.Clear();
                    foreach (var v in uvs[c]) uv2Buffer.Add(v);
                    mesh.SetUVs(c, uv2Buffer);
                }
                else mesh.SetUVs(c, uvs[c]);
            }
            mesh.boneWeights = boneWeights.ToArray();
            mesh.bindposes = bindposes.ToArray();
            mesh.SetTriangles(triangles, 0, true);
            // GPU skinning needs no CPU copy; frees ~100 bytes/vertex per character.
            mesh.UploadMeshData(true);
            return mesh;
        }

        private void AppendPart(SidekickPartRef part, BodyShapeWeights shape, List<Transform> outBones)
        {
            var m = part.mesh;
            if (!m.isReadable)
            {
                Debug.LogWarning($"[SkinnedMeshMerger] '{m.name}' is not Read/Write enabled; skipped.");
                return;
            }
            int vc = m.vertexCount;
            int baseIndex = positions.Count;

            m.GetVertices(partVerts);
            m.GetNormals(partNormals);
            m.GetTangents(partTangents);
            if (partNormals.Count != vc) { partNormals.Clear(); for (int i = 0; i < vc; i++) partNormals.Add(Vector3.up); }
            if (partTangents.Count != vc) { partTangents.Clear(); for (int i = 0; i < vc; i++) partTangents.Add(new Vector4(1, 0, 0, 1)); }
            BakeBlendShapes(m, shape, vc);

            positions.AddRange(partVerts);
            normals.AddRange(partNormals);
            tangents.AddRange(partTangents);

            for (int c = 0; c < MaxUvChannels; c++)
            {
                var attr = VertexAttribute.TexCoord0 + c;
                if (m.HasVertexAttribute(attr))
                {
                    m.GetUVs(c, partUv);
                    uvDimensions[c] = Math.Max(uvDimensions[c], m.GetVertexAttributeDimension(attr));
                    // Back-fill earlier parts that lacked this channel so lists stay vertex-aligned.
                    while (uvs[c].Count < baseIndex) uvs[c].Add(Vector4.zero);
                    uvs[c].AddRange(partUv);
                }
            }

            // Remap this part's bone indices onto the merged skeleton.
            m.GetBindposes(partBindposes);
            var names = part.boneNames;
            if (remap.Length < names.Length) remap = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string boneName = names[i];
                if (!boneIndexByName.TryGetValue(boneName, out int index))
                {
                    if (skeleton.TryGetValue(boneName, out var bone) || TryCreateBone(boneName, part.extraBones, out bone))
                    {
                        index = outBones.Count;
                        boneIndexByName.Add(boneName, index);
                        outBones.Add(bone);
                        bindposes.Add(i < partBindposes.Count ? partBindposes[i] : bone.worldToLocalMatrix);
                    }
                    else
                    {
                        Debug.LogWarning($"[SkinnedMeshMerger] Bone '{boneName}' (part '{m.name}') not in skeleton; weighted to root.");
                        index = 0;
                    }
                }
                remap[i] = index;
            }

            m.GetBoneWeights(partWeights);
            int maxIndex = names.Length - 1;
            foreach (var w in partWeights)
            {
                var r = w;
                r.boneIndex0 = remap[Mathf.Clamp(w.boneIndex0, 0, maxIndex)];
                r.boneIndex1 = remap[Mathf.Clamp(w.boneIndex1, 0, maxIndex)];
                r.boneIndex2 = remap[Mathf.Clamp(w.boneIndex2, 0, maxIndex)];
                r.boneIndex3 = remap[Mathf.Clamp(w.boneIndex3, 0, maxIndex)];
                boneWeights.Add(r);
            }
            for (int i = partWeights.Count; i < vc; i++) boneWeights.Add(new BoneWeight { weight0 = 1f });

            // Sidekick parts ship as quads split over several submeshes; flatten everything into one triangle list.
            for (int s = 0; s < m.subMeshCount; s++)
            {
                m.GetIndices(partTris, s);
                switch (m.GetTopology(s))
                {
                    case MeshTopology.Triangles:
                        for (int i = 0; i < partTris.Count; i++) triangles.Add(partTris[i] + baseIndex);
                        break;
                    case MeshTopology.Quads:
                        for (int i = 0; i + 3 < partTris.Count; i += 4)
                        {
                            int a = partTris[i] + baseIndex, b = partTris[i + 1] + baseIndex;
                            int c = partTris[i + 2] + baseIndex, d = partTris[i + 3] + baseIndex;
                            triangles.Add(a); triangles.Add(b); triangles.Add(c);
                            triangles.Add(a); triangles.Add(c); triangles.Add(d);
                        }
                        break;
                }
            }
        }

        /// <summary>Adds a missing dangle bone (and any missing ancestors) at its bind pose. It then simply follows its parent.</summary>
        private bool TryCreateBone(string boneName, ExtraBone[] extras, out Transform bone)
        {
            bone = null;
            if (extras == null) return false;
            for (int i = 0; i < extras.Length; i++)
            {
                if (extras[i].name != boneName) continue;
                var def = extras[i];
                if (!skeleton.TryGetValue(def.parent, out var parent) && !TryCreateBone(def.parent, extras, out parent)) return false;
                bone = new GameObject(def.name).transform;
                bone.SetParent(parent, false);
                bone.SetLocalPositionAndRotation(def.localPosition, def.localRotation);
                bone.localScale = def.localScale == Vector3.zero ? Vector3.one : def.localScale;
                skeleton.Add(def.name, bone);
                return true;
            }
            return false;
        }

        private void BakeBlendShapes(Mesh m, BodyShapeWeights shape, int vc)
        {
            int count = m.blendShapeCount;
            if (count == 0) return;
            EnsureCapacity(vc);
            for (int s = 0; s < count; s++)
            {
                float weight = shape.WeightFor(m.GetBlendShapeName(s));
                if (weight <= 0f) continue;
                int frames = m.GetBlendShapeFrameCount(s);
                if (frames == 0) continue;

                // Unity's frame interpolation: scale the first frame below its weight, else lerp between bracketing frames.
                float firstWeight = m.GetBlendShapeFrameWeight(s, 0);
                if (frames == 1 || weight <= firstWeight)
                {
                    m.GetBlendShapeFrameVertices(s, 0, deltaV, deltaN, deltaT);
                    Accumulate(vc, weight / Mathf.Max(firstWeight, 0.0001f), deltaV, deltaN, deltaT);
                    continue;
                }
                int hi = 1;
                while (hi < frames - 1 && m.GetBlendShapeFrameWeight(s, hi) < weight) hi++;
                float w0 = m.GetBlendShapeFrameWeight(s, hi - 1);
                float w1 = m.GetBlendShapeFrameWeight(s, hi);
                float t = Mathf.Clamp01((weight - w0) / Mathf.Max(w1 - w0, 0.0001f));
                m.GetBlendShapeFrameVertices(s, hi - 1, deltaV, deltaN, deltaT);
                m.GetBlendShapeFrameVertices(s, hi, deltaV2, deltaN2, deltaT2);
                for (int i = 0; i < vc; i++)
                {
                    partVerts[i] += Vector3.LerpUnclamped(deltaV[i], deltaV2[i], t);
                    partNormals[i] += Vector3.LerpUnclamped(deltaN[i], deltaN2[i], t);
                    var dt = Vector3.LerpUnclamped(deltaT[i], deltaT2[i], t);
                    var tan = partTangents[i];
                    partTangents[i] = new Vector4(tan.x + dt.x, tan.y + dt.y, tan.z + dt.z, tan.w);
                }
            }
            for (int i = 0; i < vc; i++)
            {
                partNormals[i] = partNormals[i].normalized;
                var tan = partTangents[i];
                var dir = new Vector3(tan.x, tan.y, tan.z).normalized;
                partTangents[i] = new Vector4(dir.x, dir.y, dir.z, tan.w);
            }
        }

        private void Accumulate(int vc, float scale, Vector3[] dv, Vector3[] dn, Vector3[] dt)
        {
            for (int i = 0; i < vc; i++)
            {
                partVerts[i] += dv[i] * scale;
                partNormals[i] += dn[i] * scale;
                var tan = partTangents[i];
                partTangents[i] = new Vector4(tan.x + dt[i].x * scale, tan.y + dt[i].y * scale, tan.z + dt[i].z * scale, tan.w);
            }
        }

        private void EnsureCapacity(int vc)
        {
            if (deltaV.Length == vc) return;
            deltaV = new Vector3[vc]; deltaN = new Vector3[vc]; deltaT = new Vector3[vc];
            deltaV2 = new Vector3[vc]; deltaN2 = new Vector3[vc]; deltaT2 = new Vector3[vc];
        }
    }
}
