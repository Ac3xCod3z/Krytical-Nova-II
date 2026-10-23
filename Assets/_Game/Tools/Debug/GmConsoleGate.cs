using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using QFSW.QC;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// The GM console in the game itself; in the Editor the GM Tools window does the same job. It opens on the GM
    /// Console key. The host, a server and an offline player run its commands here, as before. On a server it opens only
    /// for a GM (owner, 18 Oct 2026: GMs are bound to their accounts on the control page, no password is typed in the
    /// game, and nobody else gets the console): every line goes to the server (<see cref="GmRemoteLines"/>) and the answer
    /// is printed below it. For anyone else the key does nothing. While it is open it has the keyboard: the Hotbar, the
    /// window keys, chat and Y / N wait.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GmConsoleGate : MonoBehaviour
    {
        [SerializeField] private QuantumConsole console;

        private bool holdingKeyboard;

        /// <summary>Commands run on this machine: no session, or this machine is the server.</summary>
        private static bool RunsHere
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        /// <summary>Whether this player may have the console now: anyone where commands run here, a GM on a server.</summary>
        private static bool Allowed => RunsHere || (PlayerGm.Local != null && PlayerGm.Local.IsGm);

        private void OnEnable()
        {
            PlayerGm.Reply += OnReply;
            if (console != null) console.OnInvoke += Record;
        }

        private void OnDisable()
        {
            PlayerGm.Reply -= OnReply;
            if (console != null) console.OnInvoke -= Record;
            HoldKeyboard(false);
        }

        // The host's own lines go in the GM record like everyone's (a joined GM's are recorded by the server).
        private static void Record(string line)
        {
            if (!ServerRecords.Keeping || string.IsNullOrWhiteSpace(line)) return;
            ServerRecords.Write(RecordKind.Gm, $"Host as {ServerRecords.Who(NetworkManager.ServerClientId)}: {line.Trim()}");
        }

        private void Update()
        {
            if (console == null) return;
            if (console.IsActive && !Allowed) console.Deactivate(); // left the world, or GM rights ended, with it open

            InputAction key = KeyBindings.Window(KeyBindings.GmConsole);
            if (key != null && key.WasPressedThisFrame() && !KeyBindings.Listening && !KeyBindings.CtrlHeld && Allowed)
            {
                if (console.IsActive) console.Deactivate();
                else console.Activate();
            }
            HoldKeyboard(console.IsActive);
        }

        // The server's answers to this GM (and its notices: "You are GM ...").
        private void OnReply(string text)
        {
            if (console != null) console.LogToConsole(text);
        }

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
