using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The owner closed Unity and got a FATAL ERROR (14 Nov): SkyEventPresenter.OnDestroy put the sky material back through
    /// RenderSettings after Unity had torn that scene manager down. These keep it from coming back, in the sky and anywhere else.
    /// </summary>
    public sealed class TeardownSafetyTests
    {
        [Test]
        public void SkyRestore_NeverWhileShuttingDown_OnlyWhileTheSceneIsLoaded()
        {
            Assert.IsTrue(SkyEventPresenter.MayRestoreSky(false, true), "running, scene loaded: the map's sky comes back");
            Assert.IsFalse(SkyEventPresenter.MayRestoreSky(true, true), "quitting: RenderSettings may already be gone");
            Assert.IsFalse(SkyEventPresenter.MayRestoreSky(true, false));
            Assert.IsFalse(SkyEventPresenter.MayRestoreSky(false, false), "the active scene is unloading");
        }

        [Test]
        public void NoteShutdown_IsRemembered()
        {
            PropertyInfo flag = typeof(SkyEventPresenter).GetProperty(nameof(SkyEventPresenter.ShuttingDown));
            bool before = SkyEventPresenter.ShuttingDown;
            try
            {
                flag.SetValue(null, false);
                SkyEventPresenter.NoteShutdown();
                Assert.IsTrue(SkyEventPresenter.ShuttingDown);
            }
            finally { flag.SetValue(null, before); }
        }

        static readonly Regex Teardown = new Regex(@"\bvoid\s+(OnDestroy|OnDisable|OnApplicationQuit)\s*\(\s*\)");
        static readonly Regex Declaration = new Regex(@"\b[\w<>\[\],.?]+\s+(\w+)\s*\([^;{)]*\)\s*(?:=>|\{)");
        static readonly Regex Call = new Regex(@"(?<![\.\w])(\w+)\s*\(");

        /// <summary>
        /// Every first-party runtime script whose OnDestroy / OnDisable / OnApplicationQuit reaches RenderSettings (itself or through
        /// methods of the same file, three calls deep) must check a shutdown flag (<c>ShuttingDown</c>) on that path.
        /// </summary>
        [Test]
        public void Teardown_NeverTouchesRenderSettingsWithoutAShutdownGuard()
        {
            string root = Path.Combine(Application.dataPath, "_Game");
            var bad = new List<string>();
            int checkedFiles = 0;
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (path.Contains("/Tests/") || path.Contains("/Editor/") || path.Contains("~/")) continue;
                string s = File.ReadAllText(file);
                if (!s.Contains("RenderSettings.")) continue;
                checkedFiles++;

                var methods = new Dictionary<string, string>();
                foreach (Match m in Declaration.Matches(s))
                    if (!methods.ContainsKey(m.Groups[1].Value)) methods[m.Groups[1].Value] = Body(s, m.Index);

                foreach (Match m in Teardown.Matches(s))
                {
                    var text = new StringBuilder(Body(s, m.Index + m.Length));
                    var seen = new HashSet<string>();
                    var todo = new List<string> { text.ToString() };
                    for (int depth = 0; depth < 3 && todo.Count > 0; depth++)
                    {
                        var next = new List<string>();
                        foreach (string t in todo)
                            foreach (Match c in Call.Matches(t))
                                if (methods.TryGetValue(c.Groups[1].Value, out string b) && seen.Add(c.Groups[1].Value)) next.Add(b);
                        foreach (string b in next) text.Append(b);
                        todo = next;
                    }
                    string all = text.ToString();
                    if (all.Contains("RenderSettings.") && !all.Contains("ShuttingDown"))
                        bad.Add(path.Substring(path.IndexOf("Assets/")) + " " + m.Groups[1].Value);
                }
            }
            Assert.Greater(checkedFiles, 0, "the scan found no scripts: wrong folder?");
            Assert.IsEmpty(bad, "teardown reaches RenderSettings without a shutdown guard (FATAL ERROR at quit):\n" + string.Join("\n", bad));
        }

        static string Body(string s, int from)
        {
            int brace = s.IndexOf('{', from), arrow = s.IndexOf("=>", from, System.StringComparison.Ordinal);
            if (arrow != -1 && (brace == -1 || arrow < brace))
            {
                int end = s.IndexOf(';', arrow);
                return end == -1 ? s.Substring(arrow) : s.Substring(arrow, end - arrow + 1);
            }
            if (brace == -1) return string.Empty;
            int depth = 0;
            for (int i = brace; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}' && --depth == 0) return s.Substring(brace, i - brace + 1);
            }
            return string.Empty;
        }
    }
}
