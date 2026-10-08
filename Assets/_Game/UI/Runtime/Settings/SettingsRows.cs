using System;
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
            }
            row.Add(StepButton("<", () => Step(-1)));
            row.Add(value);
            row.Add(StepButton(">", () => Step(1)));
            Show();
            SettingsLedger.Recording?.Add(get, set, Show);
            return Show;
        }

        /// <summary>A value on a scale, shown as a percentage where 1 is 100%.</summary>
        public static Action Slider(VisualElement page, string label, float min, float max, Func<float> get, Action<float> set)
            => Slider(page, label, min, max, get, set, out _);

        public static Action Slider(VisualElement page, string label, float min, float max, Func<float> get, Action<float> set, out VisualElement row)
        {
            string Percent(float amount) => Mathf.RoundToInt(amount * 100f) + "%";
            row = Row(page, label);
            var slider = new Slider(min, max);
            slider.AddToClassList("settings-slider");
            var value = new Label();
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
            row.Add(slider);
            row.Add(value);
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

        private static Button StepButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("settings-step");
            return button;
        }
    }
}
