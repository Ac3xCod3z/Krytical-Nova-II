using System;
using UnityEditor;
using UnityEngine;

namespace laplahce.Watermark.Widgets
{
    public sealed class GradientButtonWidget : IWidget
    {
        private const float DEFAULT_HEIGHT = 22f;

        private readonly string label;
        private readonly Action onClick;
        private readonly float height;
        private readonly bool usePipelinePalette;

        public GradientButtonWidget(string label, Action onClick, float height = DEFAULT_HEIGHT, bool usePipelinePalette = false)
        {
            this.label = label;
            this.onClick = onClick;
            this.height = height;
            this.usePipelinePalette = usePipelinePalette;
        }

        public void Draw(WidgetContext context)
        {
            var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            DrawAt(context, rect);
        }

        public void DrawAt(WidgetContext context, Rect rect)
        {
            var buttonRect = context.ButtonStyles.DrawShadow(rect, usePipelinePalette, fontSize: 11);

            var buttonStyle = usePipelinePalette
                ? context.ButtonStyles.BuildPipeline(11, buttonRect.height)
                : context.ButtonStyles.BuildPrimary(11, buttonRect.height);

            if (GUI.Button(buttonRect, label, buttonStyle)) onClick?.Invoke();
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
        }
    }
}
