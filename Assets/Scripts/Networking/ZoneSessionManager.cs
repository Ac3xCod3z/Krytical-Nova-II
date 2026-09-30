using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Handles horizontal zone sharding and seamless player transitions across multi-server MMORPG zone instances.
    /// Enables the world to scale to thousands of concurrent players by distributing zones across dedicated servers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoneSessionManager : MonoBehaviour
    {
        public static ZoneSessionManager Instance { get; private set; }

        [Header("Current Zone Identification")]
        [SerializeField] private string currentZoneId = "Central_Plains_01";
        [SerializeField] private string currentZoneDisplayName = "Central Plains";

        [Header("Feature Gate")]
        [Tooltip("Zone transitions disconnect the current session. Keep disabled until a second zone server/scene exists.")]
        [SerializeField] private bool allowZoneTransitions;

        public event Action<string> ZoneTransitionStarted;
        public event Action<string> ZoneTransitionCompleted;
        public event Action<string> ZoneTransitionFailed;

        public string CurrentZoneId => currentZoneId;
        public string CurrentZoneDisplayName => currentZoneDisplayName;
        public bool AllowZoneTransitions => allowZoneTransitions;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Transitions a player avatar from the current zone server to a target zone server instance.
        /// </summary>
        public async Task<bool> TransitionToZoneAsync(string targetZoneId)
        {
            if (!allowZoneTransitions)
            {
                string disabled = $"Zone transition to {targetZoneId} blocked: zone transitions are disabled on {nameof(ZoneSessionManager)} (enable 'Allow Zone Transitions' once a second zone exists).";
                Debug.LogError($"[ZoneSessionManager] {disabled}", this);
                ZoneTransitionFailed?.Invoke(disabled);
                return false;
            }

            if (string.Equals(currentZoneId, targetZoneId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[ZoneSessionManager] Already in zone {targetZoneId}.");
                return true;
            }

            ZoneTransitionStarted?.Invoke(targetZoneId);
            Debug.Log($"[ZoneSessionManager] Initiating transition from {currentZoneId} to {targetZoneId}...");

            try
            {
                // Disconnect from current zone session cleanly
                if (NetworkConnectionManager.Instance != null)
                {
                    await NetworkConnectionManager.Instance.DisconnectAsync();
                }

                // Query for available sessions hosting the target zone
                var queryOptions = new QuerySessionsOptions
                {
                    Count = 10,
                    FilterOptions = new List<FilterOption>
                    {
                        new FilterOption(FilterField.StringIndex1, targetZoneId, FilterOperation.Equal),
                        new FilterOption(FilterField.IsLocked, "false", FilterOperation.Equal)
                    }
                };

                var queryResults = await MultiplayerService.Instance.QuerySessionsAsync(queryOptions);

                if (queryResults.Sessions != null && queryResults.Sessions.Count > 0)
                {
                    var targetSession = queryResults.Sessions[0];
                    Debug.Log($"[ZoneSessionManager] Found active session for zone {targetZoneId}. Joining session {targetSession.Id}...");

                    var joinedSession = await MultiplayerService.Instance.JoinSessionByIdAsync(targetSession.Id);
                    currentZoneId = targetZoneId;
                    ZoneTransitionCompleted?.Invoke(targetZoneId);
                    return true;
                }

                // If in local development and no dedicated cloud server is running, fall back to local host for that zone
                Debug.LogWarning($"[ZoneSessionManager] No active cloud session found for {targetZoneId}. Starting local development zone instance.");
                bool localSuccess = NetworkConnectionManager.Instance != null && NetworkConnectionManager.Instance.StartLocalHost();
                if (localSuccess)
                {
                    currentZoneId = targetZoneId;
                    ZoneTransitionCompleted?.Invoke(targetZoneId);
                    return true;
                }

                ZoneTransitionFailed?.Invoke($"Could not connect to zone {targetZoneId}.");
                return false;
            }
            catch (Exception ex)
            {
                string error = $"Zone transition to {targetZoneId} failed: {ex.Message}";
                Debug.LogError(error, this);
                ZoneTransitionFailed?.Invoke(error);
                return false;
            }
        }
    }
}
