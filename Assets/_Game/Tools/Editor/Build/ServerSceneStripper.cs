using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The dedicated server's copy of each scene, as it goes into the build: render-only vendor components are
    /// switched off before they ever wake. A server ships no shaders, and the lamps' light cones (Volumetric Lights)
    /// otherwise try to make a material every frame and throw. The scenes on disk and the client build are untouched.
    /// </summary>
    public sealed class ServerSceneStripper : IProcessSceneWithReport
    {
        /// <summary>Namespaces of vendor components that only draw: the server has nothing to draw them on.</summary>
        private static readonly HashSet<string> RenderOnlyNamespaces = new HashSet<string>
        {
            "VolumetricLights",
            "VolumetricFogAndMist2",
        };

        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // No report while entering Play in the Editor: only real builds, and only the server's. The summary may not
            // name the subtarget yet while scenes are processed, so the server builder's own flag counts too.
            if (report == null) return;
            if (!ServerBuildBuilder.IsBuilding && report.summary.GetSubtarget<StandaloneBuildSubtarget>() != StandaloneBuildSubtarget.Server) return;

            int off = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null || !behaviour.enabled) continue;
                    string ns = behaviour.GetType().Namespace;
                    bool renderOnly = ns != null && RenderOnlyNamespaces.Contains(ns);
                    // UI Toolkit documents: no UI assembly reads them on a server, and laying out their text looks for
                    // the system's fonts, which needs a shader (DedicatedServerOptimizer does the same as scenes load).
                    if (!renderOnly && !(behaviour is UnityEngine.UIElements.UIDocument)) continue;
                    behaviour.enabled = false;
                    off++;
                }
            if (off > 0) Debug.Log($"[Build] Server copy of {scene.name}: {off} render-only component(s) switched off.");
        }
    }
}
