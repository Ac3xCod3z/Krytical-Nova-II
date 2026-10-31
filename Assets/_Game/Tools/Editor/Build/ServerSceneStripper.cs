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
    /// What may go off is <see cref="ServerOffList"/>, nothing else.
    /// </summary>
    public sealed class ServerSceneStripper : IProcessSceneWithReport
    {
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
                    // Render-only vendor components and UI Toolkit documents (ServerOffList, ServerOff.md say why).
                    if (!ServerOffList.MayBeOffOnServer(behaviour)) continue;
                    behaviour.enabled = false;
                    off++;
                }
            if (off > 0) Debug.Log($"[Build] Server copy of {scene.name}: {off} render-only component(s) switched off.");
        }
    }
}
