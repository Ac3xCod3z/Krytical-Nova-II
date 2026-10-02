using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Configures headless execution parameters for Dedicated Game Server (UNITY_SERVER) builds.
    /// Sets optimal server tick budgets (30 Hz fixed), disables vSync and rendering overhead.
    /// </summary>
    public static class DedicatedServerOptimizer
    {
        private const int TargetServerTickRate = 30;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeServerEnvironment()
        {
#if UNITY_SERVER
            ApplyHeadlessSettings();
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
    }
}
