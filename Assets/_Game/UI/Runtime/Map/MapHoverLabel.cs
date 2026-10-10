using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The name shown beside the pointer while it is over something on a map. Lives on the HUD root (outside the
    /// maps' clipped and scaled panels), so it is styled here rather than from a map's stylesheet.
    /// </summary>
    public sealed class MapHoverLabel : Label
    {
        private static readonly Color Background = new Color32(14, 16, 28, 235);
        private static readonly Color Border = new Color32(114, 222, 196, 255);
        private static readonly Color Text = new Color32(244, 238, 214, 255);
        private const float PointerGap = 14f;
        private const float ScreenEdge = 4f;     // kept to the screen's edge
        private const float PointerBelow = 18f;  // room for the pointer itself when the name goes under it

        public MapHoverLabel()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;
            style.backgroundColor = Background;
            style.color = Text;
            style.fontSize = 13f;
            style.unityFontStyleAndWeight = FontStyle.Bold;
            style.paddingLeft = 8f;
            style.paddingRight = 8f;
            style.paddingTop = 3f;
            style.paddingBottom = 3f;
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 4f;
            style.borderLeftWidth = style.borderRightWidth = style.borderTopWidth = style.borderBottomWidth = 1f;
            style.borderLeftColor = style.borderRightColor = style.borderTopColor = style.borderBottomColor = Border;
            style.whiteSpace = WhiteSpace.NoWrap;
            // Sits above the pointer, centred on it, whatever its own width turns out to be.
            style.translate = new Translate(Length.Percent(-50f), Length.Percent(-100f));
        }

        /// <param name="panelPosition">The pointer, in panel coordinates (an event's <c>position</c>).</param>
        public void Show(string name, Vector2 panelPosition)
        {
            if (parent == null) return;
            if (text != name) text = name;
            Vector2 local = parent.WorldToLocal(panelPosition);
            // Never off the screen: held in from the sides, and under the pointer when there is no room above it.
            // Its size is the last one laid out; the pointer moves again before a wrong one could be noticed.
            float width = float.IsNaN(resolvedStyle.width) ? 0f : resolvedStyle.width;
            float height = float.IsNaN(resolvedStyle.height) ? 0f : resolvedStyle.height;
            float x = local.x, y = local.y - PointerGap;
            if (panel != null)
            {
                Rect screen = parent.WorldToLocal(panel.visualTree.worldBound);
                float half = width * 0.5f + ScreenEdge;
                x = Mathf.Clamp(x, screen.xMin + half, Mathf.Max(screen.xMin + half, screen.xMax - half));
                if (y - height < screen.yMin + ScreenEdge) y = local.y + PointerGap + height + PointerBelow;
            }
            style.left = x;
            style.top = y;
            if (style.display != DisplayStyle.Flex)
            {
                style.display = DisplayStyle.Flex;
                BringToFront();
            }
        }

        public void Hide() => style.display = DisplayStyle.None;
    }
}
