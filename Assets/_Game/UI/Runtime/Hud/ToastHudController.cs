using KryticalNova.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The toast of the HUD: news every player on the server is told (a GM message, a piece of gear tempered to the
    /// top), near the top of the screen, one at a time (<see cref="ToastQueue"/>). The server sends the text with
    /// its chat line; this only shows it. Any system can show one through
    /// <see cref="ClientPresentationRegistry.Announcements"/>. It uses the shared panel skin, so each player sees
    /// it in the look of their own faction. Timings are in <see cref="ToastSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ToastHudController : MonoBehaviour, IAnnouncementSink
    {
        private const string ShownClass = "toast--shown";
        private const string GmClass = "toast--gm";
        private const string TemperClass = "toast--temper";

        [SerializeField] private ToastSettings settings;

        private readonly ToastQueue queue = new ToastQueue();
        private VisualElement toast;
        private Label eyebrow;
        private Label text;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            if (settings == null) settings = ScriptableObject.CreateInstance<ToastSettings>();
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            toast = root?.Q("toast");
            if (toast == null)
            {
                Debug.LogError("[Toast] ToastPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            eyebrow = toast.Q<Label>("toast-eyebrow");
            text = toast.Q<Label>("toast-text");
            toast.RemoveFromClassList(ShownClass);
            queue.Clear();
            ClientPresentationRegistry.RegisterAnnouncements(this);
        }

        private void OnDisable() => ClientPresentationRegistry.UnregisterAnnouncements(this);

        public void Announce(AnnouncementKind kind, string sender, string message)
        {
            if (toast == null || string.IsNullOrEmpty(message)) return;
            queue.Enqueue(new Toast { Kind = kind, Sender = sender, Text = message }, settings.maxWaiting);
        }

        private void Update()
        {
            if (toast == null || !queue.Advance(Time.unscaledTime, settings.showSeconds, settings.fadeSeconds)) return;
            if (!queue.Showing)
            {
                toast.RemoveFromClassList(ShownClass);
                return;
            }

            Toast shown = queue.Current;
            eyebrow.text = Heading(shown);
            text.text = shown.Text;
            toast.EnableInClassList(GmClass, shown.Kind == AnnouncementKind.Gm);
            toast.EnableInClassList(TemperClass, shown.Kind == AnnouncementKind.Temper);
            var fade = new StyleList<TimeValue>(new System.Collections.Generic.List<TimeValue> { new TimeValue(settings.fadeSeconds, TimeUnit.Second) });
            toast.style.transitionDuration = fade;
            toast.AddToClassList(ShownClass);
            toast.BringToFront();
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
