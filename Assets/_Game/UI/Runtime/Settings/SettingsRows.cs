using System;
using KryticalNova.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The rows a Settings page is built from. Each returns a refresh action: call it to show the stored value again
    /// (a preset changed several options at once, or another window changed one).
    /// </summary>
    public static class SettingsRows
    {
        public static void Header(VisualElement page, string text)
        {
            var label = new Label(text);
            label.AddToClassList("eyebrow");
            label.AddToClassList("section");
            page.Add(label);
        }

        public static Label Help(VisualElement page, string text)
        {
            var label = new Label(text);
            label.AddToClassList("help");
            page.Add(label);
            return label;
        }

        /// <summary>One of a few named values, stepped through with the arrows on either side.</summary>
        public static Action Choice(VisualElement page, string label, string[] options, Func<int> get, Action<int> set)
        {
            VisualElement row = Row(page, label);
            var value = new Label();
            value.AddToClassList("settings-row-value");
            void Show() => value.text = options.Length == 0 ? "—" : options[Mathf.Clamp(get(), 0, options.Length - 1)];
            void Step(int by)
            {
                if (options.Length == 0) return;
                set((Mathf.Clamp(get(), 0, options.Length - 1) + by + options.Length) % options.Length);
                Show();
                MenuMoments.Raise(MenuMoment.Toggle, value);
            }
            row.Add(StepButton(false, () => Step(-1)));
            row.Add(value);
            row.Add(StepButton(true, () => Step(1)));
            Show();
            SettingsLedger.Recording?.Add(get, set, Show);
            return Show;
        }

        // An arrow beside a slider moves it this share of its range.
        private const float SliderNudge = 0.05f;

        /// <summary>A value on a scale, shown as a percentage where 1 is 100%.</summary>
        public static Action Slider(VisualElement page, string label, float min, float max, Func<float> get, Action<float> set)
            => Slider(page, label, min, max, get, set, out _);

        /// <param name="format">How the value is written on the handle; a percentage when left out.</param>
        public static Action Slider(VisualElement page, string label, float min, float max, Func<float> get, Action<float> set, out VisualElement row,
            Func<float, string> format = null)
        {
            string Percent(float amount) => format != null ? format(amount) : Mathf.RoundToInt(amount * 100f) + "%";
            row = Row(page, label);
            var slider = new Slider(min, max);
            slider.AddToClassList("settings-slider");
            // The value is written on the slider's handle, as on the packs' sliders, with an arrow at either end.
            var value = new Label { pickingMode = PickingMode.Ignore };
            value.AddToClassList("settings-slider-value");
            void Show()
            {
                float current = Mathf.Clamp(get(), min, max);
                slider.SetValueWithoutNotify(current);
                value.text = Percent(current);
            }
            slider.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                value.text = Percent(get());
            });
            void Nudge(int by)
            {
                set(Mathf.Clamp(get() + by * (max - min) * SliderNudge, min, max));
                Show();
                MenuMoments.Raise(MenuMoment.Slide, slider);
            }
            row.Add(StepButton(false, () => Nudge(-1)));
            row.Add(slider);
            row.Add(StepButton(true, () => Nudge(1)));
            VisualElement handle = slider.Q("unity-dragger");
            if (handle != null) handle.Add(value);
            else row.Add(value);
            Show();
            SettingsLedger.Recording?.Add(get, set, Show);
            return Show;
        }

        public static Action Toggle(VisualElement page, string label, Func<bool> get, Action<bool> set)
        {
            var toggle = new Toggle(label);
            toggle.AddToClassList("kn-toggle");
            toggle.AddToClassList("kn-card");
            page.Add(toggle);
            return Bind(toggle, get, set);
        }

        /// <summary>A toggle that is already laid out in the window's markup.</summary>
        public static Action Bind(Toggle toggle, Func<bool> get, Action<bool> set)
        {
            AddOnOff(toggle);
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
            void Show() => toggle.SetValueWithoutNotify(get());
            Show();
            SettingsLedger.Recording?.Add(get, set, Show);
            return Show;
        }

        public static VisualElement Row(VisualElement page, string label)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            row.AddToClassList("kn-card");
            var name = new Label(label);
            name.AddToClassList("settings-row-label");
            row.Add(name);
            page.Add(row);
            return row;
        }

        /// <summary>An arrow: the pack's own picture of one (the skin's sheet draws it), back or forward.</summary>
        private static Button StepButton(bool forward, Action clicked)
        {
            var button = new Button(clicked);
            button.AddToClassList("settings-step");
            button.AddToClassList(forward ? "settings-step--forward" : "settings-step--back");
            return button;
        }

        /// <summary>
        /// A switch is shown as the packs show one: ON and OFF side by side, the one that holds lit. The switch
        /// itself is unchanged (a click anywhere on its line flips it).
        /// </summary>
        private static void AddOnOff(Toggle toggle)
        {
            if (toggle == null || toggle.Q(className: "toggle-pair") != null) return;
            var pair = new VisualElement { pickingMode = PickingMode.Ignore };
            pair.AddToClassList("toggle-pair");
            foreach ((string text, string side) in new[] { ("ON", "toggle-on"), ("OFF", "toggle-off") })
            {
                var half = new Label(text) { pickingMode = PickingMode.Ignore };
                half.AddToClassList("toggle-half");
                half.AddToClassList(side);
                pair.Add(half);
            }
            toggle.Add(pair);
        }
    }
}
