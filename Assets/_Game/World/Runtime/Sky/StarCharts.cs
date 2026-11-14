using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The eight constellations of Nova's sky (owner 14 Nov 2026: names approved as drafted; "dont have constalations right next to
    /// each other"). Each is a few polylines in local degrees around its centre; the centres sit 45 degrees apart in right ascension, one
    /// per night of the moon cycle, at alternating declinations so neighbours never touch (<see cref="MinGapDegrees"/>, tested).
    /// Pure data and maths: the star-map baker draws from it, <see cref="SkyCalendar"/> uses the same frame to turn the dome.
    /// </summary>
    public static class StarCharts
    {
        /// <summary>Least empty sky between the outlines of two constellations, in degrees (tested by <c>SkyCalendarTests</c>).</summary>
        public const float MinGapDegrees = 12f;

        public struct Constellation
        {
            public string Name;
            /// <summary>Who names it that way (the Lore Bible's factions or folk).</summary>
            public string NamedBy;
            /// <summary>Right ascension of its centre in degrees: night <c>i</c> of the moon cycle has constellation <c>i</c> overhead at midnight.</summary>
            public float RightAscension;
            /// <summary>Declination of its centre, relative to the pole's elevation (0 = passes through the zenith).</summary>
            public float DeclinationOffset;
            /// <summary>Width and height of the shape in degrees.</summary>
            public float Size;
            /// <summary>Polylines in shape units: x right (east), y up, both 0-1.</summary>
            public Vector2[][] Lines;

            /// <summary>Radius of the circle that holds the whole shape, in degrees.</summary>
            public float Radius
            {
                get
                {
                    float r = 0f;
                    foreach (var line in Lines)
                        foreach (var p in line)
                            r = Mathf.Max(r, ((p - new Vector2(0.5f, 0.5f)) * Size).magnitude);
                    return r;
                }
            }
        }

        static Vector2 P(float x, float y) => new Vector2(x, 1f - y);   // drawn top-down like the research page sheet

        static Vector2[] Ring(int n, float rx, float ry, bool close)
        {
            var pts = new Vector2[close ? n + 1 : n];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = (i % n) * Mathf.PI * 2f / n;
                pts[i] = P(0.5f + rx * Mathf.Cos(a), 0.5f + ry * Mathf.Sin(a));
            }
            return pts;
        }

        static Vector2[] Coil()
        {
            var pts = new Vector2[13];
            for (int i = 0; i < pts.Length; i++)
            {
                float u = i / 12f;
                float a = u * 3.2f * Mathf.PI + 1.2f;
                pts[i] = P(0.5f + (0.08f + 0.34f * u) * Mathf.Cos(a), 0.52f + (0.08f + 0.36f * u) * Mathf.Sin(a));
            }
            return pts;
        }

        static Vector2[][] Spokes()
        {
            var ring = Ring(8, 0.34f, 0.38f, true);
            var lines = new Vector2[5][];
            lines[0] = ring;
            for (int i = 0; i < 4; i++) lines[i + 1] = new[] { P(0.5f, 0.5f), ring[i * 2] };
            return lines;
        }

        /// <summary>The eight, in the order of the moon cycle's nights.</summary>
        public static readonly Constellation[] All =
        {
            new Constellation { Name = "The Returning", NamedBy = "Covenant", RightAscension = 0f, DeclinationOffset = 4f, Size = 20f, Lines = new[]
            {
                new[] { P(.18f,.12f), P(.30f,.24f), P(.26f,.40f), P(.40f,.52f), P(.52f,.62f), P(.48f,.76f) },
                new[] { P(.48f,.76f), P(.34f,.88f) }, new[] { P(.48f,.76f), P(.62f,.88f) }, new[] { P(.48f,.76f), P(.80f,.84f) },
            } },
            new Constellation { Name = "The Spire", NamedBy = "Hegemony", RightAscension = 45f, DeclinationOffset = -10f, Size = 20f, Lines = new[]
            {
                new[] { P(.50f,.08f), P(.50f,.30f), P(.50f,.55f), P(.50f,.88f) },
                new[] { P(.36f,.30f), P(.64f,.30f) }, new[] { P(.30f,.55f), P(.70f,.55f) }, new[] { P(.22f,.88f), P(.78f,.88f) },
            } },
            new Constellation { Name = "The Gearwheel", NamedBy = "Null", RightAscension = 90f, DeclinationOffset = 8f, Size = 18f, Lines = Spokes() },
            new Constellation { Name = "The Longship", NamedBy = "Covenant (Aldhavn)", RightAscension = 135f, DeclinationOffset = -6f, Size = 21f, Lines = new[]
            {
                new[] { P(.12f,.55f), P(.24f,.74f), P(.50f,.80f), P(.76f,.74f), P(.90f,.50f) },
                new[] { P(.12f,.55f), P(.06f,.42f) }, new[] { P(.50f,.80f), P(.50f,.14f) }, new[] { P(.50f,.14f), P(.74f,.46f) },
                new[] { P(.50f,.30f), P(.30f,.48f) },
            } },
            new Constellation { Name = "The Waking Hand", NamedBy = "all", RightAscension = 180f, DeclinationOffset = 6f, Size = 20f, Lines = new[]
            {
                new[] { P(.42f,.94f), P(.36f,.64f), P(.66f,.62f), P(.58f,.94f) },
                new[] { P(.38f,.64f), P(.34f,.36f), P(.31f,.12f) }, new[] { P(.48f,.63f), P(.48f,.32f), P(.48f,.06f) },
                new[] { P(.58f,.62f), P(.61f,.33f), P(.63f,.10f) }, new[] { P(.66f,.62f), P(.74f,.42f), P(.79f,.25f) },
                new[] { P(.37f,.74f), P(.22f,.60f), P(.13f,.46f) },
            } },
            new Constellation { Name = "The Rent", NamedBy = "soldiers' saying", RightAscension = 225f, DeclinationOffset = -9f, Size = 22f, Lines = new[]
            {
                new[] { P(.06f,.30f), P(.24f,.52f), P(.34f,.28f), P(.50f,.62f), P(.62f,.36f), P(.78f,.70f), P(.94f,.52f) },
            } },
            new Constellation { Name = "The Truce Scales", NamedBy = "Concord", RightAscension = 270f, DeclinationOffset = 7f, Size = 20f, Lines = new[]
            {
                new[] { P(.50f,.10f), P(.50f,.86f) }, new[] { P(.14f,.28f), P(.86f,.28f) },
                new[] { P(.14f,.28f), P(.06f,.56f), P(.24f,.56f), P(.14f,.28f) }, new[] { P(.86f,.28f), P(.76f,.56f), P(.94f,.56f), P(.86f,.28f) },
                new[] { P(.34f,.86f), P(.66f,.86f) },
            } },
            new Constellation { Name = "The Sleeper", NamedBy = "miners' folklore", RightAscension = 315f, DeclinationOffset = -7f, Size = 20f, Lines = new[] { Coil() } },
        };

        /// <summary>A direction on the dome's own frame (right ascension and declination in degrees, before any turn).</summary>
        public static Vector3 DomeDir(float raDeg, float decDeg)
        {
            float ra = raDeg * Mathf.Deg2Rad, dec = decDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(dec) * Mathf.Cos(ra), Mathf.Cos(dec) * Mathf.Sin(ra), Mathf.Sin(dec));
        }

        /// <summary>Right ascension and declination (degrees) of a point of a shape (shape units 0-1).</summary>
        public static Vector2 RaDec(in Constellation c, Vector2 p, float poleElevation)
        {
            float dec = poleElevation + c.DeclinationOffset + (p.y - 0.5f) * c.Size;
            float ra = c.RightAscension + (p.x - 0.5f) * c.Size / Mathf.Max(0.2f, Mathf.Cos(dec * Mathf.Deg2Rad));
            return new Vector2(ra, dec);
        }

        /// <summary>Degrees between two dome directions given as (ra, dec).</summary>
        public static float Separation(Vector2 a, Vector2 b) => Vector3.Angle(DomeDir(a.x, a.y), DomeDir(b.x, b.y));

        /// <summary>The smallest empty gap (degrees) between any two constellations: centre distance minus both radii.</summary>
        public static float SmallestGap(float poleElevation, out int a, out int b)
        {
            float best = float.MaxValue; a = b = -1;
            for (int i = 0; i < All.Length; i++)
                for (int j = i + 1; j < All.Length; j++)
                {
                    var ci = new Vector2(All[i].RightAscension, poleElevation + All[i].DeclinationOffset);
                    var cj = new Vector2(All[j].RightAscension, poleElevation + All[j].DeclinationOffset);
                    float gap = Separation(ci, cj) - All[i].Radius - All[j].Radius;
                    if (gap < best) { best = gap; a = i; b = j; }
                }
            return best;
        }

        /// <summary>The constellation whose centre is nearest the given right ascension.</summary>
        public static int NearestTo(float raDeg)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < All.Length; i++)
            {
                float d = Mathf.Abs(Mathf.DeltaAngle(raDeg, All[i].RightAscension));
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
