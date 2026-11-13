using System.Collections.Generic;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Where the brain soak puts its synthetic Hostiles (wave 4, 12 Nov 2026; pure, tested): a square grid of <see cref="Columns"/> per row
    /// at <c>spacing</c> metres, centred on a planned spot (owner rule: tests plan their spots), every point with its species slot and
    /// whether it attacks on sight (the first <c>aggressiveShare</c> of each pair of points). The EditMode soak
    /// (<c>HostileSoakTimingTests</c>) and the test-lane server switch <c>-knbrainsoak</c> both use it, so the Editor's and the player's numbers
    /// are the same test. Default: 45 x 45 at 5 m (a 225 m square), half aggressive.
    /// </summary>
    public static class SoakLayout
    {
        public const int Columns = 45;
        public const float DefaultSpacing = 5f;
        public const float DefaultAggressiveShare = 0.5f;

        public struct Point
        {
            public float3 Position;    // y = 0: the caller puts it on the ground
            public int Species;        // 0 .. speciesCount - 1
            public bool Aggressive;
        }

        /// <summary>The side of the square <paramref name="count"/> points need (metres, from the first column to the last).</summary>
        public static float Side(int count, float spacing = DefaultSpacing) =>
            (math.min(math.max(count, 1), Columns) - 1) * spacing;

        /// <summary><paramref name="count"/> points around <paramref name="centre"/> (x, z).</summary>
        public static List<Point> Build(float2 centre, int count, float spacing = DefaultSpacing, int speciesCount = 1, float aggressiveShare = DefaultAggressiveShare)
        {
            var points = new List<Point>(math.max(0, count));
            if (count <= 0) return points;
            int rows = (count + Columns - 1) / Columns;
            float half = Side(count, spacing) * 0.5f;
            float halfRows = (rows - 1) * spacing * 0.5f;
            int species = math.max(1, speciesCount);
            for (int i = 0; i < count; i++)
            {
                float x = centre.x - half + (i % Columns) * spacing;
                float z = centre.y - halfRows + (i / Columns) * spacing;
                points.Add(new Point
                {
                    Position = new float3(x, 0f, z),
                    Species = i % species,
                    // aggressiveShare 0.5 = every other point, as the EditMode soak always had (i % 2 == 0 attacks on sight)
                    Aggressive = aggressiveShare == DefaultAggressiveShare ? i % 2 == 0 : (i % 100) < (int)math.round(aggressiveShare * 100f),
                });
            }
            return points;
        }
    }
}
