using System;
using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Widgets
{
    public sealed class LinkCardWidget : IWidget
    {
        private const float BUTTON_HEIGHT = 22f;
        private const float BUTTON_MIN_WIDTH = 84f;

        private readonly string title;
        private readonly string description;
        private readonly string buttonLabel;
        private readonly string url;
        private readonly string targetTab;
        private readonly Color accentColor;

        public LinkCardWidget(string title, string buttonLabel, string url, string description = null, Color? accentColor = null)
        {
            this.title = title;
            this.description = description;
            this.buttonLabel = buttonLabel;
            this.url = url;
            this.targetTab = null;
            this.accentColor = accentColor ?? WatermarkColors.Accent;
        }

        private LinkCardWidget(string title, string buttonLabel, string description, string targetTab, Color accentColor)
        {
            this.title = title;
            this.buttonLabel = buttonLabel;
            this.description = description;
            this.url = null;
            this.targetTab = targetTab;
            this.accentColor = accentColor;
        }

        public static LinkCardWidget ToTab(string title, string buttonLabel, string targetTab, string description = null, Color? accentColor = null)
        {
            return new LinkCardWidget(title, buttonLabel, description, targetTab, accentColor ?? WatermarkColors.Accent);
        }

        public void Draw(WidgetContext context)
        {
            bool hasDescription = !string.IsNullOrEmpty(description);
            float height = hasDescription ? 44f : 34f;

            var card = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(card, WatermarkColors.ContentBackground);
            EditorGUI.DrawRect(new Rect(card.x, card.y, 3, card.height), accentColor);

            float buttonWidth = ButtonSizing.CalculateWidth(buttonLabel, 11, BUTTON_MIN_WIDTH);
            float textWidth = card.width - (buttonWidth + 16f);

            if (hasDescription)
            {
                EditorGUI.LabelField(new Rect(card.x + 12, card.y + 6, textWidth, 18f), title,
                    new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = WatermarkColors.TitleText } });
                EditorGUI.LabelField(new Rect(card.x + 12, card.y + 24, textWidth, 15f), description,
                    new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, normal = { textColor = WatermarkColors.SubtitleText } });
            }
            else
            {
                EditorGUI.LabelField(new Rect(card.x + 12, card.y, textWidth, card.height), title,
                    new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 11, normal = { textColor = WatermarkColors.ContentText } });
            }

            var buttonRect = new Rect(card.xMax - (buttonWidth + 8f), card.y + (card.height - BUTTON_HEIGHT) / 2f, buttonWidth, BUTTON_HEIGHT);

            Action onClick = targetTab != null
                ? (Action)(() => context.NavigateToTab?.Invoke(targetTab))
                : () => Application.OpenURL(url);

            var button = new GradientButtonWidget(buttonLabel, onClick, BUTTON_HEIGHT);
            button.DrawAt(context, buttonRect);
        }
    }
}