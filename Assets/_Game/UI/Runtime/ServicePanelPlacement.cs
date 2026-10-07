using KryticalNova.Items;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Where a service window goes and how far the camera slides for it, as plain numbers so they can be tested.
    /// Everything is in panel units across the screen, left to right.
    /// </summary>
    public static class ServicePlacementRules
    {
        /// <summary>
        /// The left edge of a panel <paramref name="width"/> wide put beside another one: on its left when there is
        /// room, else on its right, else as far left as the screen allows.
        /// </summary>
        public static float DockBeside(float besideLeft, float besideRight, float width, float screenWidth, float gap, float margin)
        {
            float left = besideLeft - gap - width;
            if (left >= margin) return left;
            float right = besideRight + gap;
            if (right + width <= screenWidth - margin) return right;
            return Mathf.Clamp(left, margin, Mathf.Max(margin, screenWidth - margin - width));
        }

        /// <summary>
        /// How far across the screen the character should stand so the windows do not cover it: 0 leaves it in the
        /// middle, -0.5 is the left edge, +0.5 the right. The character goes to the middle of the wider free side.
        /// 0 when the windows are clear of a character standing in the middle anyway.
        /// </summary>
        /// <param name="coverLeft">Left edge of the windows.</param>
        /// <param name="coverRight">Right edge of the windows.</param>
        /// <param name="characterHalfWidth">Half the room the character takes on screen.</param>
        /// <param name="limit">The furthest the character may be moved from the middle.</param>
        public static float Framing(float coverLeft, float coverRight, float screenWidth, float characterHalfWidth, float limit)
        {
            if (screenWidth <= 0f || coverRight <= coverLeft) return 0f;
            float middle = screenWidth * 0.5f;
            if (middle + characterHalfWidth <= coverLeft || middle - characterHalfWidth >= coverRight) return 0f;
            float freeLeft = Mathf.Max(0f, coverLeft), freeRight = Mathf.Max(0f, screenWidth - coverRight);
            float target = freeLeft >= freeRight ? freeLeft * 0.5f : screenWidth - freeRight * 0.5f;
            return Mathf.Clamp(target / screenWidth - 0.5f, -limit, limit);
        }
    }

    /// <summary>
    /// Keeps a service window (the forge, the Herald) off the character. While the window is open it sits docked
    /// beside the Pack, or against the right edge when there is no Pack beside it, and the camera slides so the
    /// character stands in the clear part of the screen; closing puts both back. A window the player has placed in
    /// the HUD layout editor stays where they put it: only the camera moves for it.
    /// </summary>
    public sealed class ServicePanelPlacement
    {
        private const float Gap = 8f, Margin = 12f;
        private const float FramingLimit = 0.4f;
        // The character's box on screen, from the root (a metre above the feet): feet, head, and width as a share of height.
        private const float FeetBelow = 1f, HeadAbove = 0.9f, WidthOfHeight = 0.5f;
        private const string MinimapName = "minimap";

        private VisualElement panel, beside;
        private bool active, docked;
        private Camera viewer;

        /// <param name="window">The service window, already shown.</param>
        /// <param name="pack">A window it should sit beside (the Pack), or null.</param>
        public void Open(VisualElement window, VisualElement pack)
        {
            Close();
            panel = window;
            beside = pack;
            active = panel != null;
        }

        public void Close()
        {
            if (!active) return;
            active = false;
            if (docked && panel != null)
            {
                panel.style.left = StyleKeyword.Null;
                panel.style.top = StyleKeyword.Null;
                panel.style.translate = StyleKeyword.Null;
            }
            docked = false;
            Frame(0f);
        }

        /// <summary>Call every frame while the window is open: windows open, close and move, and the camera zooms.</summary>
        public void Tick()
        {
            if (!active || panel == null || panel.panel == null) return;
            VisualElement parent = panel.parent;
            Rect bounds = panel.worldBound;
            if (parent == null || float.IsNaN(bounds.width) || bounds.width < 1f) return; // not laid out yet
            Rect screen = panel.panel.visualTree.worldBound;
            bool besideShown = beside != null && beside.resolvedStyle.display == DisplayStyle.Flex && beside.worldBound.width > 1f;

            if (!docked && !(HudLayoutEditor.Current != null && HudLayoutEditor.Current.HasSavedPosition(panel.name)))
            {
                float left, top;
                if (besideShown)
                {
                    Rect other = beside.worldBound;
                    left = ServicePlacementRules.DockBeside(other.xMin, other.xMax, bounds.width, screen.width, Gap, Margin);
                    top = other.yMin;
                }
                else
                {
                    // Against the right edge, short of the minimap when that is where the minimap is.
                    float right = screen.width - Margin;
                    VisualElement minimap = panel.panel.visualTree.Q(MinimapName);
                    if (minimap != null && minimap.resolvedStyle.display == DisplayStyle.Flex)
                    {
                        Rect map = minimap.worldBound;
                        bool sameRows = map.yMax > bounds.yMin && map.yMin < bounds.yMin + bounds.height;
                        if (sameRows && map.xMin > screen.width * 0.5f) right = Mathf.Min(right, map.xMin - Gap);
                    }
                    left = Mathf.Max(Margin, right - bounds.width);
                    top = bounds.yMin;
                }
                top = Mathf.Clamp(top, Margin, Mathf.Max(Margin, screen.height - bounds.height - Margin));
                Vector2 local = parent.WorldToLocal(new Vector2(left, top));
                panel.style.left = local.x;
                panel.style.top = local.y;
                panel.style.translate = new Translate(0f, 0f);
                docked = true;
                return; // measured again next frame, where it now is
            }

            float coverLeft = bounds.xMin, coverRight = bounds.xMax;
            if (besideShown)
            {
                coverLeft = Mathf.Min(coverLeft, beside.worldBound.xMin);
                coverRight = Mathf.Max(coverRight, beside.worldBound.xMax);
            }
            Frame(ServicePlacementRules.Framing(coverLeft, coverRight, screen.width, CharacterHalfWidth(), FramingLimit));
        }

        /// <summary>Half the width the local character takes on screen right now, in panel units.</summary>
        private float CharacterHalfWidth()
        {
            PlayerInventory local = PlayerInventory.Local;
            if (viewer == null) viewer = Camera.main;
            if (local == null || viewer == null || panel.panel == null) return 0f;
            Vector3 root = local.transform.position;
            Vector3 feet = viewer.WorldToScreenPoint(root + Vector3.down * FeetBelow);
            Vector3 head = viewer.WorldToScreenPoint(root + Vector3.up * HeadAbove);
            if (feet.z <= 0f || head.z <= 0f) return 0f;
            Vector2 a = RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(feet.x, Screen.height - feet.y));
            Vector2 b = RuntimePanelUtils.ScreenToPanel(panel.panel, new Vector2(head.x, Screen.height - head.y));
            return Mathf.Abs(a.y - b.y) * WidthOfHeight * 0.5f;
        }

        private static void Frame(float across)
        {
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && local.TryGetComponent(out PointAndClickPlayer player)) player.SetScreenFraming(across);
        }
    }
}
