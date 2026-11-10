using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools.World
{
    /// <summary>
    /// Concord 040's four corner thrusters (owner, 9 Nov 2026): the package's flat cyan cones are cut out of the chunk meshes and each
    /// nozzle gets a real plume instead: KryticalNova/Thruster (unlit additive, UV scrolling in the shader, no CPU per frame). A
    /// LODGroup per thruster swaps the look by distance, with no script: LOD0 = rich cone (two noise layers, bright inner cone) for players on the
    /// platform looking over the edge; LOD1 = one camera-facing card with one noise sample for Zone01's ground 300 m below; culled far away.
    /// Rerun-safe (the old plumes are replaced). Called by Concord040Setup.
    /// </summary>
    public static class Concord040Thrusters
    {
        const string Folder = "Assets/_Game/World/Zones/Concord040/Thrusters";
        const float Height = 70f, TopY = -22.2f, Corner = 68f;
        const float OldHeight = 11f, OldTopY = -22.5f;   // the package cones that are cut out
        const float OpeningRadius = 5f;                  // the nozzle ring is about 10 m across
        public const float Lod0ScreenHeight = 0.16f;   // below this the card takes over (the quality level doubles it: lodBias 2, so the card starts about 190 m away)
        public const float Lod1ScreenHeight = 0.02f;   // below this nothing is drawn (about 1.5 km)

        public static void Apply(GameObject prefabRoot)
        {
            var old = prefabRoot.transform.Find("Thrusters");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            CutOldCones(prefabRoot);
            Mesh cone = Asset("ThrusterCone.asset", BuildCone());
            Mesh card = Asset("ThrusterCard.asset", BuildCard());
            Texture2D noise = BuildNoise();
            Material high = MakeMaterial("Thruster_High.mat", noise, true, false, 1.0f, new Vector4(4f, 9f, 0, 0));
            Material far = MakeMaterial("Thruster_Card.mat", noise, false, true, 1.7f, new Vector4(2f, 5f, 0, 0));

            var group = new GameObject("Thrusters").transform;
            group.SetParent(prefabRoot.transform, false);
            string[] names = { "SW", "NW", "SE", "NE" };
            int n = 0;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    var root = new GameObject("Thruster_" + names[n++]);
                    root.transform.SetParent(group, false);
                    root.transform.localPosition = new Vector3(sx * Corner, TopY, sz * Corner);
                    var r0 = Part(root.transform, "LOD0", cone, high);
                    var r1 = Part(root.transform, "LOD1", card, far);
                    var lod = root.AddComponent<LODGroup>();
                    lod.fadeMode = LODFadeMode.None;
                    lod.SetLODs(new[] { new LOD(Lod0ScreenHeight, new[] { r0 }), new LOD(Lod1ScreenHeight, new[] { r1 }) });
                    lod.RecalculateBounds();
                    lod.size = 13f;                                 // switch distances are measured on the nozzle, not on the 70 m flame
                    lod.localReferencePoint = new Vector3(0f, -6f, 0f);
                }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Concord040] 4 thrusters: LOD0 screen height >= {Lod0ScreenHeight}, card down to {Lod1ScreenHeight}, culled below.");
        }

        static Renderer Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        // The package's cyan plume cones: whole Material_002 triangles inside a box under each corner nozzle.
        static void CutOldCones(GameObject root)
        {
            var boxes = new List<Bounds>();
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    boxes.Add(new Bounds(new Vector3(sx * Corner, OldTopY - OldHeight * 0.5f, sz * Corner), new Vector3(8f, OldHeight + 1.2f, 8f)));
            int cut = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var r = mf.GetComponent<MeshRenderer>();
                Mesh m = mf.sharedMesh;
                if (r == null || m == null) continue;
                var mats = r.sharedMaterials;
                Vector3[] vs = null;
                for (int s = 0; s < m.subMeshCount && s < mats.Length; s++)
                {
                    if (mats[s] == null || mats[s].name != "Material_002") continue;
                    vs ??= m.vertices;
                    var tris = m.GetTriangles(s);
                    var keep = new List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        bool inside = false;
                        foreach (var b in boxes)
                            if (b.Contains(mf.transform.TransformPoint(vs[tris[i]])) && b.Contains(mf.transform.TransformPoint(vs[tris[i + 1]])) && b.Contains(mf.transform.TransformPoint(vs[tris[i + 2]]))) { inside = true; break; }
                        if (inside) { cut++; continue; }
                        keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]);
                    }
                    if (keep.Count == tris.Length) continue;
                    m.SetTriangles(keep, s);
                    EditorUtility.SetDirty(m);
                }
            }
            if (cut > 0) Debug.Log($"[Concord040] Cut {cut} triangles of the old flat cones.");
        }

        static Mesh BuildCone()
        {
            var v = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var col = new List<Color>(); var idx = new List<int>();
            void Cone(int sides, float r0, float r1, float h, float inner)
            {
                int b = v.Count;
                for (int ring = 0; ring < 2; ring++)
                    for (int i = 0; i <= sides; i++)
                    {
                        float a = i / (float)sides * Mathf.PI * 2f, c = Mathf.Cos(a), s = Mathf.Sin(a);
                        float r = ring == 0 ? r0 : r1;
                        v.Add(new Vector3(c * r, -ring * h, s * r));
                        nrm.Add(new Vector3(c, 0.15f, s).normalized);
                        uv.Add(new Vector2(i / (float)sides, ring));
                        col.Add(new Color(inner, 0, 0, 1));
                    }
                for (int i = 0; i < sides; i++)
                {
                    int a0 = b + i, a1 = b + i + 1, b0 = b + sides + 1 + i, b1 = b + sides + 2 + i;
                    idx.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
                }
            }
            Cone(20, OpeningRadius, 0.6f, Height, 0f);         // the outer flame
            Cone(12, OpeningRadius * 0.5f, 0.15f, Height * 0.75f, 1f); // the hot inner cone
            var m = new Mesh { name = "ThrusterCone" };
            m.SetVertices(v); m.SetNormals(nrm); m.SetUVs(0, uv); m.SetColors(col); m.SetTriangles(idx, 0);
            m.bounds = new Bounds(new Vector3(0, -Height * 0.5f, 0), new Vector3(2f * OpeningRadius, Height, 2f * OpeningRadius));
            return m;
        }

        static Mesh BuildCard()
        {
            float w = OpeningRadius, wt = 0.6f;   // as wide as the opening at the top, the cone's taper below
            var m = new Mesh { name = "ThrusterCard" };
            m.SetVertices(new[] { new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(-wt, -Height, 0), new Vector3(wt, -Height, 0) });
            m.SetNormals(new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetColors(new[] { Color.black, Color.black, Color.black, Color.black });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.bounds = new Bounds(new Vector3(0, -Height * 0.5f, 0), new Vector3(2 * w, Height, 2 * w));
            return m;
        }

        static T Asset<T>(string file, T made) where T : Object
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/" + file;
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(made, path);
            return made;
        }

        static Material MakeMaterial(string file, Texture2D noise, bool high, bool card, float intensity, Vector4 noiseScale)
        {
            string path = Folder + "/" + file;
            AssetDatabase.DeleteAsset(path);
            var mat = new Material(Shader.Find("KryticalNova/Thruster")) { name = file.Replace(".mat", ""), enableInstancing = true };
            mat.SetTexture("_NoiseTex", noise);
            mat.SetFloat("_Intensity", intensity);
            mat.SetVector("_NoiseScale", noiseScale);
            if (high) { mat.SetFloat("_High", 1f); mat.EnableKeyword("_HIGH"); }
            if (card) { mat.SetFloat("_Card", 1f); mat.EnableKeyword("_CARD"); }
            mat.renderQueue = 3000;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // A small tileable noise (wrapped value noise, two octaves) saved as a PNG.
        static Texture2D BuildNoise()
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/ThrusterNoise.png";
            if (!File.Exists(path))
            {
                const int size = 128;
                var rng = new System.Random(4021);
                float[,] Lattice(int n) { var a = new float[n, n]; for (int x = 0; x < n; x++) for (int y = 0; y < n; y++) a[x, y] = (float)rng.NextDouble(); return a; }
                var l1 = Lattice(8); var l2 = Lattice(16);
                float Sample(float[,] l, int n, float u, float v)
                {
                    float x = u * n, y = v * n; int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                    float fx = Mathf.SmoothStep(0, 1, x - x0), fy = Mathf.SmoothStep(0, 1, y - y0);
                    float a = l[x0 % n, y0 % n], b = l[(x0 + 1) % n, y0 % n], c = l[x0 % n, (y0 + 1) % n], d = l[(x0 + 1) % n, (y0 + 1) % n];
                    return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
                }
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        float u = x / (float)size, v = y / (float)size;
                        float g = Mathf.Clamp01(Sample(l1, 8, u, v) * 0.65f + Sample(l2, 16, u, v) * 0.35f);
                        g = Mathf.Clamp01((g - 0.25f) * 1.6f);
                        tex.SetPixel(x, y, new Color(g, g, g, 1f));
                    }
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.sRGBTexture = false; imp.wrapMode = TextureWrapMode.Repeat; imp.mipmapEnabled = true; imp.textureCompression = TextureImporterCompression.Compressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
