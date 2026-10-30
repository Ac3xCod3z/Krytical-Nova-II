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
    /// The two Linux players of the entity netcode's 2-client gate (DOTS plan section 43, "B3 by the Linux route"; Norton's
    /// Behavioral Protection freezes every fresh Windows exe, so both run in WSL): a dedicated server (Server subtarget,
    /// <c>UNITY_SERVER</c>) and a headless client (Player subtarget, run with <c>-batchmode -nographics</c>), Mono x64, development
    /// builds, one map (<see cref="MapScenePath"/>), no Addressable bundles, no shaders (<see cref="KnLinuxTestShaderStrip"/>). Defines:
    /// <c>KN_ENTITIES_SERVER</c> (server: the Netcode for GameObjects server parts stay off), <c>KN_TEST_LANE</c> (refuses the real
    /// server's ports), <c>KN_DEV_BOTS</c> (bot tokens instead of a Unity sign-in). Output <c>Builds/KnGameServerLinux</c> and
    /// <c>Builds/KnGameClientLinux</c> (git-ignored). The Editor's build target, subtarget and Addressables option are put back.
    /// A full-game build: one heavy job (up to 40 minutes, about 17 GB), never beside another Unity.
    /// Batch: <c>-executeMethod KryticalNova.EditorTools.KnLinuxTestBuild.BuildServer</c> (then <c>.BuildClient</c>).
    /// </summary>
    public static class KnLinuxTestBuild
    {
        public const string ServerFolder = "Builds/KnGameServerLinux";
        public const string ClientFolder = "Builds/KnGameClientLinux";
        public const string ClientIl2cppFolder = "Builds/KnGameClientLinuxIl2cpp";
        public const string ServerExe = "KnGameServer.x86_64";
        public const string ClientExe = "KnGameClient.x86_64";
        /// <summary>The one map both test players carry (the server's default map; the bots load it by name).</summary>
        public const string MapScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";

        /// <summary>Made by Build Player Ghost Assets; without it every admission is refused "not ready" (R8).</summary>
        public const string WorldAssetsPath = "Assets/_Game/Gameplay/Player/Entities/Resources/PlayerWorldAssets.asset";

        /// <summary>True while one of these builds runs (<see cref="KnLinuxTestShaderStrip"/> strips every shader then: both players are headless).</summary>
        public static bool IsBuilding { get; private set; }

        public static readonly string[] ServerDefines = { "KN_ENTITIES_SERVER", "KN_TEST_LANE", "KN_DEV_BOTS" };
        public static readonly string[] ClientDefines = { "KN_TEST_LANE", "KN_DEV_BOTS" };

        [MenuItem("Krytical Nova/Online/Entities/Build Linux Test Server (B3)")]
        public static void BuildServerMenu() => Build(server: true);

        [MenuItem("Krytical Nova/Online/Entities/Build Linux Headless Client (B3)")]
        public static void BuildClientMenu() => Build(server: false);

        public static void BuildServer() => Finish(Build(server: true));

        public static void BuildClient() => Finish(Build(server: false));

        /// <summary>The headless client with IL2CPP instead of Mono (for an Editor without the Linux Mono player variation).</summary>
        public static void BuildClientIl2cpp() => Finish(Build(server: false, il2cpp: true));

        static void Finish(bool ok)
        {
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string OutputFolder(bool server, bool il2cpp = false) =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", server ? ServerFolder : il2cpp ? ClientIl2cppFolder : ClientFolder));

        /// <summary>What Unity lacks for this build, in words; null when the module is there.</summary>
        public static string ModuleMissing(bool server, bool il2cpp = false)
        {
            if (server) return ServerBuildBuilder.ModuleMissing(ServerBuildBuilder.Os.Linux);
            string backend = il2cpp ? "il2cpp" : "mono";
            string variations = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "LinuxStandaloneSupport", "Variations");
            bool there = Directory.Exists(variations) && Directory.GetDirectories(variations, "linux64_player_development_" + backend).Length > 0;
            return there ? null : $"The Linux {backend} player development variation is not installed for {Application.unityVersion} (Unity Hub > Add modules > Linux Build Support ({(il2cpp ? "IL2CPP" : "Mono")})).";
        }

        public static bool Build(bool server, bool il2cpp = false)
        {
            string what = server ? "Linux test server" : il2cpp ? "Linux headless client (IL2CPP)" : "Linux headless client";
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogError($"[KnLinuxTestBuild] Not now: Unity is in Play mode or compiling. Build the {what} again afterwards.");
                return false;
            }
            string problem = ModuleMissing(server, il2cpp)
                ?? (AssetDatabase.LoadMainAssetAtPath(WorldAssetsPath) == null ? $"{WorldAssetsPath} is missing: run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets first." : null);
            // Only the gate's map: with every map in the list the build's process tree passed 21-23 GB and the watchdog stopped it twice.
            string[] scenes = { MapScenePath };
            if (problem == null && AssetDatabase.LoadMainAssetAtPath(MapScenePath) == null) problem = MapScenePath + " is missing";
            if (problem != null)
            {
                Debug.LogError("[KnLinuxTestBuild] " + problem);
                return false;
            }

            string folder = OutputFolder(server, il2cpp);
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnLinuxTestBuild] {folder} could not be cleared ({e.Message}).");
                return false;
            }

            BuildTarget targetWas = EditorUserBuildSettings.activeBuildTarget;
            StandaloneBuildSubtarget subtargetWas = EditorUserBuildSettings.standaloneBuildSubtarget;
            var addressables = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var bundlesWere = addressables != null ? addressables.BuildAddressablesWithPlayerBuild : default;
            var standalone = UnityEditor.Build.NamedBuildTarget.Standalone;
            ScriptingImplementation backendWas = PlayerSettings.GetScriptingBackend(standalone);
            ScriptingImplementation backend = il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x;
            var stopwatch = Stopwatch.StartNew();
            BuildReport report = null;
            try
            {
                // the maps are in the build list (as on the NGO server); the game's Addressable bundles are not needed here
                if (addressables != null)
                    addressables.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
                if (backendWas != backend) PlayerSettings.SetScriptingBackend(standalone, backend);
                IsBuilding = true;
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(folder, server ? ServerExe : ClientExe),
                    targetGroup = BuildTargetGroup.Standalone,
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)(server ? StandaloneBuildSubtarget.Server : StandaloneBuildSubtarget.Player),
                    options = BuildOptions.Development,
                    extraScriptingDefines = server ? ServerDefines : ClientDefines,
                });
            }
            catch (Exception e)
            {
                Debug.LogError($"[KnLinuxTestBuild] The {what} build threw {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                IsBuilding = false;
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
                Debug.Log($"[KnLinuxTestBuild] {what} OK: {folder} ({report.summary.totalSize / (1024.0 * 1024.0):F0} MB, {stopwatch.Elapsed.TotalMinutes:F1} min, " +
                          $"{(il2cpp ? "IL2CPP" : "Mono")}, development, {string.Join(" ", server ? ServerDefines : ClientDefines)}).");
            else
                Debug.LogError($"[KnLinuxTestBuild] {what} FAILED: {(report != null ? report.summary.result + ", " + report.summary.totalErrors + " errors" : "no report")}. See the Editor log.");
            return ok;
        }
    }
}
