using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Lets the player move and resize HUD elements tagged <c>.hud-movable</c> (each needs a unique name).
    /// Custom placement is written as inline styles: left/top as a percentage of the parent (so layouts survive
    /// resolution changes) plus a uniform scale with a top-left transform origin. Elements without a saved entry
    /// keep their USS default position. Layouts persist through <see cref="HudPreferences.LayoutJson"/>.
    /// While a panel is dragged it lines up with the other panels (edges and centres), with the screen's edges and
    /// centre, and otherwise with a grid, so a layout stays tidy without being limited to preset spots. Holding Alt
    /// places it freely; <see cref="HudPreferences.SnapToGrid"/> turns the whole thing off.
    /// A panel never leaves the screen: not while it is dragged or resized, and not when the screen or the interface
    /// size changes afterwards. The player adjusts one panel at a time, chosen by name, or all of them at once.
    /// </summary>
    public sealed class HudLayoutEditor
    {
        public const string MovableClass = "hud-movable";
        private const string EditingClass = "hud-editing";
        private const string OverlayClass = "hud-edit-overlay";
        private const string BlockingClass = "blocks-world";
        private const string WindowClass = "window";
        /// <summary>On a window that keeps itself on screen by its own means (Settings, which also puts itself back): it is not fitted here.</summary>
        public const string FitsItselfClass = "fits-itself";
        /// <summary>Kept between a window and the screen's edge: a skin's frame and close button stand outside the window's own box.</summary>
        private const float WindowMargin = 20f;
        private const float MinScale = 0.5f;
        private const float MaxScale = 2f;
        private const float ScaleStep = 0.05f;
        private const float MinVisiblePixels = 48f;
        /// <summary>How close an edge must come to another panel's edge to jump onto it (panel units).</summary>
        private const float GuideReach = 6f;
        /// <summary>The gap the default layout keeps to the screen edge: panels line up with it too.</summary>
        private const float ScreenMargin = 16f;
        private const int MajorGridEvery = 4;
        private static readonly Color GridColor = new Color(0.45f, 0.87f, 0.77f, 0.07f);
        private static readonly Color MajorGridColor = new Color(0.45f, 0.87f, 0.77f, 0.16f);
        private static readonly Color GuideColor = new Color(1f, 0.79f, 0.24f, 0.95f);
        private const int LeftButton = 0;
        private const int RightButton = 1;

        [Serializable]
        private sealed class Entry
        {
            public string name;
            public float x;
            public float y;
            public float scale = 1f;
        }

        [Serializable]
        private sealed class Layout
        {
            public List<Entry> entries = new List<Entry>();
        }

        private sealed class EditState
        {
            public VisualElement Overlay;
            public Label ScaleLabel;
            public StyleEnum<DisplayStyle> PreviousDisplay;
            public string PreviousText;
            public Action<bool> Preview;
        }

        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "summary", "PLAYER STATUS" },
            { "menu", "MENU" },
            { "hotbar-dock", "HOTBAR" },
            { "active-buffs-bar", "ACTIVE BUFFS" },
            { "boost-list", "ITEM BOOSTS" },
            { "feedback", "MESSAGES" },
            { "character-window", "CHARACTER WINDOW" },
            { "skills-window", "SKILLS WINDOW" },
            { "settings-window", "SETTINGS WINDOW" },
            { "forge-window", "FORGE WINDOW" },
            { "herald-window", "HERALD WINDOW" },
            { "inventory-window", "PACK" },
            { "equipment-window", "EQUIPMENT" },
            { "storage-window", "VAULT" },
            { "trade-window", "TRADE" },
            { "kiosk-window", "KIOSK" },
            { "shop-window", "KIOSK SHOP" },
            { "vendor-window", "SHOP" },
            { "contacts-window", "CONTACTS" },
            { "crew-window", "CREW" },
            { "minimap", "MINIMAP" },
            { "loot-notice", "LOOT NOTICE" },
            { "chat-window", "CHAT" },
            { "network-panel", "NETWORK PANEL" },
            { "npc-dialogue", "NPC DIALOGUE" },
            { "zone-toast", "ZONE NOTICE" },
            { "world-map", "WORLD MAP" },
            { "party-frames", "SQUAD" },
            { "milestone-banner", "MILESTONE BANNER" },
            { "arrange-window", "FURNISHING CRATE" }
        };

        // What a panel shows while it is being placed, when it has nothing to show by itself: a window that only
        // opens at an NPC, a banner that only appears for a moment. Its owner fills it with a likeness (true) and
        // takes that away again (false).
        private static readonly Dictionary<string, Action<bool>> previews = new Dictionary<string, Action<bool>>();

        /// <summary>The owner of a panel says how it looks while the layout editor has it out to be placed.</summary>
        public static void RegisterPreview(string name, Action<bool> show)
        {
            if (!string.IsNullOrEmpty(name) && show != null) previews[name] = show;
        }

        public static void UnregisterPreview(string name)
        {
            if (!string.IsNullOrEmpty(name)) previews.Remove(name);
        }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            previews.Clear();
            Current = null;
        }

        private readonly Dictionary<string, VisualElement> movables = new Dictionary<string, VisualElement>();
        private readonly Dictionary<VisualElement, EditState> editing = new Dictionary<VisualElement, EditState>();
        private readonly Dictionary<string, Entry> saved = new Dictionary<string, Entry>();
        private readonly HashSet<VisualElement> watched = new HashSet<VisualElement>();

        private VisualElement activeElement;
        private VisualElement activeCapture;
        private int activePointerId = -1;
        // Windows the player never placed that were moved or shrunk to stay on screen (FitWindow).
        private readonly HashSet<VisualElement> fitted = new HashSet<VisualElement>();
        private bool resizing;
        private Vector2 pointerStart;
        private Vector2 positionStart;
        private float scaleStart;
        private Vector2 sizeStart;

        // The grid and the alignment guides are drawn on one layer under the panels.
        private VisualElement layoutRoot;
        private VisualElement gridLayer;
        private readonly List<float> guideXs = new List<float>(2);
        private readonly List<float> guideYs = new List<float>(2);
        private readonly List<float> candidateXs = new List<float>(48);
        private readonly List<float> candidateYs = new List<float>(48);

        // Click-to-select: what the pointer is over, the frame that marks it, and where the clicks are heard.
        private const string EditChromePrefix = "hud-edit";
        private const string EditBarName = "hud-edit-banner";
        private const string ResizeHandleClass = "hud-resize-handle";
        private static readonly Color HoverFrame = new Color(1f, 0.79f, 0.24f, 0.9f);
        private static readonly Color HoverFill = new Color(1f, 0.79f, 0.24f, 0.08f);
        private VisualElement pickRoot;
        private VisualElement hovered;
        private VisualElement hoverFrame;

        public bool IsEditing { get; private set; }
        public event Action EditingChanged;

        // ------------------------------------------------------------------ discovery & persistence

        /// <summary>Registers every named <c>.hud-movable</c> under the given document roots.</summary>
        public void Collect(IEnumerable<VisualElement> roots)
        {
            foreach (VisualElement root in roots)
            {
                if (layoutRoot == null && root != null) layoutRoot = root;
                root?.Query(className: MovableClass).ForEach(element =>
                {
                    if (string.IsNullOrEmpty(element.name)) return;
                    movables[element.name] = element;
                    if (watched.Add(element)) element.RegisterCallback<GeometryChangedEvent>(_ => OnPanelGeometry(element));
                });
            }
        }

        /// <summary>The layout of the HUD on screen. Windows that place themselves ask it whether the player already has.</summary>
        public static HudLayoutEditor Current { get; private set; }

        /// <summary>True when the player has put this panel somewhere in the layout editor: it is theirs to place.</summary>
        public bool HasSavedPosition(string name) => !string.IsNullOrEmpty(name) && saved.ContainsKey(name);

        public void LoadAndApply()
        {
            Current = this;
            saved.Clear();
            string json = HudPreferences.LayoutJson;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var layout = JsonUtility.FromJson<Layout>(json);
                    if (layout?.entries != null)
                        foreach (Entry entry in layout.entries)
                            if (!string.IsNullOrEmpty(entry.name)) saved[entry.name] = entry;
                }
                catch (ArgumentException)
                {
                    Debug.LogWarning("Saved HUD layout was unreadable and has been reset.");
                    HudPreferences.LayoutJson = null;
                }
            }

            foreach (var pair in movables)
            {
                if (saved.TryGetValue(pair.Key, out Entry entry)) ApplyEntry(pair.Value, entry);
                else ClearPlacement(pair.Value);
            }
        }

        /// <summary>
        /// Keeps the panels on screen after a resolution or interface size change: the ones the player placed, and
        /// every window that is open.
        /// </summary>
        public void ClampAllIntoView()
        {
            foreach (var pair in movables)
            {
                if (saved.ContainsKey(pair.Key)) ClampIntoView(pair.Value);
                else if (!IsEditing && IsFittedWindow(pair.Value)) FitWindow(pair.Value);
            }
        }

        public void ResetAll()
        {
            saved.Clear();
            HudPreferences.LayoutJson = null;
            foreach (VisualElement element in movables.Values)
            {
                ClearPlacement(element);
                RefreshScaleLabel(element);
            }
        }

        private void ResetElement(VisualElement element)
        {
            saved.Remove(element.name);
            ClearPlacement(element);
            RefreshScaleLabel(element);
            Save();
        }

        private void Save()
        {
            var layout = new Layout();
            layout.entries.AddRange(saved.Values);
            HudPreferences.LayoutJson = layout.entries.Count == 0 ? null : JsonUtility.ToJson(layout);
        }

        private static void ApplyEntry(VisualElement element, Entry entry)
        {
            element.style.left = Length.Percent(entry.x * 100f);
            element.style.top = Length.Percent(entry.y * 100f);
            element.style.right = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;
            element.style.translate = new Translate(0f, 0f);
            element.style.transformOrigin = new TransformOrigin(0f, 0f);
            float scale = Mathf.Clamp(entry.scale, MinScale, MaxScale);
            element.style.scale = new Scale(new Vector2(scale, scale));
        }

        private static void ClearPlacement(VisualElement element)
        {
            element.style.left = StyleKeyword.Null;
            element.style.top = StyleKeyword.Null;
            element.style.right = StyleKeyword.Null;
            element.style.bottom = StyleKeyword.Null;
            element.style.translate = StyleKeyword.Null;
            element.style.transformOrigin = StyleKeyword.Null;
            element.style.scale = StyleKeyword.Null;
        }

        // ------------------------------------------------------------------ edit mode

        /// <summary>The choice that puts every panel in edit state at once.</summary>
        public const string AllPanels = "*";
        private string selection;

        /// <summary>The panel being adjusted (its name), <see cref="AllPanels"/>, or null while the player has not chosen.</summary>
        public string Selection => selection;

        /// <summary>Every panel that can be placed, with the name the editor shows for it, in alphabetical order.</summary>
        public void GetPanels(List<(string Name, string Label)> into)
        {
            into.Clear();
            foreach (var pair in movables)
                if (pair.Value.panel != null) into.Add((pair.Key, Describe(pair.Value)));
            into.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
        }

        /// <summary>What the editor calls a choice: a panel's name, "ALL PANELS", or "CHOOSE" for none.</summary>
        public string LabelOf(string name)
        {
            if (name == AllPanels) return "ALL PANELS";
            return name != null && movables.TryGetValue(name, out VisualElement element) ? Describe(element) : "CHOOSE";
        }

        /// <summary>Opens the editor with no panel chosen: the player picks one, or all, with <see cref="Select"/>.</summary>
        public void BeginEdit()
        {
            if (IsEditing) return;
            IsEditing = true;
            selection = null;
            ShowGrid();
            ListenForPicks(true);
            EditingChanged?.Invoke();
        }

        /// <summary>
        /// Puts one panel (by name), every panel (<see cref="AllPanels"/>) or none (null) in edit state. The others
        /// go back to looking and behaving as they do in play.
        /// </summary>
        public void Select(string name)
        {
            if (!IsEditing) return;
            CancelInteraction();
            selection = name;
            foreach (var pair in movables)
            {
                bool wanted = name == AllPanels || pair.Key == name;
                bool active = editing.ContainsKey(pair.Value);
                if (wanted && !active) EnterEdit(pair.Value);
                else if (!wanted && active) ExitEdit(pair.Value);
            }
            EditingChanged?.Invoke();
        }

        /// <summary>Puts the chosen panel (or, with all chosen, every panel) back where the game places it.</summary>
        public void ResetSelected()
        {
            if (selection == AllPanels) ResetAll();
            else if (selection != null && movables.TryGetValue(selection, out VisualElement element)) ResetElement(element);
        }

        private void EnterEdit(VisualElement element)
        {
            if (element.panel == null) return;
            var state = new EditState { PreviousDisplay = element.style.display };
            // A hidden window or dialog is shown so it can be placed; restored when it leaves edit state.
            if (element.resolvedStyle.display == DisplayStyle.None) element.style.display = DisplayStyle.Flex;
            if (element is Label label && string.IsNullOrEmpty(label.text))
            {
                state.PreviousText = label.text;
                label.text = Describe(element);
            }
            if (previews.TryGetValue(element.name, out Action<bool> preview))
            {
                state.Preview = preview;
                preview(true);
            }
            element.AddToClassList(EditingClass);
            state.Overlay = CreateOverlay(element, out state.ScaleLabel);
            element.hierarchy.Add(state.Overlay);
            editing[element] = state;
            RefreshScaleLabel(element);
        }

        private void ExitEdit(VisualElement element)
        {
            if (!editing.TryGetValue(element, out EditState state)) return;
            state.Overlay.RemoveFromHierarchy();
            element.RemoveFromClassList(EditingClass);
            state.Preview?.Invoke(false);
            element.style.display = state.PreviousDisplay;
            if (state.PreviousText != null && element is Label label) label.text = state.PreviousText;
            editing.Remove(element);
        }

        public void EndEdit()
        {
            if (!IsEditing) return;
            CancelInteraction();
            foreach (VisualElement element in new List<VisualElement>(editing.Keys)) ExitEdit(element);
            selection = null;
            ListenForPicks(false);
            HideGrid();
            IsEditing = false;
            Save();
            EditingChanged?.Invoke();
        }

        private VisualElement CreateOverlay(VisualElement element, out Label scaleLabel)
        {
            var overlay = new VisualElement { pickingMode = PickingMode.Position, tooltip = "Drag to move · right-click to reset" };
            overlay.AddToClassList(OverlayClass);
            overlay.AddToClassList(BlockingClass);
            // Said on the element itself: a panel may hide everything inside it (the player's plate does, under the
            // faction's HUD piece), and the frame it is dragged by must never be caught by that.
            overlay.style.visibility = Visibility.Visible;

            var title = new Label(Describe(element)) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("hud-edit-label");
            overlay.Add(title);
            scaleLabel = new Label { pickingMode = PickingMode.Ignore };
            scaleLabel.AddToClassList("hud-edit-scale");
            overlay.Add(scaleLabel);

            var handle = new VisualElement { pickingMode = PickingMode.Position, tooltip = "Drag to resize" };
            handle.AddToClassList(ResizeHandleClass);
            handle.AddToClassList(BlockingClass);
            overlay.Add(handle);

            overlay.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(evt, element, overlay, false));
            handle.RegisterCallback<PointerDownEvent>(evt => OnPointerDown(evt, element, handle, true));
            foreach (VisualElement source in new[] { overlay, handle })
            {
                source.RegisterCallback<PointerMoveEvent>(OnPointerMove);
                source.RegisterCallback<PointerUpEvent>(OnPointerUp);
                source.RegisterCallback<PointerCaptureOutEvent>(_ => CancelInteraction());
            }
            return overlay;
        }

        private static string Describe(VisualElement element)
            => DisplayNames.TryGetValue(element.name, out string label) ? label : element.name.Replace('-', ' ').ToUpperInvariant();

        private void RefreshScaleLabel(VisualElement element)
        {
            if (!editing.TryGetValue(element, out EditState state) || state.ScaleLabel == null) return;
            float scale = saved.TryGetValue(element.name, out Entry entry) ? entry.scale : 1f;
            state.ScaleLabel.text = $"{Mathf.RoundToInt(scale * 100f)}%";
        }

        // ------------------------------------------------------------------ pointer handling

        private void OnPointerDown(PointerDownEvent evt, VisualElement element, VisualElement capture, bool resize)
        {
            evt.StopPropagation();
            // A press always starts fresh, even if a previous gesture never delivered its pointer-up.
            if (activeElement != null) CancelInteraction();
            if (evt.button == RightButton && !resize)
            {
                ResetElement(element);
                return;
            }
            if (evt.button != LeftButton) return;
            StartGesture(element, capture, evt.pointerId, evt.position, resize);
        }

        private void StartGesture(VisualElement element, VisualElement capture, int pointerId, Vector2 pointer, bool resize)
        {
            MakeExplicit(element);
            activeElement = element;
            activeCapture = capture;
            activePointerId = pointerId;
            resizing = resize;
            pointerStart = pointer;
            positionStart = new Vector2(element.style.left.value.value, element.style.top.value.value);
            scaleStart = element.resolvedStyle.scale.value.x;
            sizeStart = new Vector2(element.layout.width, element.layout.height) * scaleStart;
            capture.CapturePointer(pointerId);
            editing[element].Overlay.AddToClassList("dragging");
        }

        // ------------------------------------------------------------------ click a panel to select it

        /// <summary>
        /// While the editor is open, a press on any panel that is on screen selects it and starts dragging it in the
        /// same press. Heard on the way down from the top of the panel tree, so it reaches panels that ignore the
        /// pointer in play and gets there before a button under the pointer does. A closed window has no place on
        /// screen to click: those are still chosen from the list.
        /// </summary>
        private void ListenForPicks(bool on)
        {
            if (on)
            {
                pickRoot = layoutRoot != null && layoutRoot.panel != null ? layoutRoot.panel.visualTree : null;
                if (pickRoot == null) return;
                pickRoot.RegisterCallback<PointerDownEvent>(OnPickDown, TrickleDown.TrickleDown);
                pickRoot.RegisterCallback<PointerMoveEvent>(OnPickMove, TrickleDown.TrickleDown);
                return;
            }
            SetHovered(null);
            if (pickRoot == null) return;
            pickRoot.UnregisterCallback<PointerDownEvent>(OnPickDown, TrickleDown.TrickleDown);
            pickRoot.UnregisterCallback<PointerMoveEvent>(OnPickMove, TrickleDown.TrickleDown);
            pickRoot = null;
        }

        private void OnPickDown(PointerDownEvent evt)
        {
            if (evt.button != LeftButton || activeElement != null) return;
            var target = evt.target as VisualElement;
            VisualElement panel = PanelUnder(target, evt.position);
            if (panel == null) return;
            if (editing.TryGetValue(panel, out EditState state))
            {
                // Already in edit state: its own frame takes the press, unless the editor's bar lies over it.
                if (!IsEditBar(target)) return;
                evt.StopPropagation();
                VisualElement handle = state.Overlay.Q(className: ResizeHandleClass);
                bool resize = handle != null && handle.worldBound.Contains(evt.position);
                StartGesture(panel, resize ? handle : state.Overlay, evt.pointerId, evt.position, resize);
                return;
            }
            evt.StopPropagation();
            SetHovered(null);
            Select(panel.name);
            if (editing.TryGetValue(panel, out state)) StartGesture(panel, state.Overlay, evt.pointerId, evt.position, false);
        }

        /// <summary>
        /// The pointer is on the editor's bar itself, not on one of its buttons or its list. The bar is wide and a
        /// panel can sit under it (the player's plate does, on a narrow screen): a press there is for that panel.
        /// </summary>
        private static bool IsEditBar(VisualElement target)
        {
            for (VisualElement at = target; at != null; at = at.hierarchy.parent)
            {
                if (at is Button) return false;
                if (at.name == EditBarName) return true;
            }
            return false;
        }

        private void OnPickMove(PointerMoveEvent evt)
        {
            if (activeElement != null) return;
            VisualElement panel = PanelUnder(evt.target as VisualElement, evt.position);
            SetHovered(panel != null && !editing.ContainsKey(panel) ? panel : null);
        }

        /// <summary>The panel a press at this point is for, or null: over the editor's own controls, or over none.</summary>
        private VisualElement PanelUnder(VisualElement target, Vector2 point)
        {
            // What the pointer is actually on, walking outward: the editor's bar and list are never a panel to move.
            bool onBar = IsEditBar(target);
            for (VisualElement at = onBar ? null : target; at != null; at = at.hierarchy.parent)
            {
                if (at.name != null && at.name.StartsWith(EditChromePrefix, StringComparison.Ordinal)) return null;
                if (at.ClassListContains(OverlayClass)) return null;
                if (IsMovable(at)) return at;
            }
            // Panels that ignore the pointer in play are never a target: find them by where they are, smallest first.
            VisualElement best = null;
            float bestArea = float.MaxValue;
            foreach (VisualElement element in movables.Values)
            {
                if (element.panel == null || element.resolvedStyle.display == DisplayStyle.None) continue;
                Rect bounds = element.worldBound;
                float area = bounds.width * bounds.height;
                if (area < 1f || area >= bestArea || !bounds.Contains(point)) continue;
                best = element;
                bestArea = area;
            }
            return best;
        }

        private bool IsMovable(VisualElement element) =>
            !string.IsNullOrEmpty(element.name) && movables.TryGetValue(element.name, out VisualElement known) && known == element;

        /// <summary>Frames the panel a click would take, so it is plain which one that is before clicking.</summary>
        private void SetHovered(VisualElement panel)
        {
            if (hovered == panel) return;
            hoverFrame?.RemoveFromHierarchy();
            hovered = panel;
            if (panel == null) return;
            if (hoverFrame == null)
            {
                hoverFrame = new VisualElement { pickingMode = PickingMode.Ignore };
                hoverFrame.style.position = Position.Absolute;
                hoverFrame.style.visibility = Visibility.Visible;
                hoverFrame.style.left = hoverFrame.style.top = hoverFrame.style.right = hoverFrame.style.bottom = 0f;
                hoverFrame.style.borderLeftWidth = hoverFrame.style.borderRightWidth = 2f;
                hoverFrame.style.borderTopWidth = hoverFrame.style.borderBottomWidth = 2f;
                hoverFrame.style.borderLeftColor = hoverFrame.style.borderRightColor = HoverFrame;
                hoverFrame.style.borderTopColor = hoverFrame.style.borderBottomColor = HoverFrame;
                hoverFrame.style.backgroundColor = HoverFill;
            }
            panel.hierarchy.Add(hoverFrame);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (activeElement == null || evt.pointerId != activePointerId) return;
            evt.StopPropagation();
            Vector2 delta = (Vector2)evt.position - pointerStart;

            float scale = scaleStart;
            Vector2 position = positionStart;
            if (resizing)
            {
                float ratioX = sizeStart.x > 1f ? (sizeStart.x + delta.x) / sizeStart.x : 1f;
                float ratioY = sizeStart.y > 1f ? (sizeStart.y + delta.y) / sizeStart.y : 1f;
                scale = scaleStart * (ratioX + ratioY) * 0.5f;
                scale = Mathf.Clamp(Mathf.Round(scale / ScaleStep) * ScaleStep, MinScale, MaxScale);
                // Never larger than the screen can hold.
                Rect screen = ScreenOf(activeElement);
                float width = activeElement.layout.width, height = activeElement.layout.height;
                if (width > 1f && height > 1f)
                {
                    float fits = Mathf.Floor(Mathf.Min(screen.width / width, screen.height / height) / ScaleStep) * ScaleStep;
                    scale = Mathf.Min(scale, Mathf.Max(MinScale, fits));
                }
                activeElement.style.scale = new Scale(new Vector2(scale, scale));
                if (editing.TryGetValue(activeElement, out EditState state))
                    state.ScaleLabel.text = $"{Mathf.RoundToInt(scale * 100f)}%";
            }
            else
            {
                position = Snap(activeElement, position + delta, scale, evt.altKey);
            }

            position = ClampPixels(activeElement, position, scale);
            activeElement.style.left = position.x;
            activeElement.style.top = position.y;
        }

        /// <summary>The area a panel must stay inside: the whole screen.</summary>
        private Rect ScreenOf(VisualElement element) =>
            layoutRoot != null && layoutRoot.panel != null ? layoutRoot.worldBound : element.hierarchy.parent.worldBound;

        /// <summary>
        /// The area all of a panel must be seen in: the screen, less for a window the room its skin's frame and
        /// close button take outside the window's own box.
        /// </summary>
        private Rect VisibleAreaOf(VisualElement element) =>
            element.ClassListContains(WindowClass) ? ScreenFit.Inset(ScreenOf(element), WindowMargin) : ScreenOf(element);

        /// <summary>The top-left corner nearest to <paramref name="topLeft"/> that keeps a box of this size inside the area.</summary>
        private static Vector2 ClampInto(Rect area, Vector2 topLeft, Vector2 size) => ScreenFit.Clamp(area, topLeft, size);

        /// <summary>Keeps the whole panel on screen while it is dragged or resized (pixel space, no layout lag).</summary>
        private Vector2 ClampPixels(VisualElement element, Vector2 position, float scale)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f) return position;
            Vector2 origin = parent.worldBound.position;
            Vector2 size = new Vector2(element.layout.width, element.layout.height) * scale;
            return ClampInto(ScreenOf(element), origin + position, size) - origin;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (activeElement == null || evt.pointerId != activePointerId) return;
            evt.StopPropagation();
            VisualElement element = activeElement;
            ReleaseCapture();
            Commit(element);
        }

        private void CancelInteraction()
        {
            if (activeElement == null) return;
            VisualElement element = activeElement;
            ReleaseCapture();
            Commit(element);
        }

        private void ReleaseCapture()
        {
            if (activeCapture != null && activeCapture.HasPointerCapture(activePointerId))
                activeCapture.ReleasePointer(activePointerId);
            if (activeElement != null && editing.TryGetValue(activeElement, out EditState state))
                state.Overlay.RemoveFromClassList("dragging");
            ClearGuides();
            activeElement = null;
            activeCapture = null;
            activePointerId = -1;
        }

        // ------------------------------------------------------------------ grid & guides

        /// <summary>
        /// Where a dragged panel comes to rest. Each axis on its own: an edge or the centre jumps onto a line another
        /// panel (or the screen) already keeps, when one is within reach; otherwise the top-left corner goes to the
        /// nearest grid line. Positions are local to the panel's parent; lines are compared in panel space.
        /// </summary>
        private Vector2 Snap(VisualElement element, Vector2 position, float scale, bool free)
        {
            guideXs.Clear();
            guideYs.Clear();
            VisualElement parent = element.hierarchy.parent;
            if (free || !HudPreferences.SnapToGrid || parent == null)
            {
                gridLayer?.MarkDirtyRepaint();
                return position;
            }

            Vector2 origin = parent.worldBound.position;
            Vector2 size = new Vector2(element.layout.width, element.layout.height) * scale;
            Vector2 world = origin + position;
            CollectCandidates(element);
            world.x = SnapAxis(world.x, size.x, candidateXs, guideXs);
            world.y = SnapAxis(world.y, size.y, candidateYs, guideYs);
            gridLayer?.MarkDirtyRepaint();
            return world - origin;
        }

        private static float SnapAxis(float start, float length, List<float> lines, List<float> guides)
        {
            float best = float.MaxValue;
            float bestLine = 0f;
            // The near edge, the middle and the far edge may each sit on a line.
            for (int part = 0; part < 3; part++)
            {
                float at = start + length * part * 0.5f;
                for (int i = 0; i < lines.Count; i++)
                {
                    float offset = lines[i] - at;
                    if (Mathf.Abs(offset) >= Mathf.Abs(best)) continue;
                    best = offset;
                    bestLine = lines[i];
                }
            }
            if (Mathf.Abs(best) <= GuideReach)
            {
                guides.Add(bestLine);
                return start + best;
            }
            float grid = HudPreferences.GridSize;
            return Mathf.Round(start / grid) * grid;
        }

        /// <summary>The lines worth lining up with: the screen's margins and centre, and every other visible panel.</summary>
        private void CollectCandidates(VisualElement dragged)
        {
            candidateXs.Clear();
            candidateYs.Clear();
            Rect screen = layoutRoot != null ? layoutRoot.worldBound : dragged.hierarchy.parent.worldBound;
            candidateXs.Add(screen.xMin + ScreenMargin);
            candidateXs.Add(screen.center.x);
            candidateXs.Add(screen.xMax - ScreenMargin);
            candidateYs.Add(screen.yMin + ScreenMargin);
            candidateYs.Add(screen.center.y);
            candidateYs.Add(screen.yMax - ScreenMargin);

            foreach (VisualElement other in movables.Values)
            {
                if (other == dragged || other.panel == null || other.resolvedStyle.display == DisplayStyle.None) continue;
                Rect bounds = other.worldBound;
                if (bounds.width < 1f || bounds.height < 1f) continue;
                candidateXs.Add(bounds.xMin);
                candidateXs.Add(bounds.center.x);
                candidateXs.Add(bounds.xMax);
                candidateYs.Add(bounds.yMin);
                candidateYs.Add(bounds.center.y);
                candidateYs.Add(bounds.yMax);
            }
        }

        private void ClearGuides()
        {
            guideXs.Clear();
            guideYs.Clear();
            gridLayer?.MarkDirtyRepaint();
        }

        private void ShowGrid()
        {
            if (layoutRoot == null) return;
            if (gridLayer == null)
            {
                gridLayer = new VisualElement { name = "hud-edit-grid", pickingMode = PickingMode.Ignore };
                gridLayer.style.position = Position.Absolute;
                gridLayer.style.left = 0f;
                gridLayer.style.top = 0f;
                gridLayer.style.right = 0f;
                gridLayer.style.bottom = 0f;
                gridLayer.generateVisualContent += DrawGrid;
            }
            // Under every panel, so the lines never cover what is being placed.
            if (gridLayer.parent != layoutRoot) layoutRoot.Insert(0, gridLayer);
            HudPreferences.Changed += RepaintGrid;
            gridLayer.MarkDirtyRepaint();
        }

        private void HideGrid()
        {
            HudPreferences.Changed -= RepaintGrid;
            gridLayer?.RemoveFromHierarchy();
        }

        private void RepaintGrid() => gridLayer?.MarkDirtyRepaint();

        private void DrawGrid(MeshGenerationContext context)
        {
            Rect area = gridLayer.contentRect;
            if (area.width < 1f || area.height < 1f) return;
            Painter2D painter = context.painter2D;
            painter.lineWidth = 1f;

            if (HudPreferences.SnapToGrid)
            {
                // Grid lines sit where panels snap: on multiples of the cell, counted from the screen's corner.
                Vector2 origin = gridLayer.worldBound.position;
                float cell = HudPreferences.GridSize;
                // A very fine grid is drawn at every other line so it stays a hint, not a hatch.
                int step = cell < 8f ? 2 : 1;
                DrawLines(painter, area, origin.x, cell, step, true);
                DrawLines(painter, area, origin.y, cell, step, false);
            }

            painter.strokeColor = GuideColor;
            painter.lineWidth = 1.5f;
            Vector2 offset = gridLayer.worldBound.position;
            foreach (float x in guideXs) Line(painter, new Vector2(x - offset.x, 0f), new Vector2(x - offset.x, area.height));
            foreach (float y in guideYs) Line(painter, new Vector2(0f, y - offset.y), new Vector2(area.width, y - offset.y));
        }

        private static void DrawLines(Painter2D painter, Rect area, float worldOrigin, float cell, int step, bool vertical)
        {
            float extent = vertical ? area.width : area.height;
            int first = Mathf.CeilToInt(worldOrigin / cell);
            for (int index = first; index * cell - worldOrigin <= extent; index++)
            {
                bool major = index % (MajorGridEvery * step) == 0;
                if (!major && index % step != 0) continue;
                float at = index * cell - worldOrigin;
                painter.strokeColor = major ? MajorGridColor : GridColor;
                if (vertical) Line(painter, new Vector2(at, 0f), new Vector2(at, area.height));
                else Line(painter, new Vector2(0f, at), new Vector2(area.width, at));
            }
        }

        private static void Line(Painter2D painter, Vector2 from, Vector2 to)
        {
            painter.BeginPath();
            painter.MoveTo(from);
            painter.LineTo(to);
            painter.Stroke();
        }

        /// <summary>Converts the element's current on-screen placement into explicit pixel left/top and a top-left origin.</summary>
        private static void MakeExplicit(VisualElement element)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null) return;
            Vector2 topLeft = parent.WorldToLocal(element.worldBound.position);
            float scale = element.resolvedStyle.scale.value.x;
            element.style.translate = new Translate(0f, 0f);
            element.style.transformOrigin = new TransformOrigin(0f, 0f);
            element.style.scale = new Scale(new Vector2(scale, scale));
            element.style.right = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;
            element.style.left = topLeft.x;
            element.style.top = topLeft.y;
        }

        /// <summary>Stores the element's placement as parent-relative percentages.</summary>
        private void Commit(VisualElement element)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f || parent.layout.height < 1f) return;
            // During a drag left/top are inline pixels (MakeExplicit), which are current even before the next layout pass.
            StyleLength left = element.style.left;
            StyleLength top = element.style.top;
            Vector2 topLeft = left.keyword == StyleKeyword.Undefined && left.value.unit == LengthUnit.Pixel
                && top.keyword == StyleKeyword.Undefined && top.value.unit == LengthUnit.Pixel
                ? new Vector2(left.value.value, top.value.value)
                : parent.WorldToLocal(element.worldBound.position);
            var entry = new Entry
            {
                name = element.name,
                x = topLeft.x / parent.layout.width,
                y = topLeft.y / parent.layout.height,
                scale = element.style.scale.keyword == StyleKeyword.Undefined
                    ? element.style.scale.value.value.x
                    : element.resolvedStyle.scale.value.x
            };
            saved[element.name] = entry;
            ApplyEntry(element, entry);
            RefreshScaleLabel(element);
            Save();
        }

        /// <summary>
        /// Pulls a placed panel back so all of it is on screen: after the window or the interface size changes, when
        /// a saved layout is loaded, and when a hidden panel is shown.
        /// </summary>
        private void ClampIntoView(VisualElement element)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f || parent.layout.height < 1f || element.panel == null) return;
            Rect bounds = element.worldBound;
            if (bounds.width < 1f || bounds.height < 1f) return; // hidden: there is nothing to measure yet

            // A panel the player made larger than this screen has room for (a window at 150 % on a smaller screen,
            // or one whose page grew) is drawn as large as fits. The size the player chose is kept, and comes back
            // when there is room for it.
            float own = Mathf.Max(0.01f, element.resolvedStyle.scale.value.x);
            float chosen = saved.TryGetValue(element.name, out Entry entry) ? Mathf.Clamp(entry.scale, MinScale, MaxScale) : own;
            Vector2 natural = bounds.size / own;
            Rect area = VisibleAreaOf(element);
            float scale = chosen * ScreenFit.ScaleToFit(area, natural * chosen);
            Vector2 placed = ClampInto(area, bounds.position, natural * scale);
            bool moved = Mathf.Abs(placed.x - bounds.x) >= 0.5f || Mathf.Abs(placed.y - bounds.y) >= 0.5f;
            bool resized = Mathf.Abs(scale - own) >= 0.004f;
            if (!moved && !resized) return;

            if (resized) element.style.scale = new Scale(new Vector2(scale, scale));
            Vector2 local = parent.WorldToLocal(placed);
            element.style.left = Length.Percent(local.x / parent.layout.width * 100f);
            element.style.top = Length.Percent(local.y / parent.layout.height * 100f);
        }

        // A hidden panel has no size, so it can only be brought back on screen once it is shown or resized.
        private void OnPanelGeometry(VisualElement element)
        {
            if (element == activeElement) return;
            // Settings moves itself by a margin and puts itself back: a clamp here would leave it where a wide page pushed it.
            if (element.ClassListContains(FitsItselfClass)) return;
            if (saved.ContainsKey(element.name)) ClampIntoView(element);
            else if (!IsEditing && IsFittedWindow(element)) FitWindow(element);
        }

        private static bool IsFittedWindow(VisualElement element) =>
            element.ClassListContains(WindowClass) && !element.ClassListContains(FitsItselfClass);

        /// <summary>
        /// A window the player never placed stands where its sheet, or the window it docks beside, puts it: fixed
        /// numbers that leave the screen on a smaller one, at a larger interface size, in a faction skin with taller
        /// lettering, or when a page of the window is taller than the one it opened on. Whenever such a window is
        /// shown or changes size it is moved as little as it takes to be wholly on screen, and one too large for the
        /// screen is shrunk to fit. Nothing is saved: its sheet places it again the next time it has room.
        /// </summary>
        private void FitWindow(VisualElement element)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || element.panel == null || parent.layout.width < 1f || parent.layout.height < 1f) return;
            if (element.resolvedStyle.display == DisplayStyle.None)
            {
                Unfit(element);
                return;
            }
            Rect bounds = element.worldBound;
            if (float.IsNaN(bounds.width) || bounds.width < 1f || bounds.height < 1f) return;

            float own = Mathf.Max(0.01f, element.resolvedStyle.scale.value.x);
            Vector2 size = bounds.size / own; // at its own scale 1
            Rect area = ScreenFit.Inset(ScreenOf(element), WindowMargin);
            float scale = ScreenFit.ScaleToFit(area, size);
            Vector2 placed = ScreenFit.Clamp(area, bounds.position, size * scale);
            bool moved = Mathf.Abs(placed.x - bounds.x) >= 0.5f || Mathf.Abs(placed.y - bounds.y) >= 0.5f;
            bool resized = Mathf.Abs(scale - own) >= 0.004f;
            if (!moved && !resized) return;

            Vector2 local = parent.WorldToLocal(placed);
            element.style.left = local.x;
            element.style.top = local.y;
            element.style.right = StyleKeyword.Auto;
            element.style.bottom = StyleKeyword.Auto;
            element.style.marginLeft = 0f;
            element.style.marginTop = 0f;
            element.style.translate = new Translate(0f, 0f);
            element.style.transformOrigin = new TransformOrigin(0f, 0f);
            element.style.scale = new Scale(new Vector2(scale, scale));
            fitted.Add(element);
        }

        /// <summary>A fitted window was closed: its sheet places it afresh the next time it opens.</summary>
        private void Unfit(VisualElement element)
        {
            if (!fitted.Remove(element)) return;
            ClearPlacement(element);
            element.style.marginLeft = StyleKeyword.Null;
            element.style.marginTop = StyleKeyword.Null;
        }

        /// <summary>
        /// A placed panel changed size by its own means (the chat window's grip): keeps it on screen and stores where
        /// it is now. Does nothing for a panel the player never moved.
        /// </summary>
        public void Recommit(VisualElement element)
        {
            if (element == null || string.IsNullOrEmpty(element.name) || !saved.TryGetValue(element.name, out Entry entry)) return;
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f || parent.layout.height < 1f) return;
            Rect bounds = element.worldBound;
            if (bounds.width < 1f || bounds.height < 1f) return;
            Vector2 local = parent.WorldToLocal(ClampInto(ScreenOf(element), bounds.position, bounds.size));
            entry.x = local.x / parent.layout.width;
            entry.y = local.y / parent.layout.height;
            ApplyEntry(element, entry);
            Save();
        }
    }
}
