using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The route of a map-ordered travel, drawn over a map layer: two strokes (dark ink under the HUD's teal)
    /// through points given in the layer's own pixels, ending in a dot on the destination. A child of the map layer, so it pans, zooms
    /// and turns with it.
    /// </summary>
    public sealed class MapRouteLine : VisualElement
    {
        private static readonly Color Line = new Color32(114, 222, 196, 235);
        private static readonly Color Ink = new Color32(14, 16, 28, 200);

        private readonly List<Vector2> points = new List<Vector2>(96);
        private readonly float width;

        public MapRouteLine(float lineWidth)
        {
            width = lineWidth;
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = 0f;
            style.top = 0f;
            style.right = 0f;
            style.bottom = 0f;
            generateVisualContent += Draw;
        }

        /// <summary>Starts a new set of points; follow with <see cref="Add"/> and <see cref="Apply"/>.</summary>
        public void Begin() => points.Clear();

        public void Add(Vector2 layerPixel) => points.Add(layerPixel);

        public void Apply()
        {
            style.display = points.Count >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
            MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext ctx)
        {
            if (points.Count < 2) return;
            var p = ctx.painter2D;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            Stroke(p, Ink, width + 2.5f);
            Stroke(p, Line, width);

            // The destination: a filled dot at the end of the line.
            Vector2 end = points[points.Count - 1];
            p.fillColor = Line;
            p.BeginPath();
            p.Arc(end, width * 1.6f, 0f, 360f);
            p.Fill();
        }

        private void Stroke(Painter2D p, Color color, float lineWidth)
        {
            p.strokeColor = color;
            p.lineWidth = lineWidth;
            p.BeginPath();
            p.MoveTo(points[0]);
            for (int i = 1; i < points.Count; i++) p.LineTo(points[i]);
            p.Stroke();
        }
    }
}
