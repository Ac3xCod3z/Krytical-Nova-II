using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using QFSW.QC;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// The GM console in the game itself; in the Editor the GM Tools window does the same job. It opens on the GM
    /// Console key for the host, a server or an offline player: the same rule every GM command checks
    /// (<see cref="GmTarget"/>). A joined client has no GM commands, so it gets no console either. While it is open
    /// it has the keyboard: the Hotbar, the window keys, chat and Y / N wait.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GmConsoleGate : MonoBehaviour
    {
        private const string ReasonNotHost = "Only the host can use the GM console.";

        [SerializeField] private QuantumConsole console;

        private bool holdingKeyboard;

        private static bool MayUse
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        private void Update()
        {
            if (console == null) return;
            if (console.IsActive && !MayUse) console.Deactivate(); // joined someone else's world with it open

            InputAction key = KeyBindings.Window(KeyBindings.GmConsole);
            if (key != null && key.WasPressedThisFrame() && !KeyBindings.Listening)
            {
                if (console.IsActive) console.Deactivate();
                else if (MayUse) console.Activate();
                else ClientPresentationRegistry.Messages?.ShowMessage(ReasonNotHost);
            }
            HoldKeyboard(console.IsActive);
        }

        private void OnDisable() => HoldKeyboard(false);

        private void HoldKeyboard(bool hold)
        {
            if (!hold && !holdingKeyboard) return;
            holdingKeyboard = hold;
            KeyBindings.KeyboardTaken = hold;
            // Every frame while open: the local player object changes when a session starts or ends.
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && local.TryGetComponent(out PlayerSkillHotbar hotbar)) hotbar.InputSuppressed = hold;
        }
    }
}
