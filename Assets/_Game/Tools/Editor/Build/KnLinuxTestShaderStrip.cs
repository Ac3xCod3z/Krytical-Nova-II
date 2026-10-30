using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Keeps shader compiling out of the two Linux players of the DOTS 2-client gate (<see cref="KnLinuxTestBuild"/>, plan section 43):
    /// both run with <c>-nographics</c> and draw nothing. Without it the server build compiled every shader and compute shader for
    /// glcore and Vulkan, and its process tree reached 21 GB before the RAM watchdog stopped it (29 Oct 2026). Same idea as
    /// server-plan's <c>ServerShaderStrip</c> (PR 166, Linux server only); active only while <see cref="KnLinuxTestBuild.IsBuilding"/>
    /// and the target is Linux, so no Windows player and no Addressables bundle is touched. Text shaders are kept (<see cref="Kept"/>). <c>KN_KEEP_SERVER_SHADERS=1</c> switches it off.
    /// </summary>
    public sealed class KnLinuxTestShaderStrip : IPreprocessShaders, IPreprocessComputeShaders, IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        static int s_Shaders, s_Compute;

        public int callbackOrder => int.MaxValue; // last, after every other stripper

        static bool Active =>
            KnLinuxTestBuild.IsBuilding
            && EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneLinux64
            && Environment.GetEnvironmentVariable("KN_KEEP_SERVER_SHADERS") != "1";

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report) { s_Shaders = s_Compute = 0; }

        /// <summary>
        /// Text shaders stay: UI Toolkit still makes a font material on a headless player, and with its shader stripped it threw
        /// "ArgumentNullException: shader" in the server log (B3 run 1, 30 Oct 2026). They are few and small.
        /// </summary>
        public static bool Kept(Shader shader)
        {
            string name = shader != null ? shader.name : "";
            return name.Contains("TextCore") || name.Contains("TextMeshPro") || name.Contains("UIElements");
        }

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (!Active || Kept(shader)) return;
            s_Shaders += data.Count;
            data.Clear();
        }

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (!Active) return;
            s_Compute += data.Count;
            data.Clear();
        }

        public void OnPostprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            if (s_Shaders + s_Compute > 0)
                Debug.Log($"[KnLinuxTestShaderStrip] stripped {s_Shaders} shader variants and {s_Compute} compute shader kernels (headless Linux test player).");
        }
    }
}
