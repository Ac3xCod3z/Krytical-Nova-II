using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The chat window in the bottom-left corner of the HUD: a tab per channel, the message log, the input line and
    /// the Combat tab with the player's own damage, evades and misses. It only shows lines the server sent this
    /// player (<see cref="IChatSink"/>) and hands typed lines to <see cref="PlayerChat"/>; who hears what is decided
    /// on the server. Enter starts typing, Enter sends, Escape leaves. The window starts in its corner, can be moved in the HUD layout editor, and is resized
    /// from the grip in its top-right corner, within the sizes the style sheet allows.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ChatWindowController : MonoBehaviour, IChatSink
    {
        private const int LinesPerChannel = 200;
        private const int LinesShown = 200;
        private const float GameMasterRefreshInterval = 0.5f;
        private const float BottomTolerance = 4f;
        private const string TabClass = "chat-tab";
        private const string ActiveTabClass = "chat-tab--active";
        private const string UnreadTabClass = "chat-tab--unread";
        private const string LineClass = "chat-line";
        private const string ResizingClass = "chat-resize--active";
        private const string Welcome = "Press Enter to chat.  /s say   /f faction   /p squad   /g crew   /w <name> whisper   /r reply";

        private sealed class Tab
        {
            public string Title;
            /// <summary>Bit per <see cref="ChatChannel"/> shown on this tab.</summary>
            public int Shows;
            /// <summary>The channel typing on this tab talks on, if it is a player channel.</summary>
            public ChatChannel? Talks;
            public Button Button;
        }

        private struct Stored
        {
            public long Order;
            public ChatLine Line;
            /// <summary>When the line arrived, by this machine's clock (shown if the player asks for the time on lines).</summary>
            public System.DateTime At;
        }

        private static readonly string[] ChannelNames = { "GENERAL", "FACTION", "CREW", "WHISPER", "GM", "SYSTEM", "COMBAT", "SQUAD" };
        private static readonly string[] LineClasses =
        {
            "chat-line--general", "chat-line--faction", "chat-line--guild", "chat-line--whisper",
            "chat-line--gm", "chat-line--system", "chat-line--combat", "chat-line--party"
        };

        private readonly List<Stored>[] history = new List<Stored>[ChannelNames.Length];
        private readonly List<Stored> merged = new List<Stored>(LinesShown * 2);
        private readonly List<Tab> tabs = new List<Tab>(8);

        private VisualElement root;
        private VisualElement boundRoot;
        private VisualElement window;
        private VisualElement tabRow;
        private VisualElement resizeGrip;
        private ScrollView log;
        private TextField input;
        private Label channelLabel;
        private Label hint;
        private ProgressionHUD hud;
        // Resolved live: the HUD rebinds to the networked player when hosting/joining.
        private PlayerSkillHotbar Hotbar => hud != null ? hud.Hotbar : null;

        private Tab activeTab;
        private ChatChannel talkChannel = ChatChannel.General;
        private string whisperTarget;
        private string replyTo;
        private long order;
        private bool typing;
        private bool pinnedToBottom = true;
        private bool welcomed;
        // The player's chat options as the lines on show were written with them.
        private bool shownTimestamps;
        private float shownTextScale = 1f;
        private Label sizeProbe;
        private bool isGameMaster;
        private float nextGameMasterRefresh;
        private int focusedFrame = -1;
        private int leftFrame = -1;

        // Items the player is linking in the line being typed (Shift + right-click on an item): where each one is and
        // the "[Name]" that stands for it in the input until the line is sent.
        private readonly List<(ItemLocation at, string tag)> pendingLinks = new List<(ItemLocation, string)>(ChatLinks.Max);
        private GearUIController gear;

        private int resizePointer = -1;
        private Vector2 resizeStartPointer;
        private Vector2 resizeStartSize;
        // Once the layout editor has moved the window it hangs from its top edge, not from the bottom of the screen.
        private bool resizeMovesTop;
        private float resizeStartTop;
        private float resizeScale = 1f;

        private void Awake()
        {
            for (int i = 0; i < history.Length; i++) history[i] = new List<Stored>(LinesPerChannel);
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("chat-window");
            if (window == null) return;

            tabRow = window.Q("chat-tabs");
            log = window.Q<ScrollView>("chat-log");
            input = window.Q<TextField>("chat-input");
            channelLabel = window.Q<Label>("chat-channel");
            hint = window.Q<Label>("chat-hint");
            resizeGrip = window.Q("chat-resize");
            hud = GetComponent<ProgressionHUD>();
            TryGetComponent(out gear);
            // Keyboard navigation (W A S D, the arrows) never walks into the chat line or any other field of the HUD.
            HudTyping.NoKeyboardFocus(root);
            if (input != null) input.tabIndex = -1;
            if (tabRow == null || log == null || input == null)
            {
                Debug.LogError("ChatWindowController needs 'chat-tabs', 'chat-log' and 'chat-input' in its UXML.", this);
                window = null;
                return;
            }

            if (boundRoot != root)
            {
                boundRoot = root;
                sizeProbe = null; // it belonged to the interface that was there before
                BuildTabs();
                BindInput();
                BindResize();
                ApplySavedSize();
            }

            ClientPresentationRegistry.RegisterChat(this);
            shownTimestamps = HudPreferences.ChatTimestamps;
            shownTextScale = HudPreferences.ChatTextScale;
            HudPreferences.Changed -= OnPreferencesChanged;
            HudPreferences.Changed += OnPreferencesChanged;
            SelectTab(activeTab ?? tabs[0]);
            if (!welcomed)
            {
                welcomed = true;
                Add(new ChatLine { Channel = ChatChannel.System, Text = Welcome });
            }
        }

        private void OnDisable()
        {
            HudPreferences.Changed -= OnPreferencesChanged;
            ClientPresentationRegistry.UnregisterChat(this);
            SetTyping(false);
        }

        private void Update()
        {
            if (window == null) return;

            if (Time.unscaledTime >= nextGameMasterRefresh)
            {
                nextGameMasterRefresh = Time.unscaledTime + GameMasterRefreshInterval;
                var chat = PlayerChat.Local;
                bool now = chat != null && chat.IsGameMaster;
                if (now != isGameMaster)
                {
                    isGameMaster = now;
                    RefreshInputState();
                }
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || typing || KeyBindings.KeyboardTaken || leftFrame == Time.frameCount) return;
            bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
            if (enter && input.enabledSelf && !AnyTextFieldFocused())
            {
                focusedFrame = Time.frameCount;
                input.Focus();
            }
        }

        // ------------------------------------------------------------------ lines

        public void Add(in ChatLine line)
        {
            int channel = (int)line.Channel;
            if (channel < 0 || channel >= history.Length || string.IsNullOrEmpty(line.Text)) return;

            var stored = new Stored { Order = ++order, Line = line, At = System.DateTime.Now };
            var list = history[channel];
            if (list.Count >= LinesPerChannel) list.RemoveAt(0);
            list.Add(stored);

            if (line.Channel == ChatChannel.Whisper && !line.Outgoing && !string.IsNullOrEmpty(line.Sender)) replyTo = line.Sender;
            if (window == null || activeTab == null) return;

            if ((activeTab.Shows & Bit(line.Channel)) != 0)
            {
                Append(stored);
                while (log.contentContainer.childCount > LinesShown) log.contentContainer.RemoveAt(0);
                return;
            }
            // Somebody spoke on a channel this tab does not show: light that channel's own tab.
            foreach (Tab tab in tabs)
                if (tab.Talks == line.Channel) tab.Button.AddToClassList(UnreadTabClass);
        }

        private void Append(in Stored stored)
        {
            var scroller = log.verticalScroller;
            pinnedToBottom = scroller.highValue <= 0f || scroller.value >= scroller.highValue - BottomTolerance;

            ChatLine line = stored.Line;
            string text = shownTimestamps ? $"[{stored.At:HH:mm}] {Format(line)}" : Format(line);
            // Every line can be highlighted with the mouse and copied (Ctrl+C). A line with items linked in it is the
            // one drawn with rich text, and only its link names are markup: what the player typed is never read as tags.
            VisualElement shown = line.HasLinks ? LinkedLine(text, line) : PlainLine(ChatLinks.Strip(text));
            shown.AddToClassList(LineClass);
            shown.AddToClassList(LineClasses[(int)line.Channel]);
            float size = LineSize();
            if (size > 0f) shown.style.fontSize = size;
            log.Add(shown);
        }

        // A line as the player wrote it: one label, never read as markup, that can be highlighted and copied (Ctrl+C).
        private static Label PlainLine(string text)
        {
            var label = new Label(text) { enableRichText = false, pickingMode = PickingMode.Position };
            label.selection.isSelectable = true;
            return label;
        }

        // ------------------------------------------------------------------ item links

        private ItemCatalog Catalog => PlayerInventory.Local != null ? PlayerInventory.Local.Catalog : null;

        /// <summary>
        /// A line with items linked in it, as a row of words that wraps like a line of text, with each item a word of
        /// its own: its name in brackets in its rarity's colour, its card while the pointer is over it, and on a
        /// right-click the piece on a doll. Every word can still be highlighted and copied.
        /// </summary>
        private VisualElement LinkedLine(string text, ChatLine line)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("chat-line--linked");
            // Only the items are coloured, except on a whisper, which keeps its own colour (owner, 22 Oct 2026).
            if (line.Channel != ChatChannel.Whisper) row.AddToClassList("chat-line--plain");
            var word = new System.Text.StringBuilder();
            void EndWord()
            {
                if (word.Length == 0) return;
                var part = new Label(word.ToString()) { enableRichText = false, pickingMode = PickingMode.Position };
                part.selection.isSelectable = true;
                part.AddToClassList("chat-word");
                row.Add(part);
                word.Clear();
            }
            foreach (char c in text)
            {
                int link = ChatLinks.IndexOf(c);
                if (link >= 0)
                {
                    EndWord();
                    if (link < line.LinkItems.Length) row.Add(LinkWord(line, link));
                    continue;
                }
                word.Append(c);
                if (c == ' ') EndWord();
            }
            EndWord();
            return row;
        }

        private Label LinkWord(ChatLine line, int link)
        {
            ItemDefinition def = Catalog != null ? Catalog.Get(line.LinkItems[link]) : null;
            var label = new Label($"[{(def != null ? def.displayName : "an unknown item")}] ") { enableRichText = false, pickingMode = PickingMode.Position };
            label.AddToClassList("chat-word");
            label.AddToClassList("chat-link");
            label.style.color = RarityColour(def != null ? def.rarity : ItemRarity.Common);
            label.RegisterCallback<PointerEnterEvent>(_ => ShowLinkCard(label, line, link));
            label.RegisterCallback<PointerLeaveEvent>(_ => { if (gear != null) gear.HideItemTooltip(); });
            label.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 1) return;
                evt.StopPropagation();
                OpenLink(label, line, link);
            });
            return label;
        }

        // The same colours as an item's name on its card (Items.uss, rarity-text-0 to 4).
        private static Color RarityColour(ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.Uncommon: return new Color32(110, 170, 255, 255);
                case ItemRarity.Rare: return new Color32(255, 218, 96, 255);
                case ItemRarity.Epic: return new Color32(200, 128, 255, 255);
                case ItemRarity.Legendary: return new Color32(255, 150, 64, 255);
                default: return new Color32(240, 243, 246, 255);
            }
        }

        private bool TryLinked(in ChatLine line, int link, out ItemDefinition def, out ItemStack stack)
        {
            def = null;
            stack = ItemStack.Empty;
            if (!line.HasLinks || link < 0 || link >= line.LinkItems.Length || Catalog == null) return false;
            def = Catalog.Get(line.LinkItems[link]);
            if (def == null) return false;
            ulong data = line.LinkData != null && link < line.LinkData.Length ? line.LinkData[link] : 0UL;
            stack = new ItemStack(line.LinkItems[link], 1, data);
            return true;
        }

        private void ShowLinkCard(VisualElement near, in ChatLine line, int link)
        {
            if (gear == null || !TryLinked(line, link, out ItemDefinition def, out ItemStack stack)) return;
            string hint = def.IsEquippable ? "Right-click: see it on a doll" : "Right-click: keep this card open";
            gear.ShowItemTooltip(near, def, stack, hint, above: true);
        }

        private void OpenLink(VisualElement near, in ChatLine line, int link)
        {
            if (gear == null || !TryLinked(line, link, out ItemDefinition def, out ItemStack stack)) return;
            if (!gear.PreviewLinkedPiece(def, stack)) ShowLinkCard(near, line, link);
        }

        /// <summary>
        /// Puts an item into the line being typed (Shift + right-click on it, or "Share in chat"): its name in
        /// brackets goes into the input, and the item goes with the line when it is sent, as a link anyone can read.
        /// </summary>
        public void InsertItemLink(ItemLocation at, ItemDefinition def)
        {
            if (window == null || def == null || !input.enabledSelf) return;
            if (pendingLinks.Count >= ChatLinks.Max)
            {
                Notice($"A line can hold {ChatLinks.Max} items at most.");
                return;
            }
            string tag = $"[{def.displayName}]";
            pendingLinks.Add((at, tag));
            string now = input.value ?? string.Empty;
            string joined = now.Length == 0 || now.EndsWith(" ") ? now + tag + " " : now + " " + tag + " ";
            input.SetValueWithoutNotify(joined);
            focusedFrame = Time.frameCount;
            input.Focus();
            input.schedule.Execute(() => input.SelectRange(input.value.Length, input.value.Length));
        }

        // The typed line with each item still in it turned into its link marker, and the items in the same order.
        private string TakeLinks(string text, out ItemLocation[] links)
        {
            links = null;
            if (pendingLinks.Count == 0 || string.IsNullOrEmpty(text)) { pendingLinks.Clear(); return text; }
            var found = new List<ItemLocation>(pendingLinks.Count);
            foreach ((ItemLocation at, string tag) in pendingLinks)
            {
                int where = text.IndexOf(tag, System.StringComparison.Ordinal);
                if (where < 0) continue; // the player took it out again
                text = text.Substring(0, where) + ChatLinks.Marker(found.Count) + text.Substring(where + tag.Length);
                found.Add(at);
            }
            pendingLinks.Clear();
            if (found.Count > 0) links = found.ToArray();
            return text;
        }

        // ------------------------------------------------------------------ the player's chat options

        /// <summary>Settings, Interface: the time on each line, or the size of the text, was changed.</summary>
        private void OnPreferencesChanged()
        {
            if (window == null || activeTab == null) return;
            bool timestamps = HudPreferences.ChatTimestamps;
            float scale = HudPreferences.ChatTextScale;
            if (timestamps == shownTimestamps && Mathf.Approximately(scale, shownTextScale)) return;
            shownTimestamps = timestamps;
            shownTextScale = scale;
            Rebuild();
        }

        /// <summary>
        /// The size a line's text is given, or 0 to leave it to the style sheets (the size as shipped). The player's
        /// size is a share of what the skin gives a chat line, which is read off a line that is never shown.
        /// </summary>
        private float LineSize()
        {
            if (Mathf.Approximately(shownTextScale, 1f)) return 0f;
            if (sizeProbe == null)
            {
                sizeProbe = new Label("Ag") { pickingMode = PickingMode.Ignore };
                sizeProbe.AddToClassList(LineClass);
                sizeProbe.style.position = Position.Absolute;
                sizeProbe.style.visibility = Visibility.Hidden;
                window.Add(sizeProbe);
                // Its size is known after the next layout, and changes with the faction's skin: size the lines again then.
                sizeProbe.RegisterCallback<GeometryChangedEvent>(_ => ResizeLines());
            }
            float skinSize = sizeProbe.resolvedStyle.fontSize;
            return skinSize > 0f ? skinSize * shownTextScale : 0f;
        }

        private void ResizeLines()
        {
            float size = LineSize();
            foreach (VisualElement line in log.contentContainer.Children())
                line.style.fontSize = size > 0f ? size : new StyleLength(StyleKeyword.Null);
        }

        private static string Format(in ChatLine line)
        {
            switch (line.Channel)
            {
                case ChatChannel.General: return $"[General] {line.Sender}: {line.Text}";
                case ChatChannel.Faction: return $"[Faction] {line.Sender}: {line.Text}";
                case ChatChannel.Party: return $"[Squad] {line.Sender}: {line.Text}";
                case ChatChannel.Guild: return $"[Crew] {line.Sender}: {line.Text}";
                case ChatChannel.Whisper: return $"{(line.Outgoing ? "To" : "From")} {line.Sender}: {line.Text}";
                case ChatChannel.Gm: return $"[GM] {line.Sender}: {line.Text}";
                default: return line.Text;
            }
        }

        private void Rebuild()
        {
            merged.Clear();
            for (int channel = 0; channel < history.Length; channel++)
                if ((activeTab.Shows & (1 << channel)) != 0) merged.AddRange(history[channel]);
            merged.Sort((a, b) => a.Order.CompareTo(b.Order));

            log.Clear();
            for (int i = Mathf.Max(0, merged.Count - LinesShown); i < merged.Count; i++) Append(merged[i]);
            pinnedToBottom = true;
        }

        // The log's height is only known after layout, and the scroll view works out its own range in that same
        // pass: follow new lines just after it, unless the player scrolled up to read.
        private void OnLogLayout(GeometryChangedEvent evt)
        {
            if (pinnedToBottom) log.schedule.Execute(ScrollToEnd);
        }

        private void ScrollToEnd()
        {
            if (pinnedToBottom) log.verticalScroller.value = log.verticalScroller.highValue;
        }

        // ------------------------------------------------------------------ tabs

        private void BuildTabs()
        {
            int players = Bit(ChatChannel.General) | Bit(ChatChannel.Faction) | Bit(ChatChannel.Party) | Bit(ChatChannel.Guild)
                | Bit(ChatChannel.Whisper);
            // Notices and GM announcements are for the player whatever tab they are on; combat has its own tab.
            int always = Bit(ChatChannel.System) | Bit(ChatChannel.Gm);

            tabs.Clear();
            tabRow.Clear();
            AddTab("ALL", players | always, null);
            AddTab("GENERAL", Bit(ChatChannel.General) | always, ChatChannel.General);
            AddTab("FACTION", Bit(ChatChannel.Faction) | always, ChatChannel.Faction);
            AddTab("SQUAD", Bit(ChatChannel.Party) | always, ChatChannel.Party);
            AddTab("CREW", Bit(ChatChannel.Guild) | always, ChatChannel.Guild);
            AddTab("WHISPER", Bit(ChatChannel.Whisper) | always, ChatChannel.Whisper);
            AddTab("GM", always, ChatChannel.Gm);
            AddTab("COMBAT", Bit(ChatChannel.Combat), null);
            activeTab = null;
        }

        private void AddTab(string title, int shows, ChatChannel? talks)
        {
            var tab = new Tab { Title = title, Shows = shows, Talks = talks, Button = new Button { text = title, focusable = false } };
            tab.Button.AddToClassList(TabClass);
            tab.Button.clicked += () => SelectTab(tab);
            tabRow.Add(tab.Button);
            tabs.Add(tab);
        }

        private void SelectTab(Tab tab)
        {
            activeTab = tab;
            foreach (Tab other in tabs) other.Button.EnableInClassList(ActiveTabClass, other == tab);
            tab.Button.RemoveFromClassList(UnreadTabClass);
            if (tab.Talks.HasValue) talkChannel = tab.Talks.Value;
            Rebuild();
            RefreshInputState();
        }

        private void Talk(ChatChannel channel, string target)
        {
            talkChannel = channel;
            if (channel == ChatChannel.Whisper && !string.IsNullOrEmpty(target)) whisperTarget = target;
            if ((activeTab.Shows & Bit(channel)) == 0)
            {
                foreach (Tab tab in tabs)
                {
                    if (tab.Talks != channel) continue;
                    SelectTab(tab);
                    return;
                }
            }
            RefreshInputState();
        }

        /// <summary>Starts a whisper to <paramref name="playerName"/>: the Whisper tab, the cursor in the input line.</summary>
        public void BeginWhisper(string playerName)
        {
            if (window == null || string.IsNullOrEmpty(playerName)) return;
            Talk(ChatChannel.Whisper, playerName);
            focusedFrame = Time.frameCount;
            input.Focus();
        }

        /// <summary>The GM channel can be read by everyone and written by the host only.</summary>
        private void RefreshInputState()
        {
            bool readOnly = talkChannel == ChatChannel.Gm && !isGameMaster;
            input.SetEnabled(!readOnly);
            if (readOnly) channelLabel.text = "GM · READ ONLY";
            else if (talkChannel == ChatChannel.Whisper && !string.IsNullOrEmpty(whisperTarget)) channelLabel.text = $"TO {whisperTarget.ToUpperInvariant()}";
            else channelLabel.text = ChannelNames[(int)talkChannel];
            if (hint != null) hint.style.display = typing || readOnly ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ typing

        private void BindInput()
        {
            input.RegisterCallback<KeyDownEvent>(OnInputKey, TrickleDown.TrickleDown);
            input.RegisterCallback<FocusInEvent>(_ => SetTyping(true));
            input.RegisterCallback<FocusOutEvent>(_ => SetTyping(false));
            log.contentContainer.RegisterCallback<GeometryChangedEvent>(OnLogLayout);
            log.RegisterCallback<GeometryChangedEvent>(OnLogLayout);
        }

        private void OnInputKey(KeyDownEvent evt)
        {
            bool submit = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter || evt.character == '\n';
            if (submit)
            {
                evt.StopPropagation();
                // The key press that focused the field must not also send an empty line.
                if (focusedFrame != Time.frameCount) Submit();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                input.SetValueWithoutNotify(string.Empty);
                pendingLinks.Clear();
                Leave();
            }
        }

        private void Submit()
        {
            string raw = input.value;
            input.SetValueWithoutNotify(string.Empty);
            Leave();

            ChatInput typed = ChatCommands.Parse(raw, talkChannel, whisperTarget, replyTo);
            switch (typed.Kind)
            {
                case ChatInputKind.Notice:
                    Notice(typed.Notice);
                    break;
                case ChatInputKind.SwitchChannel:
                    Talk(typed.Channel, typed.Target);
                    break;
                case ChatInputKind.Send:
                    // Refused here to spare the round trip; the server refuses it again for anyone who is not the host.
                    if (typed.Channel == ChatChannel.Gm && !isGameMaster)
                    {
                        Notice("Only the GM can use this channel.");
                        break;
                    }
                    Talk(typed.Channel, typed.Target);
                    var chat = PlayerChat.Local;
                    string said = TakeLinks(typed.Text, out ItemLocation[] links);
                    if (chat == null) Notice("Chat is not available right now.");
                    else chat.Send(typed.Channel, said, typed.Target, links);
                    break;
            }
            pendingLinks.Clear();
        }

        private void Notice(string text) => Add(new ChatLine { Channel = ChatChannel.System, Text = text });

        private void Leave()
        {
            leftFrame = Time.frameCount;
            input.Blur();
        }

        // Typing must not fire hotbar keys (1-0, Q/W/E/R, C, K) or the weapon toggle.
        private void SetTyping(bool value)
        {
            if (typing == value) return;
            typing = value;
            var hotbar = Hotbar;
            if (hotbar != null) hotbar.InputSuppressed = value;
            if (window != null) RefreshInputState();
        }

        private bool AnyTextFieldFocused()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement element = focused; element != null; element = element.parent)
                if (element is TextField) return true;
            return false;
        }

        // ------------------------------------------------------------------ resizing (the corner stays put)

        private void BindResize()
        {
            if (resizeGrip == null) return;
            resizeGrip.RegisterCallback<PointerDownEvent>(OnResizeDown);
            resizeGrip.RegisterCallback<PointerMoveEvent>(OnResizeMove);
            resizeGrip.RegisterCallback<PointerUpEvent>(OnResizeUp);
            resizeGrip.RegisterCallback<PointerCaptureOutEvent>(_ => EndResize(false));
        }

        private void ApplySavedSize()
        {
            if (!HudPreferences.TryGetChatSize(out Vector2 size)) return;
            // The limits are not resolved before the first layout; clamp once they are.
            window.RegisterCallbackOnce<GeometryChangedEvent>(_ => SetSize(size));
        }

        private void OnResizeDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || resizePointer >= 0) return;
            resizePointer = evt.pointerId;
            resizeStartPointer = evt.position;
            resizeStartSize = new Vector2(window.resolvedStyle.width, window.resolvedStyle.height);
            resizeScale = Mathf.Max(0.01f, window.resolvedStyle.scale.value.x);
            resizeMovesTop = window.style.top.keyword == StyleKeyword.Undefined; // an inline top: placed by the layout editor
            resizeStartTop = window.resolvedStyle.top;
            resizeGrip.CapturePointer(evt.pointerId);
            resizeGrip.AddToClassList(ResizingClass);
            evt.StopPropagation();
        }

        private void OnResizeMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != resizePointer) return;
            // The bottom-left corner stays put: dragging right widens it, dragging up makes it taller.
            Vector2 delta = ((Vector2)evt.position - resizeStartPointer) / resizeScale;
            SetSize(new Vector2(resizeStartSize.x + delta.x, resizeStartSize.y - delta.y));
            // Hanging from its top edge, it would grow downward: move the top by what the height gained instead.
            if (resizeMovesTop) window.style.top = resizeStartTop - (window.style.height.value.value - resizeStartSize.y) * resizeScale;
            evt.StopPropagation();
        }

        private void OnResizeUp(PointerUpEvent evt)
        {
            if (evt.pointerId != resizePointer) return;
            resizeGrip.ReleasePointer(evt.pointerId);
            EndResize(true);
            evt.StopPropagation();
        }

        private void EndResize(bool save)
        {
            if (resizePointer < 0) return;
            resizePointer = -1;
            resizeGrip.RemoveFromClassList(ResizingClass);
            if (save) HudPreferences.SetChatSize(new Vector2(window.resolvedStyle.width, window.resolvedStyle.height));
            // Its place changed with its size: once laid out, the layout editor keeps it on screen and stores it.
            if (resizeMovesTop) window.schedule.Execute(() => GetComponent<HudSettingsController>()?.Replace(window));
            resizeMovesTop = false;
        }

        /// <summary>Sizes the window, held between the minimum and maximum the style sheet sets.</summary>
        private void SetSize(Vector2 size)
        {
            IResolvedStyle limits = window.resolvedStyle;
            window.style.width = Clamp(size.x, limits.minWidth, limits.maxWidth);
            window.style.height = Clamp(size.y, limits.minHeight, limits.maxHeight);
        }

        private static float Clamp(float value, StyleFloat min, StyleFloat max)
        {
            if (max.keyword == StyleKeyword.Undefined && max.value > 0f) value = Mathf.Min(value, max.value);
            if (min.keyword == StyleKeyword.Undefined) value = Mathf.Max(value, min.value);
            return value;
        }

        private static int Bit(ChatChannel channel) => 1 << (int)channel;
    }
}
