using KryticalNova.UI.Menus;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Says what the player does in a UI Toolkit window (<see cref="MenuMoments"/>): the pointer coming onto a
    /// button, a switch or a slider, a press, a switch flipped, a slider moved. One watch on the window covers every
    /// control in it, the ones made later too. Used by Settings, in the menus and in the game.
    /// </summary>
    public static class WindowMoments
    {
        /// <summary>A slider says it moved at most this often while it is dragged.</summary>
        private const float SlideGap = 0.07f;
        private const string SliderHandleClass = "unity-base-slider__dragger";

        public static void Watch(VisualElement window)
        {
            if (window == null) return;
            VisualElement hovered = null;
            float lastSlide = -1f;

            window.RegisterCallback<PointerOverEvent>(evt =>
            {
                VisualElement on = ControlOf(evt.target as VisualElement, window);
                if (on == hovered) return; // still on the same control, only over another part of it
                hovered = on;
                if (on != null && on.enabledInHierarchy) MenuMoments.Raise(MenuMoment.Hover, on);
            }, TrickleDown.TrickleDown);
            window.RegisterCallback<PointerLeaveEvent>(_ => hovered = null);

            window.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                VisualElement on = ControlOf(evt.target as VisualElement, window);
                if (on != null && on.enabledInHierarchy) MenuMoments.Raise(MenuMoment.Press, on);
            }, TrickleDown.TrickleDown);

            window.RegisterCallback<ChangeEvent<bool>>(evt =>
            {
                if (evt.target is Toggle toggle) MenuMoments.Raise(MenuMoment.Toggle, toggle);
            });
            window.RegisterCallback<ChangeEvent<float>>(evt =>
            {
                if (!(evt.target is Slider slider)) return;
                float now = Time.unscaledTime;
                if (now - lastSlide < SlideGap) return;
                lastSlide = now;
                MenuMoments.Raise(MenuMoment.Slide, slider.Q(className: SliderHandleClass) ?? slider);
            });
        }

        /// <summary>The control a pointer event belongs to: the button, switch or slider it landed in, if any.</summary>
        private static VisualElement ControlOf(VisualElement from, VisualElement window)
        {
            for (VisualElement each = from; each != null && each != window; each = each.parent)
                if (each is Button || each is Toggle || each is Slider) return each;
            return null;
        }
    }
}
