using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Crowding by body size (owner pick A, 7 Nov 2026; pure, Burst). Each Hostile fighting a player stands at the player's radius + its own
    /// body radius + a gap, so a big one stands farther out by itself, and takes an arc of the ring as wide as its body seen from the
    /// player (a Bruiser's arc counts <c>bruiserFactor</c> times). Places are given while the arcs fit in <c>fill</c> of the circle; the
    /// strike limit (attack budget) is separate. Any future size works untuned: only radii go in.
    /// </summary>
    public static class HostileRingRules
    {
        /// <summary>Where a Hostile of body radius <paramref name="bodyRadius"/> stands from the player's centre, no further than it reaches.</summary>
        public static float Stand(float playerRadius, float bodyRadius, float gap, float reach) =>
            math.max(0.05f, math.min(playerRadius + math.max(0f, bodyRadius) + gap, reach));

        /// <summary>
        /// The arc (radians) a Hostile of body radius <paramref name="bodyRadius"/> takes at distance <paramref name="stand"/>: its body as
        /// seen from the player's centre, times <paramref name="factor"/> (a Bruiser's own tier); never more than the whole circle.
        /// </summary>
        public static float Arc(float bodyRadius, float stand, float factor) =>
            math.min(2f * math.PI, 2f * math.asin(math.saturate(math.max(0f, bodyRadius) / math.max(1e-3f, stand))) * math.max(1f, factor));

        /// <summary>The arc room of a player's ring: <paramref name="fill"/> of the circle (0-1).</summary>
        public static float Room(float fill) => 2f * math.PI * math.saturate(fill);

        /// <summary>
        /// Lays the holders round the ring: <paramref name="bearings"/> (radians, sorted ascending, each in [-pi, pi)) are where each stands
        /// now, <paramref name="arcs"/> their arcs (summing to at most the whole circle). Each place is the nearest to its own bearing that
        /// keeps every pair of neighbours at least half their two arcs apart, all the way round; written to <paramref name="places"/>.
        /// </summary>
        public static void Layout(NativeArray<float> bearings, NativeArray<float> arcs, NativeArray<float> places)
        {
            int n = bearings.Length;
            if (n == 0) return;
            float total = 0f;
            for (int i = 0; i < n; i++) total += arcs[i];
            places[0] = bearings[0];
            float after = total - arcs[0];   // the arcs still to place after the current one
            for (int i = 1; i < n; i++)
            {
                after -= arcs[i];
                float earliest = places[i - 1] + 0.5f * (arcs[i - 1] + arcs[i]);
                // the latest place that still leaves room for the rest before the first one comes round again
                float latest = places[0] + 2f * math.PI - 0.5f * arcs[0] - after - 0.5f * arcs[i];
                places[i] = math.clamp(bearings[i], earliest, math.max(earliest, latest));
            }
        }

        /// <summary>Bearing of <paramref name="at"/> round <paramref name="centre"/> (radians, 0 = +z, as the brain's yaw).</summary>
        public static float Bearing(float3 centre, float3 at)
        {
            float2 d = at.xz - centre.xz;
            return math.lengthsq(d) > 1e-8f ? math.atan2(d.x, d.y) : 0f;
        }
    }
}
