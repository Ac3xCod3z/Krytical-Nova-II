using System;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// A plain text tooltip for any HUD element: a title and a few lines, shown beside the element while the pointer
    /// is over it. (<see cref="VisualElement.tooltip"/> is only drawn by the Editor, never in a game panel.) The text
    /// is asked for each time the tooltip opens, so it is always current. One tooltip per HUD document, made on first use.
    /// </summary>
    public static class HudTextTooltip
    {
        private const string TipName = "text-tooltip";
        private const string RootClass = "kn-root";
        private const float Gap = 8f;

        public static void Attach(VisualElement target, Func<string> title, Func<string> body)
        {
            if (target == null) return;
            target.RegisterCallback<PointerEnterEvent>(_ => Show(target, title?.Invoke(), body?.Invoke()));
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide(target));
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide(target));
        }

        /// <summary>The element a tooltip is drawn in: the HUD's own root, so the HUD's styles and faction skin reach it.</summary>
        private static VisualElement HostOf(VisualElement target)
        {
            VisualElement top = target;
            for (VisualElement at = target; at != null; at = at.parent)
            {
                if (at.ClassListContains(RootClass)) return at;
                if (at.parent != null) top = at;
            }
            return top;
        }

        private static void Show(VisualElement target, string title, string body)
        {
            if (target.panel == null || (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))) return;
            VisualElement host = HostOf(target);
            VisualElement tip = host.Q(TipName);
            if (tip == null)
            {
                tip = new VisualElement { name = TipName, pickingMode = PickingMode.Ignore };
                tip.AddToClassList("panel");
                tip.AddToClassList("text-tooltip");
                var titleLabel = new Label { name = "text-tooltip-title", pickingMode = PickingMode.Ignore };
                titleLabel.AddToClassList("text-tooltip-title");
                var bodyLabel = new Label { name = "text-tooltip-body", pickingMode = PickingMode.Ignore };
                bodyLabel.AddToClassList("text-tooltip-body");
                tip.Add(titleLabel);
                tip.Add(bodyLabel);
                host.Add(tip);
            }
            Label heading = tip.Q<Label>("text-tooltip-title");
            heading.text = title ?? string.Empty;
            heading.style.display = string.IsNullOrEmpty(title) ? DisplayStyle.None : DisplayStyle.Flex;
            tip.Q<Label>("text-tooltip-body").text = body ?? string.Empty;

            tip.style.display = DisplayStyle.Flex;
            tip.BringToFront();
            // Beside the element, on whichever side has room, and never off the screen. One fitter per tooltip.
            if (!(tip.userData is FloatingFit fit)) tip.userData = fit = new FloatingFit(tip, Gap);
            fit.Beside(target.worldBound);
        }

        private static void Hide(VisualElement target)
        {
            VisualElement tip = HostOf(target).Q(TipName);
            if (tip != null) tip.style.display = DisplayStyle.None;
        }
    }
}
