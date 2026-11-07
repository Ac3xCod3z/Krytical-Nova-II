using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Lowers the shader compiling of the Windows join-here test client (<see cref="KnWindowsTestBuild"/>, owner 6 Nov 2026: its
    /// build was stopped twice by the RAM watchdog while compiling Synty shaders). Active only while that build runs, so the normal
    /// client and server builds and every Addressables bundle are untouched. Two rules, both only dropping variants this player can
    /// never draw:
    /// (1) a sub shader made for another pipeline (a Shader Graph's Built-In target: "RenderPipeline" tag of another pipeline, or a
    ///     <c>_BUILTIN_</c> keyword) is never picked by URP;
    /// (2) material keywords: for a shader drawn by the build's materials (the map, the ghost subscene and its bake scene, the player
    ///     world assets and everything under a Resources folder), a variant is kept only when the material keywords it has (keywords
    ///     any material of the project sets on that shader) are all set together on at least one of those materials. URP's global
    ///     keywords (shadows, lights, fog) are never material keywords, so they stay. A shader no build material uses keeps all.
    /// A keyword the game turns on at run time on a material that has it off in the asset loses its variant here: that look falls back
    /// to the plain one in this test client (never pink). <c>KN_KEEP_SHADERS=1</c> switches the strip off.
    /// </summary>
    public sealed class KnWindowsTestShaderStrip : IPreprocessShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        static Dictionary<string, List<HashSet<string>>> s_Used;      // shader key -> keyword sets of the build's materials
        static Dictionary<string, HashSet<string>> s_MaterialKeywords; // shader key -> every keyword a project material sets on it
        static int s_In, s_Kept, s_OtherPipeline, s_Keywords;
        static readonly Dictionary<string, int> s_StrippedBy = new();

        public int callbackOrder => int.MaxValue; // last, after URP's own stripping

        static bool Active =>
            KnWindowsTestBuild.IsBuilding
            && EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64
            && Environment.GetEnvironmentVariable("KN_KEEP_SHADERS") != "1";

        static readonly Regex ShaderLine = new(@"^\s*m_Shader:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);
        static readonly Regex ListItem = new(@"^\s*-\s*(\S+)\s*$", RegexOptions.Compiled);

        static string Key(string guid, long fileId) => guid + ":" + fileId;

        static string KeyOf(Shader shader) =>
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string guid, out long id) ? Key(guid, id) : null;

        /// <summary>The shader key and the m_ValidKeywords of a material file, read as text (nothing is loaded).</summary>
        static bool ReadMaterial(string path, out string key, out HashSet<string> keywords)
        {
            key = null;
            keywords = new HashSet<string>();
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (IOException) { return false; }
            bool inValid = false;
            foreach (string line in lines)
            {
                Match m = ShaderLine.Match(line);
                if (m.Success) { key = Key(m.Groups[2].Value, long.Parse(m.Groups[1].Value)); continue; }
                if (line.StartsWith("  m_ValidKeywords:")) { inValid = !line.TrimEnd().EndsWith("[]"); continue; }
                if (inValid)
                {
                    Match item = ListItem.Match(line);
                    if (item.Success) keywords.Add(item.Groups[1].Value);
                    else inValid = false;
                }
            }
            return key != null;
        }

        /// <summary>Collects the build's materials before <c>BuildPipeline.BuildPlayer</c>; <paramref name="roots"/> are asset paths.</summary>
        public static void Prepare(IEnumerable<string> roots)
        {
            string project = Path.GetDirectoryName(Application.dataPath) ?? ".";
            s_MaterialKeywords = new Dictionary<string, HashSet<string>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ReadMaterial(Path.Combine(project, path), out string key, out var keywords) || keywords.Count == 0) continue;
                if (!s_MaterialKeywords.TryGetValue(key, out var all)) s_MaterialKeywords[key] = all = new HashSet<string>();
                all.UnionWith(keywords);
            }

            var rootList = roots.ToList();
            string[] resources = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && p.Contains("/Resources/") && !AssetDatabase.IsValidFolder(p)).ToArray();
            rootList.AddRange(resources);
            s_Used = new Dictionary<string, List<HashSet<string>>>();
            int materials = 0;
            foreach (string path in AssetDatabase.GetDependencies(rootList.ToArray(), true))
            {
                if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                if (!ReadMaterial(Path.Combine(project, path), out string key, out var keywords)) continue;
                if (!s_Used.TryGetValue(key, out var sets)) s_Used[key] = sets = new List<HashSet<string>>();
                sets.Add(keywords);
                materials++;
            }
            Debug.Log($"[KnWindowsTestShaderStrip] {materials} build materials on {s_Used.Count} shaders ({resources.Length} Resources assets in the roots).");
        }

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            s_In = s_Kept = s_OtherPipeline = s_Keywords = 0;
            s_StrippedBy.Clear();
        }

        static readonly ShaderTagId PipelineTag = new("RenderPipeline");

        static bool OtherPipeline(Shader shader, ShaderSnippetData snippet)
        {
            string tag = shader.FindSubshaderTagValue((int)snippet.pass.SubshaderIndex, PipelineTag).name;
            return !string.IsNullOrEmpty(tag) && !tag.StartsWith("Universal", StringComparison.OrdinalIgnoreCase);
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!Active || shader == null || s_Used == null) return;
            int before = data.Count;
            s_In += before;
            if (OtherPipeline(shader, snippet))
            {
                s_OtherPipeline += before;
                Count(shader, before);
                data.Clear();
                return;
            }

            string key = KeyOf(shader);
            s_Used.TryGetValue(key ?? "", out var sets);
            HashSet<string> materialKeywords = null;
            if (key != null) s_MaterialKeywords.TryGetValue(key, out materialKeywords);
            for (int i = data.Count - 1; i >= 0; i--)
            {
                ShaderKeyword[] keywords = data[i].shaderKeywordSet.GetShaderKeywords();
                bool builtIn = false;
                var own = new List<string>();
                foreach (ShaderKeyword k in keywords)
                {
                    string name = k.name;
                    if (name.StartsWith("_BUILTIN_", StringComparison.Ordinal)) { builtIn = true; break; }
                    if (materialKeywords != null && materialKeywords.Contains(name)) own.Add(name);
                }
                if (builtIn) { s_OtherPipeline++; data.RemoveAt(i); continue; }
                if (sets == null || own.Count == 0) continue;
                if (!sets.Any(set => own.All(set.Contains))) { s_Keywords++; data.RemoveAt(i); }
            }
            Count(shader, before - data.Count);
            s_Kept += data.Count;
        }

        static void Count(Shader shader, int stripped)
        {
            if (stripped <= 0) return;
            s_StrippedBy.TryGetValue(shader.name, out int n);
            s_StrippedBy[shader.name] = n + stripped;
        }

        public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            if (s_In == 0) return;
            string top = string.Join(", ", s_StrippedBy.OrderByDescending(p => p.Value).Take(8).Select(p => $"{p.Key} {p.Value}"));
            Debug.Log($"[KnWindowsTestShaderStrip] {s_In} variants in, {s_Kept} kept; stripped {s_OtherPipeline} of another pipeline and " +
                      $"{s_Keywords} with material keywords no build material sets. Most stripped: {top}.");
        }
    }
}
