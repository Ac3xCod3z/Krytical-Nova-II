using KryticalNova.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The news ticker of the HUD: news every player on the server is told (a GM message, a piece of gear tempered
    /// to the top) runs across a thin band near the top of the screen, right to left, as many times as
    /// <see cref="ToastSettings.passes"/> says, so whoever missed it the first time reads it the next (owner, 15 Oct
    /// 2026; before that a toast showed it once for five seconds). One message at a time (<see cref="ToastQueue"/>);
    /// the server sends the text with its chat line, this only shows it. The kind of news is a coloured tag at the
    /// front of the line (GM gold, Tempering orange) and the names in it take the same colour. Any system can show
    /// one through <see cref="ClientPresentationRegistry.Announcements"/>. It uses the shared panel skin, so each
    /// player sees it in the look of their own faction, and it takes no room while it has nothing to say.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ToastHudController : MonoBehaviour, IAnnouncementSink
    {
        private const string ShownClass = "toast--shown";
        private const string GmClass = "toast--gm";
        private const string TemperClass = "toast--temper";
        private const string GmColour = "#FFB83C", TemperColour = "#FF851F";
        private const float BandTopPercent = 6f, BandHeight = 34f, BandWidthPercent = 46f;

        [SerializeField] private ToastSettings settings;

        private readonly ToastQueue queue = new ToastQueue();
        private VisualElement root, toast;
        private Label track;
        private float shownAt, showSeconds, textWidth, bandWidth;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (settings == null) settings = ScriptableObject.CreateInstance<ToastSettings>();
            root = GetComponent<UIDocument>().rootVisualElement;
            toast = root?.Q("toast");
            if (toast == null)
            {
                Debug.LogError("[Toast] ToastPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            track = toast.Q<Label>("toast-text");
            if (track != null) track.enableRichText = true;
            Label eyebrow = toast.Q<Label>("toast-eyebrow");
            if (eyebrow != null) eyebrow.style.display = DisplayStyle.None; // the kind is now a tag in the line itself
            toast.RemoveFromClassList(ShownClass);
            toast.style.display = DisplayStyle.None;
            // A band across the whole HUD, not a box in the milestone column: it is a line, and must cross the screen.
            // The template's own container carries its style sheet, so it is the container that moves, as a strip
            // across the top of the HUD; the band inside fills it.
            VisualElement carrier = toast.parent is TemplateContainer ? toast.parent : toast;
            if (carrier.parent != root) root.Add(carrier);
            // A framed panel at the top centre of the screen, the line running inside its bounds (owner, 15 Oct 2026:
            // not a bare strip across the screen).
            carrier.pickingMode = PickingMode.Ignore;
            carrier.style.position = Position.Absolute;
            carrier.style.left = Length.Percent(50f);
            carrier.style.translate = new Translate(Length.Percent(-50f), 0f);
            carrier.style.width = Length.Percent(BandWidthPercent);
            carrier.style.top = Length.Percent(BandTopPercent);
            carrier.style.height = BandHeight;
            queue.Clear();
            ClientPresentationRegistry.RegisterAnnouncements(this);
        }

        private void OnDisable() => ClientPresentationRegistry.UnregisterAnnouncements(this);

        public void Announce(AnnouncementKind kind, string sender, string message, string highlight = null)
        {
            if (toast == null || string.IsNullOrEmpty(message)) return;
            queue.Enqueue(new Toast { Kind = kind, Sender = sender, Text = message, Highlight = highlight }, settings.maxWaiting);
        }

        private void Update()
        {
            if (toast == null) return;
            float now = Time.unscaledTime;
            if (queue.Showing && track != null)
                track.style.left = TickerRules.Left(now - shownAt, bandWidth, textWidth, settings.speedPixelsPerSecond);
            if (!queue.Advance(now, showSeconds, settings.fadeSeconds)) return;
            if (!queue.Showing)
            {
                toast.RemoveFromClassList(ShownClass);
                // Gone once it has faded, unless the next one came up meanwhile.
                toast.schedule.Execute(() => { if (!queue.Showing) toast.style.display = DisplayStyle.None; })
                    .StartingIn(Mathf.RoundToInt(settings.fadeSeconds * 1000f) + 50);
                return;
            }

            Toast shown = queue.Current;
            string line = Line(shown);
            toast.EnableInClassList(GmClass, shown.Kind == AnnouncementKind.Gm);
            toast.EnableInClassList(TemperClass, shown.Kind == AnnouncementKind.Temper);
            var fade = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(settings.fadeSeconds, TimeUnit.Second) });
            toast.style.transitionDuration = fade;
            toast.style.display = DisplayStyle.Flex;
            // The band is as wide as the HUD; the text is measured once it is in the panel, so the passes are timed
            // to its real length. Until then it stands just off the right edge.
            float panelWidth = toast.resolvedStyle.width;
            if (float.IsNaN(panelWidth) || panelWidth < 1f) panelWidth = root.worldBound.width * BandWidthPercent / 100f;
            bandWidth = Mathf.Max(1f, panelWidth);
            if (track != null)
            {
                track.text = line;
                Vector2 size = track.MeasureTextSize(line, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
                textWidth = float.IsNaN(size.x) || size.x < 1f ? line.Length * 9f : size.x;
                track.style.left = bandWidth;
            }
            shownAt = now;
            showSeconds = TickerRules.ShowSeconds(bandWidth, textWidth, settings.speedPixelsPerSecond, settings.passes);
            // The class goes on a frame later so the transition has a state to start from.
            toast.schedule.Execute(() => { if (queue.Showing) toast.AddToClassList(ShownClass); });
            toast.BringToFront();
        }

        /// <summary>
        /// The line as it runs: the kind as a coloured tag, then the text with the names it is about in the same colour.
        /// </summary>
        public static string Line(in Toast shown)
        {
            string colour = shown.Kind == AnnouncementKind.Temper ? TemperColour : GmColour;
            string text = shown.Text ?? string.Empty;
            if (!string.IsNullOrEmpty(shown.Highlight))
                foreach (string term in shown.Highlight.Split('|'))
                {
                    string name = term.Trim();
                    if (name.Length == 0) continue;
                    int at = text.IndexOf(name, System.StringComparison.Ordinal);
                    if (at >= 0) text = text.Substring(0, at) + $"<color={colour}>{name}</color>" + text.Substring(at + name.Length);
                }
            return $"<color={colour}>{Heading(shown)}</color>    {text}";
        }

        private static string Heading(in Toast shown)
        {
            switch (shown.Kind)
            {
                case AnnouncementKind.Temper: return "TEMPERING";
                // gm.say speaks as "GM"; the GM's own /gm carries their name, as its chat line does.
                default:
                    return string.IsNullOrEmpty(shown.Sender) || shown.Sender == "GM" ? "GM" : $"GM · {shown.Sender.ToUpperInvariant()}";
            }
        }
    }
}
