using UnityEngine;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The Mini Deck's beat cue (build 17, day list 16), kept apart so it can be tested. The arc runs over the top two-thirds of
    /// the device's rim: from two-thirds down the left side, round the top-left corner, along the top, round the top-right
    /// corner and down to two-thirds of the right side. Two ticks set out from its ends just after a beat and meet at the notch
    /// (its middle, the top centre) on the next one; the notch flashes on the beat.
    /// </summary>
    public static class MiniDeckBeat
    {
        /// <summary>How far each tick has come, 0 at its end of the arc just after a beat, 1 at the notch on the next.</summary>
        public static float TickTravel(float phase01) => Mathf.Clamp01(phase01);

        /// <summary>The notch's flash, 1 on the beat and gone <paramref name="flashSeconds"/> either side of it.</summary>
        public static float NotchFlash(float phase01, float beatSeconds, float flashSeconds)
        {
            if (beatSeconds <= 0f || flashSeconds <= 0f) return 0f;
            float p = Mathf.Repeat(phase01, 1f);
            float off = Mathf.Min(p, 1f - p) * beatSeconds;
            return Mathf.Clamp01(1f - off / flashSeconds);
        }

        /// <summary>The arc's length on a <paramref name="width"/> x <paramref name="height"/> rim with corners of <paramref name="radius"/>.</summary>
        public static float Length(float width, float height, float radius)
        {
            Clamp(width, height, ref radius, out float side);
            return 2f * side + Mathf.PI * radius + Mathf.Max(0f, width - 2f * radius);
        }

        /// <summary>
        /// A point on the arc, <paramref name="t"/> 0 at its left end, 0.5 at the notch, 1 at its right end; in the device's own
        /// space (0,0 top left, y down).
        /// </summary>
        public static Vector2 Point(float t, float width, float height, float radius)
        {
            Clamp(width, height, ref radius, out float side);
            float top = Mathf.Max(0f, width - 2f * radius), corner = Mathf.PI * radius * 0.5f;
            float total = 2f * side + 2f * corner + top;
            float s = Mathf.Clamp01(t) * total;
            float sideTop = radius;                          // where a side meets its corner
            if (s <= side) return new Vector2(0f, sideTop + side - s);
            s -= side;
            if (s <= corner) return Corner(new Vector2(radius, radius), radius, Mathf.PI, s / Mathf.Max(radius, 1e-5f));
            s -= corner;
            if (s <= top) return new Vector2(radius + s, 0f);
            s -= top;
            if (s <= corner) return Corner(new Vector2(width - radius, radius), radius, 1.5f * Mathf.PI, s / Mathf.Max(radius, 1e-5f));
            s -= corner;
            return new Vector2(width, sideTop + Mathf.Min(s, side));
        }

        // a quarter turn clockwise on screen (y down) from angle a0
        private static Vector2 Corner(Vector2 centre, float radius, float a0, float turned)
        {
            float a = a0 + turned;
            return centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        // The straight part of a side, from two-thirds down to the corner.
        private static void Clamp(float width, float height, ref float radius, out float side)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(width, height) * 0.5f);
            side = Mathf.Max(0f, height * 2f / 3f - radius);
        }
    }
}
