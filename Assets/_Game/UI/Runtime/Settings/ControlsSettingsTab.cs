using System;
using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.UI.Menus;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Controls page of Settings: every key the player may change, in one list. Click a key, press the new one.
    /// A key that is already taken changes hands: the other action gets the key this one had, so nothing is left
    /// without a key. Esc cancels. The keys that cannot change are listed underneath for reference.
    /// </summary>
    public sealed class ControlsSettingsTab
    {
        // A fixed key is shown by its binding path where it has a picture, or by the word on its cap.
        private static readonly (string Label, string[] Keys)[] FixedKeys =
        {
            ("Close window / cancel", new[] { "ESC" }),
            ("Chat", new[] { "<Keyboard>/enter" }),
            ("Accept / decline a request", new[] { "Y", "N" }),
            ("Move, attack, interact", new[] { "<Mouse>/leftButton" }),
            ("Turn the camera", new[] { "<Mouse>/rightButton" }),
            ("Zoom", new[] { "<Mouse>/scroll" }),
        };

        // What a key may be: any keyboard key, or one of the spare mouse buttons. Never the two the game is played with.
        private static readonly string[] AllowedControls = { "<Keyboard>", "<Mouse>/middleButton", "<Mouse>/forwardButton", "<Mouse>/backButton" };
        private static readonly string[] RefusedPaths = { "<Keyboard>/escape", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/anyKey" };

        private readonly List<(InputAction Action, string Label, string Group)> entries = new List<(InputAction, string, string)>();
        private readonly Dictionary<InputAction, Button> keyButtons = new Dictionary<InputAction, Button>();
        private readonly Func<InputActionAsset> playerActions;
        private readonly Action<bool> listeningChanged;
        private VisualElement page;
        private InputActionRebindingExtensions.RebindingOperation operation;
        private InputAction listeningFor;

        /// <param name="playerActions">The local player's own input actions (null while there is no player).</param>
        /// <param name="listeningChanged">Told when the page starts and stops waiting for a key.</param>
        public ControlsSettingsTab(Func<InputActionAsset> playerActions, Action<bool> listeningChanged)
        {
            this.playerActions = playerActions;
            this.listeningChanged = listeningChanged;
        }

        public bool IsListening => operation != null;
        /// <summary>The frame the page last stopped listening on: the key that ended it must not act on anything else.</summary>
        public int ListeningEndedFrame { get; private set; } = -1;

        public void Build(VisualElement target)
        {
            Cancel();
            page = target;
            page.Clear();
            keyButtons.Clear();
            KeyBindings.Collect(playerActions(), entries);

            SettingsRows.Help(page, "Click a key, then press the one you want. A key already in use swaps with it. Esc cancels.");
            string group = null;
            foreach (var entry in entries)
            {
                if (entry.Group != group)
                {
                    group = entry.Group;
                    SettingsRows.Header(page, group);
                }
                AddRow(entry.Action, entry.Label);
            }
            if (playerActions() == null)
                SettingsRows.Help(page, "Hotbar and character keys appear here once your character is in the world.");

            var reset = new Button(ResetAll) { text = "RESET ALL KEYS" };
            reset.AddToClassList("kn-button-danger");
            reset.AddToClassList("section");
            page.Add(reset);

            SettingsRows.Header(page, "FIXED KEYS");
            foreach (var key in FixedKeys)
            {
                VisualElement row = SettingsRows.Row(page, key.Label);
                row.AddToClassList("controls-row");
                row.AddToClassList("fixed");
                var caps = new VisualElement { pickingMode = PickingMode.Ignore };
                caps.AddToClassList("key-caps");
                KeyCaps.Fill(caps, key.Keys);
                row.Insert(0, caps);
            }
        }

        /// <summary>Stops waiting for a key, leaving the binding as it was.</summary>
        public void Cancel()
        {
            if (operation != null) operation.Cancel();
        }

        private void AddRow(InputAction action, string label)
        {
            // The key first, as a cap, then what it does: the way the packs lay a controls page out.
            VisualElement row = SettingsRows.Row(page, label);
            row.AddToClassList("controls-row");
            var key = new Button();
            key.AddToClassList("settings-key");
            key.AddToClassList("key-caps");
            KeyCaps.Fill(key, action, KeyBindings.Label(action));
            key.clicked += () => Listen(action);
            row.Insert(0, key);
            keyButtons[action] = key;

            var reset = new Button(() => ResetOne(action)) { text = "RESET", tooltip = "Back to the default key" };
            reset.AddToClassList("settings-key-reset");
            row.Add(reset);
        }

        private void Listen(InputAction action)
        {
            if (operation != null)
            {
                bool same = listeningFor == action;
                Cancel();
                if (same) return;
            }

            string previousPath = action.bindings[0].effectivePath;
            bool wasEnabled = action.enabled;
            action.Disable(); // an action cannot be rebound while it listens for its own key
            listeningFor = action;
            KeyBindings.Listening = true;
            listeningChanged?.Invoke(true);
            Button button = keyButtons[action];
            button.Clear();
            button.Add(KeyCaps.Saying("PRESS A KEY"));
            button.AddToClassList("listening");

            var rebind = action.PerformInteractiveRebinding(0)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);
            foreach (string allowed in AllowedControls) rebind.WithControlsHavingToMatchPath(allowed);
            foreach (string refused in RefusedPaths) rebind.WithControlsExcluding(refused);

            void Finish(bool completed)
            {
                operation?.Dispose();
                operation = null;
                listeningFor = null;
                if (wasEnabled) action.Enable();
                button.RemoveFromClassList("listening");
                if (completed)
                {
                    // An action with a second default key (left and right Shift) keeps only the one just chosen.
                    for (int i = 1; i < action.bindings.Count; i++) action.ApplyBindingOverride(i, string.Empty);
                    GiveAwayKey(action, previousPath);
                    KeyBindings.Save(playerActions());
                }
                RefreshLabels();
                ListeningEndedFrame = Time.frameCount;
                KeyBindings.Listening = false;
                listeningChanged?.Invoke(false);
                MenuMoments.Raise(completed ? MenuMoment.Confirm : MenuMoment.Back, button);
            }

            operation = rebind.OnComplete(_ => Finish(true)).OnCancel(_ => Finish(false)).Start();
        }

        /// <summary>Whoever had the key this action just took gets the key it gave up.</summary>
        private void GiveAwayKey(InputAction taker, string previousPath)
        {
            string taken = taker.bindings[0].effectivePath;
            foreach (var entry in entries)
            {
                InputAction other = entry.Action;
                if (other == taker) continue;
                for (int i = 0; i < other.bindings.Count; i++)
                {
                    if (!string.Equals(other.bindings[i].effectivePath, taken, StringComparison.OrdinalIgnoreCase)) continue;
                    other.ApplyBindingOverride(i, i == 0 ? previousPath : string.Empty);
                }
            }
        }

        private void ResetOne(InputAction action)
        {
            Cancel();
            action.RemoveAllBindingOverrides();
            // Its default key may be in use by now: the action that holds it goes back to its own default too.
            string wanted = action.bindings[0].effectivePath;
            foreach (var entry in entries)
            {
                if (entry.Action == action) continue;
                if (string.Equals(entry.Action.bindings[0].effectivePath, wanted, StringComparison.OrdinalIgnoreCase))
                    entry.Action.RemoveAllBindingOverrides();
            }
            KeyBindings.Save(playerActions());
            RefreshLabels();
        }

        private void ResetAll()
        {
            Cancel();
            KeyBindings.ResetAll(playerActions());
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            foreach (var pair in keyButtons) KeyCaps.Fill(pair.Value, pair.Key, KeyBindings.Label(pair.Key));
        }
    }
}
