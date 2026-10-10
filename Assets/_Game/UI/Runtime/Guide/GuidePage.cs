using System;
using System.Collections.Generic;
using KryticalNova.UI.Menus;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Guide
{
    /// <summary>
    /// The Guide page of Settings: the topics of the <see cref="GuideLibrary"/> listed by chapter on the left, and
    /// the topic being read on the right: its clip, what it is, where it is done, and then its sections (steps,
    /// tables, tips, what can go wrong). The page is built from the parts every window shares, so it wears the
    /// faction's skin with no layout of its own per faction. Keys in the text are the ones the player has bound and
    /// follow a rebind. The topic last read is where the page opens next time.
    /// </summary>
    public sealed class GuidePage
    {
        private readonly GuideClipPlayer clips = new GuideClipPlayer();
        private readonly List<GuideTopic> order = new List<GuideTopic>();
        private readonly Dictionary<GuideTopic, Button> rows = new Dictionary<GuideTopic, Button>();
        private readonly List<GuideText.Token> scratch = new List<GuideText.Token>();
        private GuideLibrary library;
        private Func<InputActionAsset> playerActions;
        private VisualElement page, screen;
        private ScrollView reading;
        private GuideTopic current;
        private VisualElement head, frame;
        private Label enlargeHint;
        private bool large;
        // The most room the clip's screen takes: beside the text, and enlarged over the whole width of the page.
        private static readonly UnityEngine.Vector2 SmallScreen = new UnityEngine.Vector2(336f, 250f);
        private const float LargeScreenHeight = 430f, LargeScreenGap = 34f;
        private bool showing;

        public GuideTopic Current => current;

        public void Build(VisualElement target, GuideLibrary guide, Func<InputActionAsset> actions)
        {
            Hidden();
            page = target;
            library = guide;
            playerActions = actions;
            if (page == null) return;
            page.Clear();
            order.Clear();
            rows.Clear();
            current = null;
            if (library == null)
            {
                SettingsRows.Help(page, "The Guide has not been set up in this scene.");
                return;
            }

            var body = new VisualElement();
            body.AddToClassList("guide");
            page.Add(body);

            var index = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            index.AddToClassList("guide-index");
            body.Add(index);
            foreach (GuideChapter chapter in library.chapters)
            {
                if (chapter.topics.Count == 0) continue;
                var heading = new Label(chapter.title.ToUpperInvariant());
                heading.AddToClassList("eyebrow");
                heading.AddToClassList("guide-chapter");
                index.Add(heading);
                foreach (GuideTopic topic in chapter.topics)
                {
                    GuideTopic chosen = topic;
                    var row = new Button(() => Open(chosen, true)) { text = topic.title };
                    row.AddToClassList("guide-index-row");
                    index.Add(row);
                    rows[topic] = row;
                    order.Add(topic);
                }
            }

            reading = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            reading.AddToClassList("guide-reading");
            body.Add(reading);

            Open(library.Find(HudPreferences.GuideTopic) ?? (order.Count > 0 ? order[0] : null), false);
        }

        /// <summary>The page came into view: its clip plays.</summary>
        public void Shown()
        {
            if (showing) return;
            showing = true;
            // Drawn again as it comes into view: a key may have been rebound on the Controls page meanwhile.
            if (current != null) Open(current, false);
        }

        /// <summary>The page went out of view (another tab, the window closed): nothing of it keeps running.</summary>
        public void Hidden()
        {
            showing = false;
            clips.Stop();
        }

        /// <summary>Opens the topic of that id; false when there is none.</summary>
        public bool Open(string topicId)
        {
            GuideTopic topic = library != null ? library.Find(topicId) : null;
            if (topic == null) return false;
            Open(topic, false);
            return true;
        }

        private void Open(GuideTopic topic, bool byHand)
        {
            if (topic == null || reading == null) return;
            bool another = current != topic;
            current = topic;
            HudPreferences.GuideTopic = topic.id;
            foreach (var pair in rows) pair.Value.EnableInClassList("active", pair.Key == topic);

            reading.Clear();
            reading.scrollOffset = UnityEngine.Vector2.zero;
            VisualElement content = reading.contentContainer;

            // The clip, with what the topic is and where it is done beside it.
            head = new VisualElement();
            head.AddToClassList("guide-head");
            content.Add(head);
            frame = new VisualElement();
            frame.AddToClassList("guide-screen-frame");
            frame.AddToClassList("kn-card");
            screen = new VisualElement { pickingMode = PickingMode.Ignore };
            screen.AddToClassList("guide-screen");
            frame.Add(screen);
            enlargeHint = new Label { pickingMode = PickingMode.Ignore };
            enlargeHint.AddToClassList("guide-screen-hint");
            frame.Add(enlargeHint);
            // A click on the clip shows it large, over the whole width of the page; another click puts it back.
            frame.RegisterCallback<ClickEvent>(_ => SetLarge(!large));
            if (another) large = false;
            SizeScreen();
            head.Add(frame);

            var about = new VisualElement();
            about.AddToClassList("guide-about");
            head.Add(about);
            var title = new Label(topic.title.ToUpperInvariant());
            title.AddToClassList("heading");
            title.AddToClassList("guide-title");
            about.Add(title);
            if (!string.IsNullOrEmpty(topic.summary))
            {
                VisualElement summary = Line(topic.summary);
                summary.AddToClassList("guide-summary");
                about.Add(summary);
            }
            foreach (GuideSection section in topic.sections)
                if (section.kind == GuideSectionKind.Where) AddSection(about, section);

            foreach (GuideSection section in topic.sections)
                if (section.kind != GuideSectionKind.Where) AddSection(content, section);

            AddFoot(content, topic);
            // The pack's frame round the clip and its bar under each heading (the same layers every window gets).
            SyntySkin.Decorate(reading);
            if (showing) PlayClip();
            if (byHand && another) MenuMoments.Raise(MenuMoment.Tab, reading);
        }

        /// <summary>Shows the clip large (over the page's width, the text under it) or at its place beside the text.</summary>
        public void SetLarge(bool on)
        {
            if (large == on) return;
            large = on;
            SizeScreen();
            if (frame != null) MenuMoments.Raise(MenuMoment.Toggle, frame);
        }

        /// <summary>
        /// The screen takes the shape of what the topic shows of its clip (a tall window gets a tall screen), as
        /// large as its room allows: beside the text, or, enlarged, across the page with the text under it.
        /// </summary>
        private void SizeScreen()
        {
            if (frame == null || current == null) return;
            float aspect = UnityEngine.Mathf.Max(0.2f, current.ShownAspect);
            float room = reading != null ? reading.contentContainer.resolvedStyle.width : float.NaN;
            UnityEngine.Vector2 box = large
                ? new UnityEngine.Vector2(float.IsNaN(room) || room < SmallScreen.x ? 560f : room - LargeScreenGap, LargeScreenHeight)
                : SmallScreen;
            float width = UnityEngine.Mathf.Min(box.x, box.y * aspect);
            frame.style.width = frame.style.minWidth = width;
            frame.style.height = width / aspect;
            head.EnableInClassList("guide-head--large", large);
            enlargeHint.text = large ? "CLICK TO SHRINK" : "CLICK TO ENLARGE";
        }

        private void PlayClip()
        {
            if (current != null && screen != null) clips.Show(screen, current.clip, current.still, current.focus);
        }

        private VisualElement Line(string text) => GuideText.Build(text, library, playerActions?.Invoke(), scratch);

        private void AddSection(VisualElement into, GuideSection section)
        {
            var block = new VisualElement();
            block.AddToClassList("guide-section");
            block.AddToClassList("guide-section--" + section.kind.ToString().ToLowerInvariant());
            into.Add(block);
            string headingText = !string.IsNullOrEmpty(section.heading) ? section.heading
                : section.kind == GuideSectionKind.Where ? "Where"
                : section.kind == GuideSectionKind.Tip ? "Good to know"
                : section.kind == GuideSectionKind.Warning ? "Take care" : null;
            if (headingText != null)
            {
                var heading = new Label(headingText.ToUpperInvariant());
                // A boxed section names itself inside its box; the others get the pack's bar under the heading.
                heading.AddToClassList(Boxed(section.kind) ? "guide-box-heading" : "eyebrow");
                block.Add(heading);
            }

            int number = 0;
            foreach (string text in section.lines)
            {
                if (string.IsNullOrWhiteSpace(text)) continue;
                switch (section.kind)
                {
                    case GuideSectionKind.Steps:
                    {
                        var step = new VisualElement();
                        step.AddToClassList("guide-step");
                        var count = new Label((++number).ToString()) { pickingMode = PickingMode.Ignore };
                        count.AddToClassList("guide-step-number");
                        step.Add(count);
                        step.Add(Line(text));
                        block.Add(step);
                        break;
                    }
                    case GuideSectionKind.Table:
                    {
                        int cut = text.IndexOf('|');
                        var row = new VisualElement();
                        row.AddToClassList("guide-table-row");
                        VisualElement left = Line(cut < 0 ? text : text.Substring(0, cut).Trim());
                        left.AddToClassList("guide-table-name");
                        row.Add(left);
                        if (cut >= 0)
                        {
                            VisualElement right = Line(text.Substring(cut + 1).Trim());
                            right.AddToClassList("guide-table-value");
                            row.Add(right);
                        }
                        block.Add(row);
                        break;
                    }
                    default:
                        block.Add(Line(text));
                        break;
                }
            }
        }

        private static bool Boxed(GuideSectionKind kind)
            => kind == GuideSectionKind.Tip || kind == GuideSectionKind.Warning || kind == GuideSectionKind.Where;

        /// <summary>The topic before and the topic after, by name.</summary>
        private void AddFoot(VisualElement into, GuideTopic topic)
        {
            int at = order.IndexOf(topic);
            var foot = new VisualElement();
            foot.AddToClassList("guide-foot");
            into.Add(foot);
            if (at > 0) foot.Add(Turn(order[at - 1], false));
            var gap = new VisualElement();
            gap.AddToClassList("guide-foot-gap");
            foot.Add(gap);
            if (at >= 0 && at + 1 < order.Count) foot.Add(Turn(order[at + 1], true));
        }

        private Button Turn(GuideTopic to, bool forward)
        {
            var button = new Button(() => Open(to, true)) { text = forward ? to.title.ToUpperInvariant() + "  >" : "<  " + to.title.ToUpperInvariant() };
            button.AddToClassList("guide-turn");
            return button;
        }
    }
}
