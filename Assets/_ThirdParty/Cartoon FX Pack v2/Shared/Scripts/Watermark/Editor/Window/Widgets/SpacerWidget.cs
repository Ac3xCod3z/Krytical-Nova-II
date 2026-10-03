using UnityEditor;

namespace laplahce.Watermark.Widgets
{
    public sealed class SpacerWidget : IWidget
    {
        private readonly float height;

        public SpacerWidget(float height)
        {
            this.height = height;
        }

        public void Draw(WidgetContext context)
        {
            EditorGUILayout.Space(height);
        }
    }
}
