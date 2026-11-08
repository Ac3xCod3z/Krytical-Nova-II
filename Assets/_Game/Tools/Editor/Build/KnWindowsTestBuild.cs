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
    /// The Windows test player that joins the Editor's "Play DOTS here" as a second, clickable player (<c>KnJoinHere</c>, started
    /// with <c>-knjoinhere</c>). Player subtarget, Mono x64, Burst AOT off unless KN_WINJOIN_BURST=1 (owner 6 Nov 2026: its compile pushed the build past the RAM
    /// watchdog; the jobs run managed, fine for a two-player loopback test), not a development build (nothing in the join needs one), one
    /// map (<see cref="KnLinuxTestBuild.MapScenePath"/>) plus the scene that bakes the ghost subscene, no Addressable bundles, shaders
    /// kept except the variants none of its materials can draw (<see cref="KnWindowsTestShaderStrip"/>, this build only). Define
    /// <c>KN_JOIN_HERE</c> for this build only (given to the build, never written into the player settings), so no release client or
    /// server ever has the join. Output <c>Builds/WindowsTest/KnWindowsTest.exe</c> (git-ignored). The Editor's build target,
    /// subtarget, scripting backend and Addressables option are put back.
    /// Batch: <c>-executeMethod KryticalNova.EditorTools.KnWindowsTestBuild.BuildClient</c> (exits 0 when built, 1 when not).
    /// </summary>
    public static class KnWindowsTestBuild
    {
        public const string Folder = "Builds/WindowsTest";
        public const string Exe = "KnWindowsTest.exe";

        /// <summary>
        /// Burst's own per-platform settings file, written with Burst off for the build only and taken away afterwards (or what was
        /// there put back), as <see cref="KnLinuxTestBuild"/> does for Linux: without one Burst's player step once stopped compiling.
        /// </summary>
        public const string BurstSettingsPath = "ProjectSettings/BurstAotSettings_StandaloneWindows.json";
        const string BurstOffJson = "{\n    \"MonoBehaviour\": {\n        \"Version\": 4,\n        \"EnableBurstCompilation\": false\n    }\n}\n";
        internal const string BurstOnJson = "{\n    \"MonoBehaviour\": {\n        \"Version\": 4,\n        \"EnableBurstCompilation\": true,\n        \"EnableOptimisations\": true,\n        \"EnableSafetyChecks\": false,\n        \"EnableDebugInAllBuilds\": false,\n        \"CpuMinTargetX32\": 0,\n        \"CpuMaxTargetX32\": 0,\n        \"CpuMinTargetX64\": 0,\n        \"CpuMaxTargetX64\": 0,\n        \"OptimizeFor\": 0\n    }\n}\n";

        /// <summary>
        /// <c>KN_WINJOIN_BURST=1</c>: this client with Burst AOT on (owner, 6 Nov 2026: a Linux player without Burst crashed in Netcode's
        /// GhostSpawnSystem, plan section 48, so the 2-player check uses a Burst build once the PC has the memory for it).
        /// </summary>
        static bool BurstOn => Environment.GetEnvironmentVariable("KN_WINJOIN_BURST") == "1";

        public static readonly string[] Defines = { "KN_JOIN_HERE" };

        /// <summary>True while this build runs: <see cref="KnWindowsTestShaderStrip"/> strips only then.</summary>
        public static bool IsBuilding { get; private set; }

        [MenuItem("Krytical Nova/Online/Entities/Build Windows Join-Here Client")]
        public static void BuildClientMenu() => Build();

        public static void BuildClient() => Finish(Build());

        static void Finish(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string OutputFolder() =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", Folder));

        /// <summary>Where the build keeps the baked ghost subscene's header (Entities: StreamingAssets/EntityScenes/&lt;scene GUID&gt;.entityheader).</summary>
        public static string GhostHeaderPath(string folder) =>
            Path.Combine(folder, Path.GetFileNameWithoutExtension(Exe) + "_Data", "StreamingAssets", "EntityScenes",
                         AssetDatabase.AssetPathToGUID(KnLinuxTestBuild.GhostSubScenePath) + ".entityheader");

        /// <summary>What Unity lacks for this build, in words; null when the module is there.</summary>
        public static string ModuleMissing()
        {
            const string variation = "win64_player_nondevelopment_mono";
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            bool there = Directory.Exists(variations) && Directory.GetDirectories(variations, variation).Length > 0;
            return there ? null : $"The Windows {variation} variation is not installed for {Application.unityVersion} (Unity Hub > Add modules > Windows Build Support (Mono)).";
        }

        public static bool Build()
        {
            const string what = "Windows join-here client";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogError($"[KnWindowsTestBuild] Not now: Unity is in Play mode or compiling. Build the {what} again afterwards.");
                return false;
            }
            string problem = ModuleMissing()
                ?? (AssetDatabase.LoadMainAssetAtPath(KnLinuxTestBuild.WorldAssetsPath) == null ? $"{KnLinuxTestBuild.WorldAssetsPath} is missing: run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets first." : null);
            // The bake scene is listed (never loaded) so the ghost subscene is baked into the build; the map is first, so it boots there.
            string[] scenes = { KnLinuxTestBuild.MapScenePath, KnLinuxTestBuild.GhostBakeScenePath };
            foreach (string scene in scenes)
                if (problem == null && AssetDatabase.LoadMainAssetAtPath(scene) == null)
                    problem = scene + " is missing" + (scene == KnLinuxTestBuild.GhostBakeScenePath ? ": run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets first." : "");
            if (problem != null)
            {
                Debug.LogError("[KnWindowsTestBuild] " + problem);
                return false;
            }

            string folder = OutputFolder();
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnWindowsTestBuild] {folder} could not be cleared ({e.Message}; is the test client still running?).");
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
            string burstPath = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", BurstSettingsPath);
            string burstWas = File.Exists(burstPath) ? File.ReadAllText(burstPath) : null;
            try
            {
                File.WriteAllText(burstPath, BurstOn ? BurstOnJson : BurstOffJson);
                if (BurstOn) BurstAotMarker.ClearStaleMarker();   // a Burst-off build's leftover stops Burst for good (8 Nov 2026)
                // the map is in the build list; the game's Addressable bundles are not needed here
                if (addressables != null)
                    addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                if (backendWas != ScriptingImplementation.Mono2x) PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
                // Burst reads the Editor's own subtarget, not BuildPlayerOptions.subtarget (KnLinuxTestBuild, 30 Oct 2026); put back in finally
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                KnWindowsTestShaderStrip.Prepare(new[] { KnLinuxTestBuild.MapScenePath, KnLinuxTestBuild.GhostBakeScenePath,
                                                         KnLinuxTestBuild.GhostSubScenePath, KnLinuxTestBuild.WorldAssetsPath });
                IsBuilding = true;
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
                Debug.LogError($"[KnWindowsTestBuild] The {what} build threw {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                IsBuilding = false;
                try
                {
                    if (burstWas != null) File.WriteAllText(burstPath, burstWas);
                    else if (File.Exists(burstPath)) File.Delete(burstPath);
                }
                catch (Exception e) { Debug.LogError($"[KnWindowsTestBuild] Could not put back {BurstSettingsPath} ({e.Message}): remove it by hand."); }
                if (PlayerSettings.GetScriptingBackend(standalone) != backendWas) PlayerSettings.SetScriptingBackend(standalone, backendWas);
                if (addressables != null) addressables.BuildAddressablesWithPlayerBuild = bundlesWere;
                if (EditorUserBuildSettings.activeBuildTarget != targetWas || EditorUserBuildSettings.standaloneBuildSubtarget != subtargetWas)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = subtargetWas;
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, targetWas);
                }
            }

            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            if (ok)
            {
                string data = Path.Combine(folder, Path.GetFileNameWithoutExtension(Exe) + "_Data", "Plugins");
                bool hasBurst = File.Exists(Path.Combine(data, "x86_64", "lib_burst_generated.dll")) || File.Exists(Path.Combine(data, "lib_burst_generated.dll"));
                Debug.Log("[KnWindowsTestBuild] Burst AOT " + (hasBurst ? "built" : "off") + (hasBurst == BurstOn ? " (as asked)" : " (NOT what was asked)"));
            }
            string header = GhostHeaderPath(folder);
            if (ok && !File.Exists(header))
            {
                Debug.LogError($"[KnWindowsTestBuild] {what} FAILED: the player ghost subscene was not baked into the build ({header} is missing): nobody could enter.");
                return false;
            }
            if (ok && File.Exists(Path.Combine(folder, "GameAssembly.dll")))
            {
                Debug.LogError($"[KnWindowsTestBuild] {what} FAILED: the build came out IL2CPP, not Mono.");
                return false;
            }
            if (ok)
                Debug.Log($"[KnWindowsTestBuild] {what} OK: {Path.Combine(folder, Exe)} ({report.summary.totalSize / (1024.0 * 1024.0):F0} MB, {stopwatch.Elapsed.TotalMinutes:F1} min, " +
                          $"Mono, Burst {(BurstOn ? "on" : "off")}, not development, {string.Join(" ", Defines)}). Start Play DOTS here in the Editor, then run it with -knjoinhere.");
            else
                Debug.LogError($"[KnWindowsTestBuild] {what} FAILED: {(report != null ? report.summary.result + ", " + report.summary.totalErrors + " errors" : "no report")}. See the Editor log.");
            return ok;
        }
    }
}
