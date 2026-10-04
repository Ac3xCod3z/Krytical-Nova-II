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
            { "network-panel", "NETWORK PANEL" },
            { "npc-dialogue", "NPC DIALOGUE" },
            { "zone-toast", "ZONE NOTICE" },
            { "world-map", "WORLD MAP" },
            { "party-frames", "PARTY" }
        };

        private readonly Dictionary<string, VisualElement> movables = new Dictionary<string, VisualElement>();
        private readonly Dictionary<VisualElement, EditState> editing = new Dictionary<VisualElement, EditState>();
        private readonly Dictionary<string, Entry> saved = new Dictionary<string, Entry>();

        private VisualElement activeElement;
        private VisualElement activeCapture;
        private int activePointerId = -1;
        private bool resizing;
        private Vector2 pointerStart;
        private Vector2 positionStart;
        private float scaleStart;
        private Vector2 sizeStart;

        public bool IsEditing { get; private set; }
        public event Action EditingChanged;

        // ------------------------------------------------------------------ discovery & persistence

        /// <summary>Registers every named <c>.hud-movable</c> under the given document roots.</summary>
        public void Collect(IEnumerable<VisualElement> roots)
        {
            foreach (VisualElement root in roots)
            {
                root?.Query(className: MovableClass).ForEach(element =>
                {
                    if (string.IsNullOrEmpty(element.name)) return;
                    movables[element.name] = element;
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

        public void BeginEdit()
        {
            if (IsEditing) return;
            IsEditing = true;
            foreach (VisualElement element in movables.Values)
            {
                if (element.panel == null) continue;
                var state = new EditState { PreviousDisplay = element.style.display };
                // Hidden windows/dialogs are shown so they can be placed; restored when editing ends.
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
            EditingChanged?.Invoke();
        }

        public void EndEdit()
        {
            if (!IsEditing) return;
            CancelInteraction();
            foreach (var pair in editing)
            {
                VisualElement element = pair.Key;
                EditState state = pair.Value;
                state.Overlay.RemoveFromHierarchy();
                element.RemoveFromClassList(EditingClass);
                element.style.display = state.PreviousDisplay;
                if (state.PreviousText != null && element is Label label) label.text = state.PreviousText;
            }
            editing.Clear();
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
                activeElement.style.scale = new Scale(new Vector2(scale, scale));
                if (editing.TryGetValue(activeElement, out EditState state))
                    state.ScaleLabel.text = $"{Mathf.RoundToInt(scale * 100f)}%";
            }
            else
            {
                position += delta;
            }

            position = ClampPixels(activeElement, position, scale);
            activeElement.style.left = position.x;
            activeElement.style.top = position.y;
        }

        /// <summary>Keeps at least a grab-able strip of the element inside its parent (pixel space, no layout lag).</summary>
        private static Vector2 ClampPixels(VisualElement element, Vector2 position, float scale)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f) return position;
            float width = element.layout.width * scale;
            float maxX = parent.layout.width - MinVisiblePixels;
            float minX = Mathf.Min(0f, MinVisiblePixels - width);
            float maxY = parent.layout.height - MinVisiblePixels;
            return new Vector2(Mathf.Clamp(position.x, minX, Mathf.Max(minX, maxX)), Mathf.Clamp(position.y, 0f, Mathf.Max(0f, maxY)));
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
            activeElement = null;
            activeCapture = null;
            activePointerId = -1;
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

        /// <summary>Pulls a saved (percentage-placed) element back on screen after the window shrinks.</summary>
        private static void ClampIntoView(VisualElement element)
        {
            VisualElement parent = element.hierarchy.parent;
            if (parent == null || parent.layout.width < 1f || element.panel == null) return;
            Rect bounds = element.worldBound;
            Rect area = parent.worldBound;
            float minX = Mathf.Min(area.xMin, area.xMin - bounds.width + MinVisiblePixels);
            float maxX = area.xMax - MinVisiblePixels;
            float maxY = area.yMax - MinVisiblePixels;
            float x = Mathf.Clamp(bounds.x, minX, Mathf.Max(minX, maxX));
            float y = Mathf.Clamp(bounds.y, area.yMin, Mathf.Max(area.yMin, maxY));
            if (Mathf.Approximately(x, bounds.x) && Mathf.Approximately(y, bounds.y)) return;

            Vector2 local = parent.WorldToLocal(new Vector2(x, y));
            element.style.left = Length.Percent(local.x / parent.layout.width * 100f);
            element.style.top = Length.Percent(local.y / parent.layout.height * 100f);
        }
    }
}
