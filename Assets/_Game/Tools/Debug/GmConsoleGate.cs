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
    /// while typed, never logged), and once they are (<see cref="PlayerGm"/>) the console works as usual, hints and
    /// all, but every line goes to the server (<see cref="GmRemoteLines"/>) and the answer is printed below it. While it is open it has the keyboard: the Hotbar, the window keys, chat and Y / N
    /// wait.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GmConsoleGate : MonoBehaviour
    {
        private const string ReasonNoPlayer = "The GM console opens once you are in the world.";

        [SerializeField] private QuantumConsole console;

        private bool holdingKeyboard;
        private bool armed;
        private TMP_InputField input;
        private bool waitingForAnswer; // a password was sent: no new prompt until the server answers

        // Quantum Console has no call to drop a hand-over it was given; these two fields are how it keeps one.
        private static readonly System.Reflection.FieldInfo HandlingResponse = typeof(QuantumConsole).GetField(
            "_isHandlingUserResponse", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly System.Reflection.FieldInfo ResponseCallback = typeof(QuantumConsole).GetField(
            "_onSubmitResponseCallback", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

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

            // Only the password is taken from the console; a GM's lines run through it as usual, with its hints, and
            // GmRemoteLines sends them to the server.
            if (!remote) waitingForAnswer = false; // the session ended before the answer came
            if (console.IsActive && remote && !PlayerGm.Local.IsGm && !waitingForAnswer) Arm();
            else Disarm();
            HoldKeyboard(console.IsActive);
        }

        // A joined player's console hands every submitted line to this gate instead of running it. Quantum Console drops
        // the hand-over after each line, so it is taken again on the next frame.
        private void Arm()
        {
            if (armed) return;
            if (QuantumConsoleProcessor.TableIsGenerating) return; // the console takes no input until its list is ready
            armed = true;
            Mask(true);
            console.BeginResponse(OnPassword, new ResponseConfig { InputPrompt = "GM password...", LogInput = false });
        }

        private void Disarm()
        {
            if (!armed) return;
            armed = false;
            Mask(false);
            CancelResponse();
        }

        // The player became GM (or left) with the password prompt up: the console goes back to running its lines as
        // usual, hints and all; its next frame puts its own prompt back.
        private void CancelResponse()
        {
            if (HandlingResponse == null || !(bool)HandlingResponse.GetValue(console)) return;
            HandlingResponse.SetValue(console, false);
            ResponseCallback?.SetValue(console, null);
            if (input != null) input.interactable = false;
        }

        private void OnPassword(string password)
        {
            armed = false;
            ClearInput(); // before the mask comes off: the typed password must never show in the box
            Mask(false);
            if (RunsHere) { console.InvokeCommand(password); return; } // a hand-over left from a session that ended
            if (PlayerGm.Local == null) return;
            console.LogToConsole("Checking the GM password...");
            waitingForAnswer = true;
            PlayerGm.Local.Login(password);
        }

        private void OnReply(string text)
        {
            waitingForAnswer = false;
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
