using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace KryticalNova.Release
{
    /// <summary>
    /// The launcher's printed-comic dressing (owner, 19 Oct 2026, from the owner's reference): the night-indigo page with
    /// Ben-Day dots thickest in the corners and a few ink slashes, and the angular window itself, its frame cut at two
    /// corners with cyan fins round the top left and red fins round the bottom right. Every shape is fixed, so nothing moves
    /// when the window is drawn again; it is all drawn behind the live text, buttons and slideshow and never takes a click.
    /// </summary>
    internal static class ComicFrame
    {
        public static readonly Color Night = Color.FromArgb(0x14, 0x12, 0x2b);
        public static readonly Color PanelFill = Color.FromArgb(0x20, 0x27, 0x40);
        public static readonly Color Plate = Color.FromArgb(0x1b, 0x1d, 0x3a);
        public static readonly Color InnerEdge = Color.FromArgb(0x35, 0x3a, 0x78);
        public static readonly Color Paper = Color.FromArgb(0xf4, 0xee, 0xdf);
        public static readonly Color Cyan = Color.FromArgb(0x4f, 0xe3, 0xf0);
        public static readonly Color Red = Color.FromArgb(0xe3, 0x1b, 0x36);
        public static readonly Color Ink = Color.FromArgb(0x05, 0x06, 0x0d);

        public const float InkWidth = 4f;
        public const int ShadowOffset = 6;
        public const int Clip = 14;

        // ------------------------------------------------------------------ the page under everything

        /// <summary>The page, once: the indigo, the dot clusters, the ink slashes and the outer frame.</summary>
        public static Bitmap Page(Size size)
        {
            var page = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(page))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Night);
                // Dot clusters: strongest at the corners, each with its own spacing and dot size, fading to nothing.
                Dots(g, size, new PointF(Pad, Pad), 300, 9f, 4.2f, Color.FromArgb(150, 0x3d, 0x3c, 0x86));
                Dots(g, size, new PointF(size.Width - Pad, Pad), 210, 7f, 3.0f, Color.FromArgb(130, 0x3d, 0x3c, 0x86));
                Dots(g, size, new PointF(Pad, size.Height - Pad), 230, 8f, 3.4f, Color.FromArgb(140, 0x3d, 0x3c, 0x86));
                Dots(g, size, new PointF(size.Width - Pad, size.Height - Pad), 330, 10f, 4.8f, Color.FromArgb(150, 0x5c, 0x2c, 0x5e));
                Dots(g, size, new PointF(size.Width * 0.62f, size.Height), 120, 6f, 2.2f, Color.FromArgb(110, 0x3d, 0x3c, 0x86));
                Dots(g, size, new PointF(size.Width * 0.34f, 0), 110, 6f, 2.0f, Color.FromArgb(100, 0x3d, 0x3c, 0x86));

                // A few ink slashes near the corners; the middle stays quiet for the painting.
                using (var ink = new SolidBrush(Ink))
                {
                    g.FillPolygon(ink, Slash(new PointF(0, 150), new PointF(150, 0), 9));
                    g.FillPolygon(ink, Slash(new PointF(0, 196), new PointF(70, 126), 5));
                    g.FillPolygon(ink, Slash(new PointF(size.Width - 170, size.Height), new PointF(size.Width, size.Height - 170), 10));
                    g.FillPolygon(ink, Slash(new PointF(size.Width - 90, size.Height), new PointF(size.Width, size.Height - 90), 5));
                    g.FillPolygon(ink, Slash(new PointF(size.Width - 60, 0), new PointF(size.Width, 60), 6));
                    g.FillPolygon(ink, Slash(new PointF(0, size.Height - 70), new PointF(70, size.Height), 6));
                }

                OuterFrame(g, size);
            }
            return page;
        }

        // Dots on a staggered grid around one point, larger near it.
        private static void Dots(Graphics g, Size size, PointF centre, float reach, float spacing, float largest, Color colour)
        {
            using (var brush = new SolidBrush(colour))
            {
                float x0 = Math.Max(0, centre.X - reach), x1 = Math.Min(size.Width, centre.X + reach);
                float y0 = Math.Max(0, centre.Y - reach), y1 = Math.Min(size.Height, centre.Y + reach);
                int row = 0;
                for (float y = y0; y <= y1; y += spacing * 0.866f, row++)
                    for (float x = x0 + (row % 2) * spacing / 2; x <= x1; x += spacing)
                    {
                        float d = (float)Math.Sqrt((x - centre.X) * (x - centre.X) + (y - centre.Y) * (y - centre.Y));
                        if (d >= reach) continue;
                        float r = largest * (float)Math.Pow(1 - d / reach, 1.4);
                        if (r < 0.45f) continue;
                        g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
                    }
            }
        }

        // A thin slanted stroke with sharp ends.
        private static PointF[] Slash(PointF a, PointF b, float width)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
            float nx = -dy / length * width / 2, ny = dx / length * width / 2;
            return new[] { new PointF(a.X - nx * 0.2f, a.Y - ny * 0.2f), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx * 0.2f, b.Y - ny * 0.2f), new PointF(a.X - nx, a.Y - ny) };
        }

        /// <summary>How much of the frame's top-left and bottom-right corners is cut away.</summary>
        public const int WindowCut = 34;

        /// <summary>The room round the frame where its coloured fins reach out (the window's shape includes them).</summary>
        public const int Pad = 22;

        /// <summary>The frame itself: the page less the fins' room.</summary>
        public static Rectangle FrameOf(Size size) => new Rectangle(Pad, Pad, size.Width - Pad * 2, size.Height - Pad * 2);

        /// <summary>
        /// The window's own outline (owner, 19 Oct 2026: angular like the first reference): the frame with two corners cut,
        /// and the coloured fins that reach out from it, cyan round the top left and red round the bottom right.
        /// </summary>
        public static GraphicsPath Silhouette(Size size)
        {
            var path = new GraphicsPath(FillMode.Winding);
            path.AddPolygon(ClippedRect(FrameOf(size), WindowCut, 0, WindowCut, 0));
            foreach (var (shape, _) in Fins(size)) path.AddPolygon(shape);
            return path;
        }

        /// <summary>The coloured fins round the frame, fixed.</summary>
        public static List<(PointF[] Shape, Color Colour)> Fins(Size size)
        {
            Rectangle f = FrameOf(size);
            float l = f.Left, t = f.Top, r = f.Right, b = f.Bottom;
            int c = WindowCut;
            return new List<(PointF[], Color)>
            {
                // Cyan hugging the top left: along the top, round the cut corner and down the left side, its ends cut.
                (new[] { new PointF(l - 9, t + 310), new PointF(l - 9, t + c - 4), new PointF(l + c - 4, t - 9), new PointF(l + 430, t - 9),
                         new PointF(l + 420, t + 2), new PointF(l + c, t + 2), new PointF(l + 2, t + c), new PointF(l + 2, t + 300) }, Cyan),
                // A thin streak rising off the top edge, and a short piece lower on the left side.
                (new[] { new PointF(l + 196, t - 8), new PointF(l + 226, t - 21), new PointF(l + 240, t - 21), new PointF(l + 210, t - 8) }, Cyan),
                (new[] { new PointF(l - 9, f.Top + f.Height * 0.66f + 8), new PointF(l + 2, f.Top + f.Height * 0.66f), new PointF(l + 2, f.Top + f.Height * 0.66f + 52),
                         new PointF(l - 9, f.Top + f.Height * 0.66f + 60) }, Cyan),
                // Red hugging the bottom right: down the right side, round the cut corner and along the bottom.
                (new[] { new PointF(r - 2, t + 230), new PointF(r + 9, t + 220), new PointF(r + 9, b - c + 4), new PointF(r - c + 4, b + 9),
                         new PointF(r - 440, b + 9), new PointF(r - 430, b - 2), new PointF(r - c, b - 2), new PointF(r - 2, b - c) }, Red),
                // Slashes flying off the bottom-right corner.
                (new[] { new PointF(r - 130, b + 8), new PointF(r - 112, b + 8), new PointF(r - 128, b + 20), new PointF(r - 146, b + 20) }, Red),
                (new[] { new PointF(r - 96, b + 8), new PointF(r - 86, b + 8), new PointF(r - 98, b + 17), new PointF(r - 108, b + 17) }, Red),
                (new[] { new PointF(r + 8, b - 120), new PointF(r + 19, b - 134), new PointF(r + 19, b - 112), new PointF(r + 8, b - 98) }, Red),
            };
        }

        // The angular plate round the whole page, along the window's edge, with its cyan and red runs.
        private static void OuterFrame(Graphics g, Size size)
        {
            foreach (var (shape, colour) in Fins(size))
            {
                using (var fill = new SolidBrush(colour)) g.FillPolygon(fill, shape);
                using (var pen = new Pen(Ink, 3f) { LineJoin = LineJoin.Miter }) g.DrawPolygon(pen, shape);
            }
            Rectangle f = FrameOf(size);
            var outer = new Rectangle(f.Left + 1, f.Top + 1, f.Width - 3, f.Height - 3);
            var inner = new Rectangle(f.Left + 10, f.Top + 10, f.Width - 21, f.Height - 21);
            using (var ring = new GraphicsPath(FillMode.Alternate))
            {
                ring.AddPolygon(ClippedRect(outer, WindowCut, 0, WindowCut, 0));
                ring.AddPolygon(ClippedRect(inner, WindowCut - 8, 0, WindowCut - 8, 0));
                using (var plate = new SolidBrush(Plate)) g.FillPath(plate, ring);
            }
            using (var pen = new Pen(Ink, 4f) { LineJoin = LineJoin.Miter })
                g.DrawPolygon(pen, ClippedRect(outer, WindowCut, 0, WindowCut, 0));
            using (var pen = new Pen(Ink, 3f) { LineJoin = LineJoin.Miter })
                g.DrawPolygon(pen, ClippedRect(inner, WindowCut - 8, 0, WindowCut - 8, 0));

            // Runs of colour along the plate, with gaps and clipped ends: cyan along the top and left, red along the right
            // and the bottom.
            float fl = f.Left, ft = f.Top, fr = f.Right, fb = f.Bottom;
            using (var cyan = new SolidBrush(Cyan))
            {
                g.FillPolygon(cyan, Run(new PointF(fl + 456, ft + 6), new PointF(fl + 520, ft + 6), 3));
                g.FillPolygon(cyan, Run(new PointF(fl + 6, ft + 326), new PointF(fl + 6, ft + 372), 3));
            }
            using (var red = new SolidBrush(Red))
            {
                g.FillPolygon(red, Run(new PointF(fr - 6, ft + 160), new PointF(fr - 6, ft + 204), 3));
                g.FillPolygon(red, Run(new PointF(fr - 466, fb - 6), new PointF(fr - 530, fb - 6), 3));
            }
        }

        // A bar along a line, its ends cut at a slant.
        private static PointF[] Run(PointF a, PointF b, float half)
        {
            bool across = Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y);
            float cut = half * 1.6f * Math.Sign(across ? b.X - a.X : b.Y - a.Y);
            return across
                ? new[] { new PointF(a.X, a.Y - half), new PointF(b.X - cut, b.Y - half), new PointF(b.X, b.Y + half), new PointF(a.X + cut, a.Y + half) }
                : new[] { new PointF(a.X - half, a.Y + cut), new PointF(b.X - half, b.Y), new PointF(b.X + half, b.Y - cut), new PointF(a.X + half, a.Y) };
        }

        // ------------------------------------------------------------------ shapes

        /// <summary>A rectangle with its corners cut (top left, top right, bottom right, bottom left), clockwise.</summary>
        public static Point[] ClippedRect(Rectangle r, int topLeft, int topRight, int bottomRight, int bottomLeft)
        {
            var points = new List<Point>();
            void Corner(int x, int y, int cut, int dx1, int dy1, int dx2, int dy2)
            {
                if (cut <= 0) { points.Add(new Point(x, y)); return; }
                points.Add(new Point(x + dx1 * cut, y + dy1 * cut));
                points.Add(new Point(x + dx2 * cut, y + dy2 * cut));
            }
            Corner(r.Left, r.Top, topLeft, 0, 1, 1, 0);
            Corner(r.Right, r.Top, topRight, -1, 0, 0, 1);
            Corner(r.Right, r.Bottom, bottomRight, 0, -1, -1, 0);
            Corner(r.Left, r.Bottom, bottomLeft, 1, 0, 0, -1);
            return points.ToArray();
        }


        public static PointF[] ToF(Point[] points) => Array.ConvertAll(points, p => new PointF(p.X, p.Y));

        public static PointF[] Offset(PointF[] points, float dx, float dy) => Array.ConvertAll(points, p => new PointF(p.X + dx, p.Y + dy));




        /// <summary>Faint dots in one corner of a panel, clipped to it, fading out within <paramref name="reach"/>.</summary>
        public static void CornerDots(Graphics g, GraphicsPath clip, PointF corner, float reach, float spacing, float largest, Color colour)
        {
            GraphicsState state = g.Save();
            g.SetClip(clip, CombineMode.Intersect);
            var bounds = Rectangle.Round(clip.GetBounds());
            using (var brush = new SolidBrush(colour))
            {
                int row = 0;
                for (float y = corner.Y - reach; y <= corner.Y + reach; y += spacing * 0.866f, row++)
                    for (float x = corner.X - reach + (row % 2) * spacing / 2; x <= corner.X + reach; x += spacing)
                    {
                        if (!bounds.Contains((int)x, (int)y)) continue;
                        float d = (float)Math.Sqrt((x - corner.X) * (x - corner.X) + (y - corner.Y) * (y - corner.Y));
                        if (d >= reach) continue;
                        float r = largest * (float)Math.Pow(1 - d / reach, 1.6);
                        if (r < 0.4f) continue;
                        g.FillEllipse(brush, x - r, y - r, r * 2, r * 2);
                    }
            }
            g.Restore(state);
        }
    }
}
