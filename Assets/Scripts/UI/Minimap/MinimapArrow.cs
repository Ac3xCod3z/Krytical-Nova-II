using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The player on the minimap: a chevron triangle in the HUD's teal with a dark ink outline, matching the comic
    /// outlines in the world. Drawn once as vector geometry; turning it is just a style rotate (no redraw).
    /// </summary>
    public sealed class MinimapArrow : VisualElement
    {
        private static readonly Color Fill = new Color32(114, 222, 196, 255);
        private static readonly Color Ink = new Color32(14, 16, 28, 255);
        private const float Size = 26f;

        public MinimapArrow()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.width = Size;
            style.height = Size;
            // Centred in the minimap; rotation pivots on its own centre.
            style.left = Length.Percent(50);
            style.top = Length.Percent(50);
            style.marginLeft = -Size * 0.5f;
            style.marginTop = -Size * 0.5f;
            generateVisualContent += Draw;
        }

        private static void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            float c = Size * 0.5f;
            // Points up (north / camera-forward) before rotation; the notch makes the facing read at a glance.
            var tip = new Vector2(c, c - 10f);
            var right = new Vector2(c + 7.5f, c + 8.5f);
            var notch = new Vector2(c, c + 4f);
            var left = new Vector2(c - 7.5f, c + 8.5f);

            p.lineJoin = LineJoin.Round;
            p.lineWidth = 2.6f;
            p.fillColor = Fill;
            p.strokeColor = Ink;
            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(right);
            p.LineTo(notch);
            p.LineTo(left);
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }
}
