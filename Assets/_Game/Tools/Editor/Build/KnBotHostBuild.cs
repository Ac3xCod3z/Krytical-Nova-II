using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The soak's bot host (wave 4, 12 Nov 2026): a Windows headless player (Player subtarget, run with <c>-batchmode -nographics
    /// -knbots &lt;n&gt;</c>) that holds many thin-client bots in one process (<c>KnBotHost</c>). Defines <c>KN_TEST_LANE</c> +
    /// <c>KN_DEV_BOTS</c>, given to this build only (never written into the player settings). Burst AOT on. Mono by default for the
    /// tool shake-out (lead review change 5); <see cref="BuildIl2cpp"/> for the numbers that go on record. One map plus the ghost bake
    /// scene, no Addressable bundles. Output <c>Builds/Bots/EntityBotHost/KnBotHost.exe</c> (git-ignored). A player build is a heavy
    /// job (15-19 GB, 10-45 min): never beside a soak, a Blender job or another build. The Editor's build target, subtarget, backend,
    /// Addressables option and Burst settings file are put back.
    /// Batch: <c>-executeMethod KryticalNova.EditorTools.KnBotHostBuild.Build</c> (exits 0 when built, 1 when not).
    /// </summary>
    public static class KnBotHostBuild
    {
        public const string Folder = "Builds/Bots/EntityBotHost";
        public const string Exe = "KnBotHost.exe";
        public static readonly string[] Defines = { "KN_TEST_LANE", "KN_DEV_BOTS" };

        [MenuItem("Krytical Nova/Online/Entities/Soak/1 Build Bot Host", priority = 1)]
        static void BuildMenu() => Run(ScriptingImplementation.Mono2x);

        public static void Build() => Finish(Run(ScriptingImplementation.Mono2x));

        public static void BuildIl2cpp() => Finish(Run(ScriptingImplementation.IL2CPP));

        static void Finish(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string OutputFolder() =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", Folder));

        public static bool Run(ScriptingImplementation backend)
        {
            const string what = "Windows bot host";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogError($"[KnBotHostBuild] Not now: Unity is in Play mode or compiling. Build the {what} again afterwards.");
                return false;
            }
            string[] scenes = { KnLinuxTestBuild.MapScenePath, KnLinuxTestBuild.GhostBakeScenePath };
            string problem = AssetDatabase.LoadMainAssetAtPath(KnLinuxTestBuild.WorldAssetsPath) == null ? KnLinuxTestBuild.WorldAssetsPath + " is missing (Build Player Ghost Assets first)." : null;
            foreach (string scene in scenes)
                if (problem == null && AssetDatabase.LoadMainAssetAtPath(scene) == null) problem = scene + " is missing.";
            if (problem != null) { Debug.LogError("[KnBotHostBuild] " + problem); return false; }

            string folder = OutputFolder();
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnBotHostBuild] {folder} could not be cleared ({e.Message}; is a bot host still running?).");
                return false;
            }

            BuildTarget targetWas = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget subtargetWas = EditorUserBuildSettings.standaloneBuildSubtarget;
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var bundlesWere = addressables != null ? addressables.BuildAddressablesWithPlayerBuild : default;
            var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
            ScriptingImplementation backendWas = PlayerSettings.GetScriptingBackend(standalone);
            var stopwatch = Stopwatch.StartNew();
            BuildReport report = null;
            string burstPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", KnWindowsTestBuild.BurstSettingsPath);
            string burstWas = File.Exists(burstPath) ? File.ReadAllText(burstPath) : null;
            try
            {
                File.WriteAllText(burstPath, KnWindowsTestBuild.BurstOnJson);
                BurstAotMarker.ClearStaleMarker();   // a Burst-off build's leftover stops Burst for good (8 Nov 2026)
                if (addressables != null)
                    addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                if (backendWas != backend) PlayerSettings.SetScriptingBackend(standalone, backend);
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;   // Burst reads the Editor's subtarget
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(folder, Exe),
                    targetGroup = BuildTargetGroup.Standalone,
                    target = BuildTarget.StandaloneWindows64,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = BuildOptions.None,
                    extraScriptingDefines = Defines,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnBotHostBuild] The {what} build threw {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                try
                {
                    if (burstWas != null) File.WriteAllText(burstPath, burstWas);
                    else if (File.Exists(burstPath)) File.Delete(burstPath);
                }
                catch (Exception e) { Debug.LogError($"[KnBotHostBuild] Could not put back {KnWindowsTestBuild.BurstSettingsPath} ({e.Message}): remove it by hand."); }
                if (PlayerSettings.GetScriptingBackend(standalone) != backendWas) PlayerSettings.SetScriptingBackend(standalone, backendWas);
                if (addressables != null) addressables.BuildAddressablesWithPlayerBuild = bundlesWere;
                if (EditorUserBuildSettings.activeBuildTarget != targetWas || EditorUserBuildSettings.standaloneBuildSubtarget != subtargetWas)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = subtargetWas;
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, targetWas);
                }
            }

            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            if (ok && (backend == ScriptingImplementation.IL2CPP) != File.Exists(Path.Combine(folder, "GameAssembly.dll")))
            {
                Debug.LogError($"[KnBotHostBuild] {what} FAILED: the build came out with the other scripting backend.");
                return false;
            }
            if (ok)
                Debug.Log($"[KnBotHostBuild] {what} OK: {Path.Combine(folder, Exe)} ({stopwatch.Elapsed.TotalMinutes:F1} min, {backend}, Burst on, {string.Join(" ", Defines)}).");
            else
                Debug.LogError($"[KnBotHostBuild] {what} FAILED: {(report != null ? report.summary.result + ", " + report.summary.totalErrors + " errors" : "no report")}. See the Editor log.");
            return ok;
        }
    }
}
