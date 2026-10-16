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
    /// Console key. The host, a server and an offline player run its commands here, as before. A joined player gets it
    /// too, but nothing they type runs on their own machine: until they are GM it asks for their GM password (hidden
    /// while typed, never logged), and once they are (<see cref="PlayerGm"/>) every line goes to the server and the
    /// answer is printed below it. While it is open it has the keyboard: the Hotbar, the window keys, chat and Y / N
    /// wait.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GmConsoleGate : MonoBehaviour
    {
        private const string ReasonNoPlayer = "The GM console opens once you are in the world.";

        [SerializeField] private QuantumConsole console;

        private bool holdingKeyboard;
        private bool armed;
        private bool armedAsGm;
        private TMP_InputField input;

        /// <summary>Commands run on this machine: no session, or this machine is the server.</summary>
        private static bool RunsHere
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm == null || !nm.IsListening || nm.IsServer;
            }
        }

        private void OnEnable() => PlayerGm.Reply += OnReply;

        private void OnDisable()
        {
            PlayerGm.Reply -= OnReply;
            Disarm();
            HoldKeyboard(false);
        }

        private void Update()
        {
            if (console == null) return;
            bool remote = !RunsHere;
            if (console.IsActive && remote && PlayerGm.Local == null) console.Deactivate(); // left the world with it open

            InputAction key = KeyBindings.Window(KeyBindings.GmConsole);
            if (key != null && key.WasPressedThisFrame() && !KeyBindings.Listening)
            {
                if (console.IsActive) console.Deactivate();
                else if (!remote || PlayerGm.Local != null) console.Activate();
                else ClientPresentationRegistry.Messages?.ShowMessage(ReasonNoPlayer);
            }

            if (console.IsActive && remote) Arm(PlayerGm.Local.IsGm);
            else Disarm();
            HoldKeyboard(console.IsActive);
        }

        // A joined player's console hands every submitted line to this gate instead of running it. Quantum Console drops
        // the hand-over after each line, so it is taken again on the next frame.
        private void Arm(bool asGm)
        {
            if (armed && armedAsGm == asGm) return;
            if (QuantumConsoleProcessor.TableIsGenerating) return; // the console takes no input until its list is ready
            armed = true;
            armedAsGm = asGm;
            Mask(!asGm);
            console.BeginResponse(asGm ? (System.Action<string>)OnGmLine : OnPassword, new ResponseConfig
            {
                InputPrompt = asGm ? "GM command (runs on the server)..." : "GM password...",
                LogInput = asGm,
            });
        }

        private void Disarm()
        {
            if (!armed) return;
            armed = false;
            Mask(false);
        }

        private void OnPassword(string password)
        {
            armed = false;
            ClearInput(); // before the mask comes off: the typed password must never show in the box
            Mask(false);
            if (RunsHere) { console.InvokeCommand(password); return; } // a hand-over left from a session that ended
            if (PlayerGm.Local == null) return;
            console.LogToConsole("Checking the GM password...");
            PlayerGm.Local.Login(password);
        }

        private void OnGmLine(string line)
        {
            armed = false;
            ClearInput();
            if (RunsHere) { console.InvokeCommand(line); return; }
            if (line.Equals("clear", System.StringComparison.OrdinalIgnoreCase))
            {
                console.ClearConsole();
                return;
            }
            PlayerGm.Local?.Run(line);
        }

        private void OnReply(string text)
        {
            if (console != null) console.LogToConsole(text);
        }

        // Quantum Console empties its input after a command it runs itself, but not after a line it hands over.
        private void ClearInput()
        {
            if (input == null && console != null) input = console.GetComponentInChildren<TMP_InputField>(true);
            if (input != null) input.SetTextWithoutNotify(string.Empty);
        }

        private void Mask(bool hide)
        {
            if (input == null && console != null) input = console.GetComponentInChildren<TMP_InputField>(true);
            if (input == null) return;
            TMP_InputField.ContentType type = hide ? TMP_InputField.ContentType.Password : TMP_InputField.ContentType.Standard;
            if (input.contentType == type) return;
            input.contentType = type;
            input.ForceLabelUpdate();
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
