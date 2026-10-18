using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace KryticalNova.EditorTools.Cleanup
{
    /// <summary>
    /// The two checks the cleanup runs besides Maintainer's, so a file goes only when all three say nothing uses it
    /// (owner, 18 Oct 2026: "make sure your system's foolproof"). Both start from the same roots: every asset outside
    /// the folders the cleanup may touch (all of <c>Assets/_Game</c>, every zone and scene in it, Resources, plugins,
    /// tools), every scene in the build list, the kept vendor folders, and the files <c>ProjectSettings</c> and the
    /// embedded packages point at.
    /// <list type="bullet">
    /// <item><b>Unity's dependencies</b>: <see cref="AssetDatabase.GetDependencies(string[], bool)"/> from the roots.</item>
    /// <item><b>Written references</b>: every GUID written in the roots' YAML, JSON and .meta files, followed file by
    /// file through what they point at, without asking Unity, so a reference Unity does not report is still caught.</item>
    /// </list>
    /// </summary>
    public static class UsageGuard
    {
        private static readonly Regex GuidRef = new Regex(@"guid\\?""?\s*:\s*\\?""?([0-9a-f]{32})", RegexOptions.Compiled);
        private static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".unity", ".prefab", ".asset", ".mat", ".controller", ".overridecontroller", ".anim", ".mask", ".playable", ".signal",
            ".physicmaterial", ".physicsmaterial", ".lighting", ".spriteatlas", ".spriteatlasv2", ".shadergraph", ".shadersubgraph",
            ".vfx", ".terrainlayer", ".brush", ".flare", ".renderTexture", ".cubemap", ".guiskin", ".fontsettings", ".json",
            ".inputactions", ".preset", ".mixer", ".giparams", ".uss", ".uxml", ".tss", ".asmdef", ".asmref", ".index",
        };

        public sealed class Result
        {
            public HashSet<string> UnityClosure;
            public HashSet<string> WrittenClosure;
            public int Roots;
        }

        /// <summary>Paths both checks count as used. <paramref name="isCandidate"/> says which files the cleanup may touch.</summary>
        public static Result Run(Func<string, bool> isCandidate, IEnumerable<string> extraRoots, Action<string, float> progress)
        {
            var roots = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (!isCandidate(path)) roots.Add(path);
            }
            foreach (var scene in EditorBuildSettings.scenes) if (!string.IsNullOrEmpty(scene.path)) roots.Add(scene.path);
            foreach (string path in extraRoots) roots.Add(path);
            foreach (string guid in GuidsInSettingsAndPackages())
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal)) roots.Add(path);
            }

            var result = new Result { Roots = roots.Count };

            progress?.Invoke("Unity dependencies of " + roots.Count.ToString("N0") + " roots", 0.1f);
            result.UnityClosure = new HashSet<string>(roots, StringComparer.Ordinal);
            var list = roots.ToArray();
            const int chunk = 2000;
            for (int i = 0; i < list.Length; i += chunk)
            {
                progress?.Invoke("Unity dependencies", 0.1f + 0.4f * i / list.Length);
                foreach (string dep in AssetDatabase.GetDependencies(list.Skip(i).Take(chunk).ToArray(), true))
                    result.UnityClosure.Add(dep.Replace('\\', '/'));
            }

            result.WrittenClosure = WrittenClosure(roots, p => progress?.Invoke("Written references", 0.5f + 0.5f * p));
            return result;
        }

        /// <summary>Every file reached by following the GUIDs written in the roots' text and metas, then in what they reach.</summary>
        public static HashSet<string> WrittenClosure(IEnumerable<string> roots, Action<float> progress)
        {
            var reached = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            foreach (string r in roots) if (reached.Add(r)) queue.Enqueue(r);
            int done = 0;
            var guids = new HashSet<string>();
            while (queue.Count > 0)
            {
                string path = queue.Dequeue();
                if ((++done & 1023) == 0) progress?.Invoke(Math.Min(1f, (float)done / Math.Max(1, reached.Count)));
                guids.Clear();
                ReadGuids(path + ".meta", guids);
                if (TextExtensions.Contains(Path.GetExtension(path)) || LooksLikeYaml(path)) ReadGuids(path, guids);
                foreach (string guid in guids)
                {
                    string target = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(target) || !target.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    if (reached.Add(target)) queue.Enqueue(target);
                }
            }
            progress?.Invoke(1f);
            return reached;
        }

        /// <summary>The GUIDs written in ProjectSettings (render pipeline, preloaded assets, input, always-included shaders) and in embedded packages.</summary>
        public static HashSet<string> GuidsInSettingsAndPackages()
        {
            var guids = new HashSet<string>();
            foreach (string file in Directory.GetFiles("ProjectSettings", "*.asset", SearchOption.AllDirectories)) ReadGuids(file, guids);
            if (Directory.Exists("Packages"))
                foreach (string dir in Directory.GetDirectories("Packages"))
                foreach (string file in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
                    if (TextExtensions.Contains(Path.GetExtension(file)) || file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) ReadGuids(file, guids);
            return guids;
        }

        /// <summary>
        /// After a removal: the removed GUIDs that something still in the project writes a reference to. The triple
        /// check should leave this empty; anything it finds is restored at once.
        /// </summary>
        public static HashSet<string> StillReferenced(Func<string, bool> isCandidate, ISet<string> removedGuids)
        {
            var roots = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(p) && !isCandidate(p)).ToList();
            var broken = new HashSet<string>(GuidsInSettingsAndPackages().Where(removedGuids.Contains));
            var guids = new HashSet<string>();
            foreach (string path in WrittenClosure(roots, null))
            {
                guids.Clear();
                ReadGuids(path + ".meta", guids);
                if (TextExtensions.Contains(Path.GetExtension(path)) || LooksLikeYaml(path)) ReadGuids(path, guids);
                foreach (string g in guids) if (removedGuids.Contains(g) && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(g))) broken.Add(g);
            }
            return broken;
        }

        private static void ReadGuids(string file, HashSet<string> into)
        {
            if (!File.Exists(file)) return;
            string text;
            try { text = File.ReadAllText(file); }
            catch (IOException) { return; }
            foreach (Match m in GuidRef.Matches(text)) into.Add(m.Groups[1].Value);
        }

        private static bool LooksLikeYaml(string path)
        {
            try
            {
                using (var s = File.OpenRead(path))
                {
                    var head = new byte[5];
                    return s.Read(head, 0, 5) == 5 && Encoding.ASCII.GetString(head) == "%YAML";
                }
            }
            catch (IOException) { return false; }
        }
    }
}
