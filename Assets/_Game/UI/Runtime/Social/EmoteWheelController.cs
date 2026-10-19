using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Player;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using KryticalNova.Core;

namespace KryticalNova.UI
{
    /// <summary>
    /// The emote wheel. While its key is held a ring of categories is on screen; a click on one turns the ring into
    /// that category's emotes (a page at a time: the mouse wheel turns the page, the hub or a right-click goes back);
    /// a click on an emote performs it and closes the wheel, and letting go of the key closes it at any time. The
    /// wedge under the pointer is lit. What is on the wheel comes from <see cref="EmoteWheelSettings"/> through
    /// <see cref="EmoteCatalog"/>; performing is <see cref="PlayerEmotes"/>, which asks the server.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class EmoteWheelController : MonoBehaviour
    {
        private const int Hub = -2, Nothing = -1;
        private const int LeftButton = 0, RightButton = 1;
        // Shares of the wheel's radius.
        private const float InnerRadius = 0.36f, HubRadius = 0.3f, LabelRadius = 0.69f, ReachPastRim = 1.2f;
        private const float GapDegrees = 2.5f, TopDegrees = -90f;
        private const float HoverPush = 7f;
        private const string HoveredClass = "hovered", CategoryClass = "category", StanceClass = "stance";

        private const string NamecardLayer = "player-namecards";
        private VisualElement root, screen, wheel, canvas, labelLayer, fill, hover, line, namecards;
        private Label title, hint;
        private readonly List<Label> labels = new List<Label>(12);
        private readonly List<List<EmoteEntry>> catalog = new List<List<EmoteEntry>>(8);
        private readonly List<int> shownCategories = new List<int>(8);
        private readonly List<EmoteEntry> pageEntries = new List<EmoteEntry>(12);
        private PlayerEmotes emotes;
        private int category = Nothing;  // which category's emotes are on the ring; Nothing while it shows the categories
        private int page;
        private int wedges;
        private int hovered = Nothing;
        private bool open;
        private bool waitForRelease;     // an emote was picked: the key has to come up before the wheel opens again
        private bool initialized;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            screen = root?.Q("emote-wheel-screen");
            if (screen == null)
            {
                Debug.LogError("[EmoteWheel] EmoteWheel.uxml is not instanced in this UI document.", this);
                return;
            }
            wheel = screen.Q("emote-wheel");
            canvas = screen.Q("emote-wheel-canvas");
            labelLayer = screen.Q("emote-wheel-labels");
            fill = screen.Q("emote-wheel-fill");
            hover = screen.Q("emote-wheel-hover");
            line = screen.Q("emote-wheel-line");
            title = screen.Q<Label>("emote-wheel-title");
            hint = screen.Q<Label>("emote-wheel-hint");
            namecards = root.Q(NamecardLayer);
            canvas.generateVisualContent += Draw;
            screen.RegisterCallback<PointerDownEvent>(OnPointerDown);
            screen.RegisterCallback<WheelEvent>(OnWheel);
            screen.style.display = DisplayStyle.None;
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            canvas.generateVisualContent -= Draw;
            screen.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            screen.UnregisterCallback<WheelEvent>(OnWheel);
            if (open) ShowNamecards(true);
            open = false;
        }

        public bool IsOpen => open;

        private void Update()
        {
            if (!initialized) return;
            bool held = !HudTyping.IsTyping(root) && KeyBindings.Held(KeyBindings.EmoteWheel);
            if (waitForRelease)
            {
                if (!held) waitForRelease = false;
                return;
            }
            if (held && !open) Open();
            else if (!held && open) Close();
            if (open) TrackPointer();
        }

        // ------------------------------------------------------------------ open / close

        private void Open()
        {
            emotes = PlayerEmotes.Local;
            if (emotes == null || emotes.Settings == null || emotes.Settings.library == null)
            {
                waitForRelease = true; // nothing to show for this character: do not try again every frame
                return;
            }
            EmoteCatalog.Collect(emotes.Settings, emotes.Body, catalog);
            shownCategories.Clear();
            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i].Count > 0) shownCategories.Add(i);
            if (shownCategories.Count == 0)
            {
                waitForRelease = true;
                return;
            }
            category = Nothing;
            page = 0;
            hovered = Nothing;
            open = true;
            screen.style.display = DisplayStyle.Flex;
            screen.BringToFront();
            ShowNamecards(false); // the player's own would sit right under the wheel's labels
            Rebuild();
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void Close()
        {
            open = false;
            screen.style.display = DisplayStyle.None;
            ShowNamecards(true);
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void ShowNamecards(bool show)
        {
            if (namecards != null) namecards.style.visibility = show ? Visibility.Visible : Visibility.Hidden;
        }

        // ------------------------------------------------------------------ what is on the ring

        private void Rebuild()
        {
            EmoteWheelSettings settings = emotes.Settings;
            pageEntries.Clear();
            if (category == Nothing)
            {
                wedges = shownCategories.Count;
                title.text = "EMOTES";
                hint.text = "Pick a category";
            }
            else
            {
                List<EmoteEntry> entries = catalog[category];
                int size = Mathf.Max(1, settings.pageSize);
                int pages = EmoteCatalog.PageCount(entries.Count, size);
                page = Mathf.Clamp(page, 0, pages - 1);
                for (int i = page * size; i < entries.Count && i < (page + 1) * size; i++) pageEntries.Add(entries[i]);
                wedges = size; // the ring keeps its shape on a page that is not full
                title.text = settings.categories[category].name.ToUpperInvariant();
                bool anyStance = false;
                foreach (EmoteEntry entry in pageEntries) anyStance |= entry.Stance;
                hint.text = (pages > 1 ? $"{page + 1} / {pages}   scroll for more\n" : string.Empty) + "click here to go back"
                    + (anyStance ? "\ncoloured: held until you move" : string.Empty);
            }

            while (labels.Count < wedges)
            {
                var label = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
                label.AddToClassList("emote-wheel-label");
                labelLayer.Add(label);
                labels.Add(label);
            }
            for (int i = 0; i < labels.Count; i++)
            {
                Label label = labels[i];
                bool used = i < wedges && IsFilled(i);
                label.style.display = used ? DisplayStyle.Flex : DisplayStyle.None;
                if (!used) continue;
                bool isCategory = category == Nothing;
                label.text = isCategory ? settings.categories[shownCategories[i]].name.ToUpperInvariant() : pageEntries[i].Label;
                label.EnableInClassList(CategoryClass, isCategory);
                label.EnableInClassList(StanceClass, !isCategory && pageEntries[i].Stance);
                label.EnableInClassList(HoveredClass, i == hovered);
                // Share of the wheel's box: centre plus the wedge's direction.
                float angle = (TopDegrees + i * 360f / wedges) * Mathf.Deg2Rad;
                label.style.left = Length.Percent(50f + Mathf.Cos(angle) * LabelRadius * 50f);
                label.style.top = Length.Percent(50f + Mathf.Sin(angle) * LabelRadius * 50f);
            }
            canvas.MarkDirtyRepaint();
        }

        /// <summary>A wedge with something on it. The last page of a category leaves some empty.</summary>
        private bool IsFilled(int wedge) => wedge >= 0 && wedge < wedges && (category == Nothing || wedge < pageEntries.Count);

        // ------------------------------------------------------------------ pointer

        private void TrackPointer()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || root.panel == null) return;
            Vector2 pointer = mouse.position.ReadValue();
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(pointer.x, Screen.height - pointer.y));
            SetHovered(WedgeAt(panelPoint));
        }

        /// <summary>The wedge a point of the panel is over: its number, <see cref="Hub"/>, or <see cref="Nothing"/>.</summary>
        private int WedgeAt(Vector2 panelPoint)
        {
            Rect bounds = wheel.worldBound;
            float radius = Mathf.Min(bounds.width, bounds.height) * 0.5f;
            if (radius <= 1f || wedges <= 0) return Nothing;
            Vector2 offset = panelPoint - bounds.center;
            float reach = offset.magnitude / radius;
            if (reach <= HubRadius) return Hub;
            if (reach < InnerRadius || reach > ReachPastRim) return Nothing;
            float step = 360f / wedges;
            float angle = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg - TopDegrees; // 0 at the top, clockwise
            int wedge = Mathf.RoundToInt(Mathf.Repeat(angle, 360f) / step) % wedges;
            return IsFilled(wedge) ? wedge : Nothing;
        }

        private void SetHovered(int next)
        {
            if (next == hovered) return;
            if (hovered >= 0 && hovered < labels.Count) labels[hovered].RemoveFromClassList(HoveredClass);
            hovered = next;
            if (hovered >= 0 && hovered < labels.Count) labels[hovered].AddToClassList(HoveredClass);
            canvas.MarkDirtyRepaint();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (!open) return;
            evt.StopPropagation();
            GearUIController.Instance?.SuppressWorldInput();
            if (evt.button == RightButton) { Back(); return; }
            if (evt.button != LeftButton) return;
            int wedge = WedgeAt(evt.position);
            SetHovered(wedge);
            if (wedge == Hub) { Back(); return; }
            if (wedge < 0) return;
            if (category == Nothing)
            {
                category = shownCategories[wedge];
                page = 0;
                hovered = Nothing;
                Rebuild();
                return;
            }
            emotes.RequestPlay(pageEntries[wedge].Key);
            waitForRelease = true;
            Close();
        }

        private void OnWheel(WheelEvent evt)
        {
            if (!open || category == Nothing) return;
            evt.StopPropagation();
            int pages = EmoteCatalog.PageCount(catalog[category].Count, Mathf.Max(1, emotes.Settings.pageSize));
            if (pages <= 1) return;
            page = (page + (evt.delta.y > 0f ? 1 : pages - 1)) % pages;
            hovered = Nothing;
            Rebuild();
        }

        /// <summary>From a category's emotes back to the categories.</summary>
        private void Back()
        {
            if (category == Nothing) return;
            category = Nothing;
            page = 0;
            hovered = Nothing;
            Rebuild();
        }

        // ------------------------------------------------------------------ drawing

        private void Draw(MeshGenerationContext context)
        {
            if (!open || wedges <= 0) return;
            Rect area = canvas.contentRect;
            float radius = Mathf.Min(area.width, area.height) * 0.5f - HoverPush - 2f;
            if (radius <= 1f) return;
            Vector2 centre = area.center;
            Color fillColor = fill.resolvedStyle.backgroundColor;
            Color hoverColor = hover.resolvedStyle.backgroundColor;
            Color lineColor = line.resolvedStyle.backgroundColor;
            Color faintLine = new Color(lineColor.r, lineColor.g, lineColor.b, lineColor.a * 0.35f);
            Color emptyFill = new Color(fillColor.r, fillColor.g, fillColor.b, fillColor.a * 0.35f);
            Painter2D painter = context.painter2D;
            painter.lineJoin = LineJoin.Round;

            float step = 360f / wedges;
            for (int i = 0; i < wedges; i++)
            {
                bool filled = IsFilled(i);
                bool lit = filled && i == hovered;
                float middle = TopDegrees + i * step;
                float from = middle - step * 0.5f + GapDegrees * 0.5f, to = middle + step * 0.5f - GapDegrees * 0.5f;
                painter.BeginPath();
                painter.Arc(centre, radius + (lit ? HoverPush : 0f), Angle.Degrees(from), Angle.Degrees(to));
                painter.Arc(centre, radius * InnerRadius, Angle.Degrees(to), Angle.Degrees(from), ArcDirection.CounterClockwise);
                painter.ClosePath();
                painter.fillColor = lit ? hoverColor : filled ? fillColor : emptyFill;
                painter.Fill();
                painter.strokeColor = lit ? lineColor : faintLine;
                painter.lineWidth = lit ? 2.5f : 1.2f;
                painter.Stroke();
            }

            // The hub: the way back while a category is open.
            bool hubLit = hovered == Hub && category != Nothing;
            painter.BeginPath();
            painter.Arc(centre, radius * HubRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.fillColor = hubLit ? hoverColor : fillColor;
            painter.Fill();
            painter.strokeColor = hubLit ? lineColor : faintLine;
            painter.lineWidth = hubLit ? 2.5f : 1.2f;
            painter.Stroke();
        }
    }
}
