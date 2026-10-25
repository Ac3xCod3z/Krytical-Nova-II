using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Glowing eyes (the creator's Glowing Eyes row): the eyes exactly as they look without the glow, drawn once more
    /// in a second material that adds an emission channel (owner, 24 Oct 2026: "take our normal eye colour ... make a
    /// second material of that same thing and give it an emissive channel"). The Sidekick shader has no emission, so
    /// the copy is URP Lit reading the character's own colour map, with the eye-glow map (black but for the eyes) as its
    /// emission. The eyes are the faces of a part that read an eye cell: Sidekick's "Eye Color Left" (4,2) and "Eye
    /// Color Right" (5,2) irises, and (8,2), the whole eye of a skeleton's eye parts and the eyes painted into a robot's
    /// head (read from the parts, 24 Oct 2026). A robot has no eye parts, so its head is read only when none is worn.
    /// </summary>
    public sealed class EyeGlow
    {
        private const float CellSize = 1f / 16f;
        private static readonly Vector2Int[] EyeCells = { new Vector2Int(4, 2), new Vector2Int(5, 2), new Vector2Int(8, 2) };
        private const string LitShader = "Universal Render Pipeline/Lit";
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private static readonly Dictionary<(Mesh, Vector2Int), Mesh> Faces = new Dictionary<(Mesh, Vector2Int), Mesh>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Faces.Clear();

        private readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        private Material material; // the character's own: its colour map and its eye-glow map

        /// <summary>
        /// Draws <paramref name="parts"/>' eyes again in the second material: <paramref name="colourMap"/> as they look,
        /// <paramref name="emissionMap"/> times <paramref name="emission"/> as their light. Bones are found by name
        /// through <paramref name="findBone"/>.
        /// </summary>
        public void Show(IReadOnlyList<SidekickPartRef> parts, Texture colourMap, Texture emissionMap, float emission,
            Transform parent, int layer, Transform rootBone, Bounds bounds, BodyShapeWeights shape, System.Func<string, Transform> findBone)
        {
            Clear();
            if (parts == null || colourMap == null || emissionMap == null) return;
            if (material == null)
            {
                var shader = Shader.Find(LitShader);
                if (shader == null) { Debug.LogWarning("[EyeGlow] " + LitShader + " is missing."); return; }
                material = new Material(shader) { name = "M_EyeGlow (Character)", hideFlags = HideFlags.DontSave };
                material.SetColor(BaseColorId, Color.white);
                material.SetFloat(SmoothnessId, 0.5f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            material.SetTexture(BaseMapId, colourMap);
            material.SetTexture(EmissionMapId, emissionMap);
            material.SetColor(EmissionColorId, Color.white * emission);

            bool hasEyes = false;
            foreach (var part in parts) hasEyes |= part.region == BodyRegion.EyeLeft || part.region == BodyRegion.EyeRight;
            foreach (var part in parts)
            {
                bool eye = part.region == BodyRegion.EyeLeft || part.region == BodyRegion.EyeRight;
                if (!eye && !(part.region == BodyRegion.Head && !hasEyes)) continue;
                for (int c = 0; c < EyeCells.Length; c++)
                {
                    if (!eye && c < 2) continue; // a head's own iris cells are not eyes
                    var faces = FacesOf(part.mesh, EyeCells[c]);
                    if (faces == null) continue;

                    var go = new GameObject(part.mesh.name + "_Glow") { layer = layer };
                    go.transform.SetParent(parent, false);
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    var bones = new Transform[part.boneNames.Length];
                    for (int i = 0; i < bones.Length; i++) bones[i] = findBone(part.boneNames[i]) ?? parent;
                    smr.bones = bones;
                    smr.rootBone = rootBone;
                    smr.sharedMesh = faces;
                    smr.sharedMaterial = material;
                    smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    smr.localBounds = bounds;
                    for (int i = 0; i < faces.blendShapeCount; i++) smr.SetBlendShapeWeight(i, shape.WeightFor(faces.GetBlendShapeName(i)));
                    renderers.Add(smr);
                }
            }
        }

        public void Clear()
        {
            foreach (var r in renderers) if (r != null) Object.Destroy(r.gameObject);
            renderers.Clear();
        }

        /// <summary>Leaves the eyes for good (the character is gone): its own material too.</summary>
        public void Dispose()
        {
            Clear();
            if (material != null) Object.Destroy(material);
            material = null;
        }

        /// <summary>The faces of a part that read grid cell <paramref name="cell"/>, as a skinned mesh of their own (made once per part and cell).</summary>
        private static Mesh FacesOf(Mesh source, Vector2Int cell)
        {
            if (source == null) return null;
            if (Faces.TryGetValue((source, cell), out var made)) return made;
            Faces[(source, cell)] = null;
            if (!source.isReadable) return null;

            var uv = new List<Vector2>();
            source.GetUVs(0, uv);
            if (uv.Count != source.vertexCount) return null;
            var keep = new List<int>();
            for (int s = 0; s < source.subMeshCount; s++)
            {
                var tris = source.GetTriangles(s);
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    var centre = (uv[tris[i]] + uv[tris[i + 1]] + uv[tris[i + 2]]) / 3f;
                    if (InCell(centre, cell)) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
                }
            }
            if (keep.Count == 0) return null;

            // Only those vertices, in their old order, so bone weights, bind poses and body keys carry over.
            var map = new Dictionary<int, int>();
            var order = new List<int>();
            foreach (int v in keep) if (!map.ContainsKey(v)) { map[v] = order.Count; order.Add(v); }
            var verts = source.vertices; var normals = source.normals; var weights = source.boneWeights;
            var nv = new Vector3[order.Count]; var nn = new Vector3[order.Count]; var nw = new BoneWeight[order.Count]; var nuv = new Vector2[order.Count];
            for (int i = 0; i < order.Count; i++)
            {
                int o = order[i];
                // a hair in front of the eye it copies, so the two never fight over the same depth
                nv[i] = verts[o] + (normals.Length == verts.Length ? normals[o] * 0.0006f : Vector3.zero);
                if (normals.Length == verts.Length) nn[i] = normals[o];
                if (weights.Length == verts.Length) nw[i] = weights[o];
                nuv[i] = uv[o];
            }
            var tri = new int[keep.Count];
            for (int i = 0; i < keep.Count; i++) tri[i] = map[keep[i]];

            made = new Mesh { name = source.name + "_Eye" + cell.x + cell.y, vertices = nv, normals = nn, uv = nuv, boneWeights = nw, bindposes = source.bindposes, triangles = tri };
            var dv = new Vector3[source.vertexCount]; var dn = new Vector3[source.vertexCount]; var dt = new Vector3[source.vertexCount];
            var sv = new Vector3[order.Count]; var sn = new Vector3[order.Count]; var st = new Vector3[order.Count];
            for (int b = 0; b < source.blendShapeCount; b++)
                for (int f = 0; f < source.GetBlendShapeFrameCount(b); f++)
                {
                    source.GetBlendShapeFrameVertices(b, f, dv, dn, dt);
                    for (int i = 0; i < order.Count; i++) { sv[i] = dv[order[i]]; sn[i] = dn[order[i]]; st[i] = dt[order[i]]; }
                    made.AddBlendShapeFrame(source.GetBlendShapeName(b), source.GetBlendShapeFrameWeight(b, f), sv, sn, st);
                }
            made.RecalculateBounds();
            Faces[(source, cell)] = made;
            return made;
        }

        private static bool InCell(Vector2 uv, Vector2Int cell) =>
            uv.x >= cell.x * CellSize && uv.x < (cell.x + 1) * CellSize && uv.y >= cell.y * CellSize && uv.y < (cell.y + 1) * CellSize;
    }
}
