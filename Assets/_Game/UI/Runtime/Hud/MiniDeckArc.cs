using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The Mini Deck's beat arc (build 17, day list 16): drawn over the device's rim with <see cref="Painter2D"/>, from
    /// <see cref="MiniDeckBeat"/>. Set its numbers each frame and call <see cref="VisualElement.MarkDirtyRepaint"/>.
    /// </summary>
    public sealed class MiniDeckArc : VisualElement
    {
        /// <summary>How far the ticks have come (0 at the ends, 1 at the notch).</summary>
        public float Travel;
        /// <summary>The notch's flash, 0 to 1.</summary>
        public float Flash;
        /// <summary>The whole arc lit by an ON CADENCE press, 0 to 1.</summary>
        public float Lit;
        public bool ShowTicks = true;
        public float CornerRadius = 18f;
        public float Inset = -3f;
        public Color Track = new Color(0.15f, 0.19f, 0.29f, 1f);
        public Color LitColour = new Color(0.25f, 0.85f, 1f, 1f);
        public Color NotchColour = new Color(1f, 0.95f, 0.7f, 1f);
        public Color TickColour = new Color(0.96f, 0.93f, 0.87f, 1f);

        private const int Steps = 48;
        private const float Width = 4f, TickRadius = 4f;

        public MiniDeckArc()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect r = contentRect;
            float w = r.width - 2f * Inset, h = r.height - 2f * Inset;
            if (w <= 1f || h <= 1f) return;
            Vector2 at = new Vector2(Inset, Inset);
            Painter2D p = context.painter2D;
            p.lineWidth = Width;
            p.lineCap = LineCap.Round;
            Stroke(p, at, w, h, 0f, 1f, Track);
            if (Lit > 0.01f)
            {
                Color c = LitColour;
                c.a *= Mathf.Clamp01(Lit);
                Stroke(p, at, w, h, 0f, 1f, c);
            }
            Vector2 mid = at + MiniDeckBeat.Point(0.5f, w, h, CornerRadius);
            Color n = NotchColour;
            n.a *= 0.45f + 0.55f * Mathf.Clamp01(Flash);
            p.fillColor = n;
            p.BeginPath();
            float nh = 6f + 3f * Flash, nw = 3f + Flash;
            p.MoveTo(mid + new Vector2(-nw, -nh));
            p.LineTo(mid + new Vector2(nw, -nh));
            p.LineTo(mid + new Vector2(nw, nh));
            p.LineTo(mid + new Vector2(-nw, nh));
            p.ClosePath();
            p.Fill();
            if (!ShowTicks) return;
            float t = 0.5f * Mathf.Clamp01(Travel);
            Dot(p, at + MiniDeckBeat.Point(t, w, h, CornerRadius));
            Dot(p, at + MiniDeckBeat.Point(1f - t, w, h, CornerRadius));
        }

        private void Stroke(Painter2D p, Vector2 at, float w, float h, float from, float to, Color colour)
        {
            p.strokeColor = colour;
            p.BeginPath();
            p.MoveTo(at + MiniDeckBeat.Point(from, w, h, CornerRadius));
            for (int i = 1; i <= Steps; i++) p.LineTo(at + MiniDeckBeat.Point(Mathf.Lerp(from, to, i / (float)Steps), w, h, CornerRadius));
            p.Stroke();
        }

        private void Dot(Painter2D p, Vector2 c)
        {
            p.fillColor = TickColour;
            p.BeginPath();
            p.Arc(c, TickRadius, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
