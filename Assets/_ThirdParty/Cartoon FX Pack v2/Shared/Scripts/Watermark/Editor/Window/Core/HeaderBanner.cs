using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Widgets;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Core
{
    public sealed class HeaderBanner
    {
        private const float HEIGHT = 62f;
        private const float ICON_SIZE = 30f;
        private const float PAD_X = 10f;

        public void Draw(WidgetContext context, string packName, string tagline, string packVersion)
        {
            var rect = GUILayoutUtility.GetRect(0f, HEIGHT, GUILayout.ExpandWidth(true));

            EditorGUI.DrawRect(rect, WatermarkColors.ContentBackground);
            var bannerTexture = context.TextureCache.Get(new Color(0.20f, 0.15f, 0.45f, 0.72f), new Color(0.10f, 0.08f, 0.22f, 0.00f));
            GUI.DrawTexture(rect, bannerTexture, ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), WatermarkColors.Accent.WithAlpha(0.30f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), WatermarkColors.Accent.WithAlpha(0.18f));

            float centerY = rect.y + HEIGHT * 0.5f;
            var iconRect = new Rect(PAD_X, centerY - ICON_SIZE * 0.5f, ICON_SIZE, ICON_SIZE);

            EditorGUI.DrawRect(iconRect, WatermarkColors.Accent.WithAlpha(0.18f));
            EditorGUI.DrawRect(new Rect(iconRect.x, iconRect.y, iconRect.width, 1f), WatermarkColors.Accent.WithAlpha(0.45f));
            EditorGUI.DrawRect(new Rect(iconRect.x, iconRect.yMax - 1f, iconRect.width, 1f), WatermarkColors.Accent.WithAlpha(0.45f));
            GUI.Label(iconRect, EditorGUIUtility.IconContent("ParticleSystem Icon"),
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fixedWidth = ICON_SIZE, fixedHeight = ICON_SIZE, padding = new RectOffset(4, 4, 4, 4) });

            float textX = PAD_X + ICON_SIZE + 8f;
            EditorGUI.LabelField(new Rect(textX, centerY - 16f, rect.width - textX - 60f, 18f), packName,
                new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = WatermarkColors.TitleText } });
            EditorGUI.LabelField(new Rect(textX, centerY + 2f, rect.width - textX - 60f, 14f), tagline,
                new GUIStyle(EditorStyles.miniLabel) { fontSize = 10, fontStyle = FontStyle.Italic, normal = { textColor = WatermarkColors.SubtitleText } });

            float pillWidth = 36f, pillHeight = 16f;
            var pillRect = new Rect(rect.xMax - pillWidth - 10f, rect.y + 8f, pillWidth, pillHeight);
            EditorGUI.DrawRect(pillRect, WatermarkColors.BadgeBackground);
            EditorGUI.LabelField(pillRect, new GUIContent("v" + packVersion),
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10, padding = new RectOffset(7, 7, 2, 2), normal = { textColor = WatermarkColors.BadgeText } });
        }
    }
}
