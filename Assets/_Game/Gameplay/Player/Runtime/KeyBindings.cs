using System;
using System.Collections.Generic;
using KryticalNova.Networking;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.Player
{
    /// <summary>
    /// The keys a player may change, and where their choices are kept. Every one is an action of the project's input
    /// asset (Input/Actions/InputSystem_Actions). Two groups: the window keys (Pack, Map, Settings ...) in its
    /// Windows map, read by every HUD controller from the project-wide copy of the asset, which exists in every
    /// scene and before any player does; and the player's own actions (Hotbar slots, Character and Skills windows,
    /// weapon and camera keys), which each player object loads its overrides for when it starts. Overrides are saved
    /// as JSON in PlayerPrefs, per local profile like the HUD preferences. Esc, Enter, Y / N and the mouse buttons
    /// are fixed and are not listed.
    /// </summary>
    public static class KeyBindings
    {
        public const string Pack = "Pack";
        public const string Equipment = "Equipment";
        public const string Map = "Map";
        public const string Settings = "Settings";
        public const string Guide = "Guide";
        public const string Kiosk = "Kiosk";
        public const string Crew = "Crew";
        public const string Contacts = "Contacts";
        public const string PickUp = "PickUp";
        public const string NetworkPanel = "NetworkPanel";
        public const string GmConsole = "GmConsole";
        public const string EmoteWheel = "EmoteWheel";
        public const string Autopilot = "Autopilot";
        public const string AutopilotSettings = "AutopilotSettings";

        /// <summary>A window key: its action in the asset's Windows map, and what the Controls list calls it.</summary>
        public readonly struct WindowKey
        {
            public readonly string Action, Label;
            public WindowKey(string action, string label) { Action = action; Label = label; }
        }

        // The keys themselves are in the asset. An action named here and missing there is reported once.
        public static readonly WindowKey[] WindowKeys =
        {
            new WindowKey(Pack, "Pack"),
            new WindowKey(Equipment, "Equipment"),
            new WindowKey(Map, "Map"),
            new WindowKey(Settings, "Settings"),
            new WindowKey(Guide, "Guide"),
            new WindowKey(Kiosk, "Kiosk"),
            new WindowKey(Crew, "Crew"),
            new WindowKey(Contacts, "Contacts"),
            new WindowKey(PickUp, "Pick up nearest"),
            new WindowKey(NetworkPanel, "Network panel"),
            new WindowKey(GmConsole, "GM console (host)"),
            new WindowKey(EmoteWheel, "Emote wheel (hold)"),
            new WindowKey(Autopilot, "Autopilot on / off"),
            new WindowKey(AutopilotSettings, "Autopilot and Magnet"),
        };

        /// <summary>The player-asset actions the Controls list offers, by map, with the name it shows for each.</summary>
        public static readonly (string Map, string Action, string Label)[] PlayerKeys =
        {
            ("SkillCommands", "Slot1", "Hotbar 1"), ("SkillCommands", "Slot2", "Hotbar 2"), ("SkillCommands", "Slot3", "Hotbar 3"),
            ("SkillCommands", "Slot4", "Hotbar 4"), ("SkillCommands", "Slot5", "Hotbar 5"), ("SkillCommands", "Slot6", "Hotbar 6"),
            ("SkillCommands", "Slot7", "Hotbar 7"), ("SkillCommands", "Slot8", "Hotbar 8"), ("SkillCommands", "Slot9", "Hotbar 9"),
            ("SkillCommands", "Slot10", "Hotbar 10"), ("SkillCommands", "SlotQ", "Hotbar 11"), ("SkillCommands", "SlotW", "Hotbar 12"),
            ("SkillCommands", "SlotE", "Hotbar 13"), ("SkillCommands", "SlotR", "Hotbar 14"),
            ("SkillCommands", "CharacterWindow", "Character Sheet"), ("SkillCommands", "SkillsWindow", "Skills"),
            ("PointAndClick", "ToggleWeapon", "Draw / sheathe weapon"), ("PointAndClick", "FlipCamera", "Flip camera"),
        };

        /// <summary>What a key may be: any keyboard key, or one of the spare mouse buttons. Never the two the game is played with.</summary>
        public static readonly string[] AllowedControls = { "<Keyboard>", "<Mouse>/middleButton", "<Mouse>/forwardButton", "<Mouse>/backButton" };
        /// <summary>Keys that keep their fixed use, and what the game is played with: never given to an action.</summary>
        public static readonly string[] RefusedPaths =
        {
            "<Keyboard>/escape", "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/anyKey",
            "<Mouse>/leftButton", "<Mouse>/rightButton", "<Mouse>/press",
        };

        private const string KeyboardPrefix = "<Keyboard>/";
        private const string WindowMapName = "Windows";
        private static InputActionMap windows;
        private static bool reportedMissing;
        private static readonly string Prefix = BuildPrefix();
        private static string WindowsKey => Prefix + "windows";
        private static string PlayerKey => Prefix + "player";

        private static string BuildPrefix()
        {
            string profile = NetworkConnectionManager.LocalProfile();
            return string.IsNullOrEmpty(profile) ? "kn.input." : "kn.input." + profile + ".";
        }

        /// <summary>A binding changed (or all were reset): key labels on screen should be read again.</summary>
        public static event Action Changed;

        /// <summary>True while the Controls list waits for a key: no window key acts on that press.</summary>
        public static bool Listening { get; set; }

        /// <summary>
        /// True while something that is not part of the HUD has the keyboard (the GM console): window keys do
        /// nothing, and the fixed keys (Enter for chat, Y / N) wait as well.
        /// </summary>
        public static bool KeyboardTaken { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            windows = null; // the asset's own map: looked up again, never disposed
            reportedMissing = false;
            Listening = false;
            KeyboardTaken = false;
            Changed = null;
        }

        /// <summary>
        /// The Windows map of the project-wide input asset, with the player's saved keys on it. Null (and reported
        /// once) when the asset has no such map.
        /// </summary>
        private static InputActionMap Windows
        {
            get
            {
                // The asset can be reimported under a running Editor: a map of a destroyed asset is never pressed.
                if (windows != null && windows.asset == null) windows = null;
                if (windows == null)
                {
                    InputActionAsset asset = InputSystem.actions;
                    windows = asset != null ? asset.FindActionMap(WindowMapName, false) : null;
                    if (windows == null)
                    {
                        Report($"The project-wide input actions have no '{WindowMapName}' map: window keys will not work.");
                        return null;
                    }
                    // The asset outlives a Play session in the Editor: start from what is saved, not from what was left.
                    windows.RemoveAllBindingOverrides();
                    string saved = PlayerPrefs.GetString(WindowsKey, string.Empty);
                    if (!string.IsNullOrEmpty(saved))
                    {
                        TryLoad(windows, saved);
                        if (Clean(windows.actions)) PlayerPrefs.SetString(WindowsKey, windows.SaveBindingOverridesAsJson());
                    }
                }
                // A player object using the same copy of the asset may switch maps off: this one is always on.
                if (!windows.enabled) windows.Enable();
                return windows;
            }
        }

        private static void Report(string message)
        {
            if (reportedMissing) return;
            reportedMissing = true;
            Debug.LogError(message);
        }

        /// <summary>The action of a window key (see the constants above), or null when the asset does not have it.</summary>
        public static InputAction Window(string action)
        {
            InputAction found = Windows?.FindAction(action, false);
            if (found == null && Windows != null) Report($"The '{WindowMapName}' input map has no '{action}' action.");
            return found;
        }

        /// <summary>The window key was pressed this frame. Callers still check that the player is not typing.</summary>
        public static bool Pressed(string action)
        {
            if (Listening || KeyboardTaken) return false;
            InputAction found = Window(action);
            return found != null && found.WasPressedThisFrame();
        }

        /// <summary>The window key is being held down. For keys that work while held (the emote wheel).</summary>
        public static bool Held(string action)
        {
            if (Listening || KeyboardTaken) return false;
            InputAction found = Window(action);
            return found != null && found.IsPressed();
        }

        /// <summary>The key a window action is on, as text for a button or a hint ("I", "F1").</summary>
        public static string Label(string action) => Label(Window(action));

        public static string Label(InputAction action)
        {
            if (action == null || action.bindings.Count == 0) return string.Empty;
            string text = action.GetBindingDisplayString(0, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            return string.IsNullOrEmpty(text) ? "—" : text.ToUpperInvariant();
        }

        /// <summary>Gives a player's own input asset the keys this player chose. Called by the player when it starts.</summary>
        public static void ApplyTo(InputActionAsset playerActions)
        {
            if (playerActions == null) return;
            string saved = PlayerPrefs.GetString(PlayerKey, string.Empty);
            if (string.IsNullOrEmpty(saved)) return;
            TryLoad(playerActions, saved);
            bool cleaned = Clean(playerActions);
            cleaned |= SettleSharedKeys(playerActions);
            if (!cleaned) return;
            PlayerPrefs.SetString(PlayerKey, playerActions.SaveBindingOverridesAsJson());
            if (Windows != null) PlayerPrefs.SetString(WindowsKey, Windows.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
        }

        /// <summary>True for a key an action may be on: a keyboard key or a spare mouse button, and none of the refused ones.</summary>
        public static bool IsAllowedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (string refused in RefusedPaths)
                if (string.Equals(path, refused, StringComparison.OrdinalIgnoreCase)) return false;
            if (path.StartsWith(KeyboardPrefix, StringComparison.OrdinalIgnoreCase)) return path.Length > KeyboardPrefix.Length;
            foreach (string allowed in AllowedControls)
                if (string.Equals(path, allowed, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Takes back every saved key that no action may be on (a mouse button the game is played with, a fixed key,
        /// no key at all), so the action is on its own key again. A second key switched off on purpose (the other
        /// Shift) stays off. True when anything was taken back.
        /// </summary>
        public static bool Clean(IEnumerable<InputAction> actions)
        {
            bool changed = false;
            foreach (InputAction action in actions)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    string chosen = action.bindings[i].overridePath;
                    if (chosen == null) continue; // as shipped
                    if (i > 0 && chosen.Length == 0) continue;
                    if (IsAllowedPath(chosen)) continue;
                    action.RemoveBindingOverride(i);
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Two listed actions on one key: the one that was moved there goes back to its own. True when any did.
        /// </summary>
        private static bool SettleSharedKeys(InputActionAsset playerActions)
        {
            var listed = new List<(InputAction Action, string Label, string Group)>();
            Collect(playerActions, listed);
            bool changed = false;
            var holders = new Dictionary<string, InputAction>(StringComparer.OrdinalIgnoreCase);
            // Each pass can free a key another action then falls back onto: a few passes settle any chain.
            for (int pass = 0; pass < listed.Count; pass++)
            {
                holders.Clear();
                bool moved = false;
                foreach (var entry in listed)
                {
                    InputAction action = entry.Action;
                    if (action.bindings.Count == 0) continue;
                    string path = action.bindings[0].effectivePath;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!holders.TryGetValue(path, out InputAction holder)) { holders[path] = action; continue; }
                    InputAction loser = action.bindings[0].overridePath != null ? action
                        : holder.bindings[0].overridePath != null ? holder : null;
                    if (loser == null) continue; // both as shipped: the asset's own matter
                    loser.RemoveAllBindingOverrides();
                    moved = true;
                }
                if (!moved) break;
                changed = true;
            }
            return changed;
        }

        /// <summary>Every action the Controls list shows, window keys first. <paramref name="playerActions"/> may be null.</summary>
        public static void Collect(InputActionAsset playerActions, List<(InputAction Action, string Label, string Group)> into)
        {
            into.Clear();
            foreach (WindowKey key in WindowKeys)
            {
                InputAction window = Window(key.Action);
                if (window != null) into.Add((window, key.Label, "WINDOWS"));
            }
            if (playerActions == null) return;
            foreach (var key in PlayerKeys)
            {
                InputAction action = playerActions.FindActionMap(key.Map, false)?.FindAction(key.Action, false);
                if (action != null) into.Add((action, key.Label, key.Map == "SkillCommands" && key.Action.StartsWith("Slot") ? "HOTBAR" : "CHARACTER"));
            }
        }

        /// <summary>Keeps the bindings as they are now. Call after a rebind.</summary>
        public static void Save(InputActionAsset playerActions)
        {
            if (Windows != null) PlayerPrefs.SetString(WindowsKey, Windows.SaveBindingOverridesAsJson());
            if (playerActions != null) PlayerPrefs.SetString(PlayerKey, playerActions.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // Between the two halves of a capture; never part of a binding's text.
        private const char CaptureSeparator = '\u001F';

        /// <summary>The keys as they are now, in one string: for putting them back later with <see cref="Restore"/>.</summary>
        public static string Capture(InputActionAsset playerActions)
        {
            string windowKeys = Windows != null ? Windows.SaveBindingOverridesAsJson() : string.Empty;
            string playerKeys = playerActions != null ? playerActions.SaveBindingOverridesAsJson() : PlayerPrefs.GetString(PlayerKey, string.Empty);
            return windowKeys + CaptureSeparator + playerKeys;
        }

        /// <summary>Puts the keys back as <see cref="Capture"/> found them, and keeps them.</summary>
        public static void Restore(string captured, InputActionAsset playerActions)
        {
            int cut = captured != null ? captured.IndexOf(CaptureSeparator) : -1;
            if (cut < 0) return;
            string windowKeys = captured.Substring(0, cut), playerKeys = captured.Substring(cut + 1);
            if (Windows != null)
            {
                Windows.RemoveAllBindingOverrides();
                if (windowKeys.Length > 0) TryLoad(Windows, windowKeys);
                PlayerPrefs.SetString(WindowsKey, windowKeys);
            }
            if (playerActions != null)
            {
                playerActions.RemoveAllBindingOverrides();
                if (playerKeys.Length > 0) TryLoad(playerActions, playerKeys);
            }
            PlayerPrefs.SetString(PlayerKey, playerKeys);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Every key back to the one it shipped on.</summary>
        public static void ResetAll(InputActionAsset playerActions)
        {
            Windows?.RemoveAllBindingOverrides();
            playerActions?.RemoveAllBindingOverrides();
            PlayerPrefs.DeleteKey(WindowsKey);
            PlayerPrefs.DeleteKey(PlayerKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static void TryLoad(IInputActionCollection2 actions, string json)
        {
            try { actions.LoadBindingOverridesFromJson(json); }
            catch (Exception e) { Debug.LogWarning($"Saved key bindings were unreadable and were skipped: {e.Message}"); }
        }
    }
}
