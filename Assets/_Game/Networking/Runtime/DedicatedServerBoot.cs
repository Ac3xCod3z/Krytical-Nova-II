using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Networking
{
    /// <summary>
    /// A dedicated server has no front end. The build boots into the Title scene like a client does, so the server
    /// goes straight on to the zone, where the NetworkConnectionManager starts the session.
    /// </summary>
    public static class DedicatedServerBoot
    {
        public const string WorldScene = "Zone01_StarterTown";

#if UNITY_SERVER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LoadWorld()
        {
            if (SceneManager.GetActiveScene().name == WorldScene) return;
            Debug.Log($"[DedicatedServerBoot] Skipping the front end, loading {WorldScene}.");
            SceneManager.LoadScene(WorldScene);
        }
#endif
    }
}
