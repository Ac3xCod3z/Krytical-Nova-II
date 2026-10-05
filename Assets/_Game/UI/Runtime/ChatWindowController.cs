using System.Collections.Generic;
using KryticalNova.Core;
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
    /// on the server. Enter starts typing, Enter sends, Escape leaves. The window stays in its corner and is resized
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
        private bool isGameMaster;
        private float nextGameMasterRefresh;
        private int focusedFrame = -1;
        private int leftFrame = -1;

        private int resizePointer = -1;
        private Vector2 resizeStartPointer;
        private Vector2 resizeStartSize;

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
            if (tabRow == null || log == null || input == null)
            {
                Debug.LogError("ChatWindowController needs 'chat-tabs', 'chat-log' and 'chat-input' in its UXML.", this);
                window = null;
                return;
            }

            if (boundRoot != root)
            {
                boundRoot = root;
                BuildTabs();
                BindInput();
                BindResize();
                ApplySavedSize();
            }

            ClientPresentationRegistry.RegisterChat(this);
            SelectTab(activeTab ?? tabs[0]);
            if (!welcomed)
            {
                welcomed = true;
                Add(new ChatLine { Channel = ChatChannel.System, Text = Welcome });
            }
        }

        private void OnDisable()
        {
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
            if (keyboard == null || typing || leftFrame == Time.frameCount) return;
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

            var stored = new Stored { Order = ++order, Line = line };
            var list = history[channel];
            if (list.Count >= LinesPerChannel) list.RemoveAt(0);
            list.Add(stored);

            if (line.Channel == ChatChannel.Whisper && !line.Outgoing && !string.IsNullOrEmpty(line.Sender)) replyTo = line.Sender;
            if (window == null || activeTab == null) return;

            if ((activeTab.Shows & Bit(line.Channel)) != 0)
            {
                Append(line);
                while (log.contentContainer.childCount > LinesShown) log.contentContainer.RemoveAt(0);
                return;
            }
            // Somebody spoke on a channel this tab does not show: light that channel's own tab.
            foreach (Tab tab in tabs)
                if (tab.Talks == line.Channel) tab.Button.AddToClassList(UnreadTabClass);
        }

        private void Append(in ChatLine line)
        {
            var scroller = log.verticalScroller;
            pinnedToBottom = scroller.highValue <= 0f || scroller.value >= scroller.highValue - BottomTolerance;

            var label = new Label(Format(line)) { enableRichText = false, pickingMode = PickingMode.Ignore };
            label.AddToClassList(LineClass);
            label.AddToClassList(LineClasses[(int)line.Channel]);
            log.Add(label);
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
            for (int i = Mathf.Max(0, merged.Count - LinesShown); i < merged.Count; i++) Append(merged[i].Line);
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
                    if (chat == null) Notice("Chat is not available right now.");
                    else chat.Send(typed.Channel, typed.Text, typed.Target);
                    break;
            }
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
            resizeGrip.CapturePointer(evt.pointerId);
            resizeGrip.AddToClassList(ResizingClass);
            evt.StopPropagation();
        }

        private void OnResizeMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != resizePointer) return;
            // Anchored bottom-left: dragging right widens it, dragging up makes it taller.
            Vector2 delta = (Vector2)evt.position - resizeStartPointer;
            SetSize(new Vector2(resizeStartSize.x + delta.x, resizeStartSize.y - delta.y));
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
