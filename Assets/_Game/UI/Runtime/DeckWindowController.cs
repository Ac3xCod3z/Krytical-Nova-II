using System.Collections.Generic;
using KryticalNova.Audio;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Deck's window (owner, 22 Oct 2026: "an iPod / music player ... client side ... styled per faction"): what
    /// plays, a visualiser that dances to it, the buttons, the volume and every track of every pack. One window, three
    /// looks: the Hegemony's is a real phone's media screen, the Null's a cracked dark-magic device, the Covenant's a
    /// carved rune tablet (Deck.uss, under each faction's class). Opening it plays the faction's motion for everyone to
    /// see (a phone out of the pocket for the Hegemony); the music itself is heard on this machine only
    /// (<see cref="DeckPlayer"/>). Its key is the Deck key ([P] to start with).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class DeckWindowController : MonoBehaviour
    {
        private const float WindowWidth = 380f;

        [SerializeField] private DeckLibrary library;
        [SerializeField] private StyleSheet style;

        private VisualElement root, window, progressFill, listBox, art;
        private Label title, pack, timeLabel, lengthLabel, artLetter, device;
        private Button playButton, shuffleButton, repeatButton;
        private Slider volumeSlider;
        private readonly List<VisualElement> bars = new List<VisualElement>();
        private readonly List<VisualElement> rows = new List<VisualElement>();
        private float[] spectrum = new float[512];
        private float[] barLevel;
        private int shownIndex = -2;
        private bool shownPlaying, shownFight;
        private string heldEmote; // the stance the Deck's opening played, let go of when it closes

        public bool IsOpen => window != null && window.style.display != DisplayStyle.None;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root != null && style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
        }

        private void OnDisable()
        {
            window?.RemoveFromHierarchy();
            window = null;
            bars.Clear();
            rows.Clear();
        }

        private void Update()
        {
            if (root == null || library == null) return;
            if (!HudTyping.IsTyping(root) && KeyBindings.Pressed(KeyBindings.Deck)) SetOpen(!IsOpen);
            if (IsOpen) Refresh();
        }

        public void SetOpen(bool open)
        {
            DeckPlayer deck = DeckPlayer.Ensure(library);
            if (deck == null) return;
            if (open && window == null) Build(deck);
            if (window == null) return;
            bool was = IsOpen;
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open)
            {
                // Closing puts the device away (owner, 22 Oct 2026: "after a player closes the deck panel the animation
                // shouldn't hold").
                if (was && heldEmote != null) PlayerEmotes.Local?.RequestStop(heldEmote);
                heldEmote = null;
                return;
            }
            if (was) return;
            window.BringToFront();
            Look();
            shownIndex = -2;
            // The faction's motion as the Deck comes out (it ends as soon as the player moves).
            string emote = FactionSkin.Current == FactionId.Null ? library.nullEmote
                : FactionSkin.Current == FactionId.Covenant ? library.covenantEmote : library.hegemonyEmote;
            PlayerEmotes emotes = PlayerEmotes.Local;
            if (emotes != null && !string.IsNullOrEmpty(emote) && emotes.Blocked() == null)
            {
                emotes.RequestPlay(emote);
                heldEmote = emote;
            }
        }

        // ------------------------------------------------------------------ building

        private void Build(DeckPlayer deck)
        {
            window = new VisualElement { name = "deck" };
            window.AddToClassList("deck");
            window.AddToClassList("blocks-world");
            window.style.width = WindowWidth;
            window.style.display = DisplayStyle.None;

            var header = Row("deck-header");
            device = new Label("THE DECK");
            device.AddToClassList("deck-device");
            header.Add(device);
            var key = new Label($"[{KeyBindings.Label(KeyBindings.Deck)}]");
            key.AddToClassList("deck-key");
            header.Add(key);
            var close = new Button(() => SetOpen(false)) { text = "×" };
            close.AddToClassList("deck-close");
            header.Add(close);
            window.Add(header);

            var now = Row("deck-now");
            art = new VisualElement();
            art.AddToClassList("deck-art");
            artLetter = new Label();
            artLetter.AddToClassList("deck-art-letter");
            art.Add(artLetter);
            now.Add(art);
            var names = new VisualElement();
            names.AddToClassList("deck-names");
            title = new Label();
            title.AddToClassList("deck-title");
            pack = new Label();
            pack.AddToClassList("deck-pack");
            names.Add(title);
            names.Add(pack);
            now.Add(names);
            window.Add(now);

            var visual = Row("deck-visualiser");
            barLevel = new float[library.bars];
            for (int i = 0; i < library.bars; i++)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.AddToClassList("deck-bar");
                if (i % 2 == 1) bar.AddToClassList("deck-bar--alt");
                visual.Add(bar);
                bars.Add(bar);
            }
            window.Add(visual);

            var progress = new VisualElement();
            progress.AddToClassList("deck-progress");
            progressFill = new VisualElement { pickingMode = PickingMode.Ignore };
            progressFill.AddToClassList("deck-progress-fill");
            progress.Add(progressFill);
            progress.RegisterCallback<PointerDownEvent>(e => DeckPlayer.Instance?.Seek(e.localPosition.x / Mathf.Max(1f, progress.resolvedStyle.width)));
            window.Add(progress);
            var times = Row("deck-times");
            timeLabel = new Label("0:00");
            lengthLabel = new Label("0:00");
            timeLabel.AddToClassList("deck-time");
            lengthLabel.AddToClassList("deck-time");
            times.Add(timeLabel);
            times.Add(lengthLabel);
            window.Add(times);

            var buttons = Row("deck-buttons");
            shuffleButton = Button(buttons, "SHUFFLE", "deck-small", () => { var d = DeckPlayer.Instance; if (d != null) d.Shuffle = !d.Shuffle; shownIndex = -2; });
            Button(buttons, "◀◀", "deck-skip", () => DeckPlayer.Instance?.Previous());
            playButton = Button(buttons, "▶", "deck-play", () => DeckPlayer.Instance?.PlayPause());
            Button(buttons, "▶▶", "deck-skip", () => DeckPlayer.Instance?.Next());
            repeatButton = Button(buttons, "REPEAT", "deck-small", () =>
            {
                var d = DeckPlayer.Instance;
                if (d != null) d.RepeatMode = (DeckPlayer.Repeat)(((int)d.RepeatMode + 1) % 3);
                shownIndex = -2;
            });
            window.Add(buttons);

            volumeSlider = new Slider("VOLUME", 0f, 1f) { value = deck.Volume };
            volumeSlider.AddToClassList("deck-volume");
            volumeSlider.RegisterValueChangedCallback(e => { if (DeckPlayer.Instance != null) DeckPlayer.Instance.Volume = e.newValue; });
            window.Add(volumeSlider);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("deck-list");
            listBox = scroll.contentContainer;
            for (int i = 0; i < deck.Tracks.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.AddToClassList("deck-row");
                var number = new Label((i + 1).ToString("00"));
                number.AddToClassList("deck-row-number");
                var name = new Label(deck.Tracks[i].Title);
                name.AddToClassList("deck-row-title");
                var from = new Label(deck.PackOf(i));
                from.AddToClassList("deck-row-pack");
                row.Add(number);
                row.Add(name);
                row.Add(from);
                row.RegisterCallback<ClickEvent>(_ => { DeckPlayer.Instance?.PlayAt(index); shownIndex = -2; });
                listBox.Add(row);
                rows.Add(row);
            }
            window.Add(scroll);

            var foot = new Label("Plays for you alone. In a fight it steps aside for the fight's music, and comes back after.");
            foot.AddToClassList("deck-foot");
            window.Add(foot);

            root.Add(window);
            window.style.position = Position.Absolute;
            window.style.right = 24f;
            window.style.top = 90f;
        }

        private static VisualElement Row(string cls)
        {
            var e = new VisualElement();
            e.AddToClassList(cls);
            return e;
        }

        private static Button Button(VisualElement parent, string text, string cls, System.Action click)
        {
            var b = new Button(click) { text = text };
            b.AddToClassList("deck-button");
            b.AddToClassList(cls);
            parent.Add(b);
            return b;
        }

        // The look of the faction: what the device is called on its own screen.
        private void Look()
        {
            if (device == null) return;
            device.text = FactionSkin.Current == FactionId.Null ? "HEX DECK"
                : FactionSkin.Current == FactionId.Covenant ? "RUNE TABLET" : "NOVAPHONE";
        }

        // ------------------------------------------------------------------ every frame, while open

        private void Refresh()
        {
            DeckPlayer deck = DeckPlayer.Instance;
            if (deck == null) return;
            if (deck.Index != shownIndex || deck.IsPlaying != shownPlaying || deck.InFight != shownFight)
            {
                shownIndex = deck.Index;
                shownPlaying = deck.IsPlaying;
                shownFight = deck.InFight;
                MusicTrack track = deck.Current;
                title.text = track != null ? track.Title.ToUpperInvariant() : "NO TRACK";
                pack.text = deck.InFight ? "STEPPED ASIDE: FIGHT MUSIC" : deck.PackOf(deck.Index);
                artLetter.text = track != null && track.Title.Length > 0 ? track.Title.Substring(0, 1).ToUpperInvariant() : "?";
                playButton.text = deck.IsPlaying ? "❚❚" : "▶";
                shuffleButton.EnableInClassList("deck-on", deck.Shuffle);
                repeatButton.text = deck.RepeatMode == DeckPlayer.Repeat.One ? "REPEAT 1" : "REPEAT";
                repeatButton.EnableInClassList("deck-on", deck.RepeatMode != DeckPlayer.Repeat.Off);
                for (int i = 0; i < rows.Count; i++) rows[i].EnableInClassList("deck-row--now", i == deck.Index);
            }
            progressFill.style.width = Length.Percent(deck.Time01 * 100f);
            timeLabel.text = Clock(deck.Seconds);
            lengthLabel.text = Clock(deck.Length);

            // The visualiser: the spectrum in log bands, eased, as bar heights.
            deck.Spectrum(spectrum);
            int n = bars.Count;
            for (int b = 0; b < n; b++)
            {
                int from = Mathf.FloorToInt(Mathf.Pow(spectrum.Length * 0.6f, b / (float)n));
                int to = Mathf.Max(from + 1, Mathf.FloorToInt(Mathf.Pow(spectrum.Length * 0.6f, (b + 1) / (float)n)));
                float sum = 0f;
                for (int k = from; k < to && k < spectrum.Length; k++) sum += spectrum[k];
                float level = Mathf.Clamp01(Mathf.Sqrt(sum / (to - from)) * library.barGain);
                barLevel[b] = level > barLevel[b] ? level : Mathf.MoveTowards(barLevel[b], level, Time.unscaledDeltaTime * 1.8f);
                bars[b].style.height = Length.Percent(6f + barLevel[b] * 94f);
            }
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
