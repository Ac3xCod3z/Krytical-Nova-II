using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// One "title  &lt; value &gt;" row built on Synty's Settings_Item_Array: the arrows step through the options
    /// and the value label shows where you are. The row does not know what it edits; the screen hands it a title,
    /// a way to read the value and a step action.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuOptionRow : MonoBehaviour
    {
        private TMP_Text title, value;
        private Func<string> read;
        private Action<int> step;
        private bool bound;

        /// <summary>Show the row. <paramref name="onStep"/> gets -1 (left arrow) or +1 (right arrow).</summary>
        public void Show(string label, Func<string> readValue, Action<int> onStep)
        {
            Bind();
            read = readValue;
            step = onStep;
            if (title != null) title.text = label;
            gameObject.SetActive(true);
            Refresh();
        }

        public void Hide()
        {
            read = null;
            step = null;
            gameObject.SetActive(false);
        }

        /// <summary>Re-read the value, after something else changed what the row shows.</summary>
        public void Refresh()
        {
            if (value != null && read != null) value.text = read();
        }

        private void Bind()
        {
            if (bound) return;
            bound = true;
            title = MenuUi.Find<TMP_Text>(transform, "Label_Button_Name");
            value = MenuUi.Find<TMP_Text>(transform, "Label_Setting_Value");
            Arrow("Button_Left", -1);
            Arrow("Button_Right", 1);
        }

        private void Arrow(string buttonName, int direction)
        {
            var button = MenuUi.Find<Button>(transform, buttonName);
            if (button == null) return;
            MenuTouch.On(button.gameObject);
            button.onClick.AddListener(() =>
            {
                step?.Invoke(direction);
                Refresh();
                MenuMoments.Raise(MenuMoment.Toggle, value != null ? value.transform : transform);
            });
        }
    }
}
