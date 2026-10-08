using System;

namespace KryticalNova.EditorTools
{
    /// <summary>What goes into the comic treatment.</summary>
    public enum ComicIconShape
    {
        /// <summary>A full square painting (the skill icons): it becomes a rounded tile.</summary>
        Square = 0,
        /// <summary>An object on a clear ground (the item icons): it keeps its own outline.</summary>
        CutOut = 1,
    }

    /// <summary>
    /// Every number of the comic icon treatment. Sizes are in pixels of the finished icon, colours are 0xRRGGBB.
    /// Filled from the <c>IconTreatmentSettings</c> asset: plain values, so the treatment itself needs nothing of Unity
    /// and can be run and tested outside the Editor.
    /// </summary>
    public struct ComicIconLook
    {
        public int size;
        public int supersample;

        // The sticker: the same ink outline, paper keyline and hard shadow as the icons ComicIconBaker makes.
        public float margin;
        public float cornerRadius;
        public float outlineWidth;
        public float keylineWidth;
        public float shadowOffset;
        public float shadowAlpha;

        // Flat colour: the painting's soft shading is gathered into a few flat tones.
        public int flattenRadius;
        public int flattenPasses;
        public float flattenTolerance;
        public int tones;
        public float toneAdapt;
        public float toneLow;
        public float toneHigh;
        public float toneContrast;
        public int toneSettle;

        // Ink lines inside the picture.
        public float inkEdge;
        public float inkWidth;
        public int inkSpeck;
        public int inkSpareTones;

        // Ben-Day dots: the second darkest tone is drawn as dots of the darkest over the next lighter one.
        public float dotCell;
        public float dotRadius;
        public float dotContrast;

        // Colour: pulled toward the Colour Bible.
        public float saturation;
        public float palettePull;
        public uint[] palette;
        public float shadeTint;
        public uint shadeColor;
        public uint ink;
        public uint paper;

        // A set's own colour moved to another (a class of one faction drawn from a set painted in another's colour).
        public float hueFrom;
        public float hueTo;
        public float hueBand;
    }

    /// <summary>
    /// Redraws a painted icon in the game's comic style without touching the painting it came from: flat tones in
    /// place of soft shading, ink lines where the shapes meet, Ben-Day dots in the shade, colours pulled toward the
    /// Colour Bible, and the ink outline, paper keyline and hard shadow every icon in the game wears. Pixels go in
    /// and come out as RGBA bytes, rows from the top down.
    /// </summary>
    public static class ComicIconTreatment
    {
        private const float SolidAlpha = 0.5f;

        public static byte[] Treat(byte[] source, int width, int height, ComicIconShape shape, in ComicIconLook look)
        {
            if (source == null || width <= 0 || height <= 0 || source.Length < width * height * 4)
                throw new ArgumentException("The source picture is empty or shorter than its size says.");
            int ss = Math.Max(1, look.supersample);
            int work = Math.Max(16, look.size) * ss;
            int count = work * work;

            var r = new float[count];
            var g = new float[count];
            var b = new float[count];
            var solid = new bool[count];
            if (shape == ComicIconShape.Square) FitSquare(source, width, height, work, look.margin * ss, look.cornerRadius * ss, r, g, b, solid);
            else FitCutOut(source, width, height, work, look.margin * ss, r, g, b, solid);

            for (int pass = 0; pass < look.flattenPasses; pass++) Flatten(r, g, b, solid, work, Math.Max(1, look.flattenRadius * ss), look.flattenTolerance);
            bool[] inked = InkLines(r, g, b, solid, work, look.inkEdge, look.inkWidth * ss, look.inkSpeck * ss * ss);
            byte[] band = Tones(r, g, b, solid, count, look, out float[] bandValue);
            for (int pass = 0; pass < look.toneSettle; pass++) Settle(band, solid, work, ss);
            // Ink goes on the dark side of an edge: the lightest tones are never inked over, so a thin bright
            // stroke (a bolt of lightning, the edge of a blade) keeps its light.
            int lightest = bandValue.Length - Math.Max(0, look.inkSpareTones);
            for (int i = 0; i < count; i++)
                if (inked[i] && band[i] >= lightest) inked[i] = false;
            Colour(r, g, b, solid, count, look);

            float[] outside = Distance(solid, work);
            float[] big = Paint(r, g, b, solid, inked, band, bandValue, outside, work, ss, look);
            return Downsample(big, work, ss);
        }

        // ---------------------------------------------------------------- fitting the picture into the icon

        private static void FitSquare(byte[] src, int w, int h, int work, float margin, float corner, float[] r, float[] g, float[] b, bool[] solid)
        {
            float box = work - 2f * margin;
            float half = box * 0.5f, centre = work * 0.5f;
            corner = Math.Min(corner, half);
            for (int y = 0; y < work; y++)
            {
                for (int x = 0; x < work; x++)
                {
                    float px = x + 0.5f - centre, py = y + 0.5f - centre;
                    float dx = Math.Max(Math.Abs(px) - (half - corner), 0f), dy = Math.Max(Math.Abs(py) - (half - corner), 0f);
                    if (Math.Abs(px) > half || Math.Abs(py) > half || dx * dx + dy * dy > corner * corner) continue;
                    float u = (px + half) / box * w - 0.5f, v = (py + half) / box * h - 0.5f;
                    Sample(src, w, h, u, v, out float sr, out float sg, out float sb, out float sa);
                    if (sa < SolidAlpha) continue;
                    int i = y * work + x;
                    r[i] = sr; g[i] = sg; b[i] = sb;
                    solid[i] = true;
                }
            }
        }

        private static void FitCutOut(byte[] src, int w, int h, int work, float margin, float[] r, float[] g, float[] b, bool[] solid)
        {
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (src[(y * w + x) * 4 + 3] < 128) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < minX) return; // nothing drawn

            float box = work - 2f * margin;
            float scale = Math.Max(maxX - minX + 1, maxY - minY + 1) / box; // source pixels per working pixel
            float cx = (minX + maxX + 1) * 0.5f, cy = (minY + maxY + 1) * 0.5f;
            for (int y = 0; y < work; y++)
            {
                for (int x = 0; x < work; x++)
                {
                    float u = (x + 0.5f - work * 0.5f) * scale + cx - 0.5f;
                    float v = (y + 0.5f - work * 0.5f) * scale + cy - 0.5f;
                    Sample(src, w, h, u, v, out float sr, out float sg, out float sb, out float sa);
                    if (sa < SolidAlpha) continue;
                    int i = y * work + x;
                    r[i] = sr; g[i] = sg; b[i] = sb;
                    solid[i] = true;
                }
            }
        }

        /// <summary>Bilinear, weighted by alpha so a clear neighbour does not darken the edge.</summary>
        private static void Sample(byte[] src, int w, int h, float u, float v, out float r, out float g, out float b, out float a)
        {
            r = g = b = a = 0f;
            if (u < -1f || v < -1f || u > w || v > h) return;
            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
            float tx = u - x0, ty = v - y0;
            for (int j = 0; j < 2; j++)
            {
                int y = y0 + j;
                if (y < 0 || y >= h) continue;
                float wy = j == 0 ? 1f - ty : ty;
                for (int i = 0; i < 2; i++)
                {
                    int x = x0 + i;
                    if (x < 0 || x >= w) continue;
                    float wx = i == 0 ? 1f - tx : tx;
                    int at = (y * w + x) * 4;
                    float alpha = src[at + 3] / 255f * wx * wy;
                    r += src[at] / 255f * alpha;
                    g += src[at + 1] / 255f * alpha;
                    b += src[at + 2] / 255f * alpha;
                    a += alpha;
                }
            }
            if (a > 0.0001f) { r /= a; g /= a; b /= a; }
        }

        // ---------------------------------------------------------------- flat colour

        /// <summary>
        /// An edge-keeping blur: every pixel takes the mean of the pixels round it that are close to it in colour.
        /// Soft paint and noise gather into even patches, and the edges between differently coloured shapes stay
        /// sharp. <paramref name="tolerance"/> is how far apart two colours may be and still be blended.
        /// </summary>
        private static void Flatten(float[] r, float[] g, float[] b, bool[] solid, int work, int radius, float tolerance)
        {
            int side = radius * 2 + 1;
            var near = new float[side * side];
            float spread = Math.Max(1f, radius * 0.5f);
            for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                    near[(dy + radius) * side + dx + radius] = (float)Math.Exp(-(dx * dx + dy * dy) / (2f * spread * spread));

            // How much a neighbour counts by how different its colour is (squared distance, 0 to 3, in 1024 steps).
            const int Steps = 1024;
            var alike = new float[Steps + 1];
            float t2 = 2f * Math.Max(0.01f, tolerance) * Math.Max(0.01f, tolerance);
            for (int k = 0; k <= Steps; k++) alike[k] = (float)Math.Exp(-(3f * k / Steps) / t2);

            var outR = new float[r.Length];
            var outG = new float[r.Length];
            var outB = new float[r.Length];
            for (int y = 0; y < work; y++)
            {
                int y0 = Math.Max(0, y - radius), y1 = Math.Min(work - 1, y + radius);
                for (int x = 0; x < work; x++)
                {
                    int i = y * work + x;
                    if (!solid[i]) continue;
                    float cr = r[i], cg = g[i], cb = b[i];
                    float sr = 0f, sg = 0f, sb = 0f, sw = 0f;
                    int x0 = Math.Max(0, x - radius), x1 = Math.Min(work - 1, x + radius);
                    for (int ny = y0; ny <= y1; ny++)
                    {
                        int row = ny * work, weights = (ny - y + radius) * side - x + radius;
                        for (int nx = x0; nx <= x1; nx++)
                        {
                            int n = row + nx;
                            if (!solid[n]) continue;
                            float dr = r[n] - cr, dg = g[n] - cg, db = b[n] - cb;
                            float w = near[weights + nx] * alike[(int)((dr * dr + dg * dg + db * db) * (Steps / 3f))];
                            sr += r[n] * w; sg += g[n] * w; sb += b[n] * w; sw += w;
                        }
                    }
                    outR[i] = sr / sw; outG[i] = sg / sw; outB[i] = sb / sw;
                }
            }
            Array.Copy(outR, r, r.Length);
            Array.Copy(outG, g, g.Length);
            Array.Copy(outB, b, b.Length);
        }

        // ---------------------------------------------------------------- ink lines

        /// <summary>
        /// Where two flat patches meet with a clear step in colour, an ink line is drawn. A step is measured as a
        /// share of the whole range (0.15 = the two sides differ by 15 %); specks smaller than
        /// <paramref name="speck"/> pixels are dropped, and what is left is thickened to <paramref name="width"/>.
        /// </summary>
        private static bool[] InkLines(float[] r, float[] g, float[] b, bool[] solid, int work, float step, float width, int speck)
        {
            var edge = new bool[r.Length];
            if (step <= 0f || width <= 0f) return edge;
            float threshold = step * 4f; // a Sobel kernel answers a full step with 4
            for (int y = 1; y < work - 1; y++)
            {
                for (int x = 1; x < work - 1; x++)
                {
                    int i = y * work + x;
                    if (!solid[i] || !solid[i - 1] || !solid[i + 1] || !solid[i - work] || !solid[i + work]
                        || !solid[i - work - 1] || !solid[i - work + 1] || !solid[i + work - 1] || !solid[i + work + 1]) continue;
                    float m = Math.Max(Sobel(r, i, work), Math.Max(Sobel(g, i, work), Sobel(b, i, work)));
                    edge[i] = m > threshold;
                }
            }
            if (speck > 0) DropSpecks(edge, work, speck);

            // Thicken: everything within half the line's width of an edge.
            float reach = Math.Max(0f, (width - 2f) * 0.5f);
            if (reach <= 0f) return edge;
            float[] from = Distance(edge, work);
            var inked = new bool[r.Length];
            for (int i = 0; i < inked.Length; i++) inked[i] = solid[i] && from[i] <= reach;
            return inked;
        }

        private static float Sobel(float[] c, int i, int w)
        {
            float gx = c[i - w + 1] + 2f * c[i + 1] + c[i + w + 1] - c[i - w - 1] - 2f * c[i - 1] - c[i + w - 1];
            float gy = c[i + w - 1] + 2f * c[i + w] + c[i + w + 1] - c[i - w - 1] - 2f * c[i - w] - c[i - w + 1];
            return (float)Math.Sqrt(gx * gx + gy * gy);
        }

        private static void DropSpecks(bool[] edge, int work, int smallest)
        {
            var seen = new bool[edge.Length];
            var stack = new int[edge.Length];
            var members = new int[edge.Length];
            for (int start = 0; start < edge.Length; start++)
            {
                if (!edge[start] || seen[start]) continue;
                int top = 0, size = 0;
                stack[top++] = start;
                seen[start] = true;
                while (top > 0)
                {
                    int i = stack[--top];
                    members[size++] = i;
                    int x = i % work, y = i / work;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= work) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= work) continue;
                            int n = ny * work + nx;
                            if (!edge[n] || seen[n]) continue;
                            seen[n] = true;
                            stack[top++] = n;
                        }
                    }
                }
                if (size >= smallest) continue;
                for (int k = 0; k < size; k++) edge[members[k]] = false;
            }
        }

        // ---------------------------------------------------------------- tones

        /// <summary>
        /// Sorts every pixel into one of a few tones by how bright its colour is (its strongest channel, so a pure
        /// red counts as bright and stays red). Where the lines between tones fall follows the picture itself (a
        /// dark picture still gets all its tones) as far as <c>toneAdapt</c> says.
        /// </summary>
        private static byte[] Tones(float[] r, float[] g, float[] b, bool[] solid, int count, in ComicIconLook look, out float[] bandValue)
        {
            int tones = Math.Max(2, Math.Min(8, look.tones));
            var histogram = new int[256];
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                if (!solid[i]) continue;
                histogram[(int)Math.Min(255f, Value(r[i], g[i], b[i]) * 255f)]++;
                total++;
            }
            var limits = new float[tones - 1];
            int running = 0, next = 0;
            for (int bin = 0; bin < 256 && next < limits.Length; bin++)
            {
                running += histogram[bin];
                while (next < limits.Length && running >= total * (next + 1) / (float)tones)
                {
                    float even = (next + 1) / (float)tones;
                    limits[next] = even + (bin / 255f - even) * look.toneAdapt;
                    next++;
                }
            }
            for (; next < limits.Length; next++) limits[next] = (next + 1) / (float)tones;

            var band = new byte[count];
            var sum = new double[tones];
            var members = new int[tones];
            for (int i = 0; i < count; i++)
            {
                if (!solid[i]) continue;
                float value = Value(r[i], g[i], b[i]);
                int k = 0;
                while (k < limits.Length && value > limits[k]) k++;
                band[i] = (byte)k;
                sum[k] += value;
                members[k]++;
            }
            // Each tone is as bright as the pixels it gathered, drawn part of the way to an even ladder of tones so
            // the darks stay dark and the lights light whatever the painting was like.
            bandValue = new float[tones];
            for (int k = 0; k < tones; k++)
            {
                float even = look.toneLow + (look.toneHigh - look.toneLow) * k / (tones - 1);
                float own = members[k] > 0 ? (float)(sum[k] / members[k]) : even;
                bandValue[k] = own + (even - own) * look.toneContrast;
            }
            return band;
        }

        /// <summary>
        /// Every pixel takes the tone most of its neighbours have, so a tone is never left as stray specks inside
        /// another: the flat patches come out clean.
        /// </summary>
        private static void Settle(byte[] band, bool[] solid, int work, int reach)
        {
            var settled = new byte[band.Length];
            var votes = new int[8];
            for (int y = 0; y < work; y++)
            {
                int y0 = Math.Max(0, y - reach), y1 = Math.Min(work - 1, y + reach);
                for (int x = 0; x < work; x++)
                {
                    int i = y * work + x;
                    if (!solid[i]) continue;
                    Array.Clear(votes, 0, votes.Length);
                    int x0 = Math.Max(0, x - reach), x1 = Math.Min(work - 1, x + reach);
                    for (int ny = y0; ny <= y1; ny++)
                        for (int nx = x0; nx <= x1; nx++)
                            if (solid[ny * work + nx]) votes[band[ny * work + nx]]++;
                    int most = band[i];
                    for (int k = 0; k < votes.Length; k++)
                        if (votes[k] > votes[most]) most = k;
                    settled[i] = (byte)most;
                }
            }
            Array.Copy(settled, band, band.Length);
        }

        // ---------------------------------------------------------------- colour

        private static void Colour(float[] r, float[] g, float[] b, bool[] solid, int count, in ComicIconLook look)
        {
            float[] anchors = PaletteHues(look.palette);
            for (int i = 0; i < count; i++)
            {
                if (!solid[i]) continue;
                ToHsv(r[i], g[i], b[i], out float h, out float s, out float v);
                float colourful = Smooth(0.08f, 0.3f, s);
                if (look.hueBand > 0f)
                {
                    // A set painted in one colour, worn by a class of another: hues near the set's own colour move over.
                    float away = Math.Abs(HueDelta(look.hueFrom, h));
                    float share = 1f - Smooth(look.hueBand * 0.6f, look.hueBand, away);
                    h = Wrap(h + HueDelta(look.hueFrom, look.hueTo) * share);
                }
                if (anchors.Length > 0 && look.palettePull > 0f)
                {
                    float nearest = 0f, least = float.MaxValue;
                    for (int k = 0; k < anchors.Length; k++)
                    {
                        float d = Math.Abs(HueDelta(h, anchors[k]));
                        if (d >= least) continue;
                        least = d;
                        nearest = anchors[k];
                    }
                    h = Wrap(h + HueDelta(h, nearest) * look.palettePull * colourful);
                }
                s = Math.Min(1f, s * look.saturation);
                FromHsv(h, s, v, out r[i], out g[i], out b[i]);
            }
        }

        private static float[] PaletteHues(uint[] palette)
        {
            if (palette == null) return new float[0];
            var hues = new float[palette.Length];
            int n = 0;
            foreach (uint colour in palette)
            {
                Unpack(colour, out float pr, out float pg, out float pb);
                ToHsv(pr, pg, pb, out float h, out float s, out float _);
                if (s > 0.2f) hues[n++] = h; // a grey has no hue to pull toward
            }
            Array.Resize(ref hues, n);
            return hues;
        }

        // ---------------------------------------------------------------- painting

        private static float[] Paint(float[] r, float[] g, float[] b, bool[] solid, bool[] inked, byte[] band, float[] bandValue,
            float[] outside, int work, int ss, in ComicIconLook look)
        {
            float outline = look.outlineWidth * ss, keyline = look.keylineWidth * ss, cell = Math.Max(2f, look.dotCell * ss);
            int shadow = (int)Math.Round(look.shadowOffset * ss);
            Unpack(look.ink, out float inkR, out float inkG, out float inkB);
            Unpack(look.paper, out float paperR, out float paperG, out float paperB);
            Unpack(look.shadeColor, out float shadeR, out float shadeG, out float shadeB);
            float radius2 = look.dotRadius * look.dotRadius;
            int tones = bandValue.Length;

            var big = new float[work * work * 4];
            for (int y = 0; y < work; y++)
            {
                for (int x = 0; x < work; x++)
                {
                    int i = y * work + x, o = i * 4;
                    if (solid[i])
                    {
                        float pr, pg, pb;
                        if (inked[i]) { pr = inkR; pg = inkG; pb = inkB; }
                        else
                        {
                            int k = band[i];
                            bool dotted = tones > 2 && k == 1;
                            bool dot = false;
                            if (dotted)
                            {
                                // Ben-Day dots on a 45 degree grid, the same grid the older icons use.
                                float u = (x + y) / (cell * 1.41421f), v = (x - y) / (cell * 1.41421f);
                                float fu = u - (float)Math.Floor(u) - 0.5f, fv = v - (float)Math.Floor(v) - 0.5f;
                                dot = fu * fu + fv * fv < radius2;
                            }
                            // A dotted tone is drawn as dots of a darker tone over a lighter one; how far apart the
                            // two stand is the dots' contrast (1 = the darkest tone over the next lighter).
                            float value = !dotted ? bandValue[k]
                                : bandValue[1] + (bandValue[dot ? 0 : 2] - bandValue[1]) * look.dotContrast;
                            ToValue(r[i], g[i], b[i], value, out pr, out pg, out pb);
                            float tint = k == 0 ? look.shadeTint : dotted && dot ? look.shadeTint * look.dotContrast : 0f;
                            if (tint > 0f)
                            {
                                // The deepest tone leans toward the comic shadow colour, as the world's shadows do.
                                float scale = value / Math.Max(0.004f, Value(shadeR, shadeG, shadeB));
                                pr += (shadeR * scale - pr) * tint; pg += (shadeG * scale - pg) * tint; pb += (shadeB * scale - pb) * tint;
                            }
                        }
                        big[o] = Clamp01(pr); big[o + 1] = Clamp01(pg); big[o + 2] = Clamp01(pb); big[o + 3] = 1f;
                    }
                    else if (outside[i] <= outline)
                    {
                        big[o] = inkR; big[o + 1] = inkG; big[o + 2] = inkB; big[o + 3] = 1f;
                    }
                    else if (outside[i] <= outline + keyline + 0.5f)
                    {
                        // Paper keyline outside the ink, like a cut-out sticker: the HUD is dark, so the ink line
                        // alone would disappear into it.
                        big[o] = paperR; big[o + 1] = paperG; big[o + 2] = paperB;
                        big[o + 3] = Clamp01(outline + keyline + 0.5f - outside[i]);
                    }
                    else
                    {
                        // Hard drop shadow: the outlined shape again, pushed down and to the right.
                        int sx = x - shadow, sy = y - shadow;
                        if (sx >= 0 && sy >= 0 && outside[sy * work + sx] <= outline + keyline)
                        {
                            big[o] = inkR; big[o + 1] = inkG; big[o + 2] = inkB; big[o + 3] = look.shadowAlpha;
                        }
                    }
                }
            }
            return big;
        }

        /// <summary>The same colour at another brightness: its hue and its strength of colour are kept.</summary>
        private static void ToValue(float r, float g, float b, float value, out float pr, out float pg, out float pb)
        {
            float scale = value / Math.Max(0.004f, Value(r, g, b));
            pr = r * scale; pg = g * scale; pb = b * scale;
        }

        /// <summary>Chamfer distance, in working pixels, from every pixel to the nearest pixel that is set.</summary>
        private static float[] Distance(bool[] set, int work)
        {
            const float Far = 1e6f, Straight = 1f, Diagonal = 1.41421f;
            var d = new float[set.Length];
            for (int i = 0; i < d.Length; i++) d[i] = set[i] ? 0f : Far;
            for (int y = 0; y < work; y++)
            {
                for (int x = 0; x < work; x++)
                {
                    int i = y * work + x;
                    float v = d[i];
                    if (x > 0) v = Math.Min(v, d[i - 1] + Straight);
                    if (y > 0)
                    {
                        v = Math.Min(v, d[i - work] + Straight);
                        if (x > 0) v = Math.Min(v, d[i - work - 1] + Diagonal);
                        if (x < work - 1) v = Math.Min(v, d[i - work + 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            for (int y = work - 1; y >= 0; y--)
            {
                for (int x = work - 1; x >= 0; x--)
                {
                    int i = y * work + x;
                    float v = d[i];
                    if (x < work - 1) v = Math.Min(v, d[i + 1] + Straight);
                    if (y < work - 1)
                    {
                        v = Math.Min(v, d[i + work] + Straight);
                        if (x < work - 1) v = Math.Min(v, d[i + work + 1] + Diagonal);
                        if (x > 0) v = Math.Min(v, d[i + work - 1] + Diagonal);
                    }
                    d[i] = v;
                }
            }
            return d;
        }

        /// <summary>Box filter down to the icon's size, weighted by alpha so clear pixels do not darken the edges.</summary>
        private static byte[] Downsample(float[] big, int work, int ss)
        {
            int size = work / ss;
            var small = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    for (int sy = 0; sy < ss; sy++)
                    {
                        for (int sx = 0; sx < ss; sx++)
                        {
                            int o = ((y * ss + sy) * work + x * ss + sx) * 4;
                            float alpha = big[o + 3];
                            r += big[o] * alpha; g += big[o + 1] * alpha; b += big[o + 2] * alpha; a += alpha;
                        }
                    }
                    int at = (y * size + x) * 4;
                    if (a <= 0f) continue;
                    small[at] = ToByte(r / a); small[at + 1] = ToByte(g / a); small[at + 2] = ToByte(b / a);
                    small[at + 3] = ToByte(a / (ss * ss));
                }
            }
            return small;
        }

        // ---------------------------------------------------------------- colour sums

        public static float Luma(float r, float g, float b) => 0.2126f * r + 0.7152f * g + 0.0722f * b;
        /// <summary>How bright a colour is by its strongest channel: a pure red is as bright as white.</summary>
        public static float Value(float r, float g, float b) => Math.Max(r, Math.Max(g, b));

        private static byte ToByte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v * 255f)));
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        private static float Wrap(float hue) => hue - 360f * (float)Math.Floor(hue / 360f);

        private static float Smooth(float from, float to, float v)
        {
            float t = Clamp01((v - from) / Math.Max(0.0001f, to - from));
            return t * t * (3f - 2f * t);
        }

        /// <summary>The shortest way round the colour wheel from one hue to another, in degrees (-180 to 180).</summary>
        public static float HueDelta(float from, float to)
        {
            float d = Wrap(to - from);
            return d > 180f ? d - 360f : d;
        }

        public static void Unpack(uint rgb, out float r, out float g, out float b)
        {
            r = ((rgb >> 16) & 0xFF) / 255f; g = ((rgb >> 8) & 0xFF) / 255f; b = (rgb & 0xFF) / 255f;
        }

        public static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float range = max - min;
            v = max;
            s = max > 0.0001f ? range / max : 0f;
            if (range < 0.0001f) { h = 0f; return; }
            if (max == r) h = 60f * ((g - b) / range);
            else if (max == g) h = 60f * ((b - r) / range + 2f);
            else h = 60f * ((r - g) / range + 4f);
            h = Wrap(h);
        }

        public static void FromHsv(float h, float s, float v, out float r, out float g, out float b)
        {
            float c = v * s, hp = Wrap(h) / 60f;
            float x = c * (1f - Math.Abs(hp % 2f - 1f));
            float m = v - c;
            switch ((int)hp)
            {
                case 0: r = c; g = x; b = 0f; break;
                case 1: r = x; g = c; b = 0f; break;
                case 2: r = 0f; g = c; b = x; break;
                case 3: r = 0f; g = x; b = c; break;
                case 4: r = x; g = 0f; b = c; break;
                default: r = c; g = 0f; b = x; break;
            }
            r += m; g += m; b += m;
        }
    }
}
