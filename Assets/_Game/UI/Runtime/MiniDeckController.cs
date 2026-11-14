using KryticalNova.Audio;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Mini Deck (build 17, day list 16): while Cadence is live (every fight, Callouts included) a small music player takes
    /// the minimap's place, showing what plays and carrying the beat on an arc over its rim (<see cref="MiniDeckArc"/>);
    /// when Cadence ends the minimap comes back. M still opens the full map. In a Callout the chat fades too, and comes
    /// back while the player types (Enter). It reads the same beat as the 3D Beat Ring (<see cref="CombatBeat"/>,
    /// <see cref="PlayerCadence.LocalPress"/>) and decides nothing. The Beat Ring setting in Audio switches the arc.
    /// The body is the owner's pick A (13 Nov 2026): a click-wheel music player, small screen on top, round wheel below.
    /// The 3D Beat Ring is retired (<c>BeatRingSettings.retired</c>); the Beat Ring setting in Audio switches this arc.
    /// Puts itself on the HUD at run time, as the Mount window does.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniDeckController : MonoBehaviour
    {
        private const float LiveAbove = 0.01f;
        private const float FlashSeconds = 0.09f, LitSeconds = 0.45f, ChatFadeSeconds = 0.25f, SwapFadeSeconds = 0.2f;
        private const float DeviceWidth = 134f, DeviceHeight = 186f, DeviceRadius = 18f;
        private const float WheelSize = 82f, WheelCentre = 28f, BarMaxHeight = 18f;
        private readonly VisualElement[] bars = new VisualElement[12];
        // today's Beat Ring colours (BeatRingSettings: low, mid, high), by the combo step reached on the beat
        private static readonly Color StepLow = new Color(0.25f, 0.85f, 1f), StepMid = new Color(1f, 0.72f, 0.18f), StepHigh = new Color(1f, 0.22f, 0.16f);

        private VisualElement root, minimap, deck, chat;
        private MiniDeckArc arc;
        private Label title, pack;
        private bool shown, chatFaded;
        private float lit, chatOpacity = 1f;
        // Owner 13 Nov 2026 (3.3): never both at once - the minimap fades out, then the Mini Deck fades in, and back.
        private float mapOpacity = 1f, deckOpacity;
        private int step;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach();

        private static void Attach()
        {
            if (Application.isBatchMode) return;
            var hud = FindAnyObjectByType<ProgressionHUD>(FindObjectsInactive.Include);
            if (hud != null && hud.GetComponent<MiniDeckController>() == null) hud.gameObject.AddComponent<MiniDeckController>();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            minimap = root?.Q("minimap");
            chat = root?.Q("chat-window");
            if (minimap == null) return;
            Build();
            PlayerCadence.LocalPress += OnPress;
            PlayerCadence.LocalCombo += OnCombo;
        }

        private void OnDisable()
        {
            PlayerCadence.LocalPress -= OnPress;
            PlayerCadence.LocalCombo -= OnCombo;
            Show(false);
            SetChat(1f);
            // switched off: the minimap comes back whole at once
            mapOpacity = 1f; deckOpacity = 0f;
            if (minimap != null) ApplySwap(minimap, deck, 1f, 0f);
            Hud.MinimapChromeView.StepAside01 = 1f;
            deck?.RemoveFromHierarchy();
            deck = null;
        }

        private void Build()
        {
            deck = new VisualElement { name = "mini-deck", pickingMode = PickingMode.Ignore };
            deck.AddToClassList("mini-deck");
            var s = deck.style;
            s.position = Position.Absolute;
            s.width = DeviceWidth;
            s.height = DeviceHeight;
            s.left = (190f - DeviceWidth) * 0.5f;
            s.top = 2f;
            s.backgroundColor = new Color(0.055f, 0.07f, 0.12f, 0.96f);
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = DeviceRadius;
            s.borderLeftWidth = s.borderRightWidth = s.borderTopWidth = s.borderBottomWidth = 2f;
            Color edge = new Color(0.31f, 0.38f, 0.51f);
            s.borderLeftColor = s.borderRightColor = s.borderTopColor = s.borderBottomColor = edge;
            s.display = DisplayStyle.None;

            var screen = new VisualElement { name = "mini-deck-screen", pickingMode = PickingMode.Ignore };
            screen.AddToClassList("mini-deck-screen");
            var ss = screen.style;
            ss.marginLeft = ss.marginRight = 10f;
            ss.marginTop = 12f;
            ss.paddingLeft = ss.paddingRight = 7f;
            ss.paddingTop = ss.paddingBottom = 6f;
            ss.backgroundColor = new Color(0.024f, 0.04f, 0.07f, 1f);
            ss.borderTopLeftRadius = ss.borderTopRightRadius = ss.borderBottomLeftRadius = ss.borderBottomRightRadius = 6f;
            var now = new Label("NOW PLAYING") { pickingMode = PickingMode.Ignore };
            now.style.fontSize = 8;
            now.style.color = new Color(0.31f, 0.78f, 1f);
            title = new Label { name = "mini-deck-title", pickingMode = PickingMode.Ignore };
            title.style.fontSize = 11;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.9f, 0.93f, 0.97f);
            title.style.whiteSpace = WhiteSpace.NoWrap;   // one line: the wheel sits under the screen
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            pack = new Label { name = "mini-deck-pack", pickingMode = PickingMode.Ignore };
            pack.style.fontSize = 8;
            pack.style.color = new Color(0.54f, 0.59f, 0.7f);
            screen.Add(now);
            screen.Add(title);
            screen.Add(pack);
            screen.Add(BuildBars());
            deck.Add(screen);
            deck.Add(BuildWheel());

            arc = new MiniDeckArc { name = "mini-deck-arc", CornerRadius = DeviceRadius };
            deck.Add(arc);
            minimap.Add(deck);
        }

        // The Deck's bars in the screen, in today's beat colours; they bounce with the beat (no spectrum read here).
        private VisualElement BuildBars()
        {
            var row = new VisualElement { name = "mini-deck-bars", pickingMode = PickingMode.Ignore };
            var rs = row.style;
            rs.flexDirection = FlexDirection.Row;
            rs.alignItems = Align.FlexEnd;
            rs.height = BarMaxHeight;
            rs.marginTop = 4f;
            for (int i = 0; i < bars.Length; i++)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.style.flexGrow = 1f;
                bar.style.marginRight = i < bars.Length - 1 ? 1.5f : 0f;
                bar.style.height = 3f;
                bar.style.borderTopLeftRadius = bar.style.borderTopRightRadius = 1f;
                bar.style.backgroundColor = StepLow;
                bars[i] = bar;
                row.Add(bar);
            }
            return row;
        }

        /// <summary>
        /// The owner's pick (13 Nov 2026, A): an old-school click-wheel under the screen, reading MENU / back / skip / play like the
        /// Deck window's buttons. It is a look only: the Deck (P) is where the music is chosen.
        /// </summary>
        private VisualElement BuildWheel()
        {
            var wheel = new VisualElement { name = "mini-deck-wheel", pickingMode = PickingMode.Ignore };
            wheel.AddToClassList("mini-deck-wheel");
            var ws = wheel.style;
            ws.position = Position.Absolute;
            ws.width = ws.height = WheelSize;
            ws.left = (DeviceWidth - 4f - WheelSize) * 0.5f;
            ws.bottom = 10f;
            Round(ws, WheelSize * 0.5f);
            ws.backgroundColor = new Color(0.1f, 0.13f, 0.21f, 1f);
            Border(ws, 1.5f, new Color(0.18f, 0.23f, 0.35f));
            wheel.Add(WheelLabel("MENU", 0f, -1f, 7));
            wheel.Add(WheelLabel("⏮", -1f, 0f, 9));
            wheel.Add(WheelLabel("⏭", 1f, 0f, 9));
            wheel.Add(WheelLabel("⏯", 0f, 1f, 9));
            var centre = new VisualElement { name = "mini-deck-wheel-centre", pickingMode = PickingMode.Ignore };
            var cs = centre.style;
            cs.position = Position.Absolute;
            cs.width = cs.height = WheelCentre;
            cs.left = cs.top = (WheelSize - WheelCentre) * 0.5f - 1.5f;
            Round(cs, WheelCentre * 0.5f);
            cs.backgroundColor = new Color(0.055f, 0.07f, 0.12f, 1f);
            Border(cs, 1.5f, new Color(0.18f, 0.23f, 0.35f));
            wheel.Add(centre);
            return wheel;
        }

        private static Label WheelLabel(string text, float x, float y, int size)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            var s = label.style;
            s.position = Position.Absolute;
            s.width = s.height = 22f;
            float c = WheelSize * 0.5f - 1.5f - 11f, reach = WheelSize * 0.5f - 13f;
            s.left = c + x * reach;
            s.top = c + y * reach;
            s.unityTextAlign = TextAnchor.MiddleCenter;
            s.fontSize = size;
            s.color = new Color(0.9f, 0.93f, 0.97f, 0.85f);
            s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = 0f;
            s.marginLeft = s.marginRight = s.marginTop = s.marginBottom = 0f;
            return label;
        }

        private static void Round(IStyle s, float r) =>
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = r;

        private static void Border(IStyle s, float width, Color colour)
        {
            s.borderLeftWidth = s.borderRightWidth = s.borderTopWidth = s.borderBottomWidth = width;
            s.borderLeftColor = s.borderRightColor = s.borderTopColor = s.borderBottomColor = colour;
        }

        private void UpdateBars(float pulse)
        {
            float t = Time.unscaledTime;
            for (int i = 0; i < bars.Length; i++)
            {
                float level = 0.25f + 0.75f * Mathf.Abs(Mathf.Sin(t * (2.1f + i * 0.37f) + i)) * (0.55f + 0.45f * pulse);
                bars[i].style.height = 3f + level * (BarMaxHeight - 3f);
                bars[i].style.backgroundColor = level < 0.5f ? StepLow : level < 0.8f ? StepMid : StepHigh;
            }
        }

        private void OnPress(double error, bool onBeat)
        {
            if (onBeat) lit = 1f;
        }

        private void OnCombo(int next, float seconds, bool redline) => step = Mathf.Clamp(next, 0, CadenceCombo.Top);

        private void Update()
        {
            if (deck == null) return;
            bool live = CombatBeat.Live01 > LiveAbove;
            if (live != shown) Show(live);
            UpdateSwap();
            UpdateChat(live);
            if (!live) return;
            MusicTrack track = CombatBeat.Track;
            string name = track != null ? track.title : string.Empty;
            if (title.text != name) title.text = name;
            string bpm = track != null && CombatBeat.BeatSeconds > 0 ? $"{Mathf.RoundToInt((float)(60.0 / CombatBeat.BeatSeconds))} BPM" : string.Empty;
            if (pack.text != bpm) pack.text = bpm;
            UpdateBars(CombatBeat.HasBeat ? MiniDeckBeat.NotchFlash(CombatBeat.Phase01, (float)CombatBeat.BeatSeconds, 0.12f) : 0f);

            bool arcOn = AudioPreferences.BeatRing && CombatBeat.HasBeat;
            arc.visible = arcOn;
            if (!arcOn) return;
            float dt = Time.unscaledDeltaTime;
            lit = Mathf.MoveTowards(Mathf.Min(lit, Accessibility.FlashScale), 0f, dt / LitSeconds);
            arc.Travel = MiniDeckBeat.TickTravel(CombatBeat.Phase01);
            arc.Flash = MiniDeckBeat.NotchFlash(CombatBeat.Phase01, (float)CombatBeat.BeatSeconds, FlashSeconds) * Accessibility.FlashScale;
            arc.Lit = lit;
            arc.LitColour = step >= CadenceCombo.Top ? StepHigh : step >= 2 ? StepMid : StepLow;
            arc.MarkDirtyRepaint();
        }

        // The minimap's own parts step aside; the minimap element itself stays, so the Mini Deck sits where the player put it.
        private void Show(bool on)
        {
            shown = on;
            if (minimap == null || deck == null) return;
            if (!on) { lit = 0f; step = 0; }
            UpdateSwap();
        }

        // One fades fully out before the other fades in (MiniDeckBeat.SwapStep), so they never overlap.
        private void UpdateSwap()
        {
            if (minimap == null || deck == null) return;
            float delta = Time.unscaledDeltaTime / SwapFadeSeconds;
            (mapOpacity, deckOpacity) = MiniDeckBeat.SwapStep(shown, mapOpacity, deckOpacity, delta);
            ApplySwap(minimap, deck, mapOpacity, deckOpacity);
        }

        /// <summary>
        /// The whole minimap block at <paramref name="mapOpacity"/> (owner, 13 Nov 2026: nothing of it behind the Mini Deck): every element
        /// in the minimap (frame, backdrop, map, markers, ring, north, zoom buttons) and the faction's dial on its own canvas
        /// (<see cref="Hud.MinimapChromeView"/>); at 0 they are hidden too. The Mini Deck at <paramref name="deckOpacity"/>.
        /// </summary>
        public static void ApplySwap(VisualElement minimap, VisualElement deck, float mapOpacity, float deckOpacity)
        {
            bool mapGone = mapOpacity <= 0f;
            foreach (VisualElement child in minimap.Children())
            {
                if (child == deck) continue;
                child.style.visibility = mapGone ? Visibility.Hidden : StyleKeyword.Null;
                child.style.opacity = mapOpacity >= 1f ? StyleKeyword.Null : new StyleFloat(mapOpacity);
                child.pickingMode = child is Button ? (mapGone ? PickingMode.Ignore : PickingMode.Position) : child.pickingMode;
            }
            // The faction's dial (ring, frame, buttons) is drawn on its own canvas over the minimap: it fades with the rest.
            Hud.MinimapChromeView.StepAside01 = mapOpacity;
            if (deck == null) return;
            deck.style.display = deckOpacity > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            deck.style.opacity = deckOpacity >= 1f ? StyleKeyword.Null : new StyleFloat(deckOpacity);
        }

        // In a Callout the chat fades, and comes back while the player types in it.
        private void UpdateChat(bool live)
        {
            if (chat == null) return;
            PlayerCombatState combat = LocalPlayer.Get<PlayerCombatState>();
            bool typing = chat.focusController?.focusedElement is VisualElement focused && chat.Contains(focused);
            bool fade = live && combat != null && combat.InCallout && !typing;
            float target = fade ? 0f : 1f;
            if (Mathf.Approximately(chatOpacity, target) && chatFaded == fade) return;
            chatFaded = fade;
            SetChat(Mathf.MoveTowards(chatOpacity, target, Time.unscaledDeltaTime / ChatFadeSeconds));
        }

        private void SetChat(float opacity)
        {
            chatOpacity = opacity;
            if (chat != null) chat.style.opacity = Mathf.Approximately(opacity, 1f) ? StyleKeyword.Null : new StyleFloat(opacity);
        }
    }
}
