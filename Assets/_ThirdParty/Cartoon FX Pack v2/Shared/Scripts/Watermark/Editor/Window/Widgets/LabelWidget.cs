using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class LabelWidget : IWidget
    {
        private const float BOTTOM_SPACING = 2f;
        private const float INDENT = 10f;
        private const float RAIL_WIDTH = 2f;

        private readonly string text;
        private readonly int fontSize;
        private readonly Color color;
        private readonly bool sectioned;

        private GUIStyle style;
        private GUIContent content;

        public LabelWidget(string text, int fontSize = 11, Color? color = null, bool sectioned = true)
        {
            this.text = text;
            this.fontSize = fontSize;
            this.color = color ?? WatermarkColors.ContentText;
            this.sectioned = sectioned;
        }

        public void Draw(WidgetContext context)
        {
            if (style == null)
            {
                style = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    fontSize = fontSize,
                    richText = true,
                    normal = { textColor = color }
                };

                if (sectioned)
                    style.padding.left += (int)INDENT;

                content = new GUIContent(text);
            }

            var rect = GUILayoutUtility.GetRect(content, style);

            if (sectioned)
            {
                var railColor = WatermarkColors.Accent.WithAlpha(0.25f);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, RAIL_WIDTH, rect.height), railColor);
            }

            EditorGUI.LabelField(rect, content, style);

            GUILayout.Space(BOTTOM_SPACING);
        }
    }
}