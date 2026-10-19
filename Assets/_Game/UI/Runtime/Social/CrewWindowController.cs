using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using KryticalNova.Wallet;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Crew window [G]: the roster with rank, title and who is online, the announcement, and what the player
    /// may do by rank. Opened at a Vaultkeeper by a player with no Crew it is the founding form. It shows what the
    /// server sent (<see cref="PlayerCrew"/>) and sends requests; the server decides. Also adds the Crew entries to
    /// the player menu.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CrewWindowController : MonoBehaviour
    {
        private const string ConfirmPrompt = "crew-confirm";
        private const float AgeRefreshSeconds = 30f;

        private enum Edit { None, Invite, Title, Announcement }

        private VisualElement root, window, none, some, found, symbolHeader, symbolChoices, symbolEdit, editRow;
        private Label titleLabel, countLabel, noneText, announcement, editLabel, helpLabel;
        private TextField nameField, editField;
        private ScrollView list;
        private Button foundButton, inviteButton, announceButton, symbolButton, leaveButton;
        private readonly List<CrewMemberView> shown = new List<CrewMemberView>(32);

        private PlayerCrew crew;
        private Edit editing;
        private string editingMember;
        private int chosenSymbol, chosenColour;
        private VisualElement colourChoices, colourEdit;
        private bool atRegistry;
        private bool pickingSymbol;
        private float nextAgeRefresh;
        private bool initialized;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("crew-window");
            if (window == null)
            {
                Debug.LogError("[Crew] SocialPanels.uxml has no crew-window in this UI document.", this);
                return;
            }
            none = window.Q("crew-none");
            some = window.Q("crew-some");
            found = window.Q("crew-found");
            symbolHeader = window.Q("crew-symbol");
            symbolChoices = window.Q("crew-symbols");
            symbolEdit = window.Q("crew-symbols-edit");
            editRow = window.Q("crew-edit");
            titleLabel = window.Q<Label>("crew-title");
            countLabel = window.Q<Label>("crew-count");
            noneText = window.Q<Label>("crew-none-text");
            announcement = window.Q<Label>("crew-announcement");
            editLabel = window.Q<Label>("crew-edit-label");
            helpLabel = window.Q<Label>("crew-help");
            nameField = window.Q<TextField>("crew-name");
            editField = window.Q<TextField>("crew-edit-field");
            list = window.Q<ScrollView>("crew-list");
            foundButton = window.Q<Button>("crew-found-button");
            inviteButton = window.Q<Button>("crew-invite");
            announceButton = window.Q<Button>("crew-announce");
            symbolButton = window.Q<Button>("crew-symbol-button");
            leaveButton = window.Q<Button>("crew-leave");
            HudTyping.Guard(nameField);
            HudTyping.Guard(editField);
            editField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) { evt.StopPropagation(); CommitEdit(); }
                else if (evt.keyCode == KeyCode.Escape) { evt.StopPropagation(); BeginEdit(Edit.None); }
            }, TrickleDown.TrickleDown);

            window.Q<Button>("close-crew").clicked += () => SetOpen(false);
            window.Q<Button>("crew-edit-ok").clicked += CommitEdit;
            window.Q<Button>("crew-edit-cancel").clicked += () => BeginEdit(Edit.None);
            foundButton.clicked += Found;
            inviteButton.clicked += () => BeginEdit(Edit.Invite);
            announceButton.clicked += () => BeginEdit(Edit.Announcement);
            symbolButton.clicked += () => { pickingSymbol = !pickingSymbol; Refresh(); };
            leaveButton.clicked += LeaveOrDisband;
            SetDisplay(window, false);

            NpcServices.Register(ServiceIds.CrewRegistry, NpcOffer.CrewRegistry, "CREW REGISTRY", OpenRegistry);
            PlayerMenuController.Register("Crew Invite", 60, CanInviteTarget, InviteFromMenu);
            PlayerMenuController.Register("Make Officer", 61, target => LeaderOf(target, out CrewMemberView m) && m.rank == CrewRank.Member, target => WithMember(target, m => crew.SetOfficer(m.id.ToString(), true)));
            PlayerMenuController.Register("Remove Officer", 62, target => LeaderOf(target, out CrewMemberView m) && m.rank == CrewRank.Officer, target => WithMember(target, m => crew.SetOfficer(m.id.ToString(), false)));
            PlayerMenuController.Register("Set Crew Title", 63, target => LeaderOf(target, out _), target => WithMember(target, m => { SetOpen(true); BeginEdit(Edit.Title, m); }));
            PlayerMenuController.Register("Hand Over Crew", 64, target => LeaderOf(target, out _),
                target => WithMember(target, m => Confirm($"Hand the Crew to {m.name}? You become an Officer.", () => crew.HandOver(m.id.ToString()))), danger: true);
            PlayerMenuController.Register("Expel from Crew", 65, CanExpelTarget,
                target => WithMember(target, m => Confirm($"Expel {m.name} from the Crew?", () => crew.Expel(m.id.ToString()))), danger: true);

            PlayerInventory.LocalChanged += Rebind;
            initialized = true;
            Rebind();
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Rebind;
            NpcServices.Unregister(ServiceIds.CrewRegistry);
            Bind(null);
        }

        private void Rebind() => Bind(PlayerCrew.Local);

        private void Bind(PlayerCrew next)
        {
            if (crew == next) return;
            if (crew != null) crew.Changed -= Refresh;
            crew = next;
            if (crew != null) crew.Changed += Refresh;
            symbolsOf = (FactionId)255; // another character: its faction's symbols are laid out when next shown
            if (initialized) Refresh();
        }

        private CrewSymbolLibrary Library => crew != null && crew.Settings != null ? crew.Settings.symbols : null;

        /// <summary>The faction whose symbols the window shows: the local character's (a Crew is of one faction).</summary>
        private FactionId MyFaction => crew != null && crew.TryGetComponent(out PlayerIdentity identity) ? identity.Faction : FactionId.None;

        // The two rows of symbols to choose from are laid out for the character's faction, once it is known.
        private FactionId symbolsOf = (FactionId)255;

        private void EnsureSymbols()
        {
            FactionId faction = MyFaction;
            if (faction == symbolsOf || symbolChoices == null || symbolEdit == null) return;
            symbolsOf = faction;
            BuildSymbols(symbolChoices, index => { chosenSymbol = index; Refresh(); });
            // The Leader's changes are sent as they are made: the symbol and its colour are one choice.
            BuildSymbols(symbolEdit, index => crew?.SetSymbol(index, crew.Header.colour));
            colourChoices = BuildColours(symbolChoices, index => { chosenColour = index; Refresh(); });
            colourEdit = BuildColours(symbolEdit, index => crew?.SetSymbol(crew.Header.symbol, index));
        }

        // A row of swatches under a row of symbols: the colour the symbol is drawn in.
        private VisualElement BuildColours(VisualElement under, Action<int> pick)
        {
            VisualElement row = under.parent.Q(under.name + "-colours");
            if (row == null)
            {
                row = new VisualElement { name = under.name + "-colours" };
                row.AddToClassList("row");
                row.AddToClassList("crew-colours");
                under.parent.Insert(under.parent.IndexOf(under) + 1, row);
            }
            row.Clear();
            Color[] colours = crew != null && crew.Settings != null ? crew.Settings.symbolColours : null;
            for (int i = 0; colours != null && i < colours.Length; i++)
            {
                int index = i;
                var swatch = new VisualElement();
                swatch.AddToClassList("crew-colour-choice");
                swatch.style.backgroundColor = colours[i];
                swatch.RegisterCallback<ClickEvent>(_ => pick(index));
                row.Add(swatch);
            }
            return row;
        }

        private void ShowChoice(VisualElement symbols, VisualElement colours, int symbol, int colour)
        {
            CrewSettings settings = crew != null ? crew.Settings : null;
            for (int i = 0; i < symbols.childCount; i++)
            {
                symbols[i].EnableInClassList("selected", i == symbol);
                // Every symbol is shown in the colour chosen, so the choice is seen before it is made.
                symbols[i].style.unityBackgroundImageTintColor = settings != null ? settings.SymbolColour(colour) : Color.white;
            }
            for (int i = 0; colours != null && i < colours.childCount; i++) colours[i].EnableInClassList("selected", i == colour);
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            element.EnableInClassList("visible", visible);
        }

        private void BuildSymbols(VisualElement into, Action<int> pick)
        {
            into.Clear();
            int count = crew != null && crew.Settings != null ? crew.Settings.symbolCount : 0;
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var choice = new VisualElement();
                choice.AddToClassList("crew-symbol-choice");
                CrewSymbols.Apply(choice, Library, symbolsOf, index);
                choice.RegisterCallback<ClickEvent>(_ => pick(index));
                into.Add(choice);
            }
        }

        // ------------------------------------------------------------------ the player menu

        private CrewRank MyRank => crew != null && crew.InCrew ? crew.Header.myRank : CrewRank.Member;

        /// <summary>The target is in the local player's Crew, and is not the local player.</summary>
        private bool MemberOf(PlayerMenuTarget target, out CrewMemberView member)
        {
            member = default;
            return crew != null && crew.InCrew && crew.TryFindMember(target.Name, out member)
                   && !string.Equals(member.id.ToString(), crew.CharacterId, StringComparison.Ordinal);
        }

        private bool LeaderOf(PlayerMenuTarget target, out CrewMemberView member) => MemberOf(target, out member) && MyRank == CrewRank.Leader;

        private bool CanExpelTarget(PlayerMenuTarget target) => MemberOf(target, out CrewMemberView member) && CrewRules.CanExpel(MyRank, member.rank);

        private bool CanInviteTarget(PlayerMenuTarget target) =>
            crew != null && crew.InCrew && CrewRules.CanInvite(MyRank) && !crew.TryFindMember(target.Name, out _)
            && PlayerMenuController.MayJoinMine(target);

        private void WithMember(PlayerMenuTarget target, Action<CrewMemberView> act)
        {
            if (MemberOf(target, out CrewMemberView member)) act(member);
        }

        private void InviteFromMenu(PlayerMenuTarget target)
        {
            if (crew == null) return;
            if (target.InSight && target.Player.TryGetComponent(out PlayerCrew theirs)) crew.Invite(theirs);
            else crew.Invite(target.Name);
        }

        private static void Confirm(string question, Action yes)
        {
            IRequestPromptSink prompts = ClientPresentationRegistry.Prompts;
            if (prompts == null) { yes(); return; }
            prompts.Show(ConfirmPrompt, "CREW", question, 20f, accepted => { if (accepted) yes(); });
        }

        // ------------------------------------------------------------------ the window

        public bool IsOpen => initialized && window.style.display == DisplayStyle.Flex;

        public void SetOpen(bool open)
        {
            if (!initialized || open == IsOpen) return;
            SetDisplay(window, open);
            if (open)
            {
                window.BringToFront();
                Refresh();
            }
            else
            {
                atRegistry = false;
                pickingSymbol = false;
                editing = Edit.None;
                nameField.Blur();
                editField.Blur();
            }
            GearUIController.Instance?.SuppressWorldInput();
        }

        /// <summary>Opened from the Vaultkeeper: a player with no Crew gets the founding form.</summary>
        private void OpenRegistry()
        {
            atRegistry = true;
            if (IsOpen) Refresh(); else SetOpen(true);
        }

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && !HudTyping.IsTyping(root))
            {
                if (KeyBindings.Pressed(KeyBindings.Crew)) SetOpen(!IsOpen);
                else if (keyboard.escapeKey.wasPressedThisFrame && IsOpen) SetOpen(false);
            }
            if (!IsOpen) return;
            // The founding form belongs to the Vaultkeeper's counter.
            if (atRegistry && crew != null && !crew.InCrew && !NpcInteractable.IsOffered(NpcOffer.CrewRegistry, crew.transform.position, 3.5f))
            {
                atRegistry = false;
                Refresh();
            }
            if (Time.unscaledTime >= nextAgeRefresh && editing == Edit.None) Refresh();
        }

        private void Refresh()
        {
            if (!initialized) return;
            nextAgeRefresh = Time.unscaledTime + AgeRefreshSeconds;
            if (!IsOpen) return;
            EnsureSymbols();
            bool inCrew = crew != null && crew.InCrew;
            SetDisplay(none, !inCrew);
            SetDisplay(some, inCrew);
            SetDisplay(symbolHeader, inCrew);
            if (!inCrew) ShowNoCrew();
            else ShowCrew();
        }

        private void ShowNoCrew()
        {
            titleLabel.text = "CREW";
            countLabel.text = string.Empty;
            CrewSettings settings = crew != null ? crew.Settings : null;
            bool online = crew != null && crew.IsSpawned;
            SetDisplay(found, atRegistry && online && settings != null);
            if (!online) { noneText.text = "Crews need an online session."; helpLabel.text = string.Empty; return; }

            long cost = settings != null ? settings.foundingCost : 0;
            int needLevel = settings != null ? settings.founderLevel : 1;
            long creds = crew.Wallet != null ? crew.Wallet.Creds : 0;
            string terms = $"Founding a Crew takes level {needLevel} and {WalletRules.FormatCreds(cost)}.";
            if (atRegistry)
            {
                bool ready = crew.Level >= needLevel && creds >= cost;
                noneText.text = $"{terms} You are level {crew.Level} and carry {WalletRules.Format(creds)}.";
                foundButton.SetEnabled(ready);
                foundButton.text = ready ? $"FOUND CREW  ·  {WalletRules.Format(cost)}" : "FOUND CREW";
                ShowChoice(symbolChoices, colourChoices, chosenSymbol, chosenColour);
                helpLabel.text = "A Crew belongs to your faction. You lead it: invite players from the player menu (Shift+click them).";
            }
            else
            {
                noneText.text = $"You are in no Crew. {terms} Register one with a Vaultkeeper, or accept a Crew's invitation.";
                helpLabel.text = string.Empty;
            }
        }

        private void ShowCrew()
        {
            CrewHeader header = crew.Header;
            CrewRank rank = header.myRank;
            titleLabel.text = header.name.ToString().ToUpperInvariant();
            CrewSymbols.Apply(symbolHeader, crew.Settings, MyFaction, header.symbol, header.colour);
            string text = header.announcement.ToString();
            announcement.text = text.Length > 0 ? text : "Nothing announced.";

            int online = 0;
            shown.Clear();
            foreach (CrewMemberView member in crew.Roster)
            {
                shown.Add(member);
                if (member.online) online++;
            }
            shown.Sort((a, b) => CrewRules.Compare(a.rank, a.online, a.name.ToString(), b.rank, b.online, b.name.ToString()));
            countLabel.text = $"{online} ONLINE  ·  {shown.Count} / {header.memberCap}";

            list.Clear();
            long now = DateTime.UtcNow.Ticks;
            foreach (CrewMemberView member in shown) list.Add(Row(member, now));

            SetDisplay(inviteButton, CrewRules.CanInvite(rank));
            SetDisplay(announceButton, CrewRules.CanAnnounce(rank));
            SetDisplay(symbolButton, rank == CrewRank.Leader);
            SetDisplay(symbolEdit, pickingSymbol && rank == CrewRank.Leader);
            SetDisplay(colourEdit, pickingSymbol && rank == CrewRank.Leader);
            ShowChoice(symbolEdit, colourEdit, header.symbol, header.colour);
            leaveButton.text = rank == CrewRank.Leader && shown.Count > 1 ? "DISBAND" : "LEAVE";
            SetDisplay(editRow, editing != Edit.None);
            helpLabel.text = rank == CrewRank.Leader
                ? "MORE on a member: Officer, Crew Title, hand over, expel. Talk to the Crew with /g."
                : rank == CrewRank.Officer ? "You can invite, expel Members and write the announcement. Talk to the Crew with /g."
                : "Talk to the Crew with /g. LEAVE takes you out of it.";
        }

        private VisualElement Row(CrewMemberView member, long now)
        {
            string who = member.name.ToString();
            var row = new VisualElement();
            row.AddToClassList("contact-row");
            row.AddToClassList(member.online ? "online" : "offline");
            var top = new VisualElement();
            top.AddToClassList("contact-top");
            row.Add(top);

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("contact-dot");
            top.Add(dot);
            var rank = new Label(CrewRules.RankName(member.rank).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            rank.AddToClassList("crew-rank");
            rank.AddToClassList($"crew-rank-{(int)member.rank}");
            top.Add(rank);
            top.Add(Text(who, "contact-name"));
            if (member.title.Length > 0) top.Add(Text(member.title.ToString(), "crew-member-title"));
            var weapon = (WeaponClassId)member.weaponClass;
            top.Add(Text(weapon != WeaponClassId.None ? $"Lv {member.level}  ·  {WeaponClasses.DisplayName(weapon)}" : $"Lv {member.level}", "contact-detail"));
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.AddToClassList("social-spacer");
            top.Add(spacer);
            top.Add(Text(ContactRules.LastSeen(member.online, now, member.lastSeenUtc), "contact-status"));

            if (string.Equals(member.id.ToString(), crew.CharacterId, StringComparison.Ordinal)) return row;
            Button more = null;
            more = new Button(() => OpenMenu(who, more)) { text = "MORE" };
            more.AddToClassList("contact-button");
            top.Add(more);
            return row;
        }

        private static Label Text(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(className);
            return label;
        }

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

        // ------------------------------------------------------------------ actions

        private void Found()
        {
            if (crew == null) return;
            crew.Found(nameField.value, chosenSymbol, chosenColour);
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void LeaveOrDisband()
        {
            if (crew == null || !crew.InCrew) return;
            string crewName = crew.Header.name.ToString();
            if (crew.Header.myRank == CrewRank.Leader && crew.Roster.Count > 1)
                Confirm($"Disband {crewName}? Every member is taken out of it. To keep it, hand it over from a member's MORE menu instead.", () => crew.Disband());
            else if (crew.Roster.Count <= 1)
                Confirm($"Leave {crewName}? You are its last member: the Crew closes.", () => crew.Leave());
            else Confirm($"Leave {crewName}?", () => crew.Leave());
        }

        /// <summary>Opens the one line of typing for an invitation, a member's title or the announcement.</summary>
        private void BeginEdit(Edit what, CrewMemberView member = default)
        {
            editing = what;
            editingMember = what == Edit.Title ? member.id.ToString() : null;
            if (what == Edit.None)
            {
                editField.Blur();
                Refresh();
                return;
            }
            CrewSettings settings = crew != null ? crew.Settings : null;
            switch (what)
            {
                case Edit.Invite:
                    editLabel.text = "INVITE";
                    editField.maxLength = 24;
                    editField.SetValueWithoutNotify(string.Empty);
                    break;
                case Edit.Title:
                    editLabel.text = $"TITLE OF {member.name.ToString().ToUpperInvariant()}";
                    editField.maxLength = settings != null ? settings.titleMaxLength : 12;
                    editField.SetValueWithoutNotify(member.title.ToString());
                    break;
                default:
                    editLabel.text = "ANNOUNCE";
                    editField.maxLength = settings != null ? settings.announcementMaxLength : 160;
                    editField.SetValueWithoutNotify(crew != null ? crew.Header.announcement.ToString() : string.Empty);
                    break;
            }
            Refresh();
            editField.schedule.Execute(() => { editField.Focus(); editField.SelectAll(); });
        }

        private void CommitEdit()
        {
            if (crew == null || editing == Edit.None) return;
            string typed = (editField.value ?? string.Empty).Trim();
            switch (editing)
            {
                case Edit.Invite: if (typed.Length > 0) crew.Invite(typed); break;
                case Edit.Title: if (!string.IsNullOrEmpty(editingMember)) crew.SetTitle(editingMember, typed); break;
                case Edit.Announcement: crew.SetAnnouncement(typed); break;
            }
            BeginEdit(Edit.None);
        }
    }
}
