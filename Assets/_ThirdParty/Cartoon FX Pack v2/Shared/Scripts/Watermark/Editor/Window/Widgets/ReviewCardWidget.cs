using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class ReviewCardWidget : IWidget
    {
        private const float HEIGHT = 44f;
        private const float BUTTON_MIN_WIDTH = 110f;
        private const float BUTTON_HEIGHT = 22f;

        private readonly string subtitle;
        private readonly string buttonLabel;
        private readonly string reviewUrl;
        private readonly Color accentColor;

        public ReviewCardWidget(string subtitle, string buttonLabel, string reviewUrl, Color? accentColor = null)
        {
            this.subtitle = subtitle;
            this.buttonLabel = buttonLabel;
            this.reviewUrl = reviewUrl;
            this.accentColor = accentColor ?? WatermarkColors.Accent;
        }

        public void Draw(WidgetContext context)
        {
            var card = GUILayoutUtility.GetRect(0, HEIGHT, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(card, WatermarkColors.ContentBackground);
            EditorGUI.DrawRect(new Rect(card.x, card.y, 3, card.height), accentColor);

            float buttonWidth = ButtonSizing.CalculateWidth(buttonLabel, 11, BUTTON_MIN_WIDTH);
            float textWidth = card.width - (buttonWidth + 20f);

            EditorGUI.LabelField(new Rect(card.x + 10, card.y + 7, textWidth, 18f), "★★★★★",
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = new Color(0.96f, 0.65f, 0.14f) } });
            EditorGUI.LabelField(new Rect(card.x + 10, card.y + 26, textWidth, 14f), subtitle,
                new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, normal = { textColor = WatermarkColors.SubtitleText } });

            var buttonRect = new Rect(card.xMax - (buttonWidth + 10f), card.y + (card.height - BUTTON_HEIGHT) / 2f, buttonWidth, BUTTON_HEIGHT);
            var button = new GradientButtonWidget(buttonLabel, () => Application.OpenURL(reviewUrl), BUTTON_HEIGHT);
            button.DrawAt(context, buttonRect);
        }
    }
}