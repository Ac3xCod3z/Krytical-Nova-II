using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// A comic speech bubble drawn in the HUD: paper, an ink outline, a hard shadow, Ben-Day dots in its shaded
    /// corner and a tail on the side its speaker is. Its children (a line of text, an emoji) sit inside the paper.
    /// Every colour and size is a style property, set in ProgressionHUD.uss (.comic-bubble).
    /// </summary>
    public sealed class ComicBubble : VisualElement
    {
        public const string UssClass = "comic-bubble";

        private static readonly CustomStyleProperty<Color> InkProperty = new CustomStyleProperty<Color>("--bubble-ink");
        private static readonly CustomStyleProperty<Color> PaperProperty = new CustomStyleProperty<Color>("--bubble-paper");
        private static readonly CustomStyleProperty<Color> DotProperty = new CustomStyleProperty<Color>("--bubble-dot");
        private static readonly CustomStyleProperty<Color> ShadowProperty = new CustomStyleProperty<Color>("--bubble-shadow");
        private static readonly CustomStyleProperty<float> LineProperty = new CustomStyleProperty<float>("--bubble-line");
        private static readonly CustomStyleProperty<float> RadiusProperty = new CustomStyleProperty<float>("--bubble-radius");
        private static readonly CustomStyleProperty<float> TailProperty = new CustomStyleProperty<float>("--bubble-tail");
        private static readonly CustomStyleProperty<float> ShadowOffsetProperty = new CustomStyleProperty<float>("--bubble-shadow-offset");
        private static readonly CustomStyleProperty<float> DotCellProperty = new CustomStyleProperty<float>("--bubble-dot-cell");
        private static readonly CustomStyleProperty<float> DotReachProperty = new CustomStyleProperty<float>("--bubble-dot-reach");

        // What the bubble is drawn with until the style sheet has spoken: the Colour Bible's ink, paper and shadow indigo.
        private Color ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        private Color paper = new Color32(0xF4, 0xEE, 0xDF, 0xFF);
        private Color dot = new Color32(0x4C, 0x52, 0xBF, 0x99);
        private Color shadow = new Color32(0x14, 0x12, 0x2B, 0x8C);
        private float line = 3f, radius = 14f, tail = 14f, shadowOffset = 4f, dotCell = 7f, dotReach = 0.4f;
        private bool tailOnRight = true;

        /// <summary>The side the tail is on: the side the speaker is.</summary>
        public bool TailOnRight
        {
            get => tailOnRight;
            set
            {
                if (tailOnRight == value) return;
                tailOnRight = value;
                EnableInClassList(UssClass + "--tail-left", !value);
                MarkDirtyRepaint();
            }
        }

        public ComicBubble()
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnStyle);
        }

        private void OnStyle(CustomStyleResolvedEvent e)
        {
            if (e.customStyle.TryGetValue(InkProperty, out Color c)) ink = c;
            if (e.customStyle.TryGetValue(PaperProperty, out c)) paper = c;
            if (e.customStyle.TryGetValue(DotProperty, out c)) dot = c;
            if (e.customStyle.TryGetValue(ShadowProperty, out c)) shadow = c;
            if (e.customStyle.TryGetValue(LineProperty, out float f)) line = f;
            if (e.customStyle.TryGetValue(RadiusProperty, out f)) radius = f;
            if (e.customStyle.TryGetValue(TailProperty, out f)) tail = f;
            if (e.customStyle.TryGetValue(ShadowOffsetProperty, out f)) shadowOffset = f;
            if (e.customStyle.TryGetValue(DotCellProperty, out f)) dotCell = Mathf.Max(2f, f);
            if (e.customStyle.TryGetValue(DotReachProperty, out f)) dotReach = Mathf.Clamp01(f);
            MarkDirtyRepaint();
        }

        /// <summary>
        /// The paper's rectangle inside the element: the tail and the shadow take the rest. The whole element is the
        /// bubble; its padding (ProgressionHUD.uss) is the room the outline, the tail and the shadow need, so that
        /// what is inside sits on the paper.
        /// </summary>
        private Rect Body()
        {
            var r = new Rect(0f, 0f, layout.width, layout.height);
            float left = tailOnRight ? line : tail + line;
            float right = tailOnRight ? tail + line : line;
            return new Rect(r.x + left, r.y + line, Mathf.Max(1f, r.width - left - right - shadowOffset), Mathf.Max(1f, r.height - line * 2f - shadowOffset));
        }

        private void Draw(MeshGenerationContext context)
        {
            if (float.IsNaN(layout.width) || float.IsNaN(layout.height)) return; // not laid out yet
            Rect body = Body();
            if (body.width < 4f || body.height < 4f) return;
            Painter2D painter = context.painter2D;
            float r = Mathf.Min(radius, Mathf.Min(body.width, body.height) * 0.5f);

            // Hard shadow: the same shape again, pushed down and to the right.
            painter.fillColor = shadow;
            Outline(painter, new Rect(body.x + shadowOffset, body.y + shadowOffset, body.width, body.height), r);
            painter.Fill();

            painter.fillColor = paper;
            painter.strokeColor = ink;
            painter.lineWidth = line;
            painter.lineJoin = LineJoin.Round;
            Outline(painter, body, r);
            painter.Fill();

            // Ben-Day dots, on the 45 degree grid of the world's dots, growing toward the shaded corner (bottom right).
            painter.fillColor = dot;
            float inset = line * 0.5f + 1f;
            for (float y = body.yMin; y < body.yMax; y += dotCell * 0.5f)
            {
                bool odd = Mathf.RoundToInt((y - body.yMin) / (dotCell * 0.5f)) % 2 == 1;
                for (float x = body.xMin + (odd ? dotCell * 0.5f : 0f); x < body.xMax; x += dotCell)
                {
                    float shade = ((x - body.xMin) / body.width + (y - body.yMin) / body.height) * 0.5f; // 0 top left .. 1 bottom right
                    if (shade < 1f - dotReach) continue;
                    float size = dotCell * 0.28f * Mathf.Clamp01((shade - (1f - dotReach)) / Mathf.Max(0.01f, dotReach) + 0.3f);
                    if (!Inside(body, r, new Vector2(x, y), size + inset)) continue;
                    painter.BeginPath();
                    painter.Arc(new Vector2(x, y), size, 0f, 360f);
                    painter.Fill();
                }
            }

            Outline(painter, body, r);
            painter.Stroke();
        }

        /// <summary>The bubble's outline as one closed path: a rounded box with the tail on one side.</summary>
        private void Outline(Painter2D painter, Rect b, float r)
        {
            float tailY = b.y + b.height * 0.62f, half = Mathf.Min(7f, b.height * 0.2f);
            painter.BeginPath();
            painter.MoveTo(new Vector2(b.xMin + r, b.yMin));
            painter.LineTo(new Vector2(b.xMax - r, b.yMin));
            painter.ArcTo(new Vector2(b.xMax, b.yMin), new Vector2(b.xMax, b.yMin + r), r);
            if (tailOnRight)
            {
                painter.LineTo(new Vector2(b.xMax, tailY - half));
                painter.LineTo(new Vector2(b.xMax + tail, tailY + half * 0.6f));
                painter.LineTo(new Vector2(b.xMax, tailY + half));
            }
            painter.LineTo(new Vector2(b.xMax, b.yMax - r));
            painter.ArcTo(new Vector2(b.xMax, b.yMax), new Vector2(b.xMax - r, b.yMax), r);
            painter.LineTo(new Vector2(b.xMin + r, b.yMax));
            painter.ArcTo(new Vector2(b.xMin, b.yMax), new Vector2(b.xMin, b.yMax - r), r);
            if (!tailOnRight)
            {
                painter.LineTo(new Vector2(b.xMin, tailY + half));
                painter.LineTo(new Vector2(b.xMin - tail, tailY + half * 0.6f));
                painter.LineTo(new Vector2(b.xMin, tailY - half));
            }
            painter.LineTo(new Vector2(b.xMin, b.yMin + r));
            painter.ArcTo(new Vector2(b.xMin, b.yMin), new Vector2(b.xMin + r, b.yMin), r);
            painter.ClosePath();
        }

        /// <summary>True when a dot of this size at this point lies wholly on the rounded paper.</summary>
        private static bool Inside(Rect b, float r, Vector2 p, float size)
        {
            if (p.x - size < b.xMin || p.x + size > b.xMax || p.y - size < b.yMin || p.y + size > b.yMax) return false;
            float cx = Mathf.Clamp(p.x, b.xMin + r, b.xMax - r), cy = Mathf.Clamp(p.y, b.yMin + r, b.yMax - r);
            float dx = p.x - cx, dy = p.y - cy;
            // Off the corners the edges checked above are all there is; in a corner the dot must fit the rounding.
            return dx == 0f || dy == 0f || (r > size && dx * dx + dy * dy <= (r - size) * (r - size));
        }
    }
}
