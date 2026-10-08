using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The minimap's dial in the faction's Synty art. The map itself, its markers, the north mark and the zoom
    /// buttons stay the HUD's own (they are drawn over this); the dial is the frame round them, and it stands on
    /// the minimap's element by its glass, so the map fills the glass and the frame reaches out round it.
    ///
    /// An ornament of the dial that lies on the glass would be under the map. Those are listed in
    /// <see cref="overMap"/>: each is shown as an element of the HUD instead, in the same place, over the map and
    /// under the north mark and the buttons.
    /// </summary>
    public sealed class MinimapChromeView : HudChromePiece
    {
        [Tooltip("Parts of the dial that lie on the glass: drawn over the map, not under it.")]
        [SerializeField] private Image[] overMap = System.Array.Empty<Image>();

        private const string PanelName = "minimap";
        private const string MaskName = "minimap-mask";

        private readonly List<VisualElement> overlays = new List<VisualElement>();

        protected override VisualElement OnAttach(VisualElement hudRoot)
        {
            VisualElement panel = hudRoot.Q(PanelName);
            if (panel == null) return null;

            Canvas.ForceUpdateCanvases();
            var corners = new Vector3[4];
            VisualElement mask = panel.Q(MaskName);
            int at = mask != null ? panel.IndexOf(mask) + 1 : panel.childCount;
            Rect glass = Stand;
            foreach (Image part in overMap)
            {
                if (part == null || part.sprite == null) continue;
                part.rectTransform.GetWorldCorners(corners);
                Vector2 min = transform.InverseTransformPoint(corners[0]), max = transform.InverseTransformPoint(corners[2]);
                var over = new VisualElement { pickingMode = PickingMode.Ignore };
                over.style.position = Position.Absolute;
                over.style.left = Length.Percent(100f * (min.x - glass.xMin) / glass.width);
                over.style.top = Length.Percent(100f * (glass.yMax - max.y) / glass.height);
                over.style.width = Length.Percent(100f * (max.x - min.x) / glass.width);
                over.style.height = Length.Percent(100f * (max.y - min.y) / glass.height);
                over.style.backgroundImage = new StyleBackground(part.sprite);
                over.style.unityBackgroundImageTintColor = part.color;
                panel.Insert(at++, over);
                overlays.Add(over);
                part.enabled = false;
            }
            return panel;
        }

        protected override void OnDetach()
        {
            foreach (VisualElement over in overlays) over.RemoveFromHierarchy();
            overlays.Clear();
            foreach (Image part in overMap)
                if (part != null) part.enabled = true;
        }

        protected override void Mirror() { }
    }
}
