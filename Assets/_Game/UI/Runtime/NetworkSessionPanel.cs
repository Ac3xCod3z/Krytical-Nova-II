using KryticalNova.Networking;
using NetworkConnectionManager = KryticalNova.Networking.NetworkConnectionManager;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// UI Toolkit replacement for the IMGUI <see cref="NetworkDebugHUD"/> on clients: host/join locally or through
    /// online sessions, see the role and session code, disconnect. F1 or ~ toggles it. The IMGUI HUD stays on the
    /// NetworkManager prefab for headless/server use and is switched off while this panel exists.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class NetworkSessionPanel : MonoBehaviour
    {
        private const ushort DefaultPort = 7777;
        private const float RefreshInterval = 0.25f;

        [SerializeField] private bool visibleOnStart = true;

        private VisualElement root;
        private VisualElement panel;
        private VisualElement offlineGroup;
        private VisualElement onlineGroup;
        private VisualElement sessionRow;
        private Label statusLabel;
        private Label roleLabel;
        private Label clientsLabel;
        private Label sessionCodeLabel;
        private TextField ipField;
        private TextField portField;
        private TextField codeField;
        private ProgressionHUD hud;
        // Resolved live: the HUD rebinds to the networked player when hosting/joining.
        private PlayerSkillHotbar hotbar => hud != null ? hud.Hotbar : null;
        private NetworkConnectionManager subscribedManager;
        private VisualElement boundRoot;
        private float nextRefresh;
        private int focusedFields;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            panel = root?.Q("network-panel");
            if (panel == null) return;

            offlineGroup = panel.Q("network-offline");
            onlineGroup = panel.Q("network-online");
            sessionRow = panel.Q("network-session-row");
            statusLabel = panel.Q<Label>("network-status");
            roleLabel = panel.Q<Label>("network-role");
            clientsLabel = panel.Q<Label>("network-clients");
            sessionCodeLabel = panel.Q<Label>("network-session-code");
            ipField = panel.Q<TextField>("network-ip");
            portField = panel.Q<TextField>("network-port");
            codeField = panel.Q<TextField>("network-code");

            hud = GetComponent<ProgressionHUD>();

            if (boundRoot != root)
            {
                boundRoot = root;
                BindControls();
            }

            foreach (var imguiHud in FindObjectsByType<NetworkDebugHUD>(FindObjectsInactive.Include))
                imguiHud.enabled = false;

            SetVisible(panel, visibleOnStart);
            Refresh();
        }

        private void Start() => SubscribeToManager();

        private void OnDisable()
        {
            UnsubscribeFromManager();
            if (focusedFields > 0 && hotbar != null) hotbar.InputSuppressed = false;
            focusedFields = 0;
        }

        private void Update()
        {
            if (panel == null) return;
            if (subscribedManager == null) SubscribeToManager();

            var keyboard = Keyboard.current;
            if (keyboard != null && focusedFields == 0
                && (keyboard.backquoteKey.wasPressedThisFrame || keyboard.f1Key.wasPressedThisFrame))
                SetVisible(panel, panel.resolvedStyle.display == DisplayStyle.None);

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh();
            }
        }

        private void BindControls()
        {
            Bind("network-close", () => SetVisible(panel, false));
            Bind("network-host", () => Manager?.StartLocalHost(ParsePort()));
            Bind("network-join", () => Manager?.StartLocalClient(ipField?.value ?? string.Empty, ParsePort()));
            Bind("network-server", () => Manager?.StartLocalServer(ParsePort()));
            Bind("network-host-online", () => { if (Manager != null) _ = Manager.StartMediatedHostAsync(); });
            Bind("network-join-code", () => { if (Manager != null) _ = Manager.JoinMediatedSessionByCodeAsync(codeField?.value ?? string.Empty); });
            Bind("network-copy-code", () =>
            {
                if (Manager != null && !string.IsNullOrEmpty(Manager.ActiveSessionCode))
                    GUIUtility.systemCopyBuffer = Manager.ActiveSessionCode;
            });
            Bind("network-disconnect", Disconnect);

            // Typing an IP or join code must not fire hotbar keys (1-0, C, K).
            foreach (TextField field in new[] { ipField, portField, codeField })
            {
                if (field == null) continue;
                field.RegisterCallback<FocusInEvent>(_ => SetTyping(+1));
                field.RegisterCallback<FocusOutEvent>(_ => SetTyping(-1));
            }
        }

        private static NetworkConnectionManager Manager => NetworkConnectionManager.Instance;

        private void Disconnect()
        {
            if (Manager != null) _ = Manager.DisconnectAsync();
            var nm = NetworkManager.Singleton;
            if (nm != null) nm.Shutdown();
        }

        private ushort ParsePort()
        {
            return portField != null && ushort.TryParse(portField.value, out ushort port) && port != 0 ? port : DefaultPort;
        }

        private void Refresh()
        {
            var nm = NetworkManager.Singleton;
            bool connected = nm != null && (nm.IsClient || nm.IsServer);
            SetVisible(offlineGroup, !connected);
            SetVisible(onlineGroup, connected);
            if (!connected) return;

            roleLabel.text = nm.IsHost ? "Role: Host (Server + Client)" : nm.IsServer ? "Role: Dedicated Server" : "Role: Client";
            clientsLabel.text = nm.IsServer ? $"Connected Clients: {nm.ConnectedClientsIds.Count}" : "Connected to host";
            string code = Manager != null ? Manager.ActiveSessionCode : null;
            SetVisible(sessionRow, !string.IsNullOrEmpty(code));
            if (!string.IsNullOrEmpty(code)) sessionCodeLabel.text = $"Session Code: {code}";
        }

        private void SubscribeToManager()
        {
            if (subscribedManager != null || Manager == null) return;
            subscribedManager = Manager;
            subscribedManager.StatusChanged += OnStatusChanged;
            subscribedManager.ErrorReported += OnErrorReported;
        }

        private void UnsubscribeFromManager()
        {
            if (subscribedManager == null) return;
            subscribedManager.StatusChanged -= OnStatusChanged;
            subscribedManager.ErrorReported -= OnErrorReported;
            subscribedManager = null;
        }

        private void OnStatusChanged(string status)
        {
            if (statusLabel == null) return;
            statusLabel.text = status;
            statusLabel.style.color = StyleKeyword.Null;
        }

        private void OnErrorReported(string error)
        {
            if (statusLabel == null) return;
            statusLabel.text = $"Error: {error}";
            statusLabel.style.color = new Color(1f, 0.45f, 0.45f);
        }

        private void SetTyping(int delta)
        {
            int previous = focusedFields;
            focusedFields = Mathf.Max(0, focusedFields + delta);
            if (hotbar == null || (previous > 0) == (focusedFields > 0)) return;
            hotbar.InputSuppressed = focusedFields > 0;
        }

        private void Bind(string elementName, System.Action callback)
        {
            Button button = panel.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
