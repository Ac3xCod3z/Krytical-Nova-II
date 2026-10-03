using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class SubheaderWidget : IWidget
    {
        private const float HEIGHT = 22f;
        private const float TOP_MARGIN = 8f;
        private const float BOTTOM_MARGIN = 2f;
        private const float ACCENT_BAR_WIDTH = 2f;
        private const float LABEL_INSET = 8f;

        private static readonly Color SeparatorColor = new Color(0f, 0f, 0f, 0.25f);

        private readonly string label;
        private readonly Color color;

        public SubheaderWidget(string label, Color? color = null)
        {
            this.label = label;
            this.color = color ?? WatermarkColors.TitleText;
        }

        public void Draw(WidgetContext context)
        {
            GUILayout.Space(TOP_MARGIN);

            var rect = GUILayoutUtility.GetRect(0f, HEIGHT, GUILayout.ExpandWidth(true));

            var fill = WatermarkColors.Accent.WithAlpha(0.10f);
            var texture = context.TextureCache.Get(fill, fill);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, ACCENT_BAR_WIDTH, rect.height), WatermarkColors.Accent);

            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), SeparatorColor);

            EditorGUI.LabelField(
                new Rect(rect.x + ACCENT_BAR_WIDTH + LABEL_INSET, rect.y, rect.width - ACCENT_BAR_WIDTH - LABEL_INSET, rect.height),
                label,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 10,
                    alignment = TextAnchor.MiddleLeft,
                    normal = { textColor = color }
                });

            GUILayout.Space(BOTTOM_MARGIN);
        }
    }
}