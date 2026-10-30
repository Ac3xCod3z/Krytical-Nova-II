using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Batch entry for the Linux dedicated server (online Linux plan): <c>Unity -batchmode -nographics -quit -projectPath ... -executeMethod
    /// KryticalNova.Tools.Editor.Build.ServerBuildBatch.BuildLinux [-il2cpp]</c>. The same build as the menu, with the exit code a script can read.
    /// <c>-il2cpp</c> sets the server's scripting backend to IL2CPP for this run only (the project setting is not saved).
    /// </summary>
    public static class ServerBuildBatch
    {
        public static void BuildLinux()
        {
            bool il2cpp = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-il2cpp") >= 0;
            bool mono = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mono") >= 0;
            if (il2cpp) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.IL2CPP);
            if (mono) PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, ScriptingImplementation.Mono2x);
            string missing = ServerBuildBuilder.ModuleMissing(ServerBuildBuilder.Os.Linux);
            if (missing != null) { Debug.LogError("[ServerBuildBatch] " + missing); EditorApplication.Exit(2); return; }
            bool ok = ServerBuildBuilder.Build(ServerBuildBuilder.Os.Linux);
            Debug.Log("[ServerBuildBatch] Linux dedicated server build " + (ok ? "succeeded" : "FAILED") + " (IL2CPP: " + il2cpp + ", Mono: " + mono + ").");
            EditorApplication.Exit(ok ? 0 : 1);
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
