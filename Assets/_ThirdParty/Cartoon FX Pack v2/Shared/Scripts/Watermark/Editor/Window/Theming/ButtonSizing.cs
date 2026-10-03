using UnityEngine;

namespace laplahce.Watermark.Theming
{
    public static class ButtonSizing
    {
        private const float HORIZONTAL_PADDING = 24f;

        public static float CalculateWidth(string label, int fontSize, float minWidth = 0f)
        {
            var style = new GUIStyle(GUI.skin.button) { fontSize = fontSize, fontStyle = FontStyle.Bold };
            float contentWidth = style.CalcSize(new GUIContent(label)).x;
            return Mathf.Max(minWidth, contentWidth + HORIZONTAL_PADDING);
        }
    }
}