using System.Linq;
using laplahce.Watermark.Theming;
using UnityEditor;
using UnityEngine;

namespace laplahce.Watermark.Widgets
{
    public sealed class ButtonRowWidget : IWidget
    {
        private const float SPACING = 4f;

        private readonly IWidget[] buttons;

        public ButtonRowWidget(params IWidget[] buttons)
        {
            this.buttons = buttons;
        }

        public void Draw(WidgetContext context)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].Draw(context);
                    if (i < buttons.Length - 1) GUILayout.Space(SPACING);
                }
            }
        }
    }
}
