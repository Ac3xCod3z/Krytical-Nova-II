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
    /// The device's body waits for the owner's pick (click-wheel or square pad); until then it is a plain screen.
    /// Puts itself on the HUD at run time, as the Mount window does.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MiniDeckController : MonoBehaviour
    {
        private const float LiveAbove = 0.01f;
        private const float FlashSeconds = 0.09f, LitSeconds = 0.45f, ChatFadeSeconds = 0.25f;
        private const float DeviceWidth = 134f, DeviceHeight = 186f, DeviceRadius = 18f;
        // today's Beat Ring colours (BeatRingSettings: low, mid, high), by the combo step reached on the beat
        private static readonly Color StepLow = new Color(0.25f, 0.85f, 1f), StepMid = new Color(1f, 0.72f, 0.18f), StepHigh = new Color(1f, 0.22f, 0.16f);

        private VisualElement root, minimap, deck, chat;
        private MiniDeckArc arc;
        private Label title, pack;
        private bool shown, chatFaded;
        private float lit, chatOpacity = 1f;
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
            title.style.whiteSpace = WhiteSpace.Normal;
            pack = new Label { name = "mini-deck-pack", pickingMode = PickingMode.Ignore };
            pack.style.fontSize = 8;
            pack.style.color = new Color(0.54f, 0.59f, 0.7f);
            screen.Add(now);
            screen.Add(title);
            screen.Add(pack);
            deck.Add(screen);

            arc = new MiniDeckArc { name = "mini-deck-arc", CornerRadius = DeviceRadius };
            deck.Add(arc);
            minimap.Add(deck);
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
            UpdateChat(live);
            if (!live) return;
            MusicTrack track = CombatBeat.Track;
            string name = track != null ? track.title : string.Empty;
            if (title.text != name) title.text = name;
            string bpm = track != null && CombatBeat.BeatSeconds > 0 ? $"{Mathf.RoundToInt((float)(60.0 / CombatBeat.BeatSeconds))} BPM" : string.Empty;
            if (pack.text != bpm) pack.text = bpm;

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
            foreach (VisualElement child in minimap.Children())
                if (child != deck) child.style.visibility = on ? Visibility.Hidden : StyleKeyword.Null;
            deck.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (!on) { lit = 0f; step = 0; }
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
