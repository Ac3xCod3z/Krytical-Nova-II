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
            if (ServerSingleCopy.AnotherCopyRuns) return; // a second copy on this PC is quitting
            // The map of the server's settings (server-settings.json or -map); Zone01 when there is no such scene.
            string map = ServerLaunchOptions.LoadForThisServer().map;
            if (!Application.CanStreamedLevelBeLoaded(map))
            {
                Debug.LogWarning($"[DedicatedServerBoot] There is no map called \"{map}\" in this build: opening {WorldScene}.");
                map = WorldScene;
            }
            if (SceneManager.GetActiveScene().name == map) return;
            Debug.Log($"[DedicatedServerBoot] Skipping the front end, loading {map}.");
            SceneManager.LoadScene(map);
        }
#endif
    }
}
