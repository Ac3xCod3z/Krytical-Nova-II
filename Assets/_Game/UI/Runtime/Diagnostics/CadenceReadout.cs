using System.Text;
using KryticalNova.Audio;
using KryticalNova.Combat;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI
{
    /// <summary>
    /// The GM's Cadence readout (gm.cadence window on): what the fight music and its beat are doing on this machine
    /// and how this machine's player is pressing against it. A lane shows the beats coming towards a "now" line,
    /// with the stretch round each beat that counts as on it, and each press where it fell; a second strip shows
    /// where the last presses fell round the beat; under them the last press, how many were on and off the beat,
    /// how many casts hit harder, and the average timing. It is a test tool for the local player: where the game is
    /// decided (the host, or offline) it reads the presses here; joined to a server it shows what the server sends
    /// while a GM has it on (owner, 21 Oct 2026). Like the performance readout it is drawn on a canvas of its own made
    /// here in code, on the right edge half way down, and costs nothing while it is off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CadenceReadout : MonoBehaviour
    {
        private const float RefreshSeconds = 0.1f;
        private const float Margin = 12f, Padding = 8f, Gap = 6f;
        private const int CanvasOrder = 31990;      // just under the performance readout
        private const int FontSize = 13;
        // The lane: this much of the past and of what is coming, in seconds.
        private const float LaneWidth = 360f, LaneHeight = 44f, LanePast = 0.9f, LaneAhead = 2.1f;
        // The strip of where presses fall round the beat: this far either side, in milliseconds.
        private const float StripHeight = 26f, StripReachMs = 260f;
        private const int Ticks = 12, Dots = PlayerCadence.HistorySize;

        private static readonly Color Backing = new Color(0f, 0f, 0f, 0.74f);
        private static readonly Color LaneBack = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color WindowBand = new Color(0.25f, 0.85f, 0.5f, 0.28f);
        private static readonly Color BeatTick = new Color(0.9f, 0.95f, 1f, 0.75f);
        private static readonly Color BarTick = new Color(1f, 0.82f, 0.35f, 1f);
        private static readonly Color NowLine = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color OnBeat = new Color(0.3f, 0.95f, 0.5f, 1f);
        private static readonly Color OffBeat = new Color(1f, 0.38f, 0.33f, 1f);
        private static readonly Color Unjudged = new Color(0.65f, 0.65f, 0.7f, 1f);
        private static readonly Color Words = new Color(0.86f, 0.95f, 0.9f);

        private static CadenceReadout instance;

        private Canvas canvas;
        private RectTransform plate, lane, strip;
        private Text top, bottom;
        private Image nowLine;
        private readonly Image[] bands = new Image[Ticks];
        private readonly Image[] ticks = new Image[Ticks];
        private readonly Image[] laneDots = new Image[Dots];
        private readonly Image[] stripDots = new Image[Dots];
        private Image stripBand;
        private readonly StringBuilder text = new StringBuilder(512);
        private float nextRefresh;
        private PlayerInventory owner;
        private PlayerCadence cadence;
        private PlayerCombatState combat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null || Application.isBatchMode) return;
            var host = new GameObject("Cadence Readout");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CadenceReadout>();
        }

        private void Awake()
        {
            PlayerCadence.ReadoutChanged += Apply;
            Apply();
        }

        private void OnDestroy()
        {
            PlayerCadence.ReadoutChanged -= Apply;
            if (instance == this) instance = null;
        }

        // The component is on only while the readout is: switched off, it has no Update and its canvas is not drawn.
        private void Apply()
        {
            bool on = PlayerCadence.ReadoutShown || PlayerCadence.ReadoutSentHere;
            if (enabled != on) enabled = on;
            if (on && canvas == null) BuildView();
            if (canvas != null) canvas.gameObject.SetActive(on);
            nextRefresh = 0f;
        }

        private void Update()
        {
            FindPlayer();
            // A GM tool, with a player to read: where the game is decided, or on a player's own game the server sends to.
            NetworkManager network = NetworkManager.Singleton;
            bool decidedHere = network == null || !network.IsListening || network.IsServer;
            bool show = cadence != null && (decidedHere ? PlayerCadence.ReadoutShown : PlayerCadence.ReadoutSentHere);
            if (plate.gameObject.activeSelf != show) plate.gameObject.SetActive(show);
            if (!show) return;

            DrawLane();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            DrawStrip();
            Write();
        }

        private void FindPlayer()
        {
            PlayerInventory local = PlayerInventory.Local;
            if (local == owner) return;
            owner = local;
            cadence = local != null ? local.GetComponent<PlayerCadence>() : null;
            combat = local != null ? local.GetComponent<PlayerCombatState>() : null;
        }

        // ------------------------------------------------------------------ the lane: beats coming, presses made

        private float LaneX(float secondsFromNow) => (secondsFromNow + LanePast) / (LanePast + LaneAhead) * LaneWidth;

        private void DrawLane()
        {
            bool beat = CombatBeat.HasBeat;
            float beatSeconds = beat ? (float)CombatBeat.BeatSeconds : 0f;
            float window = cadence.Settings != null ? cadence.Settings.WindowSeconds : 0f;
            long first = beat ? (long)System.Math.Ceiling(CombatBeat.Beats - LanePast / beatSeconds) : 0;
            MusicTrack track = CombatBeat.Track;

            for (int i = 0; i < Ticks; i++)
            {
                long index = first + i;
                float at = beat ? (float)((index - CombatBeat.Beats) * beatSeconds) : 0f;
                bool seen = beat && at <= LaneAhead;
                ticks[i].enabled = bands[i].enabled = seen;
                if (!seen) continue;
                bool bar = BeatMath.IsBarBeat(index, track.barBeat, track.beatsPerBar);
                Place(ticks[i].rectTransform, LaneX(at), bar ? 3f : 2f, bar ? LaneHeight : LaneHeight * 0.6f);
                ticks[i].color = bar ? BarTick : BeatTick;
                Place(bands[i].rectTransform, LaneX(at), window * 2f / (LanePast + LaneAhead) * LaneWidth, LaneHeight);
            }

            // The line the beats run into flares on each beat.
            Color line = NowLine;
            line.a = Mathf.Lerp(NowLine.a, 1f, CombatBeat.Pulse(8f));
            nowLine.color = line;

            int count = cadence.HistoryCount;
            for (int i = 0; i < Dots; i++)
            {
                bool seen = false;
                if (beat && i < count)
                {
                    CadencePress press = cadence.Press(i);
                    float at = (float)((press.Beats - CombatBeat.Beats) * beatSeconds);
                    seen = press.Track == track && press.Verdict != CadenceVerdict.NoBeat && at >= -LanePast && at <= 0.05f;
                    if (seen)
                    {
                        Place(laneDots[i].rectTransform, LaneX(at), 8f, 8f);
                        laneDots[i].color = ColourOf(press.Verdict);
                    }
                }
                laneDots[i].enabled = seen;
            }
        }

        // Where the last presses fell round the beat: early to the left, late to the right, the newest brightest.
        private void DrawStrip()
        {
            float window = cadence.Settings != null ? cadence.Settings.windowMs : 0f;
            Place(stripBand.rectTransform, LaneWidth * 0.5f, window / StripReachMs * LaneWidth, StripHeight);
            int count = cadence.HistoryCount;
            for (int i = 0; i < Dots; i++)
            {
                bool seen = false;
                if (i < count)
                {
                    CadencePress press = cadence.Press(i);
                    seen = press.Verdict == CadenceVerdict.OnCadence || press.Verdict == CadenceVerdict.OffBeat;
                    if (seen)
                    {
                        float x = (Mathf.Clamp(press.ErrorMs, -StripReachMs, StripReachMs) / StripReachMs * 0.5f + 0.5f) * LaneWidth;
                        float size = i == 0 ? 10f : 7f;
                        Place(stripDots[i].rectTransform, x, size, size);
                        Color colour = ColourOf(press.Verdict);
                        colour.a = i == 0 ? 1f : Mathf.Lerp(0.75f, 0.2f, (float)i / Dots);
                        stripDots[i].color = colour;
                    }
                }
                stripDots[i].enabled = seen;
            }
        }

        private static Color ColourOf(CadenceVerdict verdict) =>
            verdict == CadenceVerdict.OnCadence ? OnBeat : verdict == CadenceVerdict.OffBeat ? OffBeat : Unjudged;

        private static void Place(RectTransform rect, float x, float width, float height)
        {
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(width, height);
        }

        // ------------------------------------------------------------------ the words

        private void Write()
        {
            CadenceSettings settings = cadence.Settings;
            text.Clear();
            text.Append("CADENCE");
            if (settings != null && !settings.enabled) text.Append("   (switched OFF in its settings)");
            text.Append('\n');
            Line("Fight", combat != null && combat.InCombat ? "in a fight: " + combat.Reasons : "not in a fight");
            MusicTrack track = CombatBeat.Track;
            if (CombatBeat.HasBeat)
            {
                long inBar = ((CombatBeat.Index - track.barBeat) % track.beatsPerBar + track.beatsPerBar) % track.beatsPerBar;
                Line("Track", track.Title + "   " + track.beatsPerMinute.ToString("0.##") + " beats a minute");
                Line("Beat", (inBar + 1) + " of " + track.beatsPerBar + "   (" + (track.BeatSeconds * 1000.0).ToString("0") + " ms apart)");
                Line("Sound", "volume " + (CombatBeat.Live01 * 100f).ToString("0") + " %, "
                    + Signed((CombatBeat.ClockLagSeconds - CombatBeat.OutputDelaySeconds) * 1000.0) + " ms from the shared clock");
            }
            else
            {
                Line("Track", CombatBeat.Live01 > 0f ? "between two tracks" : MusicConductor.Instance == null ? "this scene has no fight music" : "silent: not in a fight");
            }
            Line("Beat offset", Signed(CombatBeat.OutputDelaySeconds * 1000.0) + " ms  (Settings, Audio)");
            text.Append("\nBEATS COMING   gold = first of the bar, green = counts as on the beat");
            top.text = text.ToString();

            text.Clear();
            // Two empty lines under the heading: the strip is drawn there.
            text.Append("WHERE PRESSES FELL   early < beat > late, ").Append(StripReachMs.ToString("0")).Append(" ms each way").Append('\n', 3);
            if (cadence.HistoryCount == 0)
            {
                Line("Last key", "none yet: press a skill key in a fight");
            }
            else
            {
                CadencePress last = cadence.Press(0);
                bool judged = last.Verdict == CadenceVerdict.OnCadence || last.Verdict == CadenceVerdict.OffBeat;
                Line("Last key", last.Skill);
                Line("Fell", judged ? Mathf.Abs(last.ErrorMs).ToString("0") + " ms " + (last.ErrorMs < 0f ? "early" : "late") : "-");
                Line("Verdict", last.Verdict == CadenceVerdict.OnCadence ? "ON BEAT" : last.Verdict == CadenceVerdict.OffBeat ? "OFF BEAT" : PlayerCadence.Words(last.Verdict));
                if (last.Verdict == CadenceVerdict.OnCadence) Line("Bonus", FateWords(last, settings));
                // On the host itself the two clocks are one, give or take the sound's own lag: never "before the press".
                Line("Server heard", Mathf.Max(0f, last.HeardAfterMs).ToString("0") + " ms after the press");
            }
            int on = cadence.PressesOn, off = cadence.PressesOff;
            text.Append('\n');
            Line("Hits on beat", on.ToString());
            Line("Hits off beat", off.ToString());
            Line("Share on beat", on + off > 0 ? (100f * on / (on + off)).ToString("0") + " %" : "-");
            Line("Casts boosted", cadence.CastsOnCadence + (settings != null ? "   (+" + settings.bonusPercent.ToString("0.#") + " % damage each)" : string.Empty));
            if (cadence.PressesRefused > 0) Line("Not judged", cadence.PressesRefused + "   (no beat, or not in a fight)");
            if (on + off > 0)
            {
                float average = cadence.AverageErrorMs;
                Line("Average", Mathf.Abs(average).ToString("0") + " ms " + (average < 0f ? "early" : "late") + ", spread " + cadence.SpreadMs.ToString("0") + " ms");
            }
            if (settings != null) Line("Window", settings.windowMs.ToString("0") + " ms either side of the beat");
            // Presses that sit to one side of the beat as a habit are usually the sound arriving late, not the player.
            if (on + off >= 8 && Mathf.Abs(cadence.AverageErrorMs) >= 20f)
                text.Append("\nYour presses sit ").Append(cadence.AverageErrorMs < 0f ? "early" : "late").Append(": try Beat timing offset ")
                    .Append(Signed(CombatBeat.OutputDelaySeconds * 1000.0 + cadence.AverageErrorMs)).Append(" ms.\n");
            text.Append("\ngm.cadence reset · gm.cadence window off");
            bottom.text = text.ToString();
            Lay();
        }

        private static string FateWords(in CadencePress press, CadenceSettings settings)
        {
            switch (press.Fate)
            {
                case CadenceFate.Spent: return "spent on the cast";
                case CadenceFate.Lost: return "lost: no cast came in time";
                default:
                    return settings != null && Time.time - press.At > settings.keepSeconds ? "lost: no cast came in time" : "kept, waiting for the cast";
            }
        }

        private static string Signed(double value) => (value > 0.5 ? "+" : string.Empty) + value.ToString("0");

        private void Line(string label, string value) => text.Append(label.PadRight(15)).Append(value).Append('\n');

        // ------------------------------------------------------------------ the view

        // Top words, the lane, the strip's heading and the strip, the rest of the words: stacked on one plate.
        private void Lay()
        {
            canvas.scaleFactor = Mathf.Max(1f, Screen.height / 1080f);
            float width = Mathf.Max(LaneWidth, Mathf.Max(top.preferredWidth, bottom.preferredWidth));
            float topHeight = top.preferredHeight, bottomHeight = bottom.preferredHeight;
            float headingHeight = FontSize * 1.25f; // the strip sits under the first line of the bottom words
            float y = Padding;
            Set(top.rectTransform, y, width, topHeight);
            y += topHeight + Gap;
            Set(lane, y, LaneWidth, LaneHeight);
            y += LaneHeight + Gap;
            Set(bottom.rectTransform, y, width, bottomHeight);
            Set(strip, y + headingHeight + 2f, LaneWidth, StripHeight);
            y += bottomHeight;
            plate.sizeDelta = new Vector2(width + Padding * 2f, y + Padding);
        }

        private static void Set(RectTransform rect, float fromTop, float width, float height)
        {
            rect.anchoredPosition = new Vector2(Padding, -fromTop);
            rect.sizeDelta = new Vector2(width, height);
        }

        private void BuildView()
        {
            var host = new GameObject("Cadence Readout Canvas", typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            host.transform.SetParent(transform, false);
            canvas = host.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;

            // A plate on the right edge, half way down. Nothing here can be clicked.
            plate = Child("Plate", host.transform, Backing).rectTransform;
            plate.anchorMin = plate.anchorMax = new Vector2(1f, 0.5f);
            plate.pivot = new Vector2(1f, 0.5f);
            plate.anchoredPosition = new Vector2(-Margin, 0f);

            Font font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Menlo", "DejaVu Sans Mono" }, FontSize);
            top = Label("Top", font);
            bottom = Label("Bottom", font);

            lane = TopLeft(Child("Lane", plate, LaneBack).rectTransform);
            lane.gameObject.AddComponent<RectMask2D>(); // what runs off either end of a lane is cut there
            for (int i = 0; i < Ticks; i++) bands[i] = Mark("Band", lane, WindowBand);
            for (int i = 0; i < Ticks; i++) ticks[i] = Mark("Beat", lane, BeatTick);
            nowLine = Mark("Now", lane, NowLine);
            Place(nowLine.rectTransform, LaneX(0f), 2f, LaneHeight);
            for (int i = 0; i < Dots; i++) laneDots[i] = Mark("Press", lane, OnBeat);

            strip = TopLeft(Child("Strip", plate, LaneBack).rectTransform);
            strip.gameObject.AddComponent<RectMask2D>();
            stripBand = Mark("Window", strip, WindowBand);
            Image centre = Mark("Beat", strip, BarTick);
            Place(centre.rectTransform, LaneWidth * 0.5f, 2f, StripHeight);
            for (int i = 0; i < Dots; i++) stripDots[i] = Mark("Press", strip, OnBeat);
        }

        private Text Label(string name, Font font)
        {
            var made = new GameObject(name, typeof(RectTransform)) { layer = plate.gameObject.layer };
            TopLeft((RectTransform)made.transform).SetParent(plate, false);
            Text label = made.AddComponent<Text>();
            label.font = font;
            label.fontSize = FontSize;
            label.color = Words;
            label.alignment = TextAnchor.UpperLeft;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private Image Child(string name, Transform parent, Color colour)
        {
            var made = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
            made.transform.SetParent(parent, false);
            Image image = made.AddComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        // A mark on a lane: placed by its centre along the lane, centred on the lane's height.
        private Image Mark(string name, RectTransform on, Color colour)
        {
            Image image = Child(name, on, colour);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return image;
        }

        private static RectTransform TopLeft(RectTransform rect)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            return rect;
        }
    }
}
