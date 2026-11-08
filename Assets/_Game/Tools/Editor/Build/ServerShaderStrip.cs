using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Keeps shader compiling out of the LINUX dedicated server build (online Linux plan). "Enable Dedicated Server Optimizations" does not apply
    /// when a build goes through the Scriptable Build Pipeline (Addressables, Entities subscenes; Unity staff, Oct 2025), so the server build still
    /// compiled every shader and compute shader (Sentis/Inference Engine included) and needed 13+ GB of RAM. A headless server draws nothing.
    ///
    /// Active only while <see cref="ServerBuildBuilder"/> builds the Dedicated Server subtarget for Linux. The client, the Windows server and every
    /// Addressables bundle the client uses are untouched: ServerBuildBuilder turns "build Addressables with the player" off, so a server build makes no
    /// bundles that could reach a client. KN_KEEP_SERVER_SHADERS=1 switches this off. One line is logged with the count of stripped variants.
    /// </summary>
    public sealed class ServerShaderStrip : IPreprocessShaders, IPreprocessComputeShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private static int strippedShaders, strippedCompute;

        public int callbackOrder => int.MaxValue; // last, after every other stripper

        private static bool Active =>
            ServerBuildBuilder.IsBuilding
            && EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64
            && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server
            && Environment.GetEnvironmentVariable("KN_KEEP_SERVER_SHADERS") != "1";

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) { strippedShaders = strippedCompute = 0; }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!Active) return;
            strippedShaders += data.Count;
            data.Clear();
        }

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!Active) return;
            strippedCompute += data.Count;
            data.Clear();
        }

        public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            if (strippedShaders + strippedCompute > 0)
                Debug.Log($"[ServerShaderStrip] Linux dedicated server: stripped {strippedShaders} shader variants and {strippedCompute} compute shader kernels (set KN_KEEP_SERVER_SHADERS=1 to keep them).");
        }
    }
}
