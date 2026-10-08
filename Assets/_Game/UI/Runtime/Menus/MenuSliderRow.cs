using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// One slider row built on Synty's Settings_Item_Slider: a title, the slider with a label at each end, and
    /// two arrows that nudge it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuSliderRow : MonoBehaviour
    {
        private const int ArrowSteps = 20; // an arrow click moves a twentieth of the range

        private TMP_Text title, minLabel, maxLabel;
        private Slider slider;
        private Action<float> changed;
        private bool bound;

        public void Show(string label, string minText, string maxText, float min, float max, float current, Action<float> onChanged)
        {
            Bind();
            changed = null; // setting the range below must not report a change
            if (title != null) title.text = label;
            if (minLabel != null) minLabel.text = minText;
            if (maxLabel != null) maxLabel.text = maxText;
            if (slider != null)
            {
                slider.wholeNumbers = true;
                slider.minValue = min;
                slider.maxValue = max;
                slider.SetValueWithoutNotify(current);
            }
            changed = onChanged;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            changed = null;
            gameObject.SetActive(false);
        }

        /// <summary>Move the handle without reporting a change (undo, presets, randomise).</summary>
        public void SetValue(float current)
        {
            if (slider != null) slider.SetValueWithoutNotify(current);
        }

        private void Bind()
        {
            if (bound) return;
            bound = true;
            title = MenuUi.Find<TMP_Text>(transform, "Label_Button_Name");
            minLabel = MenuUi.Find<TMP_Text>(transform, "Label_Value_Min");
            maxLabel = MenuUi.Find<TMP_Text>(transform, "Label_Value_Max");
            slider = GetComponentInChildren<Slider>(true);
            if (slider == null) return;
            slider.onValueChanged.AddListener(v =>
            {
                changed?.Invoke(v);
                MenuMoments.Raise(MenuMoment.Slide, slider.handleRect != null ? slider.handleRect : slider.transform);
            });
            Arrow("Button_Arrow_Left", -1);
            Arrow("Button_Arrow_Right", 1);
        }

        private void Arrow(string buttonName, int direction)
        {
            var button = MenuUi.Find<Button>(transform, buttonName);
            if (button == null) return;
            MenuTouch.On(button.gameObject);
            button.onClick.AddListener(() =>
            {
                float nudge = Mathf.Max((slider.maxValue - slider.minValue) / ArrowSteps, 1f);
                slider.value = Mathf.Clamp(slider.value + direction * nudge, slider.minValue, slider.maxValue);
            });
        }
    }
}
