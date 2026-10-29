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
    }
}
