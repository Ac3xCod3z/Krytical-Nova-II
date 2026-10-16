using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Configures headless execution parameters for Dedicated Game Server (UNITY_SERVER) builds.
    /// Sets optimal server tick budgets (30 Hz fixed), disables vSync and rendering overhead. On a server build it also
    /// switches off render-only vendor components in every scene that loads (the build does it too, in
    /// <c>ServerSceneStripper</c>): a server ships no shaders, and the lamps' light cones would throw every frame.
    /// </summary>
    public static class DedicatedServerOptimizer
    {
        private const int TargetServerTickRate = 30;

        /// <summary>Namespaces of vendor components that only draw. Keep in step with ServerSceneStripper.</summary>
        private static readonly string[] RenderOnlyNamespaces = { "VolumetricLights", "VolumetricFogAndMist2" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeServerEnvironment()
        {
#if UNITY_SERVER
            ApplyHeadlessSettings();
            SceneManager.sceneLoaded += (scene, mode) => SwitchOffRenderOnly(scene);
#else
            if (Application.isBatchMode)
            {
                ApplyHeadlessSettings();
            }
#endif
        }

        private static void ApplyHeadlessSettings()
        {
            Debug.Log($"[DedicatedServerOptimizer] Initializing Headless Server Environment (Target Tick Rate: {TargetServerTickRate} Hz)");
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetServerTickRate;
            AudioListener.pause = true;
            AudioListener.volume = 0f;
        }

        private static void SwitchOffRenderOnly(Scene scene)
        {
            int off = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null || !behaviour.enabled) continue;
                    string ns = behaviour.GetType().Namespace;
                    if (ns == null || System.Array.IndexOf(RenderOnlyNamespaces, ns) < 0) continue;
                    behaviour.enabled = false;
                    off++;
                }
            if (off > 0) Debug.Log($"[DedicatedServerOptimizer] {scene.name}: {off} render-only component(s) switched off.");
        }
    }
}
