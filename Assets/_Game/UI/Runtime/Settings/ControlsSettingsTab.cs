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
    /// The Controls page of Settings: the keyboard, drawn from the faction's own key caps, with what each key does
    /// written on it. Every key the player may change is lit; click it, press the new one. A key that is already
    /// taken changes hands: the other action gets the key this one had, so nothing is left without a key. Right-click
    /// puts a key's default back. Esc cancels. The keys that cannot change are on the board too, marked as fixed,
    /// and what is not on a keyboard (the mouse) stands beside it. An action whose key is not on the board (a spare
    /// mouse button, the number pad) is listed under it, so it can still be changed.
    /// </summary>
    public sealed class ControlsSettingsTab
    {
        // The board: each row left to right. Control = the Input System's name for the key (null = a gap), the
        // legend on the cap, and the cap's width in key widths. Every row is fifteen key widths long.
        private static readonly (string Control, string Legend, float Width)[][] Board =
        {
            new (string, string, float)[]
            {
                ("escape", "ESC", 1f), (null, null, 1f), ("f1", "F1", 1f), ("f2", "F2", 1f), ("f3", "F3", 1f), ("f4", "F4", 1f),
                ("f5", "F5", 1f), ("f6", "F6", 1f), ("f7", "F7", 1f), ("f8", "F8", 1f), ("f9", "F9", 1f), ("f10", "F10", 1f),
                ("f11", "F11", 1f), ("f12", "F12", 1f), (null, null, 1f),
            },
            new (string, string, float)[]
            {
                ("backquote", "`", 1f), ("1", "1", 1f), ("2", "2", 1f), ("3", "3", 1f), ("4", "4", 1f), ("5", "5", 1f), ("6", "6", 1f),
                ("7", "7", 1f), ("8", "8", 1f), ("9", "9", 1f), ("0", "0", 1f), ("minus", "-", 1f), ("equals", "=", 1f), ("backspace", "BKSP", 2f),
            },
            new (string, string, float)[]
            {
                ("tab", "TAB", 1.5f), ("q", "Q", 1f), ("w", "W", 1f), ("e", "E", 1f), ("r", "R", 1f), ("t", "T", 1f), ("y", "Y", 1f),
                ("u", "U", 1f), ("i", "I", 1f), ("o", "O", 1f), ("p", "P", 1f), ("leftBracket", "[", 1f), ("rightBracket", "]", 1f), ("backslash", "\\", 1.5f),
            },
            new (string, string, float)[]
            {
                ("capsLock", "CAPS", 1.75f), ("a", "A", 1f), ("s", "S", 1f), ("d", "D", 1f), ("f", "F", 1f), ("g", "G", 1f), ("h", "H", 1f),
                ("j", "J", 1f), ("k", "K", 1f), ("l", "L", 1f), ("semicolon", ";", 1f), ("quote", "'", 1f), ("enter", "ENTER", 2.25f),
            },
            new (string, string, float)[]
            {
                ("leftShift", "SHIFT", 2.25f), ("z", "Z", 1f), ("x", "X", 1f), ("c", "C", 1f), ("v", "V", 1f), ("b", "B", 1f), ("n", "N", 1f),
                ("m", "M", 1f), ("comma", ",", 1f), ("period", ".", 1f), ("slash", "/", 1f), ("rightShift", "SHIFT", 2.75f),
            },
            new (string, string, float)[]
            {
                ("leftCtrl", "CTRL", 1.5f), (null, null, 1f), ("leftAlt", "ALT", 1.5f), ("space", "SPACE", 7f), ("rightAlt", "ALT", 1.5f),
                (null, null, 1f), ("rightCtrl", "CTRL", 1.5f),
            },
        };

        // A cap is this wide for one key width, and this tall. Fifteen across fill the wide Settings window.
        private const float KeyWidth = 56f, KeyHeight = 46f;
        // Lettering on a cap is sized here, not in the sheets: a skin scales the sheets' sizes, and a cap does not grow.
        private const float LegendSize = 10f, CaptionSize = 9.5f;

        // The keys that cannot change, on the board: the key, and what is written on it.
        private static readonly (string Control, string Caption, string Says)[] FixedBoardKeys =
        {
            ("escape", "Cancel", "Close window / cancel"),
            ("enter", "Chat", "Chat"),
            ("y", "Accept", "Accept a request"),
            ("n", "Decline", "Decline a request"),
        };

        // What is not on a keyboard.
        private static readonly (string Path, string Says)[] MouseKeys =
        {
            ("<Mouse>/leftButton", "Move, attack, interact"),
            ("<Mouse>/rightButton", "Turn the camera"),
            ("<Mouse>/scroll", "Zoom"),
        };

        // What a cap says an action is, where its name on the list is too long for a cap.
        private static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>
        {
            { "Equipment", "Equip" }, { "Pick up nearest", "Pick up" }, { "Network panel", "Network" }, { "GM console (host)", "GM" },
            { "Emote wheel (hold)", "Emotes" }, { "Autopilot on / off", "Autopilot" }, { "Autopilot and Magnet", "Auto set" },
            { "Character Sheet", "Sheet" }, { "Draw / sheathe weapon", "Weapon" }, { "Flip camera", "Flip cam" },
        };

        private const string HelpText = "Click a lit key, then press the one you want. A key already in use swaps with it. Right-click a key for its default. Esc cancels.";

        private sealed class BoardKey
        {
            public Button Button;
            public Label Caption;
            public string Legend;
            public InputAction Action;
            public string Says;
        }

        private readonly List<(InputAction Action, string Label, string Group)> entries = new List<(InputAction, string, string)>();
        private readonly Dictionary<InputAction, Button> keyButtons = new Dictionary<InputAction, Button>();
        private readonly Dictionary<string, BoardKey> boardKeys = new Dictionary<string, BoardKey>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<InputActionAsset> playerActions;
        private readonly Action<bool> listeningChanged;
        private VisualElement page, others;
        private Label help;
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
            boardKeys.Clear();
            KeyBindings.Collect(playerActions(), entries);

            help = SettingsRows.Help(page, HelpText);
            help.AddToClassList("controls-help");

            var board = new VisualElement();
            board.AddToClassList("controls-board");
            foreach (var row in Board)
            {
                var line = new VisualElement();
                line.AddToClassList("board-row");
                foreach (var key in row) line.Add(key.Control == null ? Gap(key.Width) : Key(key.Control, key.Legend, key.Width));
                board.Add(line);
            }
            page.Add(board);

            // Under the board: what the bands on the caps mean, the mouse, and putting every key back.
            var foot = new VisualElement();
            foot.AddToClassList("controls-foot");
            var legend = new VisualElement();
            legend.AddToClassList("controls-legend");
            foreach (string group in new[] { "WINDOWS", "HOTBAR", "CHARACTER", "FIXED" }) legend.Add(LegendChip(group));
            foot.Add(legend);
            var mouse = new VisualElement();
            mouse.AddToClassList("controls-mouse");
            foreach (var key in MouseKeys)
            {
                var item = new VisualElement();
                item.AddToClassList("controls-mouse-item");
                item.Add(KeyCaps.For(key.Path, key.Path));
                var says = new Label(key.Says);
                says.AddToClassList("controls-mouse-says");
                item.Add(says);
                mouse.Add(item);
            }
            foot.Add(mouse);
            var reset = new Button(ResetAll) { text = "RESET ALL KEYS" };
            reset.AddToClassList("kn-button-danger");
            reset.AddToClassList("controls-reset-all");
            foot.Add(reset);
            page.Add(foot);

            others = new VisualElement();
            others.AddToClassList("controls-others");
            page.Add(others);

            if (playerActions() == null)
                SettingsRows.Help(page, "Hotbar and character keys appear here once your character is in the world.");
            Paint();
        }

        /// <summary>Stops waiting for a key, leaving the binding as it was.</summary>
        public void Cancel()
        {
            if (operation != null) operation.Cancel();
        }

        // ------------------------------------------------------------------ the board

        private static VisualElement Gap(float width)
        {
            var gap = new VisualElement { pickingMode = PickingMode.Ignore };
            gap.style.width = width * KeyWidth;
            return gap;
        }

        private VisualElement Key(string control, string legend, float width)
        {
            var key = new BoardKey { Legend = legend };
            var button = new Button { focusable = false };
            button.AddToClassList("settings-key");
            button.AddToClassList("board-key");
            button.style.width = width * KeyWidth;
            button.style.height = KeyHeight;

            var cap = new VisualElement { pickingMode = PickingMode.Ignore };
            cap.AddToClassList(KeyCaps.CapClass);
            cap.AddToClassList("board-cap");
            var letter = new Label(legend) { pickingMode = PickingMode.Ignore };
            letter.AddToClassList("key-cap-text");
            letter.AddToClassList("board-letter");
            letter.style.fontSize = LegendSize;
            cap.Add(letter);
            key.Caption = new Label { pickingMode = PickingMode.Ignore };
            key.Caption.AddToClassList("board-caption");
            key.Caption.style.fontSize = CaptionSize;
            cap.Add(key.Caption);
            var band = new VisualElement { pickingMode = PickingMode.Ignore };
            band.AddToClassList("board-band");
            cap.Add(band);
            button.Add(cap);

            button.clicked += () => { if (key.Action != null) Listen(key.Action); };
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != (int)MouseButton.RightMouse || key.Action == null) return;
                evt.StopPropagation();
                ResetOne(key.Action);
            }, TrickleDown.TrickleDown);
            button.RegisterCallback<PointerEnterEvent>(_ => { if (operation == null && key.Says != null) help.text = $"{key.Legend}  ·  {key.Says}"; });
            button.RegisterCallback<PointerLeaveEvent>(_ => { if (operation == null) help.text = HelpText; });
            key.Button = button;
            boardKeys[control] = key;
            return button;
        }

        private static VisualElement LegendChip(string group)
        {
            var chip = new VisualElement();
            chip.AddToClassList("controls-legend-chip");
            chip.AddToClassList(GroupClass(group));
            var band = new VisualElement();
            band.AddToClassList("board-band");
            chip.Add(band);
            var name = new Label(group);
            name.AddToClassList("controls-legend-name");
            chip.Add(name);
            return chip;
        }

        private static string GroupClass(string group) => "group-" + group.ToLowerInvariant();

        /// <summary>The key of the board a binding is on, or null: the part of a keyboard path after the device.</summary>
        private static string BoardControl(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) return null;
            return path.Substring("<Keyboard>/".Length);
        }

        private static string Short(string label)
        {
            if (ShortNames.TryGetValue(label, out string known)) return known;
            return label.StartsWith("Hotbar ", StringComparison.Ordinal) ? "Slot " + label.Substring("Hotbar ".Length) : label;
        }

        /// <summary>Writes every key as it is now: on the board where its key is there, in the list under it otherwise.</summary>
        private void Paint()
        {
            if (page == null) return;
            keyButtons.Clear();
            foreach (BoardKey key in boardKeys.Values)
            {
                key.Action = null;
                key.Says = null;
                key.Caption.text = string.Empty;
                key.Button.EnableInClassList("bound", false);
                key.Button.EnableInClassList("fixed", false);
                key.Button.RemoveFromClassList("listening");
                foreach (string group in new[] { "WINDOWS", "HOTBAR", "CHARACTER", "FIXED" }) key.Button.RemoveFromClassList(GroupClass(group));
            }
            foreach (var fixedKey in FixedBoardKeys)
            {
                if (!boardKeys.TryGetValue(fixedKey.Control, out BoardKey key)) continue;
                key.Caption.text = fixedKey.Caption;
                key.Says = fixedKey.Says + " (fixed)";
                key.Button.AddToClassList("fixed");
                key.Button.AddToClassList(GroupClass("FIXED"));
            }

            others.Clear();
            bool anyOther = false;
            foreach (var entry in entries)
            {
                string path = entry.Action.bindings.Count > 0 ? entry.Action.bindings[0].effectivePath : null;
                string control = BoardControl(path);
                if (control != null && boardKeys.TryGetValue(control, out BoardKey key) && key.Action == null)
                {
                    // A key the player gave to an action is theirs: it shows the action, not the fixed use it had.
                    key.Action = entry.Action;
                    key.Says = $"{entry.Label} ({entry.Group.ToLowerInvariant()})";
                    key.Caption.text = Short(entry.Label);
                    key.Button.EnableInClassList("fixed", false);
                    key.Button.RemoveFromClassList(GroupClass("FIXED"));
                    key.Button.AddToClassList("bound");
                    key.Button.AddToClassList(GroupClass(entry.Group));
                    keyButtons[entry.Action] = key.Button;
                    continue;
                }
                if (!anyOther)
                {
                    anyOther = true;
                    SettingsRows.Header(others, "NOT ON THE BOARD");
                }
                AddRow(entry.Action, entry.Label);
            }
        }

        /// <summary>An action whose key is not on the board: its key as a cap, then what it does.</summary>
        private void AddRow(InputAction action, string label)
        {
            VisualElement row = SettingsRows.Row(others, label);
            row.RemoveFromClassList("kn-card"); // a bare line, as on the packs' pages: the caps carry it
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

        // ------------------------------------------------------------------ changing a key

        private string LabelOf(InputAction action)
        {
            foreach (var entry in entries)
                if (entry.Action == action) return entry.Label;
            return action.name;
        }

        private void Listen(InputAction action)
        {
            if (operation != null)
            {
                bool same = listeningFor == action;
                Cancel();
                if (same) return;
            }
            if (!keyButtons.TryGetValue(action, out Button button)) return;

            string previousPath = action.bindings[0].effectivePath;
            bool wasEnabled = action.enabled;
            action.Disable(); // an action cannot be rebound while it listens for its own key
            listeningFor = action;
            KeyBindings.Listening = true;
            listeningChanged?.Invoke(true);
            button.AddToClassList("listening");
            help.text = $"Press the key for {LabelOf(action).ToUpperInvariant()}. Esc cancels.";
            help.AddToClassList("listening");

            var rebind = action.PerformInteractiveRebinding(0)
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f);
            foreach (string allowed in KeyBindings.AllowedControls) rebind.WithControlsHavingToMatchPath(allowed);
            foreach (string refused in KeyBindings.RefusedPaths) rebind.WithControlsExcluding(refused);

            void Finish(bool completed)
            {
                operation?.Dispose();
                operation = null;
                listeningFor = null;
                if (wasEnabled) action.Enable();
                button.RemoveFromClassList("listening");
                help.RemoveFromClassList("listening");
                help.text = HelpText;
                if (completed)
                {
                    // An action with a second default key (left and right Shift) keeps only the one just chosen.
                    for (int i = 1; i < action.bindings.Count; i++) action.ApplyBindingOverride(i, string.Empty);
                    // Whatever the listener let through, an action only ever keeps a key it may be on.
                    if (!KeyBindings.IsAllowedPath(action.bindings[0].effectivePath)) action.RemoveBindingOverride(0);
                    GiveAwayKey(action, previousPath);
                    KeyBindings.Save(playerActions());
                }
                Paint();
                ListeningEndedFrame = Time.frameCount;
                KeyBindings.Listening = false;
                listeningChanged?.Invoke(false);
                // The action may be on another key now: the feel plays on the one it ended on.
                MenuMoments.Raise(completed ? MenuMoment.Confirm : MenuMoment.Back, keyButtons.TryGetValue(action, out Button now) ? now : button);
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
                    other.ApplyBindingOverride(i, i == 0 && KeyBindings.IsAllowedPath(previousPath) ? previousPath : string.Empty);
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
            Paint();
        }

        private void ResetAll()
        {
            Cancel();
            KeyBindings.ResetAll(playerActions());
            Paint();
        }
    }
}
