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
            style.left = local.x;
            style.top = local.y - PointerGap;
            if (style.display != DisplayStyle.Flex)
            {
                style.display = DisplayStyle.Flex;
                BringToFront();
            }
        }

        public void Hide() => style.display = DisplayStyle.None;
    }
}
