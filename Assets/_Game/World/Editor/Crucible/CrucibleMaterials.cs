using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// The Crucible's materials, one per Blender material (MATERIALS_022b.json), all on Synty's PolygonShader so they batch with the
    /// packs (SRP Batcher, GPU instancing on). A Synty-based one starts as a copy of its pack material (texture, atlas, alpha clip kept)
    /// and gets Fable's tint, relief normal and emission; a Crucible one is made fresh. Box-projected stone ("box") uses the shader's
    /// triplanar normals at Fable's mapping scale; height maps are not used (PolygonShader has no height input, so they never enter git).
    /// </summary>
    public static class CrucibleMaterials
    {
        const string ShaderPath = "Assets/Synty/PolygonGeneric/Shaders/PolygonShader.shadergraph";
        const string SkyShader = "KN/Crucible Sky Dome";
        /// <summary>Blender emission strength to Unity HDR intensity (stops): Fable's floors use 5-7, his strips EV 3.</summary>
        const float EmissionStopsPerStrength = 0.6f;

        static readonly Dictionary<string, string> PackFolder = new Dictionary<string, string>
        {
            { "DarkFortress", "PolygonDarkFortress" }, { "DarkFantasy", "PolygonDarkFantasy" }, { "Knights", "PolygonKnights" },
            { "FantasyKingdom", "PolygonFantasyKingdom" }, { "SciFiCity", "PolygonSciFiCity" }, { "SciFiSpace", "PolygonSciFiSpace" },
            { "SciFiWorlds", "PolygonSciFiWorlds" },
        };

        public static string AssetName(string blenderName) => "M_Crucible__" + blenderName.Replace('/', '_');

        /// <summary>Makes or refreshes every material; returns them by Blender name.</summary>
        public static Dictionary<string, Material> Build(CrucibleData data, System.Text.StringBuilder log)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null) throw new System.Exception("PolygonShader not found at " + ShaderPath);
            if (!AssetDatabase.IsValidFolder(CrucibleData.MaterialFolder)) AssetDatabase.CreateFolder(CrucibleData.Folder, "Materials");
            var result = new Dictionary<string, Material>();
            int fromPack = 0, fresh = 0, missingTex = 0;
            s_Missing.Clear();
            foreach (JObject m in data.Materials)
            {
                string name = (string)m["name"];
                string path = $"{CrucibleData.MaterialFolder}/{AssetName(name)}.mat";
                Material source = FindPackMaterial((string)m["synty_pack"] ?? PackOf(name), (string)m["synty_material"]);
                Material mat;
                if ((string)m["mode"] == "unlit")
                {
                    mat = new Material(Shader.Find(SkyShader));
                    mat.SetTexture("_MainTex", Tex((string)m["emission_tex"]));
                }
                else
                {
                    mat = source != null ? new Material(source) : new Material(shader);
                    if (source != null) fromPack++; else fresh++;
                    // a pack material on another Synty graph (Generic_Basic / Generic_Standard / Basic_Wind): carry its maps over first
                    Texture baseMap = source != null ? First(source, "_Base_Texture", "_Albedo_Map", "_MainTex", "_BaseMap") : null;
                    Texture emitMap = source != null ? First(source, "_Emission_Texture", "_Emission_Map", "_EmissionMap") : null;
                    if (mat.shader != shader)
                    {
                        mat.shader = shader;
                        if (baseMap != null) mat.SetTexture("_Base_Texture", baseMap);
                        if (emitMap != null) mat.SetTexture("_Emission_Texture", emitMap);
                    }
                    Fill(mat, m, source, ref missingTex);
                }
                mat.name = AssetName(name);
                mat.enableInstancing = true;
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { existing.shader = mat.shader; existing.CopyPropertiesFromMaterial(mat); existing.enableInstancing = true; EditorUtility.SetDirty(existing); mat = existing; }
                else AssetDatabase.CreateAsset(mat, path);
                result[name] = mat;
            }
            AssetDatabase.SaveAssets();
            log.AppendLine($"Materials: {result.Count} ({fromPack} from Synty pack materials, {fresh} fresh), textures not found: {missingTex}{(s_Missing.Count > 0 ? " (" + string.Join(", ", s_Missing) + ")" : "")}.");
            return result;
        }

        static void Fill(Material mat, JObject m, Material source, ref int missingTex)
        {
            string mode = (string)m["mode"];
            // base colour: a pack texture (kept from the copy) or a Crucible texture, times Fable's tint; or a flat colour
            if (mode == "flat" || mode == "box")
            {
                mat.SetFloat("_Enable_Base_Texture", 0f);
                mat.SetFloat("_Enable_Triplanar_Texture", 0f);
                mat.SetColor("_Color_Tint", CrucibleData.Colour(m["base_colour"]));
                mat.SetColor("_Color", CrucibleData.Colour(m["base_colour"]));
            }
            else
            {
                string baseTex = (string)m["base_tex"];
                if (mat.GetTexture("_Base_Texture") == null)
                {
                    Texture2D t = Tex(baseTex);
                    if (t == null) missingTex++;
                    mat.SetTexture("_Base_Texture", t);
                }
                mat.SetFloat("_Enable_Base_Texture", 1f);
                mat.SetFloat("_Enable_Triplanar_Texture", 0f);
                Color tint = m["tint"] != null ? CrucibleData.Colour(m["tint"]) : Color.white;
                mat.SetColor("_Color_Tint", tint);
            }
            mat.SetFloat("_Smoothness", 1f - (float?)m["roughness"] ?? 0.2f);
            mat.SetFloat("_Metallic", (float?)m["metallic"] ?? 0f);

            // relief normal: UV-mapped on its own channel, or triplanar for box-projected stone
            string normal = (string)m["normal_tex"];
            Texture2D n = normal != null ? NormalTex(normal) : null;
            if (normal != null && n == null) missingTex++;
            float strength = Mathf.Clamp((float?)m["bump_strength"] ?? 0.35f, 0.1f, 1f) / 0.35f;
            if (n != null && mode == "box")
            {
                float tiling = (float?)m["box_scale"] ?? 0.333f;
                mat.SetFloat("_Enable_Normal_Texture", 0f);
                mat.SetFloat("_Enable_Triplanar_Normals", 1f);
                foreach (string side in new[] { "Top", "Side", "Bottom" })
                {
                    mat.SetTexture("_Triplanar_Normal_Texture_" + side, n);
                    mat.SetVector("_Triplanar_Normal_Tiling_" + side, new Vector4(tiling, tiling, 0f, 0f));
                    mat.SetFloat("_Triplanar_Normal_Intensity_" + side, strength);
                }
            }
            else if (n != null)
            {
                mat.SetTexture("_Normal_Texture", n);
                mat.SetFloat("_Enable_Normal_Texture", 1f);
                mat.SetFloat("_Normal_Intensity", strength);
                mat.SetFloat("_Enable_Triplanar_Normals", 0f);
            }

            // emission: Fable's strength as HDR stops on the emission texture (or colour)
            float es = (float?)m["emission_strength"] ?? 0f;
            string emit = (string)m["emission_tex"];
            if (es > 0.01f)
            {
                Texture2D e = emit != null ? Tex(emit) : null;
                if (e == null) e = mat.GetTexture("_Emission_Texture") as Texture2D;
                Color c = m["emission_colour"] != null ? CrucibleData.Colour(m["emission_colour"]) : Color.white;
                float intensity = Mathf.Pow(2f, es * EmissionStopsPerStrength);
                if (e != null) { mat.SetTexture("_Emission_Texture", e); mat.SetFloat("_Enable_Emission_Texture", 1f); }
                else mat.SetFloat("_Enable_Emission_Texture", 0f);
                mat.SetColor("_Emission_Color_Tint", c * intensity);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else
            {
                mat.SetFloat("_Enable_Emission_Texture", 0f);
                mat.SetColor("_Emission_Color_Tint", Color.black);
            }
        }

        static Texture First(Material m, params string[] names)
        {
            foreach (string n in names)
                if (m.HasProperty(n) && m.GetTexture(n) != null) return m.GetTexture(n);
            return null;
        }

        static string PackOf(string blenderName)
        {
            int slash = blenderName.IndexOf('/');
            return slash > 0 && PackFolder.TryGetValue(blenderName.Substring(0, slash), out string p) ? p : null;
        }

        static readonly Dictionary<string, Material> s_PackCache = new Dictionary<string, Material>();

        static Material FindPackMaterial(string pack, string material)
        {
            if (string.IsNullOrEmpty(pack) || string.IsNullOrEmpty(material)) return null;
            string key = pack + "/" + material;
            if (s_PackCache.TryGetValue(key, out Material cached)) return cached;
            Material found = null;
            foreach (string guid in AssetDatabase.FindAssets(material + " t:Material", new[] { "Assets/Synty/" + pack }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) != material) continue;
                found = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (!p.Contains("/Alts/")) break;   // the pack's main copy over an alternate one
            }
            s_PackCache[key] = found;
            return found;
        }

        /// <summary>A Crucible texture by Blender file name (a 2K normal of a floor is its 4K copy here).</summary>
        static readonly HashSet<string> s_Missing = new HashSet<string>();

        static Texture2D Tex(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            Texture2D found = TexOrNull(file);
            if (found == null) s_Missing.Add(file);
            return found;
        }

        static Texture2D TexOrNull(string file)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            foreach (string ext in new[] { ".png", ".jpg", ".tga" })
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CrucibleData.TextureFolder}/{stem}{ext}");
                if (t != null) return t;
            }
            return null;
        }

        static Texture2D NormalTex(string file)
        {
            Texture2D t = TexOrNull(file) ?? TexOrNull(Path.GetFileNameWithoutExtension(file).Replace("_Normal_2K", "_Normal_4K"));
            if (t == null) s_Missing.Add(file);
            return t;
        }
    }
}
