using System;
using System.Collections.Generic;
using KryticalNova.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The Servers window (online step 4, the owner's layout A): a row per server from <see cref="ServerDirectory"/>
    /// with how it is (ONLINE, OFF, UPDATE, FULL), its players and response time, and LAST PLAYED on the one Enter
    /// joins. Players only choose: the list is the owner's, kept on the server's control page. JOIN (or Enter) remembers
    /// the choice and enters the world on it. It lives in the menus' document beside Settings, so it wears the same skin
    /// and follows the Menu Theme.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuServers : MonoBehaviour
    {
        private VisualElement root, window, shade, list;
        private Label message, empty;
        private Button join;
        private MenuSettings settings;
        private readonly List<ServerEntry> shown = new List<ServerEntry>();
        private int selected;
        private Action<ServerEntry> onJoin;
        private bool decorated;

        /// <summary>The menus' Servers window, or null in a scene without one.</summary>
        public static MenuServers Current { get; private set; }

        public static bool IsAnyOpen => Current != null && Current.IsOpen;

        public bool IsOpen => window != null && window.resolvedStyle.display != DisplayStyle.None;

        /// <summary>The Servers window of this scene's menu document (it rides on the menus' Settings object).</summary>
        public static MenuServers For(MenuSettings settings)
        {
            if (settings == null) return null;
            if (!settings.TryGetComponent(out MenuServers servers)) servers = settings.gameObject.AddComponent<MenuServers>();
            return servers;
        }

        private void OnEnable()
        {
            Current = this;
            settings = GetComponent<MenuSettings>();
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("servers-window");
            if (window == null)
            {
                Debug.LogError("MenuServers needs a 'servers-window' element in the menus' document (MenuSettings.uxml).", this);
                return;
            }
            shade = root.Q("servers-shade");
            list = root.Q("servers-list");
            message = root.Q<Label>("servers-message");
            empty = root.Q<Label>("servers-empty");
            join = root.Q<Button>("servers-join");
            Button close = root.Q<Button>("close-servers");
            if (close != null) close.clicked += Close;
            if (join != null) join.clicked += JoinSelected;
            Button refresh = root.Q<Button>("servers-refresh");
            if (refresh != null) refresh.clicked += ServerDirectory.Refresh;
            ServerDirectory.Changed += Fill;
            Show(false);
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            ServerDirectory.Changed -= Fill;
        }

        /// <summary>
        /// Opens the window; <paramref name="joining"/> runs with the server chosen by JOIN (or Enter). A
        /// <paramref name="note"/> (why the last join failed) is shown on top.
        /// </summary>
        public void Open(Action<ServerEntry> joining, string note = null)
        {
            if (window == null) return;
            onJoin = joining;
            if (!decorated)
            {
                decorated = true;
                SyntySkin.Decorate(window.parent);
            }
            if (settings != null) settings.PrepareForWindow();
            SetMessage(note);
            Fill();
            ServerDirectory.Refresh();
            Show(true);
            MenuMoments.Raise(MenuMoment.Open, window);
        }

        public void Close()
        {
            if (!IsOpen) return;
            Show(false);
            onJoin = null;
            MenuMoments.Raise(MenuMoment.Back);
        }

        private void Update()
        {
            if (!IsOpen) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) JoinSelected();
            else if (keyboard.upArrowKey.wasPressedThisFrame) Select(selected - 1);
            else if (keyboard.downArrowKey.wasPressedThisFrame) Select(selected + 1);
        }

        private void Show(bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            window.style.display = display;
            if (shade != null) shade.style.display = display;
        }

        private void SetMessage(string note)
        {
            if (message == null) return;
            message.text = note ?? string.Empty;
            message.EnableInClassList("shown", !string.IsNullOrEmpty(note));
        }

        // ------------------------------------------------------------------ rows

        private void Fill()
        {
            if (list == null) return;
            string selectedKey = selected >= 0 && selected < shown.Count ? shown[selected].Key : ServerDirectory.LastKeyChosen;
            string lastKey = ServerDirectory.LastKeyChosen;
            shown.Clear();
            shown.AddRange(ServerDirectory.Entries);
            list.Clear();
            if (empty != null)
            {
                empty.style.display = shown.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
                empty.text = ServerDirectory.Fetched
                    ? "No servers are listed yet. A server's owner puts it on the list from its control page; press REFRESH to look again."
                    : "Looking for servers...";
            }
            selected = 0;
            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i].Key == selectedKey) selected = i;
                list.Add(Row(shown[i], i, shown[i].Key == lastKey));
            }
            Select(selected);
        }

        private VisualElement Row(ServerEntry entry, int index, bool last)
        {
            var row = new VisualElement();
            row.AddToClassList("servers-row");
            row.AddToClassList("kn-card");
            var main = new VisualElement();
            main.AddToClassList("servers-row-main");
            var line = new VisualElement();
            line.AddToClassList("servers-row-line");
            line.Add(Status(entry.Status));
            var name = new Label(entry.Name);
            name.AddToClassList("servers-name");
            name.AddToClassList("kn-bold");
            line.Add(name);
            if (last)
            {
                var tag = new Label("LAST PLAYED");
                tag.AddToClassList("servers-last");
                line.Add(tag);
            }
            main.Add(line);
            var note = new Label(entry.Note);
            note.AddToClassList("servers-meta");
            if (entry.Status == ServerEntry.State.Update || entry.Status == ServerEntry.State.Full) note.AddToClassList("servers-meta--warn");
            main.Add(note);
            row.Add(main);
            bool known = entry.Status == ServerEntry.State.Online || entry.Status == ServerEntry.State.Full || entry.Status == ServerEntry.State.Update;
            row.Add(Number(known ? $"{entry.Players} / {entry.Max}" : "-"));
            row.Add(Number(known && entry.Milliseconds >= 0 ? $"{entry.Milliseconds} ms" : "-"));
            row.RegisterCallback<ClickEvent>(e =>
            {
                Select(index);
                if (e.clickCount >= 2) JoinSelected();
            });
            return row;
        }

        private static Label Status(ServerEntry.State state)
        {
            string text, kind;
            switch (state)
            {
                case ServerEntry.State.Online: text = "ONLINE"; kind = "online"; break;
                case ServerEntry.State.Full: text = "FULL"; kind = "update"; break;
                case ServerEntry.State.Update: text = "UPDATE"; kind = "update"; break;
                case ServerEntry.State.Off: text = "OFF"; kind = "off"; break;
                case ServerEntry.State.Here: text = "HERE"; kind = "off"; break;
                case ServerEntry.State.Relay: text = "RELAY"; kind = "online"; break;
                default: text = "..."; kind = "off"; break;
            }
            var label = new Label(text);
            label.AddToClassList("servers-status");
            label.AddToClassList("servers-status--" + kind);
            return label;
        }

        private static Label Number(string text)
        {
            var label = new Label(text);
            label.AddToClassList("servers-num");
            return label;
        }

        private void Select(int index)
        {
            if (shown.Count == 0)
            {
                if (join != null) join.SetEnabled(false);
                return;
            }
            selected = Mathf.Clamp(index, 0, shown.Count - 1);
            for (int i = 0; i < list.childCount; i++) list[i].EnableInClassList("selected", i == selected);
            if (join != null) join.SetEnabled(shown[selected].CanJoin);
        }

        private void JoinSelected()
        {
            if (selected < 0 || selected >= shown.Count) return;
            ServerEntry entry = shown[selected];
            if (!entry.CanJoin)
            {
                SetMessage(entry.Status == ServerEntry.State.Off ? $"{entry.Name} is not answering. It may be off, or not reachable from here." : entry.Note);
                return;
            }
            ServerDirectory.Remember(entry);
            Action<ServerEntry> joining = onJoin;
            Close();
            joining?.Invoke(entry);
        }
    }
}
