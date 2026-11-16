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
        private static bool warnedStuckSubtarget;

        public int callbackOrder => 0;

        /// <summary>
        /// A server build is what the build itself says it is: the server builder's own flag, or the subtarget named in the
        /// build's report. Never the Editor's remembered subtarget, which a killed server build can leave on Server.
        /// </summary>
        public static bool IsServerBuild(bool serverBuilderRunning, StandaloneBuildSubtarget reportSubtarget) =>
            serverBuilderRunning || reportSubtarget == StandaloneBuildSubtarget.Server;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // No report while entering Play in the Editor: only real builds, and only the server's. The summary may not
            // name the subtarget yet while scenes are processed, so the server builder's own flag counts too.
            if (report == null) return;
            if (!IsServerBuild(ServerBuildBuilder.IsBuilding, report.summary.GetSubtarget<StandaloneBuildSubtarget>()))
            {
                // A game's scene is left as it is, even when the Editor is stuck on the Server subtarget (ClientBuildGuard fixes that).
                if (!warnedStuckSubtarget && EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server)
                {
                    warnedStuckSubtarget = true;
                    Debug.LogWarning("[Build] The Editor's standalone subtarget is Server, but this build is not a server's: its scenes are not stripped.");
                }
                return;
            }

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
            RemoveClientOnlyScripts(scene);
        }

        /// <summary>
        /// Owner rule (16 Nov 2026): no visual code in the server build. Scripts of an assembly the server leaves out
        /// (defineConstraints "!UNITY_SERVER": UI, GameFeel, Audio, World.Presentation, ...) would be missing scripts in the
        /// server's copy of the scene, one warning each at load. They are taken out of THIS copy only: the scene on disk and
        /// the game's bundles are never touched (this runs only inside a server build, which builds no bundles; the black-sky
        /// lesson of build 12). An NGO NetworkBehaviour is never taken out (it would change its NetworkObject's hash).
        /// </summary>
        private static void RemoveClientOnlyScripts(Scene scene)
        {
            var doomed = new List<MonoBehaviour>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || !ServerExcludedAssemblies.Contains(mb.GetType().Assembly.GetName().Name)) continue;
                    if (mb is Unity.Netcode.NetworkBehaviour)
                    {
                        Debug.LogWarning($"[Build] Server copy of {scene.name}: {mb.GetType().FullName} on '{mb.name}' is client-only but an NGO NetworkBehaviour: kept.");
                        continue;
                    }
                    doomed.Add(mb);
                }
            // A script another one requires goes after it: retry until nothing more can go.
            int removed = 0;
            for (bool progress = true; progress && doomed.Count > 0;)
            {
                progress = false;
                for (int i = doomed.Count - 1; i >= 0; i--)
                {
                    if (!CanRemove(doomed[i])) continue;
                    Object.DestroyImmediate(doomed[i]);
                    doomed.RemoveAt(i);
                    removed++;
                    progress = true;
                }
            }
            if (removed > 0) Debug.Log($"[Build] Server copy of {scene.name}: {removed} client-only script(s) taken out (no missing scripts on the server).");
            foreach (MonoBehaviour left in doomed)
                Debug.LogWarning($"[Build] Server copy of {scene.name}: {left.GetType().FullName} on '{left.name}' could not be taken out (another component requires it).");
        }

        private static bool CanRemove(MonoBehaviour mb)
        {
            System.Type type = mb.GetType();
            foreach (Component other in mb.GetComponents<Component>())
            {
                if (other == null || other == mb) continue;
                foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                    if (Requires(req.m_Type0, type) || Requires(req.m_Type1, type) || Requires(req.m_Type2, type)) return false;
            }
            return true;
        }

        private static bool Requires(System.Type required, System.Type type) => required != null && required.IsAssignableFrom(type);
    }

    /// <summary>The first-party assemblies a dedicated server leaves out: their .asmdef says "!UNITY_SERVER" in defineConstraints.</summary>
    public static class ServerExcludedAssemblies
    {
        private static readonly Dictionary<string, bool> Known = new Dictionary<string, bool>();

        public static bool Contains(string assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName)) return false;
            if (Known.TryGetValue(assemblyName, out bool excluded)) return excluded;
            excluded = false;
            string path = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                excluded = IsServerExcluded(System.IO.File.ReadAllText(path));
            Known[assemblyName] = excluded;
            return excluded;
        }

        /// <summary>True when an .asmdef's text lists "!UNITY_SERVER" among its defineConstraints.</summary>
        public static bool IsServerExcluded(string asmdefJson)
        {
            var asmdef = JsonUtility.FromJson<AsmdefConstraints>(asmdefJson);
            if (asmdef?.defineConstraints == null) return false;
            foreach (string c in asmdef.defineConstraints)
                if (c != null && c.Replace(" ", "") == "!UNITY_SERVER") return true;
            return false;
        }

        [System.Serializable]
        private sealed class AsmdefConstraints { public string[] defineConstraints; }
    }
}
