using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Where a box goes so that all of it is on screen, as plain numbers so they can be tested. Everything is in one
    /// space (panel units, y down): the area is the screen, or the screen less a margin.
    /// </summary>
    public static class ScreenFit
    {
        /// <summary>The area less <paramref name="margin"/> on every side (never inside out).</summary>
        public static Rect Inset(Rect area, float margin)
        {
            float x = Mathf.Min(margin, area.width * 0.5f), y = Mathf.Min(margin, area.height * 0.5f);
            return new Rect(area.x + x, area.y + y, area.width - x * 2f, area.height - y * 2f);
        }

        /// <summary>The top-left corner nearest to <paramref name="topLeft"/> that keeps a box of this size inside the area.</summary>
        public static Vector2 Clamp(Rect area, Vector2 topLeft, Vector2 size)
        {
            // A box larger than the area is pinned to its top-left corner.
            float maxX = Mathf.Max(area.xMin, area.xMax - size.x);
            float maxY = Mathf.Max(area.yMin, area.yMax - size.y);
            return new Vector2(Mathf.Clamp(topLeft.x, area.xMin, maxX), Mathf.Clamp(topLeft.y, area.yMin, maxY));
        }

        /// <summary>How much a box of this size must shrink to fit the area: 1 when it fits as it is.</summary>
        public static float ScaleToFit(Rect area, Vector2 size)
        {
            if (size.x <= 0f || size.y <= 0f || area.width <= 0f || area.height <= 0f) return 1f;
            return Mathf.Min(1f, Mathf.Min(area.width / size.x, area.height / size.y));
        }

        /// <summary>
        /// The top-left corner of a box put beside <paramref name="anchor"/>: to its right when there is room, else
        /// to its left, else on the side with more room; level with its top, then moved as little as it takes to be
        /// inside the area.
        /// </summary>
        /// <summary>
        /// Centred over something, a gap above it; under it when there is no room above; and never past the area's
        /// sides. For the Hotbar's hover card, which must not cover the slots beside the one under the pointer.
        /// </summary>
        public static Vector2 Above(Rect anchor, Vector2 size, Rect area, float gap)
        {
            float x = anchor.center.x - size.x * 0.5f;
            float y = anchor.yMin - gap - size.y;
            if (y < area.yMin) y = anchor.yMax + gap;
            return Clamp(area, new Vector2(x, y), size);
        }

        public static Vector2 Beside(Rect anchor, Vector2 size, Rect area, float gap)
        {
            float right = anchor.xMax + gap, left = anchor.xMin - gap - size.x;
            float x = right + size.x <= area.xMax ? right
                : left >= area.xMin ? left
                : area.xMax - anchor.xMax >= anchor.xMin - area.xMin ? right : left;
            return Clamp(area, new Vector2(x, anchor.yMin), size);
        }
    }
}
