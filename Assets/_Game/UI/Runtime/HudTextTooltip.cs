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
            UnityEngine.Rect at = host.WorldToLocal(target.worldBound);
            tip.style.left = at.xMax + Gap;
            tip.style.top = at.yMin;
            tip.schedule.Execute(() =>
            {
                // Flip to the left of the element, and lift, when it would leave the screen.
                float width = tip.layout.width, height = tip.layout.height;
                if (at.xMax + Gap + width > host.layout.width) tip.style.left = UnityEngine.Mathf.Max(0f, at.xMin - Gap - width);
                if (at.yMin + height > host.layout.height) tip.style.top = UnityEngine.Mathf.Max(0f, host.layout.height - height);
            });
        }

        private static void Hide(VisualElement target)
        {
            VisualElement tip = HostOf(target).Q(TipName);
            if (tip != null) tip.style.display = DisplayStyle.None;
        }
    }
}
