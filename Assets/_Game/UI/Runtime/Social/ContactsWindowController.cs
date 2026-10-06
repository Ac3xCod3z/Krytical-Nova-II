using System;
using System.Collections.Generic;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Contacts window [F]: contacts (favourites first, then whoever is online), requests in both directions,
    /// the players met this session, and the block list. It shows what the server sent
    /// (<see cref="PlayerContacts"/>) and sends requests; the server decides. It also adds the contact entries to
    /// the player menu and keeps <see cref="RecentPlayers"/> fed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ContactsWindowController : MonoBehaviour
    {
        private const int TabContacts = 0, TabRequests = 1, TabRecent = 2, TabBlocked = 3;
        private const float AgeRefreshSeconds = 30f;

        private VisualElement root, window, addRow;
        private ScrollView list;
        private Label countLabel, helpLabel;
        private TextField nameField;
        private Button addButton;
        private readonly Button[] tabs = new Button[4];
        private readonly List<ContactView> shown = new List<ContactView>(32);
        private int tab;
        private string editingNote; // the id of the contact whose note is being typed
        private float nextAgeRefresh;

        private PlayerContacts contacts;
        private PlayerParty party;
        private PlayerTrade trade;
        private PlayerDuel duel;
        private bool initialized;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("contacts-window");
            if (window == null)
            {
                Debug.LogError("[Contacts] SocialPanels.uxml has no contacts-window in this UI document.", this);
                return;
            }
            list = window.Q<ScrollView>("contacts-list");
            countLabel = window.Q<Label>("contacts-count");
            helpLabel = window.Q<Label>("contacts-help");
            addRow = window.Q("contacts-add-row");
            nameField = window.Q<TextField>("contacts-name");
            addButton = window.Q<Button>("contacts-add");
            HudTyping.Guard(nameField);
            nameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
                evt.StopPropagation();
                SubmitName();
            }, TrickleDown.TrickleDown);
            addButton.clicked += SubmitName;
            window.Q<Button>("close-contacts").clicked += () => SetOpen(false);
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                tabs[i] = window.Q<Button>($"contacts-tab-{i}");
                tabs[i].clicked += () => ShowTab(index);
            }
            SetDisplay(false);

            PlayerMenuController.Register("Add Contact", 50, target => contacts != null && !contacts.IsContact(target.Name) && !contacts.IsBlocked(target.Name), AddFromMenu);
            PlayerMenuController.Register("Remove Contact", 55, target => contacts != null && contacts.IsContact(target.Name), target => contacts?.RemoveByName(target.Name), danger: true);
            PlayerMenuController.Register("Block", 90, target => contacts != null && !contacts.IsBlocked(target.Name), target => contacts?.BlockByName(target.Name), danger: true);
            PlayerMenuController.Register("Unblock", 91, target => contacts != null && contacts.IsBlocked(target.Name), target => contacts?.UnblockByName(target.Name));

            PlayerInventory.LocalChanged += Rebind;
            PlayerChat.LineShown += OnChatLine;
            RecentPlayers.Changed += OnRecentChanged;
            initialized = true;
            Rebind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Rebind;
            PlayerChat.LineShown -= OnChatLine;
            RecentPlayers.Changed -= OnRecentChanged;
            Bind(null, null, null, null);
        }

        private void Rebind()
        {
            PlayerInventory local = PlayerInventory.Local;
            if (local == null) { Bind(null, null, null, null); return; }
            Bind(local.GetComponent<PlayerContacts>(), local.GetComponent<PlayerParty>(), local.GetComponent<PlayerTrade>(), local.GetComponent<PlayerDuel>());
        }

        private void Bind(PlayerContacts nextContacts, PlayerParty nextParty, PlayerTrade nextTrade, PlayerDuel nextDuel)
        {
            if (contacts != null) contacts.Changed -= Refresh;
            if (party != null) party.Changed -= NoteSquad;
            if (trade != null) trade.Changed -= NoteTrade;
            if (duel != null) duel.Changed -= NoteDuel;
            contacts = nextContacts;
            party = nextParty;
            trade = nextTrade;
            duel = nextDuel;
            if (contacts != null) contacts.Changed += Refresh;
            if (party != null) party.Changed += NoteSquad;
            if (trade != null) trade.Changed += NoteTrade;
            if (duel != null) duel.Changed += NoteDuel;
            if (initialized) Refresh();
        }

        // ------------------------------------------------------------------ who the player dealt with

        private void OnChatLine(ChatLine line)
        {
            if (line.Channel == ChatChannel.Whisper && !string.IsNullOrEmpty(line.Sender)) RecentPlayers.Note(line.Sender);
        }

        private void NoteSquad()
        {
            string mine = party != null ? party.MemberName : null;
            if (party == null) return;
            foreach (PartyMember member in party.Roster)
                if (!string.Equals(member.Name, mine, StringComparison.OrdinalIgnoreCase)) RecentPlayers.Note(member.Name);
        }

        private void NoteTrade()
        {
            if (trade != null && trade.IsTrading) RecentPlayers.Note(trade.PartnerName);
        }

        private void NoteDuel()
        {
            if (duel == null || !duel.IsDuelling) return;
            foreach (PlayerDuel other in FindObjectsByType<PlayerDuel>(FindObjectsInactive.Exclude))
                if (other != duel && duel.IsDuellingWith(other.NetworkObject)) RecentPlayers.Note(other.DuelistName);
        }

        private void OnRecentChanged()
        {
            if (initialized && IsOpen && tab == TabRecent) Refresh();
        }

        // ------------------------------------------------------------------ the window

        public bool IsOpen => initialized && window.style.display == DisplayStyle.Flex;

        private void SetDisplay(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
        }

        public void SetOpen(bool open)
        {
            if (!initialized || open == IsOpen) return;
            SetDisplay(open);
            if (open)
            {
                window.BringToFront();
                Refresh();
            }
            else
            {
                editingNote = null;
                nameField.Blur();
            }
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && !HudTyping.IsTyping(root))
            {
                if (keyboard.fKey.wasPressedThisFrame) SetOpen(!IsOpen);
                else if (keyboard.escapeKey.wasPressedThisFrame && IsOpen) SetOpen(false);
            }
            if (!IsOpen || Time.unscaledTime < nextAgeRefresh || editingNote != null) return;
            Refresh(); // "5m ago" moves on
        }

        private void ShowTab(int index)
        {
            tab = Mathf.Clamp(index, 0, tabs.Length - 1);
            editingNote = null;
            Refresh();
        }

        private void SubmitName()
        {
            string typed = (nameField.value ?? string.Empty).Trim();
            if (typed.Length == 0 || contacts == null) return;
            if (tab == TabBlocked) contacts.BlockByName(typed);
            else contacts.Add(typed);
            nameField.SetValueWithoutNotify(string.Empty);
        }

        private void AddFromMenu(PlayerMenuTarget target)
        {
            if (contacts == null) return;
            if (target.InSight && target.Player.TryGetComponent(out PlayerContacts theirs)) contacts.Add(theirs);
            else contacts.Add(target.Name);
        }

        private void Refresh()
        {
            if (!initialized) return;
            nextAgeRefresh = Time.unscaledTime + AgeRefreshSeconds;
            int total = 0, online = 0, incoming = 0, outgoing = 0, blocked = 0;
            if (contacts != null)
                foreach (ContactView line in contacts.Lines)
                {
                    switch (line.kind)
                    {
                        case ContactKind.Contact: total++; if (line.online) online++; break;
                        case ContactKind.Incoming: incoming++; break;
                        case ContactKind.Outgoing: outgoing++; break;
                        case ContactKind.Blocked: blocked++; break;
                    }
                }
            countLabel.text = $"{online} ONLINE  ·  {total} / {ContactRules.MaxContacts}";
            tabs[TabRequests].text = incoming > 0 ? $"REQUESTS ({incoming})" : "REQUESTS";
            for (int i = 0; i < tabs.Length; i++) tabs[i].EnableInClassList("selected", i == tab);
            if (!IsOpen) return;

            addRow.style.display = tab == TabContacts || tab == TabBlocked ? DisplayStyle.Flex : DisplayStyle.None;
            addButton.text = tab == TabBlocked ? "BLOCK" : "ADD";
            list.Clear();
            if (contacts == null || !contacts.IsSpawned)
            {
                helpLabel.text = "Contacts need an online session.";
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            int rows = 0;
            if (tab == TabRecent)
            {
                foreach (string who in RecentPlayers.All) { list.Add(RecentRow(who)); rows++; }
                helpLabel.text = rows > 0 ? "Players you were in a Squad with, traded with, called out or whispered since you started the game."
                    : "Nobody yet. Squad mates, trade partners, Callout opponents and whisperers show up here.";
                return;
            }

            shown.Clear();
            foreach (ContactView line in contacts.Lines)
            {
                bool wanted = tab == TabContacts ? line.kind == ContactKind.Contact
                    : tab == TabRequests ? line.kind == ContactKind.Incoming || line.kind == ContactKind.Outgoing
                    : line.kind == ContactKind.Blocked;
                if (wanted) shown.Add(line);
            }
            shown.Sort(Compare);
            foreach (ContactView line in shown) { list.Add(Row(line, now)); rows++; }

            helpLabel.text = tab == TabContacts
                ? (rows > 0 ? "Click the diamond to pin a favourite  ·  Click a note to change it  ·  MORE for whisper, Squad, trade and block"
                    : "Type a character's name and press ADD, or Shift+click a player and choose Add Contact. They have to accept.")
                : tab == TabRequests
                    ? (rows > 0 ? "A request waits until it is answered, even while you or they are offline." : "No requests waiting.")
                    : (rows > 0 ? "Blocked characters cannot whisper, invite, trade with or send requests to you; nor you to them."
                        : "Nobody is blocked. Type a name and press BLOCK, or use the player menu.");
        }

        /// <summary>Requests to answer first; favourites, then whoever is online, then by name.</summary>
        private static int Compare(ContactView a, ContactView b)
        {
            int c = a.kind.CompareTo(b.kind);
            if (c != 0) return c;
            c = b.favourite.CompareTo(a.favourite);
            if (c != 0) return c;
            c = b.online.CompareTo(a.online);
            return c != 0 ? c : string.Compare(a.name.ToString(), b.name.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ rows

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            return label;
        }

        private static Button Action(string text, Action run, bool danger = false, bool primary = false)
        {
            var button = new Button(run) { text = text };
            button.AddToClassList("contact-button");
            if (danger) button.AddToClassList("kn-button-danger");
            if (primary) button.AddToClassList("kn-button-primary");
            return button;
        }

        private static VisualElement Spacer()
        {
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList("social-spacer");
            return spacer;
        }

        private VisualElement Row(ContactView line, long now)
        {
            string id = line.id.ToString();
            string who = line.name.ToString();
            var row = new VisualElement();
            row.AddToClassList("contact-row");
            var top = new VisualElement();
            top.AddToClassList("contact-top");
            row.Add(top);

            if (line.kind == ContactKind.Contact)
            {
                row.AddToClassList(line.online ? "online" : "offline");
                var favourite = new VisualElement { tooltip = line.favourite ? "Favourite" : "Make a favourite" };
                favourite.AddToClassList("contact-fav");
                favourite.EnableInClassList("on", line.favourite);
                bool next = !line.favourite;
                favourite.RegisterCallback<ClickEvent>(_ => contacts?.SetFavourite(id, next));
                top.Add(favourite);
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("contact-dot");
                top.Add(dot);
                top.Add(Text(who, "contact-name"));
                top.Add(Text(Detail(line), "contact-detail"));
                top.Add(Spacer());
                top.Add(Text(ContactRules.LastSeen(line.online, now, line.lastSeenUtc), "contact-status"));
                if (line.online) top.Add(Action("WHISPER", () => Whisper(who)));
                Button more = null;
                more = Action("MORE", () => OpenMenu(who, more));
                top.Add(more);
                row.Add(NoteLine(id, line.note.ToString()));
                return row;
            }

            top.Add(Text(who, "contact-name"));
            switch (line.kind)
            {
                case ContactKind.Incoming:
                    top.Add(Text($"wants to add you  ·  {Detail(line)}", "contact-detail"));
                    top.Add(Spacer());
                    top.Add(Action("ACCEPT", () => contacts?.Respond(id, true), primary: true));
                    top.Add(Action("DECLINE", () => contacts?.Respond(id, false)));
                    break;
                case ContactKind.Outgoing:
                    top.Add(Text("has not answered yet", "contact-detail"));
                    top.Add(Spacer());
                    top.Add(Action("CANCEL", () => contacts?.CancelRequest(id)));
                    break;
                default:
                    top.Add(Spacer());
                    top.Add(Action("UNBLOCK", () => contacts?.Unblock(id)));
                    break;
            }
            return row;
        }

        private static string Detail(ContactView line)
        {
            var weapon = (WeaponClassId)line.weaponClass;
            return weapon != WeaponClassId.None ? $"Lv {line.level}  ·  {WeaponClasses.DisplayName(weapon)}" : $"Lv {line.level}";
        }

        private VisualElement RecentRow(string who)
        {
            var row = new VisualElement();
            row.AddToClassList("contact-row");
            var top = new VisualElement();
            top.AddToClassList("contact-top");
            row.Add(top);
            top.Add(Text(who, "contact-name"));
            bool isContact = contacts != null && contacts.IsContact(who);
            if (isContact) top.Add(Text("a contact", "contact-detail"));
            top.Add(Spacer());
            if (!isContact && (contacts == null || !contacts.IsBlocked(who))) top.Add(Action("ADD", () => contacts?.Add(who), primary: true));
            Button more = null;
            more = Action("MORE", () => OpenMenu(who, more));
            top.Add(more);
            return row;
        }

        /// <summary>The owner's private note: click it to type, Enter (or clicking away) keeps it, Escape leaves it.</summary>
        private VisualElement NoteLine(string id, string note)
        {
            if (editingNote == id)
            {
                var field = new TextField { value = note, maxLength = ContactRules.MaxNoteLength };
                field.AddToClassList("kn-textfield");
                field.AddToClassList("contact-note-field");
                HudTyping.Guard(field);
                bool done = false;
                void Finish(bool keep)
                {
                    if (done) return;
                    done = true;
                    editingNote = null;
                    if (keep && contacts != null && field.value != note) contacts.SetNote(id, field.value);
                    field.Blur();
                    Refresh();
                }
                field.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { evt.StopPropagation(); Finish(true); }
                    else if (evt.keyCode == KeyCode.Escape) { evt.StopPropagation(); Finish(false); }
                }, TrickleDown.TrickleDown);
                field.RegisterCallback<FocusOutEvent>(_ => Finish(true));
                field.schedule.Execute(() => field.Focus());
                return field;
            }

            var label = new Label(note.Length > 0 ? note : "Add a note") { enableRichText = false };
            label.AddToClassList("contact-note");
            label.RegisterCallback<ClickEvent>(_ =>
            {
                editingNote = id;
                Refresh();
            });
            return label;
        }

        private void Whisper(string who)
        {
            var chat = GetComponent<ChatWindowController>();
            if (chat != null) chat.BeginWhisper(who);
        }

        /// <summary>The player menu for a name in the list: with their player when they are in sight, by name otherwise.</summary>
        private void OpenMenu(string who, VisualElement anchor)
        {
            if (PlayerMenuController.Instance == null) return;
            NetworkObject player = null;
            foreach (PlayerIdentity identity in FindObjectsByType<PlayerIdentity>(FindObjectsInactive.Exclude))
            {
                if (!identity.IsSpawned || !string.Equals(identity.CharacterName, who, StringComparison.OrdinalIgnoreCase)) continue;
                player = identity.NetworkObject;
                break;
            }
            Rect at = anchor != null ? anchor.worldBound : window.worldBound;
            PlayerMenuController.Instance.Open(new PlayerMenuTarget(who, player), new Vector2(at.xMin, at.yMax));
        }
    }
}
