using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Batch entries for the dedicated servers. Linux (online Linux plan): <c>Unity -batchmode -nographics -quit -projectPath ... -executeMethod
    /// KryticalNova.EditorTools.ServerBuildBatch.BuildLinux [-il2cpp]</c>. Windows, the publish's server step (31 Oct 2026: built in its own
    /// checkout, <see cref="BackgroundPublisher.ServerCheckoutFolder"/>, opened with <c>-standaloneBuildSubtarget Server</c>):
    /// <c>-executeMethod KryticalNova.EditorTools.ServerBuildBatch.BuildWindows</c>, which also writes <see cref="ResultPath"/>.
    /// The same builds as the menu, with an exit code a script can read. Linux: <c>-il2cpp</c> or <c>-mono</c> sets the server's
    /// scripting backend to IL2CPP or Mono for this run (the project setting is not saved); neither, the project's Server
    /// backend. Windows: Mono, or IL2CPP with <c>-il2cpp</c>.
    /// </summary>
    public static class ServerBuildBatch
    {
        /// <summary>"OK" or "FAILED: why", for the script or Editor that started the Windows server build.</summary>
        public const string ResultPath = ReleasePublisher.Root + "/server-only.result";

        private static bool HasArg(string name) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), name) >= 0;

        public static void BuildLinux()
        {
            bool il2cpp = HasArg("-il2cpp");
            bool mono = HasArg("-mono");
            if (il2cpp) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            if (mono) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
            ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Server);
            string missing = ServerBuildBuilder.ModuleMissing(ServerBuildBuilder.Os.Linux, backend);
            if (missing != null) { Debug.LogError("[ServerBuildBatch] " + missing); EditorApplication.Exit(2); return; }
            bool ok = ServerBuildBuilder.Build(ServerBuildBuilder.Os.Linux, null, backend);
            Debug.Log("[ServerBuildBatch] Linux dedicated server build " + (ok ? "succeeded" : "FAILED") + " (IL2CPP: " + il2cpp + ", Mono: " + mono + ", built with " + backend + ").");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>The release's Windows server (the server step of a publish, or a rebuild of it alone).</summary>
        public static void BuildWindows()
        {
            int code = 1;
            string result = "FAILED: Unity stopped before the build ended";
            try
            {
                System.IO.Directory.CreateDirectory(ReleasePublisher.Root);
                if (System.IO.File.Exists(ResultPath)) System.IO.File.Delete(ResultPath);
                // The server checkout is opened with -standaloneBuildSubtarget Server. Should that be ignored, say so and set it
                // once here, so the build still compiles as a server and the checkout stays on Server from now on.
                if (EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Server)
                {
                    Debug.LogWarning("[ServerBuildBatch] This Unity opened on the " + EditorUserBuildSettings.standaloneBuildSubtarget +
                                     " subtarget, not Server (was -standaloneBuildSubtarget Server ignored?): switching to Server once.");
                    EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
                }
                string busy = ServerBuildBuilder.FolderBusy(ServerBuildBuilder.OutputFolder(ServerBuildBuilder.Os.Windows));
                if (busy != null) result = "FAILED: " + busy;
                else if (ServerBuildBuilder.Build(ServerBuildBuilder.Os.Windows, null, HasArg("-il2cpp") ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x))
                {
                    result = "OK";
                    code = 0;
                }
                else result = "FAILED: see " + ServerBuildBuilder.ReportPath(ServerBuildBuilder.Os.Windows);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                result = "FAILED: " + e.Message;
            }
            finally
            {
                System.IO.File.WriteAllText(ResultPath, result);
                System.IO.File.AppendAllText(ReleasePublisher.LogPath, (code == 0 ? "Server rebuilt for build " + ReleasePublisher.LastReleaseBuild() + "." : "The server did not build: " + result) + System.Environment.NewLine);
                Debug.Log("[ServerBuildBatch] Windows dedicated server: " + result);
                EditorApplication.Exit(code);
            }
        }

        /// <summary>
        /// The measuring tools' bot client (a development Windows PLAYER) built from a batch. The checkout's remembered standalone subtarget may still be
        /// Server (left by a server build), which would compile UNITY_SERVER code into the "client" and make it start a dedicated server on port 7777;
        /// so the subtarget is forced to Player for this run and put back afterwards. Exit code 0 only when a fresh KryticalNovaII.exe exists.
        /// </summary>
        public static void BuildBotClientPlayer()
        {
            StandaloneBuildSubtarget was = EditorUserBuildSettings.standaloneBuildSubtarget;
            string exe = MeasureTools.ClientFolder + "/KryticalNovaII.exe";
            System.DateTime started = System.DateTime.UtcNow;
            bool ok;
            try
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                Debug.Log("[ServerBuildBatch] Bot client: standalone subtarget forced to " + EditorUserBuildSettings.standaloneBuildSubtarget + " (was " + was + ").");
                MeasureTools.BuildBotClient();
                ok = System.IO.File.Exists(exe) && System.IO.File.GetLastWriteTimeUtc(exe) >= started;
            }
            finally
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = was;
            }
            Debug.Log("[ServerBuildBatch] Bot client build " + (ok ? "succeeded" : "FAILED") + ".");
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
