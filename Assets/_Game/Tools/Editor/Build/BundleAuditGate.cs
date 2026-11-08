using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The publish gate (31 Oct 2026, after ten of build 13's 29 maps shipped with the sky switched off): before a build goes
    /// to the TEST line, every built map bundle is opened with Unity's own tools (WebExtract and binary2text) and what is
    /// switched off in it is compared with the source scene as the Editor loads it. A bundle with more components switched
    /// off, or more objects inactive, than its scene fails the publish with a plain list. Server-only switch-offs
    /// (<see cref="ServerOffList"/>, ServerOff.md) never belong in a game's bundle, so there is no exception for them.
    /// Before Promote the check is read-only: the bytes now in the store must be the bytes audited (<see cref="VerifyStaged"/>).
    /// </summary>
    public static class BundleAuditGate
    {
        public sealed class Result
        {
            public bool Ok;
            public readonly List<string> Problems = new List<string>();
            public readonly StringBuilder Log = new StringBuilder();
            public string Report => (Ok ? "Bundle audit passed.\n" : "Bundle audit FAILED:\n  " + string.Join("\n  ", Problems) + "\n") + Log;
        }

        [Serializable]
        public sealed class Bundle { public string file; public string sha256; public long size; }

        [Serializable]
        public sealed class Record { public int build; public bool ok; public string created; public List<Bundle> bundles = new List<Bundle>(); }

        public const string BundlesFolder = "KryticalNovaII_Data/StreamingAssets/aa/StandaloneWindows64";
        private const string Scratch = "Temp/BundleAudit";

        public static string RecordPath(string releaseRoot, int build) => releaseRoot + "/bundle-audit-build-" + build + ".json";

        // ------------------------------------------------------------------ before the upload to TEST

        /// <summary>
        /// Audits every map bundle of a built game folder against its scene and writes the record next to the release
        /// (<see cref="RecordPath"/>). Opens each map scene in this Editor, one at a time: run it in the publish's batch Unity.
        /// </summary>
        public static Result Audit(string gameFolder, string releaseRoot, int build)
        {
            var result = new Result();
            string folder = Path.Combine(gameFolder, BundlesFolder);
            if (!Directory.Exists(folder)) { result.Problems.Add("no map bundles at " + folder); return result; }
            string tools = Path.Combine(EditorApplication.applicationContentsPath, "Tools");
            string webExtract = Path.Combine(tools, "WebExtract.exe"), binary2text = Path.Combine(tools, "binary2text.exe");
            if (!File.Exists(webExtract) || !File.Exists(binary2text)) { result.Problems.Add("Unity's WebExtract or binary2text is missing in " + tools); return result; }

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) { result.Problems.Add("the Addressables settings are missing"); return result; }

            var record = new Record { build = build, created = DateTime.UtcNow.ToString("s") + "Z" };
            int maps = 0;
            foreach (AddressableAssetGroup group in settings.groups)
            {
                if (group == null || !group.Name.StartsWith("World_", StringComparison.Ordinal) || group.Name == MapAddressables.CommonGroup) continue;
                List<string> scenes = group.entries.Select(e => e.AssetPath).Where(p => p != null && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)).ToList();
                if (scenes.Count == 0) continue;
                maps++;
                string prefix = group.Name.ToLowerInvariant() + "_scenes_all_";
                string[] files = Directory.GetFiles(folder, prefix + "*.bundle");
                if (files.Length != 1) { result.Problems.Add($"{group.Name}: expected one built bundle '{prefix}*.bundle', found {files.Length}."); continue; }

                MapCounts scene = CountScenes(scenes, out ObjectStates sceneStates);
                MapCounts bundle = CountBundle(files[0], webExtract, binary2text, result, out ObjectStates bundleStates);
                if (bundle == null) continue;
                List<string> problems = BundleDump.Compare(group.Name, scene, bundle);
                // Object by object, by name: catches a swap (one thing switched on, another off) that the counts miss.
                problems.AddRange(ObjectCompare.Compare(group.Name, sceneStates, bundleStates));
                result.Problems.AddRange(problems);
                result.Log.AppendLine($"  {group.Name}: scene off {scene.DisabledBehaviours}/{scene.InactiveObjects}, bundle off {bundle.DisabledBehaviours}/{bundle.InactiveObjects}{(problems.Count == 0 ? "" : "  <-- DIFFERS")}");
                record.bundles.Add(new Bundle { file = Path.GetFileName(files[0]), sha256 = Sha256(files[0]), size = new FileInfo(files[0]).Length });
            }
            if (maps == 0) result.Problems.Add("no map groups found in the Addressables settings");
            if (Directory.Exists(Scratch)) Directory.Delete(Scratch, true);

            result.Ok = result.Problems.Count == 0;
            record.ok = result.Ok;
            Directory.CreateDirectory(releaseRoot);
            File.WriteAllText(RecordPath(releaseRoot, build), JsonUtility.ToJson(record, true));
            result.Log.Insert(0, $"{maps} map(s) audited (counts are components off / objects inactive).\n");
            return result;
        }

        private static MapCounts CountScenes(List<string> scenePaths, out ObjectStates states)
        {
            var total = new MapCounts();
            states = new ObjectStates();
            var signatures = new Dictionary<Type, string>();
            foreach (string path in scenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        GameObject go = t.gameObject;
                        string objectPath = PathOf(t);
                        states.AddObject(objectPath, go.activeSelf);
                        foreach (Component component in go.GetComponents<Component>())
                        {
                            if (component == null) continue;
                            bool on;
                            if (component is Behaviour behaviour) on = behaviour.enabled;
                            else if (component is Renderer renderer) on = renderer.enabled;
                            else if (component is Collider collider) on = collider.enabled;
                            else continue;
                            string type = component.GetType().Name;
                            if (component is MonoBehaviour script)
                            {
                                if (!signatures.TryGetValue(script.GetType(), out string signature)) signatures[script.GetType()] = signature = SignatureOf(script);
                                states.ScriptNames[signature] = type;
                                type = ObjectStates.BehaviourPrefix + signature;
                            }
                            states.AddComponent(objectPath, type, on);
                        }
                        if (!go.activeSelf) total.InactiveObjects++;
                        foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
                        {
                            if (behaviour == null || behaviour.enabled) continue;
                            total.DisabledBehaviours++;
                            total.AddOff(SignatureOf(behaviour), behaviour.GetType().Name);
                        }
                    }
            }
            return total;
        }

        private static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (Transform current = t; current != null; current = current.parent) names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static string SignatureOf(MonoBehaviour behaviour)
        {
            var names = new List<string>();
            var so = new SerializedObject(behaviour);
            SerializedProperty it = so.GetIterator();
            if (it.Next(true)) do names.Add(it.name); while (it.Next(false));
            return BundleDump.SignatureOf(names);
        }

        private static MapCounts CountBundle(string bundleFile, string webExtract, string binary2text, Result result, out ObjectStates states)
        {
            states = null;
            string work = Path.GetFullPath(Scratch);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            string copy = Path.Combine(work, "map.bundle");
            File.Copy(bundleFile, copy);
            if (!Run(webExtract, "\"" + copy + "\"", work, out string error)) { result.Problems.Add(Path.GetFileName(bundleFile) + ": WebExtract failed: " + error); return null; }
            string data = copy + "_data";
            var objects = new List<DumpObject>();
            foreach (string cab in Directory.GetFiles(data).Where(f => !Path.GetFileName(f).Contains(".")))
            {
                string text = cab + ".txt";
                if (!Run(binary2text, "\"" + cab + "\" \"" + text + "\"", work, out error)) { result.Problems.Add(Path.GetFileName(bundleFile) + ": binary2text failed: " + error); return null; }
                objects.AddRange(BundleDump.Parse(File.ReadLines(text)));
                File.Delete(text);
            }
            Directory.Delete(work, true);
            if (objects.Count == 0) { result.Problems.Add(Path.GetFileName(bundleFile) + ": nothing could be read from the bundle."); return null; }
            states = ObjectCompare.FromDump(objects);
            return BundleDump.Count(objects);
        }

        // ------------------------------------------------------------------ before Promote to LIVE

        /// <summary>
        /// Read-only, so it can run in the Editor you work in: the audit record of this build says OK, and every audited
        /// bundle in the staged release is byte for byte the one that was audited (its fingerprint in the update list).
        /// Null when all is well, otherwise the reason Live must not get this build.
        /// </summary>
        public static string VerifyStaged(string releaseRoot, string storeManifestPath)
        {
            if (!File.Exists(storeManifestPath)) return "no staged release (" + storeManifestPath + ").";
            string manifest = File.ReadAllText(storeManifestPath);
            var build = System.Text.RegularExpressions.Regex.Match(manifest, "\"build\"\\s*:\\s*(\\d+)");
            if (!build.Success) return "the update list names no build number.";
            string recordPath = RecordPath(releaseRoot, int.Parse(build.Groups[1].Value));
            if (!File.Exists(recordPath)) return $"build {build.Groups[1].Value} was never audited (no {recordPath}). Publish it again.";
            Record record = JsonUtility.FromJson<Record>(File.ReadAllText(recordPath));
            if (record == null || !record.ok) return $"the audit of build {build.Groups[1].Value} did not pass: see the release log.";
            foreach (Bundle bundle in record.bundles)
            {
                var entry = System.Text.RegularExpressions.Regex.Match(manifest,
                    "\"path\"\\s*:\\s*\"[^\"]*" + System.Text.RegularExpressions.Regex.Escape(bundle.file) + "\"\\s*,\\s*\"size\"\\s*:\\s*(\\d+)\\s*,\\s*\"sha256\"\\s*:\\s*\"([0-9a-f]+)\"");
                if (!entry.Success) return $"{bundle.file} is not in the staged release.";
                if (!string.Equals(entry.Groups[2].Value, bundle.sha256, StringComparison.OrdinalIgnoreCase))
                    return $"{bundle.file} in the staged release is not the bundle that was audited (fingerprints differ).";
            }
            return null;
        }

        // ------------------------------------------------------------------ small helpers

        private static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }

        private static bool Run(string exe, string arguments, string folder, out string error)
        {
            try
            {
                var info = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder };
                using (Process process = Process.Start(info))
                {
                    var err = process.StandardError.ReadToEndAsync();
                    process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    error = err.Result;
                    return process.ExitCode == 0;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
