using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The dedicated server is built with NO render pipeline (owner rule, 16 Nov 2026: no visual code in the server build). The quality
    /// levels' URP assets name renderers whose features are Rendering's scripts (sun shafts, comic lettering, glow halo...); Rendering
    /// is left out of the server, so a server that packed them logged a missing script for every feature at startup. For the server
    /// build only, every quality level (and the Graphics default) points at no pipeline; afterwards each is put back as it was.
    /// <para>The black-sky rule (build 12): a game build must never see this. The values are written to a record in Library first, and
    /// any record left by a server build that was killed half way is put back before the next build of either kind and when the Editor
    /// starts (<see cref="RestoreLeftover"/>; <see cref="ClientBuildGuard.EnsurePlayer"/> calls it for every game build).</para>
    /// </summary>
    [InitializeOnLoad]
    public sealed class ServerRenderPipelineOff : IPreprocessBuildWithReport
    {
        public const string RecordPath = "Library/KryticalNova-ServerPipelineOff.txt";
        private const string QualityPath = "ProjectSettings/QualitySettings.asset";
        private const string GraphicsLine = "graphics";

        static ServerRenderPipelineOff() => EditorApplication.delayCall += () => RestoreLeftover("Editor start");

        /// <summary>First of the build's own steps: a game build puts back a leftover and refuses to build with no pipeline at all.</summary>
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (ServerBuildBuilder.IsBuilding || report.summary.GetSubtarget<StandaloneBuildSubtarget>() == StandaloneBuildSubtarget.Server) return;
            RestoreLeftover("game build");
            if (!AnyPipeline())
                throw new BuildFailedException("[Build] This game build has no render pipeline on any quality level (a server build's setting " +
                                               "left behind?). It would ship without its look; put the URP assets back in Project Settings > Quality.");
        }

        /// <summary>True when the Graphics default or any quality level names a pipeline.</summary>
        public static bool AnyPipeline()
        {
            foreach (string guid in Read().Values) if (!string.IsNullOrEmpty(guid)) return true;
            return false;
        }

        /// <summary>Switches the pipelines off until the returned handle is disposed (a server build's try/finally).</summary>
        public static IDisposable Begin()
        {
            Restore("server build");   // a leftover first, or "was" below would record the switched-off values
            var was = Read();
            File.WriteAllText(RecordPath, Format(was));
            Apply(null);
            Debug.Log($"[Build] Server build: no render pipeline ({was.Count - 1} quality level(s) and the Graphics default put back afterwards).");
            return new Handle(was);
        }

        /// <summary>
        /// Puts back what a killed server build left switched off, if anything. True when something was put back. A game built on a
        /// checkout with no pipeline would ship without its look (build 12's black sky), so this runs before every build.
        /// </summary>
        public static bool RestoreLeftover(string why) =>
            !ServerBuildBuilder.IsBuilding && Restore(why);   // while a server build runs, its own finally puts them back

        private static bool Restore(string why)
        {
            if (!File.Exists(RecordPath)) return false;
            Dictionary<string, string> was = Parse(File.ReadAllText(RecordPath));
            Put(was);
            File.Delete(RecordPath);
            Debug.LogWarning($"[Build] {why}: a server build was stopped before it put the render pipelines back; they are back now ({was.Count} value(s)).");
            return true;
        }

        private sealed class Handle : IDisposable
        {
            private Dictionary<string, string> was;
            public Handle(Dictionary<string, string> was) => this.was = was;

            public void Dispose()
            {
                if (was == null) return;
                Put(was);
                was = null;
                if (File.Exists(RecordPath)) File.Delete(RecordPath);
                Debug.Log("[Build] Server build: render pipelines put back.");
            }
        }

        // ------------------------------------------------------------------ read and write the settings

        /// <summary>The pipeline asset of the Graphics default ("graphics") and of every quality level ("0", "1", ...), as asset GUIDs ("" = none).</summary>
        private static Dictionary<string, string> Read()
        {
            var values = new Dictionary<string, string> { [GraphicsLine] = Guid(GraphicsSettings.defaultRenderPipeline) };
            SerializedProperty levels = Levels(out _);
            for (int i = 0; i < levels.arraySize; i++)
                values[i.ToString()] = Guid(levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue);
            return values;
        }

        /// <summary>Every pipeline set to <paramref name="asset"/> (null: none).</summary>
        private static void Apply(RenderPipelineAsset asset)
        {
            GraphicsSettings.defaultRenderPipeline = asset;
            SerializedProperty levels = Levels(out SerializedObject quality);
            for (int i = 0; i < levels.arraySize; i++)
                levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue = asset;
            quality.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Put(Dictionary<string, string> values)
        {
            if (values.TryGetValue(GraphicsLine, out string graphics)) GraphicsSettings.defaultRenderPipeline = Load(graphics);
            SerializedProperty levels = Levels(out SerializedObject quality);
            for (int i = 0; i < levels.arraySize; i++)
                if (values.TryGetValue(i.ToString(), out string guid))
                    levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue = Load(guid);
            quality.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Levels(out SerializedObject quality)
        {
            quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(QualityPath)[0]);
            return quality.FindProperty("m_QualitySettings");
        }

        private static string Guid(UnityEngine.Object asset) =>
            asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long _) ? guid : string.Empty;

        private static RenderPipelineAsset Load(string guid) =>
            string.IsNullOrEmpty(guid) ? null : AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));

        // ------------------------------------------------------------------ the record (pure, tested)

        /// <summary>One "key guid" line per value, sorted, so the record reads the same every time.</summary>
        public static string Format(IDictionary<string, string> values)
        {
            var keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            var text = new System.Text.StringBuilder();
            foreach (string key in keys) text.Append(key).Append(' ').Append(values[key] ?? string.Empty).Append('\n');
            return text.ToString();
        }

        /// <summary>The record read back; a line with no GUID means "no pipeline".</summary>
        public static Dictionary<string, string> Parse(string record)
        {
            var values = new Dictionary<string, string>();
            foreach (string raw in (record ?? string.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                int space = line.IndexOf(' ');
                if (space < 0) values[line] = string.Empty;
                else values[line.Substring(0, space)] = line.Substring(space + 1).Trim();
            }
            return values;
        }
    }
}
