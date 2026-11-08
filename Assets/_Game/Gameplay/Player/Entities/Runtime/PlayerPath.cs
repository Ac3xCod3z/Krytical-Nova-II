using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// The polyline a walk follows, in integer millimetres. Pure functions, Burst-compatible (no managed types, no statics).
    /// The first point of a walk is its start (<c>MoveState.Start*</c>); <c>corners[0]</c> from a planner is ignored and
    /// <c>corners[1..count-1]</c> are the rest of the points. Lengths use an exact integer square root, so the length of a
    /// path and the point at an arc length are the same on every machine.
    /// </summary>
    public static class PlayerPath
    {
        /// <summary>Floor of the square root, exact for every non-negative long (the float estimate is corrected).</summary>
        public static long ISqrt(long v)
        {
            if (v <= 0) return 0;
            long x = (long)math.sqrt((double)v);
            while (x * x > v) x--;
            while ((x + 1) * (x + 1) <= v) x++;
            return x;
        }

        /// <summary>Distance between two points in whole millimetres (rounded down).</summary>
        public static int DistanceMm(int3 a, int3 b)
        {
            long dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z;
            return (int)ISqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>What is left of a walk <paramref name="progressMm"/> along it (0 at or past the end).</summary>
        public static int LeftMm(int3 start, NativeArray<int3> corners, int count, int progressMm) =>
            math.max(0, LengthMm(start, corners, count) - math.max(0, progressMm));

        /// <summary>Total length of start, corners[1], corners[2] ... corners[count-1].</summary>
        public static int LengthMm(int3 start, NativeArray<int3> corners, int count)
        {
            long total = 0;
            int3 prev = start;
            int n = math.min(count, corners.Length);
            for (int i = 1; i < n; i++)
            {
                total += DistanceMm(prev, corners[i]);
                prev = corners[i];
            }
            return (int)math.min(total, (long)int.MaxValue);
        }

        /// <summary>
        /// The point <paramref name="progressMm"/> along the polyline and the direction of the segment it lies on (zero when the
        /// path has no length). A progress past the end gives the last point. Walks the corners, so several short segments
        /// crossed in one tick (a fast run) cannot overshoot a corner.
        /// </summary>
        public static void PointAt(int3 start, NativeArray<int3> corners, int count, int progressMm, out int3 pos, out int3 dir)
        {
            pos = start;
            dir = int3.zero;
            int n = math.min(count, corners.Length);
            long remaining = math.max(0, progressMm);
            int3 prev = start;
            for (int i = 1; i < n; i++)
            {
                int3 next = corners[i];
                long seg = DistanceMm(prev, next);
                if (seg > 0)
                {
                    dir = next - prev;
                    if (remaining <= seg)
                    {
                        long dx = next.x - prev.x, dy = next.y - prev.y, dz = next.z - prev.z;
                        pos = new int3(
                            (int)(prev.x + dx * remaining / seg),
                            (int)(prev.y + dy * remaining / seg),
                            (int)(prev.z + dz * remaining / seg));
                        return;
                    }
                    remaining -= seg;
                }
                prev = next;
                pos = next;
            }
        }

        /// <summary>
        /// Facing for a horizontal direction in hundredths of a degree, 0..35999 (0 = +Z, like <c>Quaternion.LookRotation</c>).
        /// <paramref name="fallback"/> when the direction has no horizontal part. A derived output: float math, never compared.
        /// </summary>
        public static int YawCdegOf(int3 dir, int fallback)
        {
            if (dir.x == 0 && dir.z == 0) return fallback;
            float deg = math.degrees(math.atan2((float)dir.x, (float)dir.z));
            int c = (int)math.round(deg * 100f) % 36000;
            return c < 0 ? c + 36000 : c;
        }
    }
}
