using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Developer HUD for connecting via local host/client or mediated online sessions.
    /// Accessible via GUI toggle (F1).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkDebugHUD : MonoBehaviour
    {
        [SerializeField] private bool showHUD = true;

        private string ipInput = "127.0.0.1";
        private string portInput = "7777";
        private string joinCodeInput = "";
        private string statusMessage = "Idle";

        private void Start()
        {
            if (NetworkConnectionManager.Instance != null)
            {
                NetworkConnectionManager.Instance.StatusChanged += OnStatusChanged;
                NetworkConnectionManager.Instance.ErrorReported += OnErrorReported;
            }
        }

        private void OnDestroy()
        {
            if (NetworkConnectionManager.Instance != null)
            {
                NetworkConnectionManager.Instance.StatusChanged -= OnStatusChanged;
                NetworkConnectionManager.Instance.ErrorReported -= OnErrorReported;
            }
        }

        private void OnStatusChanged(string status) => statusMessage = status;
        private void OnErrorReported(string error) => statusMessage = $"<color=red>Error: {error}</color>";

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && KryticalNova.Core.KeyBindings.Pressed(KryticalNova.Core.KeyBindings.NetworkPanel))
            {
                showHUD = !showHUD;
            }
        }

        private void OnGUI()
        {
            if (!showHUD) return;

            GUILayout.BeginArea(new Rect(10, 10, 320, 420), GUI.skin.box);
            GUILayout.Label("<b>Krytical Nova II - Network HUD</b>", GUI.skin.label);
            GUILayout.Label($"Status: {statusMessage}");

            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsClient || nm.IsServer))
            {
                string role = nm.IsHost ? "Host (Server + Client)" : (nm.IsServer ? "Dedicated Server" : "Client");
                GUILayout.Label($"<b>Role:</b> {role}");
                GUILayout.Label($"Connected Clients: {nm.ConnectedClientsIds.Count}");

                if (NetworkConnectionManager.Instance != null && !string.IsNullOrEmpty(NetworkConnectionManager.Instance.ActiveSessionCode))
                {
                    GUILayout.Label($"<b>Session Code:</b> {NetworkConnectionManager.Instance.ActiveSessionCode}");
                    if (GUILayout.Button("Copy Join Code"))
                    {
                        GUIUtility.systemCopyBuffer = NetworkConnectionManager.Instance.ActiveSessionCode;
                    }
                }

                if (GUILayout.Button("Disconnect"))
                {
                    _ = NetworkConnectionManager.Instance != null
                        ? NetworkConnectionManager.Instance.DisconnectAsync()
                        : null;
                    if (nm != null) nm.Shutdown();
                }
            }
            else
            {
                GUILayout.Space(5);
                GUILayout.Label("<b>--- Local / Direct Network ---</b>");
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(35));
                ipInput = GUILayout.TextField(ipInput);
                GUILayout.Label("Port:", GUILayout.Width(35));
                portInput = GUILayout.TextField(portInput, GUILayout.Width(55));
                GUILayout.EndHorizontal();

                ushort.TryParse(portInput, out ushort parsedPort);
                if (parsedPort == 0) parsedPort = 7777;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Start Host"))
                {
                    NetworkConnectionManager.Instance?.StartLocalHost(parsedPort);
                }
                if (GUILayout.Button("Join Client"))
                {
                    NetworkConnectionManager.Instance?.StartLocalClient(ipInput, parsedPort);
                }
                if (GUILayout.Button("Start Server"))
                {
                    NetworkConnectionManager.Instance?.StartLocalServer(parsedPort);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                GUILayout.Label("<b>--- Online Brokered Sessions ---</b>");
                if (GUILayout.Button("Host Online Session"))
                {
                    _ = NetworkConnectionManager.Instance?.StartMediatedHostAsync();
                }

                GUILayout.BeginHorizontal();
                joinCodeInput = GUILayout.TextField(joinCodeInput, GUILayout.Width(180));
                if (GUILayout.Button("Join Code"))
                {
                    _ = NetworkConnectionManager.Instance?.JoinMediatedSessionByCodeAsync(joinCodeInput);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Press [~] or [F1] to toggle this menu.", GUI.skin.label);
            GUILayout.EndArea();
        }
    }
}
