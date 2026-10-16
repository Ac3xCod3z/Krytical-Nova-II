using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Sits on a map's NetworkManager object. Every map carries the whole network stack and an offline avatar so it
    /// can be played on its own, but only the first map's stack lives on (it is kept across loads). When a later map
    /// loads, this removes its second stack before any of it wakes, and hands its avatar to the living
    /// <see cref="OfflinePlayerHandoff"/> (hidden while a session runs). Runs before every other script.
    /// </summary>
    [DefaultExecutionOrder(-32000)]
    [DisallowMultipleComponent]
    public sealed class SceneNetworkGuard : MonoBehaviour
    {
        [Tooltip("This map's offline avatar (the scene's Player).")]
        [SerializeField] private GameObject offlinePlayer;

        private void Awake()
        {
            var living = NetworkManager.Singleton;
            if (living == null || living.gameObject == gameObject) return;

            OfflinePlayerHandoff.Adopt(offlinePlayer);
            // Inactive before the other components' Awake: this NetworkManager never registers or survives the load.
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

#if UNITY_EDITOR
        public void EditorSetOfflinePlayer(GameObject value) => offlinePlayer = value;
#endif
    }
}
