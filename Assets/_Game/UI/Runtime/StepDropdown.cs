using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// How many points one press of a "+" spends (owner, 17 Oct 2026): a small ▾ beside every "+" of a window opens
    /// a popup of +1, +5, +10, +50 and +100. One step per window, shown on all its "+" buttons and remembered between
    /// sessions. The popup closes on a pick, a click outside it, or Esc (<see cref="Close"/>).
    /// </summary>
    public sealed class StepDropdown
    {
        public static readonly int[] Steps = { 1, 5, 10, 50, 100 };

        private const string SplitClass = "step-split";
        private const string PlusClass = "step-plus";
        private const string CaretClass = "step-caret";
        private const string PopupClass = "step-popup";
        private const string OptionClass = "step-popup-option";
        private const string ActiveClass = "active-step";

        private readonly string prefsKey;
        private readonly List<Button> carets = new List<Button>();
        private readonly List<Button> options = new List<Button>();
        private VisualElement popup;
        private VisualElement host;
        private Button openedBy;

        /// <summary>The step chosen: how many points one press spends.</summary>
        public int Step { get; private set; } = 1;
        public bool IsOpen => popup != null && popup.style.display == DisplayStyle.Flex;

        /// <summary>Raised when another step is picked.</summary>
        public event Action<int> Picked;

        public StepDropdown(string prefsKey)
        {
            this.prefsKey = prefsKey;
            int saved = 1;
            try { saved = PlayerPrefs.GetInt(prefsKey, 1); }
            catch (Exception) { /* no prefs: the first step */ }
            Step = Array.IndexOf(Steps, saved) >= 0 ? saved : 1;
        }

        /// <summary>
        /// Puts a ▾ right of <paramref name="plus"/>: both go into a row that takes the button's place, so the pair
        /// lays out as the button alone did.
        /// </summary>
        public void Attach(Button plus)
        {
            if (plus == null || plus.parent == null || plus.parent.ClassListContains(SplitClass)) return;
            VisualElement parent = plus.parent;
            var split = new VisualElement();
            split.AddToClassList(SplitClass);
            parent.Insert(parent.IndexOf(plus), split);
            split.Add(plus);
            plus.AddToClassList(PlusClass);

            var caret = new Button { text = "▾", tooltip = "How many points one press spends" };
            caret.AddToClassList(CaretClass);
            caret.clicked += () => Toggle(caret);
            split.Add(caret);
            carets.Add(caret);
        }

        private void Toggle(Button caret)
        {
            if (IsOpen && openedBy == caret) { Close(); return; }
            Open(caret);
        }

        private void Open(Button caret)
        {
            // The popup hangs off the layer the window is on, so it is skinned like it and is not cut by its scroll view.
            VisualElement window = caret;
            while (window.parent != null && !window.ClassListContains("window")) window = window.parent;
            VisualElement layer = window.parent ?? window;
            if (popup == null || host != layer) Build(layer);
            openedBy = caret;
            foreach (Button option in options) option.EnableInClassList(ActiveClass, (int)option.userData == Step);

            Rect at = caret.worldBound;
            Vector2 local = layer.WorldToLocal(new Vector2(at.xMax, at.yMax + 2f));
            popup.style.left = local.x - 64f;
            popup.style.top = local.y;
            popup.style.display = DisplayStyle.Flex;
            popup.BringToFront();
        }

        private void Build(VisualElement layer)
        {
            popup?.RemoveFromHierarchy();
            if (host != null) host.UnregisterCallback<PointerDownEvent>(OnPointerDownAnywhere, TrickleDown.TrickleDown);
            host = layer;
            popup = new VisualElement { pickingMode = PickingMode.Position };
            popup.AddToClassList("kn-card");
            popup.AddToClassList(PopupClass);
            popup.AddToClassList("blocks-world"); // a click on it is not a click in the world
            popup.style.position = Position.Absolute;
            options.Clear();
            foreach (int step in Steps)
            {
                int value = step;
                var option = new Button { text = $"+{value}", userData = value };
                option.AddToClassList(OptionClass);
                option.clicked += () => Pick(value);
                popup.Add(option);
                options.Add(option);
            }
            popup.style.display = DisplayStyle.None;
            layer.Add(popup);
            layer.RegisterCallback<PointerDownEvent>(OnPointerDownAnywhere, TrickleDown.TrickleDown);
        }

        // A press outside the popup and its ▾ closes it (the ▾ itself toggles it).
        private void OnPointerDownAnywhere(PointerDownEvent evt)
        {
            if (!IsOpen || !(evt.target is VisualElement target)) return;
            if (popup.Contains(target) || (openedBy != null && openedBy.Contains(target))) return;
            Close();
        }

        private void Pick(int value)
        {
            Close();
            if (value == Step) return;
            Step = value;
            try { PlayerPrefs.SetInt(prefsKey, value); }
            catch (Exception) { /* not remembered */ }
            Picked?.Invoke(value);
        }

        public void Close()
        {
            if (popup != null) popup.style.display = DisplayStyle.None;
            openedBy = null;
        }

        /// <summary>Takes the popup and the listener off the HUD (the HUD is unbinding).</summary>
        public void Dispose()
        {
            Close();
            if (host != null) host.UnregisterCallback<PointerDownEvent>(OnPointerDownAnywhere, TrickleDown.TrickleDown);
            popup?.RemoveFromHierarchy();
            popup = null;
            host = null;
            carets.Clear();
        }
    }
}
