using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// A game (client) build is always a Player build. The Editor remembers the standalone subtarget per checkout, and a
    /// killed or failed server build leaves it on Server: client code then compiles with UNITY_SERVER, and map bundles built
    /// meanwhile can carry server-only state (31 Oct 2026: ten of build 13's 29 maps shipped with the sky presenter off).
    /// <see cref="Prepare"/> runs before any client content or player build: it forces Player, asserts it, and clears the
    /// content cache (Library/BuildCache and the Addressables output) when bundles may have been made under other settings.
    /// A cleared cache costs one full rebuild of the maps; the same settings as last time keep the cache.
    /// <para>
    /// Setting the subtarget does not recompile the Editor's own scripts in the process that is running: while the Server
    /// subtarget was active they were compiled with UNITY_SERVER, and the [ExecuteAlways] and edit-mode code that builds
    /// the map bundles would still run as a server. So when the subtarget had to be corrected (or this Editor's scripts
    /// are a server's), the build STOPS and has to be started again in a freshly compiled Editor:
    /// the background publish does that by itself (BackgroundPublisher.RunInBatch), a batch script runs
    /// <see cref="PrepareOnly"/> first, and in the Editor window the scripts are reloaded and the build is run again.
    /// </para>
    /// </summary>
    public static class ClientBuildGuard
    {
        /// <summary>How the maps' content was last built here (Library is per checkout and never committed).</summary>
        public const string StampPath = "Library/KryticalNova-ContentBuild.stamp";
        private const string BuildCachePath = "Library/BuildCache";

        /// <summary>The exit code of a batch build that stopped because the Editor has to be started again.</summary>
        public const int RestartExitCode = 4;

        /// <summary>True when this Editor's own scripts were compiled with UNITY_SERVER (the Server subtarget was active at compile time).</summary>
        public static bool EditorCompiledAsServer
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>The settings a bundle depends on that its source files do not show.</summary>
        public static string Stamp(StandaloneBuildSubtarget subtarget, string defines, string unityVersion) =>
            $"subtarget={subtarget};unity={unityVersion};defines={defines}";

        /// <summary>No stamp (never built under this guard), another stamp, or a subtarget that had to be corrected: the cache may be tainted.</summary>
        public static bool NeedsPurge(string lastStamp, string currentStamp, bool subtargetWasWrong) =>
            subtargetWasWrong || string.IsNullOrEmpty(lastStamp) || lastStamp != currentStamp;

        /// <summary>The Editor's scripts are not the game's: the subtarget had to be corrected just now, or they were compiled as a server's.</summary>
        public static bool NeedsRestart(bool subtargetWasWrong, bool editorCompiledAsServer) => subtargetWasWrong || editorCompiledAsServer;

        /// <summary>
        /// Forces Player on Windows and asserts it (throws when it cannot). True when the Editor has to be started again
        /// before a client build (see the class text); the content cache is then marked for clearing, because bundles made under
        /// the wrong scripts may be in it.
        /// </summary>
        public static bool EnsurePlayer(string what)
        {
            bool wrong = EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player;
            if (wrong)
            {
                Debug.LogWarning($"[Build] {what}: the standalone subtarget was {EditorUserBuildSettings.standaloneBuildSubtarget} (a server build left it); forcing Player.");
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.LogWarning($"[Build] {what}: the active build target was {EditorUserBuildSettings.activeBuildTarget}; switching to StandaloneWindows64.");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }
            if (EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException($"[Build] {what} needs a Windows Player build, but the Editor is on {EditorUserBuildSettings.activeBuildTarget} / {EditorUserBuildSettings.standaloneBuildSubtarget}.");

            bool restart = NeedsRestart(wrong, EditorCompiledAsServer);
            // No record means the next build clears the content cache, whatever settings it then runs on.
            if (restart && File.Exists(StampPath)) File.Delete(StampPath);
            return restart;
        }

        /// <summary>
        /// Before any client content or player build: Player is forced and asserted, and the content cache is cleared when it
        /// may be tainted. Throws (and, in a batch run, ends it with <see cref="RestartExitCode"/>) when the Editor has to be
        /// started again first; in the Editor window it reloads the scripts and asks for the build to be run again.
        /// </summary>
        public static void Prepare(string what)
        {
            if (EnsurePlayer(what))
            {
                string message = $"[Build] {what}: the standalone subtarget was corrected, or this Editor's scripts were compiled as a server's. " +
                                 "The Player subtarget is set now, but the Editor must be started again so its scripts compile as the game's; " +
                                 "nothing was built.";
                if (Application.isBatchMode)
                {
                    Debug.LogError(message + " Start the build again (exit code " + RestartExitCode + ").");
                    EditorApplication.Exit(RestartExitCode);
                }
                else
                {
                    EditorUtility.RequestScriptReload();
                    Debug.LogError(message + " The scripts are reloading: run the build again.");
                }
                throw new InvalidOperationException(message);
            }

            string current = CurrentStamp();
            string last = File.Exists(StampPath) ? File.ReadAllText(StampPath).Trim() : "";
            if (!NeedsPurge(last, current, false))
            {
                Debug.Log($"[Build] {what}: Player subtarget confirmed, content cache kept ({current}).");
                return;
            }
            Debug.Log($"[Build] {what}: clearing the content cache (was: {(last.Length == 0 ? "no record" : last)}; now: {current}).");
            // The Scriptable Build Pipeline's cache (what the bundle builder reuses by content hash) is a plain folder.
            if (Directory.Exists(BuildCachePath)) Directory.Delete(BuildCachePath, true);
            AddressableAssetSettings.CleanPlayerContent();
            // The cache is clean, but the stamp is written only when the build succeeds (RecordBuilt).
            if (File.Exists(StampPath)) File.Delete(StampPath);
        }

        /// <summary>
        /// -executeMethod for a batch script: sets Player and ends, so the Editor that then does the real build starts with
        /// scripts compiled as the game's. Exit code 0 when the Editor is ready, 1 when Player could not be set.
        /// </summary>
        public static void PrepareOnly()
        {
            int code = 0;
            try
            {
                EnsurePlayer("Prepare only");
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            EditorApplication.Exit(code);
        }

        /// <summary>Called after a client build succeeded: the next one with the same settings keeps the cache.</summary>
        public static void RecordBuilt() => File.WriteAllText(StampPath, CurrentStamp());

        private static string CurrentStamp() =>
            Stamp(EditorUserBuildSettings.standaloneBuildSubtarget, PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone), Application.unityVersion);
    }
}
