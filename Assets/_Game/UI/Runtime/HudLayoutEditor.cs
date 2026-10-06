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
        }

        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>
        {
            { "summary", "PLAYER STATUS" },
            { "menu", "MENU" },
            { "hotbar-dock", "HOTBAR" },
            { "active-buffs-bar", "ACTIVE BUFFS" },
            { "feedback", "MESSAGES" },
            { "character-window", "CHARACTER WINDOW" },
            { "skills-window", "SKILLS WINDOW" },
            { "settings-window", "SETTINGS WINDOW" },
            { "forge-window", "FORGE WINDOW" },
            { "inventory-window", "PACK" },
            { "equipment-window", "EQUIPMENT" },
            { "storage-window", "VAULT" },
            { "trade-window", "TRADE" },
            { "kiosk-window", "KIOSK" },
            { "shop-window", "KIOSK SHOP" },
            { "contacts-window", "CONTACTS" },
            { "crew-window", "CREW" },
            { "minimap", "MINIMAP" },
            { "loot-notice", "LOOT NOTICE" },
            { "chat-window", "CHAT" },
            { "network-panel", "NETWORK PANEL" },
            { "npc-dialogue", "NPC DIALOGUE" },
            { "zone-toast", "ZONE NOTICE" },
            { "world-map", "WORLD MAP" },
            { "party-frames", "SQUAD" }
        };

        private readonly Dictionary<string, VisualElement> movables = new Dictionary<string, VisualElement>();
        private readonly Dictionary<VisualElement, EditState> editing = new Dictionary<VisualElement, EditState>();
        private readonly Dictionary<string, Entry> saved = new Dictionary<string, Entry>();
        private readonly HashSet<VisualElement> watched = new HashSet<VisualElement>();

        private VisualElement activeElement;
        private VisualElement activeCapture;
        private int activePointerId = -1;
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

        public void LoadAndApply()
        {
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

        /// <summary>Keeps customised elements on screen after a resolution or window size change.</summary>
        public void ClampAllIntoView()
        {
            foreach (var pair in movables)
                if (saved.ContainsKey(pair.Key)) ClampIntoView(pair.Value);
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

            var title = new Label(Describe(element)) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("hud-edit-label");
            overlay.Add(title);
            scaleLabel = new Label { pickingMode = PickingMode.Ignore };
            scaleLabel.AddToClassList("hud-edit-scale");
            overlay.Add(scaleLabel);

            var handle = new VisualElement { pickingMode = PickingMode.Position, tooltip = "Drag to resize" };
            handle.AddToClassList("hud-resize-handle");
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

            MakeExplicit(element);
            activeElement = element;
            activeCapture = capture;
            activePointerId = evt.pointerId;
            resizing = resize;
            pointerStart = evt.position;
            positionStart = new Vector2(element.style.left.value.value, element.style.top.value.value);
            scaleStart = element.resolvedStyle.scale.value.x;
            sizeStart = new Vector2(element.layout.width, element.layout.height) * scaleStart;
            capture.CapturePointer(evt.pointerId);
            editing[element].Overlay.AddToClassList("dragging");
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

        /// <summary>The top-left corner nearest to <paramref name="topLeft"/> that keeps a box of this size inside the area.</summary>
        private static Vector2 ClampInto(Rect area, Vector2 topLeft, Vector2 size)
        {
            // A box larger than the area is pinned to its top-left corner.
            float maxX = Mathf.Max(area.xMin, area.xMax - size.x);
            float maxY = Mathf.Max(area.yMin, area.yMax - size.y);
            return new Vector2(Mathf.Clamp(topLeft.x, area.xMin, maxX), Mathf.Clamp(topLeft.y, area.yMin, maxY));
        }

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
            Vector2 placed = ClampInto(ScreenOf(element), bounds.position, bounds.size);
            if (Mathf.Abs(placed.x - bounds.x) < 0.5f && Mathf.Abs(placed.y - bounds.y) < 0.5f) return;

            Vector2 local = parent.WorldToLocal(placed);
            element.style.left = Length.Percent(local.x / parent.layout.width * 100f);
            element.style.top = Length.Percent(local.y / parent.layout.height * 100f);
        }

        // A hidden panel has no size, so it can only be brought back on screen once it is shown or resized.
        private void OnPanelGeometry(VisualElement element)
        {
            if (element == activeElement || !saved.ContainsKey(element.name)) return;
            ClampIntoView(element);
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
