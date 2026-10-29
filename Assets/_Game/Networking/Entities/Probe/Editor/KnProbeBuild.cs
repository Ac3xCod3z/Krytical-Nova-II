using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Networking.Entities.Probe
{
    /// <summary>
    /// Builds the two minimal Windows players of the M2c two-client test (plan: Netcode for Entities migration, "M2c material"):
    /// a dedicated-server player (Server subtarget, <c>UNITY_SERVER</c>) and a normal player, both Mono x64, development builds with
    /// the define <c>KN_PROBE</c>, each with ONE empty generated scene (the probe starts itself from the command line, see
    /// <see cref="KnProbeEntry"/>). Output: <c>&lt;project&gt;/Builds/KnProbeServer/KnProbeServer.exe</c> and
    /// <c>&lt;project&gt;/Builds/KnProbeClient/KnProbeClient.exe</c> (Builds/ is git-ignored). Batch:
    /// <c>Unity -batchmode -quit -projectPath &lt;clone&gt; -executeMethod KryticalNova.Networking.Entities.Probe.KnProbeBuild.BuildServer -logFile ...</c>
    /// (then <c>.BuildClient</c>; Unity exits 0 on success, 1 on failure). It changes no project setting for good: the scripting
    /// backend (only if it is not Mono), the Addressables "build with player" option, the build target and the subtarget are put
    /// back, and the generated scene is deleted again. It does not touch <c>BuildSceneOrder</c>, the scenes list or the NGO builders.
    /// <para>Baking: the project has no Entities Graphics package (no <c>Unity.Rendering.Hybrid</c>), and the probe scene has no
    /// SubScene, so no baking runs; if Entities Graphics is ever added, list it under Project Settings, Entities, Baking, Excluded
    /// Baking System Assemblies for these builds.</para>
    /// </summary>
    public static class KnProbeBuild
    {
        public const string ServerFolder = "Builds/KnProbeServer";
        public const string ClientFolder = "Builds/KnProbeClient";
        public const string ServerExe = "KnProbeServer.exe";
        public const string ClientExe = "KnProbeClient.exe";
        public const string SceneName = "KnProbeEmpty";
        private const string GeneratedFolder = "Assets/_Game/Networking/Entities/Probe/Generated";
        private const string ScenePath = GeneratedFolder + "/" + SceneName + ".unity";
        private const string ServerModulePrefix = "win64_server_development_mono";
        private const string PlayerModulePrefix = "win64_player_development_mono";

        [MenuItem("Krytical Nova/Online/DOTS Probe/Build Probe Server")]
        public static void BuildServerMenu() => Build(server: true);

        [MenuItem("Krytical Nova/Online/DOTS Probe/Build Probe Client")]
        public static void BuildClientMenu() => Build(server: false);

        /// <summary>Batch entry: <c>-executeMethod KryticalNova.Networking.Entities.Probe.KnProbeBuild.BuildServer</c>.</summary>
        public static void BuildServer() => Finish(Build(server: true));

        /// <summary>Batch entry: <c>-executeMethod KryticalNova.Networking.Entities.Probe.KnProbeBuild.BuildClient</c>.</summary>
        public static void BuildClient() => Finish(Build(server: false));

        private static void Finish(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string OutputFolder(bool server) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", server ? ServerFolder : ClientFolder));

        /// <summary>What Unity lacks for this build, in words; null when the module is there.</summary>
        public static string ModuleMissing(bool server)
        {
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "windowsstandalonesupport", "Variations");
            string prefix = server ? ServerModulePrefix : PlayerModulePrefix;
            bool there = Directory.Exists(Path.Combine(variations, prefix));
            return there ? null : $"The Windows {(server ? "Dedicated Server" : "Mono player")} development variation ({prefix}) is not installed for {Application.unityVersion}.";
        }

        public static bool Build(bool server)
        {
            string what = server ? "server" : "client";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogError($"[KnProbeBuild] Not now: Unity is in Play mode or compiling. Stop Play and build the probe {what} again.");
                return false;
            }
            string missing = ModuleMissing(server);
            if (missing != null)
            {
                Debug.LogError("[KnProbeBuild] " + missing);
                return false;
            }
            string folder = OutputFolder(server);
            string exe = Path.Combine(folder, server ? ServerExe : ClientExe);
            if (Directory.Exists(folder))
            {
                try { Directory.Delete(folder, true); }
                catch (Exception e)
                {
                    Debug.LogError($"[KnProbeBuild] {folder} could not be cleared ({e.Message}). A probe {what} may still be running: stop it first.");
                    return false;
                }
            }
            Directory.CreateDirectory(folder);

            NamedBuildTarget named = server ? NamedBuildTarget.Server : NamedBuildTarget.Standalone;
            BuildTarget targetWas = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget subtargetWas = EditorUserBuildSettings.standaloneBuildSubtarget;
            ScriptingImplementation backendWas = PlayerSettings.GetScriptingBackend(named);
            bool backendChanged = false;
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var bundlesWere = addressables != null ? addressables.BuildAddressablesWithPlayerBuild : default;
            var stopwatch = Stopwatch.StartNew();
            BuildReport report = null;
            bool sceneMade = false;
            try
            {
                MakeScene();
                sceneMade = true;
                if (backendWas != ScriptingImplementation.Mono2x)
                {
                    PlayerSettings.SetScriptingBackend(named, ScriptingImplementation.Mono2x);
                    backendChanged = true;
                }
                // The maps' bundles have nothing to do with a probe (and take minutes).
                if (addressables != null)
                    addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;

                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = exe,
                    targetGroup = BuildTargetGroup.Standalone,
                    target = BuildTarget.StandaloneWindows64,
                    subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                    options = BuildOptions.Development,
                    extraScriptingDefines = new[] { "KN_PROBE" },
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnProbeBuild] The {what} build threw {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                if (backendChanged) PlayerSettings.SetScriptingBackend(named, backendWas);
                if (addressables != null) addressables.BuildAddressablesWithPlayerBuild = bundlesWere;
                if (sceneMade) DeleteScene();
                if (EditorUserBuildSettings.activeBuildTarget != targetWas || EditorUserBuildSettings.standaloneBuildSubtarget != subtargetWas)
                {
                    EditorUserBuildSettings.standaloneBuildSubtarget = subtargetWas;
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, targetWas);
                }
            }

            bool ok = report != null && report.summary.result == BuildResult.Succeeded;
            string backend = PlayerSettings.GetScriptingBackend(named) == ScriptingImplementation.Mono2x ? "Mono" : PlayerSettings.GetScriptingBackend(named).ToString();
            string strip = PlayerSettings.GetManagedStrippingLevel(named).ToString();
            if (ok)
                Debug.Log($"[KnProbeBuild] Probe {what} OK: {exe} ({report.summary.totalSize / (1024.0 * 1024.0):F0} MB, {stopwatch.Elapsed.TotalSeconds:F0} s, " +
                          $"project backend {backend} (build used Mono), stripping {strip} + link.xml, development, KN_PROBE).");
            else
                Debug.LogError($"[KnProbeBuild] Probe {what} FAILED: {(report != null ? report.summary.result + ", " + report.summary.totalErrors + " errors" : "no report")}. See the Editor log.");
            return ok;
        }

        // One empty scene, written under the Probe folder and deleted again after the build. It is added to the Editor's open scenes
        // only for a moment (additive), so the scene the owner has open is never replaced.
        private static void MakeScene()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets/_Game/Networking/Entities/Probe", "Generated");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("the empty probe scene could not be saved at " + ScenePath);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
            AssetDatabase.Refresh();
        }

        private static void DeleteScene()
        {
            AssetDatabase.DeleteAsset(ScenePath);
            AssetDatabase.DeleteAsset(GeneratedFolder);
        }
    }
}
