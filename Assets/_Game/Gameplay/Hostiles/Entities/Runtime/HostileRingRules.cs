using Unity.Collections;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Crowding by body size (owner pick A, 7 Nov 2026) with sticky spots (owner pick, 15 Nov 2026; pure, Burst). Each Hostile fighting a
    /// player has its own spot on the player's ring: an angle it keeps for the whole fight. It strikes from the player's radius + its own
    /// body radius + a gap (a big body stands farther out by itself, never inside the player), and waits for its turn a step farther out.
    /// Its arc of the ring is as wide as its body seen from the player (a Bruiser's arc counts <c>bruiserFactor</c> times); places are
    /// given while the arcs fit in <c>fill</c> of the circle, and the strike limit (attack budget) is separate. A newcomer takes the free
    /// spot nearest where it stands; the others move only when a newcomer would overlap them, by the smallest push both ways. Any future
    /// size works untuned: only radii go in.
    /// </summary>
    public static class HostileRingRules
    {
        const float Eps = 1e-5f;

        /// <summary>Where a Hostile of body radius <paramref name="bodyRadius"/> strikes from: the player's centre to its centre, bodies a gap apart.</summary>
        public static float Stand(float playerRadius, float bodyRadius, float gap) =>
            math.max(0.05f, playerRadius + math.max(0f, bodyRadius) + math.max(0f, gap));

        /// <summary>
        /// The reach a Hostile measures from its centre: its attack range, and never less than its strike spot (<paramref name="stand"/>),
        /// so a body bigger than its range still reaches from where it touches instead of being pulled into the player.
        /// </summary>
        public static float Reach(float attackRange, float stand) => math.max(attackRange, stand);

        /// <summary>
        /// The arc (radians) a Hostile of body radius <paramref name="bodyRadius"/> takes at distance <paramref name="stand"/>: its body as
        /// seen from the player's centre, times <paramref name="factor"/> (a Bruiser's own tier); never more than the whole circle.
        /// </summary>
        public static float Arc(float bodyRadius, float stand, float factor) =>
            math.min(2f * math.PI, 2f * math.asin(math.saturate(math.max(0f, bodyRadius) / math.max(1e-3f, stand))) * math.max(1f, factor));

        /// <summary>The arc room of a player's ring: <paramref name="fill"/> of the circle (0-1).</summary>
        public static float Room(float fill) => 2f * math.PI * math.saturate(fill);

        /// <summary>An angle in [-pi, pi).</summary>
        public static float Wrap(float a) => a - 2f * math.PI * math.floor((a + math.PI) / (2f * math.PI));

        /// <summary>
        /// The spot nearest <paramref name="bearing"/> where an arc of <paramref name="arc"/> fits beside the first <paramref name="count"/>
        /// spots already taken (<paramref name="angles"/>, <paramref name="arcs"/>, any order) without overlapping any; false when no gap
        /// is wide enough (<paramref name="spot"/> is then the bearing, and <see cref="Spread"/> makes the room).
        /// </summary>
        public static bool FreeSpot(NativeArray<float> angles, NativeArray<float> arcs, int count, float arc, float bearing, out float spot)
        {
            spot = Wrap(bearing);
            if (Free(angles, arcs, count, arc, spot, -1)) return true;
            bool found = false;
            float best = float.MaxValue;
            for (int j = 0; j < count; j++)
            {
                float half = 0.5f * (arcs[j] + arc);
                for (int side = -1; side <= 1; side += 2)
                {
                    float candidate = Wrap(angles[j] + side * half);
                    if (!Free(angles, arcs, count, arc, candidate, j)) continue;
                    float off = math.abs(Wrap(candidate - bearing));
                    if (off < best) { best = off; spot = candidate; found = true; }
                }
            }
            if (!found) spot = Wrap(bearing);
            return found;
        }

        static bool Free(NativeArray<float> angles, NativeArray<float> arcs, int count, float arc, float at, int edgeOf)
        {
            for (int j = 0; j < count; j++)
            {
                float half = 0.5f * (arcs[j] + arc);
                float off = math.abs(Wrap(at - angles[j]));
                if (off < half - (j == edgeOf ? 1e-3f : Eps)) return false;
            }
            return true;
        }

        /// <summary>
        /// Clears any overlap round the ring by the smallest move both ways: every pair of neighbours that overlaps is pushed apart, each
        /// by half, until no two do (the ring's order and its mean angle are kept, so it never turns). Spots that do not overlap are never
        /// moved. The first <paramref name="count"/> <paramref name="angles"/> (any order, radians) are rewritten in place, wrapped to [-pi, pi).
        /// </summary>
        public static void Spread(NativeArray<float> angles, NativeArray<float> arcs, int count)
        {
            if (count < 2) return;
            var order = new NativeArray<int>(count, Allocator.Temp);
            var a = new NativeArray<float>(count, Allocator.Temp);
            var w = new NativeArray<float>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)   // a handful per ring: insertion sort by angle
            {
                int o = order[i];
                float key = Wrap(angles[o]);
                int k = i - 1;
                while (k >= 0 && Wrap(angles[order[k]]) > key) { order[k + 1] = order[k]; k--; }
                order[k + 1] = o;
            }
            for (int k = 0; k < count; k++) { a[k] = Wrap(angles[order[k]]); w[k] = arcs[order[k]]; }
            int rounds = 16 + 8 * count;
            for (int r = 0; r < rounds; r++)
            {
                float worst = 0f;
                for (int k = 0; k < count; k++)
                {
                    int next = k + 1 == count ? 0 : k + 1;
                    float there = next == 0 ? a[0] + 2f * math.PI : a[next];
                    float over = 0.5f * (w[k] + w[next]) - (there - a[k]);
                    if (over <= Eps) continue;
                    a[k] -= 0.5f * over;
                    a[next] += 0.5f * over;
                    worst = math.max(worst, over);
                }
                if (worst <= Eps) break;
            }
            for (int k = 0; k < count; k++) angles[order[k]] = Wrap(a[k]);
            order.Dispose();
            a.Dispose();
            w.Dispose();
        }

        /// <summary>The point <paramref name="distance"/> out from <paramref name="feet"/> at <paramref name="angle"/>, at height <paramref name="y"/>.</summary>
        public static float3 Spot(float3 feet, float angle, float distance, float y) =>
            new float3(feet.x + math.sin(angle) * distance, y, feet.z + math.cos(angle) * distance);

        /// <summary>Bearing of <paramref name="at"/> round <paramref name="centre"/> (radians, 0 = +z, as the brain's yaw).</summary>
        public static float Bearing(float3 centre, float3 at)
        {
            float2 d = at.xz - centre.xz;
            return math.lengthsq(d) > 1e-8f ? math.atan2(d.x, d.y) : 0f;
        }
    }
}
