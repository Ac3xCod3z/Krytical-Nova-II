using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Keeps one floating element (the item card, a right-click menu, a text tooltip) wholly on screen. The element
    /// is absolute and placed with left and top. It is placed again whenever its size changes while it is shown,
    /// because what it says changes after it has been placed: asked the frame it is shown, an element still has the
    /// size of what it showed before, or none, and that is how the item card came to hang off the bottom of the
    /// screen. One too large for the screen is shrunk to fit rather than cut off.
    /// </summary>
    public sealed class FloatingFit
    {
        private readonly VisualElement element;
        private readonly float gap, margin;
        private Rect anchor;
        private Vector2 corner;
        private bool beside, placed;
        // Where the last two moves of this frame put it (see Fit: it is never sent back and forth).
        private Vector2 last, beforeLast;
        private int moves, movesFrame = -1;

        /// <param name="gap">Kept between the element and what it stands beside.</param>
        /// <param name="margin">Kept to the screen's edge: a skin's frame stands a little outside the element's own box.</param>
        public FloatingFit(VisualElement floating, float gap = 8f, float margin = 12f)
        {
            element = floating;
            this.gap = gap;
            this.margin = margin;
            element?.RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        /// <summary>Beside something (its bounds in the panel): right of it, or left when there is no room.</summary>
        public void Beside(Rect anchorBounds)
        {
            beside = true;
            anchor = anchorBounds;
            Start();
        }

        /// <summary>With its top-left corner at a point in the panel, or as near to it as the screen allows.</summary>
        public void At(Vector2 panelPoint)
        {
            beside = false;
            corner = panelPoint;
            Start();
        }

        private void Start()
        {
            if (element == null) return;
            placed = true;
            moves = 0;
            Fit();
            // Its content was set this frame: the layout pass that sizes it comes after this, and tells Fit again.
            // This is for an element whose size and place turn out the same as last time, which hears nothing.
            element.schedule.Execute(Fit);
        }

        private void Fit()
        {
            if (!placed || element.panel == null || element.resolvedStyle.display == DisplayStyle.None) return;
            VisualElement parent = element.hierarchy.parent;
            Rect bounds = element.worldBound;
            if (parent == null || float.IsNaN(bounds.width) || bounds.width < 1f || bounds.height < 1f) return;

            // The size it has at its own scale 1, in the panel's units (an ancestor may be scaled: the menus' book page).
            float own = Mathf.Max(0.01f, element.resolvedStyle.scale.value.x);
            Vector2 size = bounds.size / own;
            Rect area = ScreenFit.Inset(element.panel.visualTree.worldBound, margin);
            float scale = ScreenFit.ScaleToFit(area, size);
            Vector2 at = beside ? ScreenFit.Beside(anchor, size * scale, area, gap) : ScreenFit.Clamp(area, corner, size * scale);

            Vector2 local = parent.WorldToLocal(at);
            Vector2 now = parent.WorldToLocal(bounds.position);
            if (Mathf.Abs(local.x - now.x) < 0.5f && Mathf.Abs(local.y - now.y) < 0.5f && Mathf.Abs(scale - own) < 0.004f) return;

            // The layout snaps to whole pixels, so an element's size can differ by one pixel with where it stands.
            // When that pixel decides the side it goes on, each move calls for the other and the layout never
            // settles ("Layout update failed to stabilize"). Sent back to where it stood before its last move, in
            // the same frame, it stays where it is: both places are on screen to within that pixel.
            if (movesFrame != Time.frameCount)
            {
                movesFrame = Time.frameCount;
                moves = 0;
            }
            if (moves >= 2 && (at - beforeLast).sqrMagnitude < 4f) return;
            beforeLast = last;
            last = at;
            moves++;

            element.style.transformOrigin = new TransformOrigin(0f, 0f);
            element.style.scale = scale < 0.999f ? new Scale(new Vector2(scale, scale)) : new StyleScale(StyleKeyword.Null);
            element.style.left = local.x;
            element.style.top = local.y;
        }
    }
}
