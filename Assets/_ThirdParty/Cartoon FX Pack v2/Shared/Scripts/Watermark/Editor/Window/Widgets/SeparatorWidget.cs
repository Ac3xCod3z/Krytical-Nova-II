using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class SeparatorWidget : IWidget
    {
        public void Draw(WidgetContext context)
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, WatermarkColors.Separator);
        }
    }
}
