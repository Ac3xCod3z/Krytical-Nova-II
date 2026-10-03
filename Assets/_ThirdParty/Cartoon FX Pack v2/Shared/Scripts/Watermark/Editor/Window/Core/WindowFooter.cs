using System;
using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Widgets;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Core
{
    public sealed class WindowFooter
    {
        private const float CLOSE_MIN_WIDTH = 88f;
        private const float CLOSE_HEIGHT = 22f;
        private const string CLOSE_LABEL = "Close";

        public void Draw(WidgetContext context, ref bool dontShowAgain, string dontShowAgainKey, Action onClose)
        {
            var separatorRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(separatorRect, WatermarkColors.Separator);
            EditorGUILayout.Space(5);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);

                bool next = GUILayout.Toggle(dontShowAgain, new GUIContent(" Don't show on startup"),
                    new GUIStyle(EditorStyles.toggle) { fontSize = 10, normal = { textColor = WatermarkColors.SubtleText } });

                if (next != dontShowAgain)
                {
                    dontShowAgain = next;
                    EditorPrefs.SetBool(dontShowAgainKey, dontShowAgain);
                }

                GUILayout.FlexibleSpace();

                float closeWidth = ButtonSizing.CalculateWidth(CLOSE_LABEL, 11, CLOSE_MIN_WIDTH);
                var closeRect = GUILayoutUtility.GetRect(closeWidth, CLOSE_HEIGHT, GUILayout.Width(closeWidth), GUILayout.Height(CLOSE_HEIGHT));

                var buttonRect = context.ButtonStyles.DrawShadow(closeRect, false, 3, fontSize: 11, fixedHeight: CLOSE_HEIGHT, fixedWidth: closeWidth);

                var buttonStyle = context.ButtonStyles.BuildPrimary(11, buttonRect.height);

                if (GUI.Button(buttonRect, CLOSE_LABEL, buttonStyle)) onClose?.Invoke();
                EditorGUIUtility.AddCursorRect(closeRect, MouseCursor.Link);

                GUILayout.Space(10);
            }

            EditorGUILayout.Space(6);
        }
    }
}